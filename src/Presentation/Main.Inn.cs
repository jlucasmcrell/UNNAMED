using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Spec 15 (owner finding 15): a room at the inn is a safe place to stop. The world has already caught up to the hour the character asked
    // for by the time the news arrives - the sleep is the simulation's own advance - so the save made here is the world they wake into.
    private RoomRented? _slept;

    private void InstallInnRoom() => _session.Subscribe<RoomRented>(room => _slept = room);

    /// <summary>Driven from the main loop, so the save never lands in the middle of a command's own handling.</summary>
    private void TickInnRoom()
    {
        if (_slept is not { } room)
            return;
        _slept = null;
        int minute = _session.Setup.Clock.MinuteOfDay(room.WakeTick);
        _hud.Toast($"You sleep at {room.KeeperName}'s, {room.Coin} coin paid. Up at {minute / 60:00}:{minute % 60:00}, and the road still there.", 6);
        _session.SaveInBackground(SaveSlots.Quick);
    }
}
