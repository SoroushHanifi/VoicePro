using VoicePro.Domain.ValueObjects;

namespace VoicePro.Features.Filters;

/// <summary>
/// Contract for all discrete-time digital filters in VoicePro.
///
/// <para>
/// A filter transforms an input signal x(n) into an output signal y(n) by
/// applying its transfer function H(ω) in the frequency domain, which is
/// equivalent to convolution in the time domain.
/// </para>
///
/// <para>
/// Two concrete implementations exist:
/// — <see cref="FirFilter"/>: Finite Impulse Response (feedforward only)
/// — <see cref="IirFilter"/>: Infinite Impulse Response (with feedback)
/// </para>
/// </summary>
public interface IFilter
{
    /// <summary>
    /// Type of the filter (FIR or IIR).
    /// </summary>
    FilterType Type { get; }

    /// <summary>
    /// Applies the filter to a single audio signal, returning a new filtered signal.
    /// The original signal is not modified.
    /// </summary>
    /// <param name="signal">Input audio signal x(n).</param>
    /// <returns>Filtered output signal y(n).</returns>
    AudioSignal Apply(AudioSignal signal);

    /// <summary>
    /// Computes the amplitude response A(ω) at a given frequency in Hz.
    ///
    /// <para>
    /// For FIR:  A(ω) = |H(ω)| = |Σ aₖ · e^(-jωk)|
    /// For IIR:  A(ω) = |a / (1 + b·e^(-jω))|
    /// </para>
    /// </summary>
    /// <param name="frequencyHz">Frequency in Hz to evaluate.</param>
    /// <param name="sampleRate">Sample rate of the signal (needed to convert Hz → ω).</param>
    /// <returns>Amplitude response value (0.0 = fully attenuated, 1.0 = pass-through).</returns>
    float AmplitudeResponse(double frequencyHz, int sampleRate);

    /// <summary>
    /// Computes the phase response PH(ω) at a given frequency in Hz, in radians.
    ///
    /// <para>
    /// For a symmetric FIR, this is linear: PH(ω) = -ω·N/2,
    /// meaning all frequencies are delayed by N/2 samples.
    /// </para>
    /// </summary>
    /// <param name="frequencyHz">Frequency in Hz to evaluate.</param>
    /// <param name="sampleRate">Sample rate of the signal.</param>
    /// <returns>Phase offset in radians.</returns>
    float PhaseResponse(double frequencyHz, int sampleRate);

    /// <summary>
    /// Resets the internal delay state of the filter.
    /// Call this between processing independent audio segments to avoid
    /// state leaking from one segment to the next.
    /// </summary>
    void Reset();
}

/// <summary>
/// Identifies the class of a digital filter.
/// </summary>
public enum FilterType
{
    /// <summary>
    /// Finite Impulse Response — uses only input delays (feedforward).
    /// Always stable. Can have linear phase. Equation: y(n) = Σ aₖ·x(n-k)
    /// </summary>
    FIR,

    /// <summary>
    /// Infinite Impulse Response — uses output delays (feedback).
    /// More efficient for sharp roll-offs, but can be unstable if |b| ≥ 1.
    /// Equation: y(n) = a·x(n) - b·y(n-1)
    /// </summary>
    IIR
}