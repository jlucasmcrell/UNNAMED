using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using UNNAMED.Domain;
using UNNAMED.World.Runtime;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// The simulation's determinism and command/event contracts, asserted at build time (ARCHITECTURE.md §4.1, §4.3; AGENTS.md: generation
/// randomness comes only from <c>RngChannel</c>). The wall-clock half is <see cref="ArchitectureTests.TheWallClock_IsReadOnlyByTheSession"/>
/// and the network half is <see cref="NoNetworkTests"/>.
/// </summary>
public class SimulationDoctrineTests
{
    private static readonly string[] SimulationProjects = { "Domain", "World", "Application", "Content" };

    private static readonly (string Pattern, string What)[] ForbiddenRandomness =
    {
        (@"\bnew\s+(System\.)?Random\s*\(", "a System.Random"),
        (@"\bRandom\.Shared\b", "the shared System.Random"),
        (@"\bSystem\.Random\b", "System.Random"),
        (@":\s*(System\.)?Random\b", "a type deriving from System.Random"),
        (@"(?<![\w.])Random\??\s+[A-Za-z_]\w*\s*[=;,)]", "a System.Random declared or passed in"),
        (@"\bRandomNumberGenerator\b", "the OS random source"),
        (@"\bGuid\.NewGuid\b", "a random GUID"),
    };

    /// <summary>Files allowed a randomness pattern, and why it is not simulation randomness.</summary>
    private static readonly Dictionary<string, (string Pattern, string Why)> RandomnessAllowed = new(StringComparer.Ordinal)
    {
        [Path.Combine("Domain", "EntityId.cs")] = ("RandomNumberGenerator", "a ULID's random bytes are identity, not game state (D-04)"),
        [Path.Combine("Application", "GameSession.cs")] = ("RandomNumberGenerator", "a new world with no seed picks one (PERSISTENCE.md §4.2); the seed then feeds RngChannel"),
        [Path.Combine("Application", "JournalNotes.cs")] = ("Guid.NewGuid", "names the journal's temporary file for an atomic write; file I/O, not game state"),
    };

    [Fact]
    public void SimulationCode_DrawsRandomnessOnlyFromRngChannels()
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        var offenders = new List<string>();
        foreach (string file in SimulationSources(src))
        {
            string relative = Path.GetRelativePath(src, file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split("//")[0];
                foreach (var (pattern, what) in ForbiddenRandomness.Where(f => Regex.IsMatch(code, f.Pattern)))
                {
                    if (RandomnessAllowed.TryGetValue(relative, out var allowed) && code.Contains(allowed.Pattern, StringComparison.Ordinal))
                        continue;
                    offenders.Add($"{relative}:{i + 1}: {what}: {lines[i].Trim()}");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Simulation randomness must come from RngChannel, never System.Random or the OS:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryRandomnessAllowance_IsStillNeeded()
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        foreach (var (file, (pattern, why)) in RandomnessAllowed)
            Assert.True(File.ReadAllText(Path.Combine(src, file)).Contains(pattern, StringComparison.Ordinal),
                $"{file} no longer uses {pattern} ({why}): take it off the randomness allowance list");
    }

    /// <summary>
    /// Events are facts handed to the views (§4.3): past tense, immutable, plain data. One may carry values, strings, IDs, enums, read-only
    /// collections and other records of the same kind - never a reference into the engine or presentation, a mutable collection, a
    /// delegate, or a live object of the simulation.
    /// </summary>
    [Fact]
    public void SimulationEvents_CarryOnlyPlainData()
    {
        var events = PublishedEventTypes();
        Assert.Contains(typeof(BodyMoved), events);
        Assert.Contains(typeof(WorldTimeAdvanced), events);
        var offenders = new List<string>();
        foreach (var type in events)
        {
            if (type.GetMethod("<Clone>$") is null && !type.IsValueType)
                offenders.Add($"{type.Name}: not a record");
            CheckPlain(type, type.Name, new HashSet<Type>(), offenders);
        }
        Assert.True(offenders.Count == 0, "A simulation event carries something that is not plain data:\n" + string.Join("\n", offenders.Distinct()));
    }

    /// <summary>
    /// Every command names who acts (D-11): it is a <see cref="GameCommand"/>, whose actor comes from the command's own constructor - never
    /// a fixed or default ID - and nothing in the runtime is called a command without being one.
    /// </summary>
    /// <summary>
    /// Every event goes through the bus as <c>Publish(new X(...))</c>, so <see cref="SimulationEvents_CarryOnlyPlainData"/> sees its type: an
    /// event built elsewhere and published by variable, or by an explicit type argument, would slip past it.
    /// </summary>
    [Fact]
    public void EveryPublishedEvent_IsBuiltWhereItIsPublished()
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        // The bus hands each event it was given on to the views; that is the one forwarding call.
        string bus = Path.Combine("World", "Runtime", "Events.cs");
        var offenders = new List<string>();
        foreach (string file in SimulationSources(src))
        {
            string relative = Path.GetRelativePath(src, file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split("//")[0];
                if (!Regex.IsMatch(code, @"\.Publish(<[^>]*>)?\(\s*(?!new\s)"))
                    continue;
                if (relative == bus && code.Trim() == "_views.Publish(@event);")
                    continue;
                offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "Publish events as Publish(new X(...)), so the plain-data check reaches them:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EverySimulationCommand_NamesItsActor()
    {
        var assemblies = new[] { typeof(GameCommand).Assembly, typeof(IWorldState).Assembly, Assembly.Load(new AssemblyName("UNNAMED.Application")) };
        var commands = assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract && typeof(GameCommand).IsAssignableFrom(t)).ToList();
        Assert.Contains(typeof(MoveCommand), commands);
        var offenders = new List<string>();
        foreach (var type in commands)
        {
            bool takesActor = type.GetConstructors().Any(c => c.GetParameters()
                .Any(p => p.ParameterType == typeof(EntityId) && string.Equals(p.Name, "Actor", StringComparison.Ordinal)));
            if (!takesActor)
                offenders.Add($"{type.Name}: its constructor takes no EntityId Actor");
        }
        // ...and that parameter is the one handed to GameCommand: a command built on another ID, or on a default, acts for the wrong one.
        foreach (string file in SimulationSources(Path.Combine(RepositoryRoot(), "src")))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"(\w+)\s*\([^)]*\)\s*:\s*GameCommand\(\s*([^)]*?)\s*\)"))
            {
                if (match.Groups[2].Value != "Actor")
                    offenders.Add($"{match.Groups[1].Value}: gives GameCommand '{match.Groups[2].Value}', not its own Actor");
            }
        }

        // LoggedCommand is the log's record of a command as applied (it wraps one); it is not a command itself.
        var notCommands = new[] { typeof(LoggedCommand) };
        foreach (var type in typeof(GameCommand).Assembly.GetTypes()
                     .Where(t => t.IsPublic && t.Namespace == typeof(GameCommand).Namespace && t.Name.EndsWith("Command", StringComparison.Ordinal))
                     .Where(t => !typeof(GameCommand).IsAssignableFrom(t) && !notCommands.Contains(t)))
            offenders.Add($"{type.Name}: named a command but not a GameCommand, so it names no actor");
        Assert.True(offenders.Count == 0, "A simulation command does not name the acting character or entity:\n" + string.Join("\n", offenders));
    }

