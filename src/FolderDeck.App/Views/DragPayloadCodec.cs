using System.Text.Json;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;

/// <summary>
/// 사설 드래그 포맷에 싣는 값을 객체가 아니라 글자로 바꾼다 (§4.9 · Phase 14 D77-b).
/// </summary>
/// <remarks>
/// 객체를 그대로 <c>DataObject.SetData</c> 에 실으면 같은 프로세스 안에서는 참조가 그대로
/// 돌아오지만, 프로세스 경계를 넘으면 마샬링되지 않아 「있는데 못 꺼내는」 상태가 된다
/// (§4.9, D76). 글자는 경계를 넘으므로 여기서 <see cref="FileDropPayload"/> 를 JSON 문자열로
/// 바꾼다 — 레코드 선언 자체는 손대지 않는다.
/// </remarks>
public static class DragPayloadCodec
{
    /// <summary>
    /// 사람이 읽거나 손으로 고치는 파일이 아니라 드래그 한 번 동안만 존재하는 전송용
    /// 문자열이다 — <c>FolderDeckJson.Options</c>(들여쓰기 · 저장 파일용 관대한 옵션)을
    /// 재사용하지 않고 기본값을 그대로 쓴다.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new();

    public static string Pack(FileDropPayload payload) => JsonSerializer.Serialize(payload, Options);

    /// <summary>
    /// 못 풀면 <c>null</c> 이다 — 던지지 않는다. JSON 이 아닌 글자는
    /// <see cref="JsonException"/> 으로 잡고, JSON 이지만 모양이 달라 항목 목록을 못 채우면
    /// (<see cref="FileDropPayload.Items"/> 가 <c>null</c>) 그것도 실패로 본다.
    /// </summary>
    public static FileDropPayload? Unpack(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<FileDropPayload>(text, Options);
            return payload?.Items is null ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
