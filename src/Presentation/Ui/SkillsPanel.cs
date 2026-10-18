// UNNAMED Presentation - the Skills screen: every discipline, its band and progress, what it opens and when; and what is known (WP9)
// Godot presentation only: it reads views and content; nothing here holds game state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Progression;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Skills (P), a DENSE list + detail sheet ported from ASTRAL's Field Instrument SkillsPanel (05_SKILLS_WIREFRAME). The list is the
/// whole roster by family - practised rows bright with a level and a brass progress line, the rest dim with a dash - then what is known
/// (formulas, recipes, techniques) and the discipline map. A discipline's detail gives its band, the XP to the next level, the threshold
/// ruler of what it opens and when, and the next opening. Skills grow only by challenged use: nothing here buys, spends or teaches.
/// </summary>
public partial class SkillsPanel : CanvasLayer
{
    public const string Formulas = "known/formulas", Recipes = "known/recipes", Techniques = "known/techniques", Map = "known/map";

    private const string HowTheyRise = "Skills grow only by use against a real challenge: a harder attempt teaches more, and a failure still teaches.";
    private const string NothingBuys = "No teacher, book or coin raises a skill, and a level grants attribute points, never skill points.";

    private GameSession? _session;
    private HudIcons _icons = HudIcons.None;
    private Sheet _sheet = null!;
    private ListDetail _panes = null!;
    private readonly Dictionary<string, Button> _rows = new(StringComparer.Ordinal);
    private string _listDrawn = string.Empty;
    private string _detailDrawn = string.Empty;
    private string? _selected;
    private bool _showAll;

    /// <summary>Put an item, formula or technique on a quick slot (0-7), or clear one (null); Main submits the command.</summary>
    public Action<int, string?>? Assign { get; set; }

    /// <summary>The kind of station the character is working at, if one is open: recipes are Ready only where their kind stands.</summary>
    public Func<string?>? NearStation { get; set; }

