using System.Text;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

public sealed class FileContentReaderTests
{
    [Fact]
    public void DecodesPlainUtf8Text()
    {
        var bytes = Encoding.UTF8.GetBytes("hello 안녕");

        Assert.Equal("hello 안녕", FileContentReader.DecodeText(bytes));
    }

    [Fact]
    public void StripsUtf8BomBeforeDecoding()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("bom 있음")).ToArray();

        Assert.Equal("bom 있음", FileContentReader.DecodeText(withBom));
    }

    [Fact]
    public void FallsBackToCp949WhenNotValidUtf8()
    {
        var cp949 = Encoding.GetEncoding(949).GetBytes("한국어 레거시 텍스트");

        Assert.Equal("한국어 레거시 텍스트", FileContentReader.DecodeText(cp949));
    }

    [Fact]
    public void NeitherUtf8NorCp949BytesDecodeWithoutThrowingInsteadOfCrashing()
    {

        byte[] invalid = [0x80, 0x81, 0xFF, 0xFE, 0x8F];

        var result = Record.Exception(() => FileContentReader.DecodeText(invalid));

        Assert.Null(result);
    }

    [Fact]
    public void PlainAsciiTextIsNotBinary()
    {
        Assert.False(FileContentReader.LooksBinary(Encoding.ASCII.GetBytes("plain text")));
    }

    [Fact]
    public void BytesWithANulAreBinary()
    {
        Assert.True(FileContentReader.LooksBinary([0x50, 0x4B, 0x00, 0x03]));
    }

    [Fact]
    public void EmptyBytesAreNotBinary()
    {
        Assert.False(FileContentReader.LooksBinary([]));
    }

    [Fact]
    public void TryReadTextFailsWithAReasonWhenTheFileIsMissing()
    {
        var (text, reason) = FileContentReader.TryReadText(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Null(text);
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryReadTextRejectsABinaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        File.WriteAllBytes(path, [0x50, 0x4B, 0x00, 0x03]);

        try
        {
            var (text, reason) = FileContentReader.TryReadText(path);

            Assert.Null(text);
            Assert.Contains("이진", reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadTextRejectsAFileOverTheSizeCap()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        File.WriteAllBytes(path, new byte[FileContentReader.MaxDiffFileBytes + 1]);

        try
        {
            var (text, reason) = FileContentReader.TryReadText(path);

            Assert.Null(text);
            Assert.Contains("크다", reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryReadTextReturnsTheDecodedContentForAnOrdinaryTextFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        File.WriteAllText(path, "line1\nline2", Encoding.UTF8);

        try
        {
            var (text, reason) = FileContentReader.TryReadText(path);

            Assert.Null(reason);
            Assert.Equal("line1\nline2", text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
