// UNNAMED Presentation - the words on a loot row

namespace UNNAMED.Presentation.Ui;

/// <summary>What a loot window row says besides the item's name: its count and the weight of the whole stack.</summary>
public static class LootModel
{
    public static string Count(int count) => count > 1 ? $"x{count}" : "";

    public static string Weight(long weightGrams, int count) => $"{weightGrams * count / 1000.0:0.0#} kg";
}
