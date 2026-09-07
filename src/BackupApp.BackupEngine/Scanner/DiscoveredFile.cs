using BackupApp.Domain;

namespace BackupApp.BackupEngine.Scanner;

public record DiscoveredFile(
    CanonicalPath Path,
    string AbsolutePath,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedUtc,
    FileEntryAttributes Attributes,
    bool IsReparsePoint
);
