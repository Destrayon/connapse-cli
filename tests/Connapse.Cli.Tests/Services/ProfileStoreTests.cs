using Connapse.Cli.Models;
using Connapse.Cli.Services;

namespace Connapse.Cli.Tests.Services;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _userHome;
    private readonly ProfileStore _store;

    public ProfileStoreTests()
    {
        _userHome = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid().ToString("N"));
        _store = new ProfileStore(Path.Combine(_userHome, ".connapse"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_userHome))
            Directory.Delete(_userHome, recursive: true);
    }

    [Fact]
    public void Default_profile_reads_the_login_saved_before_profiles_existed()
    {
        // What a CLI from before profiles wrote: ~/.connapse/credentials.json.
        new CredentialStore(_userHome).Save(new CliCredentials("old-token", "https://www.connapse.com", "a@example.com"));

        var profile = _store.Resolve(flag: null, environment: null);

        Assert.True(profile.IsDefault);
        Assert.Equal("old-token", CredentialStore.ForProfile(profile).Load()!.ApiKey);
    }

    [Fact]
    public void Two_logins_to_different_servers_are_kept_side_by_side()
    {
        CredentialStore.ForProfile(_store.Get("default"))
            .Save(new CliCredentials("cloud-token", "https://www.connapse.com", "a@example.com"));
        CredentialStore.ForProfile(_store.Get("home"))
            .Save(new CliCredentials("home-token", "https://connapse.home.lan", "a@example.com"));
        ConfigStore.ForProfile(_store.Get("home")).SetDefaultOrg("family");

        Assert.Equal("https://www.connapse.com", CredentialStore.ForProfile(_store.Get("default")).Load()!.ApiBaseUrl);
        Assert.Equal("https://connapse.home.lan", CredentialStore.ForProfile(_store.Get("home")).Load()!.ApiBaseUrl);
        Assert.Null(ConfigStore.ForProfile(_store.Get("default")).Load().DefaultOrgSlug);
        Assert.Equal(new[] { "default", "home" }, _store.List().Select(p => p.Name));
    }

    [Fact]
    public void Flag_beats_environment_which_beats_the_switched_profile()
    {
        _store.SetActive(_store.Get("saved"));

        Assert.Equal("flag", _store.Resolve("flag", "env").Name);
        Assert.Equal("env", _store.Resolve(null, "env").Name);
        Assert.Equal("saved", _store.Resolve(null, null).Name);
        Assert.Equal("saved", _store.Resolve("  ", "").Name);
    }

    [Fact]
    public void Switching_back_to_default_forgets_the_saved_profile()
    {
        _store.SetActive(_store.Get("home"));
        _store.SetActive(_store.Get("DEFAULT"));

        Assert.Null(_store.LoadActive());
        Assert.True(_store.Resolve(null, null).IsDefault);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("-x")]
    [InlineData("has space")]
    public void Names_that_could_leave_the_profiles_folder_are_refused(string name)
    {
        Assert.Throws<CliValidationError>(() => _store.Get(name));
    }

    [Fact]
    public void Context_resolves_once_from_the_flag_it_was_given()
    {
        var context = new ProfileContext(_store, () => "env");
        context.Select("work");

        Assert.Equal("work", context.Current.Name);
        Assert.Equal("work", context.NamedProfileOrNull);
        Assert.Equal(Path.Combine(_userHome, ".connapse", "profiles", "work"), context.Current.Directory);
    }

    [Fact]
    public void Context_names_no_profile_for_the_hint_when_it_used_the_default()
    {
        var context = new ProfileContext(_store, () => null);

        Assert.True(context.Current.IsDefault);
        Assert.Null(context.NamedProfileOrNull);
    }
}
