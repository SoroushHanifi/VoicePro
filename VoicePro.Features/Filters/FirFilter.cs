using VoicePro.Domain.ValueObjects;

namespace VoicePro.Features.Filters;

/// <summary>
/// First-order FIR (Finite Impulse Response) filter — feedforward only.
///
/// <para>
/// Filter equation (from DSP theory, equation 33 in the textbook):
///   y(n) = a₀·x(n) + a₁·x(n−1)
/// where a₀ and a₁ are the filter coefficients.
/// </para>
///
/// <para>
/// The special case a₀ = a₁ = 0.5 gives the averaging first-order filter:
///   y(n) = 0.5·x(n) + 0.5·x(n−1)
/// which is a low-pass filter with amplitude response: A(ω) = cos(ω/2).
/// </para>
///
/// <para>
/// Because FIR filters use no feedback (output delays), they are:
/// — Always stable (bounded input → bounded output)
/// — Linear phase when coefficients are symmetric (a₀ = a₁)
///   → all frequency components delayed by exactly N/2 = 0.5 samples
/// </para>
///
/// <para>
/// Transfer function: H(ω) = a₀ + a₁·e^(−jω)
/// Amplitude response: A(ω) = √(a₀² + 2·a₀·a₁·cos(ω) + a₁²)
/// Phase response:     PH(ω) = arctan(−a₁·sin(ω) / (a₀ + a₁·cos(ω)))
/// </para>
/// </summary>
public sealed class FirFilter : IFilter
{
    // ── Coefficients ─────────────────────────────────────────────────────────

    /// <summary>Coefficient for x(n) — the current input sample.</summary>
    public float A0 { get; }

    /// <summary>Coefficient for x(n−1) — the one-sample delayed input.</summary>
    public float A1 { get; }

    // ── Internal state ───────────────────────────────────────────────────────

    /// <summary>
    /// x(n−1): the previous input sample.
    /// This is the FIR "memory" — just one sample because it's first-order.
    /// </summary>
    private float _delayed;

    // ── IFilter ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public FilterType Type => FilterType.FIR;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a first-order FIR filter with explicit coefficients.
    /// </summary>
    /// <param name="a0">Coefficient for x(n).</param>
    /// <param name="a1">Coefficient for x(n−1).</param>
    public FirFilter(float a0, float a1)
    {
        A0 = a0;
        A1 = a1;
        _delayed = 0f;
    }

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the averaging first-order low-pass FIR filter from the textbook:
    ///   y(n) = 0.5·x(n) + 0.5·x(n−1)
    ///
    /// Amplitude response: A(ω) = cos(ω/2)  →  cos(π·f/sr) in Hz
    /// Phase response:     PH(ω) = −ω/2     →  linear, half-sample delay
    /// </summary>
    public static FirFilter LowPassAveraging() => new(0.5f, 0.5f);

    /// <summary>
    /// Creates a high-pass first-order FIR filter:
    ///   y(n) = 0.5·x(n) − 0.5·x(n−1)
    ///
    /// Setting a₁ to a negative value flips the amplitude response,
    /// attenuating low frequencies and passing high ones.
    /// </summary>
    public static FirFilter HighPass() => new(0.5f, -0.5f);

    /// <summary>
    /// Creates a custom FIR filter with any coefficients.
    /// For a normalized response, consider |a₀| + |a₁| ≤ 1.
    /// </summary>
    public static FirFilter Custom(float a0, float a1) => new(a0, a1);

    // ── Core processing ───────────────────────────────────────────────────────

    /// <summary>
    /// Applies the FIR filter to the audio signal, sample by sample.
    ///
    /// Implements: y(n) = a₀·x(n) + a₁·x(n−1)
    ///
    /// State (_delayed) persists across calls so that consecutive audio
    /// chunks are filtered continuously without boundary artifacts.
    /// Call <see cref="Reset"/> between independent audio segments.
    /// </summary>
    /// <param name="signal">Input audio signal x(n). Can be mono or stereo.</param>
    /// <returns>Filtered signal y(n) with the same sample rate and channel layout.</returns>
    public AudioSignal Apply(AudioSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var input = signal.Samples.Span;
        var output = new float[input.Length];

        for (int n = 0; n < input.Length; n++)
        {
            // y(n) = a₀·x(n) + a₁·x(n−1)
            output[n] = A0 * input[n] + A1 * _delayed;
            _delayed = input[n]; // store x(n) as next x(n−1)
        }

        return new AudioSignal(output.AsMemory(), signal.SampleRate, signal.Channels);
    }

    /// <summary>
    /// Computes the amplitude response A(ω) at a given frequency in Hz.
    ///
    /// From the transfer function H(ω) = a₀ + a₁·e^(−jω):
    ///   A(ω) = √(a₀² + 2·a₀·a₁·cos(ω) + a₁²)
    ///
    /// For the averaging filter (a₀ = a₁ = 0.5):
    ///   A(ω) = cos(ω/2)  — the first quarter of a cosine wave from 0 to sr/2.
    /// </summary>
    public float AmplitudeResponse(double frequencyHz, int sampleRate)
    {
        double omega = 2.0 * Math.PI * frequencyHz / sampleRate;
        // A(ω) = √(a₀² + 2·a₀·a₁·cos(ω) + a₁²)
        double a0 = A0, a1 = A1;
        double response = Math.Sqrt(a0 * a0 + 2.0 * a0 * a1 * Math.Cos(omega) + a1 * a1);
        return (float)response;
    }

    /// <summary>
    /// Computes the phase response PH(ω) at a given frequency in Hz, in radians.
    ///
    /// PH(ω) = arctan(−a₁·sin(ω) / (a₀ + a₁·cos(ω)))
    ///
    /// For the symmetric averaging filter (a₀ = a₁):
    ///   PH(ω) = −ω/2  — a linear function → constant group delay of 0.5 samples.
    /// </summary>
    public float PhaseResponse(double frequencyHz, int sampleRate)
    {
        double omega = 2.0 * Math.PI * frequencyHz / sampleRate;
        double real = A0 + A1 * Math.Cos(omega);
        double imag = -A1 * Math.Sin(omega);
        return (float)Math.Atan2(imag, real);
    }

    /// <inheritdoc/>
    public void Reset() => _delayed = 0f;

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the filter has symmetric coefficients (a₀ == a₁),
    /// which guarantees a linear phase response.
    /// </summary>
    public bool IsLinearPhase => MathF.Abs(A0 - A1) < float.Epsilon;

    public override string ToString() =>
        $"FirFilter {{ y(n) = {A0}·x(n) + {A1}·x(n-1), LinearPhase={IsLinearPhase} }}";
}