using System.Collections.Immutable;
using UNNAMED.Domain.Items;
namespace UNNAMED.Domain.Crafting;

public sealed record SalvagePlan(string? RecipeId,int Difficulty,ImmutableArray<RecipeInput> Materials,int BatchSize);

/// <summary>Disassembly of authored construction materials; no inventory or random state.</summary>
public static class SalvageRules
{
    public const string SkillId="skill.salvage";

    public static SalvagePlan? Plan(ItemDefinition item,IEnumerable<RecipeDefinition> recipes,ItemCatalog catalog)
    {
        // Heritage is identity, never material (owner, 2026-10-24): it is never broken down, whatever else it is.
        if(item.Heritage is not null || item.NoDrop || item.Class is ItemClass.Notable or ItemClass.Artefact || item.Category == "quest" || item.Slot is null && item.Bag is null)return null;
        bool Material(string id)=>catalog.Find(id)?.Category is "material" or "component";
        foreach(var recipe in recipes.Where(x=>x.OutputItemId==item.Id && x.OutputCount>0).OrderBy(x=>x.Complexity).ThenBy(x=>x.Id,StringComparer.Ordinal))
        {
            var materials=recipe.Inputs.Where(x=>x.Count>0 && Material(x.ItemId)).OrderBy(x=>x.ItemId,StringComparer.Ordinal).ToImmutableArray();
            if(!materials.IsEmpty)return new(recipe.Id,Math.Max(1,recipe.Complexity),materials,recipe.OutputCount);
        }
        // Repair data identifies one recoverable supply unit when no construction recipe exists.
        return item.Repair is { } repair && Material(repair.MaterialId)
            ? new(null,Math.Max(1,repair.SkillMin),ImmutableArray.Create(new RecipeInput(repair.MaterialId,1)),1) : null;
    }

    public static int RecoveryPercent(int skill,ItemDefinition item,ItemWear? wear,CraftingConstants rules)
    {
        int competence=Math.Clamp(rules.SalvageBasePercent+Math.Max(0,skill)*rules.SalvagePercentPerSkill,0,rules.SalvageMaxPercent);
        int condition=item.DurabilityMax<=0?100:rules.SalvageBrokenPercent+(100-rules.SalvageBrokenPercent)*Durability.Remaining(wear,item.DurabilityMax)/item.DurabilityMax;
        return competence*condition/100;
    }

    public static ImmutableArray<RecipeInput> Recover(SalvagePlan plan,int percent,Func<int,double> sample)
    {
        var result=ImmutableArray.CreateBuilder<RecipeInput>();
        for(int i=0;i<plan.Materials.Length;i++)
        {
            var material=plan.Materials[i];double expected=(double)material.Count/plan.BatchSize*percent/100;
            int count=(int)Math.Floor(expected);if(sample(i)<expected-count)count++;
            if(count>0)result.Add(new(material.ItemId,count));
        }
        // A construction recipe always returns at least one unit of its main raw material.
        if (result.Count == 0 && percent > 0 && plan.RecipeId is not null)
            result.Add(new(plan.Materials.OrderByDescending(m => m.Count).ThenBy(m => m.ItemId, StringComparer.Ordinal).First().ItemId, 1));
        return result.ToImmutable();
    }
}
