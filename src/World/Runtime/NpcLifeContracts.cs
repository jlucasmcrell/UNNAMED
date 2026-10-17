// UNNAMED World - WP12b's hireability contract, as PR #22 (claude/wp12b-npc-life-followup) defines it in NpcLifeLinks.cs (WP13)
// No Godot references - pure C#
//
// A stand-in so WP13 codes against WP12b's names: these three types are copied word for word from WP12b's NpcLifeLinks.cs. When WP12b
// merges, delete this file; nothing else changes.

using System.Collections.Immutable;
using UNNAMED.Domain.Companions;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Someone became hireable, or stopped being (WP12b): an arrival, a departure, a hire, a dismissal, an injury, a trip. Raised whenever the
/// derived answer of <see cref="INpcHireability.IsHireable"/> changes for a key; <see cref="Reason"/> is why not, when not.
/// </summary>
public sealed record HireabilityChanged(string Key, bool Hireable, string? Reason, long Tick);

/// <summary>Whether someone can be hired now, and why not; their town roles and the hireling roles those take on.</summary>
public sealed record NpcHireView(string Key, bool Hireable, string? Refusal, ImmutableArray<string> Roles, ImmutableArray<HirelingRole> HiresAs);

/// <summary>
/// Who can be hired (WP12b), derived and never stored: a vendor or a quest giver never; anyone else whose roles hire out, who has a body to
/// work with, and who is about their own life - living, in town, not injured, not already hired, not setting out. An NPC with no routine is
/// hireable exactly when their NPC file says they hire on, as before. <see cref="HireabilityChanged"/> follows every change.
/// </summary>
public interface INpcHireability
{
    bool IsHireable(string key);

    /// <summary>The whole answer for a key; null for someone the world does not hold.</summary>
    NpcHireView? Hireability(string key);
}
