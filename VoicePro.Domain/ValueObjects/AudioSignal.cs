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