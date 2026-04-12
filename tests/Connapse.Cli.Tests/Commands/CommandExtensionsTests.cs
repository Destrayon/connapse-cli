using System.CommandLine;
using Connapse.Cli.Commands;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Tests.Commands;

public sealed class CommandExtensionsTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly OrgContextProvider _context;
    private readonly Option<string?> _orgOption;
    private readonly Command _command;
    private readonly string? _originalEnv;

    public CommandExtensionsTests()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IOrgContextProvider, OrgContextProvider>();
        collection.AddSingleton<OrgContextProvider>(sp =>
            (OrgContextProvider)sp.GetRequiredService<IOrgContextProvider>());
        _services = collection.BuildServiceProvider();
        _context = _services.GetRequiredService<OrgContextProvider>();

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
    public async Task No_flag_no_env_sets_context_to_null()
    {
        _context.CurrentOrgSlug = "stale";
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
