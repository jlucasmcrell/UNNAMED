using UNNAMED.Presentation.Ui.Models;
namespace UNNAMED.Presentation.Tests;
public class NamedCharacterCastTests
{
    [Fact]
    public void EveryNamedNpcHasADistinctIdentityInBothCasts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "Presentation", "Art", "named_character_cast.json"))) root = root.Parent;
        var rows = NamedCharacterCast.Read(File.ReadAllText(Path.Combine(root!.FullName,"src","Presentation","Art","named_character_cast.json")));
        Assert.Equal(8, rows.Count);
        Assert.Equal(8, rows.Values.Select(v => v.Native).Distinct().Count());
        Assert.Equal(8, rows.Values.Select(v => v.Mpfb).Distinct().Count());
        Assert.Equal("05_Etta", rows["npc.cairnwold.tam_crowden"].Native);
    }
    [Fact]
    public void DuplicateNamedIdentitiesAreRefused() => Assert.Throws<FormatException>(() =>
        NamedCharacterCast.Read("{\"a\":{\"native\":\"Grace\",\"mpfb\":\"a\"},\"b\":{\"native\":\"Grace\",\"mpfb\":\"b\"}}"));
}
