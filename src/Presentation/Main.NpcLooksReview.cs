using Godot;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>
    /// Opt-in NPC-looks review (OTHERREACH_NPC_LOOKS_PLAN): every NPC of a town, each face framed at the plan's distance, height and field
    /// of view in daylight with no HUD, plus the plan's wide shots - taken from the game's own live figures and its own content, so a sheet
    /// shows the look that ships and not a rehearsal. One folder per town under the review directory holding the frames, the wide shots and
    /// an <c>npc_looks.json</c> the contact-sheet builder reads (id, name, people, sex, role); <c>complete.json</c> when a town's frames are
    /// all written, and again at the top when every town is.
    /// </summary>
    private async Task RunNpcLooksReview(string directory, Camera3D camera)
    {
        using var plan = JsonDocument.Parse(File.ReadAllText(OS.GetEnvironment("OTHERREACH_NPC_LOOKS_PLAN")));
        var root = plan.RootElement;
        float dist = Num(root, "dist", 2f), height = Num(root, "height", 1.62f), fov = Num(root, "fov", 32f);
        int wides = Math.Max(0, (int)Num(root, "wides", 3f));
        bool fullBody = root.TryGetProperty("full_body", out var full) && full.GetBoolean();
        DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(directory));
        if(root.TryGetProperty("capture_pixels",out var pixels))
        {
            GetWindow().Mode=Window.ModeEnum.Windowed;
            GetWindow().Size=new Vector2I(pixels[0].GetInt32(),pixels[1].GetInt32());
        }
        _hud.Visible = false;
        // The player's own body stands in these frames otherwise, and a face sheet is about the town's people.
        _avatar.Visible = false;
        var simulation = _session.Simulation ?? throw new InvalidDataException("The NPC looks review needs a live world");
        int paced = 0;
        if (root.TryGetProperty("hour", out var hour))
            VisualOptions.Pin("hour", hour.GetSingle().ToString(CultureInfo.InvariantCulture));
        var (catalogue, data) = CreatorLook();
        var towns = new List<string>();
        var framed = new HashSet<string>(StringComparer.Ordinal);
        async Task Frames(double seconds, Action? each = null)
        {
            ulong until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
            do
            {
                each?.Invoke();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            } while (Time.GetTicksMsec() < until);
        }
        // The hour has to reach the sky and the light before the first frame is read, as the race-look review lets it.
        await Frames(2);
        // Freeze Main's normal draw loop for this presentation-only fixture. Otherwise NpcsView.Draw
        // restores every live actor's transform and animation between staging it and reading pixels.
        SetProcess(false);
        if(fullBody && _scatter is not null)_scatter.Visible=false; // Opt-in outfit fixture: expose feet and hems.
        var reviewFigures=_npcs.GetChildren().OfType<Figure>().Select(figure=>(Figure:figure,Visible:figure.Visible)).ToArray();
        foreach (var town in root.GetProperty("towns").EnumerateArray())
        {
            string settlement = town.GetString()!;
            string folder = Path.Combine(directory, settlement);
            Directory.CreateDirectory(folder);
            var (roster, residents) = TownRoster(simulation.Setup, settlement, catalogue, data);
            var rows = new List<object>();
            var poseSamples=new List<object>();
            var missing = new List<string>();
            var feet = new List<Vector3>();
            foreach (var person in roster)
            {
                if (root.TryGetProperty("only",out var only) && !only.EnumerateArray().Any(item=>item.GetString()==person.Id))continue;
                framed.Add(person.Id);
                // The look exactly as the game draws it: the cast's own authored record, or the generated one their slot's place in the
                // roster assigns. Reporting it beside the frame is the point - a caption cannot drift from the face.
                var worn = person.Routine.IsSlot
                    ? residents.GetValueOrDefault(person.Routine.Id)
                    : person.Definition is { } definition
                        ? NpcLooks.Of(definition, settlement, 0, definition.PeopleId ?? person.Routine.PeopleId,
                            definition.Sex ?? person.Routine.Sex, catalogue, data, 0)
                        : null;
                string? people = person.Definition?.PeopleId ?? person.Routine.PeopleId;
                string? sex = person.Definition?.Sex ?? person.Routine.Sex;
                var figure = _npcs.FigureOf(person.Id) as Figure;
                if (figure is null || !figure.Visible)
                {
                    // A person the world does not draw here is said so rather than photographed as empty street: the frame would look
                    // exactly like an NPC standing out of shot.
                    missing.Add(person.Id);
                    GD.PushError($"NPC_LOOKS {settlement}: {person.Id} is not drawn, so it has no frame");
                    continue;
                }
                var originalTransform=figure.GlobalTransform;
                if(root.TryGetProperty("clear_stage",out var clearStage) && clearStage.GetBoolean())
                {
                    foreach(var other in reviewFigures)other.Figure.Visible=other.Figure==figure;
                    figure.Visible=true;
                    Vector2 stage=root.TryGetProperty("stage",out var stageAt)
                        ?new Vector2(stageAt[0].GetSingle(),stageAt[1].GetSingle())
                        :settlement=="ashen_hollow"?new Vector2(426,754):new Vector2(949,695);
                    float floor=_ground.Height(stage.X,stage.Y);
                    figure.GlobalPosition=new Vector3(stage.X,floor,stage.Y);
                    if(fullBody && FindChildren("*",nameof(DirectionalLight3D),true,false).OfType<DirectionalLight3D>().FirstOrDefault(light=>light.Name.ToString().Contains("Sun",StringComparison.OrdinalIgnoreCase)) is {} stageSun)
                    {
                        var lit=stageSun.GlobalBasis.Z;
                        figure.GlobalRotation=new Vector3(0,Mathf.Atan2(lit.X,lit.Z)+Mathf.DegToRad(20),0);
                    }
                }
                feet.Add(figure.GlobalPosition);
                // Two metres from the eyes, level with them: along the line to the player where the game has them looking at the player
                // (the head turns within six metres), and along the way they face where it does not.
                Vector3 aim = figure.Head ?? figure.GlobalPosition + Vector3.Up * height;
                if (fullBody) aim=figure.GlobalPosition+Vector3.Up*((worn?.StatureMm??1700)/1000f*.5f);
                void Face()
                {
                    Vector3 towards = _npcs.PlayerHead.GlobalPosition.DistanceTo(aim) < 6f
                        ? _npcs.PlayerHead.GlobalPosition - aim
                        : figure.GlobalBasis.Z;
                    if(fullBody && FindChildren("*",nameof(DirectionalLight3D),true,false).OfType<DirectionalLight3D>().FirstOrDefault(light=>light.Name.ToString().Contains("Sun",StringComparison.OrdinalIgnoreCase)) is {} sun)
                        towards=sun.GlobalBasis.Z;
                    towards.Y = 0;
                    if (towards.LengthSquared() <= .0001f)
                        towards = Vector3.Back;
                    // An NPC who works indoors: the first direction can put the camera inside timber, so turn around the head until
                    // the line from the head to the camera is clear. A frame with no wall in it is the only evidence worth keeping.
                    var space = GetWorld3D().DirectSpaceState;
                    for (int turn = 0; turn < 8; turn++)
                    {
                        float yaw = Mathf.Pi / 4f * turn;
                        var direction = new Basis(Vector3.Up, yaw) * towards.Normalized();
                        var at = aim + direction * dist;
                        if(fullBody)at.Y+=.65f;
                        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(aim, at, 1));
                        if (hit.Count == 0)
                        {
                            camera.Fov = fov;
                            camera.GlobalPosition = at;
                            camera.LookAt(aim);
                            return;
                        }
                    }
                    camera.Fov = fov;
                    camera.GlobalPosition = aim + towards.Normalized() * dist;
                    camera.LookAt(aim);
                }
                Face();
                // A fixture teleport must leave the Orenth's .4-second registration history before its portrait.
                await Frames(people == PeopleLooks.Orenth ? .6 : .2, Face);
                await Rcd3Shot(folder, person.Id);
                if(root.TryGetProperty("diagnose_layers",out var diagnose) && diagnose.GetBoolean())
                {
                    var saved=figure.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
                        .Select(mesh=>(Mesh:mesh,Visible:mesh.Visible)).ToArray();
                    foreach(string variant in new[]{"no_lining","no_body","neither"})
                    {
                        foreach(var item in saved)
                        {
                            bool lining=item.Mesh.Name.ToString().EndsWith("_lining",StringComparison.Ordinal);
                            bool skin=!item.Mesh.HasMeta("creator_part_kind");
                            item.Mesh.Visible=item.Visible && !(lining && variant!="no_body") && !(skin && variant!="no_lining");
                        }
                        await Frames(.1,Face);
                        await Rcd3Shot(folder,person.Id+"_diagnostic_"+variant);
                    }
                    foreach(var item in saved)item.Mesh.Visible=item.Visible;
                }
                if (OS.GetEnvironment("OTHERREACH_FACE_SHEET") is { Length: > 0 } && paced < 2)
                {
                    // One man and one woman of the town, so the sheet shows the library on both faces.
                    paced++;
                    foreach (string expression in Art.FaceExpression.Sheet)
                    {
                        foreach (var face in figure.FindChildren("*", "", true, false).OfType<Art.FaceDriver>())
                            face.Express(expression, 1f);
                        camera.GlobalPosition = aim + (camera.GlobalPosition - aim).Normalized() * Mathf.Min(dist, 1.1f);
                        camera.LookAt(aim);
                        await Frames(.6, null);
                        await Rcd3Shot(folder, person.Id + "_face_" + expression);
                        foreach (var face in figure.FindChildren("*", "", true, false).OfType<Art.FaceDriver>())
                            face.Express(null);
                    }
                    Face();
                    await Frames(.2, Face);
                }
                if (fullBody && people==PeopleLooks.Kal)
                {
                    Vector3 torso=figure.GlobalPosition+Vector3.Up*((worn?.StatureMm??1700)/1000f*.67f);
                    foreach (var side in new[]{("chest",1f),("back",-1f)})
                    {
                        camera.Fov=40;
                        camera.GlobalPosition=torso+figure.GlobalBasis.Z*side.Item2*.9f;
                        camera.LookAt(torso);
                        await Frames(.2);
                        await Rcd3Shot(folder,person.Id+"_"+side.Item1);
                    }
                    // A centre-back close-up fills with the wings. Keep it, plus a wider rear
                    // three-quarter frame that exposes the garment around their roots.
                    camera.Fov=43;
                    camera.GlobalPosition=torso-figure.GlobalBasis.Z*1.8f+figure.GlobalBasis.X+Vector3.Up*.35f;
                    camera.LookAt(torso);
                    await Frames(.2);
                    await Rcd3Shot(folder,person.Id+"_back_context");
                }
                if (root.TryGetProperty("motion_clips",out var clips))
                    foreach(var clip in clips.EnumerateArray())
                    {
                        var model=ArtLibrary.Find<SkinnedModel>(figure)??throw new InvalidDataException("Review body is not skinned");
                        string state=clip.GetString()!;
                        if(!model.Has(state))throw new InvalidDataException("NPC review motion unavailable: "+state);
                        var staged=figure.GlobalTransform;
                        figure.SetRestPose(state);
                        figure.SetPosture(state=="crouch_idle",false);
                        figure.Pose(staged.Origin,staged.Basis.GetEuler().Y,0,.25);
                        figure.GlobalTransform=staged;
                        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePreDraw);
                        model.Hold(state,.45f,0);model.Skeleton.Advance(0);
                        Face();
                        await Rcd3Shot(folder,person.Id+"_motion_"+clip.GetString());
                        poseSamples.Add(new {id=person.Id,requested=state,actual=model.Current,phase=.45f,
                            bone_origins=Enumerable.Range(0,model.Skeleton.GetBoneCount()).Select(index=>{
                                var point=model.Skeleton.GetBoneGlobalPose(index).Origin;
                                return new[]{point.X,point.Y,point.Z};}).ToArray()});
                    }
                figure.SetRestPose(null);
                if(ArtLibrary.Find<SkinnedModel>(figure) is {} restored)
                { restored.Hold("idle",.3f,0);restored.Skeleton.Advance(0); }
                rows.Add(new
                {
                    id = person.Id,
                    name = person.Definition?.Name ?? person.Routine.Role,
                    people = people ?? "",
                    sex = sex ?? "",
                    role = person.Definition?.Role ?? person.Routine.Role,
                    stature_cm = worn is null ? null : (float?)worn.StatureMm / 10f,
                    face = worn?.Face, hair = worn?.Hair, facial_hair = worn?.FacialHair, outfit = worn?.Outfit, footwear = worn?.Footwear,
                    sliders = worn?.Sliders.Count ?? 0,
                    // What was actually drawn, not what was asked for: a person left on a native body says so here.
                    model = figure is SkinnedFigure skinned ? skinned.ModelId
                        : figure is MHCharacters.MetaHumanFigure native ? "mh:" + native.CharacterId : figure.GetType().Name,
                    stature_scale = figure is SkinnedFigure scaled ? scaled.StatureScale : 1f,
                    original_position=originalTransform.Origin.ToString(),review_position=figure.GlobalPosition.ToString(),
                    garment_materials=figure.FindChildren("*",nameof(MeshInstance3D),true,false).OfType<MeshInstance3D>()
                        .Where(mesh=>mesh.IsVisibleInTree() && mesh.Mesh is not null && mesh.GetMeta("creator_part_kind","").AsString() is "Outfit" or "Footwear")
                        .SelectMany(mesh=>Enumerable.Range(0,mesh.Mesh!.GetSurfaceCount()).Select(surface=>new {
                            mesh=mesh.Name.ToString(),surface,grow=(mesh.GetSurfaceOverrideMaterial(surface) as BaseMaterial3D)?.Grow,
                            grow_amount=(mesh.GetSurfaceOverrideMaterial(surface) as BaseMaterial3D)?.GrowAmount})).ToArray(),
                });
                figure.GlobalTransform=originalTransform;
                foreach(var other in reviewFigures)other.Figure.Visible=other.Visible;
                GD.Print($"NPC_LOOKS {settlement} face {person.Id}: {people} {sex}, {worn?.Face ?? "(no record)"}");
            }
            var reached = await WideShots(camera, folder, settlement, feet, wides, Frames);
            File.WriteAllText(Path.Combine(folder,"pose_samples.json"),JsonSerializer.Serialize(poseSamples));
            File.WriteAllText(Path.Combine(folder, "npc_looks.json"), JsonSerializer.Serialize(new
            {
                town = settlement, dist, height, fov, full_body=fullBody, hour = VisualOptions.Hour,
                fixture_ground_y=root.TryGetProperty("stage",out var groundAt)?_ground.Height(groundAt[0].GetSingle(),groundAt[1].GetSingle()):(float?)null,
                foreground_scatter_hidden=fullBody,
                npcs = rows, wide = reached, missing,
            }, new JsonSerializerOptions { WriteIndented = true }));
            // A town's marker is written only once its frames and its manifest are on disk, so a reader never sees half a town.
            File.WriteAllText(Path.Combine(folder, "complete.json"), JsonSerializer.Serialize(new
            {
                town = settlement, npcs = rows.Count, missing = missing.Count, wides = reached.Count,
            }));
            GD.Print($"NPC_LOOKS {settlement} captured {rows.Count} faces, {reached.Count} wides, {missing.Count} not drawn");
            towns.Add(settlement);
        }
        // Anything the world draws that no town in the plan named - an adventurer, a person whose routine lives elsewhere, a roster
        // this plan forgot. Said out loud, because a sheet of two towns shows what it framed and cannot show what it left out.
        var unlisted = _npcs.DrawnIds.Where(id => !framed.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (unlisted.Length > 0)
            GD.Print("NPC_LOOKS drawn but in no planned town: " + string.Join(", ", unlisted));
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
        {
            status = "NPC_LOOKS_CAPTURED_REQUIRES_REVIEW", towns, people = VisualOptions.People, unlisted,
        }));
    }

    /// <summary>
    /// A town's people from content rather than from a list: every routine that lives there, the named NPC it places or the role slot its
    /// residents hold (<see cref="RoutineDefinition.NpcId"/>, <see cref="RoutineDefinition.IsSlot"/>), and the looks the slots' residents
    /// wear, which <see cref="NpcLooks.Residents"/> assigns by the roster's own ordinal order.
    /// </summary>
    private static (List<(string Id, RoutineDefinition Routine, NpcDefinition? Definition)> Roster,
        ImmutableDictionary<string, CharacterAppearance> Residents) TownRoster(
        SimulationSetup setup, string settlement, Ui.Models.CreatorCatalogue catalogue, Ui.Models.HumGenCreatorData data)
    {
        var roster = setup.NpcLife.Routines.Values.Where(r => r.Settlement == settlement).OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => (Id: r.NpcId ?? r.Id, Routine: r, Definition: setup.Social.Npcs.GetValueOrDefault(r.NpcId ?? r.Id))).ToList();
        // A slot content never sexed draws nobody, and says so by its absence here rather than by a look no other call would make.
        var slots = roster.Where(p => p.Routine is { IsSlot: true, Sex: not null, PeopleId: not null })
            .Select(p => (SlotId: p.Routine.Id, People: p.Routine.PeopleId!, Sex: p.Routine.Sex!));
        return (roster, NpcLooks.Residents(slots, settlement, catalogue, data));
    }

    /// <summary>
    /// The plan's wide shots of a town: the camera swung round the people it just framed, far enough back to hold all of them, at eye
    /// height above the ground they stand on. What was framed is written down with the frames, because a wide nobody can locate is
    /// evidence of nothing.
    /// </summary>
    private async Task<List<object>> WideShots(Camera3D camera, string folder, string settlement, List<Vector3> feet, int wides,
        Func<double, Action?, Task> frames)
    {
        var shots = new List<object>();
        if (feet.Count == 0 || wides == 0)
            return shots;
        // The median person's neighbourhood: one NPC working a site far from the village must not drag the frame off the rest.
        var ordered = feet.OrderBy(at => at.X).ThenBy(at => at.Z).ToList();
        Vector3 median = ordered[ordered.Count / 2];
        var cluster = feet.Where(at => at.DistanceTo(median) <= 30f).ToList();
        Vector3 centre = Vector3.Zero;
        foreach (var at in cluster)
            centre += at;
        centre /= cluster.Count;
        var ground = _npcs.Ground ?? ((x, z) => centre.Y);
        float reach = Mathf.Clamp(cluster.Max(at => at.DistanceTo(centre)) + 6f, 8f, 18f);
        var space = GetWorld3D().DirectSpaceState;
        for (int shot = 0; shot < wides; shot++)
        {
            Vector3 aim = centre + Vector3.Up * 1.2f;
            Vector3 at = aim;
            for (float bearing = 0; bearing < Mathf.Tau; bearing += Mathf.Pi / 4f)
            {
                for (float try_ = reach; try_ >= 5f; try_ -= 1.5f)
                {
                    var candidate = centre + new Basis(Vector3.Up, Mathf.Tau * shot / wides + bearing) * new Vector3(0, 0, try_);
                    candidate.Y = ground(candidate.X, candidate.Z) + 2.0f;
                    if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(aim, candidate, 1)).Count > 0)
                        continue;
                    at = candidate;
                    break;
                }
                if (at != aim)
                    break;
            }
            camera.Fov = 60;
            camera.GlobalPosition = at;
            camera.LookAt(aim);
            string name = $"wide_{shot + 1}";
            await frames(.3, null);
            await Rcd3Shot(folder, name);
            shots.Add(new
            {
                name, at = new[] { at.X, at.Y, at.Z }, aim = new[] { aim.X, aim.Y, aim.Z }, fov = camera.Fov,
                people = feet.Count, centre = new[] { centre.X, centre.Y, centre.Z },
            });
            GD.Print($"NPC_LOOKS {settlement} wide {name} from ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0})");
        }
        return shots;
    }

    private static float Num(JsonElement plan, string key, float fallback) => plan.TryGetProperty(key, out var value) ? value.GetSingle() : fallback;

    private (Ui.Models.CreatorCatalogue Catalogue, Ui.Models.HumGenCreatorData Data)? _reviewCreator;

    /// <summary>The creator's catalogue and slider data, which a look is held to and generated from (the same file the game reads).</summary>
    private (Ui.Models.CreatorCatalogue Catalogue, Ui.Models.HumGenCreatorData Data) CreatorLook()
    {
        if (_reviewCreator is { } read)
            return read;
        string json = File.ReadAllText(ProjectSettings.GlobalizePath("res://Art/humgen_creator.json"));
        return (_reviewCreator = (Ui.Models.CreatorCatalogue.Read(json), new Ui.Models.HumGenCreatorData(json))).Value;
    }
}
