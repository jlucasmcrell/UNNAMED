"""Append a test proving every fauna species leaves the body its kind names (hunting and skinning hooks, asserted)."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Application.Tests\CarcassTests.cs')

TEST = '''
    [Fact]
    public void EveryFaunaSpecies_LeavesTheBodyItsKindNames()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The fauna this branch carries: each kind names a carcass, and each carcass is a two-charge node that trains skill.skinning -
        // the hunting-to-skinning wire, asserted for the animals a hunter actually meets on the frontier.
        foreach (var (kind, carcass) in new[]
                 {
                     ("creature.beast.doe", "node.carcass.deer"), ("creature.beast.buck", "node.carcass.deer"),
                     ("creature.beast.elk", "node.carcass.elk"), ("creature.beast.moose", "node.carcass.moose"),
                     ("creature.beast.rabbit", "node.carcass.rabbit"), ("creature.beast.red_fox", "node.carcass.red_fox"),
                     ("creature.beast.duck", "node.carcass.duck"), ("creature.beast.crow", "node.carcass.crow"),
                     ("creature.fish.trout", "node.carcass.trout"),
                 })
        {
            Assert.Equal(carcass, session.Setup.Combat.Creatures[kind].CarcassRef);
            Assert.Equal("skill.skinning", session.Setup.Crafting.Nodes[carcass].SkillId);
            Assert.Equal(2, session.Setup.Crafting.Nodes[carcass].Charges);
        }
    }
}
'''

code = PATH.read_text(encoding='utf-8').rstrip()
if 'EveryFaunaSpecies_LeavesTheBodyItsKindNames' not in code:
    assert code.endswith('}')
    PATH.write_text(code[:-1] + TEST, encoding='utf-8')
    print('appended')
else:
    print('already there')
