using System;
using System.IO;
using System.Linq;
using Azure.Core;
using Xunit;

namespace Classon.Identity.Specs;

public class LocalFileSystemAccessTokenCacheSpecs : IDisposable
{
    private static readonly string DefaultCacheDir = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".access-token-cache");

    [Fact]
    public void GivenNoRootDir_ShouldPersistUnderDefaultUserProfilePath()
    {
        var cache = new LocalFileSystemAccessTokenCache();
        var key = Guid.NewGuid().ToString();
        var entry = new AccessTokenCacheEntry(
            TimeSpan.FromHours(1), new AccessToken("some-token", DateTimeOffset.UtcNow.AddHours(1)),
            new TokenRequestContext(["some-scope"]));

        cache.SetValue(key, entry);

        var expectedFile = Path.Join(DefaultCacheDir, $"{key}.json");
        Assert.True(File.Exists(expectedFile));
    }

    public void Dispose()
    {
        if (!Directory.Exists(DefaultCacheDir))
            return;
        foreach (var file in Directory.EnumerateFiles(DefaultCacheDir))
            File.Delete(file);
        if (!Directory.EnumerateFileSystemEntries(DefaultCacheDir).Any())
            Directory.Delete(DefaultCacheDir);
    }
}
