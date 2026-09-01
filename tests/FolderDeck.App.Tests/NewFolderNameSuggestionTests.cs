using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;

/// <summary>
/// 「새 폴더 만들기」모달 기본값 (§4.6 · D75-b 6-2). 모달 자체는 이 회차가 아니다 —
/// <see cref="FolderPanelViewModel.SuggestNewFolderName"/> 하나만 문다.
/// </summary>
public sealed class NewFolderNameSuggestionTests : IDisposable
{
    private readonly string _root;

    public NewFolderNameSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.NewFolderNameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 무시.
        }
    }

    // 6
    [Fact]
    public void SuggestsPlainNameWhenFree() =>
        Assert.Equal("새 폴더", FolderPanelViewModel.SuggestNewFolderName(_root));

    // 7
    [Fact]
    public void SuggestsNumberedNameWhenTaken()
    {
        Directory.CreateDirectory(Path.Combine(_root, "새 폴더"));

        Assert.Equal("새 폴더 (2)", FolderPanelViewModel.SuggestNewFolderName(_root));
    }
}
