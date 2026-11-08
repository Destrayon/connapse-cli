using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Services;

/// <summary>One saved login: a name and the directory its credentials.json and config.json live in.</summary>
public sealed record CliProfile(string Name, string Directory)
{
    public bool IsDefault => ProfileStore.IsDefaultName(Name);
}

/// <summary>
/// The CLI's home directory and the profiles in it. The default profile keeps its files at the top of the
/// home directory, where a single login has always lived, so setups from before profiles keep working.
/// A named profile lives in <c>profiles/&lt;name&gt;/</c>. <c>connapse auth switch</c> saves the profile
/// commands use when neither <c>--profile</c> nor <c>CONNAPSE_PROFILE</c> names one, in <c>active-profile</c>.
/// </summary>
public sealed partial class ProfileStore
{
    public const string DefaultName = "default";
    public const string HomeVariable = "CONNAPSE_HOME";
    public const string ProfileVariable = "CONNAPSE_PROFILE";

    private const string ProfilesFolder = "profiles";
    private const string ActiveFile = "active-profile";

    public ProfileStore(string homeDirectory)
    {
        HomeDirectory = homeDirectory;
    }

    /// <summary>Where the CLI keeps its files: <c>CONNAPSE_HOME</c> when set, else <c>~/.connapse</c>.</summary>
    public string HomeDirectory { get; }

    /// <summary>The home directory for this machine and environment.</summary>
    public static ProfileStore FromEnvironment()
    {
        var overridden = Environment.GetEnvironmentVariable(HomeVariable);
        return new ProfileStore(string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(HomeDirectoryResolver.GetHomeDirectory(), ".connapse")
            : Path.GetFullPath(overridden));
    }

    public static bool IsDefaultName(string name) =>
        string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The profile named <paramref name="name"/>, whether or not anything is saved in it yet.</summary>
    public CliProfile Get(string name)
    {
        if (IsDefaultName(name))
            return new CliProfile(DefaultName, HomeDirectory);
        if (!NamePattern().IsMatch(name))
            throw new CliValidationError(
                $"'{name}' is not a valid profile name. Use letters, digits, '.', '_' or '-', starting with a letter or digit.");
        return new CliProfile(name, Path.Combine(HomeDirectory, ProfilesFolder, name));
    }

    /// <summary>
    /// The profile a command uses: <paramref name="flag"/> (from <c>--profile</c>), else
    /// <paramref name="environment"/> (from <c>CONNAPSE_PROFILE</c>), else the one <c>auth switch</c> saved,
    /// else the default.
    /// </summary>
    public CliProfile Resolve(string? flag, string? environment)
    {
        if (!string.IsNullOrWhiteSpace(flag))
            return Get(flag.Trim());
        if (!string.IsNullOrWhiteSpace(environment))
            return Get(environment.Trim());
        return Get(LoadActive() ?? DefaultName);
    }

    /// <summary>The profile <c>auth switch</c> saved, or null when it saved none (or switched back to the default).</summary>
    public string? LoadActive()
    {
        var path = Path.Combine(HomeDirectory, ActiveFile);
        if (!File.Exists(path))
            return null;
        var name = File.ReadAllText(path).Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Makes <paramref name="profile"/> the one commands use when nothing else names one.</summary>
    public void SetActive(CliProfile profile)
    {
        var path = Path.Combine(HomeDirectory, ActiveFile);
        if (profile.IsDefault)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Directory.CreateDirectory(HomeDirectory);
        File.WriteAllText(path, profile.Name + Environment.NewLine);
    }

    /// <summary>The default profile, then every named profile folder, by name.</summary>
    public IReadOnlyList<CliProfile> List()
    {
        var profiles = new List<CliProfile> { Get(DefaultName) };
        var folder = Path.Combine(HomeDirectory, ProfilesFolder);
        if (Directory.Exists(folder))
        {
            profiles.AddRange(Directory.GetDirectories(folder)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => NamePattern().IsMatch(name) && !IsDefaultName(name))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new CliProfile(name, Path.Combine(folder, name))));
        }
        return profiles;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex NamePattern();
}

/// <summary>
/// The profile this run of the CLI uses. Program.Main tells it what <c>--profile</c> said once the command
/// line is parsed; the profile is worked out on first use, so <c>--help</c> never trips over a bad name.
/// </summary>
public sealed class ProfileContext(ProfileStore store, Func<string?> environment)
{
    private string? _flag;
    private CliProfile? _current;

    public ProfileContext(ProfileStore store)
        : this(store, () => Environment.GetEnvironmentVariable(ProfileStore.ProfileVariable))
    {
    }

    public ProfileStore Store { get; } = store;

    /// <summary>Records the <c>--profile</c> value; call before anything reads <see cref="Current"/>.</summary>
    public void Select(string? flag) => _flag = flag;

    public CliProfile Current => _current ??= Store.Resolve(_flag, environment());

    /// <summary>The profile's name if it has been worked out and is not the default, for hints.</summary>
    public string? NamedProfileOrNull => _current is { IsDefault: false } p ? p.Name : null;
}

public static class ProfileServiceCollectionExtensions
{
    /// <summary>
    /// Registers <paramref name="profiles"/> and the credential and config stores of the profile it picks.
    /// The stores are built on first use, after Program.Main has told the context what --profile said.
    /// </summary>
    public static IServiceCollection AddProfileStores(this IServiceCollection services, ProfileContext profiles)
    {
        services.AddSingleton(profiles);
        services.AddSingleton(sp => CredentialStore.ForProfile(sp.GetRequiredService<ProfileContext>().Current));
        services.AddSingleton(sp => ConfigStore.ForProfile(sp.GetRequiredService<ProfileContext>().Current));
        return services;
    }
}
