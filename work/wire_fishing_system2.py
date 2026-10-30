"""Patch the fishing system with anchors matched exactly as they appear (goal round 2)."""
from pathlib import Path

fish = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\World\Runtime\Fishing.cs')
code = fish.read_text(encoding='utf-8')

pairs = []

pairs.append((
    'if(Space.Waters.FirstOrDefault(w=>w.Key==command.WaterKey) is not { CatchTableId: not null } water) return "There is nothing to fish for here";',
    'if(Space.Waters.FirstOrDefault(w=>w.Key==command.WaterKey) is not { CatchTableId: not null } water) return "There is nothing to fish for here";\n'
    '        if(Rules.RodItemId is { } rod && !Carries(rod)) return "You need a rod";',
))

pairs.append((
    'State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishCast,water.Key,tick,tick+CastTicks,0));',
    '_baitScale=TakeBait();\n'
    '        State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishCast,water.Key,tick,tick+CastTicks,0));',
))

pairs.append((
    'State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishWaiting,activity.TargetKey,tick,0,\n'
    '                    tick+channel.Int(0,BiteMinTicks,BiteMaxTicks+1)));',
    'int wait=channel.Int(0,BiteMinTicks,BiteMaxTicks+1);\n'
    '                if(_baitScale is { } scale) wait=Math.Max(BiteMinTicks/2,(int)Math.Round(wait*scale));\n'
    '                State.SetActivity(_owner,new PlayerActivity(ActivityKind.FishWaiting,activity.TargetKey,tick,0,tick+wait));',
))

pairs.append((
    'if(Space.Waters.FirstOrDefault(w=>w.Key==activity.TargetKey) is not { } water\n'
    '            || water.CatchTableId is not { } tableId || !_context.Setup.Items.LootTables.TryGetValue(tableId,out var table))\n'
    '            return;',
    'if(Space.Waters.FirstOrDefault(w=>w.Key==activity.TargetKey) is not { } water)\n'
    '            return;\n'
    '        double hour=Clock.Hour(activity.StartedTick)%Clock.HoursPerDay;\n'
    '        string tableId=Rules.TableFor(water.Kind,hour) ?? water.CatchTableId ?? string.Empty;\n'
    '        if(!_context.Setup.Items.LootTables.TryGetValue(tableId,out var table))\n'
    '            return;',
))

pairs.append((
    '    private RuntimeState State => _context.State;',
    '    private RuntimeState State => _context.State;\n'
    '    private FishingRules Rules => _context.Setup.Fishing;\n'
    '    private WorldClock Clock => _context.Setup.Clock;\n'
    '\n'
    '    /// <summary>The bait scale of the cast in the water now.</summary>\n'
    '    private double? _baitScale;\n'
    '\n'
    '    /// <summary>True when the pack holds <paramref name="definitionId"/> in any stack, placed or not.</summary>\n'
    '    private bool Carries(string definitionId) =>\n'
    '        State.Pack.Containers.Any(c => c.Stacks.Any(s => s.DefId == definitionId))\n'
    '        || State.Pack.Unplaced.Any(s => s.DefId == definitionId);\n'
    '\n'
    '    /// <summary>Spend one bait and return what its kind is worth to the wait; null when none is carried.</summary>\n'
    '    private double? TakeBait()\n'
    '    {\n'
    '        foreach (var container in State.Pack.Containers)\n'
    '            foreach (var stack in container.Stacks)\n'
    '                if (Rules.Baits.TryGetValue(stack.DefId, out var bait))\n'
    '                {\n'
    '                    _context.Dispatch(new ExchangeItems(ImmutableArray.Create(new StackTake(stack.ItemId, 1)), null, 0, Quality.Standard));\n'
    '                    return bait.BiteScale;\n'
    '                }\n'
    '        return null;\n'
    '    }',
))

pairs.append((
    '    private void End(string waterKey,string reason,long tick)\n    {\n        State.SetActivity(_owner,null);',
    '    private void End(string waterKey,string reason,long tick)\n    {\n        State.SetActivity(_owner,null);\n        _baitScale=null;',
))

for old, new in pairs:
    if old not in code:
        raise SystemExit('anchor missing:\n' + old[:160])
    code = code.replace(old, new, 1)

fish.write_text(code, encoding='utf-8')

sim = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\World\Runtime\Simulation.cs')
code = sim.read_text(encoding='utf-8')
old = '    public WeatherRules Weather { get; init; } = WeatherRules.Default;'
if 'public FishingRules Fishing' not in code:
    code = code.replace(old, old + '\n\n    /// <summary>How a cast is equipped and what each water\'s table is by the hour (config.fishing; the D20 follow-on).</summary>\n    public FishingRules Fishing { get; init; } = FishingRules.Default;', 1)
    sim.write_text(code, encoding='utf-8')
print('fishing reads its rules')
