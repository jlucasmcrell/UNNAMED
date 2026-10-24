using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Paid rest is saved after its command completes; it never skips the shared world clock (DN8).
    private RoomRented? _slept;

    private void InstallInnRoom() => _session.Subscribe<RoomRented>(room => _slept = room);

    /// <summary>Driven from the main loop, so the save never lands in the middle of a command's own handling.</summary>
    private void TickInnRoom()
    {
        if (_slept is not { } room)
            return;
        _slept = null;
        _hud.Toast($"You rest at {room.KeeperName}'s, {room.Coin} coin paid. Health and stamina restored.", 6);
        // A bed and an hour to spare is when a reader takes up what they last found (spec 41, rest snippet): the newest page, in its own
        // words, and nothing when the journal is empty.
        if (LatestPage() is { Length: > 0 } read)
            _hud.Toast(read, 10);
        _session.SaveInBackground(SaveSlots.Quick);
    }

    /// <summary>The opening of the most recently found lore page: what the character has to read before sleeping.</summary>
    private string LatestPage() =>
        _session.Simulation is { } simulation ? UNNAMED.Domain.Knowledge.LoreRules.Latest(_session.Setup.Lore, simulation.Lore) : "";
}
