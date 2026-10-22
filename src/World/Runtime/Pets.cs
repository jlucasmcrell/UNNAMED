// UNNAMED World - pets at run time: tamed out of the wild or taken from a stall, ordered about, fighting with its own skills,
// drawn off with a lure, and home badly wounded rather than dead (spec 43 and its owner rulings of 2026-10-21)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ─────────────────────────────────────────────────────────────────

/// <summary>
/// Offer a wild animal the food it takes and try to take it (spec 43's taming): the offer is a hold-to-calm channel, and damage or
/// being spotted breaks it. The food is laid down - spent - when the offer begins, whether or not the animal takes it.
/// </summary>
public sealed record TameCommand(EntityId Actor, EntityId Creature, string FoodItem) : GameCommand(Actor);

/// <summary>
/// Take the animal that is already the character's: the pup from its lead at the waystation, out of the pack, into the party.
/// </summary>
public sealed record AdoptPetCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);

/// <summary>
/// Tell a pet what to do (owner ruling 4). <see cref="XMm"/>/<see cref="ZMm"/> are the ground point four of the orders name - where to
/// wait, where to keep watch, where to look ahead from, where the lure leads; the others ignore them.
/// </summary>
public sealed record OrderPetCommand(EntityId Actor, EntityId Pet, PetOrder Order, long XMm = 0, long ZMm = 0) : GameCommand(Actor);

/// <summary>Feed the pet from the pack: the bond rises, and a bond is what makes a wary animal take the hard orders.</summary>
public sealed record FeedPetCommand(EntityId Actor, EntityId Pet, EntityId Item) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>The offer is under way: the animal is being calmed, and this is when and on what odds it would take.</summary>
public sealed record TameAttempted(EntityId Creature, long EndsTick, int ChancePercent, long Tick);

/// <summary>An offer broke: the animal was roused, hurt, or the character drew away.</summary>
public sealed record TameBroken(EntityId Creature, string Reason, long Tick);

/// <summary>The roll was against the character: the animal turns away, having seen the hand that reached for it.</summary>
public sealed record TameFailed(EntityId Creature, int ChancePercent, long Tick);

/// <summary>A wild animal is the character's now.</summary>
public sealed record PetTamed(EntityId Pet, string CreatureId, string Name, long Tick);

/// <summary>A pet came out of the pack: the pup, taken off the lead it travelled on.</summary>
public sealed record PetAdopted(EntityId Pet, string CreatureId, string Name, long Tick);

public sealed record PetOrdered(EntityId Pet, PetOrder Order, long Tick);

/// <summary>It would not: a wary animal and a hard order (owner ruling 1: a tamed animal starts wary and obeys less).</summary>
public sealed record PetRefused(EntityId Pet, PetOrder Order, long Tick);

public sealed record PetFed(EntityId Pet, int Bond, long Tick);

/// <summary>Its own skill was used: a bite that landed, or a lure that took.</summary>
public sealed record PetSkillUsed(EntityId Pet, PetSkillKind Skill, EntityId Creature, long Tick);

/// <summary>The skill reached its next level (owner ruling 3: each pet's own skills level with use).</summary>
public sealed record PetSkillLevelled(EntityId Pet, PetSkillKind Skill, int Level, long Tick);

/// <summary>A pet in a fight was struck back: the creature it bit turned on it.</summary>
public sealed record PetStruck(EntityId Pet, EntityId Creature, int Damage, int Health, long Tick);

/// <summary>At no health: not dead. Home, badly wounded, mending until <see cref="UntilTick"/> (owner ruling 1).</summary>
public sealed record PetWoundedHome(EntityId Pet, long UntilTick, long Tick);

/// <summary>Mended, at home, waiting to be collected.</summary>
public sealed record PetRecovered(EntityId Pet, long Tick);

/// <summary>A young animal grew into the grown one.</summary>
public sealed record PetGrown(EntityId Pet, string CreatureId, string Name, long Tick);

/// <summary>A lure took: the creature is walking to the place the handler chose.</summary>
public sealed record PetLured(EntityId Pet, EntityId Creature, long XMm, long ZMm, long Tick);

