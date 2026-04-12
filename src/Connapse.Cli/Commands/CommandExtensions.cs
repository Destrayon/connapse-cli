using System.CommandLine;
using System.CommandLine.Parsing;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

/// <summary>
/// Helpers shared by every leaf command in Phase 5. Centralises the
/// boilerplate that reads the global <c>--org</c> option, populates
/// <see cref="IOrgContextProvider"/>, and delegates to the real handler.
/// </summary>
public static class CommandExtensions
{
    /// <summary>
    /// Wraps a command handler so that <see cref="IOrgContextProvider.CurrentOrgSlug"/>
    /// is populated using the full <see cref="OrgResolver"/> precedence
    /// (flag → env → config default → null) before the inner handler runs.
    /// </summary>
    public static Func<ParseResult, CancellationToken, Task<int>> WithOrgContext(
        this IServiceProvider services,
        Option<string?> orgOption,
        Func<ParseResult, CancellationToken, Task<int>> handler)
    {
        return async (parseResult, ct) =>
        {
            var flagValue = parseResult.GetValue(orgOption);
            var envValue = Environment.GetEnvironmentVariable("CONNAPSE_ORG");

            var configStore = services.GetRequiredService<ConfigStore>();
            var resolver = services.GetRequiredService<OrgResolver>();
            var config = configStore.Load();

            var resolved = resolver.Resolve(new OrgResolutionContext(
                FlagValue: flagValue,
                EnvironmentVariable: envValue,
                ConfigDefault: config.DefaultOrgSlug,
                Edition: config.Edition ?? Models.ServerEdition.Unknown));

            var context = services.GetRequiredService<OrgContextProvider>();
            context.CurrentOrgSlug = resolved;

            return await handler(parseResult, ct);
        };
    }
}
