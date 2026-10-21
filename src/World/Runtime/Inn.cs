// UNNAMED World - the inn's room: a bed until the hour the character asks for, and the world going on while they sleep (spec 15, owner
// finding 15). No Godot references - pure C#

namespace UNNAMED.World.Runtime;

/// <summary>
/// To the simulation: let the character a room at the inn <paramref name="KeeperId"/> keeps, to be woken at <paramref name="Hour"/> (a game
/// hour of the day, 0-23). The keeper's reply says the price, as a hire's reply states its risk; the sleep itself is the world's own advance.
/// </summary>
internal sealed record RentRoom(string KeeperId, int Hour) : InternalCommand;

/// <summary>
/// A room taken and the sleep done: the keeper, what it cost, the hour asked for, and the world tick the character wakes at - raised after
/// the world has caught up, so a save made on it is the world they wake into.
/// </summary>
public sealed record RoomRented(string KeeperId, string KeeperName, string Settlement, int Hour, long WakeTick, long Coin, long Tick);
