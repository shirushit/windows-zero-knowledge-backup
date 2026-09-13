using System.Security.Cryptography;
using System.Text;

namespace BackupApp.StressTests;

public sealed class GeneratedFileInfo
{
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256Hex { get; init; }
}

public static class SyntheticFileTreeGenerator
{
    public static async Task<IReadOnlyDictionary<string, GeneratedFileInfo>> GenerateAsync(
        string rootDirectory,
        bool includeLockedFile = true)
    {
        Directory.CreateDirectory(rootDirectory);
        var files = new Dictionary<string, GeneratedFileInfo>(StringComparer.OrdinalIgnoreCase);

        var random = new Random(1337);

        // 1. Hebrew directories & files
        var hebrewDir = Path.Combine(rootDirectory, "מסמכים וחוזים");
        Directory.CreateDirectory(hebrewDir);
        await AddFileAsync(files, rootDirectory, Path.Combine(hebrewDir, "הסכם_סודיות_2026.docx"), GenerateBytes(random, 12 * 1024));
        await AddFileAsync(files, rootDirectory, Path.Combine(hebrewDir, "דוח_כספי_סופי_רבעון_1.xlsx"), GenerateBytes(random, 45 * 1024));

        // 2. Deeply nested directories with spaces and punctuation
        var deepDir = Path.Combine(rootDirectory, "Deep Level 1", "תיקייה פנימית 2", "Level 3 with (Special # & %)");
        Directory.CreateDirectory(deepDir);
        await AddFileAsync(files, rootDirectory, Path.Combine(deepDir, "רשימת מטלות [דחוף].txt"), Encoding.UTF8.GetBytes("משימה 1: גיבוי באפס ידע\nמשימה 2: אימות שלמות נתונים!"));
        await AddFileAsync(files, rootDirectory, Path.Combine(deepDir, "data_metrics.csv"), GenerateBytes(random, 128 * 1024));

        // 3. Medium size files (1MB, 2MB)
        var mediaDir = Path.Combine(rootDirectory, "מדיה ותמונות");
        Directory.CreateDirectory(mediaDir);
        await AddFileAsync(files, rootDirectory, Path.Combine(mediaDir, "photo_large_hdr.png"), GenerateBytes(random, 1024 * 1024));
        await AddFileAsync(files, rootDirectory, Path.Combine(mediaDir, "video_sample_clip.mp4"), GenerateBytes(random, 2 * 1024 * 1024));

        // 4. Large file (8MB) to trigger multi-chunking
        var largeDir = Path.Combine(rootDirectory, "קבצים גדולים");
        Directory.CreateDirectory(largeDir);
        await AddFileAsync(files, rootDirectory, Path.Combine(largeDir, "archive_db_dump.bin"), GenerateBytes(random, 8 * 1024 * 1024));

        // 5. Very small edge case files (0 bytes, 1 byte, 10 bytes)
        var edgeDir = Path.Combine(rootDirectory, "קצוות ובדיקות");
        Directory.CreateDirectory(edgeDir);
        await AddFileAsync(files, rootDirectory, Path.Combine(edgeDir, "empty_file.empty"), []);
        await AddFileAsync(files, rootDirectory, Path.Combine(edgeDir, "single_byte.dat"), [0x42]);
        await AddFileAsync(files, rootDirectory, Path.Combine(edgeDir, "few_bytes.dat"), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        // 6. In-use file (opened with FileShare.ReadWrite)
        if (includeLockedFile)
        {
            var lockedPath = Path.Combine(rootDirectory, "active_in_use_log.log");
            var lockedData = Encoding.UTF8.GetBytes("Log started at " + DateTime.UtcNow.ToString("O") + "\nActive stream writing...");
            await AddFileAsync(files, rootDirectory, lockedPath, lockedData);
        }

        return files;
    }

    private static async Task AddFileAsync(
        Dictionary<string, GeneratedFileInfo> files,
        string rootDirectory,
        string fullPath,
        byte[] content)
    {
        await File.WriteAllBytesAsync(fullPath, content);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var relPath = Path.GetRelativePath(rootDirectory, fullPath);

        files[relPath] = new GeneratedFileInfo
        {
            RelativePath = relPath,
            FullPath = fullPath,
            SizeBytes = content.Length,
            Sha256Hex = hash
        };
    }

    private static byte[] GenerateBytes(Random random, int size)
    {
        var bytes = new byte[size];
        random.NextBytes(bytes);
        return bytes;
    }
}
