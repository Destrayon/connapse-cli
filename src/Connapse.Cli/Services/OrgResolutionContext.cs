using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// Inputs to OrgResolver. Separated from the resolver so tests can construct
/// precedence scenarios without instantiating the full CLI pipeline.
/// </summary>
public readonly record struct OrgResolutionContext(
    string? FlagValue,
    string? EnvironmentVariable,
    string? ConfigDefault,
    ServerEdition Edition);
