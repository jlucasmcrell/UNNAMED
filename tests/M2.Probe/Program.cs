// M2 probe: runs world generation and saves in a SEPARATE process, so the M2 tests can check
// determinism across fresh processes and survival of a real process kill mid-write.
//
//   digest <world-seed-hex> <content-hash> <wolf-target>   print the region r_0_0 digest
//   save <profile-root> <old|new> [<SaveStep>]             save a fixture world; FailFast at the step

using UNNAMED.M2Probe;
using UNNAMED.Persistence;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

switch (args.FirstOrDefault())
{
    case "digest":
    {
        var generator = M2Fixtures.Generator(int.Parse(args[3]));
        var tuple = new BaselineTuple(BaselineTuple.ParseSeed(args[1]), CellBaselineGeneratorV1.Version, args[2]);
        Console.WriteLine(RegionDigest.Compute(generator, tuple, new RegionKey(0, 0)));
        return 0;
    }
    case "save":
    {
        SaveStep? crashAt = args.Length > 3 ? Enum.Parse<SaveStep>(args[3]) : null;
        // Kill terminates the process on the spot (TerminateProcess): no finally blocks, no flushing, no
        // cleanup - a kill, not an exception. (Environment.FailFast would also do, but on Windows it can
        // raise an error-reporting dialog that stalls an unattended test run.)
        var store = new SaveStore(args[1], onStep: step =>
        {
            if (step == crashAt)
                System.Diagnostics.Process.GetCurrentProcess().Kill();
        });
        var world = args[2] == "new" ? M2Fixtures.NewWorld(new Registry()) : M2Fixtures.OldWorld(new Registry());
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));
        Console.WriteLine("saved");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: digest <seed> <content-hash> <wolf-target> | save <root> <old|new> [<SaveStep>]");
        return 2;
}
