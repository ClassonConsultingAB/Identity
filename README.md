# Classon.Identity

A TokenCredential decorator with caching behavior.

Calling `AddCachingTokenCredential(...)` adds a `TokenCredential` singleton to the service collection.

_Note that Microsoft already has built caching support for the `ManagedIdentityCredential` of NuGet package `Azure.Identity`._

## Automatic Renewal

When a cached token is retrieved that is near its expiry, a new token is automatically fetched on a background thread.

This makes renewal happen only for tokens that are used often. By default, a token is considered to be near its expiry when it has only got 10% left of its lifetime. For example, if a token is valid for 60 minutes, automatic renewal will happen if a cached token is used after 54 minutes.

## TL;DR;

With ASP.NET dependency injection:

```csharp
var services = new ServiceCollection();
services.AddCachingTokenCredential(new DefaultAzureCredential());
var sp = services.BuildServiceProvider();
var credential = sp.GetRequiredService<TokenCredential>();

const string scope = "...";
// Token retrieved from IDP.
var token1 = await credential.GetTokenAsync(scope);
// Token retrieved from cache.
var token2 = await credential.GetTokenAsync(scope);
```

Direct:

```csharp
var credential = CachingTokenCredential.Create(new DefaultAzureCredential());
// Token retrieved from IDP.
var token1 = await credential.GetTokenAsync(scope);
// Token retrieved from cache.
var token2 = await credential.GetTokenAsync(scope);
```