    public void Bind(GameSession session) => _session = session;

    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        _sheet.SetIcon(icons.Binds("panel.skills") ? icons.For("panel.skills") : null);
        _listDrawn = string.Empty;
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Skills", "skills", UiKit.Wide, scrolls: false);
        _sheet.Shape(UiKit.Wide, dense: true);
        _sheet.SetHint(HowTheyRise);
        _panes = UiKit.ListDetail();
        _sheet.Body.AddChild(_panes.Root);
        _sheet.OnShown(() =>
        {
            _listDrawn = string.Empty;
            Refresh();
            if (_selected is not null && _rows.TryGetValue(_selected, out var row))
                row.GrabFocus();
        });
    }

    /// <summary>Open on a row: a skill ID or one of the known views; null picks as the wireframe does (the highest practised skill).</summary>
    public void Open(string? select = null)
    {
        _selected = select;
        Visible = true;
    }

    /// <summary>Redraw what changed (asked every frame: the world runs while the screen is open, and the selection is kept).</summary>
    public void Refresh()
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var view = simulation.Player;
        var progression = view.Progression;
        string state = string.Join(",", progression.Skills.Select(s => $"{s.Key}:{s.Value.Level}:{s.Value.ProgressXp}")) + "|" +
                       string.Join(",", progression.Known.Keys) + "|" + string.Join(",", QuickBar(simulation)) + $"|{_showAll}";
        if (state != _listDrawn)
        {
            _listDrawn = state;
            _detailDrawn = string.Empty;
            string? focused = UiKit.FocusedId(_sheet.Root);
            DrawList(progression);
            UiKit.Refocus(_sheet.Root, focused);
        }
        var roster = _session.Setup.Progression.Roster;
        if (_selected is null || !_rows.ContainsKey(_selected))
            _selected = progression.Skills.Where(s => s.Value.Level > 0 && roster.Any(r => r.Id == s.Key))
                .OrderByDescending(s => s.Value.Level).ThenBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key).FirstOrDefault()
                ?? roster.FirstOrDefault()?.Id ?? Formulas;
        // The pools and the carried counts change the known views' "not now" lines and counts.
        string detail = $"{state}|{simulation.Findings.Length}|{_selected}|{progression.Pools}|{view.Stats.StrainTolerance}|" +
                        string.Join(",", view.Inventory.Select(e => $"{e.DefId}{e.Count}")) + "|" +
                        string.Join(",", view.Equipment.Select(kv => $"{kv.Key}:{kv.Value}")) + $"|{NearStation?.Invoke()}|" +
                        string.Join(",", progression.Known.Keys.Where(IsTechnique).Select(simulation.TechniqueCooldown).Select(t => (t + 19) / 20));
        if (detail == _detailDrawn)
            return;
        _detailDrawn = detail;
        foreach (var (id, row) in _rows)
            UiKit.Select(row, id == _selected);
        // The chosen row in view, though the list scrolls: a known view opened by name sits below the roster.
        if (_rows.TryGetValue(_selected, out var chosen))
            Callable.From(() => { if (IsInstanceValid(chosen) && _panes.ListScroll.IsAncestorOf(chosen)) _panes.ListScroll.EnsureControlVisible(chosen); }).CallDeferred();
        UiKit.Clear(_panes.Detail);
        _panes.DetailScroll.ScrollVertical = 0;
        switch (_selected)
        {
            case Formulas:
                DrawFormulas(simulation);
                break;
            case Recipes:
                DrawRecipes(simulation);
                break;
            case Techniques:
                DrawTechniques(simulation);
                break;
            case Map:
                DrawMap(progression);
                break;
            default:
                DrawSkill(progression, _selected);
                break;
        }
    }

    private void Choose(string id)
    {
        if (_selected == id)
            return;
        _selected = id;
        Refresh();
    }

    private static string?[] QuickBar(Simulation simulation) => simulation.QuickBar.ToArray();

    // ---- the list -------------------------------------------------------------------------------------------------------------

    /// <summary>The roster by family, practised bright with a level and a brass line, the rest dim with a dash; then what is known.</summary>
    private void DrawList(CharacterProgression progression)
    {
        var rules = _session!.Setup.Progression;
        UiKit.Clear(_panes.List);
        _rows.Clear();
        var listed = Shown(progression);
        foreach (var family in listed.GroupBy(s => s.Family))
        {
            var section = UiKit.Section(family.Key);
            foreach (var skill in family)
            {
                var state = progression.Skills.GetValueOrDefault(skill.Id);
                bool practised = state is { } s && (s.Level > 0 || s.ProgressXp > 0);
                int level = state?.Level ?? 0;
                var row = UiKit.ListRow(skill.Id, Names.Of(_session, skill.Id), practised ? rules.BandOf(level).Name : "not yet practised", practised ? HudStyle.Bone : HudStyle.Ash,
                    height: 44, left: UiKit.Icon(_icons, skill.Id, 24));
                if (practised)
                {
                    var line = UiKit.Gauge(HudStyle.Brass, 0, 3, quarters: false);
                    line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    bool ceiling = level >= rules.Skills.CommonCeiling;
                    line.Set(ceiling ? 1 : state!.ProgressXp, ceiling ? 1 : rules.Skills.ToNext(level));
                    row.Middle.AddChild(line);
                }
                var shown = HudStyle.Text(HudStyle.Strong, 20, practised ? HudStyle.Bone : HudStyle.Muted);
                shown.Text = practised ? $"{level}" : "—";
                shown.CustomMinimumSize = new Vector2(32, 0);
                shown.HorizontalAlignment = HorizontalAlignment.Right;
                shown.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.Right.AddChild(shown);
                Wire(row.Button, skill.Id);
                section.AddChild(row.Button);
            }
            _panes.List.AddChild(section);
        }
        int hidden = DisciplineRoster.Hidden(rules.Roster, listed);
        if (hidden > 0 || _showAll)
        {
            var toggle = UiKit.Action(DisciplineRoster.Toggle(_showAll, hidden), () =>
            {
                _showAll = !_showAll;
                Callable.From(Refresh).CallDeferred();
            }, compact: true, id: "skills/show_all");
            _panes.List.AddChild(toggle);
        }

        var known = UiKit.Section("Known");
        var magic = _session.Setup.Magic.Formulas;
        var recipes = _session.Setup.Crafting.Recipes;
        foreach (var (id, words, count) in new[]
                 {
                     (Formulas, "Formulas", progression.Known.Keys.Count(magic.ContainsKey)),
                     (Recipes, "Recipes", progression.Known.Keys.Count(recipes.ContainsKey)),
                     (Techniques, "Techniques", progression.Known.Keys.Count(IsTechnique)),
                 })
        {
            var row = UiKit.ListRow(id, words, count == 0 ? "none yet" : $"{count} known", height: 44);
            Wire(row.Button, id);
            known.AddChild(row.Button);
        }
        known.AddChild(HudStyle.Rule(8));
        var map = UiKit.ListRow(Map, "Discipline map", "what every discipline opens", height: 44);
        Wire(map.Button, Map);
        known.AddChild(map.Button);
        _panes.List.AddChild(known);
    }

    /// <summary>The disciplines the list shows now: the practised and the linked to something known, or every one.</summary>
    private IReadOnlyList<SkillEntry> Shown(CharacterProgression progression)
    {
        var session = _session!;
        var linked = progression.Known.Keys.Select(id =>
                session.Setup.Magic.Formulas.TryGetValue(id, out var f) ? f.DomainSkillId
                : session.Setup.Crafting.Recipes.TryGetValue(id, out var r) ? r.SkillId
                : session.Setup.Techniques.TryGetValue(id, out var t) ? t.SkillId : null)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        return DisciplineRoster.Shown(session.Setup.Progression.Roster,
            id => progression.Skills.GetValueOrDefault(id) is { } s && (s.Level > 0 || s.ProgressXp > 0), linked, _showAll, _selected);
    }

    private void Wire(Button row, string id)
    {
        _rows[id] = row;
        row.Pressed += () => Choose(id);
        row.FocusEntered += () => Callable.From(() => Choose(id)).CallDeferred();
    }

    /// <summary>A technique (C5): a known definition of the <c>ability</c> kind. Formulas and recipes are listed on their own.</summary>
    private static bool IsTechnique(string id) => id.StartsWith("ability.", StringComparison.Ordinal);

    // ---- a discipline ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A discipline: its name, family and level; its band and the XP to the next level; the threshold ruler; the next opening and every
    /// opening it holds; the techniques of it that are known; and how skills rise.
    /// </summary>
    private void DrawSkill(CharacterProgression progression, string skillId)
    {
        var session = _session!;
        var rules = session.Setup.Progression;
        var entry = rules.Roster.First(r => r.Id == skillId);
        var state = progression.Skills.GetValueOrDefault(skillId);
        int level = state?.Level ?? 0;
        bool practised = state is { } s && (s.Level > 0 || s.ProgressXp > 0);
        int ceiling = rules.Skills.CommonCeiling;

        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        var name = HudStyle.Text(HudStyle.Caps, 30, HudStyle.Bone, caps: true);
        name.Text = Names.Of(session, skillId);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(name);
        top.AddChild(UiKit.Caption(entry.Family));
        top.AddChild(UiKit.Chip(rules.BandOf(level).Name, practised ? HudStyle.Brass : HudStyle.Iron, practised ? HudStyle.Brass : HudStyle.Ash));
        top.AddChild(UiKit.Caption("Level"));
        var shown = HudStyle.Text(HudStyle.Strong, 30, practised ? HudStyle.Brass : HudStyle.Muted);
        shown.Text = practised ? $"{level}" : "—";
        top.AddChild(shown);
        head.AddChild(top);
        if (level >= ceiling)
        {
            head.AddChild(UiKit.Words($"At the common ceiling, {ceiling}: only a designated mastery goes further.", 17, HudStyle.Ash));
        }
        else if (practised)
        {
            long toNext = rules.Skills.ToNext(level);
            var gauge = UiKit.Gauge(HudStyle.Brass, 0, 8, marks: 10);
            gauge.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            gauge.Set(state!.ProgressXp, toNext);
            head.AddChild(gauge);
            head.AddChild(UiKit.Words($"{state.ProgressXp} of {toNext} XP to {level + 1}", 17, HudStyle.Ash));
        }
        else
        {
            head.AddChild(UiKit.Words("Untrained - use it against a real challenge to begin.", 17, HudStyle.Ash));
        }
        if (entry.Teaches.Length > 0)
            head.AddChild(UiKit.Words($"What teaches it: {entry.Teaches}", 17, HudStyle.Bone));
        _panes.Detail.AddChild(head);

        var openings = Openings(session, skillId);
        var ruler = UiKit.Section("Thresholds", $"0 to the common ceiling, {ceiling}");
        ruler.AddChild(new ThresholdRuler(level, ceiling, rules.SkillBands, openings) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _panes.Detail.AddChild(ruler);

        var next = openings.FirstOrDefault(o => o.Level > level);
        var nextSection = UiKit.Section("Next opening");
        nextSection.AddChild(next is null
            ? UiKit.Quiet("Nothing more opens before the common ceiling.")
            : OpeningRow(next, level));
        _panes.Detail.AddChild(nextSection);

        var all = UiKit.Section("What it opens", "by level, from content");
        foreach (var opening in openings)
            all.AddChild(OpeningRow(opening, level));
        _panes.Detail.AddChild(all);

        var held = UiKit.Section("Techniques");
        var known = progression.Known.Keys.Where(IsTechnique).ToList();
        if (known.Count == 0)
            held.AddChild(UiKit.Quiet("This discipline holds no techniques you know yet."));
        foreach (string id in known)
            held.AddChild(UiKit.Row(UiKit.Chip("Known", HudStyle.Iron, HudStyle.Bone), Names.Of(session, id), Source(session, progression.Known[id])).Root);
        _panes.Detail.AddChild(held);

        var rise = UiKit.Section("How skills rise");
        rise.AddChild(UiKit.Words(HowTheyRise, 16, HudStyle.Muted));
        rise.AddChild(UiKit.Words(NothingBuys, 16, HudStyle.Muted));
        _panes.Detail.AddChild(rise);
    }

    /// <summary>One opening: its level as a chip (lichen once reached), and what it opens.</summary>
    private static Control OpeningRow(Opening opening, int level)
    {
        bool reached = level >= opening.Level;
        var chip = UiKit.Chip($"At {opening.Level}", reached ? HudStyle.Lichen : HudStyle.Iron, reached ? HudStyle.Lichen : HudStyle.Ash);
        chip.CustomMinimumSize = new Vector2(72, 0);
        var row = UiKit.Row(chip, opening.Words, reached ? "open" : $"{opening.Level - level} more {(opening.Level - level == 1 ? "level" : "levels")}",
            opening.Band ? HudStyle.Brass : HudStyle.Bone, height: 36);
        return row.Root;
    }

    /// <summary>A level at which a discipline opens something, as content defines it.</summary>
    public sealed record Opening(int Level, string Words, bool Band);

    /// <summary>
    /// What a discipline opens, by level, read off content: its passives, a harvest's bonus, each formula of its domain past fizzling,
    /// each recipe of it past crude work, and the bands. Nothing is invented: a discipline with none of these shows only its bands.
    /// </summary>
    internal static List<Opening> Openings(GameSession session, string skillId)
    {
        var rules = session.Setup.Progression;
        var list = new List<Opening>();
        foreach (var passive in session.Setup.Combat.Passives.Where(p => p.SkillId == skillId))
            list.Add(new(passive.FromLevel, $"{Names.Of(session, passive.Stat)} ×{passive.Multiplier:0.##}", false));
        foreach (var bonus in session.Setup.Crafting.YieldBonuses.Where(b => b.SkillId == skillId))
            list.Add(new(bonus.FromLevel, $"+{bonus.Amount} from every harvest", false));
        foreach (var formula in session.Setup.Magic.Formulas.Values.Where(f => f.DomainSkillId == skillId && f.Complexity > 0))
            list.Add(new(formula.Complexity, $"{Names.Of(session, formula.Id)} no longer fizzles", false));
        foreach (var recipe in session.Setup.Crafting.Recipes.Values.Where(r => r.SkillId == skillId && r.Complexity > 0))
            list.Add(new(recipe.Complexity, $"{Names.Of(session, recipe.Id)}: no crude work", false));
        foreach (var band in rules.SkillBands.Where(b => b.From > 0 && b.From <= 100))
            list.Add(new(band.From, band.From == rules.Skills.CommonCeiling ? $"{band.Name} - the common ceiling" : band.Name, true));
        return list.OrderBy(o => o.Level).ThenBy(o => o.Band).ThenBy(o => o.Words, StringComparer.Ordinal).ToList();
    }

    // ---- what is known --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The known formulas, the spellbook: each with its domain and complexity against the character's skill, its Focus and its Strain at
    /// that skill, the chance it fizzles, the backlash past tolerance, why it cannot be worked now, where it was learned, and its quick slot.
    /// </summary>
    private void DrawFormulas(Simulation simulation)
    {
        var session = _session!;
        var view = simulation.Player;
        var magic = session.Setup.Magic;
        Title("Formulas", "workings known, in the order they were learned");
        var list = UiKit.Section("Known formulas");
        list.AddThemeConstantOverride("separation", 18);
        var known = view.Progression.Known.Where(k => magic.Formulas.ContainsKey(k.Key)).OrderBy(k => k.Value.Tick).ThenBy(k => k.Key, StringComparer.Ordinal).ToList();
        if (known.Count == 0)
            list.AddChild(UiKit.Quiet("None yet - a formula is taught or read, never bought with points."));
        int tolerance = (int)view.Stats.StrainTolerance;
        var context = Context(simulation);
        foreach (var (id, learned) in known)
        {
            var formula = magic.Formulas[id];
            int skill = ProgressionEngine.SkillLevel(view.Progression, formula.DomainSkillId);
            int cost = MagicRules.StrainCost(formula, skill, magic.Constants);
            var card = Card(id, Source(session, learned));
            card.AddChild(UiKit.Words($"{Names.Of(session, formula.DomainSkillId)} · complexity {formula.Complexity} · your {Names.Of(session, formula.DomainSkillId)} {skill}", 16, HudStyle.Ash));
            // The read of the risk is words at this skill (canon 26 W8, 07 §5.7): the slip chance is a number only from 30, the Strain's severity from 45.
            var read = AvailabilityRules.Risk(formula, magic.Constants, context);
            var numbers = Numbers(("Focus", $"{formula.FocusCost}", HudStyle.Bone), ("Strain", $"{cost} of {tolerance}", HudStyle.Bone),
                ("Read", read.Word, read.Word == "steady" ? HudStyle.Lichen : read.Word == "uncertain" ? HudStyle.Ember : HudStyle.Wound));
            card.AddChild(numbers);
            card.AddChild(AvailabilityBlock(AvailabilityRules.Formula(formula, magic.Constants, context), checklist: false));
            foreach (string line in read.Lines)
                card.AddChild(UiKit.Words(line, 16, HudStyle.Ash));
            if (skill < formula.Complexity)
                card.AddChild(UiKit.Words($"It stops slipping at {Names.Of(session, formula.DomainSkillId)} {formula.Complexity}.", 16, HudStyle.Muted));
            card.AddChild(SlotPicker(simulation, id));
            list.AddChild(card);
        }
        _panes.Detail.AddChild(list);
    }

    /// <summary>The known recipes: station, skill against complexity and the quality it gives, the inputs, and where each was learned.</summary>
    private void DrawRecipes(Simulation simulation)
    {
        var session = _session!;
        var view = simulation.Player;
        var context = Context(simulation);
        var crafting = session.Setup.Crafting;
        Title("Recipes", "made at a station of their kind; the skill decides the quality");
        var list = UiKit.Section("Known recipes");
        list.AddThemeConstantOverride("separation", 18);
        var known = view.Progression.Known.Where(k => crafting.Recipes.ContainsKey(k.Key)).OrderBy(k => k.Value.Tick).ThenBy(k => k.Key, StringComparer.Ordinal).ToList();
        if (known.Count == 0)
            list.AddChild(UiKit.Quiet("None yet - a recipe is taught, read or worked out at the bench."));
        foreach (var (id, learned) in known)
        {
            var recipe = crafting.Recipes[id];
            int skill = ProgressionEngine.SkillLevel(view.Progression, recipe.SkillId);
            var card = Card(id, Source(session, learned), recipe.OutputItemId);
            card.AddChild(UiKit.Words($"{Names.Of(session, recipe.StationKind)} · {Names.Of(session, recipe.SkillId)} · complexity {recipe.Complexity} · your {Names.Of(session, recipe.SkillId)} {skill}", 16, HudStyle.Ash));
            int crude = CraftingRules.CrudePercent(skill, recipe.Complexity, crafting.Constants);
            int fine = recipe.QualityRoll ? CraftingRules.FinePercent(skill, recipe.Complexity, crafting.Constants) : 0;
            card.AddChild(Numbers(("Crude", crude == 0 ? "none" : $"{crude}%", crude == 0 ? HudStyle.Lichen : HudStyle.Wound),
                ("Fine", recipe.QualityRoll ? $"{fine}%" : "—", HudStyle.Bone)));
            card.AddChild(UiKit.Words("Needs " + string.Join(", ", recipe.Inputs.Select(i =>
                $"{i.Count} × {Names.Of(session, i.ItemId)} (have {view.Inventory.Where(e => e.DefId == i.ItemId).Sum(e => e.Count)})")), 16, HudStyle.Ash));
            card.AddChild(AvailabilityBlock(AvailabilityRules.Recipe(recipe, context), checklist: false));
            list.AddChild(card);
        }
        _panes.Detail.AddChild(list);

        // Findings (canon 26 W7): what trying at a station taught, oldest first; a found recipe is above, in Known recipes.
        var findings = UiKit.Section("Findings", "what trying taught you");
        if (simulation.Findings.IsEmpty)
            findings.AddChild(UiKit.Quiet("None yet - try what you carry at a bench or a forge, and what comes of it is kept here."));
        foreach (var finding in simulation.Findings)
        {
            var line = FindingWords.Line(finding, key => Names.Of(session, key));
            findings.AddChild(UiKit.Row(UiKit.Chip(FindingKeys.Key(finding.Kind), finding.Kind == FindingKind.Found ? HudStyle.Lichen : HudStyle.Brass,
                finding.Kind == FindingKind.Found ? HudStyle.Lichen : HudStyle.Brass), line.Says, line.Where, height: 44).Root);
        }
        _panes.Detail.AddChild(findings);
    }

    /// <summary>The known techniques (C5), each with where it was learned and its quick slot.</summary>
    private void DrawTechniques(Simulation simulation)
    {
        var session = _session!;
        Title("Techniques", "moves known, from teachers, books and practice");
        var list = UiKit.Section("Known techniques");
        list.AddThemeConstantOverride("separation", 18);
        var known = simulation.Player.Progression.Known.Where(k => IsTechnique(k.Key)).OrderBy(k => k.Value.Tick).ToList();
        if (known.Count == 0)
            list.AddChild(UiKit.Quiet("None yet - a technique is taught, read, found or worked out, never bought with points."));
        var context = Context(simulation);
        foreach (var (id, learned) in known)
        {
            var card = Card(id, Source(session, learned));
            if (session.Setup.Techniques.TryGetValue(id, out var technique))
            {
                card.AddChild(UiKit.Words(AvailabilityRules.TechniqueSummary(technique, key => Names.Of(session, key)), 16, HudStyle.Ash));
                card.AddChild(AvailabilityBlock(AvailabilityRules.Technique(technique, context), checklist: true));
            }
            card.AddChild(SlotPicker(simulation, id));
            list.AddChild(card);
        }
        _panes.Detail.AddChild(list);
    }

    /// <summary>The discipline map: every discipline as a column, its level as a brass fill, what it opens as nodes by threshold.</summary>
    private void DrawMap(CharacterProgression progression)
    {
        var session = _session!;
        var rules = session.Setup.Progression;
        Title("Discipline map", "every discipline, how far it has grown, and what opens where");
        var columns = rules.Roster.Select(r => new DisciplineMap.Column(r.Id, Names.Of(session, r.Id), r.Family,
            ProgressionEngine.SkillLevel(progression, r.Id), Openings(session, r.Id).Where(o => !o.Band).ToList())).ToList();
        var map = UiKit.Section("By family");
        map.AddChild(new DisciplineMap(columns, rules.Skills.CommonCeiling, rules.SkillBands) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        map.AddChild(UiKit.Words("A filled node is open; a hollow one is still ahead. Point at a node to read it.", 16, HudStyle.Muted));
        _panes.Detail.AddChild(map);
    }

    private void Title(string title, string note)
    {
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 4);
        var name = HudStyle.Text(HudStyle.Caps, 30, HudStyle.Bone, caps: true);
        name.Text = title;
        head.AddChild(name);
        head.AddChild(UiKit.Words(note, 16, HudStyle.Muted));
        _panes.Detail.AddChild(head);
    }

    /// <summary>What the evaluator needs of the character now: pools, the weapon family in hand, what is carried, the station open, the waits.</summary>
    private AvailabilityContext Context(Simulation simulation)
    {
        var session = _session!;
        var view = simulation.Player;
        var progression = view.Progression;
        string? held = null, heldName = null;
        if (view.Equipment.TryGetValue(EquipSlot.MainHand, out var hand) && view.Inventory.FirstOrDefault(e => e.ItemId == hand) is { } entry
            && session.Setup.Items.Catalog.Find(entry.DefId) is { Weapon.SkillId: { } skill })
        {
            held = skill;
            heldName = Names.Of(session, entry.DefId);
        }
        return new AvailabilityContext(id => ProgressionEngine.SkillLevel(progression, id),
            progression.Pools.Stamina ?? (int)view.Stats.StaminaMax, progression.Pools.Focus ?? (int)view.Stats.FocusMax, progression.Pools.Strain,
            (int)view.Stats.StrainTolerance, progression.Pools.Health ?? (int)view.Stats.HealthMax, held, heldName,
            id => view.Inventory.Where(e => e.DefId == id).Sum(e => e.Count), NearStation?.Invoke(),
            id => (simulation.TechniqueCooldown(id) + 19) / 20, key => Names.Of(session, key));
    }

    /// <summary>
    /// An availability as the row shows it: the word as a chip, the first reason beside it, and - on a detail card - the checklist of what
    /// is needed against what the character has, each marked met or not met in words (never by colour alone).
    /// </summary>
    private static Control AvailabilityBlock(Availability availability, bool checklist)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 4);
        var tone = availability.Ready ? HudStyle.Lichen : availability.Kind is AvailabilityKind.NeedsGear or AvailabilityKind.NeedsSituation ? HudStyle.Brass : HudStyle.Ember;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(UiKit.Chip(availability.Word, tone, tone));
        if (availability.Reason is { } reason)
            head.AddChild(UiKit.Words(reason, 16, HudStyle.Bone));
        block.AddChild(head);
        if (availability.Caution is { } caution)
            block.AddChild(UiKit.Words(caution, 16, HudStyle.Wound));
        if (checklist)
        {
            block.AddChild(UiKit.Caption("What you need"));
            foreach (var line in availability.Checklist)
            {
                var met = UiKit.Chip(line.Met ? "met" : "not met", line.Met ? HudStyle.Lichen : HudStyle.Ember, line.Met ? HudStyle.Lichen : HudStyle.Ember);
                met.CustomMinimumSize = new Vector2(96, 0);
                block.AddChild(UiKit.Row(met, line.Need, $"you: {line.Have}", height: 36).Root);
            }
        }
        return block;
    }

    /// <summary>A known thing's card: its icon and name, and where it was learned at the right.</summary>
    private VBoxContainer Card(string id, string source, string? icon = null)
    {
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 4);
        var row = UiKit.Row(UiKit.Icon(_icons, icon ?? id, 36), Names.Of(_session!, id), height: 40);
        // One line at its own width: a wrapping label beside the expanding name is squeezed to a letter a line.
        var from = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash);
        from.Text = source;
        from.VerticalAlignment = VerticalAlignment.Center;
        row.Root.AddChild(from);
        card.AddChild(row.Root);
        return card;
    }

    private static HBoxContainer Numbers(params (string Label, string Value, Color Tone)[] values)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        foreach (var (label, value, tone) in values)
        {
            var pair = new HBoxContainer();
            pair.AddThemeConstantOverride("separation", 8);
            pair.AddChild(UiKit.Caption(label));
            var shown = HudStyle.Text(HudStyle.Strong, 18, tone);
            shown.Text = value;
            pair.AddChild(shown);
            row.AddChild(pair);
        }
        return row;
    }

    /// <summary>
    /// The eight quick slots as key caps: the ones holding <paramref name="id"/> marked; pressing another puts it there (it leaves any
    /// other slot), pressing its own clears it.
    /// </summary>
    internal Control SlotPicker(Simulation simulation, string id)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(UiKit.Caption("Quick slot"));
        var bar = simulation.QuickBar;
        for (int slot = 0; slot < bar.Length; slot++)
        {
            int at = slot;
            bool here = bar[slot] == id;
            var button = UiKit.Action(HelpPanel.Key($"quick_{slot + 1}"), () => Assign?.Invoke(at, here ? null : id), here ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                compact: true, id: $"slot/{id}/{slot}");
            button.TooltipText = here ? "Take it off this slot"
                : bar[slot] is { } other ? $"Put it here, in place of {Names.Of(_session!, other)}" : "Put it on this slot";
            if (here)
                UiKit.Select(button, true);
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>Where something known was learned, with its source label: "Learned: taught by Kera Voss".</summary>
    internal static string Source(GameSession session, KnownTechnique learned) => CharacterWords.Labelled(learned, id => Names.Of(session, id));
}

