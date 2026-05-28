using VoicePro.Domain.Exceptions;

namespace VoicePro.Domain.ValueObjects;

/// <summary>
/// Number of audio channels.
/// </summary>
public enum AudioChannels
{
    Mono = 1,
    Stereo = 2
}

/// <summary>
/// Immutable value object representing a digital audio signal — the f(n) of DSP theory.
/// Wraps a <see cref="ReadOnlyMemory{T}"/> of PCM samples so no copying occurs on access.
///
/// <para>
/// Mono signals store all samples in <see cref="Samples"/>.
/// Stereo signals interleave L/R: [L0, R0, L1, R1, ...].
/// Use <see cref="GetChannel"/> to extract a single channel as a new <see cref="AudioSignal"/>.
/// </para>
/// </summary>
public sealed class AudioSignal
{
    // ── Core data ────────────────────────────────────────────────────────────

    /// <summary>
    /// Raw PCM samples. For stereo this is interleaved [L, R, L, R, ...].
    /// For DSP algorithms always prefer <see cref="GetChannel"/> or <see cref="ToMono"/>.
    /// </summary>
    public ReadOnlyMemory<float> Samples { get; }

    /// <summary>Validated sample rate of this signal.</summary>
    public SampleRate SampleRate { get; }

    /// <summary>Channel layout of this signal.</summary>
    public AudioChannels Channels { get; }

    // ── Derived properties ───────────────────────────────────────────────────

    /// <summary>
    /// Number of frames (channel-independent samples).
    /// For stereo: Samples.Length / 2. For mono: Samples.Length.
    /// </summary>
    public int FrameCount => Samples.Length / (int)Channels;

    /// <summary>Duration of the signal in seconds.</summary>
    public double Duration => SampleRate.ToSeconds(FrameCount);

    /// <summary>Shorthand: total number of raw samples in the buffer.</summary>
    public int Length => Samples.Length;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="AudioSignal"/>.
    /// </summary>
    /// <param name="samples">PCM data. For stereo must be interleaved.</param>
    /// <param name="sampleRate">A validated <see cref="ValueObjects.SampleRate"/>.</param>
    /// <param name="channels">Channel layout — defaults to Mono.</param>
    /// <exception cref="InvalidAudioSignalException">
    /// Thrown when the sample buffer is empty or has an odd length for stereo.
    /// </exception>
    public AudioSignal(
        ReadOnlyMemory<float> samples,
        SampleRate sampleRate,
        AudioChannels channels = AudioChannels.Mono)
    {
        if (samples.IsEmpty)
            throw new InvalidAudioSignalException("Sample buffer must not be empty.");

        if (channels == AudioChannels.Stereo && samples.Length % 2 != 0)
            throw new InvalidAudioSignalException(
                $"Stereo signal requires an even number of samples, but got {samples.Length}.");

        Samples = samples;
        SampleRate = sampleRate ?? throw new ArgumentNullException(nameof(sampleRate));
        Channels = channels;
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>Creates a mono signal from a float array.</summary>
    public static AudioSignal FromArray(float[] samples, SampleRate sampleRate) =>
        new(samples.AsMemory(), sampleRate, AudioChannels.Mono);

    /// <summary>Creates a stereo signal from an interleaved float array.</summary>
    public static AudioSignal FromInterleavedArray(float[] samples, SampleRate sampleRate) =>
        new(samples.AsMemory(), sampleRate, AudioChannels.Stereo);

    // ── Indexer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the raw sample at position <paramref name="n"/> (f(n) in DSP notation).
    /// For stereo, this indexes the interleaved buffer directly.
    /// </summary>
    public float this[int n] => Samples.Span[n];

    // ── Channel operations ───────────────────────────────────────────────────

    /// <summary>
    /// Extracts a single channel as a new mono <see cref="AudioSignal"/>.
    /// For a mono signal returns itself.
    /// </summary>
    /// <param name="channelIndex">0 = Left/Mono, 1 = Right.</param>
    public AudioSignal GetChannel(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= (int)Channels)
            throw new ArgumentOutOfRangeException(
                nameof(channelIndex),
                $"Channel index {channelIndex} is out of range for a {Channels} signal.");

        if (Channels == AudioChannels.Mono)
            return this;

        var span = Samples.Span;
        var result = new float[FrameCount];
        int step = (int)Channels;

        for (int i = 0; i < FrameCount; i++)
            result[i] = span[i * step + channelIndex];

        return new AudioSignal(result.AsMemory(), SampleRate, AudioChannels.Mono);
    }

