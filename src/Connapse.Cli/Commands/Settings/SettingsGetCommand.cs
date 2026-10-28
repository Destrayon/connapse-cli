using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Settings;

public static class SettingsGetCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var categoryArg = SettingsHelpers.CategoryArgument();

        var cmd = new Command("get", "Show a settings category as JSON. API keys are never shown.");
        cmd.Arguments.Add(categoryArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var api = services.GetRequiredService<ApiClient>();
            var settings = await SettingsHelpers.GetAsync(api, parseResult.GetRequiredValue(categoryArg), ct);
            Console.WriteLine(SettingsHelpers.Indented(settings));
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
