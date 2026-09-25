// UNNAMED Presentation - what the game hears: the simulation's events and the drawn world, mapped to the sound set's semantic IDs
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Greybox;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// The mapping layer between the game and PHASE1_AUDIO_EVENT_CONTRACT.md. The contract's events - <c>footstep(surface, gait)</c>,
/// <c>swing(family)</c>, <c>impact(family, material)</c>, <c>walk_step()</c>, <c>cell_detail()</c>, <c>gear_shift()</c> and the rest - are
/// the audio side's names for moments, not the game's API: each is recognised here from what the game already has (an event the
/// simulation published, or the drawn world changing between frames) and resolved to a family of stable IDs in the manifest. The
/// discriminators come from the art bindings (a weapon's family, a creature's voice and flesh, a cell's bed, details and surface), so
/// no content ID is named in code. Presentation only: it listens and plays, and never touches the simulation or its random numbers.
/// </summary>
public partial class SoundEvents : Node
{
    private const float Stride = 0.75f;
    private const float RunStride = 1.1f;
    private const float RunSpeed = 2.2f;
    private const float CreatureRunSpeed = 2.6f;
    private const float Hearing = 40f;
    private const double BedFadeSeconds = 3;

    private readonly Random _chance = new();
    private readonly Dictionary<EntityId, (Vector3 At, double NextStep, double NextIdle)> _creatures = new();
    private readonly Dictionary<string, AudioStreamPlayer> _beds = new(StringComparer.Ordinal);
    private readonly List<(string Id, AudioStreamPlayer? Player, double From)> _strain = new();
    private readonly List<Aabb> _indoors = new();
    private readonly Queue<string> _workings = new();
    private SoundBank _bank = null!;
    private GameSession _session = null!;
    private ArtBindings _bindings = null!;
    private AudioStreamPlayer? _forge;
    private Vector3 _feet;
    private float _walked;
    private double _airborneFor;
    private string? _bed;
    private string? _lastDetail;
    private double _nextDetail;
    private string? _openContainer;
    private bool _panelsOpen;
    private string? _wielded;
    private bool _wieldedKnown;
    private double _clock;

    public void Bind(GameSession session, ArtBindings bindings, SoundBank bank)
    {
        _session = session;
        _bindings = bindings;
        _bank = bank;
        if (bank.Count == 0)
            return;
        // Indoors is inside a building's walls: the bounds of the structures that share a wall prefix.
        foreach (string prefix in bindings.BuildingPrefixes)
        {
            var walls = session.Setup.Layout.Space.Blockers.Where(b => b.Id.StartsWith(prefix, StringComparison.Ordinal)).OfType<BoxBlocker>().ToList();
            if (walls.Count > 0)
            {
                var from = new Vector3(walls.Min(w => w.MinXMm) / 1000f, -1000, walls.Min(w => w.MinZMm) / 1000f);
                var to = new Vector3(walls.Max(w => w.MaxXMm) / 1000f, 1000, walls.Max(w => w.MaxZMm) / 1000f);
                _indoors.Add(new Aabb(from, to - from));
            }
        }
        foreach (string band in new[] { "moderate", "high", "critical" })
        {
            string id = $"sfx.magic.strain.{band}.01";
            _strain.Add((id, bank.Loop(id), band switch { "moderate" => 0.35, "high" => 0.65, _ => 0.85 }));
        }
        if (bindings.ForgeStation is not null)
            _forge = bank.Loop("sfx.crafting.forge.ambience.01");
        _nextDetail = Irregular();
        Subscribe();
    }

