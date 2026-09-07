using BackupApp.Domain;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class CanonicalPathTests
{
    [Theory]
    [InlineData(@"folder\subfolder\file.txt", "folder/subfolder/file.txt")]
    [InlineData("/leading/slash/file.txt", "leading/slash/file.txt")]
    [InlineData("trailing/slash/folder/", "trailing/slash/folder")]
    [InlineData(@"mix/of\slashes/file.doc", "mix/of/slashes/file.doc")]
    [InlineData("single_file.bin", "single_file.bin")]
    public void CanonicalPath_ShouldNormalizeSlashes(string raw, string expected)
    {
        var path = CanonicalPath.From(raw);
        path.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(@"\")]
    [InlineData("//")]
    [InlineData("folder//empty_segment")]
    public void CanonicalPath_ShouldRejectEmptyPathsOrSegments(string invalidPath)
    {
        var act = () => CanonicalPath.From(invalidPath);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("folder/../escape.txt")]
    [InlineData("folder/./current.txt")]
    [InlineData("./current.txt")]
    public void CanonicalPath_ShouldRejectPathTraversal(string traversalPath)
    {
        var act = () => CanonicalPath.From(traversalPath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*traversal*");
    }

    [Theory]
    [InlineData("file<name.txt")]
    [InlineData("file>name.txt")]
    [InlineData("file:name.txt")]
    [InlineData("file\"name.txt")]
    [InlineData("file|name.txt")]
    [InlineData("file?name.txt")]
    [InlineData("file*name.txt")]
    public void CanonicalPath_ShouldRejectIllegalCharacters(string illegalPath)
    {
        var act = () => CanonicalPath.From(illegalPath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*illegal*");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("PRN")]
    [InlineData("aux.dat")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("com9.log")]
    [InlineData("folder/LPT1/sub.txt")]
    public void CanonicalPath_ShouldRejectReservedDeviceNames(string reservedPath)
    {
        var act = () => CanonicalPath.From(reservedPath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*reserved*");
    }

    [Fact]
    public void CanonicalPath_Equals_ShouldBeCaseInsensitive()
    {
        var path1 = CanonicalPath.From("Folder/SubFolder/File.TXT");
        var path2 = CanonicalPath.From("folder/subfolder/file.txt");

        path1.Should().Be(path2);
        (path1 == path2).Should().BeTrue();
    }

    [Fact]
    public void CanonicalPath_ResolveUnder_ShouldProduceSafeAbsolutePath()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "test-restore-root");
        var path = CanonicalPath.From("sub/folder/file.txt");

        var resolved = path.ResolveUnder(tempBase);
        var expected = Path.GetFullPath(Path.Combine(tempBase, "sub", "folder", "file.txt"));

        resolved.Should().Be(expected);
    }

    [Fact]
    public void CanonicalPath_GetFileNameAndParent_ShouldExtractCorrectly()
    {
        var path = CanonicalPath.From("photos/summer2026/vacation.jpg");

        path.GetFileName().Should().Be("vacation.jpg");
        path.GetParentDirectory().Should().Be("photos/summer2026");

        var rootFile = CanonicalPath.From("notes.txt");
        rootFile.GetFileName().Should().Be("notes.txt");
        rootFile.GetParentDirectory().Should().BeNull();
    }
}
