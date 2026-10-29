namespace UNNAMED.Presentation.Ui.Models;

/// <summary>Schedule activities choose authored motion; unavailable motion stays explicit in the asset coverage.</summary>
public static class CharacterActivity
{
    public static string? Clip(string? activity, string? role) => activity switch
    {
        "sleep" => null, // FurnitureAt owns support height and the lying pose.
        "eat" or "eating" => "eat",
        "drink" or "drinking" => "drink",
        "work" => role switch
        {
            "smith" or "blacksmith" => "work_smith",
            "cook" or "innkeeper" => "work_cook",
            "woodcutter" => "work_chop",
            "porter" or "carrier" => "work_carry",
            "farmer" or "herbalist" => "work_harvest",
            "cleaner" => "work_sweep",
            "miner" => "work_mine",
            "scholar" or "scribe" => "work_read",
            "fletcher" => "work_craft",
            _ => null,
        },
        _ => null,
    };
}
