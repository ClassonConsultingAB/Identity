using System;
using Classon.Identity.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Classon.Identity.Specs;

public class CachingTokenCredentialInputValidationSpecs
{
    [Theory]
    [InlineData(0.0 - 1E-9, false)]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(1.0 + 1E-9, false)]
    public void GivenInput_ShouldBeValid(double factor, bool expectedOk)
    {
        if (expectedOk)
        {
            Action();
            return;
        }

        Assert.Throws<ArgumentOutOfRangeException>(Action);
        return;

        void Action() =>
            new ServiceCollection()
                .AddCachingTokenCredential(
                    new FakeCredential(new FakeTimeProvider()), countAsNearExpirationFactor: factor);
    }
}