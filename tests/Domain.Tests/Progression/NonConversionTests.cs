using System.Reflection;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>
/// ROADMAP M2c exit (c), PROGRESSION.md §1: no API exists by which gold, items or another axis's currency can
/// advance level XP, attributes or skill. Each axis advances only through its own currency type, and a
/// learning event changes only the knowledge record.
/// </summary>
public class NonConversionTests
{
    private static readonly Type[] Currencies =
        { typeof(XpAward), typeof(SkillPractice), typeof(TechniqueLearning), typeof(AttributeAllocation), typeof(AttributeGrant) };

    private static readonly Type[] Advancing =
        { typeof(CharacterProgression), typeof(XpResult), typeof(SkillResult), typeof(LearnResult), typeof(DeathResult) };

    private static IEnumerable<MethodInfo> AdvancingMethods() =>
        typeof(ProgressionEngine).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => Advancing.Contains(m.ReturnType));

    [Fact]
    public void EveryMethodThatAdvancesAnAxis_TakesOnlyTheProgression_TheRules_AndAtMostOneCurrency()
    {
        var allowed = Currencies.Append(typeof(CharacterProgression)).Append(typeof(ProgressionRules)).ToHashSet();
        var methods = AdvancingMethods().ToList();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var parameters = method.GetParameters().Select(p => p.ParameterType).ToList();
            Assert.All(parameters, t => Assert.True(allowed.Contains(t), $"{method.Name} takes a {t.Name}, which is no axis's currency"));
            Assert.True(parameters.Count(Currencies.Contains) <= 1, $"{method.Name} takes two currencies: a conversion");
        }
    }

    [Fact]
    public void EachCurrencyAdvancesOnlyItsOwnAxis()
    {
        // The pairing is fixed by type: an XP award cannot reach Practice, practice cannot reach Award, and so on.
        var expected = new Dictionary<string, Type?>
        {
            [nameof(ProgressionEngine.Award)] = typeof(XpAward),
            [nameof(ProgressionEngine.Practice)] = typeof(SkillPractice),
            [nameof(ProgressionEngine.Learn)] = typeof(TechniqueLearning),
            [nameof(ProgressionEngine.Allocate)] = typeof(AttributeAllocation),
            [nameof(ProgressionEngine.Grant)] = typeof(AttributeGrant),
            [nameof(ProgressionEngine.Die)] = null,
            [nameof(ProgressionEngine.Create)] = null,
        };
        foreach (var method in AdvancingMethods())
        {
            Assert.True(expected.ContainsKey(method.Name), $"{method.Name} advances an axis but is not in the audited list");
            var currency = method.GetParameters().Select(p => p.ParameterType).SingleOrDefault(Currencies.Contains);
            Assert.Equal(expected[method.Name], currency);
        }
    }

    [Fact]
    public void TheCurrencies_CarryNothingButProgressionData()
    {
        // No currency record can smuggle in an item stack, a price, or another world object.
        foreach (var type in Currencies.Append(typeof(KillContext)))
        {
            foreach (var property in type.GetProperties())
            {
                var t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                bool plain = t.IsPrimitive || t == typeof(string) || t.Namespace == typeof(ProgressionEngine).Namespace;
                Assert.True(plain, $"{type.Name}.{property.Name} is a {t.FullName}");
            }
        }
    }

    [Fact]
    public void TheDomain_CannotSeeItemsOrGold()
    {
        // Items and inventories live in World; Domain references neither it nor anything above it.
        var references = typeof(ProgressionEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(references, n => n is "UNNAMED.World" or "UNNAMED.Persistence" or "UNNAMED.Content" or "UNNAMED.Application");
    }
}
