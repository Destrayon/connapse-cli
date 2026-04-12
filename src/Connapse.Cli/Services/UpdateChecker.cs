using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using Connapse.Cli.Models;
using NuGet.Versioning;

namespace Connapse.Cli.Services;

/// <summary>
/// Handles `connapse update` and the passive once-a-day "update available"
/// notification.
///
/// Ported from the original Connapse.CLI Program.cs HandleUpdate +
/// CheckForUpdateNotification and their helpers. Points at the new
/// Destrayon/connapse-cli repository.
/// </summary>
public sealed class UpdateChecker
{
    private const string RepoOwner = "Destrayon";
    private const string RepoName = "connapse-cli";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    private const string AllReleasesUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=10";

    public static string GetCurrentVersion() =>
        typeof(UpdateChecker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public async Task<int> UpdateAsync(bool checkOnly, bool includePre, CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();
        Console.WriteLine($"Current version: v{currentVersion}");
        if (includePre)
            WriteColor(ConsoleColor.DarkGray, "Including pre-release versions.");

        using var ghClient = CreateGitHubClient(currentVersion);

        GitHubRelease? release;
        try
        {
            release = await GetLatestReleaseAsync(ghClient, includePre, ct);
        }
        catch (Exception ex)
        {
            return Error($"Could not reach GitHub: {ex.Message}");
        }

        if (release is null)
            return Error("Could not parse release information from GitHub.");

        var latestVersion = release.TagName.TrimStart('v');
        Console.WriteLine($"Latest version:  v{latestVersion}");

        if (!IsNewer(latestVersion, currentVersion))
        {
            WriteColor(ConsoleColor.Green, "You are already up to date.");
            return 0;
        }

        WriteColor(ConsoleColor.Cyan, $"Update available: v{currentVersion} → v{latestVersion}");
        if (checkOnly) return 0;

        var assetName = GetPlatformAssetName();
        var asset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            WriteColor(ConsoleColor.Yellow, $"No binary for this platform ({assetName}) in the release.");
            Console.WriteLine("If you installed via NuGet, run:  dotnet tool update -g Connapse.CLI");
            return 1;
        }

        if (IsGlobalToolInstall())
            return await UpdateGlobalToolAsync(ghClient, release, latestVersion, ct);

        return await UpdateStandaloneBinaryAsync(ghClient, asset, assetName, latestVersion, ct);
    }

