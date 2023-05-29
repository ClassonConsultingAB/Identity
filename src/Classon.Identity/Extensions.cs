using Azure.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Classon.Identity;

public static class CachingTokenCredentialExtensions
{
    /// <summary>
    /// Adds a TokenCredential as a singleton to the services. The TokenCredential is decorated with a caching behavior
    /// which automatically fetches new access tokens whenever a cached access token is used close to its expiry.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="credential">Represents a credential capable of providing an OAuth token.</param>
    /// <param name="accessTokenCache">Makes it possible to inject a previous cache, which is useful for increasing
    /// performance in automatic tests.</param>
    /// <param name="countAsNearExpirationFactor">A value between or equal to 0.0 or 1.0, describing how near an access
    /// token's expiry a new token should be fetched proactively.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static IServiceCollection AddCachingTokenCredential(
        this IServiceCollection builder, TokenCredential credential,
        IAccessTokenCache? accessTokenCache = null, double countAsNearExpirationFactor = 0.1)
    {
        if (countAsNearExpirationFactor is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(countAsNearExpirationFactor));
        return builder
            .AddSingleton<ICachingTokenClock>(_ => new CachingTokenClock())
            .AddSingleton<TokenCredential>(sp =>
                CachingTokenCredential.Create(
                    credential, countAsNearExpirationFactor, accessTokenCache, sp.GetService<ICachingTokenClock>()));
    }
}

public static class TokenCredentialExtensions
{
    /// <summary>
    /// Gets an AccessToken for the specified set of scopes.
    /// </summary>
    /// <param name="credential">Represents a credential capable of providing an OAuth token.</param>
    /// <param name="scope">The scope required for the token.</param>
    /// <param name="cancellationToken">The CancellationToken to use.</param>
    /// <returns>A valid AccessToken.</returns>
    public static async ValueTask<string> GetTokenAsync(
        this TokenCredential credential, string scope, CancellationToken? cancellationToken = null)
    {
        var result = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), cancellationToken ?? CancellationToken.None)
            .ConfigureAwait(false);
        return result.Token;
    }
}
