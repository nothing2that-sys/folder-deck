using System.Text.Json;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;










public static class DragPayloadCodec
{





    private static readonly JsonSerializerOptions Options = new();

    public static string Pack(FileDropPayload payload) => JsonSerializer.Serialize(payload, Options);






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
