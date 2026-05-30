/// EXTENSION TO: VoicePro.Domain/ValueObjects/AudioSignal.cs
/// 
/// Add these methods to the AudioSignal class after the Resampling section
/// and before the Diagnostics section.

namespace VoicePro.Domain.ValueObjects;

/// <summary>
/// Extension methods for signal delay operations.
/// Based on DSP theory section 5.4.6: "Delays"
/// 
/// Core equation: delayed(n) = s(n - d)
/// where d is the delay in samples.
/// </summary>
public sealed partial class AudioSignal
{
    // ── Delay & Echo ──────────────────────────────────────────────────────────

    /// <summary>
    /// Delays the signal by <paramref name="delaySamples"/> samples.
    /// Implements: delayed(n) = s(n - d) from DSP theory.
    ///
    /// <para>
    /// From the textbook: a delay operation shifts the time coordinate of a signal.
    /// Delayed samples are filled with silence (zeros) at the beginning.
    /// This operation preserves the signal's amplitude and frequency content but
    /// shifts it forward in time — the foundation of filters, echo, and reverb.
    /// </para>
    ///
    /// <para>
    /// Use cases:
    /// — Aligning signals from different sources (latency compensation)
    /// — Building echo and reverb effects
    /// — Creating multi-tap delay filters
    /// — Phase alignment in mixing
    /// </para>
    /// </summary>
    /// <param name="delaySamples">Number of samples to delay (≥ 0).</param>
    /// <returns>A delayed signal with length increased by <paramref name="delaySamples"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="delaySamples"/> is negative.
    /// </exception>
    public AudioSignal Delay(int delaySamples)
    {
        if (delaySamples < 0)
            throw new ArgumentOutOfRangeException(
                nameof(delaySamples),
                "Delay must be non-negative.");

        if (delaySamples == 0)
            return this; // No-op

        int step = (int)Channels;
        int totalLength = Samples.Length + (delaySamples * step);
        var result = new float[totalLength];

        // First delaySamples×step positions are silence (zeros)
        // Remaining positions are filled with original signal
        var originalSpan = Samples.Span;
        Array.Copy(originalSpan.ToArray(), 0, result, delaySamples * step, originalSpan.Length);

        return new AudioSignal(result.AsMemory(), SampleRate, Channels);
    }

    /// <summary>
    /// Delays the signal by a time duration (in seconds).
    /// Convenience wrapper around <see cref="Delay(int)"/>.
    /// </summary>
    /// <param name="delaySeconds">Delay duration in seconds.</param>
    /// <returns>A delayed signal.</returns>
    public AudioSignal DelayByTime(double delaySeconds)
    {
        int samples = SampleRate.ToSampleCount(delaySeconds);
        return Delay(samples);
    }

    /// <summary>
    /// Creates a slap-back echo effect: Y = S + g·z^(-d)S
    /// where z^(-d) is the delay operator and g is the echo gain.
    ///
    /// <para>
    /// From the textbook (eq. 32): Y = S + 0.5z^(-d)S creates an echo by mixing
    /// the original signal with a delayed, attenuated copy.
    ///
    /// Example: Vocal with slap-back:
    /// <code>
    /// var delayed = vocal.SlapBackEcho(
    ///     delaySamples: (int)(0.2 * sampleRate),  // 200ms
    ///     echoGain: 0.4f);
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Parameters guide:
    /// — delaySamples: typically 0.1–0.5 seconds × sample rate
    /// — echoGain: 0.3–0.5 (higher = more prominent echo, but risk of clipping)
    /// </para>
    ///
    /// <para>
    /// Note: This is a single-tap delay. For multiple reflections or feedback,
    /// apply this method iteratively or implement a feedback loop.
    /// </para>
    /// </summary>
    /// <param name="delaySamples">Delay time in samples (the "d" in z^(-d)).</param>
    /// <param name="echoGain">Gain of the delayed signal (0.0 to 0.5 recommended).</param>
    /// <returns>Mixed signal: original + attenuated delayed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="delaySamples"/> is negative.
    /// </exception>
    public AudioSignal SlapBackEcho(int delaySamples, float echoGain = 0.5f)
    {
        if (delaySamples < 0)
            throw new ArgumentOutOfRangeException(
                nameof(delaySamples),
                "Delay must be non-negative.");

        if (delaySamples == 0)
        {
            // No delay: just amplify original
            return Scale(1f + echoGain);
        }

        // Step 1: Delay the signal and scale by echo gain
        var delayed = Delay(delaySamples).Scale(echoGain);

        // Step 2: Pad the original signal to match delayed length
        int step = (int)Channels;
        int totalLength = Samples.Length + (delaySamples * step);
        var paddedOriginal = new float[totalLength];
        Array.Copy(Samples.Span.ToArray(), paddedOriginal, Samples.Length);

        var originalPadded = new AudioSignal(paddedOriginal.AsMemory(), SampleRate, Channels);

        // Step 3: Mix original + delayed
        return originalPadded.MixWith(delayed);
    }

