namespace VoicePro.Domain.Exceptions;

/// <summary>
/// Base exception for all domain-level validation errors in VoicePro.
/// Thrown when audio data or DSP parameters violate domain constraints.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when an AudioSignal is constructed with invalid parameters:
/// — empty sample buffer
/// — odd-length stereo data (must be interleaved pairs)
/// — mismatched frame counts or sample rates during mixing
/// — incompatible channel layouts
/// </summary>
public class InvalidAudioSignalException : DomainException
{
    public InvalidAudioSignalException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a SampleRate is requested with a non-standard value.
/// Only these rates are supported in DSP pipelines:
/// 8000, 16000, 22050, 44100, 48000, 96000 Hz
/// </summary>
public class InvalidSampleRateException : DomainException
{
    /// <summary>Standard audio sample rates supported by the library.</summary>
    public static readonly HashSet<int> SupportedRates = new()
    {
        8_000,
        16_000,
        22_050,
        44_100,
        48_000,
        96_000
    };

    public int RequestedRate { get; }

    public InvalidSampleRateException(int requestedRate)
        : base($"Sample rate {requestedRate} Hz is not supported. " +
               $"Supported rates: {string.Join(", ", SupportedRates.OrderBy(r => r))} Hz")
    {
        RequestedRate = requestedRate;
    }
}

/// <summary>
/// Thrown when a window function (e.g., Hann) is created with invalid parameters:
/// — size not a power of 2
/// — size &lt; 2
/// </summary>
public class InvalidWindowException : DomainException
{
    public int RequestedSize { get; }

    public InvalidWindowException(int size)
        : base($"Window size must be a power of 2 ≥ 2, but got {size}.")
    {
        RequestedSize = size;
    }

    public InvalidWindowException(string message, int requestedSize)
        : base(message)
    {
        RequestedSize = requestedSize;
    }
}

/// <summary>
/// Thrown when a resampling operation fails due to:
/// — target sample rate not supported or invalid
/// — resampling ratio is extreme (e.g., >10× upsampling without quality loss)
/// — insufficient samples for interpolation
/// </summary>
public class ResamplingException : DomainException
{
    public int SourceRate { get; }
    public int TargetRate { get; }
    public double ResamplingRatio { get; }

    public ResamplingException(int sourceRate, int targetRate, string message)
        : base(message)
    {
        SourceRate = sourceRate;
        TargetRate = targetRate;
        ResamplingRatio = (double)targetRate / sourceRate;
    }

    public ResamplingException(int sourceRate, int targetRate)
        : base($"Cannot resample from {sourceRate} Hz to {targetRate} Hz. " +
               $"Supported resampling ratio: 0.1× to 10×")
    {
        SourceRate = sourceRate;
        TargetRate = targetRate;
        ResamplingRatio = (double)targetRate / sourceRate;
    }
}