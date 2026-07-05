using System;
using Classon.Identity.Specs.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Classon.Identity.Specs;

public class WorkerStoreSpecs
{
    private WorkerStore Sut { get; }

    public WorkerStoreSpecs()
    {
        var fakeClock = new FakeTimeProvider();
        Sut = new WorkerStore(
            new FakeCredential(fakeClock), 0.0, new InMemoryAccessTokenCache(), fakeClock);
    }

    private static string GenerateKey() => Guid.NewGuid().ToString();

    [Fact]
    public void GivenSameKey_ShouldReturnSameWorker()
    {
        var key1 = GenerateKey();
        var worker = Sut.GetOrCreateWorker(key1);
        Assert.Same(worker, Sut.GetOrCreateWorker(key1));
    }

    [Fact]
    public void GivenDifferentKeys_ShouldReturnDifferentWorkers()
    {
        var worker = Sut.GetOrCreateWorker(GenerateKey());
        Assert.NotSame(worker, Sut.GetOrCreateWorker(GenerateKey()));
    }
}