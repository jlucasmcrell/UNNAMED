// UNNAMED World - factions and reputation (M7 design §5.5)
// No Godot references - pure C#

namespace UNNAMED.World.Runtime;

/// <summary>
/// Owns: <see cref="StateSlice.Factions"/> - the player's faction ledger, seeded from the saved player and captured with them. In E2 it
/// only claims the slice; acts, reports and standing arrive in E3. It has no tick and mints nothing.
/// </summary>
internal sealed class FactionSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public FactionSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }
}
