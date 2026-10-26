using Godot;
using System.Text.Json;
namespace UNNAMED.Presentation.Art;

/// <summary>Native higher-resolution maps on LOD0 only. Original mesh, UVs, rig and lower-LOD materials stay intact.</summary>
internal sealed class HumGenCloseupTextures
{
    private sealed record Map(MeshInstance3D Mesh, int Surface, string Part, string Role, string Path, Texture2D? Original);
    private readonly ArtLibrary _art;
    private readonly string _id;
    private readonly List<Map> _maps = new();
    private readonly Dictionary<Map, Texture2D> _resident = new();
    private HumGenCloseupTextures(ArtLibrary art, string id) { _art=art; _id=id; }
    public static HumGenCloseupTextures? Create(Node model, ArtLibrary art, string id)
    {
        if (OS.GetEnvironment("OTHERREACH_CLOSE_TEXTURES")=="off" || art.RiggedFile(id,"quality_4k/textures.json") is not {} manifest) return null;
        using var json=JsonDocument.Parse(File.ReadAllText(manifest));
        string body=art.RiggedFile(id,id+"_rigged.glb") ?? throw new InvalidDataException(id);
        if(json.RootElement.GetProperty("target_body_sha256").GetString()!=Godot.FileAccess.GetSha256(body))
            throw new InvalidDataException("Close-up maps target a different body: "+id);
        var result=new HumGenCloseupTextures(art,id);
        foreach(string part in new[]{"Body","Eyes"})
        {
            var mesh=model.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
                .Single(m=>m.Name.ToString().EndsWith("_"+part+"_LOD0",StringComparison.Ordinal));
            for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
            {
                if(mesh.Mesh.SurfaceGetMaterial(surface)is not BaseMaterial3D source)throw new InvalidDataException("Close-up source material missing");
                // Eyes otherwise share the imported material across instances. Skin already has a local HumanShaders material.
                if(part=="Eyes")mesh.SetSurfaceOverrideMaterial(surface,(Material)(mesh.GetSurfaceOverrideMaterial(surface)??source).Duplicate());
                foreach(var map in json.RootElement.GetProperty(part).EnumerateObject())
                {
                    string path=art.RiggedFile(id,"quality_4k/"+map.Value.GetString())??throw new InvalidDataException("Close-up source map missing");
                    Texture2D? original=map.Name switch {"color"=>source.AlbedoTexture,"normal"=>source.NormalTexture,"orm"=>source.RoughnessTexture,_=>throw new InvalidDataException("Unknown close-up role")};
                    result._maps.Add(new(mesh,surface,part,map.Name,path,original));
                }
            }
        }
        return result;
    }
    public void Update(bool near)
    {
        if(near && _resident.Count==0)
        {
            foreach(var map in _maps)_resident[map]=_art.ScopedTexture(map.Path,map.Part=="Body"?4096:1024);
            GD.Print("UNNAMED close textures: "+_id+", native4K skin/1K eyes, LOD0 only");
        }
        if(_resident.Count==0)return;
        foreach(var map in _maps)
        {
            Texture2D? value=near?_resident[map]:map.Original;
            var current=map.Mesh.GetSurfaceOverrideMaterial(map.Surface)??map.Mesh.Mesh.SurfaceGetMaterial(map.Surface);
            Apply(current,map,value);
            // Creator colour choices clone this template; restore it too when releasing the near maps.
            string key="look_base_"+map.Surface;
            if(map.Mesh.HasMeta(key))Apply(map.Mesh.GetMeta(key).As<Material>(),map,value);
        }
        if(!near)
        {
            foreach(var texture in _resident.Values)texture.Dispose();
            _resident.Clear();
            GD.Print("UNNAMED close textures: "+_id+", released; original2K lower LODs");
        }
    }
    private void Apply(Material material,Map map,Texture2D? value)
    {
        if(map.Part=="Body" && material is ShaderMaterial skin && skin.ResourceName.EndsWith("_HumanShaders",StringComparison.Ordinal))
        {
            string key=map.Role switch {"color"=>"texture_albedo","normal"=>"texture_normal",_=>"texture_roughness"};
            var current=skin.GetShaderParameter(key).AsGodotObject();
            // A people's own colour overlay wins over the plain body's native colour map.
            if(map.Role=="color" && current!=map.Original && current!=_resident[map])return;
            if(current!=value)skin.SetShaderParameter(key,value);
        }
        else if(map.Part=="Eyes" && material is BaseMaterial3D eye)
        {
            if(map.Role=="color")eye.AlbedoTexture=value;
            else eye.NormalTexture=value;
        }
    }
}
