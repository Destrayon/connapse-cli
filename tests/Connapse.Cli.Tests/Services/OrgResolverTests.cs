using Connapse.Cli.Models;
using Connapse.Cli.Services;

namespace Connapse.Cli.Tests.Services;

public sealed class OrgResolverTests
{
    private readonly OrgResolver _resolver = new();

    [Fact]
    public void Flag_takes_precedence_over_env_and_config()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: "flag-org",
            EnvironmentVariable: "env-org",
            ConfigDefault: "config-org",
            Edition: ServerEdition.Cloud);

        Assert.Equal("flag-org", _resolver.Resolve(ctx));
    }

    [Fact]
    public void Env_var_used_when_no_flag()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: null,
            EnvironmentVariable: "env-org",
            ConfigDefault: "config-org",
            Edition: ServerEdition.Cloud);

        Assert.Equal("env-org", _resolver.Resolve(ctx));
    }

    [Fact]
    public void Config_default_used_when_no_flag_and_no_env()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: null,
            EnvironmentVariable: null,
            ConfigDefault: "config-org",
            Edition: ServerEdition.Cloud);

        Assert.Equal("config-org", _resolver.Resolve(ctx));
    }

    [Fact]
    public void Returns_null_when_nothing_set()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: null,
            EnvironmentVariable: null,
            ConfigDefault: null,
            Edition: ServerEdition.Cloud);

        Assert.Null(_resolver.Resolve(ctx));
    }

    [Fact]
    public void Empty_string_flag_is_ignored()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: "",
            EnvironmentVariable: "env-org",
            ConfigDefault: null,
            Edition: ServerEdition.Cloud);

        Assert.Equal("env-org", _resolver.Resolve(ctx));
    }

    [Fact]
    public void Whitespace_flag_is_ignored()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: "   ",
            EnvironmentVariable: "env-org",
            ConfigDefault: null,
            Edition: ServerEdition.Cloud);

        Assert.Equal("env-org", _resolver.Resolve(ctx));
    }

    [Fact]
    public void Self_hosted_edition_returns_null_regardless_of_inputs()
    {
        var ctx = new OrgResolutionContext(
            FlagValue: "flag-org",
            EnvironmentVariable: "env-org",
            ConfigDefault: "config-org",
            Edition: ServerEdition.SelfHosted);

        Assert.Null(_resolver.Resolve(ctx));
    }

    [Fact]
    public void Unknown_edition_treats_as_cloud()
    {
        // Before EditionDetector has run (first command), we don't know the
        // edition. We default to Cloud behavior so org resolution still works.
        var ctx = new OrgResolutionContext(
            FlagValue: null,
            EnvironmentVariable: null,
            ConfigDefault: "config-org",
            Edition: ServerEdition.Unknown);

        Assert.Equal("config-org", _resolver.Resolve(ctx));
    }
}