    /// <summary>
    /// Converts a stereo signal to mono by averaging L and R channels.
    /// Returns itself unchanged when already mono.
    /// </summary>
    public AudioSignal ToMono()
    {
        if (Channels == AudioChannels.Mono)
            return this;

        var span = Samples.Span;
        var result = new float[FrameCount];

        for (int i = 0; i < FrameCount; i++)
            result[i] = (span[i * 2] + span[i * 2 + 1]) * 0.5f;

        return new AudioSignal(result.AsMemory(), SampleRate, AudioChannels.Mono);
    }

    // ── Slicing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a zero-copy slice of this signal (frame-based, channel-aware).
    /// </summary>
    /// <param name="startFrame">Inclusive start frame.</param>
    /// <param name="frameCount">Number of frames to include.</param>
    public AudioSignal Slice(int startFrame, int frameCount)
    {
        int step = (int)Channels;

        if (startFrame < 0 || startFrame >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        if (frameCount <= 0 || startFrame + frameCount > FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameCount));

        var sliced = Samples.Slice(startFrame * step, frameCount * step);
        return new AudioSignal(sliced, SampleRate, Channels);
    }

    // ── Mixing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Mixes (sums) this signal with another, sample by sample: mix(n) = s1(n) + s2(n).
    ///
    /// <para>
    /// From DSP theory: signals with opposite phases (π offset) cancel completely
    /// (destructive interference); in-phase signals double in amplitude (constructive).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidAudioSignalException">
    /// Thrown when sample rates or frame counts differ.
    /// </exception>
    public AudioSignal MixWith(AudioSignal other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));

        if (other.SampleRate != SampleRate)
            throw new InvalidAudioSignalException(
                $"Cannot mix signals with different sample rates ({SampleRate} vs {other.SampleRate}).");

        if (other.FrameCount != FrameCount)
            throw new InvalidAudioSignalException(
                $"Cannot mix signals with different lengths ({FrameCount} vs {other.FrameCount} frames).");

        if (other.Channels != Channels)
            throw new InvalidAudioSignalException(
                $"Cannot mix signals with different channel layouts ({Channels} vs {other.Channels}).");

        var a = Samples.Span;
        var b = other.Samples.Span;
        var result = new float[a.Length];

        for (int i = 0; i < a.Length; i++)
            result[i] = a[i] + b[i];

        return new AudioSignal(result.AsMemory(), SampleRate, Channels);
    }

    /// <summary>
    /// Mixes two signals with independent gain factors: mix(n) = g1·s1(n) + g2·s2(n).
    ///
    /// <para>
    /// This is the workhorse of digital mixers. By applying separate gains before summing,
    /// you can balance level and phase relationships without clipping.
    /// In a mixer topology, every channel has its own fader (gain) before the summing bus.
    /// </para>
    ///
    /// <para>
    /// Example: balance a vocal (0.7 gain) with a backing track (0.3 gain):
    /// <code>
    /// var mixed = vocal.MixWithGain(0.7f, backing, 0.3f);
    /// </code>
    /// Then call <see cref="Normalize"/> if needed to prevent clipping.
    /// </para>
    /// </summary>
    /// <param name="gain1">Gain factor for this signal (typically 0.0 to 1.0).</param>
    /// <param name="other">The other signal to mix in.</param>
    /// <param name="gain2">Gain factor for the other signal.</param>
    /// <exception cref="InvalidAudioSignalException">
    /// Thrown when sample rates, frame counts, or channel layouts differ.
    /// </exception>
    public AudioSignal MixWithGain(float gain1, AudioSignal other, float gain2)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));

        if (other.SampleRate != SampleRate)
            throw new InvalidAudioSignalException(
                $"Cannot mix signals with different sample rates ({SampleRate} vs {other.SampleRate}).");

        if (other.FrameCount != FrameCount)
            throw new InvalidAudioSignalException(
                $"Cannot mix signals with different lengths ({FrameCount} vs {other.FrameCount} frames).");

        if (other.Channels != Channels)
            throw new InvalidAudioSignalException(
                $"Cannot mix signals with different channel layouts ({Channels} vs {other.Channels}).");

        var a = Samples.Span;
        var b = other.Samples.Span;
        var result = new float[a.Length];

        for (int i = 0; i < a.Length; i++)
            result[i] = a[i] * gain1 + b[i] * gain2;

        return new AudioSignal(result.AsMemory(), SampleRate, Channels);
    }

    /// <summary>
    /// Static helper to mix multiple signals with independent gains.
    /// Equivalent to: Σ(gain[i] × signal[i]).
    /// </summary>
    /// <param name="signalsAndGains">
    /// Pairs of (gain, signal) tuples. Must have at least one pair.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the collection is empty or signals have incompatible parameters.
    /// </exception>
    public static AudioSignal MixWithGains(params (float gain, AudioSignal signal)[] signalsAndGains)
    {
        if (signalsAndGains == null || signalsAndGains.Length == 0)
            throw new ArgumentException("At least one signal required.", nameof(signalsAndGains));

        var result = signalsAndGains[0].signal.Scale(signalsAndGains[0].gain);
        for (int i = 1; i < signalsAndGains.Length; i++)
            result = result.MixWithGain(1f, signalsAndGains[i].signal.Scale(signalsAndGains[i].gain), 1f);

        return result;
    }

    /// <summary>
    /// Static helper to mix multiple signals with equal gain.
    /// </summary>
    public static AudioSignal Mix(IReadOnlyList<AudioSignal> signals)
    {
        if (signals is null || signals.Count == 0)
            throw new ArgumentException("At least one signal required.", nameof(signals));

        var result = signals[0];
        for (int i = 1; i < signals.Count; i++)
            result = result.MixWith(signals[i]);
        return result;
    }

    // ── Scaling & Offset ─────────────────────────────────────────────────────

    /// <summary>
    /// Multiplies every sample by <paramref name="gain"/>: scaled(n) = s(n) × g.
    ///
    /// <para>
    /// gain &gt; 1 amplifies, 0 &lt; gain &lt; 1 attenuates, gain = 0 produces silence.
    /// For PCM conversion use <see cref="Normalize"/> first so the signal fits
    /// within the quantizer's range without clipping.
    /// </para>
    /// </summary>
    public AudioSignal Scale(float gain)
    {
        var span = Samples.Span;
        var result = new float[span.Length];
        for (int i = 0; i < span.Length; i++)
            result[i] = span[i] * gain;
        return new AudioSignal(result.AsMemory(), SampleRate, Channels);
    }

    /// <summary>
    /// Scales the signal so its peak absolute amplitude equals 1.0 (normalized range).
    /// Returns itself unchanged when the signal is already silent.
    ///
    /// <para>
    /// Normalization is required before PCM quantization to use the full dynamic range
    /// without overflow — equivalent to SNR-optimal gain staging.
    /// </para>
    /// </summary>
    public AudioSignal Normalize()
    {
        float peak = PeakAmplitude();
        return peak < float.Epsilon ? this : Scale(1f / peak);
    }

    /// <summary>
    /// Adds a constant DC offset to every sample: offset(n) = s(n) + dc.
    ///
    /// <para>
    /// Shifts the mean value of the signal. A bipolar signal in [−1, +1]
    /// becomes unipolar in [0, +1] with dc = 1.0 followed by Scale(0.5).
    /// This is exactly how the Hann window is constructed from a cosine wave.
    /// </para>
    /// </summary>
    public AudioSignal WithOffset(float dc)
    {
        var span = Samples.Span;
        var result = new float[span.Length];
        for (int i = 0; i < span.Length; i++)
            result[i] = span[i] + dc;
        return new AudioSignal(result.AsMemory(), SampleRate, Channels);
    }

    // ── Resampling ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resamples this signal to a target sample rate using linear interpolation.
    ///
    /// <para>
    /// From DSP theory: resampling is a cascade of three operations:
    /// 1. Apply anti-aliasing low-pass filter (downsampling)
    /// 2. Change the clock rate
    /// 3. Fill in the gaps with interpolation (upsampling)
    ///
    /// This implementation uses linear interpolation, which preserves continuity
    /// but introduces slight high-frequency attenuation. For higher quality,
    /// consider polyphase filtering (not yet implemented).
    /// </para>
    ///
    /// <para>
    /// Common use cases:
    /// — ASR: 48 kHz audio → 16 kHz (model input)
    /// — Playback: match device sample rate
    /// — Pitch shifting: resample then time-stretch
    /// </para>
    /// </summary>
    /// <param name="targetSampleRate">The new sample rate (must be valid and different from current).</param>
    /// <returns>A resampled <see cref="AudioSignal"/> with the same audio but different sample density.</returns>
    /// <exception cref="ResamplingException">
    /// Thrown when the target rate is invalid, unsupported, or would require extreme resampling.
    /// </exception>
    /// <exception cref="InvalidAudioSignalException">
    /// Thrown when resampling results in an invalid signal.
    /// </exception>
    public AudioSignal Resample(SampleRate targetSampleRate)
    {
        if (targetSampleRate is null)
            throw new ArgumentNullException(nameof(targetSampleRate));

        if (targetSampleRate == SampleRate)
            return this; // No-op

        double ratio = (double)targetSampleRate.Value / SampleRate.Value;

        // Prevent extreme resampling that would lose quality or require huge buffers
        if (ratio < 0.1 || ratio > 10.0)
            throw new ResamplingException(
                SampleRate.Value,
                targetSampleRate.Value,
                $"Resampling ratio {ratio:F2}× is extreme. Supported range: 0.1× to 10×");

        int step = (int)Channels;
        int oldFrameCount = FrameCount;
        int newFrameCount = (int)Math.Round(oldFrameCount * ratio);

        // Ensure we have at least 1 frame
        if (newFrameCount < 1)
            newFrameCount = 1;

        var oldSamples = Samples.Span;
        var newSamples = new float[newFrameCount * step];

        // For each output frame, interpolate from the old sample grid
        for (int frameOut = 0; frameOut < newFrameCount; frameOut++)
        {
            double frameIn_precise = frameOut / ratio;
            int frameIn_floor = (int)frameIn_precise;
            int frameIn_ceil = Math.Min(frameIn_floor + 1, oldFrameCount - 1);
            double frac = frameIn_precise - frameIn_floor;

            // Interpolate each channel independently (works for mono and stereo)
            for (int ch = 0; ch < step; ch++)
            {
                int idxFloor = frameIn_floor * step + ch;
                int idxCeil = frameIn_ceil * step + ch;
                float vFloor = oldSamples[idxFloor];
                float vCeil = oldSamples[idxCeil];

                newSamples[frameOut * step + ch] =
                    (float)(vFloor * (1.0 - frac) + vCeil * frac);
            }
        }

        return new AudioSignal(newSamples.AsMemory(), targetSampleRate, Channels);
    }

    // ── Diagnostics ──────────────────────────────────────────────────────────

    /// <summary>Returns the peak absolute amplitude across all samples.</summary>
    public float PeakAmplitude()
    {
        var span = Samples.Span;
        float peak = 0f;
        for (int i = 0; i < span.Length; i++)
        {
            float abs = MathF.Abs(span[i]);
            if (abs > peak) peak = abs;
        }
        return peak;
    }

    /// <summary>Returns the root-mean-square amplitude — useful for SNR estimation.</summary>
    public float RmsAmplitude()
    {
        var span = Samples.Span;
        float sum = 0f;
        for (int i = 0; i < span.Length; i++)
            sum += span[i] * span[i];
        return MathF.Sqrt(sum / span.Length);
    }

    public override string ToString() =>
        $"AudioSignal {{ {Channels}, {SampleRate}, Frames={FrameCount}, Duration={Duration:F3}s }}";
}