using BackupApp.BackupEngine.Capture;
using BackupApp.BackupEngine.Scanner;
using BackupApp.Domain;

namespace BackupApp.BackupEngine.ChangeDetection;

public enum FileChangeType
{
    New,
    Modified,
    Unchanged,
    Deleted,
    Renamed
}

public record DetectedFileChange(
    FileChangeType ChangeType,
    CanonicalPath Path,
    CanonicalPath? OldPath,
    DiscoveredFile? Discovered,
    FileVersion? PreviousVersion,
    FileEntryId? ReusedFileEntryId
);

public interface IChangeDetectionService
{
    Task<IReadOnlyList<DetectedFileChange>> DetectChangesAsync(
        IReadOnlyList<DiscoveredFile> currentFiles,
        IReadOnlyList<FileVersion> previousSnapshotFiles,
        IStableFileCaptureService captureService,
        CancellationToken cancellationToken = default
    );
}

public sealed class ChangeDetectionService : IChangeDetectionService
{
    public async Task<IReadOnlyList<DetectedFileChange>> DetectChangesAsync(
        IReadOnlyList<DiscoveredFile> currentFiles,
        IReadOnlyList<FileVersion> previousSnapshotFiles,
        IStableFileCaptureService captureService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentFiles);
        ArgumentNullException.ThrowIfNull(previousSnapshotFiles);
        ArgumentNullException.ThrowIfNull(captureService);

        var previousByPath = previousSnapshotFiles.ToDictionary(f => f.Path, f => f);
        var remainingPreviousByPath = previousSnapshotFiles.ToDictionary(f => f.Path, f => f);
        var results = new List<DetectedFileChange>();
        var potentiallyNew = new List<DiscoveredFile>();

        foreach (var file in currentFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (previousByPath.TryGetValue(file.Path, out var prev))
            {
                remainingPreviousByPath.Remove(file.Path);

                // Quick metadata check: if size and last write time match (within 1 second tolerance), mark Unchanged without reading file
                bool isTimestampMatch = file.ModifiedUtc == prev.ModifiedUtc ||
                                        Math.Abs((file.ModifiedUtc - prev.ModifiedUtc).TotalSeconds) < 1.0;

                if (file.SizeBytes == prev.SizeBytes && isTimestampMatch)
                {
                    results.Add(new DetectedFileChange(
                        FileChangeType.Unchanged,
                        file.Path,
                        null,
                        file,
                        prev,
                        prev.FileEntryId
                    ));
                    continue;
                }

                // If size or timestamp differs, mark as Modified to trigger hash calculation, chunking, encryption and upload
                results.Add(new DetectedFileChange(
                    FileChangeType.Modified,
                    file.Path,
                    null,
                    file,
                    prev,
                    prev.FileEntryId
                ));
            }
            else
            {
                potentiallyNew.Add(file);
            }
        }

        // Rename detection: only examine potentially new files if there are prior deleted files to match against
        if (remainingPreviousByPath.Count > 0 && potentiallyNew.Count > 0)
        {
            var previousByHashAndSize = remainingPreviousByPath.Values
                .GroupBy(f => (f.SizeBytes, f.ContentHashSha256))
                .ToDictionary(g => g.Key, g => new Queue<FileVersion>(g));

            var possibleSizes = new HashSet<long>(remainingPreviousByPath.Values.Select(f => f.SizeBytes));

            foreach (var file in potentiallyNew)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (possibleSizes.Contains(file.SizeBytes))
                {
                    var capture = await captureService.CaptureFileAsync(file.AbsolutePath, cancellationToken: cancellationToken).ConfigureAwait(false);
                    var hash = capture.Status == StableCaptureStatus.Success ? capture.ContentHashSha256 : null;

                    if (hash != null && previousByHashAndSize.TryGetValue((file.SizeBytes, hash), out var candidates) && candidates.Count > 0)
                    {
                        var renamedFrom = candidates.Dequeue();
                        remainingPreviousByPath.Remove(renamedFrom.Path);

                        results.Add(new DetectedFileChange(
                            FileChangeType.Renamed,
                            file.Path,
                            renamedFrom.Path,
                            file,
                            renamedFrom,
                            renamedFrom.FileEntryId
                        ));
                        continue;
                    }
                }

                results.Add(new DetectedFileChange(
                    FileChangeType.New,
                    file.Path,
                    null,
                    file,
                    null,
                    null
                ));
            }
        }
        else
        {
            foreach (var file in potentiallyNew)
            {
                results.Add(new DetectedFileChange(
                    FileChangeType.New,
                    file.Path,
                    null,
                    file,
                    null,
                    null
                ));
            }
        }

        // All files remaining in previous snapshot are marked as Deleted
        foreach (var deleted in remainingPreviousByPath.Values)
        {
            results.Add(new DetectedFileChange(
                FileChangeType.Deleted,
                deleted.Path,
                null,
                null,
                deleted,
                deleted.FileEntryId
            ));
        }

        return results;
    }
}