/// <summary>
/// The threshold ruler (05_SKILLS_WIREFRAME's first eventual view): 0 to the common ceiling with a tick every five levels, the bands named
/// above it, the level as a brass fill, and each opening as a mark under it - lichen once reached, ember for the next, iron beyond.
/// </summary>
public sealed partial class ThresholdRuler : Control
{
    private readonly int _level, _ceiling;
    private readonly IReadOnlyList<SkillBand> _bands;
    private readonly IReadOnlyList<SkillsPanel.Opening> _openings;

    internal ThresholdRuler(int level, int ceiling, IReadOnlyList<SkillBand> bands, IReadOnlyList<SkillsPanel.Opening> openings)
    {
        _level = level;
        _ceiling = ceiling;
        _bands = bands;
        _openings = openings.Where(o => !o.Band && o.Level <= ceiling).ToList();
        CustomMinimumSize = new Vector2(0, 88 * HudStyle.TextScale);
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = " ";
    }

    /// <summary>Where a level sits: on a square-root scale, so the early levels - where Phase 1's openings all are - get the room.</summary>
    private float X(int level) => 8 + (Size.X - 16) * (float)Math.Sqrt((double)Math.Clamp(level, 0, _ceiling) / _ceiling);

    private float Track => 34 * HudStyle.TextScale;

