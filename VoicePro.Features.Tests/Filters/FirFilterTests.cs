using Xunit;
using VoicePro.Domain.ValueObjects;
using VoicePro.Features.Filters;

namespace VoicePro.Features.Tests.Filters;

// ═══════════════════════════════════════════════════════════════════════════
// FIR FILTER TESTS
// ═══════════════════════════════════════════════════════════════════════════

public class FirFilterTests
{
    private readonly SampleRate _sr = SampleRate.Hz44100;

    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void LowPassAveraging_HasCorrectCoefficients()
    {
        var filter = FirFilter.LowPassAveraging();

        Assert.Equal(0.5f, filter.A0);
        Assert.Equal(0.5f, filter.A1);
    }

    [Fact]
    public void HighPass_HasNegativeA1()
    {
        var filter = FirFilter.HighPass();

        Assert.Equal(0.5f, filter.A0);
        Assert.Equal(-0.5f, filter.A1);
    }

    [Fact]
    public void IsLinearPhase_SymmetricCoeffs_ReturnsTrue()
    {
        // a₀ = a₁ → symmetric → linear phase
        var filter = FirFilter.LowPassAveraging();
        Assert.True(filter.IsLinearPhase);
    }

    [Fact]
    public void IsLinearPhase_AsymmetricCoeffs_ReturnsFalse()
    {
        var filter = FirFilter.Custom(0.7f, 0.3f);
        Assert.False(filter.IsLinearPhase);
    }

