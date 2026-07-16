using System.Text;

namespace OtSnapshotGui;

internal static class AtomicFile
{
    public static void WriteText(string path, string contents)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        RemoveStaleArtifacts(directory, Path.GetFileName(fullPath));

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.bak");
        try
        {
            File.WriteAllText(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            InstallDestination(tempPath, fullPath, backupPath);
        }
        catch
        {
            TryDelete(tempPath);
            TryDelete(backupPath);
            throw;
        }
    }

    private static void RemoveStaleArtifacts(string directory, string leaf)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        try
        {
            foreach (var artifact in Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(artifact);
                if (!name.StartsWith(leaf + ".", StringComparison.OrdinalIgnoreCase) ||
                    (!name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (File.GetLastWriteTimeUtc(artifact) < cutoff)
                {
                    TryDelete(artifact);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cleanup failure must not block a valid settings or server write.
        }
    }

    private static void InstallDestination(string tempPath, string fullPath, string backupPath)
    {
        const int maxRetries = 5;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                    TryDelete(backupPath);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }

                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(10 * (attempt + 1));
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next write can retry cleanup.
        }
    }
}
