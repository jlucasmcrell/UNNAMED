using System.Collections.Immutable;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Combat;

public enum TechniqueRequirementKind { Skill, Attribute, Technique, Equipment, Body, Knowledge, Relationship, Standing }

/// <summary>A technique's own gates, independent of the source that teaches it. Two skill records make a hybrid.</summary>
public sealed record TechniqueRequirement(TechniqueRequirementKind Kind, string Ref, int Minimum = 1, string Dimension = "trust");
public sealed record TechniqueRequirementCheck(TechniqueRequirement Requirement, int Have)
{
    public bool Met => Have >= Requirement.Minimum;
    public string Reason => $"needs {Requirement.Kind.ToString().ToLowerInvariant()} {Requirement.Ref} {Requirement.Minimum}, has {Have}";
}

public static class TechniqueRequirements
{
    public static ImmutableArray<TechniqueRequirementCheck> Check(TechniqueDefinition technique, Func<TechniqueRequirement, int> value) =>
        technique.Requirements.Select(r => new TechniqueRequirementCheck(r, value(r))).ToImmutableArray();

    public static string? Refusal(TechniqueDefinition technique, Func<TechniqueRequirement, int> value) =>
        Check(technique, value).FirstOrDefault(c => !c.Met)?.Reason;

    public static string? Problem(TechniqueRequirement requirement) =>
        !Enum.IsDefined(requirement.Kind) ? "unknown technique requirement kind"
        : string.IsNullOrWhiteSpace(requirement.Ref) ? "a requirement names its subject"
        : requirement.Minimum < 0 && requirement.Kind is not (TechniqueRequirementKind.Relationship or TechniqueRequirementKind.Standing) ? "requirement thresholds are not negative"
        : requirement.Kind == TechniqueRequirementKind.Attribute && !ProgressionKeys.TryParseAttribute(requirement.Ref, out _) ? "unknown attribute requirement"
        : requirement.Kind == TechniqueRequirementKind.Skill && !ProgressionKeys.IsSkillId(requirement.Ref) ? "a skill requirement names a skill"
        : null;
}

/// <summary>OD-C01: one physical scaling budget, shared rather than multiplied by three attributes.</summary>
public sealed record PhysicalScaling(double Might, double Agility, double Precision)
{
    public static PhysicalScaling Heavy { get; } = new(1, 0, 0);
    public static PhysicalScaling Balanced { get; } = new(.5, .5, 0);
    public static PhysicalScaling Dagger { get; } = new(0, .5, .5);
    public static PhysicalScaling Bow { get; } = new(.4, 0, .6);
    public static PhysicalScaling Polearm { get; } = new(.6, .4, 0);

    public string? Problem() => !double.IsFinite(Might) || !double.IsFinite(Agility) || !double.IsFinite(Precision)
        || Might < 0 || Agility < 0 || Precision < 0 || Math.Abs(Might + Agility + Precision - 1) > 1e-9
        ? "physical scaling weights must be finite, non-negative and sum to 1" : null;

    public double Multiplier(int might, int agility, int precision, int attributeBase, double percentPerPoint) =>
        Math.Max(.1, 1 + (Might * (might - attributeBase) + Agility * (agility - attributeBase)
            + Precision * (precision - attributeBase)) * percentPerPoint / 100);
}
