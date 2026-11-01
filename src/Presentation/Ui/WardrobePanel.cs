using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>Owned native garments, separate from the equipment. Only commands change the saved look or spend a dye.</summary>
public partial class WardrobePanel : CanvasLayer
{
    private GameSession _session = null!;
    private ArtLibrary _art = null!;
    private ArtBindings _bindings = null!;
    private Func<string?> _model = null!;
    private Sheet _sheet = null!;
    private CreatorPreview _preview = null!;
    private VBoxContainer _choices = null!;
    private string? _signature;

    public void Bind(GameSession session, ArtLibrary art, ArtBindings bindings, Func<string?> model)
        => (_session, _art, _bindings, _model) = (session, art, bindings, model);

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Wardrobe and dyes", null, 1100);
        _sheet.SetHint("Your clothes and colours. Armour, weapons and their protection stay equipped.");
        var row = new HBoxContainer();
        _sheet.Body.AddChild(row);
        _preview = new CreatorPreview { Centered = true, CustomMinimumSize = new Vector2(390, 540), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _preview.Bind(_art, _bindings);
        row.AddChild(_preview);
        _choices = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(_choices);
        _sheet.OnShown(() => _signature = null);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session.Simulation is not { } world) return;
        var look = world.Player.Identity.Appearance;
        string signature = System.Text.Json.JsonSerializer.Serialize(look) + "|" + _model() + "|" +
            string.Join(',', world.Player.Inventory.Where(i => _session.Characters.Wardrobe.Dyes.Any(d => d.ItemId == i.DefId)).Select(i => $"{i.ItemId}:{i.Count}"));
        if (signature == _signature) return;
        _signature = signature;
        Rebuild(world);
    }

    private void Rebuild(Simulation world)
    {
        UiKit.Clear(_choices);
        var identity = world.Player.Identity;
        var look = identity.Appearance;
        if (_model()?.StartsWith("humgen_creator_", StringComparison.Ordinal) != true ||
            WardrobeLook.BodyType(_session.Characters, identity) is not { } bodyType)
        {
            _choices.AddChild(UiKit.Quiet("This body's wardrobe is not available in this build."));
            return;
        }
        string sex = bodyType.EndsWith("_f", StringComparison.Ordinal) ? "f" : "m";
        var setup = _session.Characters.Wardrobe;
        bool over = WardrobeLook.CoversGear(look);
        bool Delivered(CreatorChoice kind, string option)
        {
            string asset = HumGenCreatorData.Part(kind, sex, option);
            if (_art.RiggedFile(asset, "creator_part.json") is not { } partFile ||
                _art.RiggedFile(_model()!, "identity_weights.json") is not { } bodyFile) return false;
            using var part = System.Text.Json.JsonDocument.Parse(File.ReadAllText(partFile));
            using var target = System.Text.Json.JsonDocument.Parse(File.ReadAllText(bodyFile));
            return part.RootElement.GetProperty("target_base").GetString() == _model() &&
                part.RootElement.GetProperty("target_body_sha256").GetString() == target.RootElement.GetProperty("target_body_sha256").GetString();
        }
        void Change(string? outfit, string? feet, bool cover) => _session.Submit(new ChangeWardrobeCommand(world.PlayerId, outfit, feet, cover));
        void Options(string heading, CreatorChoice kind, IEnumerable<WardrobeChoice> all, string? selected, Action<string?> choose)
        {
            var section = UiKit.Section(heading);
            var menu = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var choices = all.Where(c => c.For(bodyType) && Delivered(kind, c.Id)).ToArray();
            menu.AddItem("Creator default");
            foreach (var choice in choices) menu.AddItem(LookSwatches.ShownNames.GetValueOrDefault(choice.Id, choice.Name));
            menu.Select(Array.FindIndex(choices, c => c.Id == selected) + 1);
            menu.ItemSelected += index => choose(index == 0 ? null : choices[index - 1].Id);
            section.AddChild(menu);
            _choices.AddChild(section);
        }
        Options("Clothes", CreatorChoice.Outfit, setup.Outfits, look?.Outfit, id => Change(id, look?.Footwear, over));
        Options("Footwear", CreatorChoice.Footwear, setup.Footwear, look?.Footwear, id => Change(look?.Outfit, id, over));
        var cover = new CheckButton { Text = "Keep these clothes visible when armour is worn", ButtonPressed = over };
        cover.Toggled += value => Change(look?.Outfit, look?.Footwear, value);
        _choices.AddChild(cover);
        _choices.AddChild(UiKit.Quiet("Drag the preview to turn. Scroll to look closer.\nThe preview shows your clothes. Armour protection and heritage rules stay the same."));
        _preview.Show(_model(), identity.PeopleId, 0, new WornOutfit(ClothesOnly: true));
        _preview.Look(identity.PeopleId, look);
        var dyes = UiKit.Section("Cloth dyes", "One pot colours one region. Learn the recipes from Herbal Remedies and steep them at a cookfire.");
        bool found = false;
        foreach (var stack in world.Player.Inventory)
        {
            if (setup.Dyes.FirstOrDefault(d => d.ItemId == stack.DefId) is not { } dye) continue;
            found = true;
            var row = new HBoxContainer();
            row.AddChild(UiKit.Words($"{Names.Of(_session, stack.DefId)} ×{stack.Count}"));
            foreach (var region in Enum.GetValues<ClothRegion>())
            {
                var button = UiKit.Action(region.ToString(), () => _session.Submit(new ApplyClothDyeCommand(world.PlayerId, stack.ItemId, region)), compact: true);
                int value = region == ClothRegion.Shirt ? dye.Shirt : dye.Trousers;
                string key = region == ClothRegion.Shirt ? WardrobeLook.Shirt : WardrobeLook.Trousers;
                var garment = setup.Outfits.FirstOrDefault(o => o.Id == (look?.Outfit ?? "outfit.office.new_intern"));
                int regionMask = region == ClothRegion.Shirt ? 1 : 2;
                button.Disabled = garment is null || (garment.DyeRegions & regionMask) == 0 || look?.Sliders.GetValueOrDefault(key) == value;
                row.AddChild(button);
            }
            dyes.AddChild(row);
        }
        if (!found) dyes.AddChild(UiKit.Quiet("No cloth dyes carried."));
        dyes.AddChild(UiKit.Quiet("Earth dyes colour shirts and trousers. Leather, metal and the frontier outfit keep their own finish."));
        _choices.AddChild(dyes);
    }
}
