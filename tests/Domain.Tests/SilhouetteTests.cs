using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S1's silhouette (STEALTH.md section 2.4): a body in the dark with light behind it is a shape a watch sees, and light behind is the rule the gem
/// cannot show on its own.
/// </summary>
public class SilhouetteTests
{
    [Fact]
    public void ABodyInTheDark_WithALitBackdrop_IsSilhouetted()
    {
        Assert.True(Visibility.Silhouetted(LightBand.Dark, litBeyondPermille: 900));
        Assert.True(Visibility.Silhouetted(LightBand.Dim, litBeyondPermille: 600));
    }

    [Fact]
    public void ABodyAlreadyInTheLight_IsNotASilhouette_ItIsSimplyLit()
    {
        // The band's own factor already carries a lit body; calling it a silhouette as well would double-count.
        Assert.False(Visibility.Silhouetted(LightBand.Lit, litBeyondPermille: 900));
        Assert.False(Visibility.Silhouetted(LightBand.Bright, litBeyondPermille: 900));
    }

    [Fact]
    public void ADarkBackdrop_LeavesTheBodyInTheDark()
    {
        Assert.False(Visibility.Silhouetted(LightBand.Dark, litBeyondPermille: 90));
        Assert.False(Visibility.Silhouetted(LightBand.Dark, litBeyondPermille: 449));
    }
}