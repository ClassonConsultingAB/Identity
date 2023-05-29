using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Azure.Core;

namespace Classon.Identity;

public class CachingTokenCredential : TokenCredential
{
    private readonly WorkerStore _workerStore;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public static CachingTokenCredential Create(
        TokenCredential credential, double countAsNearExpirationFactor = 0.1,
        IAccessTokenCache? accessTokenCache = null, ICachingTokenClock? clock = null) =>
        new(credential, countAsNearExpirationFactor,
            accessTokenCache ?? new InMemoryAccessTokenCache(),
            clock ?? new CachingTokenClock());

    private CachingTokenCredential(
        TokenCredential credential, double countAsNearExpirationFactor, IAccessTokenCache accessTokenCache,
        ICachingTokenClock clock)
    {
        _workerStore = new WorkerStore(credential, countAsNearExpirationFactor, accessTokenCache, clock);
    }

    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var worker = await GetWorkerAsync(requestContext, cancellationToken);
        return await worker.GetTokenAsync(requestContext, cancellationToken);
    }

    public override AccessToken GetToken(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var worker = GetWorker(requestContext, cancellationToken);
        return worker.GetToken(requestContext, cancellationToken);
    }

    private async ValueTask<CachingTokenCredentialWorker> GetWorkerAsync(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var key = requestContext.FormatCacheKey();
        if (_workerStore.TryGetValue(key, out var w1))
            return w1;
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _workerStore.GetOrCreateWorker(key);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private CachingTokenCredentialWorker GetWorker(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var key = requestContext.FormatCacheKey();
        if (_workerStore.TryGetValue(key, out var w))
            return w;
        _semaphore.Wait(cancellationToken);
        try
        {
            return _workerStore.GetOrCreateWorker(key);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

internal class WorkerStore
{
    private readonly ConcurrentDictionary<string, CachingTokenCredentialWorker> _workers = new();
    private readonly Func<string, CachingTokenCredentialWorker> CreateWorker;

    public WorkerStore(
        TokenCredential credential, double countAsNearExpirationFactor, IAccessTokenCache accessTokenCache,
        ICachingTokenClock clock)
    {
        CreateWorker = key => new CachingTokenCredentialWorker(
            key, credential, countAsNearExpirationFactor, accessTokenCache, clock);
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out CachingTokenCredentialWorker worker) =>
        _workers.TryGetValue(key, out worker);

    public CachingTokenCredentialWorker GetOrCreateWorker(string key)
    {
        if (_workers.TryGetValue(key, out var w))
            return w;
        var worker = CreateWorker(key);
        _workers[key] = worker;
        return worker;
    }
}

internal class CachingTokenCredentialWorker
{
    private readonly string _key;
    private readonly TokenCredential _credential;
    private readonly ICachingTokenClock _clock;
    private readonly double _countAsNearExpirationFactor;
    private readonly IAccessTokenCache _accessTokenCache;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public CachingTokenCredentialWorker(string key, TokenCredential credential,
        double countAsNearExpirationFactor, IAccessTokenCache accessTokenCache, ICachingTokenClock clock)
    {
        _key = key;
        _credential = credential;
        _countAsNearExpirationFactor = countAsNearExpirationFactor;
        _accessTokenCache = accessTokenCache;
        _clock = clock;
    }

    public async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var t1 = await TryGetCachedAccessTokenWithAutomaticRenewalAsync(cancellationToken);
        if (t1 != null)
            return t1.Value;
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var t2 = await TryGetCachedAccessTokenWithAutomaticRenewalAsync(cancellationToken);
            if (t2 != null)
                return t2.Value;
            return await GetTokenAndUpdateCacheAsync(requestContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var t1 = TryGetCachedAccessTokenWithAutomaticRenewal(cancellationToken);
        if (t1 != null)
            return t1.Value;
        _semaphore.Wait(cancellationToken);
        try
        {

            var t2 = TryGetCachedAccessTokenWithAutomaticRenewal(cancellationToken);
            if (t2 != null)
                return t2.Value;
            return GetTokenAndUpdateCache(requestContext, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async ValueTask<AccessToken> GetTokenAndUpdateCacheAsync(TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await _credential.GetTokenAsync(requestContext, cancellationToken).ConfigureAwait(false);
        await UpdateCacheAsync(accessToken, requestContext);
        return accessToken;
    }

    private AccessToken GetTokenAndUpdateCache(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var accessToken = _credential.GetToken(requestContext, cancellationToken);
        UpdateCache(accessToken, requestContext);
        return accessToken;
    }

    private AccessToken? TryGetCachedAccessTokenWithAutomaticRenewal(CancellationToken cancellationToken)
    {
        var entry = TryGetNotExpiredCachedAccessToken();
        return OrchestrateUpdateCache(entry, cancellationToken);
    }

    private async ValueTask<AccessToken?> TryGetCachedAccessTokenWithAutomaticRenewalAsync(
        CancellationToken cancellationToken)
    {
        var entry = await TryGetNotExpiredCachedAccessTokenAsync();
        return OrchestrateUpdateCache(entry, cancellationToken);
    }

    private AccessToken? OrchestrateUpdateCache(AccessTokenCacheEntry? entry, CancellationToken cancellationToken)
    {
        if (entry == null)
            return null;
        if (IsNearExpiration(entry.AccessToken, entry.ValidRange))
            Task.Run(() =>
                TryUpdateCacheAsync(entry.RequestContext, entry.AccessToken, cancellationToken), cancellationToken);
        return entry.AccessToken;
    }

    private AccessTokenCacheEntry? TryGetNotExpiredCachedAccessToken()
    {
        var entry = _accessTokenCache.TryGetValue(_key);
        return MapExpiredAsNull(entry);
    }

    private AccessTokenCacheEntry? MapExpiredAsNull(AccessTokenCacheEntry? entry)
    {
        if (entry == null || IsExpired(entry.AccessToken))
            return null;
        return entry;
    }

    private async ValueTask<AccessTokenCacheEntry?> TryGetNotExpiredCachedAccessTokenAsync()
    {
        var entry  = await _accessTokenCache.TryGetValueAsync(_key);
        return MapExpiredAsNull(entry);
    }

    private bool IsNearExpiration(AccessToken accessToken, TimeSpan validRange) =>
        _clock.UtcNow >= accessToken.ExpiresOn.Subtract(validRange.Multiply(_countAsNearExpirationFactor));

    private bool IsExpired(AccessToken accessToken) =>
        _clock.UtcNow >= accessToken.ExpiresOn;

    private AccessToken? _lastUpdatedAccessToken;

    private async Task TryUpdateCacheAsync(
        TokenRequestContext requestContext, AccessToken oldToken, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastUpdatedAccessToken.HasValue && _lastUpdatedAccessToken.Value.Token != oldToken.Token)
                return;
            _lastUpdatedAccessToken = await GetTokenAndUpdateCacheAsync(requestContext, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async ValueTask UpdateCacheAsync(AccessToken accessToken, TokenRequestContext requestContext)
    {
        var entry = CreateCacheEntry(accessToken, requestContext);
        await _accessTokenCache.SetValueAsync(_key, entry);
    }

    private AccessTokenCacheEntry CreateCacheEntry(AccessToken accessToken, TokenRequestContext requestContext)
    {
        var validityRange = accessToken.ExpiresOn - _clock.UtcNow;
        return new AccessTokenCacheEntry(validityRange, accessToken, requestContext);
    }

    private void UpdateCache(AccessToken accessToken, TokenRequestContext requestContext)
    {
        var entry = CreateCacheEntry(accessToken, requestContext);
        _accessTokenCache.SetValue(_key, entry);
    }
}

internal static class TokenRequestContextExtensions
{
    public static string FormatCacheKey(this TokenRequestContext c) =>
        $"{c.Claims}-{c.TenantId}-{string.Join('-', c.Scopes)}";
}
