using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// M7's guards over navigation (M7 design §6, G3-G25, N-X1): the derived grid is never saved, never reached for its bytes and never
/// read for its work counts; no Godot navigation outside the spike; no system subscribes to events; the authority reads no clock.
/// </summary>
public class M7GuardTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }

    /// <summary>Every C# source under a directory of the repository, generated and build output left out.</summary>
    private static List<(string Path, string[] Lines)> Sources(string relative)
    {
        string root = Path.Combine(Root(), relative);
        char sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}.godot{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetRelativePath(Root(), f).Replace('\\', '/'), File.ReadAllLines(f)))
            .ToList();
    }

    private static IEnumerable<string> Hits(IEnumerable<(string Path, string[] Lines)> files, Regex pattern) =>
        from file in files
        from line in file.Lines.Select((text, i) => (text, i))
        where pattern.IsMatch(line.text)
        select $"{file.Path}:{line.i + 1}: {line.text.Trim()}";

    private static List<(string Path, string[] Lines)> NavDomainFiles() =>
        Sources("src/Domain/Spatial").Where(f => Path.GetFileName(f.Path).StartsWith("Nav", StringComparison.Ordinal)).ToList();

    // N-X1 (G16)
    [Fact]
    public void NavDomain_IsIntegerOnly()
    {
        var files = NavDomainFiles();
        Assert.True(files.Count >= 9, $"only {files.Count} Nav*.cs files found");
        var hits = Hits(files, new Regex(@"\bdouble\b|\bfloat\b|Math\.(Sqrt|Sin|Cos|Atan|Pow|Round)\b")).ToList();
        Assert.True(hits.Count == 0, "Floating point in navigation:\n" + string.Join("\n", hits));
    }

    // G3
    [Fact]
    public void PersistenceNeverReferencesTheNavigationGrid()
    {
        var hits = Hits(Sources("src/Persistence"), new Regex(@"\b(NavGrid|NavTile|NavScratch|NavCounters)\b")).ToList();
        Assert.True(hits.Count == 0, "Persistence names the navigation grid:\n" + string.Join("\n", hits));
    }

    // G4
    [Fact]
    public void PresentationUsesNoGodotNavigation_OutsideTheSpike()
    {
        var files = Sources("src/Presentation").Where(f => !f.Path.StartsWith("src/Presentation/Spike/", StringComparison.Ordinal));
        var hits = Hits(files, new Regex(@"\b(NavigationServer3D|NavigationAgent3D|NavigationRegion3D|NavigationMesh)\b")).ToList();
        Assert.True(hits.Count == 0, "Godot navigation outside the spike:\n" + string.Join("\n", hits));
    }

    // G5
    [Fact]
    public void SystemsNeverSubscribe()
    {
        var hits = Hits(Sources("src/World").Concat(Sources("src/Domain")), new Regex(@"\.Subscribe\s*[<(]")).ToList();
        Assert.True(hits.Count == 0, "A system subscribes to events:\n" + string.Join("\n", hits));
    }

    // G8
    [Fact]
    public void M7SystemsMintNoWallClockIds()
    {
        string[] systems = { "Building.cs", "BuildingRules.cs", "Navigation.cs", "Factions.cs", "Errands.cs" };
        var files = Sources("src/World/Runtime").Where(f => systems.Contains(Path.GetFileName(f.Path))).ToList();
        Assert.Contains(files, f => f.Path.EndsWith("/Navigation.cs", StringComparison.Ordinal));
        var hits = Hits(files, new Regex(@"NewId|Registry\.")).ToList();
        Assert.True(hits.Count == 0, "An M7 system mints or registers an ID itself:\n" + string.Join("\n", hits));
    }

    // G15
    [Fact]
    public void CountersAreNeverRead()
    {
        var authority = Sources("src/Domain").Concat(Sources("src/World")).ToList();
        bool IsSink(string path) => path.EndsWith("/NavCounters.cs", StringComparison.Ordinal);
        bool IsView(string path) => path == "src/World/Runtime/Navigation.cs";

        // The snapshot type is named only where it is defined and where the view carries it.
        var typeHits = Hits(authority.Where(f => !IsSink(f.Path) && !IsView(f.Path)), new Regex(@"\bNavCounters\b")).ToList();
        Assert.True(typeHits.Count == 0, "NavCounters is named outside its definition and the view:\n" + string.Join("\n", typeHits));
        // A snapshot is taken only to build the view.
        var snapshots = Hits(authority.Where(f => !IsSink(f.Path)), new Regex(@"\.Snapshot\(\)")).ToList();
        Assert.All(snapshots, hit => Assert.True(hit.StartsWith("src/World/Runtime/Navigation.cs:", StringComparison.Ordinal) && hit.Contains("View(", StringComparison.Ordinal), hit));
        Assert.Single(snapshots);
        // No count is read anywhere in the authority.
        var reads = Hits(authority.Where(f => !IsSink(f.Path)),
            new Regex(@"\.(FullBuilds|RectRebuilds|TilesRestamped|NodesRestamped|PlansByOutcome|MaxExpansionsOneQuery|EditChecks|EditRefusalsByRule|FloodNodes)\b")).ToList();
        Assert.True(reads.Count == 0, "A navigation count is read:\n" + string.Join("\n", reads));
        // The sink offers counting and the snapshot, and nothing a decision could read.
        var members = typeof(NavCounterSink).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name).ToList();
        Assert.All(members, name => Assert.True(name.StartsWith("Count", StringComparison.Ordinal) || name == nameof(NavCounterSink.Snapshot), name));
        Assert.Empty(typeof(NavCounterSink).GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    // G24
    [Fact]
    public void M7Code_NamesItsComparers()
    {
        string[] world = { "/Navigation.cs", "/Factions.cs", "/Building.cs", "/BuildingRules.cs" };
        string[] content = { "/NavigationContent.cs", "/FactionContent.cs", "/BuildingContent.cs" };
        var files = NavDomainFiles()
            .Concat(Sources("src/World/Runtime").Where(f => world.Any(w => f.Path.EndsWith(w, StringComparison.Ordinal))))
            .Concat(Sources("src/Domain/Factions")).Concat(Sources("src/Domain/Building"))
            .Concat(Sources("src/Domain/Spatial").Where(f => f.Path.EndsWith("/StructureFootprints.cs", StringComparison.Ordinal)))
            .Concat(Sources("src/Content").Where(f => content.Any(c => f.Path.EndsWith(c, StringComparison.Ordinal)))).ToList();
        Assert.Contains(files, f => f.Path == "src/Domain/Factions/Factions.cs");
        Assert.Contains(files, f => f.Path == "src/World/Runtime/BuildingRules.cs");
        Assert.Contains(files, f => f.Path == "src/Domain/Building/Building.cs");
        var construction = new Regex(@"ToImmutableSortedDictionary\(|ImmutableSortedDictionary\.Create|ToImmutableSortedSet\(|ImmutableSortedSet\.Create|new SortedDictionary<string|new SortedSet<string|SortedDictionary<string[^>]*>\s+\w+\s*=\s*new\(|SortedSet<string>\s+\w+\s*=\s*new\(");
        var offenders = new List<string>();
        foreach (var (path, lines) in files)
        {
            string text = string.Join("\n", lines);
            foreach (Match match in construction.Matches(text))
            {
                int end = text.IndexOf(';', match.Index);
                string statement = text[match.Index..(end < 0 ? text.Length : end)];
                if (!statement.Contains("StringComparer.Ordinal", StringComparison.Ordinal))
                    offenders.Add($"{path}: {statement.Split('\n')[0].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "A sorted collection without an ordinal comparer:\n" + string.Join("\n", offenders));
    }

    // G1
    [Fact]
    public void OnlyBuildingDispatchesRebuildNavigation()
    {
        var hits = Hits(Sources("src"), new Regex(@"new\s+RebuildNavigation\(")).ToList();
        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.StartsWith("src/World/Runtime/Building.cs:", hit));
    }

    /// <summary>
    /// Ruling 5 (M7 design §8.2): every M7 action is bound to a key or a mouse button in <c>Main.DefineInput</c>, and none needs a radial or
    /// a pointer-driven menu. E8 adds build_repair; E9 adds work_order.
    /// </summary>
    [Fact]
    public void EveryM7Action_IsBoundToADirectKey()
    {
        string main = string.Join("\n", Sources("src/Presentation").Single(f => f.Path == "src/Presentation/Main.cs").Lines);
        int start = main.IndexOf("private static void DefineInput()", StringComparison.Ordinal);
        Assert.True(start >= 0, "Main.DefineInput not found");
        string define = main[start..];
        foreach (string binding in new[]
                 {
                     "Bind(\"build_mode\", Key.B)", "Bind($\"build_piece_{n}\", Key.Key1 + n - 1)", "Bind(\"build_piece_next\", Key.Pagedown)",
                     "Bind(\"build_piece_prev\", Key.Pageup)", "Bind(\"build_rotate\", Key.R)", "Bind(\"build_dismantle\", Key.Z, Key.Delete)",
                     "(\"build_place\", MouseButton.Left)", "Bind(\"build_debug\", Key.F2)", "Bind(\"faction_debug\", Key.F6)",
                     "Bind(\"build_repair\", Key.T)",
                 })
            Assert.Contains(binding, define);
        Assert.Matches(new Regex(@"for \(int n = 1; n <= 7; n\+\+\)\s*\n\s*Bind\(\$""build_piece_\{n\}"""), define);
        // Code only: a comment may say there is no radial.
        var code = Sources("src/Presentation").SelectMany(f => f.Lines).Select(l => l.Contains("//", StringComparison.Ordinal) ? l[..l.IndexOf("//", StringComparison.Ordinal)] : l);
        Assert.DoesNotContain(code, l => l.Contains("radial", StringComparison.OrdinalIgnoreCase));
    }

    // G4
    [Fact]
    public void PresentationUsesNoPhysicsQueries()
    {
        var hits = Hits(Sources("src/Presentation"), new Regex(@"\b(IntersectRay|PhysicsRayQueryParameters3D|RayCast3D|ShapeCast3D)\b")).ToList();
        Assert.True(hits.Count == 0, "Presentation asks the physics engine where things are:\n" + string.Join("\n", hits));
    }

    // G7
    [Fact]
    public void PlacementRules_AreReadOnly()
    {
        var files = Sources("src/World/Runtime").Concat(Sources("src/Domain/Spatial"))
            .Where(f => f.Path.EndsWith("/BuildingRules.cs", StringComparison.Ordinal) || f.Path.EndsWith("/NavEditCheck.cs", StringComparison.Ordinal)).ToList();
        Assert.Contains(files, f => f.Path.EndsWith("/BuildingRules.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Path.EndsWith("/NavEditCheck.cs", StringComparison.Ordinal));   // E7
        var hits = Hits(files, new Regex(@"Dispatch\(|Events\.Publish|State\.Set|State\.Place|State\.Remove|Registry\.|NewId")).ToList();
        Assert.True(hits.Count == 0, "The placement rules write:\n" + string.Join("\n", hits));
    }

    // G13
    [Fact]
    public void BuildingNeverRecordsAnAct()
    {
        var files = Sources("src/World/Runtime")
            .Where(f => f.Path.EndsWith("/Building.cs", StringComparison.Ordinal) || f.Path.EndsWith("/BuildingRules.cs", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, files.Count);
        var hits = Hits(files, new Regex(@"new\s+RecordAct\(")).ToList();
        Assert.True(hits.Count == 0, "Building records a faction act:\n" + string.Join("\n", hits));
    }

    /// <summary>Ruling 2, one storey: building adds nothing to how a body moves - no member on the movement rules or the kinematics.</summary>
    [Fact]
    public void MovementRules_GainsNoMembers()
    {
        static IEnumerable<string> Members(Type type) =>
            type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m is not MethodInfo { IsSpecialName: true }).Select(m => m.Name).Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(new[]
        {
            ".ctor", "<Clone>$", "AirtimeMs", "BaseSpeedMmPerSecond", "BodyRadiusMm", "CrouchHeightMm", "CrouchPercent", "Deconstruct", "Equals", "GetHashCode",
            "HeightMm", "InteractReachMm", "JumpApexMm", "JumpRiseMs", "JumpTuckRadiusMm", "LiftMm", "SpeedMmPerSecond", "SprintPercent", "StandHeightMm",
            "ToString", "WalkPercent",
        }, Members(typeof(MovementRules)));
        Assert.Equal(new[] { "Blocks", "CanStand", "IsClear", "Step" }, Members(typeof(Kinematics)).Where(n => n is not ("Equals" or "GetHashCode" or "ToString" or "GetType")));
    }

    // G25
    [Fact]
    public void NavigationGridBytes_AreNeverUnwrapped()
    {
        var pattern = new Regex(@"ImmutableCollectionsMarshal");
        var presentation = Hits(Sources("src/Presentation"), pattern).ToList();
        Assert.True(presentation.Count == 0, "Presentation unwraps immutable arrays:\n" + string.Join("\n", presentation));
        var authority = Hits(Sources("src/Domain").Concat(Sources("src/World"))
            .Where(f => f.Path is not ("src/Domain/Spatial/NavTile.cs" or "src/Domain/Spatial/NavGrid.cs")), pattern).ToList();
        Assert.True(authority.Count == 0, "ImmutableCollectionsMarshal outside NavTile.cs and NavGrid.cs:\n" + string.Join("\n", authority));
    }

    // STOP S3
    [Fact]
    public void AuthorityNeverReadsAClock()
    {
        var clock = new Regex(@"\bStopwatch\b|DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow|Environment\.TickCount");
        var world = Hits(Sources("src/World"), clock).ToList();
        Assert.True(world.Count == 0, "The world reads a clock:\n" + string.Join("\n", world));

        // Domain: only EntityId.NewId, whose ULID carries the time by definition (D-04).
        var domain = Hits(Sources("src/Domain"), clock).ToList();
        Assert.All(domain, hit => Assert.StartsWith("src/Domain/EntityId.cs:", hit));
        Assert.Single(domain);

        // Application: only the manifest label a save is captured with, which nothing in the authority reads back.
        var application = Hits(Sources("src/Application"), clock).ToList();
        var captured = Assert.Single(application);
        Assert.StartsWith("src/Application/GameSession.cs:", captured);
        Assert.Contains("CapturedAt", captured);
    }

    // ── factions (M7 design §5.8, §5.11; G2, G6) ──────────────────────────────────────────────────────────────

    // G2
    [Fact]
    public void PresentationSource_NeverDerivesStanding()
    {
        var hits = Hits(Sources("src/Presentation"), new Regex(@"\bStandingLadder\b|\bStandingTierOf\b|\bFactionRules\b|\bStandingOf\b|\.Ladder\.")).ToList();
        Assert.True(hits.Count == 0, "Presentation derives a standing tier:\n" + string.Join("\n", hits));
    }

    /// <summary>The files G6 holds to "no faction state": tactical, movement, building and quest code (the last two join as they land).</summary>
    private static readonly string[] Tactical =
    {
        "src/World/Runtime/Combat.cs", "src/World/Runtime/Creatures.cs", "src/World/Runtime/Companions.cs", "src/World/Runtime/Magic.cs",
        "src/World/Runtime/Errands.cs", "src/World/Runtime/Navigation.cs", "src/World/Runtime/Building.cs", "src/World/Runtime/BuildingRules.cs",
        "src/World/Runtime/Quests.cs", "src/Domain/Quests/Quests.cs",
    };

    // G6: a reflection check, not a token grep - it never bans the bare TierOf, the simulation-tier helper.
    [Fact]
    public void TacticalCode_NeverReadsFactionState()
    {
        var declared = new Regex(@"\b(?:class|record|struct|enum|interface)\s+(?:struct\s+)?(\w+)");
        var names = Tactical.Where(f => File.Exists(Path.Combine(Root(), f)))
            .SelectMany(f => declared.Matches(File.ReadAllText(Path.Combine(Root(), f))).Select(m => (File: f, Name: m.Groups[1].Value)))
            .ToList();
        Assert.True(names.Count >= 40, $"only {names.Count} declarations found in the tactical files");
        var world = typeof(UNNAMED.World.Runtime.Simulation).Assembly;
        var domain = typeof(UNNAMED.Domain.Quests.QuestRules).Assembly;
        var types = names.SelectMany(n => (n.File.StartsWith("src/Domain/", StringComparison.Ordinal) ? domain : world).GetTypes()
                .Where(t => t.DeclaringType is null && t.Name.Split('`')[0] == n.Name && t.Namespace is "UNNAMED.World.Runtime" or "UNNAMED.Domain.Quests"))
            .Distinct().SelectMany(WithNested).ToList();
        Assert.Contains(types, t => t.Name == "CreatureSystem");

        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(All).Where(f => Banned(f.FieldType)))
                offenders.Add($"{type.FullName}.{field.Name}: {field.FieldType.Name}");
            foreach (var property in type.GetProperties(All).Where(p => Banned(p.PropertyType)))
                offenders.Add($"{type.FullName}.{property.Name}: {property.PropertyType.Name}");
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                if (method is MethodInfo info && Banned(info.ReturnType) || method.GetParameters().Any(p => Banned(p.ParameterType)))
                    offenders.Add($"{type.FullName}.{method.Name}: its signature");
                foreach (var member in Referenced(method))
                {
                    if (BannedMember(member))
                        offenders.Add($"{type.FullName}.{method.Name} reads {member.DeclaringType?.Name}.{member.Name}");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Tactical code reads faction state:\n" + string.Join("\n", offenders));
    }

    private static IEnumerable<Type> WithNested(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested));

    private static bool Banned(Type type)
    {
        if (type.IsArray || type.IsByRef || type.IsPointer)
            return Banned(type.GetElementType()!);
        return type.Namespace == "UNNAMED.Domain.Factions" || (type.IsGenericType && type.GetGenericArguments().Any(Banned));
    }

    private static bool BannedMember(MemberInfo member) =>
        member switch
        {
            Type t => Banned(t),
            FieldInfo f => Banned(f.FieldType) || f.DeclaringType is { } d && Banned(d),
            MethodBase m => m.DeclaringType is { } d && Banned(d) || (m is MethodInfo i && Banned(i.ReturnType)) || m.GetParameters().Any(p => Banned(p.ParameterType))
                || (m.DeclaringType?.Name, m.Name) is ("RuntimeState", "get_Factions") or ("RuntimeState", "StandingOf") or ("SimulationSetup", "get_Factions")
                    or ("IDialogueFacts", "StandingLevel"),
            _ => false,
        };

    /// <summary>Every field, method and type a method body names: the IL's metadata-token operands, resolved.</summary>
    private static IEnumerable<MemberInfo> Referenced(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            yield break;
        }
        if (il is null)
            yield break;
        var typeArgs = method.DeclaringType is { IsGenericType: true } d ? d.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (int at = 0; at < il.Length;)
        {
            short value = il[at++];
            if (value == 0xFE)
                value = unchecked((short)(0xFE00 | il[at++]));
            var code = OpCodesByValue[value];
            switch (code.OperandType)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    int token = BitConverter.ToInt32(il, at);
                    MemberInfo? member = null;
                    try
                    {
                        member = method.Module.ResolveMember(token, typeArgs, methodArgs);
                    }
                    catch (ArgumentException)
                    {
                    }
                    if (member is not null)
                        yield return member;
                    at += 4;
                    break;
                case OperandType.InlineSwitch:
                    at += 4 + 4 * BitConverter.ToInt32(il, at);
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    at += 8;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    at += 1;
                    break;
                case OperandType.InlineVar:
                    at += 2;
                    break;
                default:
                    at += 4;
                    break;
            }
        }
    }

    private static readonly Dictionary<short, System.Reflection.Emit.OpCode> OpCodesByValue =
        typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

    [Fact]
    public void FactionSystem_ReadsNoSightNoBodiesAndNoFacing()
    {
        var factions = Sources("src/World/Runtime").Single(f => f.Path == "src/World/Runtime/Factions.cs");
        var hits = Hits(new[] { factions }, new Regex(@"SightWalls|Perception|\.Body\b|FacingMdeg|State\.Npcs|State\.Conversation|Dispatch\(")).ToList();
        Assert.True(hits.Count == 0, "FactionSystem reads sight, a body, a facing or a conversation, or dispatches:\n" + string.Join("\n", hits));

        var authority = new[] { typeof(UNNAMED.World.Runtime.Simulation).Assembly, typeof(UNNAMED.Domain.Factions.FactionRules).Assembly };
        var reserved = authority.SelectMany(a => a.GetTypes())
            .SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly).Select(m => m.Name)))
            .Where(n => n is "WitnessRules" or "BestWitness" or "IsSettled").ToList();
        Assert.True(reserved.Count == 0, "The witnessed channel is M9: " + string.Join(", ", reserved));
    }
}
