using Connapse.Cli.Models;
using Connapse.Cli.Services;

namespace Connapse.Cli.Tests.Services;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigStore _store;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid());
        _store = new ConfigStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_returns_empty_config_when_file_missing()
    {
        var config = _store.Load();
        Assert.NotNull(config);
        Assert.Null(config.ApiBaseUrl);
        Assert.Null(config.DefaultOrgSlug);
        Assert.Null(config.Edition);
        Assert.Null(config.EditionCheckedAt);
    }

    [Fact]
    public void Save_and_Load_roundtrip_preserves_all_fields()
    {
        var now = new DateTime(2026, 4, 12, 12, 0, 0, DateTimeKind.Utc);
        var config = new CliConfig
        {
            ApiBaseUrl = "https://www.connapse.com",
            DefaultOrgSlug = "khastra",
            Edition = ServerEdition.Cloud,
            EditionCheckedAt = now,
        };

        _store.Save(config);
        var loaded = _store.Load();

        Assert.Equal("https://www.connapse.com", loaded.ApiBaseUrl);
        Assert.Equal("khastra", loaded.DefaultOrgSlug);
        Assert.Equal(ServerEdition.Cloud, loaded.Edition);
        Assert.Equal(now, loaded.EditionCheckedAt);
    }

    [Fact]
    public void Load_returns_empty_config_when_file_corrupt()
    {
        var path = Path.Combine(_tempDir, ".connapse", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var config = _store.Load();
        Assert.NotNull(config);
        Assert.Null(config.DefaultOrgSlug);
    }

    [Fact]
    public void SetDefaultOrg_preserves_other_fields()
    {
        _store.Save(new CliConfig
        {
            ApiBaseUrl = "https://www.connapse.com",
            Edition = ServerEdition.Cloud,
        });

        _store.SetDefaultOrg("khastra");

        var loaded = _store.Load();
        Assert.Equal("khastra", loaded.DefaultOrgSlug);
        Assert.Equal("https://www.connapse.com", loaded.ApiBaseUrl);
        Assert.Equal(ServerEdition.Cloud, loaded.Edition);
    }

    [Fact]
    public void ClearDefaultOrg_sets_slug_to_null()
    {
        _store.Save(new CliConfig { DefaultOrgSlug = "khastra" });
        _store.ClearDefaultOrg();
        Assert.Null(_store.Load().DefaultOrgSlug);
    }

    [Fact]
    public void SetEdition_records_checked_at_timestamp()
    {
        var before = DateTime.UtcNow;
        _store.SetEdition(ServerEdition.Cloud);
        var after = DateTime.UtcNow;

        var loaded = _store.Load();
        Assert.Equal(ServerEdition.Cloud, loaded.Edition);
        Assert.NotNull(loaded.EditionCheckedAt);
        Assert.InRange(loaded.EditionCheckedAt!.Value, before, after);
    }
}
