using System.Windows.Controls;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="PickOneDialog"/>를 실제로 생성해본다(<see cref="StaWpfTestSupport"/>의 STA
/// 요령) — 2026-09-21 손 확인에서 실제로 재현된 결함의 회귀 시험이다.
/// </summary>
/// <remarks>
/// 증상: 타일 비교(Ctrl+D)에서 상대 타일을 고르는 다이얼로그가 뜬 뒤, 「고르기」를
/// 눌러도(또는 Enter — AcceptButton.IsDefault) 비교가 전혀 시작되지 않고 상태 바 안내
/// 문구조차 안 떴다("이렇다 할 이벤트조차 없음", JH 보고). 원인: <c>ListBox</c>가 기본
/// 선택 없이(<c>SelectedIndex == -1</c>) 뜨고, <c>Loaded</c> 핸들러는 키보드 포커스만
/// 옮길 뿐 항목을 선택하지 않는다 — 목록을 직접 클릭해 고르지 않은 채 확인을 누르면
/// <c>PickedIndex</c>가 <c>null</c>이 되어 <c>DialogResult</c>가 <c>false</c>로 닫히고,
/// <c>MainViewModel.OnCompareRequested</c>는 <c>picked is null</c>이면 아무 경고도 없이
/// 조용히 되돌아간다 — 후보가 하나뿐인 흔한 경우(타일 두 개만 열려 있을 때)에 특히 이
/// 모양으로 나타난다.
/// </remarks>
public sealed class PickOneDialogConstructionTests
{
    [Fact]
    public void ConstructingWithOptionsDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            _ = new PickOneDialog("제목", "안내", ["산출물"]);
        });
    }

    [Fact]
    public void FirstOptionIsSelectedByDefault()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var dialog = new PickOneDialog("제목", "안내", ["산출물", "작업"]);
            var list = (ListBox)dialog.FindName("OptionList")!;

            Assert.Equal(0, list.SelectedIndex);
        });
    }

    [Fact]
    public void NoOptionsLeavesNothingSelectedButDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var dialog = new PickOneDialog("제목", "안내", []);
            var list = (ListBox)dialog.FindName("OptionList")!;

            Assert.Equal(-1, list.SelectedIndex);
        });
    }
}
