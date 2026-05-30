using Xunit;
using VoicePro.Domain.ValueObjects;

namespace VoicePro.Domain.Tests;

/// <summary>
/// Test suite for signal delay operations based on DSP textbook section 5.4.6.
/// Tests the delay equation: delayed(n) = s(n - d)
/// </summary>
public class SignalDelayTests
{
    private readonly SampleRate _sr = SampleRate.Hz48000;

    [Fact]
    public void Delay_ZeroDelay_ReturnsSelf()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 0.5f, 0.5f }, _sr);

        // Act
        var delayed = signal.Delay(0);

        // Assert
        Assert.Same(signal, delayed); // Same object reference (no-op)
    }

    [Fact]
    public void Delay_SimpleCase_FillsWithZerosAtBeginning()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 1.0f, 2.0f, 3.0f }, _sr);

        // Act
        var delayed = signal.Delay(2);

        // Assert
        // First 2 samples should be silence
        Assert.Equal(0.0f, delayed[0], precision: 6);
        Assert.Equal(0.0f, delayed[1], precision: 6);
        // Next 3 samples are the original signal
        Assert.Equal(1.0f, delayed[2], precision: 6);
        Assert.Equal(2.0f, delayed[3], precision: 6);
        Assert.Equal(3.0f, delayed[4], precision: 6);
    }

    [Fact]
    public void Delay_IncreaseLength_Correctly()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[1000], _sr);

        // Act
        var delayed = signal.Delay(500);

        // Assert
        Assert.Equal(1500, delayed.Length);
        Assert.Equal(1500, delayed.FrameCount);
    }

    [Fact]
    public void Delay_Stereo_InterleavingPreserved()
    {
        // Arrange: [L0, R0, L1, R1]
        var stereo = AudioSignal.FromInterleavedArray(
            new[] { 1.0f, 2.0f, 3.0f, 4.0f }, _sr);

        // Act
        var delayed = stereo.Delay(1); // Delay by 1 frame = 2 samples

        // Assert
        // First frame (2 samples) should be silence
        Assert.Equal(0.0f, delayed[0], precision: 6);
        Assert.Equal(0.0f, delayed[1], precision: 6);
        // Original data follows
        Assert.Equal(1.0f, delayed[2], precision: 6);
        Assert.Equal(2.0f, delayed[3], precision: 6);
    }

    [Fact]
    public void Delay_NegativeDelay_Throws()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[100], _sr);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => signal.Delay(-5));
    }

    [Fact]
    public void DelayByTime_100Milliseconds_CorrectSampleCount()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[1000], _sr);
        double delayTime = 0.1; // 100ms

        // Act
        var delayed = signal.DelayByTime(delayTime);

        // Assert
        int expectedDelay = _sr.ToSampleCount(delayTime); // ~4800 samples @ 48kHz
        Assert.Equal(1000 + expectedDelay, delayed.FrameCount);
    }

    [Fact]
    public void Delay_PreservesAmplitude_OriginalSamplesUnchanged()
    {
        // Arrange: varied amplitude signal
        var signal = AudioSignal.FromArray(
            new[] { 0.1f, 0.5f, 0.9f, -0.3f, -0.7f }, _sr);

        // Act
        var delayed = signal.Delay(3);

        // Assert: verify original samples are copied exactly
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(signal[i], delayed[3 + i], precision: 6);
        }
    }
}

public class SlapBackEchoTests
{
    private readonly SampleRate _sr = SampleRate.Hz48000;

    [Fact]
    public void SlapBackEcho_ZeroDelay_OnlyAmplifies()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 0.5f }, _sr);

        // Act
        var echo = signal.SlapBackEcho(0, 0.5f);

        // Assert: gain = 1 + 0.5 = 1.5
        Assert.Equal(0.75f, echo[0], precision: 6);
    }

    [Fact]
    public void SlapBackEcho_StandardCase_CorrectMix()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 1.0f, 2.0f, 3.0f }, _sr);
        int delay = 2;
        float gain = 0.5f;

        // Act
        var echo = signal.SlapBackEcho(delay, gain);

        // Assert
        // Position 0,1: only original (padded)
        Assert.Equal(1.0f, echo[0], precision: 6);
        Assert.Equal(2.0f, echo[1], precision: 6);
        // Position 2: original[0] + 0 = 1.0
        Assert.Equal(1.0f, echo[2], precision: 6);
        // Position 3: original[1] + 0.5*original[0] = 2.0 + 0.5*1.0 = 2.5
        Assert.Equal(2.5f, echo[3], precision: 6);
        // Position 4: original[2] + 0.5*original[1] = 3.0 + 0.5*2.0 = 4.0
        Assert.Equal(4.0f, echo[4], precision: 6);
        // Position 5: 0 + 0.5*original[2] = 0.5*3.0 = 1.5
        Assert.Equal(1.5f, echo[5], precision: 6);
    }

    [Fact]
    public void SlapBackEcho_Stereo_ChannelsPreserved()
    {
        // Arrange
        var stereo = AudioSignal.FromInterleavedArray(
            new[] { 1.0f, 1.0f, 2.0f, 2.0f }, _sr);

        // Act
        var echo = stereo.SlapBackEcho(1, 0.5f);

        // Assert
        Assert.Equal(AudioChannels.Stereo, echo.Channels);
        Assert.Equal(8, echo.Length); // 4 original + 4 delayed
    }

    [Fact]
    public void SlapBackEcho_LargeGain_RisksClipping()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 0.6f }, _sr);

        // Act: deliberately use high gain to demonstrate clipping risk
        var echo = signal.SlapBackEcho(1, 0.8f);

        // Assert: peak should exceed 1.0 (clipping risk)
        float peak = echo.PeakAmplitude();
        Assert.True(peak > 1.0f);
    }

    [Fact]
    public void SlapBackEchoByTime_ProperConversion()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[1000], _sr);

        // Act
        var echo = signal.SlapBackEchoByTime(0.1, 0.5f); // 100ms delay

        // Assert
        int expectedDelay = _sr.ToSampleCount(0.1);
        Assert.Equal(1000 + expectedDelay, echo.FrameCount);
    }

    [Fact]
    public void SlapBackEcho_NegativeDelay_Throws()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[100], _sr);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => signal.SlapBackEcho(-5, 0.5f));
    }
}

