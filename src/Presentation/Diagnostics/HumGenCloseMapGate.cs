using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
namespace UNNAMED.Presentation.Diagnostics;
public partial class HumGenCloseMapGate : Node3D
{
 public override void _Ready()
 {
  try {
   string root=OS.GetCmdlineUserArgs()[0];OS.SetEnvironment("OTHERREACH_CHARACTER_ROOT",Path.Combine(root,"humgen_characters"));
   VisualOptions.Parse("tier=medium,people=humgen,textures=cache");
   var camera=new Camera3D{Current=true};AddChild(camera);var art=new ArtLibrary(Path.Combine(root,"primary"),true);var rows=new List<object>();
   int Width(SkinnedModel figure,string part) {
    var mesh=figure.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>().Single(m=>m.Name.ToString().EndsWith("_"+part+"_LOD0",StringComparison.Ordinal));
    var material=mesh.GetSurfaceOverrideMaterial(0)??mesh.Mesh.SurfaceGetMaterial(0);
    return (material is ShaderMaterial skin?skin.GetShaderParameter("texture_albedo").As<Texture2D>():((BaseMaterial3D)material).AlbedoTexture).GetWidth();
   }
   foreach(string id in new[]{"humgen_player_ada_f","humgen_player_mateo_m"}) {
    var figure=SkinnedModel.Create(art,id,new Dictionary<string,string>(),"player")??throw new InvalidDataException(id);AddChild(figure);figure.SetProcess(false);
    var npc=SkinnedModel.Create(art,id,new Dictionary<string,string>(),"npc_quality_probe")??throw new InvalidDataException(id);AddChild(npc);npc.SetProcess(false);
    for(int cycle=0;cycle<2;cycle++) {
     camera.GlobalPosition=new Vector3(0,0,3);figure._Process(1.0/60);npc._Process(1.0/60);
     if(Width(figure,"Body")!=4096||Width(figure,"Eyes")!=1024||Width(npc,"Body")!=2048||Width(npc,"Eyes")!=512)throw new InvalidDataException("Near/NPC resolution policy failed");
     figure.ProcessMode=Node.ProcessModeEnum.Disabled;
     if(Width(figure,"Body")!=2048||Width(figure,"Eyes")!=512)throw new InvalidDataException("Disabled creator retained close maps");
     figure.ProcessMode=Node.ProcessModeEnum.Inherit;figure._Process(1.0/60);
     if(Width(figure,"Body")!=4096)throw new InvalidDataException("Reopened creator did not restore close maps");
     camera.GlobalPosition=new Vector3(0,0,80);figure._Process(1.0/60);
     if(Width(figure,"Body")!=2048||Width(figure,"Eyes")!=512)throw new InvalidDataException("Far maps were not restored");
    }
    rows.Add(new{id,near_skin=4096,near_eyes=1024,far_skin=2048,far_eyes=512,npc_skin=2048,npc_eyes=512,cycles=2,disabled_creator_restores_original_maps=true});npc.Free();figure.Free();
   }
   File.WriteAllText(OS.GetCmdlineUserArgs()[1],JsonSerializer.Serialize(new{status="NATIVE_CLOSE_FAR_AND_NPC_POLICY_PASS",rows},new JsonSerializerOptions{WriteIndented=true}));GD.Print("HUMGEN_CLOSE_MAP_GATE_PASS 2");GetTree().Quit();
  } catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
 }
}
