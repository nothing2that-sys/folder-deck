using System.Windows;

namespace FolderDeck.App.Views;

/// <summary>이름 바꾸기 입력 창 (§4.6 309행 · A군 ②).</summary>
public partial class RenameDialog : Window
{
    public RenameDialog(string title, string message, string current)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        NameBox.Text = current;

        Loaded += (_, _) =>
        {
            NameBox.Focus();

            // 확장자를 뺀 부분만 고른다 — 탐색기와 같은 감각이다. 전체를 고르면 새 이름만
            // 타이핑한 손이 확장자를 같이 날려 버린다(그 이름은 규칙상 통과하므로 아무도
            // 막아 주지 않는다). 앞자리 점(".gitignore")은 확장자가 아니라 이름이다.
            var dot = current.LastIndexOf('.');
            if (dot > 0)
            {
                NameBox.Select(0, dot);
            }
            else
            {
                NameBox.SelectAll();
            }
        };
    }

    /// <summary>사람이 적은 그대로. <b>다듬지 않는다</b> — 판정은 뷰모델의 일이다.</summary>
    public string? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = NameBox.Text;
        DialogResult = true;
    }
}