    /// <summary>
    /// A frame of the drawn world: where the character's feet are and how fast they move, in the air or not, Strain as a share of its
    /// tolerance, the container and station open, and whether a panel is up.
    /// </summary>
    public void Update(Vector3 feet, float speed, bool airborne, double strain, string? openContainer, bool panelsOpen, double delta)
    {
        if (_bank.Count == 0)
            return;
        _clock += delta;
        var moved = new Vector2(feet.X - _feet.X, feet.Z - _feet.Z).Length();
        _feet = feet;
        string surface = Surface(feet);
        // footstep(surface, gait): one per stride on the ground; a sprint uses the run family.
        if (airborne)
        {
            _airborneFor += delta;
            _walked = 0;
        }
        else
        {
            if (_airborneFor > 0.3)
                _bank.Play($"sfx.player.land.{(surface == "stone" ? "stone" : "dirt")}", feet);
            _airborneFor = 0;
            _walked += moved < 2f ? moved : 0;
            bool run = speed > RunSpeed;
            if (speed > 0.3f && _walked >= (run ? RunStride : Stride))
            {
                _walked = 0;
                _bank.Play($"sfx.player.footstep.{surface}.{(run ? "run" : "walk")}", feet);
            }
        }
        // The weapon a new game or a load starts with is simply in hand; only a change is heard.
        string? family = Family(_session.Simulation!.Combat.Weapon.Source);
        if (_wieldedKnown)
            Wielding(family);
        _wielded = family;
        _wieldedKnown = true;
        Creatures(delta);
        Ambience(feet, delta);
        StrainLayers(strain, delta);
        Forge(feet);
        // interaction_open() / interaction_close() for a chest; the menus opening and closing.
        if (openContainer != _openContainer)
        {
            string? key = openContainer ?? _openContainer;
            if (key is not null && _bindings.Containers.GetValueOrDefault(key)?.Sound is { } sound)
                _bank.Play($"sfx.interaction.{sound}.{(openContainer is not null ? "open" : "close")}", feet);
            _openContainer = openContainer;
        }
        if (panelsOpen != _panelsOpen)
        {
            _bank.Play(panelsOpen ? "sfx.ui.menu.open" : "sfx.ui.menu.close");
            _panelsOpen = panelsOpen;
        }
    }

    /// <summary>A shot came to rest as drawn: a working's impact where it burst; an arrow's in the wall or stone it struck.</summary>
    public void Arrived(Vector3 at, bool working, bool struck)
    {
        if (_bank.Count == 0)
            return;
        if (working)
        {
            if (_workings.Count > 0 && Stem(_workings.Dequeue()) is { } stem)
                _bank.Play($"sfx.magic.{stem}.impact", at);
            return;
        }
        if (!struck && MaterialAt(at) is { } material && material is "wood" or "stone")
            _bank.Play($"sfx.weapon.bow.arrow.impact.{material}", at);
    }