    /// <summary>
    /// Creates a slap-back echo using a time duration.
    /// Convenience wrapper around <see cref="SlapBackEcho(int, float)"/>.
    /// </summary>
    /// <param name="delaySeconds">Echo delay time in seconds.</param>
    /// <param name="echoGain">Echo amplitude factor (0.0 to 0.5 recommended).</param>
    /// <returns>Mixed signal with echo effect.</returns>
    public AudioSignal SlapBackEchoByTime(double delaySeconds, float echoGain = 0.5f)
    {
        int samples = SampleRate.ToSampleCount(delaySeconds);
        return SlapBackEcho(samples, echoGain);
    }

    /// <summary>
    /// Creates a feedback delay line: each echo feeds back into the input.
    /// Implements a sequence of decaying echoes.
    ///
    /// <para>
    /// Formula (iterative): y = s + g·z^(-d)s + g²·z^(-2d)s + ...
    /// where feedback &lt; 1 ensures convergence (no infinite buildup).
    /// </para>
    ///
    /// <para>
    /// Example: Ambient reverb tail
    /// <code>
    /// var reverb = signal.FeedbackDelay(
    ///     delayTime: 0.1,      // 100ms between echoes
    ///     feedback: 0.5f,      // Each echo is half the previous
    ///     iterations: 5);      // 5 repeating echoes
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="delaySeconds">Time between echoes in seconds.</param>
    /// <param name="feedback">Gain per iteration (0.0 to ~0.9; avoid ≥ 1.0).</param>
    /// <param name="iterations">Number of echo repeats.</param>
    /// <returns>Signal with feedback delay effect.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when feedback ≥ 1.0 or iterations &lt; 1.
    /// </exception>
    public AudioSignal FeedbackDelay(double delaySeconds, float feedback, int iterations)
    {
        if (feedback >= 1.0f)
            throw new ArgumentOutOfRangeException(
                nameof(feedback),
                "Feedback must be < 1.0 to avoid infinite buildup.");

        if (iterations < 1)
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                "Iterations must be at least 1.");

        int delaySamples = SampleRate.ToSampleCount(delaySeconds);
        var result = this;
        float currentGain = feedback;

        for (int i = 0; i < iterations; i++)
        {
            var delayed = Delay((i + 1) * delaySamples).Scale(currentGain);

            // Pad result to match delayed length
            int step = (int)Channels;
            int targetLength = (i + 2) * delaySamples * step;
            var paddedResult = new float[targetLength];
            Array.Copy(result.Samples.Span.ToArray(), paddedResult, result.Samples.Length);

            result = new AudioSignal(paddedResult.AsMemory(), SampleRate, Channels)
                .MixWith(delayed);

            currentGain *= feedback;
        }

        return result;
    }
}