    public override void _Draw()
    {
        var font = HudStyle.Caps;
        int size = HudStyle.Sized(16);
        float y = Track;
        DrawRect(new Rect2(X(0), y - 2, X(_ceiling) - X(0), 4), HudStyle.Track);
        DrawRect(new Rect2(X(0), y - 2, X(_level) - X(0), 4), HudStyle.Brass);
        for (int level = 0; level <= _ceiling; level += 5)
            DrawRect(new Rect2(X(level), y - (level % 10 == 0 ? 7 : 5), 1, level % 10 == 0 ? 14 : 10), HudStyle.Iron);
        // Each band's floor marked, and its name centred over its stretch up to the next floor; the ceiling is the ruler's end.
        var bands = _bands.Where(b => b.From <= _ceiling).ToList();
        for (int i = 0; i < bands.Count; i++)
        {
            float x = X(bands[i].From);
            var tone = bands[i].From <= _level ? HudStyle.Brass : HudStyle.IronBright;
            DrawRect(new Rect2(x - 1, y - 12, 2, 24), tone);
            if (bands[i].From >= _ceiling)
                continue;
            float end = X(i + 1 < bands.Count ? bands[i + 1].From : _ceiling);
            DrawString(font, new Vector2(x, y - 14), bands[i].Name.ToUpperInvariant(), HorizontalAlignment.Center, end - x, size,
                bands[i].From <= _level ? HudStyle.Brass : HudStyle.Muted);
        }
        var next = _openings.FirstOrDefault(o => o.Level > _level);
        foreach (var opening in _openings)
        {
            var tone = opening.Level <= _level ? HudStyle.Lichen : opening == next ? HudStyle.Ember : HudStyle.IronBright;
            float x = X(opening.Level);
            DrawColoredPolygon(new[] { new Vector2(x, y + 8), new Vector2(x + 6, y + 14), new Vector2(x, y + 20), new Vector2(x - 6, y + 14) }, tone);
        }
        foreach (int level in new[] { 0, 5, 10 }.Concat(_bands.Select(b => b.From)).Where(l => l <= _ceiling).Distinct())
            DrawString(font, new Vector2(X(level) - 20, y + 40 * HudStyle.TextScale), $"{level}", HorizontalAlignment.Center, 40, size, HudStyle.Muted);
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var near = _openings.Where(o => Math.Abs(X(o.Level) - atPosition.X) <= 8 && atPosition.Y > Track).ToList();
        return near.Count == 0 ? string.Empty : string.Join("\n", near.Select(o => $"{o.Level}: {o.Words}"));
    }
}

