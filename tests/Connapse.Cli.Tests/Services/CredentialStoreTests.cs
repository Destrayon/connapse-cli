using Connapse.Cli.Models;
using Connapse.Cli.Services;

namespace Connapse.Cli.Tests.Services;

public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CredentialStore _store;

    public CredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid());
        _store = new CredentialStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_returns_null_when_file_does_not_exist()
    {
        var result = _store.Load();
        Assert.Null(result);
    }

    [Fact]
    public void Save_creates_file_and_Load_returns_same_values()
    {
        var creds = new CliCredentials(
            ApiKey: "test-token-abc",
            ApiBaseUrl: "https://cloud.connapse.io",
            UserEmail: "alice@example.com",
            RefreshToken: "refresh-xyz",
            ExpiresAt: new DateTime(2026, 4, 12, 13, 0, 0, DateTimeKind.Utc));

        _store.Save(creds);
        var loaded = _store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("test-token-abc", loaded!.ApiKey);
        Assert.Equal("https://cloud.connapse.io", loaded.ApiBaseUrl);
        Assert.Equal("alice@example.com", loaded.UserEmail);
        Assert.Equal("refresh-xyz", loaded.RefreshToken);
        Assert.Equal(new DateTime(2026, 4, 12, 13, 0, 0, DateTimeKind.Utc), loaded.ExpiresAt);
    }

    [Fact]
    public void Save_creates_parent_directory_if_missing()
    {
        var creds = new CliCredentials("k", "https://x", "e@x");
        _store.Save(creds);
        Assert.True(File.Exists(Path.Combine(_tempDir, ".connapse", "credentials.json")));
    }

    [Fact]
    public void Load_returns_null_when_file_is_corrupt()
    {
        var path = Path.Combine(_tempDir, ".connapse", "credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");

        var result = _store.Load();
        Assert.Null(result);
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        _store.Save(new CliCredentials("k", "https://x", "e@x"));
        _store.Delete();
        Assert.Null(_store.Load());
    }

    [Fact]
    public void Delete_is_idempotent_when_file_does_not_exist()
    {
        // Should not throw
        _store.Delete();
    }

    [Fact]
    public void Save_overwrites_existing_file()
    {
        _store.Save(new CliCredentials("k1", "https://x", "e1@x"));
        _store.Save(new CliCredentials("k2", "https://x", "e2@x"));
        var loaded = _store.Load();
        Assert.Equal("k2", loaded!.ApiKey);
        Assert.Equal("e2@x", loaded.UserEmail);
    }
}
