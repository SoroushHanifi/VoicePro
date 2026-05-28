namespace VoicePro.Domain.Exceptions;

/// <summary>
/// Base class for all VoicePro domain exceptions.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a SampleRate value is invalid or not in the supported standard set.
/// </summary>
public sealed class InvalidSampleRateException : DomainException
{
    public int AttemptedValue { get; }

    public InvalidSampleRateException(int value)
        : base($"Sample rate '{value}' Hz is not supported. " +
               $"Supported rates: {string.Join(", ", SupportedRates.Select(r => $"{r / 1000.0:G}k"))} Hz.")
    {
        AttemptedValue = value;
    }

    internal static readonly int[] SupportedRates = [8000, 16000, 22050, 44100, 48000, 96000];
}

/// <summary>
/// Thrown when an AudioSignal is constructed with invalid data.
/// </summary>
public sealed class InvalidAudioSignalException : DomainException
{
    public InvalidAudioSignalException(string message) : base(message) { }
}