using System.CommandLine;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Settings;

/// <summary><c>settings set</c> and <c>settings reset</c>.</summary>
public static class SettingsSetCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var categoryArg = SettingsHelpers.CategoryArgument();
        var pairsArg = new Argument<string[]>("key=value")
        {
            Description = "Settings to change. A value that reads as JSON (3, true, null, {...}) is sent as that; anything else as text.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var replaceOptions = new JsonObjectOptions("settings",
            "The whole category as a JSON object, replacing what is saved. An API key left out keeps its value.");

        var cmd = new Command("set", "Change settings in a category; the rest keep their values.");
        cmd.Arguments.Add(categoryArg);
        cmd.Arguments.Add(pairsArg);
        replaceOptions.AddTo(cmd);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var category = parseResult.GetRequiredValue(categoryArg);
            var pairs = parseResult.GetValue(pairsArg) ?? [];
            var replacement = replaceOptions.ReadText(parseResult);

            if (replacement is not null && pairs.Length > 0)
                throw new CliValidationError("Pass key=value pairs or --settings, not both.");
            if (replacement is null && pairs.Length == 0)
                throw new CliValidationError("Nothing to set; pass key=value pairs, --settings or --settings-file.");

            var api = services.GetRequiredService<ApiClient>();
            JsonObject settings;
            // The version the change is made on; null for --settings, which replaces whatever is saved.
            EntityTagHeaderValue? version = null;
            if (replacement is not null)
            {
                AdminHelpers.ParseJsonObject(replacement, replaceOptions.Inline.Name);
                settings = (JsonObject)JsonNode.Parse(replacement)!;
            }
            else
            {
                // The server replaces the whole category, so the change goes onto what is saved now.
                (settings, version) = await SettingsHelpers.ReadAsync(api, category, ct);
                foreach (var pair in pairs)
                {
                    var eq = pair.IndexOf('=');
                    if (eq <= 0)
                        throw new CliValidationError($"'{pair}' isn't key=value.");
                    var key = pair[..eq];
                    // Keep the server's spelling of a key, so chunkSize=1 and chunksize=1 change the same setting.
                    var existing = settings.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                        AdminHelpers.WriteWarning($"'{key}' isn't among the '{category}' settings the server returned; sending it anyway.");
                    settings[existing ?? key] = SettingsHelpers.ParseValue(pair[(eq + 1)..]);
                }
            }

            JsonObject saved;
            try
            {
                saved = await SettingsHelpers.PutAsync(api, category, settings, ct, version);
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Someone saved the category after it was read; saving anyway would undo their change.
                Console.Error.WriteLine(
                    $"Settings changed, reload: the '{category}' settings were saved by someone else after this command read them, so nothing was saved. " +
                    "Run the command again to make your change on top of theirs.");
                return CliExitCodes.UserError;
            }
            if (saved["success"]?.GetValueKind() == System.Text.Json.JsonValueKind.False)
            {
                Console.Error.WriteLine(saved["message"]?.GetValue<string>() ?? $"The server didn't save the '{category}' settings.");
                return CliExitCodes.UserError;
            }
            AdminHelpers.WriteSuccess(saved["message"]?.GetValue<string>() ?? $"Saved the '{category}' settings.");
            if (saved["legacyVectorsExist"]?.GetValue<bool>() == true)
                AdminHelpers.WriteWarning(
                    $"{saved["legacyVectorCount"]} vector(s) were made by another embedding model; 'connapse settings reindex' re-embeds them.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateReset(IServiceProvider services, Option<string?> orgOption)
    {
        var categoryArg = SettingsHelpers.CategoryArgument();

        var cmd = new Command("reset", "Reset a settings category to its configured defaults.");
        cmd.Arguments.Add(categoryArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var category = parseResult.GetRequiredValue(categoryArg);
            var client = services.GetRequiredService<ConnapseApiClient>();

            await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
                () => client.Api.V1.Settings[category].DeleteAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Reset the '{category}' settings to their defaults.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
