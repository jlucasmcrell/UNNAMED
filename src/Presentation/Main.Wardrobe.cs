using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    private WardrobePanel _wardrobe = null!;
    private CharacterAppearance? _drawnWardrobe;
    private WornOutfit? _drawnClothes;

    private void DressPlayer(WornOutfit equipment)
    {
        var identity = _session.Simulation!.Player.Identity;
        var shown = WardrobeLook.CoversGear(identity.Appearance) ? new WornOutfit(ClothesOnly: true) : equipment;
        _avatar.WearEquipment(shown);
        // Armour changes clear body masks: apply the creator's stencil after dressing, once per change.
        if (_drawnClothes != shown || !Equals(_drawnWardrobe, identity.Appearance))
        {
            PeopleAppearance.ApplyLook(_avatar, identity.PeopleId, identity.Appearance, _art, _avatarModel);
            _drawnClothes = shown;
            _drawnWardrobe = identity.Appearance;
        }
    }
}
