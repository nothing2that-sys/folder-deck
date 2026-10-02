namespace FolderDeck.App.Services;





public sealed record SettingsEditRequest(
    IReadOnlyDictionary<string, string> ExtensionGlyphs,
    bool ShowShellIcons,
    int EverythingMaxResults,
    EverythingStatus EverythingStatus);


public sealed record SettingsEditResult(
    IReadOnlyDictionary<string, string> ExtensionGlyphs,
    bool ShowShellIcons,
    int EverythingMaxResults);






public interface ISettingsEditor
{



    SettingsEditResult? Edit(SettingsEditRequest request);
}






internal sealed class NullSettingsEditor : ISettingsEditor
{
    public static readonly NullSettingsEditor Instance = new();

    private NullSettingsEditor()
    {
    }

    public SettingsEditResult? Edit(SettingsEditRequest request) => null;
}