/// <summary>A creature that has been led once will not go again for content's while (enemies learn, owner ruling 5).</summary>
public sealed record LureRefused(EntityId Pet, EntityId Creature, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>
/// A pet as the HUD and the panel show it: its own name, role, orders and standing, its health and bond, the skills and their levels,
/// and - while it is away - the tick it is home and mended by.
/// </summary>
public sealed record PetView(EntityId Id, string CreatureId, string Name, PetRole Role, PetOrder Order, PetCondition Condition, int Health,
    int MaxHealth, int Level, int Bond, Body Body, string Doing, ImmutableArray<PetSkill> Skills)
{
    /// <summary>While badly wounded: the tick it is home and mended, and can be called again.</summary>
    public long? HomeUntilTick { get; init; }

    /// <summary>How it is in words: healthy, hurt, critical, or badly wounded and home.</summary>
    public string Standing => Condition == PetCondition.Wounded ? "badly wounded, home"
        : Health * 100 >= MaxHealth * 60 ? "healthy" : Health * 100 >= MaxHealth * 30 ? "hurt" : "critical";
}

// ── state ───────────────────────────────────────────────────────────────────

/// <summary>
/// A pet as the simulation holds it. Its body is its own - an animal, not an NPC and not a spawner's creature - moved by this system
/// through the one movement function. Only a bite in progress and a taming offer are not saved, as with creatures and companions.
/// </summary>
internal sealed record PetState(EntityId Id, PetSpecies Species, string Name, PetCondition Condition, PetOrder Order, Body Body,
    int Health, int Bond, ImmutableArray<PetSkill> Skills)
{
    /// <summary>The spawn slot it was tamed out of, so the wild animal does not stand there again on the next load.</summary>
    public string? TamedFromKey { get; init; }

    /// <summary>The place its last ground order named; null for the orders that name none.</summary>
    public Body? Wait { get; init; }

    public string? TargetKey { get; init; }
    public long NextAttackTick { get; init; }
    public long NextStruckTick { get; init; }
    public long LastFedTick { get; init; }
    public long WoundedTick { get; init; }
    public long RecoversAtTick { get; init; }
    public long AdoptedTick { get; init; }
    public int StuckTicks { get; init; }
    public int Level { get; init; }

    /// <summary>In its mouth: the blow it is in the middle of, transient as a creature's is.</summary>
    public ActionState Action { get; init; } = ActionState.Idle;

    /// <summary>The tick its last order was given: a scout's clock, and a lure's.</summary>
    public long OrderTick { get; init; }

    public bool Up => Condition == PetCondition.Up;
    public string Key => Id.Value;
}

/// <summary>An offer of food under way: which creature, what it was offered, the tick it would be taken at, and its health then.</summary>
internal sealed record TameOffer(EntityId Creature, string Food, long EndsTick, int Health, long Tick);

/// <summary>
/// An offer under way as the HUD shows it: what was offered, when the hand comes out of the pack, and the odds the roll will use - the
/// number is fixed when the offer begins, so holding on for longer than the channel is never a different roll.
/// </summary>
public sealed record TameProgress(EntityId Creature, string Food, long EndsTick, int ChancePercent, long StartedTick);

/// <summary>
/// Owns: <see cref="StateSlice.Pets"/> - the animals that travel with the character. What it does (spec 43, owner rulings): it takes a
/// wild animal with the right food and a calm approach, or a pup from a stall; it keeps each pet's own skills, which level with use; it
/// obeys the tactical orders, four of which name a place on the ground; it fights beside the character and is struck back for it; it takes
/// a slot in the party, which companions share (<c>config.party</c>); and at no health it goes home badly wounded and is collected again
/// rather than dying. It never leaves the world by death. While the character is away (WP11) a wounded pet mends and a young one grows.
/// </summary>
internal sealed partial class PetSystem : IAbstractAdvance
{
    private const double SampleScale = 18446744073709551615.0;

    /// <summary>How close the character must be to offer food: a hand's reach and a step, no further than that.</summary>
    private const long OfferReachMm = 4_000;

    /// <summary>Where a following pet settles, and how far behind it starts to run.</summary>
    private const long FollowNearMm = 2_500;
    private const long FollowFarMm = 4_000;

    /// <summary>While it waits or guards: how near a creature must come before it is met.</summary>
    private const long GuardRadiusMm = 8_000;

    /// <summary>How far a pulling pet will leave the character to take one creature.</summary>
    private const long PullRadiusMm = 25_000;

    /// <summary>How far ahead a scouting pet goes, and how long it looks before it turns back.</summary>
    private const long ScoutAheadMm = 20_000;
    private const long ScoutTicks = 120;

    /// <summary>How long a lure keeps the pet at the place it was sent before it may try again.</summary>
    private const int LureRecoveryTicks = 40;

    /// <summary>Arrived: within this much of where it was sent.</summary>
    private const long ArrivedMm = 1_500;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly ImmutableArray<PetRecord> _saved;
    private readonly Dictionary<string, TameOffer> _offers = new(StringComparer.Ordinal);

    private RuntimeState State => _context.State;

    public PetSystem(SystemContext context, SliceOwner owner, EntityId player, ImmutableArray<PetRecord> saved)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _saved = saved;
    }

    private PetSetup Setup => _context.Setup.Pets ?? PetSetup.None;

    /// <summary>The pet rules the world runs under (<c>config.pets</c>): a pack with none has the empty ones.</summary>
    public PetRules Rules => Setup.Rules;
    private int TickMs => _context.Setup.TickMilliseconds;
    private CombatConstants C => _context.Setup.Combat.Constants;
    private AwarenessRules Senses => _context.Setup.Combat.Awareness;

    /// <summary>How many companions travel with the character: the other half of the one party cap (owner ruling 2).</summary>
    public Func<int>? Companions { get; init; }

    /// <summary>The creature the character struck last, within the focus window: what "attack my target" takes (M6's tactic, shared).</summary>
    public Func<string?>? PlayerFocus { get; init; }

    /// <summary>The character's own Handling, which taming trains and the odds are counted from.</summary>
    private int Handling => ProgressionEngine.SkillLevel(State.Progression, "skill.handling");

    // ── the world starting, and the character coming back ───────────────────

    /// <summary>World start: every pet the save holds, at the body it stood on, with the skills it had learnt.</summary>
    public void Populate()
    {
        var built = ImmutableSortedDictionary.CreateBuilder<string, PetState>(StringComparer.Ordinal);
        foreach (var record in _saved)
        {
            if (Setup.Of(record.CreatureId) is not { } species)
                continue;
            if (_context.Setup.Combat.Creatures.GetValueOrDefault(record.CreatureId) is not { } definition)
                continue;
            var space = SpaceOf(record.Space);
            var body = new Body(record.XMm, _context.Setup.SpaceFor(space).Terrain.HeightAtMm(record.XMm, record.ZMm), record.ZMm, record.FacingMdeg);
            built[record.Id.Value] = new PetState(record.Id, species, record.Name, record.Condition, record.Order, body,
                Math.Clamp(record.Health, 0, definition.MaxHealth), record.Bond, record.Skills)
            {
                TamedFromKey = record.TamedFromKey,
                Wait = record.WaitXMm == 0 && record.WaitZMm == 0 ? null : new Body(record.WaitXMm, body.YMm, record.WaitZMm, record.FacingMdeg),
                WoundedTick = record.WoundedTick,
                RecoversAtTick = record.RecoversAtTick,
                LastFedTick = record.LastFedTick,
                AdoptedTick = record.AdoptedTick,
                Level = record.Level > 0 ? record.Level : definition.Level,
                OrderTick = record.WoundedTick,
            };
        }
        State.SetPets(_owner, built.ToImmutable());
    }

    /// <summary>What is saved of the party's animals: each one's body, condition, order, health, bond, skills and home clock.</summary>
    public ImmutableArray<PetRecord> Records() => State.Pets.Values
        .OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => new PetRecord(p.Id, p.Species.CreatureId, p.Name, p.Species.Role, p.Order, p.Condition,
            p.Body.XMm, p.Body.ZMm, p.Body.FacingMdeg, p.Health, p.Bond, p.Skills)
        {
            Space = State.Space.ToString(),
            TamedFromKey = p.TamedFromKey,
            WaitXMm = p.Wait?.XMm ?? 0,
            WaitZMm = p.Wait?.ZMm ?? 0,
            WoundedTick = p.WoundedTick,
            RecoversAtTick = p.RecoversAtTick,
            LastFedTick = p.LastFedTick,
            AdoptedTick = p.AdoptedTick,
            Level = p.Level,
        }).ToImmutableArray();

    /// <summary>Whether a spawn slot's animal was taken as a pet: it does not stand in the wild again.</summary>
    public bool TamedAway(string spawnKey) => State.Pets.Values.Any(p => p.TamedFromKey == spawnKey);

    /// <summary>The offer of food under way on a creature, or null when no hand is held out to it.</summary>
    public TameProgress? Offer(EntityId creature)
    {
        foreach (var offer in _offers.Values)
        {
            if (offer.Creature != creature || !State.Creatures.TryGetValue(KeyOf(offer.Creature), out var target))
                continue;
            var species = Setup.Of(target.Definition.Id);
            int chance = species is null ? 0 : Chance(species, offer.Food, target);
            return new TameProgress(offer.Creature, offer.Food, offer.EndsTick, chance, offer.Tick);
        }
        return null;
    }

    private string KeyOf(EntityId creature) => State.Creatures.Values.FirstOrDefault(c => c.Id == creature)?.Key ?? "";

    // ── the party cap, and what a party does to an encounter ────────────────

    /// <summary>The cap the pets share with the companions (<c>config.party</c>'s max_members).</summary>
    private int Cap() => _context.Setup.Social.Party?.MaxMembers ?? int.MaxValue;

    private int Followers() => Companions?.Invoke() ?? 0;

    private string? Room()
    {
        int pets = State.Pets.Values.Count(p => p.Up);
        return PetParty.Refusal(Followers(), pets, Cap());
    }

    /// <summary>
    /// What the party is worth to an encounter (owner ruling 5): one point per pet on its feet, and a quarter for every level it has past
    /// the first. The character alone is zero, and meets the fight exactly as it was authored.
    /// </summary>
    public double StrengthPoints()
    {
        var up = State.Pets.Values.Where(p => p.Up).ToList();
        return Encounters.Points(up.Count + Followers(), up.Sum(p => Math.Max(0, p.Level - 1)));
    }

    /// <summary>The scale a spawned creature stands at, from the party as it is: content's percent per point, and the levels that buys.</summary>
    public EncounterScale Encounter => Encounters.Of(StrengthPoints(), Rules.Encounter);

    // ── commands ────────────────────────────────────────────────────────────

    public string? Handle(OrderPetCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Pets.TryGetValue(command.Pet.Value, out var pet))
            return "no such pet";
        if (!pet.Up)
            return pet.RecoversAtTick > tick ? $"{pet.Name} is at home, mending: not before {Home(pet)} " : $"{pet.Name} is home, and wants collecting";
        if (Names(command.Order) && command.XMm == 0 && command.ZMm == 0)
            return "that order names a place: mark one on the ground";
        if (Obedient(pet) is { } refused)
            return refused;
        var body = State.Body;
        var wait = Names(command.Order) ? new Body(command.XMm, body.YMm, command.ZMm, body.FacingMdeg) : pet.Wait;
        State.SetPet(_owner, pet with
        {
            Order = command.Order,
            Wait = wait,
            OrderTick = tick,
            TargetKey = command.Order == PetOrder.AttackTarget ? PlayerFocus?.Invoke() : null,
            NextAttackTick = 0,
            NextStruckTick = 0,
            StuckTicks = 0,
        });
        _context.Events.Publish(new PetOrdered(pet.Id, command.Order, tick));
        return null;
    }

    private static bool Names(PetOrder order) => order is PetOrder.WaitHere or PetOrder.Guard or PetOrder.Scout or PetOrder.LureToHere;

    /// <summary>
    /// Whether it does as it is told now: a tamed animal that has not known the character long may refuse a hard order (owner ruling 1's
    /// "starts wary, obeys less"). The bond it has built is what decides, on the world's own channel so a replay rolls the same.
    /// </summary>
    private string? Obedient(PetState pet)
    {
        if (pet.Bond >= PetBond.Sure || !Hard(pet.Order))
            return null;
        var channel = RngChannel.Open(State.World.WorldSeed, _context.CellOf(pet.Body), "pets", $"obey:{pet.Key}@{State.WorldTick}");
        if (channel.UInt64(0) / SampleScale * 100 < PetBond.Obey(pet.Bond, pet.Order))
            return null;
        _context.Events.Publish(new PetRefused(pet.Id, pet.Order, State.WorldTick));
        return $"{pet.Name} is not sure enough of you yet for that";
    }

    private static bool Hard(PetOrder order) => order is PetOrder.AttackTarget or PetOrder.Pull or PetOrder.WaitHere;

    public string? Handle(FeedPetCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!State.Pets.TryGetValue(command.Pet.Value, out var pet))
            return "no such pet";
        if (!pet.Up)
            return $"{pet.Name} is not here to feed";
        // It eats from the hand: at the character's side, the same reach an offer needs. A pet that has wandered or been left on guard is called
        // back first.
        if (Distance(State.Body, pet.Body) > OfferReachMm)
            return $"{pet.Name} is too far to feed: call it to you first";
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return "you are not carrying that";
        if (!pet.Species.Eats(entry.DefId))
            return $"{pet.Name} will not eat that";
        int bond = Math.Min(PetBond.Max, pet.Bond + 3);
        // The food goes down the animal, not into the pack: an item that is eaten is eaten (the same rule as an offer's).
        _context.Dispatch(new ConsumeItem(entry.DefId, 1));
        State.SetPet(_owner, pet with { Bond = bond, LastFedTick = tick });
        _context.Events.Publish(new PetFed(pet.Id, bond, tick));
        return null;
    }

    /// <summary>
    /// Offer the food and begin the channel (spec 43's approach): the animal must be tameable, no stronger than the character, not already
    /// roused, and close; the food must be one it eats. The odds are fixed when the offer begins, so the number the roll will use is the
    /// number the HUD can show.
    /// </summary>
    public string? Handle(TameCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Creatures.ContainsKey(command.Creature.Value) && Find(command.Creature) is not { } found)
            return "no such animal";
        var creature = Find(command.Creature)!;
        if (!creature.Alive)
            return "it is dead";
        if (Setup.Of(creature.Definition.Id) is not { } species)
            return "that is not an animal you can tame";
        int level = State.Progression.Level;
        // Two levels above the character is the hardest offer the game makes, and the odds already say what it costs (config.pets
        // tame.over_level: fifteen off for each level over). Beyond that it is a fight, not a taming.
        if (creature.Definition.Level > level + 2)
            return $"it is beyond you: a level {creature.Definition.Level} animal against level {level}";
        if (!species.Eats(command.FoodItem))
            return $"it will not take {command.FoodItem}";
        if (creature.Mind == CreatureMind.Engaged)
            return "it has seen you: creep up on it, or come back when it has forgotten";
        if (Distance(State.Body, creature.Body) > OfferReachMm)
            return "too far to offer it anything";
        if (Room() is { } full)
            return full;
        if (!State.Inventory.Any(e => e.DefId == command.FoodItem))
            return $"you are not carrying any {command.FoodItem}";
        int chance = Chance(species, command.FoodItem, creature);
        _offers[creature.Key] = new TameOffer(creature.Id, command.FoodItem, tick + Rules.Tame.ChannelTicks, creature.Health, tick);
        // The meat is laid down when it is offered, not when it is taken: a broken offer costs the food.
        _context.Dispatch(new ConsumeItem(command.FoodItem, 1));
        _context.Dispatch(new PracticeSkill(new SkillPractice("skill.handling", Math.Max(1, creature.Definition.Level), PracticeOutcome.Success, tick)));
        _context.Events.Publish(new TameAttempted(creature.Id, tick + Rules.Tame.ChannelTicks, chance, tick));
        return null;
    }

    /// <summary>Take the animal that is already the character's out of the pack (the pup bought at the waystation).</summary>
    public string? Handle(AdoptPetCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return "you are not carrying that";
        if (PetOf(entry.DefId) is not { } creatureId)
            return "that is not an animal";
        if (Setup.Of(creatureId) is not { } species)
            return "that animal cannot be kept";
        if (Room() is { } full)
            return full;
        var pet = NewPet(species, creatureId, tick);
        State.SetPet(_owner, pet);
        _context.Dispatch(new ConsumeItem(entry.DefId, 1));
        _context.Events.Publish(new PetAdopted(pet.Id, creatureId, pet.Name, tick));
        return null;
    }

    /// <summary>The creature an item is a live animal of (<c>pet_creature</c> on the item), or null.</summary>
    private string? PetOf(string itemDefId) => _context.Setup.Items.Catalog.Find(itemDefId)?.PetCreature;

    // ── one tick of a pet's life ────────────────────────────────────────────

    public void Tick(long tick)
    {
        foreach (string key in State.Pets.Keys.ToList())
        {
            var pet = State.Pets[key];
            if (!pet.Up)
            {
                if (pet.RecoversAtTick > 0 && PetWounds.Home(tick, pet.RecoversAtTick))
                {
                    State.SetPet(_owner, pet with { Condition = PetCondition.Up, Health = MaxHealth(pet), RecoversAtTick = 0 });
                    _context.Events.Publish(new PetRecovered(pet.Id, tick));
                }
                continue;
            }
            State.SetPet(_owner, Live(pet, tick));
        }
        Offers(tick);
        Midnight(tick);
    }

    private PetState Live(PetState pet, long tick)
    {
        var (phase, _) = pet.Action.PhaseAt(tick, C);
        if (pet.Action.Kind != ActionKind.Idle && phase == CombatPhase.Idle)
            pet = pet with { Action = ActionState.Idle };
        switch (phase)
        {
            case CombatPhase.Windup:
                return pet;
            case CombatPhase.Active:
                return Bite(pet, tick);
            case CombatPhase.Recovery:
                return pet;
        }
        // The creature it bit may turn on it: the one it is fighting strikes back on its own blow's timing.
        pet = Struck(pet, tick);
        if (!pet.Up)
            return pet;
        if (TierOf(pet.Body) != SimulationTier.A)
            return pet with { StuckTicks = 0 };
        return pet.Order switch
        {
            PetOrder.Follow => Follow(pet, tick),
            PetOrder.WaitHere => Hold(pet, GuardRadiusMm / 2, tick),
            PetOrder.Guard => Hold(pet, GuardRadiusMm, tick),
            PetOrder.Scout => Scout(pet, tick),
            PetOrder.Pull => Pull(pet, tick),
            PetOrder.AttackTarget => Chase(pet, tick),
            PetOrder.LureToHere => Lure(pet, tick),
            _ => pet,
        };
    }

    private PetState Follow(PetState pet, long tick)
    {
        if (Hunt(pet, 6_000) is { } prey)
            return Fight(pet, prey, tick);
        double distance = Distance(pet.Body, State.Body);
        if (distance <= FollowNearMm)
            return pet with { StuckTicks = 0, TargetKey = null };
        return Walk(pet, State.Body.XMm, State.Body.ZMm, distance > FollowFarMm * 3 ? Gait.Sprint : distance > FollowFarMm ? Gait.Run : Gait.Walk);
    }

    /// <summary>Waiting or guarding: go to the post, then hold it - meeting whatever comes inside the guard's reach, and no further.</summary>
    private PetState Hold(PetState pet, long radius, long tick)
    {
        var post = pet.Wait ?? State.Body;
        double away = Distance(pet.Body, post);
        if (away > ArrivedMm)
            return Walk(pet, post.XMm, post.ZMm, away > radius * 3 ? Gait.Run : Gait.Walk);
        if (Hunt(pet, radius) is { } prey)
            return Fight(pet, prey, tick);
        return pet with { StuckTicks = 0, TargetKey = null };
    }

    /// <summary>Look ahead of the character for a while, then come back: what a scout brings is what it saw, and the HUD says so.</summary>
    private PetState Scout(PetState pet, long tick)
    {
        if (tick - pet.OrderTick > ScoutTicks)
            return pet with { Order = PetOrder.Follow, OrderTick = tick, StuckTicks = 0 };
        var body = State.Body;
        double heading = body.FacingMdeg / 1000.0 * Math.PI / 180;
        long aheadX = body.XMm + (long)(Math.Sin(heading) * ScoutAheadMm);
        long aheadZ = body.ZMm + (long)(Math.Cos(heading) * ScoutAheadMm);
        if (Distance(pet.Body, new Body(aheadX, body.YMm, aheadZ, body.FacingMdeg)) > ArrivedMm)
            return Walk(pet, aheadX, aheadZ, Gait.Run);
        return pet with { StuckTicks = 0 };
    }

    /// <summary>
    /// Take one creature off its pack: close on the nearest one already fighting the party, bite it once, and come back - what follows is
    /// what was pulled, and only the one that was bitten knows anything to follow.
    /// </summary>
    private PetState Pull(PetState pet, long tick)
    {
        if (pet.TargetKey is { } key && State.Creatures.TryGetValue(key, out var marked) && marked.Alive && marked.Mind == CreatureMind.Engaged)
            return Fight(pet, marked, tick);
        var prey = Hunt(pet, PullRadiusMm);
        if (prey is null)
            return pet with { Order = PetOrder.Follow, TargetKey = null };
        return Fight(pet, prey with { }, tick) with { TargetKey = prey.Key };
    }

    private PetState Chase(PetState pet, long tick)
    {
        if ((pet.TargetKey ?? PlayerFocus?.Invoke()) is { } key && State.Creatures.TryGetValue(key, out var target) && target.Alive
            && target.Site.Space == State.Space && Distance(pet.Body, target.Body) <= C.LeashMm)
            return Fight(pet, target, tick);
        return pet.Order == PetOrder.AttackTarget ? pet with { Order = PetOrder.Follow, TargetKey = null } : pet;
    }

    /// <summary>
    /// The lure (the control skill): go to the place the handler marked, then draw one creature there - the nearest engaged one inside the
    /// rules' radius, led no further than the rules allow, and refused by an enemy that has been led before. A lure is a working, not a
    /// blow: it is checked once, when the pet has reached the point.
    /// </summary>
    private PetState Lure(PetState pet, long tick)
    {
        if (pet.Wait is not { } spot)
            return pet with { Order = PetOrder.Follow };
        if (Distance(pet.Body, spot) > ArrivedMm)
            return Walk(pet, spot.XMm, spot.ZMm, Gait.Run);
        if (tick - pet.OrderTick < LureRecoveryTicks)
            return pet;
        var prey = State.Creatures.Values
            .Where(x => x.Alive && x.Mind == CreatureMind.Engaged && x.Site.Space == State.Space && TierOf(x.Body) == SimulationTier.A
                        && Distance(x.Body, pet.Body) <= Rules.Lure.RadiusMm)
            .OrderBy(x => Distance(x.Body, pet.Body)).ThenBy(x => x.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        var settled = pet with { Order = PetOrder.Follow, OrderTick = tick, TargetKey = null };
        if (prey is null)
            return settled;
        // An enemy that has been led off once knows the trick: it is not drawn again until it is reset, and it says so rather than the working
        // quietly doing nothing (owner ruling 5, "enemies that ... ignore lures they've seen").
        if (prey.LureLearned)
        {
            _context.Events.Publish(new LureRefused(pet.Id, prey.Id, tick));
            return settled;
        }
        var channel = RngChannel.Open(State.World.WorldSeed, _context.CellOf(prey.Body), "pets", $"lure:{pet.Key}>{prey.Key}@{tick}");
        if (channel.UInt64(0) / SampleScale * 100 >= PetLure.Chance(Rules.Lure, PetSkills.Of(pet.Skills, PetSkillKind.Lure), prey.Definition.Level))
        {
            _context.Events.Publish(new LureRefused(pet.Id, prey.Id, tick));
            return settled;
        }
        // Led towards the point, and no further than the rules' travel from where it stands. It learns the trick as it follows (DrawCreature
        // sets that on the creature itself), so a second lure on the same animal is refused until it is reset.
        double toPoint = Math.Max(1, Distance(prey.Body, spot));
        int travel = PetLure.Travel(Rules.Lure, prey.Definition.Level);
        double step = Math.Min(travel, toPoint);
        long x = prey.Body.XMm + (long)((spot.XMm - prey.Body.XMm) / toPoint * step);
        long z = prey.Body.ZMm + (long)((spot.ZMm - prey.Body.ZMm) / toPoint * step);
        _context.Dispatch(new DrawCreature(prey.Id, x, z, Rules.Lure.ImmunityTicks));
        _context.Events.Publish(new PetLured(pet.Id, prey.Id, x, z, tick));
        return Practise(settled, PetSkillKind.Lure, prey.Id, tick);
    }

    /// <summary>The nearest living creature already fighting the party, inside <paramref name="radius"/>.</summary>
    private CreatureState? Hunt(PetState pet, long radius) => State.Creatures.Values
        .Where(x => x.Alive && x.Site.Space == State.Space && TierOf(x.Body) == SimulationTier.A && x.Mind == CreatureMind.Engaged
                    && Distance(pet.Body, x.Body) <= radius && !Walled(pet.Body, x.Body))
        .OrderBy(x => Distance(pet.Body, x.Body)).ThenBy(x => x.Key, StringComparer.Ordinal)
        .FirstOrDefault();

    private AttackProfile Bite(PetState pet) => _context.Setup.Combat.Creatures[pet.Species.CreatureId].Attack;

    private PetState Fight(PetState pet, CreatureState prey, long tick)
    {
        pet = pet with { TargetKey = prey.Key };
        var attack = Bite(pet);
        double reach = attack.ReachMm + prey.Definition.RadiusMm - 100;
        if (Distance(pet.Body, prey.Body) > reach || Walled(pet.Body, prey.Body))
            return Walk(pet, prey.Body.XMm, prey.Body.ZMm, Gait.Sprint);
        int toward = CombatRules.FacingTowards(pet.Body.XMm, pet.Body.ZMm, prey.Body.XMm, prey.Body.ZMm);
        var turned = Turn(pet.Body, toward);
        pet = pet with { Body = turned };
        if (Math.Abs(Delta(turned.FacingMdeg, toward)) > 30_000 || tick < pet.NextAttackTick)
            return pet;
        _context.Events.Publish(new AttackStarted(pet.Id, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
        return pet with { Action = ActionState.Begin(ActionKind.Attack, tick, attack) };
    }

    /// <summary>The bite's active window: the creature it is fighting, once, resolved as any blow is and handed to the creature's owner.</summary>
    private PetState Bite(PetState pet, long tick)
    {
        var action = pet.Action;
        var attack = action.Attack ?? Bite(pet);
        if (pet.TargetKey is not { } key || !State.Creatures.TryGetValue(key, out var prey) || !prey.Alive || action.Struck.Contains(prey.Id))
            return pet;
        if (!CombatRules.InFront(pet.Body.XMm, pet.Body.ZMm, pet.Body.FacingMdeg, prey.Body.XMm, prey.Body.ZMm,
                attack.ReachMm + prey.Definition.RadiusMm, C.MeleeArcMdeg) || Walled(pet.Body, prey.Body))
            return pet with { Action = action with { Struck = action.Struck.Add(prey.Id) }, NextAttackTick = tick + attack.TotalTicks };
        var defense = new DefenseProfile(prey.ScaledMaxHealth, prey.Definition.Armor, prey.Definition.Resistances, Blocking: false, Dodging: false);
        var strike = new Strike(attack, PetSkills.DamageMultiplier(PetSkills.Of(pet.Skills, PetSkillKind.Bite)), C.BaseCritPercent, 1.0);
        var result = CombatRules.Resolve(strike, defense, C, Random(pet.Key, prey.Key, prey.Body, tick));
        _context.Dispatch(new WoundCreature(prey.Id, result, attack.Source)
        {
            Attacker = pet.Id,
            AttackerDefId = pet.Species.CreatureId,
            FromXMm = pet.Body.XMm,
            FromZMm = pet.Body.ZMm,
        });
        pet = Practise(pet with
            {
                Action = action with { Struck = action.Struck.Add(prey.Id) },
                NextAttackTick = tick + attack.TotalTicks,
                // What it bit turns on it: the creature's own blow comes back at the pet on the creature's own timing.
                NextStruckTick = tick + attack.TotalTicks,
            },
            PetSkillKind.Bite, prey.Id, tick);
        // One bite and it comes back: a pull that lands turns the creature on the party, and the pet is done pulling.
        return pet.Order == PetOrder.Pull ? pet with { Order = PetOrder.Follow, OrderTick = tick } : pet;
    }

    /// <summary>The creature it bit strikes back: a pet is wounded in real fights, and that is how it ends up home rather than dead.</summary>
    private PetState Struck(PetState pet, long tick)
    {
        if (pet.TargetKey is not { } key || !State.Creatures.TryGetValue(key, out var prey) || !prey.Alive || prey.Mind != CreatureMind.Engaged)
            return pet;
        if (pet.NextStruckTick == 0 || tick < pet.NextStruckTick)
            return pet;
        var attack = prey.Definition.Attack;
        if (Distance(pet.Body, prey.Body) > attack.ReachMm + prey.Definition.RadiusMm || Walled(pet.Body, prey.Body))
            return pet with { NextStruckTick = tick + C.OutOfCombatTicks };
        var defense = Defense(pet);
        var result = CombatRules.Resolve(new Strike(attack, prey.ScaleDamage, C.BaseCritPercent, 1.0), defense, C, Random(prey.Key, pet.Key, pet.Body, tick));
        int health = Math.Max(0, pet.Health - result.Final);
        _context.Events.Publish(new PetStruck(pet.Id, prey.Id, result.Final, health, tick));
        pet = pet with { Health = health, NextStruckTick = tick + attack.TotalTicks };
        return health > 0 ? pet : Wound(pet, tick);
    }

    /// <summary>
    /// At no health, home (owner ruling 1): the record stays, the condition changes, and it is at the place its species calls home until
    /// content's days are up. Nothing here can kill a pet.
    /// </summary>
    private PetState Wound(PetState pet, long tick)
    {
        var home = HomeBody(pet);
        int days = PetWounds.Days(Rules.Retire, Random(pet.Key, "wound", home, tick));
        long until = PetWounds.RecoversAt(tick, days, _context.Setup.Clock.TicksPerDay);
        var wounded = pet with
        {
            Condition = PetCondition.Wounded,
            Health = 0,
            Body = home,
            WoundedTick = tick,
            RecoversAtTick = until,
            Action = ActionState.Idle,
            TargetKey = null,
            NextAttackTick = 0,
            NextStruckTick = 0,
            Order = PetOrder.Follow,
        };
        State.SetPet(_owner, wounded);
        _context.Events.Publish(new PetWoundedHome(pet.Id, until, tick));
        return wounded;
    }

    private string Home(PetState pet) =>
        $"{(pet.RecoversAtTick - State.WorldTick) / (double)Math.Max(1, _context.Setup.Clock.TicksPerDay):0.#} days";

    private Body HomeBody(PetState pet)
    {
        foreach (var (layout, site) in _context.Setup.Sites(l => l.Locations))
        {
            if (site.Id != pet.Species.HomeLocation)
                continue;
            return new Body(site.XMm, _context.Setup.SpaceFor(layout.SpaceId).Terrain.HeightAtMm(site.XMm, site.ZMm), site.ZMm, 180_000);
        }
        return State.Body;
    }

    // ── the offer, and what it takes ────────────────────────────────────────

    /// <summary>The odds of one offer: the species' taste, the creature's health and notice, and the character's Handling.</summary>
    private int Chance(PetSpecies species, string food, CreatureState creature) =>
        PetTaming.Chance(Rules.Tame, Handling, species.Quality(food), creature.Definition.Level, State.Progression.Level, creature.Health,
            creature.Definition.MaxHealth, creature.Awareness < Senses.Suspicious, species.Temperament);

    /// <summary>
    /// Each offer under way: broken by the animal noticing the character or being hurt, or by the character stepping away; otherwise taken
    /// when its channel runs out, on the odds fixed when it began.
    /// </summary>
    private void Offers(long tick)
    {
        foreach (var (key, offer) in _offers.ToList())
        {
            if (!State.Creatures.TryGetValue(key, out var creature) || !creature.Alive)
            {
                _offers.Remove(key);
                _context.Events.Publish(new TameBroken(offer.Creature, "it is gone", tick));
                continue;
            }
            if (creature.Health < offer.Health)
            {
                _offers.Remove(key);
                _context.Events.Publish(new TameBroken(offer.Creature, "it was hurt", tick));
                continue;
            }
            if (creature.Mind == CreatureMind.Engaged)
            {
                _offers.Remove(key);
                _context.Events.Publish(new TameBroken(offer.Creature, "it saw you", tick));
                continue;
            }
            if (Distance(State.Body, creature.Body) > OfferReachMm * 3 / 2)
            {
                _offers.Remove(key);
                _context.Events.Publish(new TameBroken(offer.Creature, "you drew away", tick));
                continue;
            }
            if (tick < offer.EndsTick)
                continue;
            _offers.Remove(key);
            var species = Setup.Of(creature.Definition.Id)!;
            int chance = Chance(species, offer.Food, creature);
            var roll = RngChannel.Open(State.World.WorldSeed, _context.CellOf(creature.Body), "pets", $"tame:{key}@{tick}");
            if (roll.UInt64(0) / SampleScale * 100 >= chance)
            {
                _context.Events.Publish(new TameFailed(creature.Id, chance, tick));
                _context.Dispatch(new RouseCreature(creature.Id));
                continue;
            }
            var pet = NewPet(species, creature.Definition.Id, tick) with
            {
                Body = creature.Body,
                TamedFromKey = creature.Key,
                Wait = creature.Body,
            };
            State.SetPet(_owner, pet);
            _context.Dispatch(new TakeCreature(creature.Id));
            _context.Events.Publish(new PetTamed(pet.Id, creature.Definition.Id, pet.Name, tick));
        }
    }

    private PetState NewPet(PetSpecies species, string creatureId, long tick)
    {
        var definition = _context.Setup.Combat.Creatures[creatureId];
        var body = State.Body;
        return new PetState(EntityId.NewId(EntityKind.Summon), species, species.Name, PetCondition.Up, PetOrder.Follow,
            new Body(body.XMm, body.YMm, body.ZMm, body.FacingMdeg), definition.MaxHealth, PetBond.Tamed, PetSkills.Start(species.Skills))
        {
            Wait = body,
            AdoptedTick = tick,
            Level = definition.Level,
        };
    }

    // ── while the character is away ─────────────────────────────────────────

    /// <summary>
    /// The world going on while the character is away (WP11): a wounded pet mends and is home when its days are up, and a young one grows
    /// into the grown animal. Closed-form - both are functions of the tick alone, so an absence split anywhere ends the same.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        foreach (string key in State.Pets.Keys.ToList())
            State.SetPet(_owner, Aged(State.Pets[key], toTick));
    }

    /// <summary>A pet as the tick alone makes it: mended at home, and grown if its days are up.</summary>
    private PetState Aged(PetState pet, long tick)
    {
        if (pet.Species.Young && pet.Species.GrowsInto is { } grown && pet.AdoptedTick > 0
            && tick >= pet.AdoptedTick + pet.Species.GrowDays * _context.Setup.Clock.TicksPerDay)
            pet = Grow(pet, grown, tick);
        if (!pet.Up && pet.RecoversAtTick > 0 && PetWounds.Home(tick, pet.RecoversAtTick))
            pet = pet with { Condition = PetCondition.Up, Health = MaxHealth(pet), RecoversAtTick = 0 };
        return pet;
    }

    /// <summary>At each midnight: a pet that has been kept and fed grows surer of the character, and a pup becomes the grown animal.</summary>
    private void Midnight(long tick)
    {
        if (_context.Setup.Clock.DaysCrossed(tick - 1, tick) <= 0)
            return;
        foreach (string key in State.Pets.Keys.ToList())
        {
            var pet = Aged(State.Pets[key], tick);
            if (pet.Up && pet.Bond < PetBond.Max)
                pet = pet with { Bond = pet.Bond + 1 };
            State.SetPet(_owner, pet);
        }
    }

    private PetState Grow(PetState pet, string grown, long tick)
    {
        if (Setup.Of(grown) is not { } species || _context.Setup.Combat.Creatures.GetValueOrDefault(grown) is not { } definition)
            return pet;
        var next = pet with { Species = species, Name = species.Name, Health = definition.MaxHealth, Level = definition.Level, AdoptedTick = 0 };
        State.SetPet(_owner, next);
        _context.Events.Publish(new PetGrown(pet.Id, grown, species.Name, tick));
        return next;
    }

    // ── the pet's own skills ────────────────────────────────────────────────

    private PetState Practise(PetState pet, PetSkillKind kind, EntityId creature, long tick)
    {
        if (PetSkills.Of(pet.Skills, kind) is not { } skill)
            return pet;
        var practice = PetSkills.Practice(skill, Rules.Skills);
        _context.Events.Publish(new PetSkillUsed(pet.Id, kind, creature, tick));
        if (practice.Levelled)
            _context.Events.Publish(new PetSkillLevelled(pet.Id, kind, practice.Skill.Level, tick));
        return pet with { Skills = pet.Skills.Select(s => s.Kind == kind ? practice.Skill : s).ToImmutableArray() };
    }

    private DefenseProfile Defense(PetState pet)
    {
        var definition = _context.Setup.Combat.Creatures[pet.Species.CreatureId];
        return new DefenseProfile(definition.MaxHealth, definition.Armor, definition.Resistances, Blocking: false, Dodging: false);
    }

    private int MaxHealth(PetState pet) => _context.Setup.Combat.Creatures[pet.Species.CreatureId].MaxHealth;

    // ── moving, turning, looking ────────────────────────────────────────────

    private PetState Walk(PetState pet, long toXMm, long toZMm, Gait gait)
    {
        var from = pet.Body;
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return pet;
        var intent = new MoveIntent((int)Math.Round(dx / length * MoveIntent.FullDeflection), (int)Math.Round(dz / length * MoveIntent.FullDeflection),
            gait, CombatRules.FacingTowards(from.XMm, from.ZMm, toXMm, toZMm));
        var moved = Kinematics.Step(from, intent, _context.Setup.Movement, _context.Setup.SpaceFor(State.Space), Obstacles(), TickMs);
        double expected = _context.Setup.Movement.SpeedMmPerSecond(gait) * TickMs / 1000.0;
        bool headway = Distance(moved, from) >= expected * 0.3;
        return pet with { Body = moved, StuckTicks = headway ? 0 : pet.StuckTicks + 1 };
    }

    /// <summary>What a pet's body cannot pass: closed doors, the living creatures, the character and the other party's people.</summary>
    private List<Blocker> Obstacles()
    {
        long radius = _context.Setup.Movement.BodyRadiusMm;
        var obstacles = new List<Blocker>(_context.ClosedDoors());
        obstacles.AddRange(State.Creatures.Values.Where(x => x.Alive && x.Site.Space == State.Space)
            .Select(x => (Blocker)new CircleBlocker(x.Key, x.Body.XMm, x.Body.ZMm, x.Definition.RadiusMm, 0)));
        obstacles.Add(new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, radius, 0));
        obstacles.AddRange(State.Npcs.Values.Where(n => n.Space == State.Space)
            .Select(n => (Blocker)new CircleBlocker(n.Definition.Id, n.Body.XMm, n.Body.ZMm, radius, 0)));
        return obstacles;
    }

    private bool Walled(Body from, Body to) =>
        _context.Setup.SpaceFor(State.Space).Blockers.Concat(_context.ClosedDoors())
            .Where(b => State.Space.IsExterior || b.ClearanceMm < 1800).Any(b => b.Crosses(from.XMm, from.ZMm, to.XMm, to.ZMm));

    /// <summary>The body turned towards a facing by at most 360 degrees a second, as every body turns here.</summary>
    private Body Turn(Body body, int towardMdeg)
    {
        long step = 360_000L * TickMs / 1000;
        int delta = Delta(body.FacingMdeg, towardMdeg);
        if (Math.Abs(delta) <= step)
            return body with { FacingMdeg = towardMdeg };
        long turned = body.FacingMdeg + Math.Sign(delta) * step;
        return body with { FacingMdeg = (int)(((turned % 360_000) + 360_000) % 360_000) };
    }

    private static int Delta(int fromMdeg, int toMdeg)
    {
        int delta = ((toMdeg - fromMdeg) % 360_000 + 360_000) % 360_000;
        return delta > 180_000 ? delta - 360_000 : delta;
    }

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));

    private SpaceId SpaceOf(string? space) => string.IsNullOrEmpty(space) ? State.Space : SpaceId.Parse(space);

    /// <summary>A creature by its instance, as the system's own dictionary keys them by slot.</summary>
    private CreatureState? Find(EntityId id) => State.Creatures.Values.FirstOrDefault(c => c.Id == id);

    /// <summary>The rolls of one blow, keyed by who struck whom on which tick: the same on a replay and after a load.</summary>
    private Func<uint, double> Random(string attacker, string target, Body at, long tick)
    {
        var channel = RngChannel.Open(State.World.WorldSeed, _context.CellOf(at), "pets", $"{attacker}>{target}@{tick}");
        return sample => channel.UInt64(sample) / SampleScale;
    }

    private SimulationTier TierOf(Body body) => State.Tiers.GetValueOrDefault(_context.CellOf(body).ToString(), SimulationTier.D);

    // ── what the HUD and the panel show ─────────────────────────────────────

    /// <summary>Every pet the character has, on its feet or home badly wounded, in a stable order.</summary>
    public ImmutableArray<PetView> Views() => State.Pets.Values
        .OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => new PetView(p.Id, p.Species.CreatureId, p.Name, p.Species.Role, p.Order, p.Condition, p.Health, MaxHealth(p), p.Level,
            p.Bond, p.Body, Doing(p), p.Skills)
        {
            HomeUntilTick = p.Condition == PetCondition.Wounded ? p.RecoversAtTick : null,
        }).ToImmutableArray();

    private static string Doing(PetState p) => p.Condition == PetCondition.Wounded ? "home"
        : p.Action.Kind == ActionKind.Attack ? "fighting"
        : p.Order switch
        {
            PetOrder.Follow => "following",
            PetOrder.WaitHere => "waiting",
            PetOrder.Guard => "guarding",
            PetOrder.Scout => "scouting",
            PetOrder.Pull => "pulling",
            PetOrder.AttackTarget => "on your target",
            PetOrder.LureToHere => "luring",
            _ => "following",
        };
}