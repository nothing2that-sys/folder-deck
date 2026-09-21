using System.Windows;
using System.Windows.Input;

namespace FolderDeck.App.Views;

/// <summary>여럿 중 하나를 글자 목록으로 고르는 범용 창(2026-09-18).</summary>
public partial class PickOneDialog : Window
{
    public PickOneDialog(string title, string message, IReadOnlyList<string> options)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        OptionList.ItemsSource = options;

        // 첫 항목을 기본으로 골라 둔다 — 안 그러면 SelectedIndex 가 -1로 시작해서, 목록을
        // 직접 클릭해 고르지 않고 그냥 Enter/「고르기」를 누르면(AcceptButton.IsDefault) 아무
        // 일도 없이 조용히 취소된다(2026-09-18 손 확인 — 후보가 하나뿐일 때 특히 그렇게
        // 보인다, "이벤트조차 없음"). 후보가 하나면 이미 원하는 답이라 그대로 확정되고,
        // 여럿이면 눈에 보이는 기본값일 뿐 언제든 다시 골라 바꿀 수 있다.
        if (options.Count > 0)
        {
            OptionList.SelectedIndex = 0;
        }

        Loaded += (_, _) => OptionList.Focus();
    }

    /// <summary>고른 자리(0부터). 취소면 <c>null</c>.</summary>
    public int? PickedIndex { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var index = OptionList.SelectedIndex;
        PickedIndex = index >= 0 ? index : null;
        DialogResult = PickedIndex is not null;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OptionList.SelectedIndex >= 0)
        {
            PickedIndex = OptionList.SelectedIndex;
            DialogResult = true;
        }
    }
}
