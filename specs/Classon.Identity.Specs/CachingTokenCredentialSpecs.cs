using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Classon.Identity.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Classon.Identity.Specs;

public partial class CachingTokenCredentialSpecs
{
    [Theory, InlineData(AccessMode.Async), InlineData(AccessMode.Sync)]
    public async Task GivenNoCache_ShouldRequestNew(AccessMode mode)
    {
        SelectedMode = mode;
        var fakeToken = FakeCredential.CreateFakeToken();
        FakeCredential.RegisterScope(Scope1, () => fakeToken);
        var accessToken = await RequestAccessTokenAsync(Scope1);
        Assert.Equal(fakeToken.Token, accessToken);
    }

    [Theory, InlineData(AccessMode.Async), InlineData(AccessMode.Sync)]
    public async Task GivenCache_ShouldNotRequestNew(AccessMode mode)
    {
        SelectedMode = mode;
        var token1 = await RequestAccessTokenAsync(Scope1);
        var token2 = await RequestAccessTokenAsync(Scope1);
        Assert.Equal(token1, token2);
        Assert.Equal(1, FakeCredential.NumberOfRequests);
    }

    [Theory, InlineData(AccessMode.Async), InlineData(AccessMode.Sync)]
    public async Task GivenMultipleConcurrentCalls_ShouldOnlyRequestOnce(AccessMode mode)
    {
        SelectedMode = mode;
        FakeCredential.SetPerformanceOverhead(TimeSpan.FromMilliseconds(10));
        await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ =>
                Task.Run(() => RequestAccessTokenAsync(Scope1))));
        Assert.Equal(1, FakeCredential.NumberOfRequests);
    }

    [Theory, InlineData(AccessMode.Async), InlineData(AccessMode.Sync)]
    public async Task GivenDifferentScopes_ShouldRequestNew(AccessMode mode)
    {
        SelectedMode = mode;
        var accessToken1 = await RequestAccessTokenAsync(Scope1);
        var accessToken2 = await RequestAccessTokenAsync(Scope2);
        Assert.NotEqual(accessToken2, accessToken1);
    }

    [Theory, InlineData(AccessMode.Async), InlineData(AccessMode.Sync)]
    public async Task GivenTimeAdvancement_ShouldRequestNewWhenInvalid(AccessMode mode)
    {
        SelectedMode = mode;
        var accessToken1 = await RequestAccessTokenAsync(Scope1);
        FakeClock.Advance(TimeSpan.FromMinutes(60));
        var accessToken2 = await RequestAccessTokenAsync(Scope1);
        Assert.NotEqual(accessToken1, accessToken2);
    }

    [Theory, InlineData(AccessMode.Async), InlineData(AccessMode.Sync)]
    public async Task GivenTimeAdvancement_ShouldNotRequestNewWhenStillValid(AccessMode mode)
    {
        SelectedMode = mode;
        var accessToken1 = await RequestAccessTokenAsync(Scope1);
        FakeClock.Advance(TimeSpan.FromMinutes(60).Subtract(TimeSpan.FromTicks(1)));
        var accessToken2 = await RequestAccessTokenAsync(Scope1);
        Assert.Equal(accessToken1, accessToken2);
    }

    [Theory]
    [InlineData(54.00000000000000, SilentRenew.Yes, AccessMode.Async)]
    [InlineData(54.00000000000000, SilentRenew.Yes, AccessMode.Sync)]
    [InlineData(53.99999999999999, SilentRenew.No, AccessMode.Async)]
    [InlineData(53.99999999999999, SilentRenew.No, AccessMode.Sync)]
    public async Task GivenTimeAdvancement_ShouldRequestNewWhenNearlyExpired(
        double advancementInMinutes, SilentRenew expectedRenew, AccessMode mode)
    {
        SelectedMode = mode;
        // First request
        var accessToken1 = await RequestAccessTokenAsync(Scope1);

        // Second request
        FakeClock.Advance(TimeSpan.FromMinutes(advancementInMinutes));
        var accessToken2 = await RequestAccessTokenAsync(Scope1);
        // The old token should have been retrieved from cache
        Assert.Equal(accessToken1, accessToken2);

        // Third request
        await Task.Delay(100); // Wait so that background renew have time to complete
        var accessToken3 = await RequestAccessTokenAsync(Scope1);
        if (expectedRenew == SilentRenew.Yes)
            // It should have been renewed in background
            Assert.NotEqual(accessToken1, accessToken3);
        else
            Assert.Equal(accessToken1, accessToken3);
    }

    [Fact]
    public async Task GivenCancellationToken_ShouldBeAbleToAbort()
    {
        using var cts = new CancellationTokenSource();
        FakeCredential.SetPerformanceOverhead(TimeSpan.FromSeconds(10));
        var t = Assert.ThrowsAsync<TaskCanceledException>(() =>
            Sut.GetTokenAsync(string.Empty, cts.Token).AsTask());
        await cts.CancelAsync();
        await t;
    }

    [Fact]
    public async Task GivenNoRegisteredTimeProvider_ShouldFallBackToSystemClock()
    {
        var services = new ServiceCollection();
        services.AddCachingTokenCredential(FakeCredential);
        var credential = services.BuildServiceProvider().GetRequiredService<TokenCredential>();
        var accessToken = await credential.GetTokenAsync(Scope1);
        Assert.False(string.IsNullOrEmpty(accessToken));
    }

    [Theory, Repeat(20)]
    public async Task GivenTimeAdvancement_ShouldRequestNewWhenNearlyExpiredOnlyOnce(int i)
    {
        if (i == int.MinValue) // Remove warning that i is not used
            return;

        SelectedMode = AccessMode.Async;

        // First request
        await RequestAccessTokenAsync(Scope1);
        Assert.Equal(1, FakeCredential.NumberOfRequests);

        // Multiple requests
        FakeClock.Advance(TimeSpan.FromMinutes(59));
        FakeCredential.SetPerformanceOverhead(TimeSpan.FromMilliseconds(10));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => RequestAccessTokenAsync(Scope1)));

        await Task.Delay(40); // Wait so that background renew have time to complete
        Assert.Equal(2, FakeCredential.NumberOfRequests);
    }

    [Theory, InlineData(AccessMode.Sync), InlineData(AccessMode.Async)]
    public async Task GivenFileSystemCache_ShouldCacheWhenRestarted(AccessMode mode)
    {
        SelectedMode = mode;
        SelectedCache = Cache.FileSystem;
        var accessToken1 = await RequestAccessTokenAsync(Scope1);
        _sut = null;
        var accessToken2 = await RequestAccessTokenAsync(Scope1);
        Assert.Equal(accessToken1, accessToken2);
    }

    [Theory, InlineData(AccessMode.Sync), InlineData(AccessMode.Async)]
    public async Task GivenFileSystemCache_ShouldAlsoCacheInMem(AccessMode mode)
    {
        SelectedMode = mode;
        SelectedCache = Cache.FileSystem;
        var accessToken1 = await RequestAccessTokenAsync(Scope1);
        ClearCacheFiles();
        var accessToken2 = await RequestAccessTokenAsync(Scope1);
        Assert.Equal(accessToken1, accessToken2);
    }

    [Theory, InlineData(AccessMode.Sync), InlineData(AccessMode.Async)]
    public async Task GivenFileSystemCache_ShouldHandleCachedNullData(AccessMode mode)
    {
        SelectedMode = mode;
        SelectedCache = Cache.FileSystem;
        var accessToken1 = await RequestAccessTokenAsync(Scope1);

        var file = Directory.EnumerateFiles(AccessTokenCachePath).Single();
        await File.WriteAllTextAsync(file, "null");

        _sut = null;
        var accessToken2 = await RequestAccessTokenAsync(Scope1);
        Assert.NotEqual(accessToken1, accessToken2);
    }

}

