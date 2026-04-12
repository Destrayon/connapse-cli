namespace Connapse.Cli.Models;

/// <summary>
/// Outcome of post-login org selection.
/// </summary>
public sealed record PostLoginOrgResult(
    string? SelectedSlug,
    PostLoginOrgOutcome Outcome);

public enum PostLoginOrgOutcome
{
    /// <summary>User has no orgs; no default set.</summary>
    NoOrgs,
    /// <summary>Single org auto-selected as default.</summary>
    AutoSelected,
    /// <summary>Multi-org user picked one via prompt.</summary>
    UserSelected,
    /// <summary>Multi-org user in non-interactive session; no default set.</summary>
    NonInteractiveDeferred,
}
