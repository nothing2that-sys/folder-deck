using FolderDeck.App.Interop;

namespace FolderDeck.App.Tests;









public sealed class ShellMenuPolicyTests
{


    [Theory]
    [InlineData("delete")]
    public void RemovesDeleteOnly(string verb)
    {
        Assert.True(ShellMenuPolicy.IsRemovedVerb(verb));
    }





    [Theory]
    [InlineData("cut")]
    [InlineData("link")]
    [InlineData("open")]
    [InlineData("copy")]
    [InlineData("copyaspath")]
    [InlineData("properties")]
    [InlineData("paste")]
    public void KeepsEverythingElse(string verb)
    {
        Assert.False(ShellMenuPolicy.IsRemovedVerb(verb));
    }


    [Fact]
    public void KeepsItemWithoutVerb()
    {
        Assert.False(ShellMenuPolicy.IsRemovedVerb(null));
        Assert.False(ShellMenuPolicy.IsRemovedVerb(string.Empty));
    }




    [Theory]
    [InlineData("Delete")]
    [InlineData("DELETE")]
    public void VerbMatchIsCaseInsensitive(string verb)
    {
        Assert.True(ShellMenuPolicy.IsRemovedVerb(verb));
    }




    [Theory]
    [InlineData("Cut")]
    [InlineData("CUT")]
    public void KeptCutMatchIsCaseInsensitive(string verb)
    {
        Assert.False(ShellMenuPolicy.IsRemovedVerb(verb));
    }




    [Theory]
    [InlineData(0x11u, ShellMenuAppCommand.RevealInExplorer)]
    [InlineData(0x12u, ShellMenuAppCommand.Rename)]
    [InlineData(0x13u, ShellMenuAppCommand.SetAnchor)]
    [InlineData(0x14u, ShellMenuAppCommand.RegisterFolder)]
    [InlineData(0x15u, ShellMenuAppCommand.Trash)]
    public void AppIdsMapToTheirOwnCommand(uint id, ShellMenuAppCommand expected)
    {
        Assert.Equal(expected, ShellMenuPolicy.ToAppCommand(id));
    }




    [Theory]
    [InlineData(0x1000u)]
    [InlineData(0x1001u)]
    [InlineData(0x7FFFu)]
    public void ShellIdsAreNotAppCommands(uint id)
    {
        Assert.Equal(ShellMenuAppCommand.None, ShellMenuPolicy.ToAppCommand(id));
    }


    [Fact]
    public void ZeroIsNothingChosen()
    {
        Assert.Equal(ShellMenuAppCommand.None, ShellMenuPolicy.ToAppCommand(0));
    }






    [Theory]
    [InlineData(0x01u)]
    [InlineData(0x10u)]
    [InlineData(0x17u)]
    [InlineData(0x0FFFu)]
    public void UnknownAppBandIdRunsNothing(uint id)
    {
        Assert.Equal(ShellMenuAppCommand.None, ShellMenuPolicy.ToAppCommand(id));
    }




    [Fact]
    public void FolderGetsAllFiveInOrder()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: true);

        Assert.Equal(
            [
                ShellMenuAppCommand.RevealInExplorer,
                ShellMenuAppCommand.Rename,
                ShellMenuAppCommand.SetAnchor,
                ShellMenuAppCommand.RegisterFolder,
                ShellMenuAppCommand.Trash,
            ],
            items.Select(i => i.Command));
    }





    [Fact]
    public void FileSkipsTheFolderOnlyTwo()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: false);

        Assert.Equal(
            [
                ShellMenuAppCommand.RevealInExplorer,
                ShellMenuAppCommand.Rename,
                ShellMenuAppCommand.Trash,
            ],
            items.Select(i => i.Command));
    }







    [Fact]
    public void FileGetsCompareContentItemWhenRequested()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: false, canCompareContent: true);

        Assert.Equal(
            [
                ShellMenuAppCommand.RevealInExplorer,
                ShellMenuAppCommand.Rename,
                ShellMenuAppCommand.CompareContent,
                ShellMenuAppCommand.Trash,
            ],
            items.Select(i => i.Command));
    }


    [Fact]
    public void CompareContentDefaultsToOff()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: false);

        Assert.DoesNotContain(items, i => i.Command == ShellMenuAppCommand.CompareContent);
    }


    [Fact]
    public void DirectoryNeverGetsCompareContentEvenWhenRequested()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: true, canCompareContent: true);

        Assert.DoesNotContain(items, i => i.Command == ShellMenuAppCommand.CompareContent);
    }


    [Fact]
    public void CompareContentIdIsAlsoBelowTheShellBand()
    {
        var item = ShellMenuPolicy.AppItems(isDirectory: false, canCompareContent: true)
            .Single(i => i.Command == ShellMenuAppCommand.CompareContent);

        Assert.Equal(item.Command, ShellMenuPolicy.ToAppCommand((uint)item.Command));
    }




    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverlappingFourAreNotInserted(bool isDirectory)
    {
        var texts = ShellMenuPolicy.AppItems(isDirectory).Select(i => i.Text).ToList();

        Assert.DoesNotContain(texts, t => t.StartsWith("열기", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("복사", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("경로 복사", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("속성", StringComparison.Ordinal));
    }





    [Fact]
    public void KeyLabelsRideOnTheItemText()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: true);

        Assert.Equal("이름 바꾸기\tF2", items.Single(i => i.Command == ShellMenuAppCommand.Rename).Text);
        Assert.Equal("휴지통으로 삭제\tDel", items.Single(i => i.Command == ShellMenuAppCommand.Trash).Text);
    }




    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryAppItemIdIsBelowTheShellBand(bool isDirectory)
    {
        foreach (var item in ShellMenuPolicy.AppItems(isDirectory))
        {
            Assert.Equal(item.Command, ShellMenuPolicy.ToAppCommand((uint)item.Command));
        }
    }







    [Fact]
    public void TwoInTheSameFolderShareOneParent()
    {
        Assert.True(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt", @"C:\a\b\two.txt"]));
        Assert.True(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt", @"C:\A\B\two.txt"]));
    }




    [Fact]
    public void TwoInDifferentFoldersDoNot()
    {
        Assert.False(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt", @"C:\a\c\two.txt"]));
    }


    [Fact]
    public void OneAlwaysSharesOneParent()
    {
        Assert.True(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt"]));
    }


    [Fact]
    public void EmptyListSharesNothing()
    {
        Assert.False(ShellMenuPolicy.SharesOneParent([]));
    }
}
