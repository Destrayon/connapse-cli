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
    /// is populated from the parsed <c>--org</c> option (or the
    /// <c>CONNAPSE_ORG</c> environment variable) before the inner handler
    /// runs. Reads the <see cref="OrgResolver"/> precedence: flag → env.
    /// The <see cref="ConfigStore"/> default is NOT read here — that's a
    /// per-command decision handled by <see cref="OrgResolver.Resolve"/>
    /// when the command actually needs the slug.
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

            var context = services.GetRequiredService<OrgContextProvider>();

            // Priority: flag > env. Config default is applied later by OrgResolver
            // inside ApiClient when edition is known to be Cloud.
            if (!string.IsNullOrWhiteSpace(flagValue))
                context.CurrentOrgSlug = flagValue;
            else if (!string.IsNullOrWhiteSpace(envValue))
                context.CurrentOrgSlug = envValue;
            else
                context.CurrentOrgSlug = null;

            return await handler(parseResult, ct);
        };
    }
}
