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
            var destination = GitBashPaths.CheckFolder(parseResult.GetValue(destinationOption) ?? "/", "--destination");

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

            var root = File.Exists(sourcePath) ? null : sourcePath;

            foreach (var file in files)
            {
                Console.Write($"  {(root is null ? Path.GetFileName(file) : RelativeName(root, file))}... ");

                try
                {
                    var folder = FolderFor(destination, root, file);

                    // Sent by hand, not through the generated client: the spec describes the upload as a model
                    // of byte arrays, which Kiota's multipart writer refuses (it takes only its MultipartBody),
                    // and which would hold each file in memory and drop its name.
                    using var content = new MultipartFormDataContent();
                    await using var fileStream = File.OpenRead(file);
                    using var streamContent = new StreamContent(fileStream);
                    streamContent.Headers.ContentType =
                        new MediaTypeHeaderValue("application/octet-stream");

                    content.Add(streamContent, "files", Path.GetFileName(file));
                    content.Add(new StringContent(folder), "path");
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

    /// <summary>
    /// The container folder <paramref name="file"/> goes into: <paramref name="destination"/> plus the
    /// file's subfolder under the uploaded folder <paramref name="root"/>, so the folder's layout is kept
    /// and two files with the same name in different subfolders don't overwrite each other.
    /// </summary>
    internal static string FolderFor(string destination, string? root, string file)
    {
        if (root is null)
            return destination;

        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var relative = Path.GetRelativePath(Path.GetFullPath(root), dir);
        if (relative == ".")
            return destination;

        // A backslash is a separator only on Windows. Elsewhere it can be part of a folder name, and
        // the server would read it as a separator, merging 'a\b' with 'a/b'.
        if (Path.DirectorySeparatorChar != '\\' && relative.Contains('\\'))
            throw new CliValidationError(
                $"Folder '{relative}' has a backslash in its name, which a container folder can't hold.");
        relative = relative.Replace(Path.DirectorySeparatorChar, '/');

        return destination.TrimEnd('/') + "/" + relative + "/";
    }

    private static string RelativeName(string root, string file) =>
        Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file)).Replace(Path.DirectorySeparatorChar, '/');
}
