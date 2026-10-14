using System.Text.Json;
using System.Text.Json.Serialization;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Layout;

/// <summary>1회용 창의 타일 배치 프리셋 (2026-10-14 S2).</summary>
public enum LayoutPreset
{
    /// <summary>1개 전체.</summary>
    Single,

    /// <summary>좌우 둘.</summary>
    SideBySide,

    /// <summary>상하 둘.</summary>
    Stacked,

    /// <summary>2x2 넷.</summary>
    Quad,
}

/// <summary>
/// 프리셋 → 타일 배치. 4x4 격자 위의 칸 걸침(span)으로만 표현한다 — 렌더러가 4x4 고정이라
/// (<see cref="GridLayout.EnsureGrid"/>) 다른 격자 크기를 쓰지 않는다.
/// </summary>
/// <remarks>
/// 타일은 <b>전부 순환 칸</b>(<see cref="TileKind.Rotating"/>, <c>FolderId = null</c>)이다.
/// <see cref="GridLayout.NormalizeTiles"/> 가 폴더 없는 상시 칸을 지우므로 빈 칸으로 설 수 있는
/// 것은 순환 칸뿐이다. 순서와 <c>RotationIndex</c> 는 읽기 순서(왼→오, 위→아래)다.
/// </remarks>
public static class LayoutPresets
{
    public static readonly IReadOnlyList<LayoutPreset> All =
        [LayoutPreset.Single, LayoutPreset.SideBySide, LayoutPreset.Stacked, LayoutPreset.Quad];

    /// <summary>화면에 보이는 이름.</summary>
    public static string Label(LayoutPreset preset) => preset switch
    {
        LayoutPreset.Single => "1개 전체",
        LayoutPreset.SideBySide => "좌우",
        LayoutPreset.Stacked => "상하",
        LayoutPreset.Quad => "4개",
        _ => preset.ToString(),
    };

    public static List<TileSpec> Build(LayoutPreset preset)
    {
        (int X, int Y, int W, int H)[] cells = preset switch
        {
            LayoutPreset.Single => [(0, 0, 4, 4)],
            LayoutPreset.SideBySide => [(0, 0, 2, 4), (2, 0, 2, 4)],
            LayoutPreset.Stacked => [(0, 0, 4, 2), (0, 2, 4, 2)],
            LayoutPreset.Quad => [(0, 0, 2, 2), (2, 0, 2, 2), (0, 2, 2, 2), (2, 2, 2, 2)],
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

        return
        [
            .. cells.Select((c, i) => new TileSpec
            {
                Kind = TileKind.Rotating,
                FolderId = null,
                CellX = c.X,
                CellY = c.Y,
                SpanX = c.W,
                SpanY = c.H,
                RotationIndex = i + 1,
            }),
        ];
    }
}

/// <summary>
/// <see cref="LayoutPreset"/> 을 너그럽게 읽는다 — 모르는 값이면 <see cref="AppSettings.DefaultScratchLayout"/>.
/// </summary>
/// <remarks>
/// 저장소의 전역 열거형 변환기(<c>allowIntegerValues: false</c>)는 모르는 문자열에서 던진다. 그러면
/// 설정 파일 <b>전체</b>가 손상으로 읽혀 앱이 설정을 덮어쓰지 않는 모드로 떨어진다. 프리셋 하나 때문에
/// 그렇게 되면 안 되므로 이 속성만 램프(<see cref="AppSettings.TileGap"/>)처럼 기본값으로 닫는다.
/// </remarks>
public sealed class LenientLayoutPresetConverter : JsonConverter<LayoutPreset>
{
    public override LayoutPreset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<LayoutPreset>(reader.GetString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        reader.Skip();
        return AppSettings.DefaultScratchLayout;
    }

    public override void Write(Utf8JsonWriter writer, LayoutPreset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
