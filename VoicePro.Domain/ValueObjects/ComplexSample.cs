namespace VoicePro.Domain.ValueObjects;

/// <summary>
/// Immutable value object representing a single complex number: re + j·im.
///
/// <para>
/// In DSP theory this corresponds to one output bin of an FFT — a point in the
/// complex plane described by Euler's formula: e^(jω) = cos(ω) + j·sin(ω).
/// </para>
///
/// <para>
/// Implemented as a <see langword="readonly struct"/> because FFT produces large
/// arrays of these; avoiding heap allocation per sample is critical for performance.
/// </para>
/// </summary>
public readonly struct ComplexSample : IEquatable<ComplexSample>
{
    // ── Static constants ─────────────────────────────────────────────────────

    /// <summary>0 + 0j</summary>
    public static readonly ComplexSample Zero = new(0f, 0f);

    /// <summary>1 + 0j</summary>
    public static readonly ComplexSample One = new(1f, 0f);

    /// <summary>0 + 1j  (the imaginary unit)</summary>
    public static readonly ComplexSample J = new(0f, 1f);

    // ── Core value ───────────────────────────────────────────────────────────

    /// <summary>Real part (x-axis / cosine component).</summary>
    public float Real { get; }

    /// <summary>Imaginary part (y-axis / sine component).</summary>
    public float Imaginary { get; }

    // ── Construction ─────────────────────────────────────────────────────────

    public ComplexSample(float real, float imaginary)
    {
        Real = real;
        Imaginary = imaginary;
    }

    /// <summary>
    /// Creates a complex sample from polar form: magnitude × e^(j·phase).
    /// Implements Euler's formula: a·e^(jφ) = a·cos(φ) + j·a·sin(φ).
    /// </summary>
    /// <param name="magnitude">Radius of the point in the complex plane (≥ 0).</param>
    /// <param name="phase">Angle in radians.</param>
    public static ComplexSample FromPolar(float magnitude, float phase) =>
        new(magnitude * MathF.Cos(phase),
            magnitude * MathF.Sin(phase));

    // ── Derived properties ───────────────────────────────────────────────────

    /// <summary>
    /// Magnitude (modulus): |c| = √(re² + im²).
    /// In a spectrum this is the amplitude of the frequency component.
    /// </summary>
    public float Magnitude => MathF.Sqrt(Real * Real + Imaginary * Imaginary);

    /// <summary>
    /// Phase (argument): φ = arctan2(im, re), range [−π, π].
    /// In a spectrum this is the phase offset of the frequency component.
    /// </summary>
    public float Phase => MathF.Atan2(Imaginary, Real);

    /// <summary>
    /// Power (magnitude squared): |c|² = re² + im².
    /// Avoids a sqrt — use this when comparing magnitudes.
    /// </summary>
    public float Power => Real * Real + Imaginary * Imaginary;

    /// <summary>
    /// Complex conjugate: (re + j·im)* = re − j·im.
    /// Used in the relationship: real_signal = ½(c + c*).
    /// </summary>
    public ComplexSample Conjugate() => new(Real, -Imaginary);

    // ── Arithmetic operators ─────────────────────────────────────────────────

    public static ComplexSample operator +(ComplexSample a, ComplexSample b) =>
        new(a.Real + b.Real, a.Imaginary + b.Imaginary);

    public static ComplexSample operator -(ComplexSample a, ComplexSample b) =>
        new(a.Real - b.Real, a.Imaginary - b.Imaginary);

    /// <summary>
    /// Complex multiplication: (a+jb)(c+jd) = (ac−bd) + j(ad+bc).
    /// This is a rotation + scaling in the complex plane — the heart of the FFT butterfly.
    /// </summary>
    public static ComplexSample operator *(ComplexSample a, ComplexSample b) =>
        new(a.Real * b.Real - a.Imaginary * b.Imaginary,
            a.Real * b.Imaginary + a.Imaginary * b.Real);

    public static ComplexSample operator *(float scalar, ComplexSample c) =>
        new(scalar * c.Real, scalar * c.Imaginary);

    public static ComplexSample operator *(ComplexSample c, float scalar) =>
        new(scalar * c.Real, scalar * c.Imaginary);

    public static ComplexSample operator -(ComplexSample c) =>
        new(-c.Real, -c.Imaginary);

    // ── Equality ─────────────────────────────────────────────────────────────

    public bool Equals(ComplexSample other) =>
        Real == other.Real && Imaginary == other.Imaginary;

    public override bool Equals(object? obj) =>
        obj is ComplexSample c && Equals(c);

    public override int GetHashCode() => HashCode.Combine(Real, Imaginary);

    public static bool operator ==(ComplexSample a, ComplexSample b) => a.Equals(b);
    public static bool operator !=(ComplexSample a, ComplexSample b) => !a.Equals(b);

    // ── Display ──────────────────────────────────────────────────────────────

    public override string ToString() =>
        Imaginary >= 0
            ? $"{Real:F4} + {Imaginary:F4}j"
            : $"{Real:F4} - {MathF.Abs(Imaginary):F4}j";
}