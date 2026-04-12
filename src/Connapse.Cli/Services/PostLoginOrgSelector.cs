using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// After OAuth login against a Cloud server, picks a default org for the
/// user based on how many orgs they have and whether the session is
/// interactive.
/// </summary>
public sealed class PostLoginOrgSelector
{
    private readonly IConsolePrompt _console;

    public PostLoginOrgSelector(IConsolePrompt console)
    {
        _console = console;
    }

    public PostLoginOrgResult Select(IReadOnlyList<Organization> orgs)
    {
        if (orgs.Count == 0)
        {
            _console.WriteColoredLine(
                "  You are not a member of any organizations. Ask an admin to invite you.",
                ConsoleColor.Yellow);
            return new PostLoginOrgResult(null, PostLoginOrgOutcome.NoOrgs);
        }

        if (orgs.Count == 1)
        {
            var only = orgs[0];
            _console.WriteColoredLine(
                $"  Default organization: {only.Name} ({only.Slug})",
                ConsoleColor.Green);
            return new PostLoginOrgResult(only.Slug, PostLoginOrgOutcome.AutoSelected);
        }

        if (!_console.IsInteractive)
        {
            _console.WriteLine("  You belong to multiple organizations:");
            for (var i = 0; i < orgs.Count; i++)
                _console.WriteLine($"    {i + 1}. {orgs[i].Name} ({orgs[i].Slug})");
            _console.WriteLine("  Run 'connapse org switch <slug>' to set a default.");
            return new PostLoginOrgResult(null, PostLoginOrgOutcome.NonInteractiveDeferred);
        }

        _console.WriteLine("  Select your default organization:");
        for (var i = 0; i < orgs.Count; i++)
            _console.WriteLine($"    {i + 1}. {orgs[i].Name} ({orgs[i].Slug})");

        var choice = _console.PromptForChoice($"  Enter 1-{orgs.Count}: ", orgs.Count);
        if (choice is null)
        {
            _console.WriteLine("  No selection made. Run 'connapse org switch <slug>' to set a default later.");
            return new PostLoginOrgResult(null, PostLoginOrgOutcome.NonInteractiveDeferred);
        }

        var chosen = orgs[choice.Value - 1];
        _console.WriteColoredLine(
            $"  Default organization: {chosen.Name} ({chosen.Slug})",
            ConsoleColor.Green);
        return new PostLoginOrgResult(chosen.Slug, PostLoginOrgOutcome.UserSelected);
    }
}
