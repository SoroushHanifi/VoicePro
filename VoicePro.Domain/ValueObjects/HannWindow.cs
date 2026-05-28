using VoicePro.Domain.Exceptions;

namespace VoicePro.Domain.ValueObjects;

/// <summary>
/// Immutable Hann window function, pre-computed as a Lookup Table (LUT).
///
/// <para>
/// The Hann window is defined as:
///   hann(n) = 0.5 − 0.5 × cos(2πn / N),  0 ≤ n &lt; N
/// which is a cosine wave scaled and offset into the range [0, 1].
/// </para>
///
/// <para>
/// Applied to an audio frame before FFT to prevent spectral leakage:
/// by tapering the frame edges to zero, the FFT sees a smooth periodic signal
/// instead of a discontinuous jump at the boundaries.
/// </para>
///
/// <para>
/// Instances are cached by size — calling <see cref="ForSize"/> with the same N
/// always returns the same pre-computed object with no reallocation.
/// </para>
/// </summary>
public sealed class HannWindow
{
    // ── Core data ────────────────────────────────────────────────────────────

    /// <summary>Pre-computed window coefficients in [0, 1].</summary>
    public ReadOnlyMemory<float> Coefficients { get; }

    /// <summary>Number of points in the window (= FFT frame size).</summary>
    public int Size => Coefficients.Length;

    // ── Cache ─────────────────────────────────────────────────────────────────

    private static readonly Dictionary<int, HannWindow> _cache = new();
    private static readonly object _lock = new();

    // ── Construction ─────────────────────────────────────────────────────────

    private HannWindow(int size)
    {
        var buffer = new float[size];
        // hann(n) = 0.5 - 0.5 × cos(2πn / N)
        // = Offset(−cos wave) → scale by 0.5 → offset by +0.5
        // result: 0.0 at edges, 1.0 at centre
        float twoPiOverN = 2f * MathF.PI / size;
        for (int n = 0; n < size; n++)
            buffer[n] = 0.5f - 0.5f * MathF.Cos(twoPiOverN * n);
        Coefficients = buffer;
    }

    /// <summary>
    /// Returns a cached <see cref="HannWindow"/> for the given frame size.
    /// The window is computed once and reused for every subsequent call with the same size.
    /// </summary>
    /// <param name="size">Frame size in samples — must be a power of 2 ≥ 2.</param>
    /// <exception cref="InvalidAudioSignalException">
    /// Thrown when <paramref name="size"/> is not a positive power of 2.
    /// </exception>
    public static HannWindow ForSize(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new InvalidWindowException(
                $"Hann window size must be a power of 2 ≥ 2, but got {size}.");

        lock (_lock)
        {
            if (!_cache.TryGetValue(size, out var window))
            {
                window = new HannWindow(size);
                _cache[size] = window;
            }
            return window;
        }
    }

    // ── Application ──────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the window to a frame: windowed(n) = frame(n) × hann(n).
    /// Returns a new array — the input is not modified.
    /// </summary>
    /// <param name="frame">Input frame — must have exactly <see cref="Size"/> samples.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="frame"/> length does not match <see cref="Size"/>.
    /// </exception>
    public float[] Apply(ReadOnlySpan<float> frame)
    {
        if (frame.Length != Size)
            throw new ArgumentException(
                $"Frame length {frame.Length} must match window size {Size}.",
                nameof(frame));

        var coeffs = Coefficients.Span;
        var result = new float[Size];
        for (int n = 0; n < Size; n++)
            result[n] = frame[n] * coeffs[n];
        return result;
    }

    /// <summary>
    /// Applies the window in-place, writing into <paramref name="destination"/>.
    /// Use this overload in hot paths to avoid an extra allocation.
    /// </summary>
    /// <param name="frame">Source frame — must have exactly <see cref="Size"/> samples.</param>
    /// <param name="destination">Output buffer — must have exactly <see cref="Size"/> samples.</param>
    public void Apply(ReadOnlySpan<float> frame, Span<float> destination)
    {
        if (frame.Length != Size)
            throw new ArgumentException(
                $"Frame length {frame.Length} must match window size {Size}.", nameof(frame));
        if (destination.Length != Size)
            throw new ArgumentException(
                $"Destination length {destination.Length} must match window size {Size}.", nameof(destination));

        var coeffs = Coefficients.Span;
        for (int n = 0; n < Size; n++)
            destination[n] = frame[n] * coeffs[n];
    }

    // ── Diagnostics ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the coherent gain of the window: mean of all coefficients.
    /// For a Hann window this is 0.5 — useful for amplitude correction after FFT.
    /// </summary>
    public float CoherentGain()
    {
        var span = Coefficients.Span;
        float sum = 0f;
        for (int n = 0; n < span.Length; n++)
            sum += span[n];
        return sum / span.Length;
    }

    public override string ToString() => $"HannWindow {{ Size={Size} }}";
}