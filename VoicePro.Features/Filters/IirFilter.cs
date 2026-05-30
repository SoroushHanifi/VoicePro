using VoicePro.Domain.ValueObjects;

namespace VoicePro.Features.Filters;

/// <summary>
/// First-order IIR (Infinite Impulse Response) filter — feedback (recursive).
///
/// <para>
/// Filter equation (from DSP theory, equation 39 in the textbook):
///   y(n) = a·x(n) − b·y(n−1)
/// where:
///   a = feedforward gain (scales the input)
///   b = feedback coefficient (scales the previous output)
/// </para>
///
/// <para>
/// The transfer function is (equation 41):
///   H(ω) = a / (1 + b·e^(−jω))
///
/// Unlike FIR, the variable z⁻¹ is in the denominator, which creates a pole
/// at the frequency that makes (1 + b·e^(−jω)) = 0.
/// </para>
///
/// <para>
/// Stability constraint: |b| &lt; 1.
/// If |b| ≥ 1, the filter has a pole on or outside the unit circle → unstable.
/// A pole close to (but inside) the unit circle creates a sharp resonant peak.
/// </para>
///
/// <para>
/// Amplitude response (equation 42):
///   A(ω) = a / √(1 + b² + 2·b·cos(ω))
/// </para>
///
/// <para>
/// IIR filters are preferred in music/audio DSP when:
/// — Sharp frequency roll-offs are needed (fewer coefficients than FIR for same sharpness)
/// — Real-time processing with low latency (no linear-phase delay)
/// — Resonance effects (poles near unit circle boost specific frequencies)
/// </para>
/// </summary>
public sealed class IirFilter : IFilter
{
    // ── Coefficients ─────────────────────────────────────────────────────────

    /// <summary>
    /// Feedforward gain 'a': scales the input x(n).
    /// Controls the overall output level.
    /// </summary>
    public float A { get; }

    /// <summary>
    /// Feedback coefficient 'b': scales the previous output y(n−1).
    /// Must satisfy |b| &lt; 1 for stability.
    /// Values close to 1.0 create a sharp resonant low-pass filter.
    /// Values close to −1.0 create a sharp resonant high-pass filter.
    /// </summary>
    public float B { get; }

    // ── Internal state ────────────────────────────────────────────────────────

    /// <summary>
    /// y(n−1): the previous output sample.
    /// This is the IIR "memory" — the feedback that gives IIR its recursive nature.
    /// Because the output feeds back, the impulse response is theoretically infinite.
    /// </summary>
    private float _previousOutput;

    // ── IFilter ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public FilterType Type => FilterType.IIR;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a first-order IIR filter.
    /// </summary>
    /// <param name="a">Feedforward gain. Typically in (0, 1].</param>
    /// <param name="b">
    /// Feedback coefficient. Must satisfy |b| &lt; 1 for stability.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when |b| ≥ 1, which would make the filter unstable.
    /// </exception>
    public IirFilter(float a, float b)
    {
        if (MathF.Abs(b) >= 1f)
            throw new ArgumentException(
                $"Feedback coefficient |b| must be < 1 for stability, but got b = {b}. " +
                $"A pole on or outside the unit circle causes the filter output to grow without bound.",
                nameof(b));

        A = a;
        B = b;
        _previousOutput = 0f;
    }

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a gentle low-pass IIR filter.
    ///
    /// With b close to +1, the pole is near ω=0 (DC) — the filter has a strong
    /// preference for passing low frequencies. The closer b is to 1, the sharper
    /// the roll-off but the closer to instability.
    ///
    /// Default: a = 0.1, b = 0.9  →  gentle low-pass with slight resonance.
    /// </summary>
    public static IirFilter LowPass(float b = 0.9f)
    {
        if (b <= 0 || b >= 1f)
            throw new ArgumentOutOfRangeException(nameof(b), "b must be in (0, 1) for a low-pass IIR.");
        float a = 1f - b; // ensures DC gain = 1: A(0) = a/(1+b) * ... normalised
        return new IirFilter(a, b);
    }

