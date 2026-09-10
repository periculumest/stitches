using System.Text.RegularExpressions;

namespace StitchHelper;

public static class TemporaryFiles
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "stitch-helper-staging");
    public static string CreatePath(string purpose)
    {
        if (purpose is not ("import" or "read" or "export")) throw new ArgumentException("Unknown staging purpose.");
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, purpose + "-" + Guid.NewGuid().ToString("N"));
    }

    public static void Cleanup()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var path in Directory.EnumerateFiles(Root))
        {
            if (!Regex.IsMatch(Path.GetFileName(path), "^(import|read|export)-[a-f0-9]{32}$") || File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-24)) continue;
            try
            {
                // Only our staging files, older than a day and not held open by another operation.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) { } // Active file or another worker already removed it.
        }
    }
}
