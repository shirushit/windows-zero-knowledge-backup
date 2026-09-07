using BackupApp.UI.Services;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class PathFormatterTests
{
    [Fact]
    public void FormatForRtl_WithEnglishPath_ShouldWrapWithLrm()
    {
        var input = @"C:\Users\owner\Documents\file.txt";
        var formatted = PathFormatter.FormatForRtl(input);

        formatted.Should().StartWith(PathFormatter.Lrm.ToString());
        formatted.Should().EndWith(PathFormatter.Lrm.ToString());
        formatted.Should().Contain(@"\");
    }

    [Fact]
    public void FormatForRtl_WithHebrewPath_ShouldPreserveStructureAndOrder()
    {
        var input = @"C:\מסמכים חשובים\דוחות 2026\סיכום.docx";
        var formatted = PathFormatter.FormatForRtl(input);

        formatted.Should().StartWith(PathFormatter.Lrm.ToString());
        formatted.Should().Contain("דוחות 2026");
        formatted.Should().Contain("סיכום.docx");
    }

    [Theory]
    [InlineData(0, "0 בייט")]
    [InlineData(500, "500 בייט")]
    [InlineData(1024, "1 ק\"ב")]
    [InlineData(1048576, "1 מ\"ב")]
    [InlineData(1073741824, "1 ג\"ב")]
    public void FormatBytes_ShouldFormatAccuratelyInHebrew(long bytes, string expected)
    {
        var result = PathFormatter.FormatBytes(bytes);
        result.Should().Be(expected);
    }
}
