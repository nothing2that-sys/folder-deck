using System.Windows;
using FolderDeck.Core.Renaming;

namespace FolderDeck.App.Views;


public partial class BatchRenameDialog : Window
{
    private readonly IReadOnlyList<string> _originalNames;
    private readonly IReadOnlyCollection<string> _otherExistingNames;
    private IReadOnlyList<RenamePreviewRow> _lastRows = [];

    public BatchRenameDialog(IReadOnlyList<string> originalNames, IReadOnlyCollection<string> otherExistingNames)
    {
        InitializeComponent();
        _originalNames = originalNames;
        _otherExistingNames = otherExistingNames;








        FindReplaceRadio.IsChecked = true;
        UpperRadio.IsChecked = true;
        StartBox.Text = "1";
        PaddingBox.Text = "3";
    }


    public IReadOnlyList<RenamePreviewRow>? Result { get; private set; }

    private RenameRuleKind SelectedRuleKind =>
        NumberingRadio.IsChecked == true ? RenameRuleKind.Numbering
        : CaseRadio.IsChecked == true ? RenameRuleKind.ChangeCase
        : RenameRuleKind.FindReplace;

    private CaseChangeKind SelectedCaseChange =>
        LowerRadio.IsChecked == true ? CaseChangeKind.Lower
        : TitleRadio.IsChecked == true ? CaseChangeKind.TitleCase
        : CaseChangeKind.Upper;

    private void OnRuleChanged(object sender, RoutedEventArgs e)
    {
        FindReplacePanel.Visibility = FindReplaceRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        NumberingPanel.Visibility = NumberingRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CasePanel.Visibility = CaseRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Recompute();
    }

    private void OnInputChanged(object sender, RoutedEventArgs e) => Recompute();

    private void Recompute()
    {
        var rule = SelectedRuleKind switch
        {
            RenameRuleKind.Numbering => new RenameRule(
                RenameRuleKind.Numbering,
                NumberPrefix: PrefixBox.Text,
                NumberStart: ParseIntOr(StartBox.Text, 1),
                NumberPadding: ParseIntOr(PaddingBox.Text, 3)),
            RenameRuleKind.ChangeCase => new RenameRule(RenameRuleKind.ChangeCase, CaseChange: SelectedCaseChange),
            _ => new RenameRule(RenameRuleKind.FindReplace, Find: FindBox.Text, Replace: ReplaceBox.Text),
        };

        _lastRows = BatchRenamePlanner.Preview(_originalNames, rule, _otherExistingNames);
        PreviewList.ItemsSource = _lastRows;

        var blocked = _lastRows.Count(row => row.IsBlocked);
        StatusText.Text = blocked > 0
            ? $"충돌하거나 쓸 수 없는 이름이 {blocked}개 있다 — 고치기 전엔 실행할 수 없다."
            : string.Empty;
        ExecuteButton.IsEnabled = blocked == 0;
    }

    private static int ParseIntOr(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

    private void OnExecute(object sender, RoutedEventArgs e)
    {
        Result = _lastRows;
        DialogResult = true;
    }
}