/// <summary>
/// The discipline map (05_SKILLS_WIREFRAME's second eventual view): the disciplines as columns by family, levels rising from 0 to the
/// common ceiling with the bands across them, each column's level as a brass fill and what it opens as nodes at their thresholds.
/// </summary>
public sealed partial class DisciplineMap : Control
{
    public sealed record Column(string Id, string Name, string Family, int Level, IReadOnlyList<SkillsPanel.Opening> Openings);

    private readonly IReadOnlyList<Column> _columns;
    private readonly int _ceiling;
    private readonly IReadOnlyList<SkillBand> _bands;
    private const float Head = 50, Foot = 52, Gutter = 92;

    public DisciplineMap(IReadOnlyList<Column> columns, int ceiling, IReadOnlyList<SkillBand> bands)
    {
        _columns = columns;
        _ceiling = ceiling;
        _bands = bands;
        CustomMinimumSize = new Vector2(0, 340 * HudStyle.TextScale);
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = " ";
    }

    private float Top => Head * HudStyle.TextScale;

    private float Bottom => Size.Y - Foot * HudStyle.TextScale;

    /// <summary>Where a level sits: on the ruler's square-root scale.</summary>
    private float Y(int level) => Bottom - (Bottom - Top) * (float)Math.Sqrt((double)Math.Clamp(level, 0, _ceiling) / _ceiling);

