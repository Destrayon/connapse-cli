using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Org;

internal static class OrgHelpers
{
    /// <summary>
    /// Probes the server edition and, if it's self-hosted, prints a
    /// friendly notice. Returns true if the caller should short-circuit.
    /// </summary>
    public static async Task<(bool Skip, ServerEdition Edition)> CheckEditionAsync(
        IServiceProvider services,
        CancellationToken ct)
    {
        var editionDetector = services.GetRequiredService<EditionDetector>();
        var edition = await editionDetector.GetEditionAsync(ct: ct);

        if (edition == ServerEdition.SelfHosted)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Organizations are only available in Connapse Cloud.");
            Console.WriteLine("Your server is self-hosted.");
            Console.ResetColor();
            return (Skip: true, edition);
        }

        return (Skip: false, edition);
    }
}