    public async Task CheckForUpdateNotificationAsync(CancellationToken ct = default)
    {
        try
        {
            var checkFile = Path.Combine(HomeDirectoryResolver.GetHomeDirectory(), ".connapse", "last-update-check");

            if (File.Exists(checkFile) &&
                DateTime.TryParse(File.ReadAllText(checkFile).Trim(), out var lastCheck) &&
                DateTime.UtcNow - lastCheck < TimeSpan.FromHours(24))
                return;

            var currentVersion = GetCurrentVersion();
            using var ghClient = CreateGitHubClient(currentVersion);
            ghClient.Timeout = TimeSpan.FromSeconds(3);

            var release = await ghClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, ct);

            Directory.CreateDirectory(Path.GetDirectoryName(checkFile)!);
            File.WriteAllText(checkFile, DateTime.UtcNow.ToString("O"));

            if (release is null) return;
            var latestVersion = release.TagName.TrimStart('v');
            if (IsNewer(latestVersion, currentVersion))
            {
                WriteColor(
                    ConsoleColor.Yellow,
                    $"\n  Note: connapse v{latestVersion} is available. Run 'connapse update' to upgrade.");
            }
        }
        catch
        {
            // Silent on failure — this is a background notification.
        }
    }

    public void CheckPendingUpdateResult()
    {
        var resultPath = GetUpdateResultPath();
        if (!File.Exists(resultPath)) return;

        try
        {
            var content = File.ReadAllText(resultPath).Trim();
            File.Delete(resultPath);

            if (content.StartsWith("OK:", StringComparison.Ordinal))
            {
                var version = content["OK:".Length..];
                WriteColor(ConsoleColor.Green, $"  Update to v{version} completed successfully.");
            }
            else if (content.StartsWith("FAIL:", StringComparison.Ordinal))
            {
                var reason = content["FAIL:".Length..];
                WriteColor(ConsoleColor.Red, $"  Background update failed: {reason}");
                Console.WriteLine("  Try running: dotnet tool update -g Connapse.CLI");
            }
        }
        catch
        {
            // ignore
        }
    }

    // ------------------------------------------------------------------
    // Private helpers (mechanically ported from the original Program.cs)
    // ------------------------------------------------------------------

    private static HttpClient CreateGitHubClient(string currentVersion)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("connapse-cli", currentVersion));
        return client;
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync(
        HttpClient ghClient, bool includePre, CancellationToken ct)
    {
        if (!includePre)
            return await ghClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, ct);

        var releases = await ghClient.GetFromJsonAsync<List<GitHubRelease>>(AllReleasesUrl, ct);
        return releases?.FirstOrDefault(r => !r.Draft);
    }

    private async Task<int> UpdateGlobalToolAsync(
        HttpClient ghClient, GitHubRelease release, string latestVersion, CancellationToken ct)
    {
        var nupkgAsset = release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));

        if (nupkgAsset is null)
        {
            WriteColor(ConsoleColor.Yellow, "No NuGet package in this release. Download the binary manually:");
            Console.WriteLine($"  https://github.com/{RepoOwner}/{RepoName}/releases/tag/{release.TagName}");
            return 1;
        }

        Console.Write($"Download and install v{latestVersion}? [y/N] ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm is not ("y" or "yes")) return 0;

        var tmpDir = Path.Combine(Path.GetTempPath(), "connapse-update");
        if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        Directory.CreateDirectory(tmpDir);

        try
        {
            Console.Write($"Downloading {nupkgAsset.Name}... ");
            using var dlResponse = await ghClient.GetAsync(
                nupkgAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            dlResponse.EnsureSuccessStatusCode();
            var nupkgPath = Path.Combine(tmpDir, nupkgAsset.Name);
            await using (var fs = new FileStream(nupkgPath, FileMode.Create, FileAccess.Write))
                await dlResponse.Content.CopyToAsync(fs, ct);
            Console.WriteLine("done.");

            var resultPath = GetUpdateResultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

            if (OperatingSystem.IsWindows())
                SpawnWindowsToolUpdate(tmpDir, resultPath, latestVersion);
            else
                SpawnUnixToolUpdate(tmpDir, resultPath, latestVersion);

            WriteColor(ConsoleColor.Green, $"Update to v{latestVersion} is installing in the background.");
            Console.WriteLine("Run 'connapse version' shortly to confirm.");
            return 0;
        }
        catch (Exception ex)
        {
            return Error($"\nUpdate failed: {ex.Message}");
        }
    }

    private async Task<int> UpdateStandaloneBinaryAsync(
        HttpClient ghClient, GitHubAsset asset, string assetName, string latestVersion, CancellationToken ct)
    {
        Console.Write($"Download and install v{latestVersion}? [y/N] ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm is not ("y" or "yes")) return 0;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return Error("Could not determine current executable path.");

        var tmpPath = exePath + ".new";
        Console.Write($"Downloading {assetName}... ");
        try
        {
            using var response = await ghClient.GetAsync(
                asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write);
            await response.Content.CopyToAsync(fs, ct);
        }
        catch (Exception ex)
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
            return Error($"\nDownload failed: {ex.Message}");
        }
        Console.WriteLine("done.");

        if (OperatingSystem.IsWindows())
        {
            // Running .exe is locked on Windows — hand off to a batch script.
            var batPath = Path.Combine(Path.GetTempPath(), "connapse-update.bat");
            File.WriteAllText(batPath,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak > nul\r\n" +
                $"move /y \"{tmpPath}\" \"{exePath}\" > nul\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            WriteColor(ConsoleColor.Green, $"connapse will be updated to v{latestVersion} in a moment.");
        }
        else
        {
            File.Move(tmpPath, exePath, overwrite: true);
            var mode = File.GetUnixFileMode(exePath);
            File.SetUnixFileMode(exePath,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            WriteColor(ConsoleColor.Green, $"Updated to v{latestVersion}. Restart connapse to confirm.");
        }

        return 0;
    }

    private static void SpawnWindowsToolUpdate(string tmpDir, string resultPath, string latestVersion)
    {
        var batPath = Path.Combine(Path.GetTempPath(), "connapse-tool-update.bat");
        File.WriteAllText(batPath,
            "@echo off\r\n" +
            "timeout /t 3 /nobreak > nul\r\n" +
            $"dotnet tool update -g Connapse.CLI --add-source \"{tmpDir}\" --version {latestVersion} > nul 2>&1\r\n" +
            "if %ERRORLEVEL% == 0 (\r\n" +
            $"  echo OK:{latestVersion}> \"{resultPath}\"\r\n" +
            ") else (\r\n" +
            $"  echo FAIL:dotnet tool update exited with code %ERRORLEVEL%> \"{resultPath}\"\r\n" +
            ")\r\n" +
            $"rmdir /s /q \"{tmpDir}\" 2> nul\r\n" +
            "del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    private static void SpawnUnixToolUpdate(string tmpDir, string resultPath, string latestVersion)
    {
        var shellCmd =
            $"sleep 3 && " +
            $"dotnet tool update -g Connapse.CLI --add-source '{tmpDir}' --version {latestVersion} > /dev/null 2>&1 && " +
            $"echo 'OK:{latestVersion}' > '{resultPath}' || " +
            $"echo 'FAIL:dotnet tool update failed' > '{resultPath}'; " +
            $"rm -rf '{tmpDir}'";
        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"-c \"{shellCmd}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    private static string GetPlatformAssetName()
    {
        if (OperatingSystem.IsWindows()) return "connapse-cli-win-x64.exe";
        if (OperatingSystem.IsLinux()) return "connapse-cli-linux-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "connapse-cli-osx-arm64"
                : "connapse-cli-osx-x64";
        return "connapse-cli-linux-x64";
    }

    private static bool IsGlobalToolInstall()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;
        var dotnetToolsDir = Path.Combine(
            HomeDirectoryResolver.GetHomeDirectory(), ".dotnet", "tools");
        return exePath.StartsWith(dotnetToolsDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUpdateResultPath() =>
        Path.Combine(HomeDirectoryResolver.GetHomeDirectory(), ".connapse", "update-result");

    private static bool IsNewer(string latest, string current)
    {
        try
        {
            if (NuGetVersion.TryParse(latest, out var latestV) && NuGetVersion.TryParse(current, out var currentV))
                return latestV > currentV;
        }
        catch
        {
            // NuGet.Versioning assembly may be unavailable in trimmed builds
        }

        var latestBase = StripPreRelease(latest);
        var currentBase = StripPreRelease(current);
        if (Version.TryParse(latestBase, out var lv) && Version.TryParse(currentBase, out var cv))
        {
            if (lv != cv) return lv > cv;
            var latestIsPre = latest.Contains('-');
            var currentIsPre = current.Contains('-');
            return !latestIsPre && currentIsPre;
        }

        return string.Compare(latest, current, StringComparison.Ordinal) > 0;
    }

    private static string StripPreRelease(string version) =>
        version.Contains('-') ? version[..version.IndexOf('-')] : version;

    private static int Error(string message)
    {
        WriteColor(ConsoleColor.Red, $"Error: {message}");
        return 1;
    }

    private static void WriteColor(ConsoleColor color, string message)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = original;
    }
}