    // ── Signal processing ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_FirstSample_UsesZeroDelay()
    {
        // Arrange: first call, _delayed = 0
        // y(0) = 0.5·x(0) + 0.5·0 = 0.5·x(0)
        var filter = FirFilter.LowPassAveraging();
        var signal = AudioSignal.FromArray(new[] { 1.0f }, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        Assert.Equal(0.5f, result[0], precision: 6);
    }

    [Fact]
    public void Apply_TwoSamples_CorrectOutput()
    {
        // y(0) = 0.5·1 + 0.5·0 = 0.5
        // y(1) = 0.5·1 + 0.5·1 = 1.0
        var filter = FirFilter.LowPassAveraging();
        var signal = AudioSignal.FromArray(new[] { 1.0f, 1.0f }, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        Assert.Equal(0.5f, result[0], precision: 6);
        Assert.Equal(1.0f, result[1], precision: 6);
    }

    [Fact]
    public void Apply_ConstantSignal_PassesThroughAfterFirstSample()
    {
        // For a constant input c: after the first sample, y(n) = 0.5c + 0.5c = c
        var filter = FirFilter.LowPassAveraging();
        var samples = Enumerable.Repeat(1.0f, 100).ToArray();
        var signal = AudioSignal.FromArray(samples, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        // All samples from index 1 onwards should equal 1.0
        for (int i = 1; i < result.Length; i++)
            Assert.Equal(1.0f, result[i], precision: 6);
    }

    [Fact]
    public void Apply_ImpulseSignal_DecaysAfterImpulse()
    {
        // Arrange: impulse at n=0, silence after
        // y(0) = 0.5·1 + 0.5·0 = 0.5
        // y(1) = 0.5·0 + 0.5·1 = 0.5  (x(n-1) = 1)
        // y(2) = 0.5·0 + 0.5·0 = 0.0
        var filter = FirFilter.LowPassAveraging();
        var signal = AudioSignal.FromArray(new[] { 1.0f, 0.0f, 0.0f }, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        Assert.Equal(0.5f, result[0], precision: 6);
        Assert.Equal(0.5f, result[1], precision: 6);  // FIR: impulse response has FINITE length
        Assert.Equal(0.0f, result[2], precision: 6);  // ← key: back to zero after 2 samples
    }

    [Fact]
    public void Apply_HighPass_AttenuatesDCComponent()
    {
        // A constant signal is pure DC (f=0 Hz)
        // High-pass should heavily attenuate it
        var filter = FirFilter.HighPass();
        var samples = Enumerable.Repeat(1.0f, 10).ToArray();
        var signal = AudioSignal.FromArray(samples, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        // After the first sample, y = 0.5·1 − 0.5·1 = 0 for DC
        for (int i = 1; i < result.Length; i++)
            Assert.Equal(0.0f, result[i], precision: 6);
    }

    [Fact]
    public void Apply_PreservesChannelLayout()
    {
        var filter = FirFilter.LowPassAveraging();
        var stereo = AudioSignal.FromInterleavedArray(
            new[] { 1.0f, 0.5f, 1.0f, 0.5f }, SampleRate.Hz44100);

        var result = filter.Apply(stereo);

        Assert.Equal(AudioChannels.Stereo, result.Channels);
        Assert.Equal(stereo.SampleRate, result.SampleRate);
    }

    [Fact]
    public void Reset_ClearsDelayState()
    {
        var filter = FirFilter.LowPassAveraging();
        var signal = AudioSignal.FromArray(new[] { 1.0f }, SampleRate.Hz44100);

        // First call — builds up state
        filter.Apply(signal);

        // Reset
        filter.Reset();

        // Second call — should behave as if fresh
        var result = filter.Apply(signal);
        Assert.Equal(0.5f, result[0], precision: 6); // y(0) = 0.5·1 + 0.5·0 = 0.5
    }

    // ── Amplitude response ────────────────────────────────────────────────────

    [Fact]
    public void AmplitudeResponse_LowPass_AtDC_IsOne()
    {
        // Low-pass FIR: A(0) = cos(0) = 1.0 → DC passes fully
        var filter = FirFilter.LowPassAveraging();
        float response = filter.AmplitudeResponse(0, 44100);

        Assert.Equal(1.0f, response, precision: 4);
    }

    [Fact]
    public void AmplitudeResponse_LowPass_AtNyquist_IsZero()
    {
        // Low-pass FIR: A(sr/2) = cos(π/2) = 0.0 → Nyquist is fully blocked
        var filter = FirFilter.LowPassAveraging();
        float response = filter.AmplitudeResponse(22050, 44100);

        Assert.Equal(0.0f, response, precision: 4);
    }

    [Fact]
    public void AmplitudeResponse_LowPass_AtQuarterNyquist_IsDecreasing()
    {
        // A(sr/4) should be between 0 and 1 — the filter is attenuating
        var filter = FirFilter.LowPassAveraging();
        float atDC = filter.AmplitudeResponse(0, 44100);
        float atMid = filter.AmplitudeResponse(11025, 44100);
        float atNyquist = filter.AmplitudeResponse(22050, 44100);

        Assert.True(atDC > atMid && atMid > atNyquist,
            "Low-pass amplitude must be monotonically decreasing from DC to Nyquist.");
    }

    [Fact]
    public void AmplitudeResponse_HighPass_AtDC_IsZero()
    {
        // High-pass: a₁ = -0.5 → A(0) = √(0.25 - 0.5 + 0.25) = 0
        var filter = FirFilter.HighPass();
        float response = filter.AmplitudeResponse(0, 44100);

        Assert.Equal(0.0f, response, precision: 4);
    }

    [Fact]
    public void AmplitudeResponse_HighPass_AtNyquist_IsOne()
    {
        // High-pass: at Nyquist (ω=π): A(π) = √(0.25 + 0.5 + 0.25) = 1.0
        var filter = FirFilter.HighPass();
        float response = filter.AmplitudeResponse(22050, 44100);

        Assert.Equal(1.0f, response, precision: 4);
    }

    // ── Phase response ────────────────────────────────────────────────────────

    [Fact]
    public void PhaseResponse_LowPass_IsLinear()
    {
        // For symmetric FIR, phase should be linear: PH(ω) = -ω/2
        var filter = FirFilter.LowPassAveraging();

        float ph1 = filter.PhaseResponse(5000, 44100);
        float ph2 = filter.PhaseResponse(10000, 44100);

        // Check linearity: ph(2f) ≈ 2 × ph(f)
        Assert.Equal(2.0f, ph2 / ph1, precision: 3);
    }

    [Fact]
    public void PhaseResponse_LowPass_AtDC_IsZero()
    {
        var filter = FirFilter.LowPassAveraging();
        float phase = filter.PhaseResponse(0, 44100);

        Assert.Equal(0.0f, phase, precision: 4);
    }

    // ── FilterType ────────────────────────────────────────────────────────────

    [Fact]
    public void Type_IsFIR()
    {
        var filter = FirFilter.LowPassAveraging();
        Assert.Equal(FilterType.FIR, filter.Type);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// IIR FILTER TESTS
// ═══════════════════════════════════════════════════════════════════════════

public class IirFilterTests
{
    private readonly SampleRate _sr = SampleRate.Hz44100;

    // ── Construction & stability ──────────────────────────────────────────────

    [Fact]
    public void Constructor_UnstableB_Throws()
    {
        // |b| >= 1 → unstable → should throw
        var ex = Assert.Throws<ArgumentException>(() => new IirFilter(0.5f, 1.0f));
        Assert.Contains("stability", ex.Message);
    }

    [Fact]
    public void Constructor_BNegativeOne_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new IirFilter(0.5f, -1.0f));
        Assert.Contains("stability", ex.Message);
    }

    [Fact]
    public void Constructor_ValidCoefficients_IsStable()
    {
        var filter = new IirFilter(0.1f, 0.9f);
        Assert.True(filter.IsStable);
    }

    [Fact]
    public void LowPass_Factory_CreatesStableFilter()
    {
        var filter = IirFilter.LowPass(0.9f);
        Assert.True(filter.IsStable);
        Assert.Equal(FilterType.IIR, filter.Type);
    }

    [Fact]
    public void HighPass_Factory_CreatesStableFilter()
    {
        var filter = IirFilter.HighPass(-0.9f);
        Assert.True(filter.IsStable);
    }

    // ── Signal processing ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_FirstSample_NoPreviousOutput()
    {
        // y(0) = a·x(0) - b·0 = a·x(0)
        var filter = new IirFilter(0.5f, 0.5f);
        var signal = AudioSignal.FromArray(new[] { 1.0f }, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        Assert.Equal(0.5f, result[0], precision: 6);
    }

    [Fact]
    public void Apply_ImpulseSignal_DecaysExponentially()
    {
        // For y(n) = a·x(n) - b·y(n-1) with impulse at n=0:
        // y(0) = a
        // y(1) = -b·a = -b·y(0)
        // y(2) = -b·y(1) = b²·a
        // → exponential decay with alternating sign when b > 0
        float a = 1.0f, b = 0.5f;
        var filter = new IirFilter(a, b);
        var signal = AudioSignal.FromArray(new[] { 1.0f, 0.0f, 0.0f, 0.0f }, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        Assert.Equal(1.0f, result[0], precision: 5);
        Assert.Equal(-0.5f, result[1], precision: 5);  // -b·a = -0.5
        Assert.Equal(0.25f, result[2], precision: 5);  // b²·a = 0.25
        Assert.Equal(-0.125f, result[3], precision: 5); // -b³·a = -0.125
    }

    [Fact]
    public void Apply_ImpulseResponse_IsInfinite_NeverExactlyZero()
    {
        // IIR key property: impulse response never fully decays to zero
        // (in theory — in float it eventually underflows, but much slower than FIR)
        float a = 1.0f, b = 0.9f;
        var filter = new IirFilter(a, b);

        // 50 samples after the impulse
        var longSilence = new float[51];
        longSilence[0] = 1.0f;
        var signal = AudioSignal.FromArray(longSilence, SampleRate.Hz44100);
        var result = filter.Apply(signal);

        // At sample 50, the response should still be non-zero (b^50 ≈ 0.005)
        Assert.True(MathF.Abs(result[50]) > 1e-4f,
            "IIR impulse response should still be non-zero 50 samples after impulse.");
    }

    [Fact]
    public void Apply_FeedbackBuildsUp_WithConstantInput()
    {
        // IIR accumulates history — output should reach a steady state
        var filter = IirFilter.LowPass(0.9f);
        var samples = Enumerable.Repeat(1.0f, 200).ToArray();
        var signal = AudioSignal.FromArray(samples, SampleRate.Hz44100);

        var result = filter.Apply(signal);

        // After enough samples, the output should approach DC gain ≈ 1.0
        float steadyState = result[result.Length - 1];
        Assert.InRange(steadyState, 0.9f, 1.1f);
    }

    [Fact]
    public void Apply_PreservesChannelLayout_AndSampleRate()
    {
        var filter = IirFilter.LowPass();
        var stereo = AudioSignal.FromInterleavedArray(
            new[] { 1.0f, 0.5f, 1.0f, 0.5f }, SampleRate.Hz44100);

        var result = filter.Apply(stereo);

        Assert.Equal(AudioChannels.Stereo, result.Channels);
        Assert.Equal(stereo.SampleRate, result.SampleRate);
        Assert.Equal(stereo.Length, result.Length);
    }

    [Fact]
    public void Reset_ClearsFeedbackState()
    {
        var filter = new IirFilter(1.0f, 0.9f);
        var impulse = AudioSignal.FromArray(new[] { 1.0f, 0.0f }, SampleRate.Hz44100);

        // First pass — builds up feedback
        filter.Apply(impulse);

        filter.Reset();

        // After reset, should behave as if starting fresh
        var result = filter.Apply(impulse);
        Assert.Equal(1.0f, result[0], precision: 5);   // y(0) = a·1 - b·0 = 1.0
        Assert.Equal(-0.9f, result[1], precision: 5);  // y(1) = a·0 - b·1.0 = -0.9
    }

    // ── Amplitude response ────────────────────────────────────────────────────

    [Fact]
    public void AmplitudeResponse_LowPass_AtDC_IsHighest()
    {
        var filter = IirFilter.LowPass(0.9f);
        float atDC = filter.AmplitudeResponse(0, 44100);
        float atNyquist = filter.AmplitudeResponse(22050, 44100);

        Assert.True(atDC > atNyquist,
            "Low-pass IIR: DC response must be greater than Nyquist response.");
    }

    [Fact]
    public void AmplitudeResponse_HigherB_SharperRolloff()
    {
        // Closer b is to 1, the sharper the low-pass roll-off
        var gentle = IirFilter.LowPass(0.5f);
        var sharp = IirFilter.LowPass(0.9f);

        float gentleMid = gentle.AmplitudeResponse(5000, 44100);
        float sharpMid = sharp.AmplitudeResponse(5000, 44100);

        // The sharper filter attenuates more at mid-frequencies
        Assert.True(sharpMid < gentleMid,
            "Higher b coefficient should produce sharper roll-off (lower mid-freq response).");
    }

    [Fact]
    public void AmplitudeResponse_Matches_TextbookFormula()
    {
        // Verify: A(ω) = a / √(1 + b² + 2·b·cos(ω))
        float a = 0.1f, b = 0.9f;
        var filter = new IirFilter(a, b);

        double freq = 1000;
        double sr = 44100;
        double omega = 2.0 * Math.PI * freq / sr;
        double expected = a / Math.Sqrt(1.0 + b * b + 2.0 * b * Math.Cos(omega));

        float actual = filter.AmplitudeResponse(freq, (int)sr);

        Assert.Equal((float)expected, actual, precision: 5);
    }

    // ── FIR vs IIR comparison ─────────────────────────────────────────────────

    [Fact]
    public void FirIir_BothLowPass_SameDirectionResponse()
    {
        var fir = FirFilter.LowPassAveraging();
        var iir = IirFilter.LowPass(0.9f);

        // Both should pass DC more than Nyquist
        Assert.True(fir.AmplitudeResponse(0, 44100) > fir.AmplitudeResponse(22050, 44100));
        Assert.True(iir.AmplitudeResponse(0, 44100) > iir.AmplitudeResponse(22050, 44100));
    }

    [Fact]
    public void FirImpulse_EndsIn2Samples_IirDoesNotEnd()
    {
        // FIR impulse response has finite length (2 non-zero samples for first-order)
        // IIR impulse response never fully decays

        var fir = FirFilter.LowPassAveraging();
        var iir = new IirFilter(1.0f, 0.9f);

        var impulse = AudioSignal.FromArray(
            new float[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, SampleRate.Hz44100);

        var firOut = fir.Apply(impulse);
        var iirOut = iir.Apply(impulse);

        // FIR: should be 0 from index 2 onwards
        for (int i = 2; i < firOut.Length; i++)
            Assert.Equal(0.0f, firOut[i], precision: 6);

        // IIR: should still be non-zero at index 9
        Assert.True(MathF.Abs(iirOut[9]) > 1e-5f);
    }

    // ── FilterType ────────────────────────────────────────────────────────────

    [Fact]
    public void Type_IsIIR()
    {
        var filter = IirFilter.LowPass();
        Assert.Equal(FilterType.IIR, filter.Type);
    }
}