    private static void CheckPlain(Type type, string path, HashSet<Type> seen, List<string> offenders)
    {
        if (!seen.Add(type))
            return;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            if (property.Name == "EqualityContract")
                continue;
            CheckMember(property.PropertyType, $"{path}.{property.Name}", seen, offenders);
        }
        if (type.IsValueType)
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                CheckMember(field.FieldType, $"{path}.{field.Name}", seen, offenders);
    }

    private static void CheckMember(Type type, string path, HashSet<Type> seen, List<string> offenders)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(EntityId))   // EntityId is a sealed, immutable value class
            return;
        string assembly = type.Assembly.GetName().Name!;
        if (assembly.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) || assembly.Contains("Presentation", StringComparison.Ordinal))
        {
            offenders.Add($"{path}: references {type.FullName} from {assembly}");
            return;
        }
        if (type.IsArray)
        {
            offenders.Add($"{path}: a mutable array ({type.Name})");
            return;
        }
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (ReadOnlyCollections.Contains(definition))
            {
                foreach (var argument in type.GetGenericArguments())
                    CheckMember(argument, $"{path}<{argument.Name}>", seen, offenders);
                return;
            }
            if (definition.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true)
            {
                offenders.Add($"{path}: a mutable collection ({type.Name})");
                return;
            }
        }
        if (typeof(Delegate).IsAssignableFrom(type) || type == typeof(object) || typeof(Type).IsAssignableFrom(type))
        {
            offenders.Add($"{path}: {type.Name} is not data");
            return;
        }
        if (!assembly.StartsWith("UNNAMED.", StringComparison.Ordinal))
        {
            offenders.Add($"{path}: {type.FullName} is not a plain type");
            return;
        }
        if (!type.IsValueType && type.GetMethod("<Clone>$") is null)
        {
            offenders.Add($"{path}: {type.FullName} is a live object, not a record");
            return;
        }
        CheckPlain(type, path, seen, offenders);
    }

    private static readonly Type[] ReadOnlyCollections =
    {
        typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyDictionary<,>), typeof(IReadOnlySet<>),
        typeof(ImmutableArray<>), typeof(ImmutableList<>), typeof(ImmutableDictionary<,>), typeof(ImmutableSortedDictionary<,>),
        typeof(ImmutableHashSet<>), typeof(ImmutableSortedSet<>), typeof(KeyValuePair<,>),
    };

    /// <summary>Every type the simulation publishes as <c>Publish(new X(...))</c>, resolved in the simulation assemblies.</summary>
    private static List<Type> PublishedEventTypes()
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        var types = new[] { typeof(GameCommand).Assembly, typeof(IWorldState).Assembly, Assembly.Load(new AssemblyName("UNNAMED.Application")) }
            .SelectMany(a => a.GetTypes())
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.ToList());
        var found = new List<Type>();
        var unresolved = new List<string>();
        foreach (string file in SimulationSources(src))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\.Publish\(\s*new\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\("))
            {
                string name = match.Groups[1].Value.Split('.')[^1];
                if (types.TryGetValue(name, out var candidates) && candidates.Count == 1)
                    found.Add(candidates[0]);
                else
                    unresolved.Add($"{Path.GetRelativePath(src, file)}: {name}");
            }
        }
        Assert.True(unresolved.Count == 0, "Published events that name no single type:\n" + string.Join("\n", unresolved.Distinct()));
        return found.Distinct().ToList();
    }

    private static IEnumerable<string> SimulationSources(string src)
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        return SimulationProjects
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(src, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
