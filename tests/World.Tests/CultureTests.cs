using System.Globalization;
using UNNAMED.Domain;

namespace UNNAMED.World.Tests;

/// <summary>
/// Generation output and identity text must not depend on the machine's culture (PERSISTENCE.md
/// T-02: "under a pinned numeric and comparison policy"). Each culture here breaks a different
/// culture-sensitive shortcut.
/// </summary>
public class CultureTests
{
    [Theory]
    [InlineData("tr-TR")]   // dotted and dotless i: culture-sensitive upper/lower-casing changes letters
    [InlineData("de-DE")]   // decimal comma
    [InlineData("ar-SA")]   // right-to-left, non-Gregorian calendar
    [InlineData("th-TH")]   // Buddhist calendar
    public void GenerationAndIdentity_AreTheSameUnderEveryCulture(string culture)
    {
        string invariant = InCulture(CultureInfo.InvariantCulture, Fingerprint);
        string local = InCulture(CultureInfo.GetCultureInfo(culture), Fingerprint);

        Assert.Equal(invariant, local);
    }

    private static string Fingerprint() => string.Join("|",
        RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Seed, new RegionKey(-1, 2)),
        TestWorlds.Generator().Fingerprint,
        EntityId.Parse("itm_01arz3ndektsv4rrffq69g5fav").Value,
        EntityId.Create(EntityKind.Item, 1_469_922_850_259, new byte[10]).Value,
        DefinitionId.IsValid("item.weapon.iron_sword"),
        DefinitionId.IsValid("Item.Weapon.Iron_Sword"),
        CellKey.Parse("r_neg1_2:c_07_11").ToString(),
        WorldSeed.Format(0x5C1A9E7B4D2F0083));

    private static T InCulture<T>(CultureInfo culture, Func<T> action)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