public sealed partial class CachingTokenCredentialSpecs : IDisposable
{
    private const string Scope1 = "some-scope-1";
    private const string Scope2 = "some-scope-2";

    public CachingTokenCredentialSpecs()
    {
        FakeClock = new FakeTimeProvider();
        FakeCredential = new FakeCredential(FakeClock);
        FakeCredential.RegisterScope(Scope1);
        FakeCredential.RegisterScope(Scope2);
        AccessTokenCachePath =
            Path.Join(
                Path.Join(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".test-access-token-cache"),
                Guid.NewGuid().ToString());
    }

    private TokenCredential BuildSut()
    {
        var services = new ServiceCollection();
        var cache = SelectedCache == Cache.FileSystem
            ? new LocalFileSystemAccessTokenCache(AccessTokenCachePath)
            : null;
        services
            .AddCachingTokenCredential(FakeCredential, cache)
            .AddSingleton<TimeProvider>(FakeClock);
        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<TokenCredential>();
    }

    public enum SilentRenew
    {
        Yes,
        No
    }

    public enum AccessMode
    {
        Async,
        Sync
    }

    public enum Cache
    {
        InMem,
        FileSystem
    }

    private FakeCredential FakeCredential { get; }
    private FakeTimeProvider FakeClock { get; }
    private AccessMode? SelectedMode { get; set; }
    private Cache SelectedCache { get; set; } = Cache.InMem;
    private TokenCredential? _sut;

    private readonly object _mutex = new ();
    private TokenCredential Sut
    {
        get
        {
            lock (_mutex)
            {
                return _sut ??= BuildSut();
            }
        }
    }

    private string AccessTokenCachePath { get; }

    private async Task<string> RequestAccessTokenAsync(string scope)
    {
        if (SelectedMode is null)
            throw new InvalidOperationException("Mode needs to be set");

        if (SelectedMode == AccessMode.Async)
            return await Sut.GetTokenAsync(scope);

        var requestContext = new TokenRequestContext(new[] { scope });
        // ReSharper disable once MethodHasAsyncOverload - We intend to also test sync overload
        return Sut.GetToken(requestContext, CancellationToken.None).Token;
    }

    public void Dispose()
    {
        if (!Directory.Exists(AccessTokenCachePath))
            return;
        ClearCacheFiles();
    }

    private void ClearCacheFiles()
    {
        try
        {
            Directory.Delete(AccessTokenCachePath, true);
        }
        catch (IOException)
        {
            Thread.Sleep(100);
            Directory.Delete(AccessTokenCachePath, true);
        }
    }
}
