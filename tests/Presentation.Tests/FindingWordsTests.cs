using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>Findings in words (UI progression F): found names the recipe, a hint says there is more without saying what, nothing says so.</summary>
public class FindingWordsTests
{
    private static string Name(string id) => id switch
    {
        "workbench" => "Workbench",
        "recipe.leatherwork.cloak" => "Cloak",
        "item.material.wolf_hide" => "Wolf hide",
        "item.material.bog_cotton" => "Bog cotton",
        "item.material.timber" => "Timber",
        _ => Names.Words(id),
    };

    private static Finding Of(FindingKind kind, string? recipe, params string[] materials) =>
        new(kind, "station.hollow.workbench", "workbench", recipe, materials.ToImmutableArray(), 100);

    [Fact]
    public void AFoundRecipe_IsNamed_WithWhatWasWorked()
    {
        var line = FindingWords.Line(Of(FindingKind.Found, "recipe.leatherwork.cloak", "item.material.bog_cotton", "item.material.wolf_hide"), Name);
        Assert.Equal("Cloak worked out", line.Says);
        Assert.Equal("At the workbench, working bog cotton and wolf hide.", line.Where);
    }

    [Fact]
    public void AHint_NamesWhatWasCarried_ButNeverTheRecipeItHintsAt()
    {
        var finding = Of(FindingKind.Hint, "recipe.leatherwork.cloak", "item.material.wolf_hide");
        var line = FindingWords.Line(finding, Name);
        Assert.Equal("There is more to wolf hide at the workbench", line.Says);
        Assert.Equal("It hints at something, but it wants more than you carried.", line.Where);
        Assert.DoesNotContain("loak", line.Says + line.Where);
    }

    [Fact]
    public void NothingCameOfIt_SaysSo()
    {
        var line = FindingWords.Line(Of(FindingKind.Nothing, null), Name);
        Assert.Equal("Nothing came of it at the workbench", line.Says);
    }

    [Fact]
    public void ManyMaterials_ReadAsAList()
    {
        var line = FindingWords.Line(Of(FindingKind.Hint, "recipe.leatherwork.cloak", "item.material.bog_cotton", "item.material.timber", "item.material.wolf_hide"), Name);
        Assert.Equal("There is more to bog cotton, timber and wolf hide at the workbench", line.Says);
    }

    [Fact]
    public void OnlyAHintAndNothing_AreToasted_AFoundRecipeIsToastedAsLearned()
    {
        Assert.Null(FindingWords.Toast(Of(FindingKind.Found, "recipe.leatherwork.cloak"), Name));
        Assert.Equal("There is more to this at the workbench: noted under Recipes", FindingWords.Toast(Of(FindingKind.Hint, "recipe.leatherwork.cloak", "item.material.wolf_hide"), Name));
        Assert.Equal("Nothing came of it at the workbench: noted under Recipes", FindingWords.Toast(Of(FindingKind.Nothing, null), Name));
    }
}
