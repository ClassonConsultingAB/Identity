using System;

namespace Classon.Identity.Specs.Support;

public class FakeClock : ICachingTokenClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2022, 01, 02, 12, 00, 00, TimeSpan.Zero);
}
