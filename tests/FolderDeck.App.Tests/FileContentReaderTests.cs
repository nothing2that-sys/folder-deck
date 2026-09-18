using System.Text;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// 내용 비교(기능 후보 ①의 연장, 2026-09-18)가 파일을 문자열로 읽는 자리
/// (<see cref="FileContentReader"/>) — 이진 판정과 인코딩 추정(BOM·UTF-8·CP949).
/// </summary>
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

    /// <summary>
    /// UTF-8 도 CP949 도 아닌 바이트(예: Shift-JIS·Latin-1 텍스트)는 예외가 아니라 조용히
    /// 깨진 문자로 떨어진다 — <c>Encoding.GetEncoding(949)</c>는 기본으로 예외 폴백이 아니라
    /// 치환 폴백을 쓴다(실측, 2026-09-18). <see cref="DecodeText"/>가 이 경로에서 절대
    /// 던지지 않는다는 것을 못박아 둔다 — 호출부(<c>TryReadText</c>)가 이 경우를 별도로
    /// 잡을 필요가 없다는 근거다.
    /// </summary>
    [Fact]
    public void NeitherUtf8NorCp949BytesDecodeWithoutThrowingInsteadOfCrashing()
    {
        // 0x80 로 시작 — UTF-8 이어짐 바이트가 선두에 온 것이라 이미 UTF-8 로 무효고, BOM
        // 검사(EF BB BF · FF FE · FE FF)에도 안 걸린다 — CP949 폴백까지 실제로 타게 한다.
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
