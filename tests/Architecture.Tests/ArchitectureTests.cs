using System.Reflection;
using System.Runtime.CompilerServices;
using UNNAMED.Domain;
using UNNAMED.World;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// ARCHITECTURE.md §2 and §5, asserted mechanically "before there is anything to violate": the
/// engine boundary, the internal writer, and no state shared between two worlds in one process.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(IWorldState).Assembly;
    private static readonly Assembly Registry = typeof(UNNAMED.EntityRegistry.EntityRegistry).Assembly;
    private static readonly Assembly World = typeof(WorldDelta).Assembly;
    private static readonly Assembly Persistence = typeof(UNNAMED.Persistence.SaveStore).Assembly;
    private static readonly Assembly Application = Assembly.Load(new AssemblyName("UNNAMED.Application"));

    public static TheoryData<string> EngineFreeAssemblies => new()
    {
        "UNNAMED.Domain", "UNNAMED.Application", "UNNAMED.Content", "UNNAMED.EntityRegistry", "UNNAMED.World", "UNNAMED.Persistence",
    };

    [Theory]
    [MemberData(nameof(EngineFreeAssemblies))]
    public void OnlyPresentation_MayReferenceGodot(string assembly) =>
        Assert.DoesNotContain(
            Assembly.Load(new AssemblyName(assembly)).GetReferencedAssemblies(),
            reference => reference.Name!.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void TheWorldStateWriter_IsNotVisibleOutsideDomain()
    {
        var writer = Domain.GetType("UNNAMED.Domain.IWorldStateWriter", throwOnError: true)!;
        Assert.False(writer.IsVisible);

        // The one method that hands a system the writer must be internal too, or it leaks the writer.
        var configure = typeof(ISystem).GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.True(configure.IsAssembly, "ISystem.Configure must be internal");
    }

    [Fact]
    public void ApplicationAndPersistence_CannotWriteAuthoritativeState()
    {
        // Neither may declare InternalsVisibleTo access to Domain or World.
        foreach (var owner in new[] { Domain, World })
        {
            var friends = owner.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
            Assert.DoesNotContain(Application.GetName().Name, friends);
            Assert.DoesNotContain(Persistence.GetName().Name, friends);
            Assert.All(friends, name => Assert.True(name.EndsWith(".Tests", StringComparison.Ordinal) || name == "M2.Probe",
                $"{owner.GetName().Name} grants internals to '{name}', which is not a test assembly"));
        }
    }

    /// <summary>
    /// The sparse world delta is authoritative state (§4.4). Reading it is public; anything that
    /// changes it is internal. A new public member must be added here deliberately.
    /// </summary>
    [Fact]
    public void WorldDelta_ExposesNoPublicMutation()
    {
        string[] allowed =
        {
            nameof(WorldDelta.Baseline), nameof(WorldDelta.GetFlag), nameof(WorldDelta.IsHarvested),
            nameof(WorldDelta.GetPopulationAlive), nameof(WorldDelta.Occupant), nameof(WorldDelta.TakeSnapshot),
            nameof(WorldDelta.EffectiveCellDigest), nameof(WorldDelta.FromSnapshot), nameof(WorldDelta.CreatedIn),
            "get_" + nameof(WorldDelta.Generator), "get_" + nameof(WorldDelta.WorldSeed),
        };
        var exposed = typeof(WorldDelta)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(allowed.OrderBy(n => n, StringComparer.Ordinal), exposed);
    }

    /// <summary>
    /// "Two independent world instances in one process must not share mutable state" (§5). A static
    /// field that can change, or a static collection that can, would be shared by every world.
    /// </summary>
    [Fact]
    public void StateAssemblies_HoldNoStaticMutableState()
    {
        var offenders = new List<string>();
        foreach (var assembly in new[] { Domain, Registry, World, Persistence })
        {
            // The MessagePack source generator emits a type-to-formatter lookup into our assembly; it
            // holds serializers, not game state.
            foreach (var type in assembly.GetTypes().Where(t => t.Namespace?.StartsWith("MessagePack", StringComparison.Ordinal) != true))
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral || field.IsDefined(typeof(CompilerGeneratedAttribute)) || field.Name.Contains('<'))
                        continue;
                    if (!field.IsInitOnly || IsMutableContainer(field.FieldType))
                        offenders.Add($"{type.FullName}.{field.Name} ({field.FieldType.Name})");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Static mutable state:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheRegistry_OwnsIdentityOnly_AndDependsOnNothingButDomain()
    {
        var projectReferences = Registry.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("UNNAMED.", StringComparison.Ordinal));
        Assert.Equal(new[] { "UNNAMED.Domain" }, projectReferences);
    }

    private static bool IsMutableContainer(Type type) =>
        type.IsArray
        || (type.IsGenericType && type.GetGenericTypeDefinition() is var d
            && (d == typeof(List<>) || d == typeof(Dictionary<,>) || d == typeof(HashSet<>) || d == typeof(SortedDictionary<,>)
                || d == typeof(Queue<>) || d == typeof(Stack<>)))
        || type == typeof(System.Text.StringBuilder);
}
