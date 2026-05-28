using VoicePro.Domain.Exceptions;

namespace VoicePro.Domain.ValueObjects;

/// <summary>
/// Immutable value object representing a complex-valued signal — the output of an FFT.
///
/// <para>
/// Each element is a <see cref="ComplexSample"/> (re + j·im) corresponding to one
/// frequency bin. The relationship to the real-valued time-domain signal is given by
/// Euler's formula: a·cos(ωn) = ½a·(e^(jωn) + e^(−jωn)).
/// </para>
///
/// <para>
/// For a real-valued input of length N, the FFT produces N complex bins, but only
/// bins 0..N/2 are independent — the upper half is the complex conjugate mirror
/// of the lower half. Use <see cref="UniqueLength"/> to access only the meaningful bins.
/// </para>
/// </summary>
public sealed class ComplexSignal
{
    // ── Core data ────────────────────────────────────────────────────────────

    /// <summary>All FFT bins (full N-length output).</summary>
    public ReadOnlyMemory<ComplexSample> Bins { get; }

    /// <summary>Sample rate of the original time-domain signal.</summary>
    public SampleRate SampleRate { get; }

    // ── Derived properties ───────────────────────────────────────────────────

    /// <summary>Total number of bins (= N, the FFT size).</summary>
    public int Length => Bins.Length;

    /// <summary>
    /// Number of unique (non-mirrored) bins for a real-valued input: N/2 + 1.
    /// Bins above this index are complex conjugates of bins below it.
    /// </summary>
    public int UniqueLength => Length / 2 + 1;

    /// <summary>Frequency resolution per bin in Hz: sr / N.</summary>
    public double BinResolution => (double)SampleRate.Value / Length;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="ComplexSignal"/> from FFT output bins.
    /// </summary>
    /// <param name="bins">Full FFT output array.</param>
    /// <param name="sampleRate">Sample rate of the original audio.</param>
    public ComplexSignal(ReadOnlyMemory<ComplexSample> bins, SampleRate sampleRate)
    {
        if (bins.IsEmpty)
            throw new InvalidAudioSignalException("ComplexSignal bins must not be empty.");

        if ((bins.Length & (bins.Length - 1)) != 0)
            throw new InvalidAudioSignalException(
                $"FFT size must be a power of 2, but got {bins.Length}.");

        Bins = bins;
        SampleRate = sampleRate ?? throw new ArgumentNullException(nameof(sampleRate));
    }

    // ── Indexer ──────────────────────────────────────────────────────────────

    /// <summary>Gets the complex bin at index <paramref name="k"/>.</summary>
    public ComplexSample this[int k] => Bins.Span[k];

    // ── Frequency mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the frequency in Hz corresponding to bin index <paramref name="k"/>.
    /// Implements: f(k) = k × sr / N.
    /// </summary>
    public double BinFrequency(int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(k, Length);
        return k * BinResolution;
    }

    /// <summary>
    /// Returns the bin index closest to a given frequency in Hz.
    /// Useful for reading amplitude at a specific frequency.
    /// </summary>
    public int FrequencyToBin(double frequencyHz)
    {
        int k = (int)Math.Round(frequencyHz / BinResolution);
        return Math.Clamp(k, 0, UniqueLength - 1);
    }

    // ── Spectrum extraction ──────────────────────────────────────────────────

    /// <summary>
    /// Magnitude spectrum: |FFT(k)| = √(re² + im²) for each bin.
    /// Represents the amplitude of each frequency component.
    /// Only returns the <see cref="UniqueLength"/> non-mirrored bins.
    /// </summary>
    public float[] MagnitudeSpectrum()
    {
        var span = Bins.Span;
        var result = new float[UniqueLength];

        for (int k = 0; k < UniqueLength; k++)
            result[k] = span[k].Magnitude;

        return result;
    }

    /// <summary>
    /// Power spectrum: |FFT(k)|² = re² + im² for each bin.
    /// Avoids a sqrt — use when relative comparisons are enough.
    /// Only returns the <see cref="UniqueLength"/> non-mirrored bins.
    /// </summary>
    public float[] PowerSpectrum()
    {
        var span = Bins.Span;
        var result = new float[UniqueLength];

        for (int k = 0; k < UniqueLength; k++)
            result[k] = span[k].Power;

        return result;
    }

    /// <summary>
    /// Phase spectrum: arctan2(im, re) for each bin, in radians.
    /// Only returns the <see cref="UniqueLength"/> non-mirrored bins.
    /// </summary>
    public float[] PhaseSpectrum()
    {
        var span = Bins.Span;
        var result = new float[UniqueLength];

        for (int k = 0; k < UniqueLength; k++)
            result[k] = span[k].Phase;

        return result;
    }

    // ── Display ──────────────────────────────────────────────────────────────

    public override string ToString() =>
        $"ComplexSignal {{ Bins={Length}, UniqueLength={UniqueLength}, " +
        $"BinResolution={BinResolution:F2} Hz, SampleRate={SampleRate} }}";
}