    /// <summary>A column's width: the map less the bands' names in the gutter at the right.</summary>
    private float Width => (Size.X - Gutter) / Math.Max(1, _columns.Count);

    private float X(int column) => Width * (column + 0.5f);

    public override void _Draw()
    {
        var caps = HudStyle.Caps;
        int size = HudStyle.Sized(16);
        // The bands across every column, each named with its level in the gutter at the right of its line.
        foreach (var band in _bands.Where(b => b.From <= _ceiling))
        {
            float y = Y(band.From);
            DrawLine(new Vector2(0, y), new Vector2(Size.X - Gutter + 4, y), new Color(HudStyle.Iron, 0.5f), 1);
            DrawString(caps, new Vector2(Size.X - Gutter + 10, y + size * 0.35f), band.Name.ToUpperInvariant(), HorizontalAlignment.Left,
                Gutter - 10, size, HudStyle.Muted);
        }
        string? family = null;
        int families = 0;
        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            float x = X(i);
            if (column.Family != family)
            {
                family = column.Family;
                // Every other family's name a line lower, so a one-column family's name may run on over its neighbour's.
                float line = size * (1 + 1.2f * (families++ % 2));
                float left = Width * i;
                if (i > 0)
                    DrawLine(new Vector2(left, Top - 6), new Vector2(left, Size.Y), HudStyle.Iron, 1);
                DrawString(caps, new Vector2(left + 6, line), family.ToUpperInvariant(), HorizontalAlignment.Left, Width * (_columns.Count(c => c.Family == family) + 1) - 8,
                    size, HudStyle.Brass);
            }
            DrawRect(new Rect2(x - 2, Top, 4, Bottom - Top), HudStyle.Track);
            if (column.Level > 0)
                DrawRect(new Rect2(x - 2, Y(column.Level), 4, Bottom - Y(column.Level)), HudStyle.Brass);
            foreach (var opening in column.Openings.Where(o => o.Level <= _ceiling))
            {
                var at = new Vector2(x, Y(opening.Level));
                if (opening.Level <= column.Level)
                    DrawCircle(at, 6, HudStyle.Lichen);
                else
                    DrawArc(at, 6, 0, Mathf.Tau, 20, HudStyle.IronBright, 2);
            }
            DrawMultilineString(HudStyle.Body, new Vector2(x - Width / 2 + 2, Bottom + size + 4), column.Name, HorizontalAlignment.Center, Width - 4, size, 2,
                column.Level > 0 ? HudStyle.Bone : HudStyle.Ash);
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            if (Math.Abs(X(i) - atPosition.X) > Width / 2)
                continue;
            var column = _columns[i];
            var near = column.Openings.Where(o => Math.Abs(Y(o.Level) - atPosition.Y) <= 8).ToList();
            return near.Count > 0
                ? string.Join("\n", near.Select(o => $"{column.Name} {o.Level}: {o.Words}"))
                : $"{column.Name}: level {column.Level}";
        }
        return string.Empty;
    }
}