    private void Subscribe()
    {
        var s = _session;
        s.Subscribe<AttackStarted>(e =>
        {
            double commit = e.WindupTicks * _session.TickSeconds;
            if (e.Attacker == PlayerId)
            {
                switch (Family(e.Source))
                {
                    case "sword":
                        _bank.Play("sfx.weapon.sword.swing.light", Chest(_feet), commit);
                        break;
                    case "polearm":
                        _bank.Play("sfx.weapon.polearm.thrust", Chest(_feet), commit);
                        break;
                    case "bow":
                        _bank.Play("sfx.player.weapon.bow.nock", Chest(_feet));
                        _bank.Play("sfx.weapon.bow.draw", Chest(_feet));
                        break;
                }
            }
            else if (Companion(e.Attacker) is not null && _bindings.CompanionWeapon?.Family == "polearm" && Where(e.Attacker) is { } at)
            {
                _bank.Play("sfx.weapon.polearm.thrust", at, commit);
            }
            else if (Creature(e.Attacker) is { } creature && Voice(creature.DefId) is { } voice && Where(e.Attacker) is { } at2)
            {
                // attack() fires with the attack's commit, not its windup.
                _bank.Play($"sfx.creature.{voice}.attack", at2, commit);
            }
        });
        s.Subscribe<HitResolved>(Hit);
        s.Subscribe<CreatureNoticed>(e =>
        {
            if (e.Mind == CreatureMind.Engaged && Creature(e.Creature) is { } c && Voice(c.DefId) is { } voice && Where(e.Creature) is { } at)
                _bank.Play($"sfx.creature.{voice}.alert", at);
        });
        s.Subscribe<CreatureKilled>(e =>
        {
            if (Voice(e.DefId) is { } voice && Where(e.Creature) is { } at)
                _bank.Play($"sfx.creature.{voice}.death", at);
        });
        s.Subscribe<StanceChanged>(e =>
        {
            if (e.Actor == PlayerId)
                _bank.Play(e.Stance == Stance.Crouched ? "sfx.player.crouch.enter" : "sfx.player.crouch.exit", _feet);
        });
        s.Subscribe<Jumped>(e =>
        {
            if (e.Actor == PlayerId)
                _bank.Play("sfx.player.jump.effort", Chest(_feet));
        });
        s.Subscribe<ShotLoosed>(e =>
        {
            // A thrown working is heard where it bursts (Arrived); remember which one is in the air.
            if (_session.Setup.Magic.Formulas.ContainsKey(e.Source))
                _workings.Enqueue(e.Source);
            if (e.Attacker != PlayerId || Family(e.Source) != "bow")
                return;
            _bank.Play("sfx.weapon.bow.release", Chest(_feet));
            _bank.Play("sfx.weapon.bow.arrow.flight", Chest(_feet), 0.05);
        });
        s.Subscribe<CastStarted>(e =>
        {
            // cast_start(): the formulas that have one (the ward is heard at its activation).
            if (e.Caster == PlayerId && Stem(e.FormulaId) is { } stem && _bank.Has($"sfx.magic.{stem}.cast"))
                _bank.Play($"sfx.magic.{stem}.cast", Chest(_feet));
        });
        s.Subscribe<CastCompleted>(e =>
        {
            if (e.Caster != PlayerId || Stem(e.FormulaId) is not { } stem)
                return;
            // A working's release: the ward's activation, the mending's resolution, the bolt's flight.
            foreach (string moment in new[] { "activate", "resolve", "travel" })
            {
                if (_bank.Has($"sfx.magic.{stem}.{moment}"))
                {
                    _bank.Play($"sfx.magic.{stem}.{moment}", Chest(_feet));
                    break;
                }
            }
        });
        s.Subscribe<EffectExpired>(e =>
        {
            if (e.Target == PlayerId && FormulaApplying(e.EffectId) is { } stem && _bank.Has($"sfx.magic.{stem}.end"))
                _bank.Play($"sfx.magic.{stem}.end", Chest(_feet));
        });
        s.Subscribe<DoorToggled>(e => _bank.Play(e.Open ? "sfx.interaction.door.open" : "sfx.interaction.door.close", Where(e.Actor) ?? _feet));
        s.Subscribe<TookAll>(e =>
        {
            if (e.Actor == PlayerId && e.Taken > 0)
                _bank.Play("sfx.interaction.loot.take_all", _feet);
        });
        s.Subscribe<ItemMoved>(e =>
        {
            if (e.Actor == PlayerId && e.From == ItemPlace.Ground && e.To == ItemPlace.Carried)
                _bank.Play("sfx.interaction.pickup", _feet);
        });
        s.Subscribe<ItemEquipped>(e => Equipped(e.Actor, e.Slot, true));
        s.Subscribe<ItemUnequipped>(e => Equipped(e.Actor, e.Slot, false));
        s.Subscribe<NodeGathered>(e =>
        {
            // gather_hit() then gather_complete(): the pick into the seam, and the ore breaking free.
            if (_bindings.Nodes.GetValueOrDefault(e.NodeDefId)?.Sound != "mining")
                return;
            _bank.Play("sfx.crafting.mining.strike", _feet);
            _bank.Play("sfx.crafting.mining.strike", _feet, 0.45);
            _bank.Play("sfx.crafting.ore.break", _feet, 0.9);
        });
        s.Subscribe<ItemCrafted>(_ =>
        {
            // craft_hit() at an anvil (a few blows), then craft_complete().
            bool anvil = _openStation is { } station && _bindings.Stations.GetValueOrDefault(station)?.Sound == "anvil";
            for (int i = 0; anvil && i < 3; i++)
                _bank.Play("sfx.crafting.anvil.strike", _feet, 0.4 * i);
            _bank.Play("sfx.crafting.complete", _feet, anvil ? 1.3 : 0);
        });
        s.Subscribe<PlayerDied>(_ => _bank.Play("sfx.player.death", _feet));
        s.Subscribe<CommandRejected>(_ => _bank.Play("sfx.ui.error"));
        s.Subscribe<ReplyChosen>(_ => _bank.Play("sfx.ui.select"));
        s.Subscribe<ItemBought>(_ => _bank.Play("sfx.ui.select"));
        s.Subscribe<ItemSold>(_ => _bank.Play("sfx.ui.select"));
        s.Subscribe<ConversationEnded>(_ => _bank.Play("sfx.ui.back"));
    }

    private string? _openStation;

    /// <summary>The station open alongside the inventory (its kind), for the sound of what is made there.</summary>
    public void SetStation(string? kind) => _openStation = kind;

