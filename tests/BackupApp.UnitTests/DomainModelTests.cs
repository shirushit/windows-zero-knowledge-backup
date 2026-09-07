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
    public void ObjectId_ShouldNormalizeHexToLowercase()
    {
        var id = ObjectId.FromHex("A1B2C3D4E5F6");
        id.Value.Should().Be("a1b2c3d4e5f6");
    }

    [Fact]
    public void Snapshot_CommittedInvariant_ShouldReflectStatus()
    {
        var snapshot = new Snapshot(
            SnapshotId.New(),
            BackupSetId.New(),
            1,
            DateTimeOffset.UtcNow,
            SnapshotStatus.Committed,
            10,
            1024 * 1024
        );

        snapshot.IsCommitted.Should().BeTrue();

        var inProgress = snapshot with { Status = SnapshotStatus.InProgress };
        inProgress.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public void FileMetadata_ShouldHoldConsistentProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var path = CanonicalPath.From("documents/test.txt");
        var metadata = new FileMetadata(path, 1024, now, now, "dummyhash");

        metadata.RelativePath.Value.Should().Be("documents/test.txt");
        metadata.SizeBytes.Should().Be(1024);
        metadata.ContentHashSha256.Should().Be("dummyhash");
    }
}
