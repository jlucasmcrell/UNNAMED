namespace UNNAMED.Domain.Spatial;

/// <summary>Clear, dry ground for a fire, bedroll and space to stand beside them.</summary>
public static class CampRules
{
    public const long RadiusMm = 3_000;
    public static string? Placement(RegionLayout layout,long x,long z,IEnumerable<(long X,long Z)> towns)
    {
        if(!layout.SpaceId.IsExterior) return "Camps belong outdoors";
        var space=layout.Space;
        if(x-RadiusMm<space.MinXMm || x+RadiusMm>space.MaxXMm || z-RadiusMm<space.MinZMm || z+RadiusMm>space.MaxZMm)
            return "There is not enough ground here";
        if(towns.Any(t=>Math.Pow(t.X-x,2)+Math.Pow(t.Z-z,2)<100_000d*100_000)) return "Camp outside the town";
        foreach(var road in layout.Roads)
            for(int i=1;i<road.Points.Length;i++)
            {
                var a=road.Points[i-1]; var b=road.Points[i];
                double dx=b.XMm-a.XMm,dz=b.ZMm-a.ZMm;
                double t=Math.Clamp(((x-a.XMm)*dx+(z-a.ZMm)*dz)/Math.Max(1,dx*dx+dz*dz),0,1);
                if(Math.Pow(x-a.XMm-t*dx,2)+Math.Pow(z-a.ZMm-t*dz,2)<6_000d*6_000) return "Keep the road clear";
            }
        if(space.Blockers.Any(b=>b.DistanceTo(x,z)<RadiusMm)) return "Find a clear patch of ground";
        long height=space.Terrain.HeightAtMm(x,z);
        if(height<350) return "Find dry ground above the water";
        foreach(long dx in new long[] {-RadiusMm,0,RadiusMm})
            foreach(long dz in new long[] {-RadiusMm,0,RadiusMm})
                if(Math.Abs(space.Terrain.HeightAtMm(x+dx,z+dz)-height)>350 || space.HeightAtMm(x+dx,z+dz)!=space.Terrain.HeightAtMm(x+dx,z+dz))
                    return "Find level ground away from buildings";
        return null;
    }
}