    private void Hit(HitResolved e)
    {
        if (e.Dodged || Where(e.Target) is not { } at)
            return;
        var simulation = _session.Simulation!;
        string? family = e.Attacker == PlayerId ? Family(e.Source)
            : Companion(e.Attacker) is not null ? _bindings.CompanionWeapon?.Family : null;
        // An arrow is heard where it lands, when it lands.
        double delay = family == "bow" && Where(e.Attacker) is { } from ? from.DistanceTo(at) / ProjectilesView.ArrowSpeed : 0;
        var creature = Creature(e.Target);
        if (e.Blocked)
        {
            // block(): sword only.
            if (e.Target == PlayerId && Family(simulation.Combat.Weapon.Source) == "sword")
                _bank.Play("sfx.weapon.sword.block", Chest(at));
            return;
        }
        // impact(weapon family, target material): the matrix of families; an arrow on plate has no asset of its own and takes the spear's.
        string material = creature is not null ? _bindings.Creatures.GetValueOrDefault(creature.DefId)?.Flesh ?? "flesh" : "flesh";
        string? impact = family switch
        {
            "sword" => $"sfx.weapon.sword.impact.{material}",
            "polearm" => $"sfx.weapon.polearm.impact.{material}",
            "bow" => material == "plate" ? "sfx.weapon.polearm.impact.plate" : $"sfx.weapon.bow.arrow.impact.{material}",
            _ => null,
        };
        if (impact is not null)
            _bank.Play(impact, Chest(at), delay);
        if (creature is not null && e.HealthAfter > 0 && Voice(creature.DefId) is { } voice)
            _bank.Play($"sfx.creature.{voice}.hurt", Chest(at), delay + 0.05);
        if (e.Target == PlayerId && e.Damage > 0)
        {
            var combat = simulation.Combat;
            bool heavy = e.Critical || e.Damage * 100 >= combat.MaxHealth * 15;
            _bank.Play(heavy ? "sfx.player.hurt.heavy" : "sfx.player.hurt.light", Chest(at));
            // ward_hit(): a blow landing on the braced body.
            foreach (var effect in combat.Effects)
            {
                if (FormulaApplying(effect.EffectId) is { } stem && _bank.Has($"sfx.magic.{stem}.hit"))
                    _bank.Play($"sfx.magic.{stem}.hit", Chest(at));
            }
        }
    }

    /// <summary>gear_shift() on a real equipment change: metal for a weapon, cloth for the rest.</summary>
    private void Equipped(EntityId actor, EquipSlot slot, bool on)
    {
        if (actor != PlayerId)
            return;
        _bank.Play(slot is EquipSlot.MainHand or EquipSlot.OffHand ? "sfx.player.gear.metal" : "sfx.player.gear.cloth", Chest(_feet));
        if (on)
            _bank.Play("sfx.ui.equip");
    }

    /// <summary>The weapon in hand changing: a sword sheathed as it goes and drawn as it comes; a spear brought to guard.</summary>
    private void Wielding(string? family)
    {
        if (family == _wielded)
            return;
        if (_wielded == "sword")
            _bank.Play("sfx.player.weapon.sword.sheath", Chest(_feet));
        if (family == "sword")
            _bank.Play("sfx.weapon.sword.draw", Chest(_feet), 0.15);
        else if (family == "polearm")
            _bank.Play("sfx.player.weapon.spear.ready", Chest(_feet), 0.15);
        _wielded = family;
    }

    /// <summary>
    /// walk_step() and its faster kin for each creature near enough to hear, one sound per footfall (the spider's carry two steps each,
    /// so they come at their own length); and idle(), no oftener than every 8-15 s a creature.
    /// </summary>
    private void Creatures(double delta)
    {
        foreach (var c in _session.Simulation!.Creatures)
        {
            if (!c.Alive || _bindings.Creatures.GetValueOrDefault(c.DefId) is not { } art)
                continue;
            var at = new Vector3(c.Body.XMm / 1000f, c.Body.YMm / 1000f, c.Body.ZMm / 1000f);
            if (!_creatures.TryGetValue(c.Id, out var heard))
                heard = (at, _clock, _clock + 8 + _chance.NextDouble() * 7);
            float speed = delta > 0 ? new Vector2(at.X - heard.At.X, at.Z - heard.At.Z).Length() / (float)delta : 0;
            bool near = at.DistanceTo(_feet) < Hearing;
            if (near && speed > 0.3f && speed < 20f && _clock >= heard.NextStep)
            {
                bool run = speed > CreatureRunSpeed;
                string gait = art.Steps.GetValueOrDefault(run ? "run" : "walk") ?? "walk";
                if (_bank.Play($"sfx.creature.{art.Voice}.{gait}", at))
                    heard.NextStep = _clock + (art.StepsPerSound > 1 ? (run ? 0.6 : 0.7) : (run ? 0.3 : 0.5));
            }
            if (near && c.Mind != CreatureMind.Engaged && !c.Asleep && _clock >= heard.NextIdle)
            {
                _bank.Play($"sfx.creature.{art.Voice}.idle", at + Vector3.Up);
                heard.NextIdle = _clock + 8 + _chance.NextDouble() * 7;
            }
            _creatures[c.Id] = (at, heard.NextStep, heard.NextIdle);
        }
    }

