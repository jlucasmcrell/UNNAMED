// UNNAMED World - the party's sheets: each member's progression on the character's rules, and the gear they wear in the same ten slots,
// given or loaned (SYSTEMS_COMPLETENESS_PLAN §2.15, WP15b; EXT/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES :20-36, :74-92)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Hand a party member a carried item to wear, within a hand's reach of them: <see cref="Loan"/>, it can be asked back; dismissal leaves both gifts and loans with their holder (§2.15). Gear is player-assigned only in this bar (canon :84-92).
/// </summary>
public sealed record GiveGearCommand(EntityId Actor, string NpcId, EntityId Item, bool Loan) : GameCommand(Actor);

/// <summary>Ask for a loaned piece back from a party member, within a hand's reach of them. A gift is theirs and is not asked back.</summary>
public sealed record TakeBackGearCommand(EntityId Actor, string NpcId, EquipSlot Slot) : GameCommand(Actor);

/// <summary>Move a whole stack between the character's and a nearby companion's pack.</summary>
public sealed record TransferPartyItemCommand(EntityId Actor,string NpcId,EntityId Item,bool ToCompanion) : GameCommand(Actor);
public sealed record EquipPartyPackCommand(EntityId Actor,string NpcId,EntityId Item) : GameCommand(Actor);
public sealed record UnequipPartyGearCommand(EntityId Actor,string NpcId,EquipSlot Slot) : GameCommand(Actor);

/// <summary>Spend one of a party member's unspent attribute points (§2.15: on the character's rules; the player assigns them).</summary>
public sealed record SpendPartyAttributeCommand(EntityId Actor, string NpcId, CharacterAttribute Attribute) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>A party member put on (<see cref="ItemDefId"/>) or gave up (null) what is in a slot; <see cref="Loaned"/> for a loan.</summary>
public sealed record PartyGearChanged(string NpcId, EquipSlot Slot, string? ItemDefId, bool Loaned, long Tick);

/// <summary>A party member gained level XP (WP15b): from a fight they shared.</summary>
public sealed record PartyExperienceGained(string NpcId, long Awarded, int LevelsGained, int Level, long Tick);

