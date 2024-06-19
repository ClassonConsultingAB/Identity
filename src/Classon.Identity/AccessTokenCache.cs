using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;

namespace Classon.Identity;

/// <summary>
/// Makes it possible to inject a shared cache, which is useful for increasing performance in automatic tests.
/// </summary>
public interface IAccessTokenCache
{
    AccessTokenCacheEntry? TryGetValue(string key);
    ValueTask<AccessTokenCacheEntry?> TryGetValueAsync(string key);
    void SetValue(string key, AccessTokenCacheEntry value);
    ValueTask SetValueAsync(string key, AccessTokenCacheEntry value);
}

public record AccessTokenCacheEntry(
    TimeSpan ValidRange, AccessToken AccessToken, TokenRequestContext RequestContext);

public class InMemoryAccessTokenCache : IAccessTokenCache
{
    private readonly ConcurrentDictionary<string, AccessTokenCacheEntry> _accessTokenCache = new();

    public AccessTokenCacheEntry? TryGetValue(string key) =>
        _accessTokenCache.GetValueOrDefault(key);

    public ValueTask<AccessTokenCacheEntry?> TryGetValueAsync(string key)
    {
        return ValueTask.FromResult(TryGetValue(key));
    }

    public void SetValue(string key, AccessTokenCacheEntry value) =>
        _accessTokenCache[key] = value;

    public ValueTask SetValueAsync(string key, AccessTokenCacheEntry value)
    {
        SetValue(key, value);
        return ValueTask.CompletedTask;
    }
}

public class LocalFileSystemAccessTokenCache : IAccessTokenCache
{
    private readonly InMemoryAccessTokenCache _inMemCache = new();
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _rootDir;

    public LocalFileSystemAccessTokenCache(string? rootDir = null)
    {
        _rootDir = !string.IsNullOrEmpty(rootDir)
            ? rootDir
            : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".access-token-cache");
    }

    private static async Task<string> LoadAsync(string path)
    {
        try
        {
            await _semaphore.WaitAsync();
            return await File.ReadAllTextAsync(path);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static string Load(string path)
    {
        try
        {
            _semaphore.Wait();
            return File.ReadAllText(path);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task SaveAsync(string path, string json)
    {
        try
        {
            await _semaphore.WaitAsync();
            await File.WriteAllTextAsync(path, json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static void Save(string path, string json)
    {
        try
        {
            _semaphore.Wait();
            File.WriteAllText(path, json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string FormatCachePath(string key)
    {
        if (!Directory.Exists(_rootDir))
            Directory.CreateDirectory(_rootDir);
        var sanitizedKey = ReplaceIllegalChars(key);
        return Path.Join(_rootDir, $"{sanitizedKey}.json");
    }

    private static string ReplaceIllegalChars(string value) =>
        Path.GetInvalidFileNameChars()
            .Aggregate(value, (current, c) => current.Replace(c.ToString(), "_"));

    private static readonly JsonSerializerOptions _serializerOptions = new ()
    {
        Converters = { new AccessTokenConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static AccessTokenCacheEntry? DeserializeEntry(string json) =>
        JsonSerializer.Deserialize<AccessTokenCacheEntry>(json, _serializerOptions);

    private static string SerializeEntry(AccessTokenCacheEntry value) =>
        JsonSerializer.Serialize(value, _serializerOptions);

    public AccessTokenCacheEntry? TryGetValue(string key)
    {
        var entry = _inMemCache.TryGetValue(key);
        if (entry != null)
            return entry;
        var path = FormatCachePath(key);
        if (!File.Exists(path))
            return null;
        var json = Load(path);
        var restoredValue = DeserializeEntry(json);
        if (restoredValue == null)
            return null;
        _inMemCache.SetValue(key, restoredValue);
        return restoredValue;
    }

    public async ValueTask<AccessTokenCacheEntry?> TryGetValueAsync(string key)
    {
        var entry = await _inMemCache.TryGetValueAsync(key);
        if (entry != null)
            return entry;
        var path = FormatCachePath(key);
        if (!File.Exists(path))
            return null;
        var json = await LoadAsync(path);
        var restoredValue = DeserializeEntry(json);
        if (restoredValue == null)
            return null;
        await _inMemCache.SetValueAsync(key, restoredValue);
        return restoredValue;
    }

    public void SetValue(string key, AccessTokenCacheEntry value)
    {
        _inMemCache.SetValue(key, value);
        var path = FormatCachePath(key);
        var json = SerializeEntry(value);
        Save(path, json);
    }

    public async ValueTask SetValueAsync(string key, AccessTokenCacheEntry value)
    {
        await _inMemCache.SetValueAsync(key, value);
        var path = FormatCachePath(key);
        var json = SerializeEntry(value);
        await SaveAsync(path, json);
    }
}

internal class AccessTokenConverter : JsonConverter<AccessToken>
{
    // https://github.com/Azure/azure-sdk-for-net/issues/9460

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private record AccessTokenModel(string Token, DateTimeOffset ExpiresOn);

    public override AccessToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var model = JsonSerializer.Deserialize<AccessTokenModel>(ref reader, _jsonSerializerOptions)!;
        return new AccessToken(model.Token, model.ExpiresOn);
    }

    public override void Write(Utf8JsonWriter writer, AccessToken value, JsonSerializerOptions options)
    {
        var model = new AccessTokenModel(value.Token, value.ExpiresOn);
        JsonSerializer.Serialize(writer, model, _jsonSerializerOptions);
    }
}

