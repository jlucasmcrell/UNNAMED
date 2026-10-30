"""The fishing system reads its rules: a rod to cast, a bait spent to shorten the wait, and the water's table by the hour (goal round 2).

Everything here is additive: the merged loop's own behaviour is what runs when the pack declares no config.
"""
from pathlib import Path

fish = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\World\Runtime\Fishing.cs')
code = fish.read_text(encoding='utf-8')

# The rod gate, right after the water is known to hold fish.
old = '''        if (Space.Waters.FirstOrDefault(w=>w.Key==command.WaterKey) is not { CatchTableId: not null } water) return "There is nothing to fish for here";'''
new = '''        if (Space.Waters.FirstOrDefault(w=>w.Key==command.WaterKey) is not { CatchTableId: not null } water) return "There is nothing to fish for here";
        if (Rules.RodItemId is { } rod && !Carries(rod)) return "You need a rod";'''
assert old in code
code = code.replace(old, new, 1)

# A bait is taken at the cast, and its scale is remembered on the cast so the bite it causes is shorter.
old = '''        State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishCast,water.Key,tick,tick+CastTicks,0));'''
new = '''        _baitScale = TakeBait();
        State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishCast,water.Key,tick,tick+CastTicks,0));'''
assert old in code
code = code.replace(old, new, 1)

old = '''                var channel=RngChannel.Open(State.World.WorldSeed,_context.CellOf(State.Body),"fishing",$"{activity.TargetKey}@cast@{activity.StartedTick}");
                State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishWaiting,activity.TargetKey,tick,0,
                    tick+channel.Int(0,BiteMinTicks,BiteMaxTicks+1)));'''
new = '''                var channel=RngChannel.Open(State.World.WorldSeed,_context.CellOf(State.Body),"fishing",$"{activity.TargetKey}@cast@{activity.StartedTick}");
                // A bait shortens the wait it was cast with; the channel still decides which wait, so the same cast is still the same fish.
                int wait=channel.Int(0,BiteMinTicks,BiteMaxTicks+1);
                if(_baitScale is { } scale) wait=Math.Max(BiteMinTicks/2,(int)Math.Round(wait*scale));
                State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishWaiting,activity.TargetKey,tick,0,tick+wait));'''
assert old in code
code = code.replace(old, new, 1)

# The catch table comes from the pack's schedule when it names one for this water and hour.
old = '''        if(Space.Waters.FirstOrDefault(w=>w.Key==activity.TargetKey) is not { } water
            || water.CatchTableId is not { } tableId || !_context.Setup.Items.LootTables.TryGetValue(tableId,out var table))
            return;'''
new = '''        if(Space.Waters.FirstOrDefault(w=>w.Key==activity.TargetKey) is not { } water)
            return;
        // The hour the cast began in decides which of the water's tables is fished, where the pack schedules them.
        double hour=Clock.Hour(activity.StartedTick)%Clock.HoursPerDay;
        string tableId=Rules.TableFor(water.Kind,hour) ?? water.CatchTableId ?? string.Empty;
        if(!_context.Setup.Items.LootTables.TryGetValue(tableId,out var table))
            return;'''
assert old in code
code = code.replace(old, new, 1)

# Members: the rules, the clock, the carried-item probe and the bait.
old = '''    private RuntimeState State => _context.State;'''
new = '''    private RuntimeState State => _context.State;
    private FishingRules Rules => _context.Setup.Fishing;
    private WorldClock Clock => _context.Setup.Clock;

    /// <summary>The bait scale of the cast in the water now, once the pack's baits are known.</summary>
    private double? _baitScale;

    /// <summary>True when the pack holds <paramref name="definitionId"/> in any of its stacks.</summary>
    private bool Carries(string definitionId) =>
        _context.State.Pack.Containers.Any(c => c.Stacks.Any(s => s.DefId == definitionId))
        || _context.State.Pack.Unplaced.Any(s => s.DefId == definitionId);

    /// <summary>Spend one bait from the pack and return what its kind is worth to the wait, or null when none was carried.</summary>
    private double? TakeBait()
    {
        foreach (var container in _context.State.Pack.Containers)
            foreach (var stack in container.Stacks)
                if (Rules.Baits.TryGetValue(stack.DefId, out var bait))
                {
                    _context.Dispatch(new ExchangeItems(ImmutableArray.Create(new StackTake(stack.ItemId, 1)), null, 0, Quality.Standard));
                    return bait.BiteScale;
                }
        return null;
    }'''
assert old in code
code = code.replace(old, new, 1)

# Drop the bait scale when the line comes back, so the next cast starts clean.
old = '''    private void End(string waterKey,string reason,long tick)
    {
        State.SetActivity(_owner,null);'''
new = '''    private void End(string waterKey,string reason,long tick)
    {
        State.SetActivity(_owner,null);
        _baitScale=null;'''
assert old in code
code = code.replace(old, new, 1)
fish.write_text(code, encoding='utf-8')

# The setup carries the rules, beside the weather's.
sim = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\World\Runtime\Simulation.cs')
code = sim.read_text(encoding='utf-8')
old = '    public WeatherRules Weather { get; init; } = WeatherRules.Default;'
new = '''    public WeatherRules Weather { get; init; } = WeatherRules.Default;

    /// <summary>How a cast is equipped and what each water's table is by the hour (config.fishing; the D20 follow-on).</summary>
    public FishingRules Fishing { get; init; } = FishingRules.Default;'''
assert old in code
code = code.replace(old, new, 1)
sim.write_text(code, encoding='utf-8')
print('fishing reads its rules')
