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
            bool creator = OS.GetEnvironment("OTHERREACH_HUMGEN_CREATOR") == "1";
            // A root need not hold every body: this builds the entries for what it does hold, so a region root can be
            // completed on its own (B-013).
            foreach(var sex in new[]{"female","male"})
            {
                string id=creator ? "humgen_creator_" + sex : "humgen_fidelity_"+sex+"_caucasian2";
                if (art.Rigged(id) is not { } model)
                {
                    GD.PrintErr("NATIVE_CACHE_SKIPPED "+id);
                    continue;
                }
                CharacterMaterials.Upgrade(model,out _,new Dictionary<string,HumanEye.Maps>(),nativeHumGen:true);
                HumGenMaterials.Apply(model,art,id);
                HumGenMouth.Apply(model,art,id);
                model.Free();
                GD.Print("NATIVE_CACHE_BUILT "+id);
            }
            if (creator)
            {
                string sets = Path.Combine(root, "rigged", "humgen_creator_materials");
                foreach (string path in Directory.Exists(sets) ? Directory.EnumerateFiles(sets, "*.png", SearchOption.AllDirectories) : [])                {
                    string role = path.EndsWith("_BaseColor.png", StringComparison.Ordinal) ? "color"
                        : path.EndsWith("_Normal.png", StringComparison.Ordinal) ? "normal" : "data";
                    if (art.Texture(path, role) is null) throw new InvalidDataException("Native preset cache failed: " + path);
                }
                // Every creator PART's own maps: the hair-flow LODs and the rest of what HumGenMaterials resolves through
                // native_materials.json. No creator gate built these, so a package went out without them and refused to
                // boot the first time a part was worn (B-013: 219 >=2K maps in the shipped Test 02 package).
                foreach (string part in Directory.EnumerateDirectories(Path.Combine(root, "rigged")))
                {
                    if (!File.Exists(Path.Combine(part, "native_materials.json")))
                        continue;
                    string id = Path.GetFileName(part);
                    if (art.Rigged(id) is not { } model)
                    {
                        GD.PrintErr("NATIVE_CACHE_SKIPPED " + id);
                        continue;
                    }
                    HumGenMaterials.Apply(model, art, id);
                    model.Free();
                }
            }
            art.Cache.Save();GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
}
