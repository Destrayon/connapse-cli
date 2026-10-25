using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

/// <summary>
/// <c>connapse api &lt;method&gt; &lt;path&gt;</c>: any API route on the signed-in server, for routes the
/// CLI has no command for yet. Like <c>gh api</c>, it prints the response body as the server sent it.
/// </summary>
public static class ApiCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var methodArg = new Argument<string>("method")
        {
            Description = "The HTTP method: GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS.",
        };

        var pathArg = new Argument<string>("path")
        {
            Description = "The route on the signed-in server, with any query string, e.g. /api/containers?take=50.",
        };

        var dataOption = new Option<string?>("--data", "-d")
        {
            Description = "The request body: the text itself, @file to read a file, or - to read stdin.",
        };

        var contentTypeOption = new Option<string>("--content-type")
        {
            Description = "The body's Content-Type. Defaults to application/json.",
            DefaultValueFactory = _ => "application/json",
        };

        var paginateOption = new Option<bool>("--paginate")
        {
            Description = "For a GET of a paged list, follow every page (skip/take) and print all items as one JSON array.",
        };

        var cmd = new Command("api", "Call any API route directly, with your sign-in.");
        cmd.Arguments.Add(methodArg);
        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(dataOption);
        cmd.Options.Add(contentTypeOption);
        cmd.Options.Add(paginateOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var request = new ApiPassthroughRequest(
                parseResult.GetRequiredValue(methodArg),
                parseResult.GetRequiredValue(pathArg),
                parseResult.GetValue(dataOption),
                parseResult.GetValue(contentTypeOption) ?? "application/json",
                parseResult.GetValue(paginateOption));
            var passthrough = new ApiPassthrough(services.GetRequiredService<ApiClient>());
            return await passthrough.RunAsync(request, Console.In, Console.Out, Console.Error, ct);
        }));

        return cmd;
    }
}
