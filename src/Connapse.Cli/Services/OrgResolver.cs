using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// Picks the target organization for a request using a 4-tier precedence:
///   1. --org flag
///   2. CONNAPSE_ORG environment variable
///   3. config.json default
///   4. null (no org header sent)
///
/// Short-circuits to null when the server edition is self-hosted.
/// </summary>
public sealed class OrgResolver
{
    public string? Resolve(OrgResolutionContext context)
    {
        // Self-hosted has no orgs.
        if (context.Edition == ServerEdition.SelfHosted)
            return null;

        if (!string.IsNullOrWhiteSpace(context.FlagValue))
            return context.FlagValue;

        if (!string.IsNullOrWhiteSpace(context.EnvironmentVariable))
            return context.EnvironmentVariable;

        if (!string.IsNullOrWhiteSpace(context.ConfigDefault))
            return context.ConfigDefault;

        return null;
    }
}
