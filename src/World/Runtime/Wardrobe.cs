using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;

namespace UNNAMED.World.Runtime;

/// <summary>Change clothes only; null keeps the creator's default. Dye colours are changed only by ApplyClothDyeCommand.</summary>
public sealed record ChangeWardrobeCommand(EntityId Actor, string? Outfit, string? Footwear, bool OverGear) : GameCommand(Actor);
public enum ClothRegion { Shirt, Trousers }
public sealed record ApplyClothDyeCommand(EntityId Actor, EntityId Dye, ClothRegion Region) : GameCommand(Actor);
public sealed record WardrobeChanged(long Tick);

internal sealed class WardrobeSystem(SystemContext context, SliceOwner owner, EntityId player)
{
    private RuntimeState State => context.State;
    private WardrobeSetup Setup => context.Setup.Character.Wardrobe;
    private CharacterAppearance Look => State.Identity.Appearance ?? new CharacterAppearance(
        Setup.DefaultStatures.GetValueOrDefault(State.Identity.PeopleId, 1750), null, null, null, null, null);
    private string? Ready(EntityId actor) => actor != player ? $"unknown actor {actor}"
        : State.PlayerCombat.Defeated ? "cannot change clothes now"
        : WardrobeLook.BodyType(context.Setup.Character, State.Identity) is null ? "this body has no wardrobe" : null;

    public string? Handle(ChangeWardrobeCommand command, long tick)
    {
        if (Ready(command.Actor) is { } why) return why;
        if (WardrobeLook.BodyType(context.Setup.Character, State.Identity) is not { } bodyType) return "this body has no wardrobe";
        bool Offered(string? id, ImmutableArray<WardrobeChoice> choices) => id is null || choices.Any(c => c.Id == id && c.For(bodyType));
        if (!Offered(command.Outfit, Setup.Outfits) || !Offered(command.Footwear, Setup.Footwear)) return "these clothes do not fit this body";
        var look = Look;
        Save(look with { Outfit = command.Outfit, Footwear = command.Footwear,
            Sliders = look.Sliders.SetItem(WardrobeLook.OverGear, command.OverGear ? 1 : 0) }, tick);
        return null;
    }

    public string? Handle(ApplyClothDyeCommand command, long tick)
    {
        if (Ready(command.Actor) is { } why) return why;
        if (!Enum.IsDefined(command.Region)) return "choose shirt or trousers";
        var stack = State.Inventory.FirstOrDefault(i => i.ItemId == command.Dye);
        var dye = Setup.Dyes.FirstOrDefault(d => d.ItemId == stack?.DefId);
        if (stack is null || dye is null) return "carry a cloth dye first";
        var look = Look;
        var garment = Setup.Outfits.FirstOrDefault(o => o.Id == (look.Outfit ?? "outfit.office.new_intern"));
        int regionMask = command.Region == ClothRegion.Shirt ? 1 : 2;
        if (garment is null || (garment.DyeRegions & regionMask) == 0) return "these clothes have no dyeable cloth in that region";
        string key = command.Region == ClothRegion.Shirt ? WardrobeLook.Shirt : WardrobeLook.Trousers;
        int value = command.Region == ClothRegion.Shirt ? dye.Shirt : dye.Trousers;
        if (look.Sliders.GetValueOrDefault(key) == value) return "the cloth is already this colour";
        if (context.Dispatch(new ExchangeItems([new StackTake(stack.ItemId, 1)], null, 0, 0)) is { } refused) return refused;
        Save(look with { Sliders = look.Sliders.SetItem(key, value) }, tick);
        return null;
    }

    private void Save(CharacterAppearance look, long tick)
    {
        State.SetAppearance(owner, look);
        context.Events.Publish(new WardrobeChanged(tick));
    }
}
