using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderDeck.Core.Storage;

/// <summary>직렬화 옵션 (§7.2 — System.Text.Json 내장만 사용).</summary>
public static class FolderDeckJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            // 사람이 파일을 열어 읽고 손으로 고칠 수 있게.
            WriteIndented = true,

            // 한글 제목·설명이 \uXXXX 로 escape 되지 않게 한다. 출력은 로컬 JSON 파일이고
            // HTML/스크립트에 끼워 넣지 않으므로 relaxed escaping 이 맞다.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            // null 옵셔널 슬롯(grid/tiles/window/copyTray/macros)은 파일에 쓰지 않는다.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,

            // 손으로 고친 파일의 주석은 허용, 알 수 없는 필드는 무시(앞으로 늘어날 슬롯 대비).
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // enum 은 "list"/"details"/"largeIcons"처럼 camelCase 문자열로. 숫자는 받지 않고,
        // 모르는 값이면 JsonException → CorruptData 로 보고된다(조용히 기본값으로 넘기지 않음).
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

        return options;
    }
}
