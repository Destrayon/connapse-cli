using System.CommandLine;
using Connapse.Cli.Commands;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Tests.Commands;

public sealed class CommandExtensionsTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly OrgContextProvider _context;
    private readonly ConfigStore _configStore;
    private readonly Option<string?> _orgOption;
    private readonly Command _command;
    private readonly string? _originalEnv;
    private readonly string _tempHome;

    public CommandExtensionsTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempHome);

        var collection = new ServiceCollection();
        collection.AddSingleton<IOrgContextProvider, OrgContextProvider>();
        collection.AddSingleton<OrgContextProvider>(sp =>
            (OrgContextProvider)sp.GetRequiredService<IOrgContextProvider>());
        collection.AddSingleton(new ConfigStore(_tempHome));
        collection.AddSingleton<OrgResolver>();
        _services = collection.BuildServiceProvider();
        _context = _services.GetRequiredService<OrgContextProvider>();
        _configStore = _services.GetRequiredService<ConfigStore>();

        _orgOption = new Option<string?>("--org") { Recursive = true };
        _command = new Command("test");
        _command.Options.Add(_orgOption);

        _originalEnv = Environment.GetEnvironmentVariable("CONNAPSE_ORG");
        Environment.SetEnvironmentVariable("CONNAPSE_ORG", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CONNAPSE_ORG", _originalEnv);
        _services.Dispose();
        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    private async Task<int> Invoke(string args, Func<ParseResult, CancellationToken, Task<int>> handler)
    {
        _command.SetAction(_services.WithOrgContext(_orgOption, handler));
        var parseResult = _command.Parse(args);
        return await parseResult.InvokeAsync();
    }

    [Fact]
    public async Task Flag_value_populates_context()
    {
        await Invoke("test --org khastra", (_, _) => Task.FromResult(0));
        Assert.Equal("khastra", _context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Env_var_populates_context_when_flag_missing()
    {
        Environment.SetEnvironmentVariable("CONNAPSE_ORG", "env-org");
        await Invoke("test", (_, _) => Task.FromResult(0));
        Assert.Equal("env-org", _context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Flag_takes_precedence_over_env()
    {
        Environment.SetEnvironmentVariable("CONNAPSE_ORG", "env-org");
        await Invoke("test --org flag-org", (_, _) => Task.FromResult(0));
        Assert.Equal("flag-org", _context.CurrentOrgSlug);
    }

    [Fact]
    public async Task No_flag_no_env_no_config_sets_context_to_null()
    {
        _context.CurrentOrgSlug = "stale";
        await Invoke("test", (_, _) => Task.FromResult(0));
        Assert.Null(_context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Config_default_populates_context_when_flag_and_env_missing()
    {
        _configStore.Save(new Connapse.Cli.Models.CliConfig
        {
            DefaultOrgSlug = "config-org",
            Edition = Connapse.Cli.Models.ServerEdition.Cloud,
        });

        await Invoke("test", (_, _) => Task.FromResult(0));

        Assert.Equal("config-org", _context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Flag_takes_precedence_over_config_default()
    {
        _configStore.Save(new Connapse.Cli.Models.CliConfig
        {
            DefaultOrgSlug = "config-org",
            Edition = Connapse.Cli.Models.ServerEdition.Cloud,
        });

        await Invoke("test --org flag-org", (_, _) => Task.FromResult(0));

        Assert.Equal("flag-org", _context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Self_hosted_edition_ignores_config_default()
    {
        _configStore.Save(new Connapse.Cli.Models.CliConfig
        {
            DefaultOrgSlug = "should-be-ignored",
            Edition = Connapse.Cli.Models.ServerEdition.SelfHosted,
        });

        await Invoke("test", (_, _) => Task.FromResult(0));

        Assert.Null(_context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Empty_flag_treated_as_missing()
    {
        Environment.SetEnvironmentVariable("CONNAPSE_ORG", "env-org");
        await Invoke("test --org \"\"", (_, _) => Task.FromResult(0));
        Assert.Equal("env-org", _context.CurrentOrgSlug);
    }

    [Fact]
    public async Task Handler_return_value_is_propagated()
    {
        var exit = await Invoke("test", (_, _) => Task.FromResult(42));
        Assert.Equal(42, exit);
    }
}
