namespace Connapse.Cli.Services;

/// <summary>
/// Git Bash on Windows rewrites any argument that starts with / into a Windows path before the CLI sees
/// it, so a container folder like /notes arrives as C:/Program Files/Git/notes. A server path never
/// starts with a drive letter, so one that does was rewritten (or was a local path given by mistake).
/// </summary>
public static class GitBashPaths
{
    public const string Fix = "run with MSYS_NO_PATHCONV=1 set";

    public static bool LooksRewritten(string value)
    {
        string v = value.Trim();
        return v.Length > 2 && char.IsAsciiLetter(v[0]) && v[1] == ':' && (v[2] == '/' || v[2] == '\\');
    }

    /// <summary>Returns <paramref name="folder"/>, or refuses it when it is a Windows path.</summary>
    public static string CheckFolder(string folder, string optionName)
    {
        if (LooksRewritten(folder))
            throw new CliValidationError(
                $"{optionName} '{folder}' is a Windows path, not a folder in the container. " +
                $"Git Bash rewrites arguments that start with /, so /notes becomes C:/Program Files/Git/notes; {Fix}.");
        return folder;
    }
}
