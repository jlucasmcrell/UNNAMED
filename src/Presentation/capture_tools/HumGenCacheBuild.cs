using Godot;
using UNNAMED.Presentation.Art;
namespace UNNAMED.Presentation;

// Offline build of the existing cache. No world or GPU rendering.
public partial class HumGenCacheBuild : Node
{
    public override void _Ready()
    {
        try
        {
            var root=OS.GetEnvironment("OTHERREACH_HUMGEN_CACHE_ROOT");
            if(string.IsNullOrWhiteSpace(root))throw new InvalidDataException("OTHERREACH_HUMGEN_CACHE_ROOT is required");
            ArtLibrary.BuildTextureCache=true;
            var art=new ArtLibrary(root,useTextureCache:true);
            foreach(var sex in new[]{"female","male"})
            {
                string id="humgen_fidelity_"+sex+"_caucasian2";
                var model=art.Rigged(id)??throw new InvalidDataException(id);
                CharacterMaterials.Upgrade(model,out _,new Dictionary<string,HumanEye.Maps>(),nativeHumGen:true);
                HumGenMaterials.Apply(model,art,id);
                HumGenMouth.Apply(model,art,id);
                model.Free();
                GD.Print("NATIVE_CACHE_BUILT "+id);
            }
            art.Cache.Save();GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
}
