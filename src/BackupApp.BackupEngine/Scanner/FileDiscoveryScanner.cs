using BackupApp.Domain;

namespace BackupApp.BackupEngine.Scanner;

public interface IFileDiscoveryScanner
{
    IAsyncEnumerable<DiscoveredFile> DiscoverFilesAsync(
        string rootDirectory,
        ExclusionFilter exclusionFilter,
        Action<string, Exception>? onScanWarning = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class FileDiscoveryScanner : IFileDiscoveryScanner
{
    public async IAsyncEnumerable<DiscoveredFile> DiscoverFilesAsync(
        string rootDirectory,
        ExclusionFilter exclusionFilter,
        Action<string, Exception>? onScanWarning = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(exclusionFilter);

        var rootInfo = new DirectoryInfo(rootDirectory);
        if (!rootInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Root backup directory does not exist: {rootDirectory}");
        }

        var queue = new Queue<DirectoryInfo>();
        queue.Enqueue(rootInfo);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDir = queue.Dequeue();

            // Skip reparse points (junctions/symlinks) to prevent cycles
            if (currentDir.Attributes.HasFlag(FileAttributes.ReparsePoint) && !ReferenceEquals(currentDir, rootInfo))
            {
                continue;
            }

            // Check if directory itself is excluded
            if (!ReferenceEquals(currentDir, rootInfo))
            {
                var dirCanonical = PathNormalizer.ToCanonicalPath(rootDirectory, currentDir.FullName);
                if (exclusionFilter.IsExcluded(dirCanonical, isDirectory: true))
                {
                    continue;
                }
            }

            // Enumerate subdirectories
            IEnumerable<DirectoryInfo> subDirs = [];
            try
            {
                subDirs = currentDir.EnumerateDirectories();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                onScanWarning?.Invoke(currentDir.FullName, ex);
                continue;
            }

            foreach (var subDir in subDirs)
            {
                queue.Enqueue(subDir);
            }

            // Enumerate files
            IEnumerable<FileInfo> files = [];
            try
            {
                files = currentDir.EnumerateFiles();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                onScanWarning?.Invoke(currentDir.FullName, ex);
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip symlinks
                var isReparse = file.Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (isReparse)
                {
                    continue;
                }

                var canonicalPath = PathNormalizer.ToCanonicalPath(rootDirectory, file.FullName);
                if (exclusionFilter.IsExcluded(canonicalPath, isDirectory: false))
                {
                    continue;
                }

                var attributes = MapAttributes(file.Attributes);

                yield return new DiscoveredFile(
                    canonicalPath,
                    file.FullName,
                    file.Length,
                    file.CreationTimeUtc,
                    file.LastWriteTimeUtc,
                    attributes,
                    isReparse
                );
            }

            // Yield control periodically to avoid starving cooperative threads
            await Task.Yield();
        }
    }

    private static FileEntryAttributes MapAttributes(FileAttributes attr)
    {
        var result = FileEntryAttributes.None;
        if (attr.HasFlag(FileAttributes.ReadOnly)) result |= FileEntryAttributes.ReadOnly;
        if (attr.HasFlag(FileAttributes.Hidden)) result |= FileEntryAttributes.Hidden;
        if (attr.HasFlag(FileAttributes.System)) result |= FileEntryAttributes.System;
        if (attr.HasFlag(FileAttributes.Archive)) result |= FileEntryAttributes.Archive;
        if (attr.HasFlag(FileAttributes.Encrypted)) result |= FileEntryAttributes.Encrypted;
        return result;
    }
}
