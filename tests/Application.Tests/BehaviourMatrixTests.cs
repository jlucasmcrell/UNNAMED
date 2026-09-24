using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M3d's exit proof for the creature layer: the content bible's five archetypes (and the grey wolf that proved the
/// systems first) are separate creatures whose bodies and blows differ, and they behave differently in the real
/// simulation - measured, not asserted from the YAML. The committed matrix (docs/M3D_BEHAVIOUR_MATRIX.md) must equal
/// what the build produces; regenerate it with UNNAMED_WRITE_MATRIX=1 after an intended change and review the diff.
/// </summary>
public class BehaviourMatrixTests
{
    private const int Worlds = 8;
    private const double TickSeconds = 0.05;
    private const int MaxTicks = 2_400;

    /// <summary>Open ground: nothing solid within 32 m of it.</summary>
    private static readonly (double X, double Z) Home = (125, 70);

    /// <summary>A role that holds its place and colours nothing: the archetype alone is measured.</summary>
    private const string Neutral = "roamer";

    private const string Hound = "creature.beast.ash_ember_hound";
    private const string Husk = "creature.undead.bone_walker_husk";
    private const string Armour = "creature.construct.animated_armour";
    private const string Boar = "creature.beast.bristleback_boar";
    private const string Spider = "creature.beast.cave_hunting_spider";

    /// <summary>The content bible's §10 archetype table: what each is for.</summary>
    private static readonly (string Id, string Part)[] Archetypes =
    {
        (Hound, "fast predator: movement, timing, pursuit"),
        (Husk, "basic humanoid: readable melee and reach"),
        (Armour, "armored heavy: armor and weak-point logic"),
        (Boar, "charging brute: lateral movement and terrain"),
        (Spider, "ambush, nonhuman: perception and avoidance"),
        (Arena.Wolf, "the M3c proof archetype (pack beast)"),
    };

    private sealed record Measured(
        string Id,
        string Opener,
        double TellAtM,
        double LandsFromM,
        double FirstBlowS,
        double? CatchesSprinterS,
        string? CatchesWith,
        double? KillS,
        int FighterDeaths,
        double DieS,
        double? WalkNoticedM,
        double? SprintNoticedM)
    {
        public string Signature => string.Join("|", Opener, Math.Round(TellAtM), Math.Round(LandsFromM), CatchesSprinterS.HasValue,
            WalkNoticedM is { } w ? Math.Round(w / 4) : -1, KillS is { } k ? Math.Round(k / 5) : -1);
    }

    [Fact]
    public void TheBehaviourMatrix_IsGeneratedFromTheBuild_AndEachArchetypeBehavesAsItsOwnCreature()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var combat = session.Setup.Combat;

        var rows = Archetypes.Select(a => Measure(session, a.Id)).ToList();
        string matrix = Render(session, rows);
        string path = Path.Combine(Harness.RepoRoot(), "docs", "M3D_BEHAVIOUR_MATRIX.md");
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_MATRIX") == "1")
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(matrix));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), matrix);

        // Five separate creatures, each with its own body and blow - never one body in five roles.
        var five = Archetypes.Take(5).Select(a => combat.Creatures[a.Id]).ToList();
        Assert.Equal(5, five.Select(c => c.Attack.Source).Distinct().Count());
        Assert.Equal(5, five.Select(c => (c.MoveSpeedMmPerSecond, c.MaxHealth, c.RadiusMm)).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.Signature).Distinct().Count());

        var by = rows.ToDictionary(r => r.Id);
        // A sprint gets away from everything but the hound's pace and the boar's charge (sidestep a charge; do not run from it).
        Assert.Equal(combat.Creatures[Hound].Attack.Source, by[Hound].CatchesWith);
        Assert.Equal(combat.Creatures[Boar].Charge!.Source, by[Boar].CatchesWith);
        Assert.All(rows.Where(r => r.Id is not (Hound or Boar)), r => Assert.Null(r.CatchesSprinterS));
        // The husk's cleave lands from further off than any other blow.
        Assert.All(rows.Where(r => r.Id != Husk), r => Assert.True(by[Husk].LandsFromM > r.LandsFromM + 0.5, $"{r.Id} lands from {r.LandsFromM:0.0} m"));
        // The armour is the slowest thing to cut down face to face.
        Assert.All(rows.Where(r => r.Id != Armour), r => Assert.True((by[Armour].KillS ?? double.MaxValue) > (r.KillS ?? 0), r.Id));
        // The boar opens from range, with its charge.
        Assert.Equal(combat.Creatures[Boar].Charge!.Source, by[Boar].Opener);
        Assert.True(by[Boar].TellAtM >= 5, $"the boar's tell began at {by[Boar].TellAtM:0.0} m");
        // The spider is the one a walker can come closest to before it notices.
        Assert.All(rows.Where(r => r.Id != Spider), r => Assert.True((by[Spider].WalkNoticedM ?? 0) < (r.WalkNoticedM ?? double.MaxValue), r.Id));
    }

    // ── probes ─────────────────────────────────────────────────────────────

    private static Measured Measure(GameSession session, string id)
    {
        double facing = Facing(session, id);
        var (opener, tellAt, landsFrom, firstBlow) = Stand(session, id, facing);
        var (catches, with) = Flee(session, id, facing);
        var (kill, deaths) = Kill(session, id, facing);
        double die = Die(session, id, facing);
        return new Measured(id, opener, tellAt, landsFrom, firstBlow, catches, with, kill, deaths, die,
            Noticed(session, id, facing, behind: false, Gait.Walk), Noticed(session, id, facing, behind: true, Gait.Sprint));
    }

    /// <summary>Where the creature faces at rest (placement is keyed, so it faces the same way in every probe).</summary>
    private static double Facing(GameSession session, string id) =>
        Arena.OpenCreatures(session, session.Setup, (Home.X, Home.Z - 40), 0, new[] { (id, Home.X, Home.Z, Neutral) })
            .Simulation.Creatures.Single().Body.FacingMdeg / 1000.0 * Math.PI / 180;

    private static (double X, double Z) Ahead(double facing, double metres) =>
        (Home.X + Math.Sin(facing) * metres, Home.Z + Math.Cos(facing) * metres);

    private static int DegreesToward((double X, double Z) from) =>
        CombatRules.FacingTowards((long)(from.X * 1000), (long)(from.Z * 1000), (long)(Home.X * 1000), (long)(Home.Z * 1000)) / 1000;

    private static double Between(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2)) / 1000;

    private static Arena Open(GameSession session, string id, (double X, double Z) player, int level, string weapon, ulong seed = 42) =>
        Arena.OpenCreatures(session, session.Setup, player, DegreesToward(player), new[] { (id, Home.X, Home.Z, Neutral) },
            r => Fighter(r, level, weapon), seed);

    /// <summary>A level-L character with its L-1 level-up points in Might, the weapon in hand (and arrows for a bow).</summary>
    private static PlayerRecord Fighter(PlayerRecord fresh, int level, string weapon)
    {
        var inventory = fresh.Inventory.ToList();
        if (inventory.All(e => e.DefId != weapon))
            inventory.Add(Arena.Stack(weapon, 1));   // the bow is found in the world, not carried from the start (M6)
        if (weapon == "item.weapon.hunting_bow")
            inventory.Add(Arena.Stack("item.ammo.arrow_rough", 40));
        var progression = fresh.Progression with
        {
            Level = level,
            Allocation = level > 1
                ? ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, level - 1) })
                : ImmutableSortedDictionary<CharacterAttribute, int>.Empty,
        };
        return new PlayerRecord(fresh.Id, fresh.Name, fresh.XMm, fresh.YMm, fresh.ZMm, fresh.AppearanceSeed, inventory, progression, fresh.FacingMdeg,
            fresh.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, inventory.Single(e => e.DefId == weapon).ItemId) }, fresh.Currency,
            fresh.Effects);
    }

    /// <summary>Loose one arrow at it and wait for the arrow to land.</summary>
    private static void Shoot(Arena arena)
    {
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 1);
    }

    /// <summary>
    /// 13 m in front of it, one arrow, then stand: what it opens with, how far off its tell begins and its blow lands
    /// (centre to centre), and how long from the arrow to its first blow landing.
    /// </summary>
    private static (string Opener, double TellAt, double LandsFrom, double FirstBlow) Stand(GameSession session, string id, double facing)
    {
        var arena = Open(session, id, Ahead(facing, 13), 3, "item.weapon.hunting_bow");
        var started = arena.Record<AttackStarted>();
        var hits = arena.Record<HitResolved>();
        Shoot(arena);
        long shot = arena.Simulation.WorldTick;
        string opener = "";
        double tellAt = 0;
        for (int i = 0; i < MaxTicks; i++)
        {
            arena.Face(arena.Creature());
            arena.Tick();
            var creature = arena.Creature();
            double distance = Between(creature.Body, arena.Simulation.Player.Body);
            if (opener == "" && started.FirstOrDefault(s => s.Attacker == creature.Id) is { } tell)
                (opener, tellAt) = (tell.Source, distance);
            if (hits.FirstOrDefault(h => h.Target == arena.Player) is { } blow)
                return (opener, tellAt, distance, (blow.Tick - shot) * TickSeconds);
        }
        throw new InvalidOperationException($"{id} never landed a blow on a player standing 13 m off");
    }

    /// <summary>8 m in front of it, one arrow, then sprint straight away: how long until it lands a blow, and with what, if it ever does.</summary>
    private static (double? Seconds, string? With) Flee(GameSession session, string id, double facing)
    {
        var start = Ahead(facing, 8);
        var arena = Open(session, id, start, 3, "item.weapon.hunting_bow");
        var hits = arena.Record<HitResolved>();
        Shoot(arena);
        long shot = arena.Simulation.WorldTick;
        int away = (DegreesToward(start) + 180) % 360;
        double rad = away * Math.PI / 180;
        for (int i = 0; i < 400; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player,
                new MoveIntent((int)Math.Round(Math.Sin(rad) * 1000), (int)Math.Round(Math.Cos(rad) * 1000), Gait.Sprint, away * 1000)));
            arena.Tick();
            if (hits.FirstOrDefault(h => h.Target == arena.Player) is { } blow)
                return ((blow.Tick - shot) * TickSeconds, blow.Source);
        }
        return (null, null);
    }

    /// <summary>A level-3 fighter with the rusted sword, face to face: median seconds to the kill, and how many worlds the fighter died in.</summary>
    private static (double? Median, int Deaths) Kill(GameSession session, string id, double facing)
    {
        var seconds = new List<double>();
        int deaths = 0;
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Open(session, id, Ahead(facing, 2.6), 3, "item.weapon.rusted_sword", seed);
            var started = arena.Record<AttackStarted>();
            var died = arena.Record<PlayerDied>();
            for (int i = 0; i < MaxTicks && died.Count == 0 && arena.Creature().Alive; i++)
                Swing(arena);
            if (died.Count > 0)
            {
                deaths++;
                continue;
            }
            Assert.False(arena.Creature().Alive, $"{id} world {seed}: not killed in {MaxTicks * TickSeconds} s");
            seconds.Add((arena.Simulation.WorldTick - started.First(s => s.Attacker == arena.Player).Tick) * TickSeconds);
        }
        return (seconds.Count == 0 ? null : Median(seconds), deaths);
    }

    /// <summary>One tick of the simplest competent fight: face it, close to reach, swing whenever free.</summary>
    private static void Swing(Arena arena)
    {
        var creature = arena.Creature();
        var body = arena.Simulation.Player.Body;
        var combat = arena.Simulation.Combat;
        double dx = creature.Body.XMm - body.XMm, dz = creature.Body.ZMm - body.ZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm);
        var definition = arena.Simulation.Setup.Combat.Creatures[creature.DefId];
        bool inReach = distance <= combat.Weapon.ReachMm + definition.RadiusMm - 150;
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, inReach
            ? MoveIntent.Idle(facing)
            : new MoveIntent((int)Math.Round(dx / distance * 1000), (int)Math.Round(dz / distance * 1000), Gait.Run, facing)));
        if (inReach && combat.Phase == CombatPhase.Idle)
            arena.Simulation.Enqueue(new AttackCommand(arena.Player));
        arena.Tick();
    }

    /// <summary>A level-1 character in no armor wakes it with one swing, then stands: median seconds from the first wound to death.</summary>
    private static double Die(GameSession session, string id, double facing)
    {
        var seconds = new List<double>();
        for (ulong seed = 1; seed <= Worlds; seed++)
        {
            var arena = Open(session, id, Ahead(facing, 2.6), 1, "item.weapon.rusted_sword", seed);
            var hits = arena.Record<HitResolved>();
            var died = arena.Record<PlayerDied>();
            arena.Submit(new AttackCommand(arena.Player));
            for (int i = 0; i < MaxTicks && died.Count == 0; i++)
                arena.Tick();
            var death = Assert.Single(died);
            seconds.Add((death.Tick - hits.First(h => h.Target == arena.Player && h.Damage > 0).Tick) * TickSeconds);
        }
        return Median(seconds);
    }

    /// <summary>From 32 m, straight at it - head-on or from behind - until it notices: how far off it was then.</summary>
    private static double? Noticed(GameSession session, string id, double facing, bool behind, Gait gait)
    {
        var start = Ahead(facing, behind ? -32 : 32);
        var arena = Open(session, id, start, 1, "item.weapon.rusted_sword");
        for (int i = 0; i < MaxTicks; i++)
        {
            var creature = arena.Creature();
            var body = arena.Simulation.Player.Body;
            double distance = Between(creature.Body, body);
            if (creature.Mind != CreatureMind.Unaware)
                return distance;
            if (distance < 1.2)
                return null;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(creature.Body.XMm - body.XMm, creature.Body.ZMm - body.ZMm, gait)));
            arena.Tick();
        }
        return null;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }

    // ── rendering ──────────────────────────────────────────────────────────

    private static string N(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string M(long mm) => (mm / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Render(GameSession session, List<Measured> rows)
    {
        var combat = session.Setup.Combat;
        var movement = session.Setup.Movement;
        var b = new StringBuilder();
        b.Append("# M3d behaviour matrix\n\n");
        b.Append("**Generated from the build; do not edit.** `BehaviourMatrixTests` reads the creatures from the game's own content and ");
        b.Append("measures them in the real simulation, and fails when this file differs from what the build produces. Regenerate after ");
        b.Append("an intended change with `UNNAMED_WRITE_MATRIX=1 dotnet test --filter BehaviourMatrix` (from `src/`) and review the diff.\n\n");
        b.Append("Two layers, kept apart (owner ruling, M3d): a **creature archetype** is a body and its blows - what it is; a **role** is ");
        b.Append("what one creature does in its place - how it waits, how far it goes, whether it calls. Any archetype can take any role. ");
        b.Append("The five archetypes are the content bible's (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §10); the grey wolf is the ");
        b.Append("archetype the systems were proven on first. Every number is placeholder tuning (`PROTOTYPE.md` A-5).\n\n");

        b.Append("## Archetypes: bodies and blows (content)\n\n");
        b.Append("| Creature | For | Level | Health | Armor torso / head | Speed m/s | Turn deg/s | Blow | Also | Sight m / FOV deg / hearing m |\n");
        b.Append("|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var (id, part) in Archetypes)
        {
            var c = combat.Creatures[id];
            b.Append($"| {session.DisplayName(id)} | {part} | {c.Level} | {c.MaxHealth} | {c.Armor.GetValueOrDefault(BodyRegion.Torso)} / ");
            b.Append($"{c.Armor.GetValueOrDefault(BodyRegion.Head)} | {M(c.MoveSpeedMmPerSecond)} | {c.TurnMdegPerSecond / 1000} | {Blow(session, c.Attack)} | ");
            b.Append($"{Also(session, c)} | {M(c.Senses.SightMm)} / {c.Senses.FieldOfViewMdeg / 1000} / {M(c.Senses.HearingMm)} |\n");
        }

        b.Append("\n## Archetypes: measured in the simulation\n\n");
        b.Append($"Each alone on open ground in the neutral `{Neutral}` role (with no route to walk, it holds its place), so only the archetype differs. ");
        b.Append("Distances are centre to centre.\n\n");
        b.Append("- **Opens with / tell at / lands from**: a level-3 archer 13 m in front looses one arrow, then stands. The first blow it ");
        b.Append("begins, how far off its tell (the windup) began, how far off the blow landed, and seconds from the arrow to that blow.\n");
        b.Append($"- **Runs down a sprinter**: from 8 m, one arrow, then a straight sprint away ({M(movement.BaseSpeedMmPerSecond * movement.SprintPercent / 100)} m/s): ");
        b.Append("seconds until its first blow lands and which blow it was, or no.\n");
        b.Append($"- **Sword kill**: a level-3 fighter with the rusted sword, face to face, swinging whenever free; median over {Worlds} worlds, ");
        b.Append("from the first swing, and the worlds the fighter died in first.\n");
        b.Append($"- **Death standing**: a level-1 character in no armor wakes it with one swing and stands; median over {Worlds} worlds from the first wound.\n");
        b.Append("- **Walker seen at / sprinter heard at**: from 32 m straight at it - walking head-on, sprinting from behind - how far off it ");
        b.Append("was when it first took notice (suspicious or more).\n\n");
        b.Append("| Creature | Opens with | Tell at m | Lands from m | Arrow to blow s | Runs down a sprinter | Sword kill s (fighter died) | Death standing s | Walker seen at m | Sprinter heard at m |\n");
        b.Append("|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var r in rows)
        {
            string kill = r.KillS is { } k ? N(k) : "no kill";
            b.Append($"| {session.DisplayName(r.Id)} | {session.DisplayName(r.Opener)} | {N(r.TellAtM)} | {N(r.LandsFromM)} | {N(r.FirstBlowS)} | ");
            b.Append($"{(r.CatchesSprinterS is { } s ? $"yes, {N(s)} s ({session.DisplayName(r.CatchesWith!)})" : "no")} | {kill} ({r.FighterDeaths}/{Worlds}) | {N(r.DieS)} | ");
            b.Append($"{(r.WalkNoticedM is { } w ? N(w) : "never")} | {(r.SprintNoticedM is { } h ? N(h) : "never")} |\n");
        }

        b.Append("\n## Roles (content)\n\n");
        b.Append("| Role | At rest | Territory m | Calls for help | Answers calls | Keeps off m | Flees below | Also |\n");
        b.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (var role in combat.Roles.Values)
        {
            var also = new List<string>();
            if (role.WanderMm > 0)
                also.Add($"wanders {M(role.WanderMm)} m");
            if (role.FlankMm > 0)
                also.Add($"flanks {M(role.FlankMm)} m wide");
            if (role.StrikeWithinMm > 0)
                also.Add($"strikes inside {M(role.StrikeWithinMm)} m");
            if (role.Unaware == UnawareBehaviour.Sleep)
                also.Add($"hears at {role.SleepHearingPercent}% asleep");
            if (role.PounceOnNoise)
                also.Add("goes for a footfall in its territory");
            b.Append($"| {role.Id} | {role.Unaware.ToString().ToLowerInvariant()} | {(role.TerritoryMm > 0 ? M(role.TerritoryMm) : "-")} | ");
            b.Append($"{(role.CallsForHelp ? "yes" : "no")} | {(role.AnswersCalls ? "yes" : "no")} | {(role.KeepDistanceMm > 0 ? M(role.KeepDistanceMm) : "-")} | ");
            b.Append($"{(role.FleeBelowPercent > 0 ? $"{role.FleeBelowPercent}%" : "-")} | {(also.Count == 0 ? "-" : string.Join("; ", also))} |\n");
        }

        b.Append("\n## Ashen Hollow's spawners (content)\n\n");
        b.Append("| Spawner | At (x, z) m | Creatures, each in its role | Returns |\n");
        b.Append("|---|---|---|---|\n");
        foreach (var site in combat.Spawns.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            string members = string.Join(", ", site.Members.GroupBy(m => (m.CreatureId, m.RoleId))
                .Select(g => $"{session.DisplayName(g.Key.CreatureId)} as {g.Key.RoleId}{(g.Count() > 1 ? $" x{g.Count()}" : "")}"));
            string returns = site.RespawnTicks > 0 ? $"after {N(site.RespawnTicks * TickSeconds / 60)} min" : "no";
            b.Append($"| {site.Key} | ({M(site.XMm)}, {M(site.ZMm)}) | {members} | {returns} |\n");
        }
        return b.ToString();
    }

    private static string Blow(GameSession session, AttackProfile attack) =>
        $"{session.DisplayName(attack.Source)}: {attack.DamageType} {attack.DamageMin}-{attack.DamageMax}, reach {M(attack.ReachMm)} m" +
        (attack.LungeMm > 0 ? $" + {M(attack.LungeMm)} m lunge" : "") + $", tell {N(attack.WindupTicks * TickSeconds)} s" +
        (attack.OnHitEffect is { } effect ? $", {session.DisplayName(effect)} {attack.OnHitEffectPercent}%" : "");

    private static string Also(GameSession session, CreatureDefinition c)
    {
        var also = new List<string>();
        if (c.Charge is { } charge)
            also.Add($"{session.DisplayName(charge.Source)}: {M(charge.ChargeSpeedMmPerSecond)} m/s from {M(charge.ChargeMinRangeMm)}-{M(charge.ReachMm)} m, " +
                     $"{charge.DamageType} {charge.DamageMin}-{charge.DamageMax}{(charge.ForcesStagger ? ", knocks down" : "")}, " +
                     $"stunned {N(charge.StunTicks * TickSeconds)} s by what it runs into");
        if (c.Attack.Advances)
            also.Add("bites on the run");
        if (c.WeakPoint is { } weak)
            also.Add($"open {weak.Region.ToString().ToLowerInvariant()}{(weak.FromBehind ? " from behind" : "")}");
        foreach (var (type, resist) in c.Resistances)
            also.Add($"{type} {(resist > 0 ? "resisted" : "weakness")} {Math.Abs(resist) * 100:0}%");
        var immune = session.Setup.Combat.Effects.Values.Where(e => e.ImmuneTags.Overlaps(c.Tags)).Select(e => session.DisplayName(e.Id)).ToList();
        if (immune.Count > 0)
            also.Add($"immune to {string.Join(", ", immune)}");
        return also.Count == 0 ? "-" : string.Join("; ", also);
    }
}