    /// <summary>
    /// Creates a gentle high-pass IIR filter.
    ///
    /// With b close to −1, the pole is near ω=π (Nyquist) — the filter passes
    /// high frequencies and attenuates low ones.
    ///
    /// Default: a = 0.95, b = -0.95  →  gentle high-pass.
    /// </summary>
    public static IirFilter HighPass(float b = -0.9f)
    {
        if (b >= 0 || b <= -1f)
            throw new ArgumentOutOfRangeException(nameof(b), "b must be in (-1, 0) for a high-pass IIR.");
        float a = 1f + b; // DC blocking: as ω→0, output → 0
        return new IirFilter(MathF.Abs(a), b);
    }

    /// <summary>
    /// Creates a custom IIR filter with explicit coefficients.
    /// You are responsible for ensuring |b| &lt; 1.
    /// </summary>
    public static IirFilter Custom(float a, float b) => new(a, b);

    // ── Core processing ───────────────────────────────────────────────────────

    /// <summary>
    /// Applies the IIR filter to the audio signal, sample by sample.
    ///
    /// Implements: y(n) = a·x(n) − b·y(n−1)
    ///
    /// The previous output (_previousOutput) persists across calls for
    /// continuous processing. Call <see cref="Reset"/> between independent segments.
    /// </summary>
    /// <param name="signal">Input audio signal x(n).</param>
    /// <returns>Filtered signal y(n) with the same sample rate and channel layout.</returns>
    public AudioSignal Apply(AudioSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var input = signal.Samples.Span;
        var output = new float[input.Length];

        for (int n = 0; n < input.Length; n++)
        {
            // y(n) = a·x(n) − b·y(n−1)
            float yn = A * input[n] - B * _previousOutput;
            output[n] = yn;
            _previousOutput = yn; // feedback: this output becomes next y(n−1)
        }

        return new AudioSignal(output.AsMemory(), signal.SampleRate, signal.Channels);
    }

    /// <summary>
    /// Computes the amplitude response A(ω) at a given frequency in Hz.
    ///
    /// From the transfer function H(ω) = a / (1 + b·e^(−jω)):
    ///   A(ω) = a / √(1 + b² + 2·b·cos(ω))
    ///
    /// Note: the denominator can never be zero when |b| &lt; 1, so the filter
    /// is always bounded (stable). As b → 1, A(0) → a/(1−b) which grows large
    /// — the resonant peak effect used in music DSP for resonant filters.
    /// </summary>
    public float AmplitudeResponse(double frequencyHz, int sampleRate)
    {
        double omega = 2.0 * Math.PI * frequencyHz / sampleRate;
        // A(ω) = a / √(1 + b² + 2·b·cos(ω))
        double denom = Math.Sqrt(1.0 + B * B + 2.0 * B * Math.Cos(omega));
        return denom < double.Epsilon ? float.MaxValue : (float)(A / denom);
    }

    /// <summary>
    /// Computes the phase response PH(ω) at a given frequency in Hz, in radians.
    ///
    /// The phase of H(ω) = a / (1 + b·e^(−jω)):
    ///   PH(ω) = −arctan(b·sin(ω) / (1 + b·cos(ω)))
    ///
    /// Unlike FIR, IIR phase response is nonlinear — different frequencies get
    /// different time delays. This is a trade-off for the steeper roll-off.
    /// </summary>
    public float PhaseResponse(double frequencyHz, int sampleRate)
    {
        double omega = 2.0 * Math.PI * frequencyHz / sampleRate;
        // Phase of denominator: arctan(b·sin(ω) / (1 + b·cos(ω)))
        // Then negate because it's in the denominator
        double real = 1.0 + B * Math.Cos(omega);
        double imag = B * Math.Sin(omega);
        return (float)-Math.Atan2(imag, real);
    }

    /// <inheritdoc/>
    public void Reset() => _previousOutput = 0f;

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>
    /// DC gain of the filter: A(0) — how much the filter amplifies or attenuates
    /// a constant (zero-frequency) signal.
    /// For a low-pass IIR: DC gain = a / (1 + b).
    /// </summary>
    public float DcGain => AmplitudeResponse(0, 44100); // frequency-independent

    /// <summary>
    /// Returns true when this filter is stable (|b| &lt; 1).
    /// Always true because the constructor enforces stability.
    /// </summary>
    public bool IsStable => MathF.Abs(B) < 1f;

    public override string ToString() =>
        $"IirFilter {{ y(n) = {A}·x(n) - {B}·y(n-1), Stable={IsStable}, DcGain={DcGain:F3} }}";
}