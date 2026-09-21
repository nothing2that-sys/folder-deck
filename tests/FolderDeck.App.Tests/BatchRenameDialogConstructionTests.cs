using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="BatchRenameDialog"/>를 실제로 생성해본다(<see cref="StaWpfTestSupport"/>의 STA
/// 요령) — 2026-09-18 손 확인에서 실제로 재현된 크래시(파일을 고르고 Ctrl+F2만 눌러도 매번
/// 터짐)의 회귀 시험이다. 원인은 XAML의 <c>StartBox</c>/<c>PaddingBox</c> 기본값
/// (<c>Text="1"</c>/<c>"3"</c>)이 <c>InitializeComponent()</c> 도중, 생성자가 아직
/// <c>_originalNames</c>를 대입하기 전에 <c>TextChanged</c>를 쏴서 <c>Recompute</c>가 null
/// 참조로 죽는 것이었다 — 크래시 로그(<c>%APPDATA%\FolderDeck\crashes\</c>)로 확인, 코드
/// 자체를 시험이 못 무는 자리라(BatchRenameDialog·PickOneDialog·DuplicateFinderWindow
/// 전부 그동안 이 시험이 없었다) 정확히 여기서 놓쳤다.
/// </summary>
public sealed class BatchRenameDialogConstructionTests
{
    [Fact]
    public void ConstructingWithOneOrMoreNamesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            _ = new BatchRenameDialog(["a.txt", "b.txt"], []);
        });
    }

    [Fact]
    public void ConstructingWithASingleNameDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            _ = new BatchRenameDialog(["only.txt"], []);
        });
    }
}
