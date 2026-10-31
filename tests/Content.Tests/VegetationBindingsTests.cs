using System.Text.Json;

namespace UNNAMED.Content.Tests;

public class VegetationBindingsTests
{
    [Fact]
    public void LivingTreeWindPool_ContainsNoRigidDeadAsh()
    {
        using var bindings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.Root(), "content", "_art_bindings.json")));
        var prefixes = bindings.RootElement.GetProperty("structure_prefixes");
        var trees = prefixes.GetProperty("tree_");
        Assert.DoesNotContain("flora_cos_dead_ash_tree",
            trees.GetProperty("models").EnumerateArray().Select(model => model.GetString()));
        Assert.Equal(1, trees.GetProperty("wind").GetSingle());
        Assert.Equal(0, prefixes.GetProperty("cw_dead_").GetProperty("wind").GetSingle());
    }
}
