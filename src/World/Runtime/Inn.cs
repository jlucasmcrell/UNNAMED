// UNNAMED World - paid inn rest without advancing the shared world (owner ruling 2026-10-24, DN8).
namespace UNNAMED.World.Runtime;

/// <summary>Let the character a room and restore health/stamina; the world clock keeps running normally.</summary>
internal sealed record RentRoom(string KeeperId) : InternalCommand;

/// <summary>A completed paid rest at the current world tick; no wake-hour or time-skip promise.</summary>
public sealed record RoomRented(string KeeperId, string KeeperName, string Settlement, long Coin, long Tick);
