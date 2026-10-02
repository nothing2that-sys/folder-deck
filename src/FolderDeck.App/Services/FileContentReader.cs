using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FolderDeck.App.Services;

internal static class FileContentReader
{

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

    internal static bool LooksBinary(byte[] bytes) =>
        Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinarySniffBytes)) >= 0;

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
