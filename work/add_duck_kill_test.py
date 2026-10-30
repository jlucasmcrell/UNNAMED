"""A duck kill leaves a duck carcass that yields feathers: the hunting-to-skinning wire, proved on a species this branch added.

The wolf test proves the wire for a hostile. This one uses a mallard - band 1, 20 health - so it is a two-hit fight rather than a long one, and it
asserts the body that appears is the duck's own node, not the deer's or the wolf's.
"""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Application.Tests\CarcassTests.cs')

TEST = '''
    [Fact]
    public void AKilledMallard_LeavesADuckCarcass_ThatYieldsFeathers()
    {
        using var profile = new TempProfile();
        const string Duck = "creature.beast.duck";
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Duck, Open.X, Open.Z + 1.5, "wildlife") });
        var killed = arena.Record<CreatureKilled>();
        var before = CarcassKeys(arena.Simulation);

        arena.Fight(arena.Creature(), 400);

        var kill = Assert.Single(killed);
        Assert.Equal(Duck, kill.DefId);
        var fresh = arena.Simulation.Nodes
            .Where(n => n.NodeDefId == "node.carcass.duck" && !before.Contains(n.Key))
            .ToList();
        Assert.True(fresh.Count == 1, $"new duck carcasses: {fresh.Count}; all carcass keys: {string.Join(",", CarcassKeys(arena.Simulation))}");
        Assert.Contains("item.material.feathers",
            session.Setup.Crafting.Resources["resource.carcass.duck"].Yields.Select(y => y.ItemRef));
    }
}
'''

code = PATH.read_text(encoding='utf-8').rstrip()
if 'AKilledMallard_LeavesADuckCarcass' not in code:
    assert code.endswith('}')
    PATH.write_text(code[:-1] + TEST, encoding='utf-8')
    print('appended')
else:
    print('already there')
