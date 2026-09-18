namespace NavyThunder.Data;

/// <summary>
/// Locates the repository root (marker: NavyThunder.slnx) by walking up from a start
/// directory, so CLIs and tests work regardless of the build output they run from.
/// </summary>
public static class RepoLocator
{
    public static string? FindRepoRoot(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NavyThunder.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static string FindDataDirectory(string? startDirectory = null)
    {
        string? root = FindRepoRoot(startDirectory)
                       ?? throw new DirectoryNotFoundException(
                           "Could not locate the repository root (marker: NavyThunder.slnx). Pass --data explicitly.");

        return Path.Combine(root, "data");
    }
}
