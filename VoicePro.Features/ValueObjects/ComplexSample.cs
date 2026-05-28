using MathNet.Numerics;

namespace VoicePro.Features.ValueObjects;

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
/// Internally wraps <see cref="Complex32"/> from MathNet.Numerics.
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

    // ── Internal MathNet value ───────────────────────────────────────────────

    private readonly Complex32 _value;

    // ── Core properties ──────────────────────────────────────────────────────

    /// <summary>Real part (x-axis / cosine component).</summary>
    public float Real => _value.Real;

    /// <summary>Imaginary part (y-axis / sine component).</summary>
    public float Imaginary => _value.Imaginary;

    // ── Construction ─────────────────────────────────────────────────────────

    public ComplexSample(float real, float imaginary)
    {
        _value = new Complex32(real, imaginary);
    }

    /// <summary>Internal constructor from MathNet Complex32.</summary>
    internal ComplexSample(Complex32 value)
    {
        _value = value;
    }

    /// <summary>
    /// Creates a complex sample from polar form: magnitude × e^(j·phase).
    /// Implements Euler's formula: a·e^(jφ) = a·cos(φ) + j·a·sin(φ).
    /// </summary>
    /// <param name="magnitude">Radius of the point in the complex plane (≥ 0).</param>
    /// <param name="phase">Angle in radians.</param>
    public static ComplexSample FromPolar(float magnitude, float phase) =>
        new(Complex32.FromPolarCoordinates(magnitude, phase));

    // ── Derived properties ───────────────────────────────────────────────────

    /// <summary>
    /// Magnitude (modulus): |c| = √(re² + im²).
    /// In a spectrum this is the amplitude of the frequency component.
    /// </summary>
    public float Magnitude => _value.Magnitude;

    /// <summary>
    /// Phase (argument): φ = arctan2(im, re), range [−π, π].
    /// In a spectrum this is the phase offset of the frequency component.
    /// </summary>
    public float Phase => _value.Phase;

    /// <summary>
    /// Power (magnitude squared): |c|² = re² + im².
    /// Avoids a sqrt — use this when comparing magnitudes.
    /// </summary>
    public float Power => _value.MagnitudeSquared;

    /// <summary>
    /// Complex conjugate: (re + j·im)* = re − j·im.
    /// Used in the relationship: real_signal = ½(c + c*).
    /// </summary>
    public ComplexSample Conjugate() => new(_value.Conjugate());

    // ── Conversion ───────────────────────────────────────────────────────────

    /// <summary>Converts to MathNet Complex32 for use in MathNet algorithms.</summary>
    public Complex32 ToComplex32() => _value;

    /// <summary>Creates a ComplexSample from a MathNet Complex32.</summary>
    public static ComplexSample FromComplex32(Complex32 c) => new(c);

    // ── Arithmetic operators ─────────────────────────────────────────────────

    public static ComplexSample operator +(ComplexSample a, ComplexSample b) =>
        new(a._value + b._value);

    public static ComplexSample operator -(ComplexSample a, ComplexSample b) =>
        new(a._value - b._value);

    /// <summary>
    /// Complex multiplication: (a+jb)(c+jd) = (ac−bd) + j(ad+bc).
    /// This is a rotation + scaling in the complex plane — the heart of the FFT butterfly.
    /// </summary>
    public static ComplexSample operator *(ComplexSample a, ComplexSample b) =>
        new(a._value * b._value);

    public static ComplexSample operator *(float scalar, ComplexSample c) =>
        new(scalar * c._value);

    public static ComplexSample operator *(ComplexSample c, float scalar) =>
        new(scalar * c._value);

    public static ComplexSample operator -(ComplexSample c) =>
        new(-c._value);

    // ── Equality ─────────────────────────────────────────────────────────────

    public bool Equals(ComplexSample other) => _value == other._value;
    public override bool Equals(object? obj) => obj is ComplexSample c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(Real, Imaginary);

    public static bool operator ==(ComplexSample a, ComplexSample b) => a.Equals(b);
    public static bool operator !=(ComplexSample a, ComplexSample b) => !a.Equals(b);

    // ── Display ──────────────────────────────────────────────────────────────

    public override string ToString() =>
        Imaginary >= 0
            ? $"{Real:F4} + {Imaginary:F4}j"
            : $"{Real:F4} - {MathF.Abs(Imaginary):F4}j";
}