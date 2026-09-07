using BackupApp.Domain;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class DomainModelTests
{
    [Fact]
    public void SnapshotId_ShouldCreateValidUniqueInstance()
    {
        var id1 = SnapshotId.New();
        var id2 = SnapshotId.New();

        id1.Should().NotBe(id2);
        id1.ToString().Should().HaveLength(32);
    }

    [Fact]
    public void FileMetadata_ShouldHoldConsistentProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var metadata = new FileMetadata("documents/test.txt", 1024, now, now, "dummyhash");

        metadata.RelativePath.Should().Be("documents/test.txt");
        metadata.SizeBytes.Should().Be(1024);
        metadata.ContentHashSha256.Should().Be("dummyhash");
    }
}
