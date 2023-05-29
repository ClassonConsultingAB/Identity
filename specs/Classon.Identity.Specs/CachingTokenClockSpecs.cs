using System;
using Classon.Identity.Specs.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Classon.Identity.Specs;

public class CachingTokenClockSpecs
{
    [Fact]
    public void GivenClock_ShouldReturnCurrentTime()
    {
        var sut = new ServiceCollection()
            .AddCachingTokenCredential(new FakeCredential(new FakeClock()))
            .BuildServiceProvider()
            .GetRequiredService<ICachingTokenClock>();
        var diff = sut.UtcNow - DateTimeOffset.UtcNow;
        diff.Should().BeLessThan(TimeSpan.FromMilliseconds(1));
    }
}