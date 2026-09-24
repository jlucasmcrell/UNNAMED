// M2 probe: runs world generation, saves and migrations in a SEPARATE process, so the tests can check
// determinism across fresh processes and survival of a real process kill mid-write.
//
//   digest <world-seed-hex> <wolf-target>                  print the region r_0_0 digest
//   save <profile-root> <old|new> [<SaveStep>]             save a fixture world; killed at the step
//   migrate <profile-root> [<SaveStep>]                    migrate slot quick under the fixture content; killed at the step
//   fixture <profile-root>                                 write the historical-fixture world with this build

using UNNAMED.M2Probe;
using UNNAMED.Persistence;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

switch (args.FirstOrDefault())
{
    case "fixture":
        M2Fixtures.Historical.Write(args[1]);
        Console.WriteLine("fixture written");
        return 0;
    case "digest":
    {
        var generator = M2Fixtures.Generator(int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine(RegionDigest.Compute(generator, WorldSeed.Parse(args[1]), new RegionKey(0, 0)));
        return 0;
    }
    case "save":
    {
        var store = KillableStore(args[1], args.Length > 3 ? Enum.Parse<SaveStep>(args[3]) : null);
        var world = args[2] == "new" ? M2Fixtures.NewWorld(new Registry()) : M2Fixtures.OldWorld(new Registry());
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));
        Console.WriteLine("saved");
        return 0;
    }
    case "migrate":
    {
        var store = KillableStore(args[1], args.Length > 2 ? Enum.Parse<SaveStep>(args[2]) : null);
        Console.WriteLine(store.Migrate(SaveSlots.Quick, M2Fixtures.Historical.Context(new Registry())).Result);
        return 0;
    }
    default:
        Console.Error.WriteLine(
            "usage: digest <seed> <wolf-target> | save <root> <old|new> [<SaveStep>] | migrate <root> [<SaveStep>] | fixture <root>");
        return 2;
}

// Kill terminates the process on the spot (TerminateProcess): no finally blocks, no flushing, no
// cleanup - a kill, not an exception. (Environment.FailFast would also do, but on Windows it can raise
// an error-reporting dialog that stalls an unattended test run.) The step is announced first, so the
// test can prove the kill landed exactly there and not in some earlier crash.
static SaveStore KillableStore(string root, SaveStep? killAt) => new(root, onStep: step =>
{
    if (step != killAt)
        return;
    Console.Out.Write($"KILL {step}");
    Console.Out.Flush();
    System.Diagnostics.Process.GetCurrentProcess().Kill();
});
