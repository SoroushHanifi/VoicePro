using VoicePro.Domain.Exceptions;

namespace VoicePro.Domain.ValueObjects;

/// <summary>
/// Strongly-typed, immutable value object representing a validated audio sample rate.
/// Only standard rates used in DSP pipelines are accepted.
/// </summary>
public sealed class SampleRate : IEquatable<SampleRate>, IComparable<SampleRate>
{
    // ── Standard rates ───────────────────────────────────────────────────────

    /// <summary>8 000 Hz — telephony / speech recognition.</summary>
    public static readonly SampleRate Hz8000 = new(8_000);

    /// <summary>16 000 Hz — wideband speech, most ASR models (Whisper default).</summary>
    public static readonly SampleRate Hz16000 = new(16_000);

    /// <summary>22 050 Hz — half CD, common in music synthesis.</summary>
    public static readonly SampleRate Hz22050 = new(22_050);

    /// <summary>44 100 Hz — CD Audio standard.</summary>
    public static readonly SampleRate Hz44100 = new(44_100);

    /// <summary>48 000 Hz — professional video/audio standard.</summary>
    public static readonly SampleRate Hz48000 = new(48_000);

    /// <summary>96 000 Hz — high-resolution audio.</summary>
    public static readonly SampleRate Hz96000 = new(96_000);

    // ── Core value ───────────────────────────────────────────────────────────

    /// <summary>The sample rate in Hz.</summary>
    public int Value { get; }

    /// <summary>Nyquist frequency: the maximum representable frequency without aliasing.</summary>
    public double NyquistFrequency => Value / 2.0;

    /// <summary>Duration of a single sample in seconds (T = 1 / sr).</summary>
    public double SamplePeriod => 1.0 / Value;

    // ── Construction ─────────────────────────────────────────────────────────

    private SampleRate(int value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a <see cref="SampleRate"/> from an integer value.
    /// </summary>
    /// <exception cref="InvalidSampleRateException">
    /// Thrown when <paramref name="hz"/> is not in the supported set.
    /// </exception>
    public static SampleRate FromHz(int hz)
    {
        if (!InvalidSampleRateException.SupportedRates.Contains(hz))
            throw new InvalidSampleRateException(hz);

        return hz switch
        {
            8_000 => Hz8000,
            16_000 => Hz16000,
            22_050 => Hz22050,
            44_100 => Hz44100,
            48_000 => Hz48000,
            96_000 => Hz96000,
            _ => new SampleRate(hz) // unreachable, but satisfies compiler
        };
    }

    /// <summary>
    /// Tries to create a <see cref="SampleRate"/> without throwing.
    /// </summary>
    public static bool TryFromHz(int hz, out SampleRate? result)
    {
        if (InvalidSampleRateException.SupportedRates.Contains(hz))
        {
            result = FromHz(hz);
            return true;
        }

        result = null;
        return false;
    }

    // ── DSP helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a duration in seconds to a sample count.
    /// </summary>
    public int ToSampleCount(double durationSeconds)
    {
        if (durationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be non-negative.");

        return (int)Math.Round(durationSeconds * Value);
    }

    /// <summary>
    /// Converts a sample index to its corresponding time offset in seconds.
    /// </summary>
    public double ToSeconds(int sampleIndex) => (double)sampleIndex / Value;

    // ── Equality, comparison, operators ──────────────────────────────────────

    public bool Equals(SampleRate? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is SampleRate sr && Equals(sr);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(SampleRate? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator ==(SampleRate? a, SampleRate? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(SampleRate? a, SampleRate? b) => !(a == b);
    public static bool operator <(SampleRate a, SampleRate b) => a.Value < b.Value;
    public static bool operator >(SampleRate a, SampleRate b) => a.Value > b.Value;
    public static bool operator <=(SampleRate a, SampleRate b) => a.Value <= b.Value;
    public static bool operator >=(SampleRate a, SampleRate b) => a.Value >= b.Value;

    /// <summary>Implicit conversion so int literals work where SampleRate is expected.</summary>
    public static implicit operator int(SampleRate sr) => sr.Value;

    public override string ToString() =>
        Value >= 1_000
            ? $"{Value / 1000.0:G} kHz"
            : $"{Value} Hz";
}