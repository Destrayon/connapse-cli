using System.Text.Json;
using System.Text.Json.Serialization;
using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// Reads and writes ~/.connapse/config.json (non-secret preferences).
/// Kept separate from CredentialStore so the secrets file can have tighter
/// permissions without affecting preferences.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public ConfigStore(string baseDirectory)
    {
        _filePath = Path.Combine(baseDirectory, ".connapse", "config.json");
    }

    public CliConfig Load()
    {
        if (!File.Exists(_filePath))
            return new CliConfig();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
        }
        catch (JsonException)
        {
            return new CliConfig();
        }
    }

    public void Save(CliConfig config)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void SetDefaultOrg(string slug)
    {
        var current = Load();
        Save(current with { DefaultOrgSlug = slug });
    }

    public void ClearDefaultOrg()
    {
        var current = Load();
        Save(current with { DefaultOrgSlug = null });
    }

    public void SetEdition(ServerEdition edition)
    {
        var current = Load();
        Save(current with { Edition = edition, EditionCheckedAt = DateTime.UtcNow });
    }
}