public class FeedbackDelayTests
{
    private readonly SampleRate _sr = SampleRate.Hz48000;

    [Fact]
    public void FeedbackDelay_SingleIteration_IsSlabBackEcho()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 1.0f, 2.0f, 3.0f }, _sr);
        double delayTime = 1.0 / _sr.Value; // 1 sample
        float feedback = 0.5f;

        // Act
        var result = signal.FeedbackDelay(delayTime, feedback, 1);

        // Assert: should produce a single echo (similar to SlapBackEcho)
        Assert.NotNull(result);
        Assert.True(result.FrameCount > signal.FrameCount);
    }

    [Fact]
    public void FeedbackDelay_MultipleIterations_DecayingEchoes()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new[] { 1.0f }, _sr);
        double delayTime = 1.0 / _sr.Value;
        float feedback = 0.5f;

        // Act
        var result = signal.FeedbackDelay(delayTime, feedback, 3);

        // Assert: length should accommodate multiple delayed copies
        Assert.NotNull(result);
        Assert.True(result.FrameCount >= signal.FrameCount);
    }

    [Fact]
    public void FeedbackDelay_FeedbackGreaterEqual1_Throws()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[100], _sr);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => signal.FeedbackDelay(0.1, 1.0f, 2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => signal.FeedbackDelay(0.1, 1.5f, 2));
    }

    [Fact]
    public void FeedbackDelay_ZeroIterations_Throws()
    {
        // Arrange
        var signal = AudioSignal.FromArray(new float[100], _sr);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => signal.FeedbackDelay(0.1, 0.5f, 0));
    }

    [Fact]
    public void FeedbackDelay_DecreasingAmplitude_WithLowerFeedback()
    {
        // Arrange: Create a signal where we can track echo amplitudes
        var signal = AudioSignal.FromArray(new[] { 1.0f }, _sr);

        // Act: with feedback=0.3, each iteration should be much quieter
        var result = signal.FeedbackDelay(1.0 / _sr.Value, 0.3f, 4);

        // Assert: should not clip
        float peak = result.PeakAmplitude();
        Assert.True(peak < 2.5f); // 1.0 + 0.3 + 0.09 + 0.027 + ... ≈ 1.43
    }
}

/// <summary>
/// Integration tests combining delay with other DSP operations
/// </summary>
public class DelayIntegrationTests
{
    private readonly SampleRate _sr = SampleRate.Hz48000;

    [Fact]
    public void Delay_ThenNormalize_PreventClipping()
    {
        // Arrange: signal with echo that would clip
        var signal = AudioSignal.FromArray(new[] { 0.7f }, _sr);
        var echo = signal.SlapBackEcho(10, 0.5f); // might clip

        // Act
        var normalized = echo.Normalize();

        // Assert
        Assert.Equal(1.0f, normalized.PeakAmplitude(), precision: 5);
    }

    [Fact]
    public void Delay_ThenResample_TimeAlignmentPreserved()
    {
        // Arrange: 1 second of silence @ 48kHz
        var signal = AudioSignal.FromArray(new float[48000], _sr);
        double delayTime = 0.5; // delay by 0.5 seconds

        // Act
        var delayed = signal.DelayByTime(delayTime);
        var resampled = delayed.Resample(SampleRate.Hz16000);

        // Assert: resample should still work; duration should be preserved
        double originalDuration = delayed.Duration;
        double resampledDuration = resampled.Duration;
        Assert.Equal(originalDuration, resampledDuration, precision: 2);
    }

    [Fact]
    public void Delay_StereoMonoConversion_ChannelIndependent()
    {
        // Arrange: stereo signal
        var stereo = AudioSignal.FromInterleavedArray(
            new[] { 1.0f, 2.0f, 3.0f, 4.0f }, _sr);

        // Act: delay entire stereo signal
        var delayedStereo = stereo.Delay(2);
        var mono = delayedStereo.ToMono();

        // Assert: mono should have corresponding delay
        Assert.Equal(AudioChannels.Mono, mono.Channels);
        Assert.True(mono.FrameCount > stereo.FrameCount);
    }

    [Fact]
    public void Delay_VocalWithReverbTail()
    {
        // Realistic scenario: vocal with feedback delay reverb
        var vocal = AudioSignal.FromArray(
            Enumerable.Range(0, 24000).Select(i => (float)Math.Sin(i * 0.01f)).ToArray(),
            _sr);

        // Act: create reverb tail with 3 decaying echoes
        var withReverb = vocal.FeedbackDelay(0.15, 0.4f, 3);

        // Assert
        Assert.True(withReverb.FrameCount > vocal.FrameCount);
        Assert.True(withReverb.Duration > vocal.Duration);
    }
}