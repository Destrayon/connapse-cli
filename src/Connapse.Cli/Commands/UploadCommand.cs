using System.CommandLine;
using System.Net.Http.Headers;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

public static class UploadCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var pathArg = new Argument<string>("path")
        {
            Description = "Path to a file or directory to upload.",
        };

        var containerOption = new Option<string>("--container")
        {
            Description = "Container name to upload into.",
            Required = true,
        };

        var strategyOption = new Option<string>("--strategy")
        {
            Description = "Chunking strategy name. Defaults to 'Semantic'.",
            DefaultValueFactory = _ => "Semantic",
        };

        var destinationOption = new Option<string>("--destination")
        {
            Description = "Destination folder inside the container. Defaults to '/'.",
            DefaultValueFactory = _ => "/",
        };

        var cmd = new Command("upload", "Upload a file or directory to a container.");
        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(containerOption);
        cmd.Options.Add(strategyOption);
        cmd.Options.Add(destinationOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var sourcePath = parseResult.GetRequiredValue(pathArg);
            var containerName = parseResult.GetRequiredValue(containerOption);
            var strategy = parseResult.GetValue(strategyOption) ?? "Semantic";
            var destination = parseResult.GetValue(destinationOption) ?? "/";

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                throw new CliValidationError($"Path '{sourcePath}' does not exist.");

            var apiClient = services.GetRequiredService<ApiClient>();
            var client = services.GetRequiredService<ConnapseApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(client, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            var files = new List<string>();
            if (File.Exists(sourcePath))
                files.Add(sourcePath);
            else
                files.AddRange(Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories));

            if (files.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"No files found under '{sourcePath}'.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            Console.WriteLine($"Uploading {files.Count} file(s) to container '{containerName}'");
            Console.WriteLine();

            var successful = 0;
            var failed = 0;
            var uploadPath = $"/api/containers/{containerId}/files";

            foreach (var file in files)
            {
                Console.Write($"  {Path.GetFileName(file)}... ");

                try
                {
                    // Sent by hand, not through the generated client: the spec describes the upload as a model
                    // of byte arrays, which Kiota's multipart writer refuses (it takes only its MultipartBody),
                    // and which would hold each file in memory and drop its name.
                    using var content = new MultipartFormDataContent();
                    await using var fileStream = File.OpenRead(file);
                    using var streamContent = new StreamContent(fileStream);
                    streamContent.Headers.ContentType =
                        new MediaTypeHeaderValue("application/octet-stream");

                    content.Add(streamContent, "files", Path.GetFileName(file));
                    content.Add(new StringContent(destination), "path");
                    content.Add(new StringContent(strategy), "strategy");

                    using var response = await apiClient.SendRawAsync(
                        HttpMethod.Post, uploadPath, content, ct);

                    response.EnsureSuccessStatusCode();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Uploaded");
                    Console.ResetColor();
                    successful++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Failed: {ex.Message}");
                    Console.ResetColor();
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Summary: {successful} successful, {failed} failed");
            if (successful > 0)
                Console.WriteLine("Files are being processed in the background.");

            return failed == 0 ? CliExitCodes.Success : CliExitCodes.UserError;
        }));

        return cmd;
    }
}