    /// <summary>The cell's looping bed, cross-faded on entry; and cell_detail() on top of it, at irregular intervals, placed round the listener.</summary>
    private void Ambience(Vector3 feet, double delta)
    {
        string cell = CellKey.OfWorld(feet.X, feet.Z).ToString();
        var sounds = _bindings.CellSounds.GetValueOrDefault(cell);
        _bed = sounds?.Bed;
        if (_bed is not null && !_beds.ContainsKey(_bed) && _bank.Loop(_bed) is { } started)
            _beds[_bed] = started;
        float step = (float)(delta / BedFadeSeconds);
        foreach (var (id, player) in _beds)
        {
            float level = Mathf.DbToLinear(player.VolumeDb);
            level = Mathf.MoveToward(level, id == _bed ? 1f : 0f, step);
            player.VolumeDb = Mathf.LinearToDb(Math.Max(level, 0.0001f));
        }
        if (sounds is null || sounds.Details.Count == 0 || _clock < _nextDetail)
            return;
        var choices = sounds.Details.Count > 1 ? sounds.Details.Where(d => d != _lastDetail).ToList() : sounds.Details.ToList();
        string detail = choices[_chance.Next(choices.Count)];
        _lastDetail = detail;
        float bearing = (float)(_chance.NextDouble() * Mathf.Tau), distance = 12f + (float)_chance.NextDouble() * 16f;
        _bank.Play(detail, feet + new Vector3(Mathf.Cos(bearing) * distance, 2f, Mathf.Sin(bearing) * distance));
        _nextDetail = _clock + Irregular();
    }

