namespace Connapse.Cli.Services;

/// <summary>
/// Resolves the user's home directory in a cross-platform way.
/// Ported from the existing Connapse.CLI/Program.cs GetUserHomePath() helper.
/// </summary>
public static class HomeDirectoryResolver
{
    public static string GetHomeDirectory() =>
        Environment.GetEnvironmentVariable("USERPROFILE")
        ?? Environment.GetEnvironmentVariable("HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
