using System.Text.Json;
using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// Reads and writes ~/.connapse/credentials.json. The base directory is
/// injected to make the store testable without touching the real home
/// directory.
/// </summary>
public sealed class CredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;

    public CredentialStore(string baseDirectory)
    {
        _filePath = Path.Combine(baseDirectory, ".connapse", "credentials.json");
    }

    public CliCredentials? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<CliCredentials>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(CliCredentials credentials)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(credentials, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void Delete()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
