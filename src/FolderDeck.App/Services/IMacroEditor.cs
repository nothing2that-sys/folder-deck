using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

public sealed record MacroDraft(
    string Name,
    FileOperationKind Op,
    MacroSourceKind SourceKind,
    string? FixedPath,
    MacroDestKind DestKind,
    ConflictPolicy OnConflict,
    bool Confirm,

    bool RetargetFixedPath = false,
    bool RepinFolderIds = false);

public interface IMacroEditor
{

    MacroDraft? Edit(MacroDraft draft, bool canFixPath, bool isEdit);
}
