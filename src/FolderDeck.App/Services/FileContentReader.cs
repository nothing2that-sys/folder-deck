using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FolderDeck.App.Services;

/// <summary>
/// 내용 비교(기능 후보 ①의 연장, 2026-09-18)가 파일을 문자열로 읽어들이는 자리 — DiffPlex
/// (<see cref="FolderDeck.Core.Comparison.FileContentDiffer"/>)는 텍스트만 받으므로, 이진
/// 판정과 인코딩 추정을 여기서 끝내고 <see cref="MainViewModel"/> 은 문자열만 받는다.
/// </summary>
/// <remarks>
/// 코드페이지(CP949 등) 등록은 정적 생성자에서 한 번만 한다 — 앱의 <c>OnStartup</c> 훅에
/// 기대지 않는다, 그래야 시험 프로세스에서도 그대로 동작한다.
/// </remarks>
internal static class FileContentReader
{
    /// <summary>내용 비교가 다루는 파일 크기 상한 — DiffPlex 는 스트리밍이 아니라 전체를
    /// 메모리에 올려 계산하므로, 큰 로그 파일 등에서 앱이 멈춘 것처럼 보이는 것을 막는다.</summary>
    internal const long MaxDiffFileBytes = 5 * 1024 * 1024;

    private const int BinarySniffBytes = 8000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static FileContentReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static (string? Text, string? FailureReason) TryReadText(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return (null, $"경로를 읽을 수 없다 — {ex.Message}");
        }

        if (!info.Exists)
        {
            return (null, $"파일이 없다 — {path}");
        }

        if (info.Length > MaxDiffFileBytes)
        {
            return (null, $"파일이 너무 크다({info.Length / (1024 * 1024)}MB) — " +
                          $"내용 비교는 {MaxDiffFileBytes / (1024 * 1024)}MB까지만 지원한다");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"파일을 읽지 못했다 — {ex.Message}");
        }

        return LooksBinary(bytes)
            ? (null, "이진 파일로 보인다 — 내용 비교는 텍스트 파일만 지원한다")
            : (DecodeText(bytes), null);
    }

    /// <summary>앞쪽 <see cref="BinarySniffBytes"/> 바이트 안에 NUL이 있으면 이진으로 본다 —
    /// git 등이 쓰는 것과 같은 흔한 휴리스틱이다.</summary>
    internal static bool LooksBinary(byte[] bytes) =>
        Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinarySniffBytes)) >= 0;

    /// <summary>
    /// BOM이 있으면 그대로 따르고, 없으면 UTF-8로 엄격하게 시도한 뒤 실패하면 CP949(한국어
    /// 환경의 레거시 텍스트 인코딩)로 한 번 더 시도한다. <b>이 메서드는 던지지 않는다</b> —
    /// <c>Encoding.GetEncoding(949)</c>는 기본으로 예외 폴백이 아니라 치환 폴백을 쓰므로
    /// (실측, 2026-09-18 — <c>FileContentReaderTests
    /// .NeitherUtf8NorCp949BytesDecodeWithoutThrowingInsteadOfCrashing</c>), UTF-8도 CP949도
    /// 아닌 인코딩(Shift-JIS·Latin-1 등)의 텍스트는 예외가 아니라 <b>조용히 깨진 문자로</b>
    /// 나온다 — 자동 인코딩 감지를 더 하지 않는 V1이 받아들인 한계다.
    /// </summary>
    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }
}
