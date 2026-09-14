using System.Xml.Linq;

namespace FolderDeck.App.Tests;

public sealed class IconNameLayoutTests
{
    [Theory]
    [InlineData("LargeIconList", "144", "132")]
    [InlineData("ExtraLargeIconList", "168", "156")]
    public void IconNamesUseThreeWrappedLinesWithoutEllipsis(
        string listName, string expectedCellHeight, string expectedContentHeight)
    {
        var document = XDocument.Load(FindRepositoryFile("src", "FolderDeck.App", "Views", "FolderPanelView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = document.Descendants()
            .Single(element => element.Name.LocalName == "ListView" && (string?)element.Attribute(x + "Name") == listName);
        var panel = list.Descendants().Single(element => element.Name.LocalName == "VirtualizingWrapPanel");
        var stack = list.Descendants().Single(element => element.Name.LocalName == "StackPanel");
        var name = list.Descendants()
            .Single(element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding Name}");

        Assert.Equal(expectedCellHeight, (string?)panel.Attribute("ItemHeight"));
        Assert.Equal(expectedContentHeight, (string?)stack.Attribute("Height"));
        Assert.Equal("48", (string?)name.Attribute("Height"));
        Assert.Equal("Wrap", (string?)name.Attribute("TextWrapping"));
        Assert.Null(name.Attribute("TextTrimming"));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {Path.Combine(parts)}");
    }
}
