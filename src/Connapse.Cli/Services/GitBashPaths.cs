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

    /// <summary>
    /// The folders Git Bash may have prefixed onto an argument: its install folder, which Git Bash
    /// exports as EXEPATH (sometimes its bin or usr\bin subfolder), and the default install locations.
    /// </summary>
    public static IReadOnlyList<string> GitRoots()
    {
        var roots = new List<string>();
        string? exePath = Environment.GetEnvironmentVariable("EXEPATH");
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            string root = Normalize(exePath);
            foreach (string suffix in new[] { "/usr/bin", "/bin" })
            {
                if (root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    root = root[..^suffix.Length];
                    break;
                }
            }
            roots.Add(root);
        }
        roots.Add("C:/Program Files/Git");
        roots.Add("C:/Program Files (x86)/Git");
        return roots;
    }

    /// <summary>
    /// What the user typed before Git Bash rewrote it, such as /code for C:/Program Files/Git/code, or
    /// null when <paramref name="value"/> isn't under one of <paramref name="roots"/>.
    /// </summary>
    public static string? OriginalOf(string value, IEnumerable<string> roots)
    {
        string v = Normalize(value);
        foreach (string root in roots)
        {
            string r = Normalize(root);
            if (r.Length == 0)
                continue;
            if (v.Equals(r, StringComparison.OrdinalIgnoreCase))
                return "/";
            if (v.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
                return v[r.Length..];
        }
        return null;
    }

    /// <summary>The sentence that says what Git Bash did to <paramref name="value"/>.</summary>
    public static string Explain(string value, IEnumerable<string>? roots = null)
    {
        string? original = OriginalOf(value, roots ?? GitRoots());
        return original is null
            ? $"If you typed a folder that starts with /, Git Bash rewrote it into '{value}'; {Fix}."
            : $"Git Bash rewrote '{original}' into '{value}' because it starts with /; {Fix}.";
    }

    /// <summary>Returns <paramref name="folder"/>, or refuses it when it is a Windows path.</summary>
    public static string CheckFolder(string folder, string optionName)
    {
        if (LooksRewritten(folder))
            throw new CliValidationError(
                $"{optionName} '{folder}' is a Windows path, not a folder in the container. {Explain(folder)}");
        return folder;
    }

    private static string Normalize(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');
}
