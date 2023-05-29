namespace Classon.Identity;

/// <summary>
/// Abstraction for making automatic testing possible.
/// </summary>
public interface ICachingTokenClock
{
    DateTimeOffset UtcNow { get; }
}

internal class CachingTokenClock : ICachingTokenClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
