using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Actual world stones/states. Only the before shot hides the added presentation.
    // Burst shots audition the exact travel effect; they do not claim a real journey.
    private async Task RunRcd8WaystoneReview(string directory,Camera3D camera)
    {
        _rcd3Driving=true;
        _avatar.Visible=false; // Explicit object-only visual fixture, with the normal world still running.
        _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
        var sim=_session.Simulation ?? throw new InvalidDataException("Waystone gate has no world");
        var evidence=new List<object>();
        foreach(var view in sim.Waystones)
        {
            var model=_hollow.PlacedModel(view.Site.Body.Id) ?? throw new InvalidDataException("Missing actual Waystone mesh: "+view.Site.Key);
            var glow=model.GetNode<WaystoneGlow>("WaystoneAmbient");
            var aim=model.ToGlobal(ArtGallery.Bounds(model).GetCenter());
            string id=view.Site.Key.Split('.').Last();
            foreach(float distance in new[]{5f,20f})
            {
                camera.GlobalPosition=aim+new Vector3(distance*.6f,1.2f,distance*.8f);
                camera.LookAt(aim);_shadowLods.Update(camera.GlobalPosition);
                if(view.Awake)
                {
                    glow.Visible=false;await Rcd3Wait(1);
                    await Rcd3Shot(directory,$"{id}_{distance:0}m_before");
                }
                glow.Visible=true;await Rcd3Wait(3);
                await Rcd3Shot(directory,$"{id}_{distance:0}m_{(view.Awake?"awake":"dormant")}");
                evidence.Add(new { stone=view.Site.Key,view.Awake,view.Known,distance,
                    camera=camera.GlobalPosition.ToString(),target=aim.ToString(),world_tick=sim.WorldTick,
                    before="Same camera/world; added glow hidden only",after="Actual authoritative awake state" });
                if(view.Awake && distance==5)
                {
                    TravelStonePulse(view.Site.Key,false);await Rcd3Wait(.12);
                    await Rcd3Shot(directory,id+"_departure_burst_fixture");await Rcd3Wait(1);
                    TravelStonePulse(view.Site.Key,true);await Rcd3Wait(.12);
                    await Rcd3Shot(directory,id+"_arrival_burst_fixture");await Rcd3Wait(1);
                }
            }
        }
        File.WriteAllText(Path.Combine(directory,"complete.json"),JsonSerializer.Serialize(new {
            status="WAYSTONE_WORLD_VISUALS_REQUIRE_CLAUDE_REVIEW",stones=evidence,
            travel_burst="Presentation audition only; actual TravelCommand journey gate remains pending",
            people=VisualOptions.People,engine_max_fps=Engine.MaxFps
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