/// <summary>A party member's skill gained XP (WP15b): a blow that told, or a day's work.</summary>
public sealed record PartySkillPracticed(string NpcId, string SkillId, long XpGained, int LevelsGained, int Level, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>A piece a party member wears, as the party card shows it.</summary>
public sealed record PartyGearView(EquipSlot Slot, EntityId Item, string DefId, int Quality, bool Loaned);

/// <summary>
/// A party member's sheet, as the Party panel shows it (WP15b): level and XP toward the next, unspent points, skills, health on their feet,
/// what they wear and what else is theirs, how they regard the character in words (no approval bar, §2.15), and whether they go along with
/// what they are asked.
/// </summary>
public sealed record PartySheetView(string NpcId, string Name, int Level, long LevelProgressXp, long ToNextLevelXp, int UnspentPoints,
    ImmutableSortedDictionary<string, SkillState> Skills, int MaxHealth, ImmutableArray<PartyGearView> Gear, ImmutableArray<InventoryEntry> Belongings,
    string Regard, Disposition Disposition)
{
    public ImmutableSortedDictionary<CharacterAttribute,int> Attributes {get;init;}=ImmutableSortedDictionary<CharacterAttribute,int>.Empty;
    public long CarriedGrams {get;init;}
    public long CarryLimitGrams {get;init;}
}

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="PartySheetSystem"/>: a party member used a discipline (a blow that wounded, a day's work).</summary>
internal sealed record PracticePartySkill(string NpcId, SkillPractice Practice) : InternalCommand;

/// <summary>To <see cref="PartySheetSystem"/>: a creature was killed (its <see cref="CreatureKilled"/>); the party may share it.</summary>
internal sealed record ShareKill(CreatureKilled Killed) : InternalCommand;

/// <summary>To <see cref="PartySheetSystem"/>: someone left the party alive; what they were only loaned comes back to the character.</summary>
internal sealed record ReturnLoans(string NpcId) : InternalCommand;

/// <summary>
/// To <see cref="PartySheetSystem"/>: a hireling died (DN9). What was loaned comes back to the character; what was theirs lies where they
/// fell; and their sheet is closed.
/// </summary>
internal sealed record SettleFallen(string NpcId, long XMm, long ZMm) : InternalCommand;

/// <summary>To <see cref="CompanionSystem"/>: a party member's gear or attributes changed; their health, weapon and armour follow.</summary>
internal sealed record RefitMember(string NpcId) : InternalCommand;

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.PartySheets"/> - each past or present party member's progression, gear and belongings (WP15b). A member's
/// progression runs on the character's rules (<see cref="ProgressionEngine"/>, its curve and guards): level XP from fights they share - a kill
/// by the character or by anyone in the party, while they are on their feet and within the leash of it - and skill XP from blows that told
/// and from a hireling's day of work. A sheet is kept from the first time anything on it changes. A level grants attribute points the player spends for them. Gear is what the character hands them,
/// given or loaned; the body it makes is the companion system's (<see cref="RefitMember"/>). A sheet outlives a member's time in the party:
/// someone dismissed keeps it, and has it again when they come back. Nothing of it goes on while the character is away but a hireling's day
/// of work, which the companion system steps; so the advance is empty here, and the chunk-invariance test holds it to that.
/// </summary>
internal sealed class PartySheetSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public PartySheetSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private ProgressionRules Rules => _context.Setup.Progression;

    /// <summary>A kill shared: every party member on their feet within the leash of it gains its XP through their own guards.</summary>
    public void Listen(SimulationEvents events) => events.Listen<CreatureKilled>(e => _context.Dispatch(new ShareKill(e)));

    /// <summary>Nothing of a sheet goes on while the character is away: a hireling's day of work comes through the companion system's advance.</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
    }

    public ImmutableArray<PartySheet> Records() => State.PartySheets.Values.ToImmutableArray();

    // ── joining, leaving, falling ───────────────────────────────────────────

    /// <summary>
    /// A member's sheet: kept once anything on it changed, a fresh one - level 1, the starting package, nothing worn - until then. So a
    /// sheet is made the first time a member gains, wears or spends something, and someone who never did has none to save.
    /// </summary>
    private PartySheet SheetOf(string npcId) =>
        State.PartySheets.GetValueOrDefault(npcId) ?? new PartySheet(npcId, ProgressionEngine.Create(Rules));

    /// <summary>
    /// Someone with a life of their own joins as they are (WP13: an adventurer keeps their level and gear when hired): their sheet is theirs,
    /// and what they wear becomes instances the world knows.
    /// </summary>
    public string? Handle(AdoptSheet command)
    {
        foreach (var item in command.Sheet.Items)
        {
            if (!State.World.Registry.Exists(item.ItemId))
                State.World.Registry.CreateEntity(DefinitionId.Parse(item.DefId), item.ItemId);
        }
        State.SetPartySheet(_owner, command.Sheet);
        return null;
    }

    /// <summary>Someone with a life of their own left with their sheet (WP13): it, and what is on it, goes back into that life.</summary>
    public string? Handle(ReleaseSheet command)
    {
        if (!State.PartySheets.TryGetValue(command.NpcId, out var sheet))
            return null;
        foreach (var item in sheet.Items.Where(i => State.World.Registry.Exists(i.ItemId)))
            State.World.Registry.DestroyEntity(item.ItemId);
        State.RemovePartySheet(_owner, command.NpcId);
        return null;
    }

    public string? Handle(ReturnLoans command, long tick)
    {
        if (!State.PartySheets.TryGetValue(command.NpcId, out var sheet))
            return null;
        foreach (var (slot, gear) in sheet.Gear.Where(g => g.Value.Loaned).ToList())
        {
            sheet = sheet with { Gear = sheet.Gear.Remove(slot) };
            State.SetPartySheet(_owner, sheet);
            _context.Dispatch(new ReceiveStack(gear.Item));
            _context.Events.Publish(new PartyGearChanged(command.NpcId, slot, null, true, tick));
        }
        return null;
    }

    public string? Handle(SettleFallen command, long tick)
    {
        if (!State.PartySheets.TryGetValue(command.NpcId, out var sheet))
            return null;
        State.RemovePartySheet(_owner, command.NpcId);
        foreach (var gear in sheet.Gear.Values)
            _context.Dispatch(new ReceiveStack(gear.Item) { DropAt = gear.Loaned ? null : (command.XMm, command.ZMm) });
        foreach (var item in sheet.Belongings)
            _context.Dispatch(new ReceiveStack(item) { DropAt = (command.XMm, command.ZMm) });
        return null;
    }

    // ── gear ────────────────────────────────────────────────────────────────

    public string? Handle(GiveGearCommand command, long tick)
    {
        if (Reach(command.Actor, command.NpcId) is { } far)
            return far;
        var sheet = SheetOf(command.NpcId);
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return $"{command.Item} is not carried";
        if (_context.Setup.Items.Catalog.Find(entry.DefId) is not { } definition)
            return $"{entry.DefId} is not an item this build knows";
        if (Heritage(command.NpcId, definition) is { } heritage)
            return heritage;
        if (definition.Slot is not { } slot)
            return $"{definition.Id} is not worn";
        if (definition.Weapon is { Ranged: true })
            return $"{Name(command.NpcId)} fights hand to hand";
        if (EquipmentRules.Refusal(definition, sheet.Progression, Rules) is { } refused)
            return $"{Name(command.NpcId)}: {refused.Replace("you have", "they have")}";

        // What this displaces: whatever shares a slot with it - a two-hander takes both hands, and a piece in the off hand frees a two-hander.
        var taken = EquipmentRules.Occupies(definition).ToHashSet();
        var displaced = sheet.Gear.Where(g => taken.Contains(g.Key) || EquipmentRules.Occupies(Definition(g.Value)).Any(taken.Contains)).ToList();
        var gear=sheet.Gear;
        var belongings=sheet.Belongings;
        foreach(var (at,old) in displaced)
        {
            gear=gear.Remove(at);
            if(!old.Loaned)belongings=belongings.Add(old.Item);
        }
        var next=sheet with {
            Gear=gear.SetItem(slot,new PartyGear(entry with {Bag=0,Slot=-1,Locked=false,Junk=false},command.Loan)),
            Belongings=belongings.OrderBy(e=>e.ItemId.Value,StringComparer.Ordinal).ToImmutableArray() };
        if(PackRefusal(next) is { } full)return full;
        if (_context.Dispatch(new ReleaseItem(entry.ItemId)) is { } busy)return busy;
        if (_context.Dispatch(new HandOver(entry.ItemId)) is { } kept)return kept;
        foreach(var (at,old) in displaced)
        {
            if(old.Loaned)_context.Dispatch(new ReceiveStack(old.Item));
            _context.Events.Publish(new PartyGearChanged(command.NpcId,at,null,old.Loaned,tick));
        }
        State.SetPartySheet(_owner,next);
        _context.Events.Publish(new PartyGearChanged(command.NpcId, slot, entry.DefId, command.Loan, tick));
        _context.Dispatch(new RefitMember(command.NpcId));
        return null;
    }

    public string? Handle(TakeBackGearCommand command, long tick)
    {
        if (Reach(command.Actor, command.NpcId) is { } far)
            return far;
        var sheet = SheetOf(command.NpcId);
        if (!sheet.Gear.TryGetValue(command.Slot, out var gear))
            return $"{Name(command.NpcId)} wears nothing in {EquipSlots.Key(command.Slot)}";
        if (!gear.Loaned)
            return $"{Name(command.NpcId)}'s {gear.Item.DefId} was a gift: it is theirs";
        if(_context.Dispatch(new CanReceiveStack(gear.Item)) is { } full)return full;
        State.SetPartySheet(_owner, sheet with { Gear = sheet.Gear.Remove(command.Slot) });
        _context.Dispatch(new ReceiveStack(gear.Item));
        _context.Events.Publish(new PartyGearChanged(command.NpcId, command.Slot, null, true, tick));
        _context.Dispatch(new RefitMember(command.NpcId));
        return null;
    }

    private long Weight(IEnumerable<InventoryEntry> items)=>items.Sum(e=>(_context.Setup.Items.Catalog.Find(e.DefId)?.WeightGrams??0)*e.Count);
    private string? PackRefusal(PartySheet sheet)
    {
        if(sheet.Belongings.Length>_context.Setup.Items.Inventory.StackSlots)return $"{Name(sheet.NpcId)}'s pack is full";
        long limit=_context.Setup.Items.Inventory.CarryLimitGrams(sheet.Progression,Rules);
        return Weight(sheet.Items)>limit ? $"That would be too heavy for {Name(sheet.NpcId)}" : null;
    }

    public string? Handle(TransferPartyItemCommand command,long tick)
    {
        if(Reach(command.Actor,command.NpcId) is { } far)return far;
        var sheet=SheetOf(command.NpcId);
        if(command.ToCompanion)
        {
            if(State.Inventory.FirstOrDefault(e=>e.ItemId==command.Item) is not { } entry)return "That item is no longer in your pack";
            if(State.Equipment.ContainsValue(entry.ItemId) || State.Bags.Contains(entry.ItemId))return "Unequip it first";
            if(Heritage(command.NpcId,_context.Setup.Items.Catalog.Get(entry.DefId)) is { } heritage)return heritage;
            var next=sheet with { Belongings=sheet.Belongings.Add(entry with {Bag=0,Slot=-1}) };
            if(PackRefusal(next) is { } full)return full;
            if(_context.Dispatch(new ReleaseItem(entry.ItemId)) is { } busy)return busy;
            if(_context.Dispatch(new HandOver(entry.ItemId)) is { } refused)return refused;
            State.SetPartySheet(_owner,next);
        }
        else
        {
            if(sheet.Belongings.FirstOrDefault(e=>e.ItemId==command.Item) is not { } entry)return "That item is no longer in their pack";
            if(_context.Dispatch(new CanReceiveStack(entry)) is { } full)return full;
            State.SetPartySheet(_owner,sheet with { Belongings=sheet.Belongings.Remove(entry) });
            _context.Dispatch(new ReceiveStack(entry));
        }
        return null;
    }

    public string? Handle(UnequipPartyGearCommand command,long tick)
    {
        if(Reach(command.Actor,command.NpcId) is { } far)return far;
        var sheet=SheetOf(command.NpcId);
        if(!sheet.Gear.TryGetValue(command.Slot,out var gear))return "That slot is empty";
        var next=sheet with {Gear=sheet.Gear.Remove(command.Slot),Belongings=sheet.Belongings.Add(gear.Item)};
        if(PackRefusal(next) is { } full)return full;
        State.SetPartySheet(_owner,next);
        _context.Events.Publish(new PartyGearChanged(command.NpcId,command.Slot,null,gear.Loaned,tick));
        _context.Dispatch(new RefitMember(command.NpcId));
        return null;
    }

    public string? Handle(EquipPartyPackCommand command,long tick)
    {
        if(Reach(command.Actor,command.NpcId) is { } far)return far;
        var sheet=SheetOf(command.NpcId);
        if(sheet.Belongings.FirstOrDefault(e=>e.ItemId==command.Item) is not { } entry)return "That item is no longer in their pack";
        if(_context.Setup.Items.Catalog.Find(entry.DefId) is not {Slot:{ } slot} definition)return "That item cannot be equipped";
        if(definition.Weapon is {Ranged:true})return $"{Name(command.NpcId)} fights hand to hand";
        if(EquipmentRules.Refusal(definition,sheet.Progression,Rules) is { } refused)return refused.Replace("you have","they have");
        var occupied=EquipmentRules.Occupies(definition).ToHashSet();
        var displaced=sheet.Gear.Where(g=>occupied.Contains(g.Key) || EquipmentRules.Occupies(Definition(g.Value)).Any(occupied.Contains)).ToArray();
        var gear=sheet.Gear;var pack=sheet.Belongings.Remove(entry);
        foreach(var (at,piece) in displaced){gear=gear.Remove(at);pack=pack.Add(piece.Item);}
        var next=sheet with {Gear=gear.SetItem(slot,new PartyGear(entry,false)),Belongings=pack};
        if(PackRefusal(next) is { } full)return full;
        State.SetPartySheet(_owner,next);
        foreach(var (at,piece) in displaced)_context.Events.Publish(new PartyGearChanged(command.NpcId,at,null,piece.Loaned,tick));
        _context.Events.Publish(new PartyGearChanged(command.NpcId,slot,entry.DefId,false,tick));
        _context.Dispatch(new RefitMember(command.NpcId));
        return null;
    }

    private ItemDefinition Definition(PartyGear gear) =>
        _context.Setup.Items.Catalog.Find(gear.Item.DefId) ?? throw new InvalidOperationException($"{gear.Item.DefId} is worn but not defined");

    /// <summary>Why the character cannot hand a party member something now, or null: they are not in the party, are down, or are out of reach.</summary>
    private string? Reach(EntityId actor, string npcId)
    {
        if (actor != _player)
            return $"unknown actor {actor}";
        if (!State.Companions.TryGetValue(npcId, out var member) || !State.Npcs.TryGetValue(npcId, out var npc))
            return $"{Name(npcId)} is not with you";
        if (member.Condition == CompanionCondition.Downed)
            return $"{Name(npcId)} is down";
        return _context.InTalkReach(npc.Body) ? null : $"{Name(npcId)} is out of reach";
    }

    // ── progression ─────────────────────────────────────────────────────────

    public string? Handle(SpendPartyAttributeCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!State.Companions.ContainsKey(command.NpcId))
            return $"{Name(command.NpcId)} is not with you";
        var sheet = SheetOf(command.NpcId);
        if (!Enum.IsDefined(command.Attribute))
            return "no such attribute";
        if (sheet.Progression.UnspentAttributePoints < 1)
            return $"{Name(command.NpcId)} has no unspent attribute points";
        State.SetPartySheet(_owner, sheet with { Progression = ProgressionEngine.Allocate(sheet.Progression, new AttributeAllocation(command.Attribute, 1)) });
        _context.Dispatch(new RefitMember(command.NpcId));
        return null;
    }

    public string? Handle(PracticePartySkill command)
    {
        if (!ProgressionKeys.IsSkillId(command.Practice.SkillId))
            return null;
        var sheet = SheetOf(command.NpcId);
        var result = ProgressionEngine.Practice(sheet.Progression, command.Practice, Rules);
        if (result.XpGained == 0 && result.Progression.NoveltyFirsts.SequenceEqual(sheet.Progression.NoveltyFirsts))
            return null;
        State.SetPartySheet(_owner, sheet with { Progression = result.Progression });
        _context.Events.Publish(new PartySkillPracticed(command.NpcId, command.Practice.SkillId, result.XpGained, result.LevelsGained,
            ProgressionEngine.SkillLevel(result.Progression, command.Practice.SkillId), command.Practice.Tick));
        return null;
    }

    /// <summary>
    /// A kill by the character or a party member: each member on their feet within the leash of the body gains its XP through their own
    /// anti-farm guards, as the character would (AG-1..AG-3, the spawner as the cluster). Someone else's kill shares nothing.
    /// </summary>
    public string? Handle(ShareKill command)
    {
        var killed = command.Killed;
        var party = State.Companions.Values.Where(c => State.Npcs.ContainsKey(c.NpcId)).ToList();
        if (party.Count == 0 || _context.Setup.Social.Companions is not { } tuning)
            return null;
        if (killed.Killer != _player && !party.Any(c => State.Npcs[c.NpcId].InstanceId == killed.Killer))
            return null;
        if (State.Creatures.Values.FirstOrDefault(c => c.Id == killed.Creature) is not { } creature || creature.Definition.XpValue <= 0)
            return null;
        var at = creature.Body;
        foreach (var member in party.Where(c => c.Condition == CompanionCondition.Up).OrderBy(c => c.NpcId, StringComparer.Ordinal))
        {
            var body = State.Npcs[member.NpcId].Body;
            double dx = body.XMm - at.XMm, dz = body.ZMm - at.ZMm;
            if (Math.Sqrt(dx * dx + dz * dz) > tuning.LeashMm)
                continue;
            var sheet = SheetOf(member.NpcId);
            var result = ProgressionEngine.Award(sheet.Progression, new XpAward(XpSource.Combat, creature.Definition.XpValue, killed.Tick)
            {
                Kill = new KillContext(creature.Definition.Id, creature.Definition.Level, creature.Site.Key),
            }, Rules);
            State.SetPartySheet(_owner, sheet with { Progression = result.Progression });
            if (result.Awarded > 0)
                _context.Events.Publish(new PartyExperienceGained(member.NpcId, result.Awarded, result.LevelsGained, result.Progression.Level, killed.Tick));
        }
        return null;
    }

    // ── views ───────────────────────────────────────────────────────────────

    public PartySheetView? View(string npcId, Func<string, (int MaxHealth, string Regard, Disposition Disposition)> standing)
    {
        if (!State.PartySheets.ContainsKey(npcId) && !State.Companions.ContainsKey(npcId))
            return null;
        var sheet = SheetOf(npcId);
        var p = sheet.Progression;
        long toNext = p.Level >= Rules.LevelCap ? 0 : Rules.Curve.ToReach(p.Level + 1);
        var (health, regard, disposition) = standing(npcId);
        return new PartySheetView(npcId, Name(npcId), p.Level, p.LevelProgressXp, toNext, p.UnspentAttributePoints, p.Skills, health,
            sheet.Gear.Select(g => new PartyGearView(g.Key, g.Value.Item.ItemId, g.Value.Item.DefId, g.Value.Item.Quality, g.Value.Loaned)).ToImmutableArray(),
            sheet.Belongings, regard, disposition) {
                Attributes=ProgressionKeys.Attributes.ToImmutableSortedDictionary(a=>a,a=>ProgressionEngine.AttributeValue(p,a,Rules)),
                CarriedGrams=Weight(sheet.Items),CarryLimitGrams=_context.Setup.Items.Inventory.CarryLimitGrams(p,Rules) };
    }

    /// <summary>Why a party member may not be handed a heritage item (owner, 2026-10-24): they must be of its people; null when they may.</summary>
    private string? Heritage(string npcId, ItemDefinition item) =>
        HeritageRules.Allows(item, State.Npcs[npcId].Definition.PeopleId) ? null : $"{Name(npcId)} is not {HeritageRules.PeopleWord(item.Heritage!)}: heritage passes only between its own people";

    private string Name(string npcId) =>
        State.Npcs.TryGetValue(npcId, out var npc) ? npc.Definition.Name
        : _context.Setup.Social.Npcs.TryGetValue(npcId, out var definition) ? definition.Name
        : npcId;
}
