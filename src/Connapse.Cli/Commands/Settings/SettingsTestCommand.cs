using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Settings;

public static class SettingsTestCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var categoryArg = new Argument<string>("category")
        {
            Description = "What to test: embedding, llm, crossencoder or minio.",
        };
        var settingsOptions = new JsonObjectOptions("settings",
            "Settings to test before saving them. Without them, the saved ones are tested.");
        var timeoutOption = new Option<int?>("--timeout") { Description = "Seconds to wait for the service." };

        var cmd = new Command("test", "Check that the server can reach an embedding, LLM, reranker or storage service.");
        cmd.Arguments.Add(categoryArg);
        settingsOptions.AddTo(cmd);
        cmd.Options.Add(timeoutOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var category = parseResult.GetRequiredValue(categoryArg);
            var settings = settingsOptions.Read(parseResult);
            if (settings is null)
            {
                try
                {
                    var saved = await SettingsHelpers.GetAsync(services.GetRequiredService<ApiClient>(), category, ct);
                    settings = AdminHelpers.ParseJsonObject(saved.ToJsonString(), "settings");
                }
                catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new CliValidationError(
                        $"'{category}' has no saved settings to test; pass them with --settings or --settings-file.");
                }
            }

            var client = services.GetRequiredService<ConnapseApiClient>();
            var result = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope, () => client.Api.Settings.TestConnection.PostAsync(
                new TestConnectionRequest
                {
                    Category = category,
                    Settings = settings,
                    TimeoutSeconds = parseResult.GetValue(timeoutOption),
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no test result.");

            if (result.Success == true)
            {
                AdminHelpers.WriteSuccess($"Reached it: {result.Message}");
                return CliExitCodes.Success;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Couldn't reach it: {result.Message}");
            Console.ResetColor();
            return CliExitCodes.UserError;
        }));

        return cmd;
    }
}
