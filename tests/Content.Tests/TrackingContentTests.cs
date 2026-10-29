using System;

using System.Linq;
using UNNAMED.Domain.Creatures;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The tracking skill's content (canon skill.tracking): the spoor's own numbers, and the seven techniques canon's table gives the skill
/// at the needs it gives them. The TRK lint refuses a technique with a grade canon does not use, or one that could be had without the
/// skill at all.
/// </summary>
public class TrackingContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-tracking-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void TheShippedContent_IsTheSkillsOwnCanonTable()
    {
        var rules = TrackingContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));

        // What the config says the spoor does: fresh for 30 s, gone by 150 s, a scent by 60 s, at 20 Hz.
        Assert.Equal(600, rules.FreshTicks);
        Assert.Equal(3_000, rules.FadeTicks);
        Assert.Equal(1_200, rules.ScentTicks);
        Assert.Equal(90_000, rules.ScentConeMdeg);
        Assert.Equal(1_500, rules.MarkSpacingMm);

        // And canon's seven, by the needs canon prints beside them.
        // Canon's table is written by grade; the rules hold the techniques sorted by id, so the same set is compared in that order.
        Assert.Equal(
            new[]
            {
                Tracking.ReadTheGround, Tracking.TrailSign, Tracking.FollowTheQuarry, Tracking.PredatorsBearing,
                Tracking.WalkTheirRoad, Tracking.ReadTheWake, Tracking.WhatTheTrailRefusesToSay,
            }.OrderBy(id => id, StringComparer.Ordinal),
            rules.Techniques.Keys);
        AssertTechnique(rules, Tracking.ReadTheGround, "N", ("tracking", 15), ("insight", 12));
        AssertTechnique(rules, Tracking.FollowTheQuarry, "P", ("tracking", 30), ("survival", 30));
        AssertTechnique(rules, Tracking.WalkTheirRoad, "A", ("tracking", 45), ("stealth", 45));
        AssertTechnique(rules, Tracking.PredatorsBearing, "P", ("tracking", 30), ("insight", 20));
        AssertTechnique(rules, Tracking.WhatTheTrailRefusesToSay, "E", ("tracking", 60), ("insight", 30));
    }

    [Fact]
    public void ATechniqueInAGradeCanonDoesNotUse_IsRefused()
    {
        using var content = new EditedContent(Path.Combine("abilities", "tracking", "read_the_ground.yaml"), "grade: N", "grade: X");
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "TRK001" && e.Message.Contains("grade", StringComparison.Ordinal));
    }

    [Fact]
    public void ATechniqueThatNeedsNoTracking_IsRefused()
    {
        using var content = new EditedContent(Path.Combine("abilities", "tracking", "read_the_ground.yaml"),
            "needs: { tracking: 15, insight: 12 }", "needs: { insight: 12 }");
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "TRK001" && e.Message.Contains("no tracking level", StringComparison.Ordinal));
    }

    private static void AssertTechnique(TrackingRules rules, string id, string grade, params (string Competence, int Level)[] needs)
    {
        var technique = rules.Techniques[id];
        Assert.Equal(id, technique.Id);
        Assert.Equal(grade, technique.Grade);
        Assert.Equal(needs.Length, technique.Needs.Count);
        foreach (var (competence, level) in needs)
            Assert.Equal(level, technique.Needs.GetValueOrDefault(competence));
    }
}
