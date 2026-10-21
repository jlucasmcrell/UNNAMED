using UNNAMED.Presentation.Ui.Models;
namespace UNNAMED.Presentation.Tests;
public class CharacterSexTests
{
    [Theory]
    [InlineData("f", "Beard_Full", false)]
    [InlineData("feminine", "Trey_Moustache", false)]
    [InlineData("f", "female_eyebrows", true)]
    [InlineData("f", "Hair_Bob", true)]
    [InlineData("m", "Beard_Full", true)]
    public void FemaleNamedCharactersNeverShowBeardGrooms(string sex, string mesh, bool allowed) => Assert.Equal(allowed, CharacterSex.AllowsGroom(sex, mesh));
    [Theory]
    [InlineData("f", "feminine", true)]
    [InlineData("f", "masculine", false)]
    [InlineData("m", "feminine", false)]
    [InlineData("m", "masculine", true)]
    public void NativeBodySelectionHonoursAuthoredSex(string sex, string body, bool match) => Assert.Equal(match, CharacterSex.Matches(sex, body));
}