    /// <summary>The three Strain layers cross-fading with Strain: moderate from 0.35 of tolerance, high from 0.65, critical from 0.85.</summary>
    private void StrainLayers(double strain, double delta)
    {
        for (int i = 0; i < _strain.Count; i++)
        {
            var (_, player, from) = _strain[i];
            if (player is null)
                continue;
            double to = i + 1 < _strain.Count ? _strain[i + 1].From : 2;
            // In over the first 0.1 above its band's start; out as the next band comes in.
            double level = Math.Clamp((strain - from) / 0.1 + 0.001, 0, 1) * (1 - Math.Clamp((strain - to) / 0.1, 0, 1));
            float current = Mathf.DbToLinear(player.VolumeDb);
            player.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, Mathf.MoveToward(current, (float)level, (float)delta)));
        }
    }

    /// <summary>station_ambience(): the forge, loud beside it and gone twenty metres off.</summary>
    private void Forge(Vector3 feet)
    {
        if (_forge is null)
            return;
        float nearest = _session.Setup.Layout.Stations.Where(s => s.Kind == _bindings.ForgeStation)
            .Select(s => new Vector2(s.XMm / 1000f - feet.X, s.ZMm / 1000f - feet.Z).Length()).DefaultIfEmpty(1e6f).Min();
        float level = Math.Clamp(1f - (nearest - 3f) / 17f, 0f, 1f);
        _forge.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, level * level));
    }

    private string Surface(Vector3 feet)
    {
        if (_indoors.Any(box => box.HasPoint(feet)))
            return _bindings.InteriorSurface;
        return _bindings.CellSounds.GetValueOrDefault(CellKey.OfWorld(feet.X, feet.Z).ToString())?.Surface ?? "dirt";
    }

    /// <summary>What a point rests against: the structure it touches (its bound material), else the cell's ground.</summary>
    private string? MaterialAt(Vector3 at)
    {
        long x = (long)(at.X * 1000), z = (long)(at.Z * 1000);
        foreach (var blocker in _session.Setup.Layout.Space.Blockers)
        {
            bool touches = blocker switch
            {
                BoxBlocker b => x >= b.MinXMm - 600 && x <= b.MaxXMm + 600 && z >= b.MinZMm - 600 && z <= b.MaxZMm + 600,
                CircleBlocker c => (x - c.CenterXMm) * (x - c.CenterXMm) + (z - c.CenterZMm) * (z - c.CenterZMm) <= (c.RadiusMm + 600) * (c.RadiusMm + 600),
                _ => false,
            };
            if (touches)
                return _bindings.StructureMaterial(blocker.Id);
        }
        return Surface(at);
    }

    private double Irregular() => 7 + _chance.NextDouble() * 16;

    private EntityId PlayerId => _session.Simulation!.PlayerId;

    private string? Family(string? itemDefId) => itemDefId is null ? null : _bindings.Weapons.GetValueOrDefault(itemDefId)?.Family;

    private string? Voice(string defId) => _bindings.Creatures.GetValueOrDefault(defId)?.Voice is { Length: > 0 } voice ? voice : null;

    private string? Stem(string formulaId) => _bindings.FormulaSounds.GetValueOrDefault(formulaId);

    private string? FormulaApplying(string effectId) =>
        _session.Setup.Magic.Formulas.Values.Where(f => f.Applies.Contains(effectId)).Select(f => Stem(f.Id)).FirstOrDefault(s => s is not null);

    private CreatureView? Creature(EntityId id) => _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == id);

    private CompanionView? Companion(EntityId id) => _session.Simulation!.Companions.FirstOrDefault(c => c.InstanceId == id);

    private Vector3? Where(EntityId id)
    {
        var simulation = _session.Simulation!;
        if (id == simulation.PlayerId)
            return _feet;
        if (Creature(id) is { } creature)
            return new Vector3(creature.Body.XMm / 1000f, creature.Body.YMm / 1000f, creature.Body.ZMm / 1000f);
        return simulation.Npcs.FirstOrDefault(n => n.InstanceId == id) is { } npc
            ? new Vector3(npc.Body.XMm / 1000f, npc.Body.YMm / 1000f, npc.Body.ZMm / 1000f)
            : null;
    }

    private static Vector3 Chest(Vector3 feet) => feet + new Vector3(0, 1.2f, 0);

    /// <summary>
    /// Every family (or single ID) this mapping can ask the sound set for, with the contract's event that asks for it, given the bindings
    /// - the same names the handlers above build, listed here for the static audio coverage check (Phase A, the owner's A4). Optional ones
    /// are asked for only when the set has them (a formula's cast, activation, end or ward hit). A name the handlers build and this list
    /// lacks shows up in the runtime report as requested but unmapped, so the two cannot drift apart unnoticed.
    /// </summary>
    public static IReadOnlyList<MappedSound> Mapped(ArtBindings bindings)
    {
        var list = new List<MappedSound>();
        void Add(string family, string contractEvent, bool optional = false) => list.Add(new MappedSound(family, contractEvent, optional));
        var surfaces = bindings.CellSounds.Values.Select(c => c.Surface).Append(bindings.InteriorSurface).Append("dirt").Distinct(StringComparer.Ordinal);
        foreach (string surface in surfaces)
        {
            Add($"sfx.player.footstep.{surface}.walk", "footstep(surface, walk)");
            Add($"sfx.player.footstep.{surface}.run", "footstep(surface, run)");
        }
        Add("sfx.player.land.stone", "land(stone)");
        Add("sfx.player.land.dirt", "land(dirt)");
        Add("sfx.player.jump.effort", "jump_effort()");
        Add("sfx.player.crouch.enter", "crouch_enter()");
        Add("sfx.player.crouch.exit", "crouch_exit()");
        Add("sfx.player.hurt.light", "hurt(light)");
        Add("sfx.player.hurt.heavy", "hurt(heavy)");
        Add("sfx.player.death", "death()");
        Add("sfx.player.gear.metal", "gear_shift(metal)");
        Add("sfx.player.gear.cloth", "gear_shift(cloth)");
        var families = bindings.Weapons.Values.Select(w => w.Family).Concat(bindings.CompanionWeapon is { } c ? new[] { c.Family } : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToList();
        var materials = bindings.Creatures.Values.Select(c => c.Flesh).Append("flesh").Distinct(StringComparer.Ordinal).ToList();
        foreach (string family in families)
        {
            switch (family)
            {
                case "sword":
                    Add("sfx.weapon.sword.swing.light", "swing(sword, light)");
                    Add("sfx.weapon.sword.block", "block()");
                    Add("sfx.weapon.sword.draw", "draw(sword)");
                    Add("sfx.player.weapon.sword.sheath", "sheathe(sword)");
                    foreach (string material in materials)
                        Add($"sfx.weapon.sword.impact.{material}", $"impact(sword, {material})");
                    break;
                case "polearm":
                    Add("sfx.weapon.polearm.thrust", "swing(polearm)");
                    Add("sfx.player.weapon.spear.ready", "ready(polearm)");
                    foreach (string material in materials)
                        Add($"sfx.weapon.polearm.impact.{material}", $"impact(polearm, {material})");
                    break;
                case "bow":
                    Add("sfx.player.weapon.bow.nock", "nock()");
                    Add("sfx.weapon.bow.draw", "draw_bow()");
                    Add("sfx.weapon.bow.release", "swing(bow): release");
                    Add("sfx.weapon.bow.arrow.flight", "projectile_flight(bow)");
                    foreach (string material in materials)
                        Add(material == "plate" ? "sfx.weapon.polearm.impact.plate" : $"sfx.weapon.bow.arrow.impact.{material}", $"impact(bow, {material})");
                    Add("sfx.weapon.bow.arrow.impact.wood", "impact(arrow, wood): a structure");
                    Add("sfx.weapon.bow.arrow.impact.stone", "impact(arrow, stone): a structure");
                    break;
            }
        }
        foreach (var creature in bindings.Creatures.Values.Where(c => c.Voice.Length > 0))
        {
            foreach (string moment in new[] { "idle", "alert", "attack", "hurt", "death" })
                Add($"sfx.creature.{creature.Voice}.{moment}", $"{moment}()");
            foreach (string gait in new[] { "walk", "run" }.Select(g => creature.Steps.GetValueOrDefault(g) ?? "walk").Distinct(StringComparer.Ordinal))
                Add($"sfx.creature.{creature.Voice}.{gait}", $"{gait}_step()");
        }
        foreach (string stem in bindings.FormulaSounds.Values.Distinct(StringComparer.Ordinal))
        {
            Add($"sfx.magic.{stem}.impact", "impact()", optional: true);
            foreach (string moment in new[] { "cast", "activate", "resolve", "travel", "end", "hit" })
                Add($"sfx.magic.{stem}.{moment}", moment switch { "cast" => "cast_start()", "hit" => "ward_hit()", var m => $"{m}()" }, optional: true);
        }
        foreach (string band in new[] { "moderate", "high", "critical" })
            Add($"sfx.magic.strain.{band}.01", $"strain({band})");
        foreach (var sound in bindings.Containers.Values.Where(c => c.Sound is not null))
        {
            Add($"sfx.interaction.{sound.Sound}.open", "interaction_open()");
            Add($"sfx.interaction.{sound.Sound}.close", "interaction_close()");
        }
        Add("sfx.interaction.door.open", "interaction_open(door)");
        Add("sfx.interaction.door.close", "interaction_close(door)");
        Add("sfx.interaction.loot.take_all", "loot_take_all()");
        Add("sfx.interaction.pickup", "interaction_pickup()");
        if (bindings.Nodes.Values.Any(n => n.Sound == "mining"))
        {
            Add("sfx.crafting.mining.strike", "gather_hit()");
            Add("sfx.crafting.ore.break", "gather_complete()");
        }
        if (bindings.Stations.Values.Any(s => s.Sound == "anvil"))
            Add("sfx.crafting.anvil.strike", "craft_hit()");
        Add("sfx.crafting.complete", "craft_complete()");
        if (bindings.ForgeStation is not null)
            Add("sfx.crafting.forge.ambience.01", "station_ambience()");
        foreach (var cell in bindings.CellSounds.Values)
        {
            Add(cell.Bed, "ambience bed");
            foreach (string detail in cell.Details)
                Add(detail, "cell_detail()");
        }
        foreach (string ui in new[] { "menu.open", "menu.close", "select", "back", "equip", "error" })
            Add($"sfx.ui.{ui}", $"ui {ui}");
        return list.GroupBy(m => m.Family, StringComparer.Ordinal).Select(g => g.First() with { Optional = g.All(m => m.Optional) }).ToList();
    }
}
