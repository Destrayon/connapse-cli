namespace Connapse.Cli.Services;

/// <summary>
/// Supplies the resolved org slug for the current command invocation.
/// Implemented in the command layer to bridge System.CommandLine's
/// InvocationContext into ApiClient without a direct dependency.
/// </summary>
public interface IOrgContextProvider
{
    /// <summary>The resolved org slug for the current command, or null.</summary>
    string? CurrentOrgSlug { get; }
}

/// <summary>
/// Simple mutable provider used by the command layer and tests.
/// </summary>
public sealed class OrgContextProvider : IOrgContextProvider
{
    public string? CurrentOrgSlug { get; set; }
}
