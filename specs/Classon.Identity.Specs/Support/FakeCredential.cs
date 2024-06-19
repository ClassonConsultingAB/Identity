using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Classon.Identity.Specs.Support;

internal class FakeCredential : TokenCredential
{
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Func<AccessToken>> _scopeToAccessToken = new();
    public int NumberOfRequests { get; private set; }
    private TimeSpan? _overhead;

    public FakeCredential(TimeProvider clock)
    {
        _clock = clock;
    }

    public AccessToken CreateFakeToken(TimeSpan? validity = null) =>
        new(Guid.NewGuid().ToString(), _clock.GetUtcNow().Add(validity ?? TimeSpan.FromHours(1)));

    public void RegisterScope(string scope, Func<AccessToken>? getAccessToken = null)
    {
        _scopeToAccessToken[scope] = getAccessToken ?? (() => CreateFakeToken());
    }

    private async ValueTask<AccessToken> GetFakeTokenAsync(TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        if (_overhead != null) await Task.Delay(_overhead.Value, cancellationToken);
        NumberOfRequests++;
        return _scopeToAccessToken[requestContext.Scopes.Single()]();
    }

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetFakeTokenAsync(requestContext, cancellationToken);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetFakeTokenAsync(requestContext, cancellationToken).AsTask().Result;

    public void SetPerformanceOverhead(TimeSpan overhead) => _overhead = overhead;
}
