using Connapse.Cli.Models;
using Connapse.Cli.Services;

namespace Connapse.Cli.Tests.Services;

public sealed class PostLoginOrgSelectorTests
{
    private readonly FakeConsolePrompt _console = new();

    private PostLoginOrgSelector CreateSelector() => new(_console);

    private static Organization Org(string slug, string name) =>
        new(Id: Guid.NewGuid(), Name: name, Slug: slug, CreatedAt: DateTime.UtcNow);

    [Fact]
    public void Zero_orgs_returns_null_and_prints_warning()
    {
        var result = CreateSelector().Select(new List<Organization>());

        Assert.Null(result.SelectedSlug);
        Assert.Equal(PostLoginOrgOutcome.NoOrgs, result.Outcome);
        Assert.Contains(_console.WrittenLines, line =>
            line.Contains("not a member", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Single_org_auto_selects()
    {
        var orgs = new List<Organization> { Org("khastra", "Khastra Inc.") };

        var result = CreateSelector().Select(orgs);

        Assert.Equal("khastra", result.SelectedSlug);
        Assert.Equal(PostLoginOrgOutcome.AutoSelected, result.Outcome);
    }

    [Fact]
    public void Multi_org_in_tty_prompts_and_returns_chosen_slug()
    {
        _console.IsInteractive = true;
        _console.ChoiceResponder = _ => 2; // pick 2nd org
        var orgs = new List<Organization>
        {
            Org("khastra", "Khastra"),
            Org("acme", "Acme Co."),
            Org("initech", "Initech"),
        };

        var result = CreateSelector().Select(orgs);

        Assert.Equal("acme", result.SelectedSlug);
        Assert.Equal(PostLoginOrgOutcome.UserSelected, result.Outcome);
    }

    [Fact]
    public void Multi_org_in_non_tty_returns_null_and_prints_instructions()
    {
        _console.IsInteractive = false;
        var orgs = new List<Organization>
        {
            Org("khastra", "Khastra"),
            Org("acme", "Acme Co."),
        };

        var result = CreateSelector().Select(orgs);

        Assert.Null(result.SelectedSlug);
        Assert.Equal(PostLoginOrgOutcome.NonInteractiveDeferred, result.Outcome);
        Assert.Contains(_console.WrittenLines, line =>
            line.Contains("connapse org switch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Prompt_returning_null_defers_like_non_tty()
    {
        _console.IsInteractive = true;
        _console.ChoiceResponder = _ => null; // user cancelled / EOF
        var orgs = new List<Organization>
        {
            Org("khastra", "Khastra"),
            Org("acme", "Acme Co."),
        };

        var result = CreateSelector().Select(orgs);

        Assert.Null(result.SelectedSlug);
        Assert.Equal(PostLoginOrgOutcome.NonInteractiveDeferred, result.Outcome);
    }

    private sealed class FakeConsolePrompt : IConsolePrompt
    {
        public bool IsInteractive { get; set; } = true;
        public Func<int, int?> ChoiceResponder { get; set; } = _ => 1;
        public List<string> WrittenLines { get; } = new();

        public int? PromptForChoice(string prompt, int count)
        {
            WrittenLines.Add(prompt);
            return ChoiceResponder(count);
        }

        public void WriteLine(string message) => WrittenLines.Add(message);

        public void WriteColoredLine(string message, ConsoleColor color) =>
            WrittenLines.Add(message);
    }
}
