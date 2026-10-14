// UNNAMED Presentation - the skin of a character-standard body, drawn by HumanShaders (Phase B, character fidelity)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// HumanShaders by MatMADNESS (MIT + CC BY 4.0, pinned commit 95a54cff) applied to a character's SKIN surface, at
/// <c>res://Art/HumanShaders/</c>. Attribution, the upstream README and the full MIT licence sit beside the shader.
///
/// WHY THIS EXISTS RATHER THAN THE ENGINE'S OWN SUBSURFACE SCATTERING. CharacterMaterials used to give skin Godot's built-in
/// <c>SubsurfScatterStrength</c> and nothing else, and three separate blind reviews named the result: "diffuse and plasticky
/// without micro-texture", "no subsurface scattering", "a flat, uniform roughness that reads like matte toy plastic". Raising
/// that scalar from 0.22 to 0.75 measured only "marginally better" with the reviewer adding that neither frame escaped
/// "opaque game-engine plastic". HumanShaders implements what the same review asked for instead: <c>sss_mode_skin</c> with a
/// three-lobe wrapped diffuse driven by three mip levels of the normal map, a dual-lobe specular, and a depth-faded
/// subsurface term with a noise dither. That is a rendering feature, and it was already written and already used on a
/// character at an owner checkpoint - this file is the adapter, not a reimplementation.
///
/// The adapter is a port of the upstream folder's own <c>apply_human_skin.gd</c>, which is kept beside it for reference.
/// It copies what the imported material already carries and supplies flat stand-ins for what it does not: this project's
/// characters have an albedo, a roughness map (green channel, which is what the shader reads) and a normal map, and no
/// micro-detail, occlusion, translucency or SSS-noise map, so those features are switched OFF rather than fed a guess.
/// </summary>
public static class HumanSkin
{
    private const string ShaderPath = "res://Art/HumanShaders/skin_shader.gdshader";
    private const string LightwarpPath = "res://Art/HumanShaders/lightwarp_skin1.tres";

    private static Shader? _shader;
    private static Texture2D? _lightwarp;
    private static Texture2D? _white;
    private static Texture2D? _flatNormal;
    private static bool _tried;
    private static bool _reportedOcclusion;

    /// <summary>A 4x4 single-colour texture, for the maps the characters do not have.</summary>
    private static Texture2D Solid(Color colour)
    {
        var image = Image.Create(4, 4, false, Image.Format.Rgba8);
        image.Fill(colour);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Replace one skin surface with the HumanShaders material. False when the shader will not load or the surface is not
    /// a plain material, so the caller can fall back to the built-in response rather than draw nothing.
    /// </summary>
    public static bool Apply(MeshInstance3D mesh, int surface, BaseMaterial3D source)
    {
        // `--visual skin=engine` DRAWS THE SAME CHARACTER WITHOUT THIS SHADER, which is what makes the comparison
        // possible at all. Every blind A/B of it so far had to be run by moving the shader file aside, so the two
        // arms were never captured by the same build and the result could not be repeated - and the first attempt
        // was thrown out anyway because the head was 20-25 px and neither arm could be resolved. With a key, one
        // build draws both arms and the only difference between them is the shader. Never set in a shipping run.
        //
        // THE BISECT ARMS, added 2026-09-29. `human` and `engine` answer one bit - is the shader's OUTPUT different
        // from the engine's - and that bit came back 0 with no way to ask which term was responsible. Each extra
        // value below changes EXACTLY ONE SetShaderParameter, so a difference in the frame is attributable.
        string arm = VisualOptions.All.GetValueOrDefault("skin") ?? "human";
        if (arm == "engine")
            return false;
        if (!_tried)
        {
            _tried = true;
            _shader = ResourceLoader.Exists(ShaderPath) ? GD.Load<Shader>(ShaderPath) : null;
            _lightwarp = ResourceLoader.Exists(LightwarpPath) ? GD.Load<Texture2D>(LightwarpPath) : null;
            _white = Solid(Colors.White);
            _flatNormal = Solid(new Color(0.5f, 0.5f, 1f));
            if (_shader is null || _lightwarp is null)
                GD.PrintErr($"UNNAMED character materials: HumanShaders did not load ({ShaderPath}) - skin falls back to the engine's SSS");
            else
                GD.Print("UNNAMED character materials: HumanShaders skin shader loaded");
        }
        if (_shader is null || _lightwarp is null)
            return false;

        var m = new ShaderMaterial { ResourceName = (source.ResourceName ?? "skin") + "_HumanShaders", Shader = _shader };
        m.SetShaderParameter("albedo", source.AlbedoColor);
        m.SetShaderParameter("texture_albedo", source.AlbedoTexture ?? _white);
        m.SetShaderParameter("roughness", source.Roughness);
        m.SetShaderParameter("texture_roughness", source.RoughnessTexture ?? _white);
        m.SetShaderParameter("texture_normal",
            source.NormalEnabled && source.NormalTexture is not null ? source.NormalTexture : _flatNormal);
        m.SetShaderParameter("normal_strength", source.NormalEnabled ? source.NormalScale : 0f);
        m.SetShaderParameter("specular", source.MetallicSpecular);
        // Skin is a dielectric: no metallic map, and the shader multiplies the texture by this scalar.
        m.SetShaderParameter("metallic", 0f);
        m.SetShaderParameter("texture_metallic", _white);
        // Features this project has no map for are OFF, not fed a guess.
        m.SetShaderParameter("texture_micro_detail", _flatNormal);
        m.SetShaderParameter("micro_normal_strength", 0f);
        m.SetShaderParameter("use_micro_detail", false);
        m.SetShaderParameter("use_noise", false);
        m.SetShaderParameter("lightwarp", _lightwarp);
        m.SetShaderParameter("texture_translucency", _white);
        m.SetShaderParameter("translucency", false);
        m.SetShaderParameter("use_subsurface_scattering", true);
        // OCCLUSION, AND IT IS THE ONE INPUT THIS SHADER CANNOT DO WITHOUT. Three lines above it, in the fragment
        // function, the subsurface term is `sss_strength_value * texture(ambient_occlusion_texture, base_uv).r *
        // smooth_depth`. Fed a flat white that multiplier is 1.0 everywhere, so the subsurface response has no spatial
        // variation at all - a uniform lift, which is exactly what a blind review described as "still lacks believable
        // subsurface scattering" when this shader was first measured against the engine's.
        //
        // It was flat white because there WAS no occlusion map: measured out of the shipped GLB, `Human_skin`'s own
        // ORM atlas has R = 0.999 - the channel was never authored - while its G carries roughness and its B
        // metalness. The Ashen Hollow demo's characters carry a real `orm` whose R averages 0.63, and that difference
        // is the whole of it. Cairnwold's five now have an R baked from their own geometry by
        // `tools/asset_pipeline/cairnwold/bake_character_ao.py`, and it is wired into the GLB's occlusionTexture slot
        // by `patch_glb_texture.py --slot occlusion`.
        //
        // So this binds the model's own occlusion map when it HAS one and stays off when it does not - which is the
        // honest gate: a character that ships no AO keeps the old flat-white behaviour rather than being handed a
        // guess, exactly as the rest of this file does for micro-detail, translucency and the SSS noise.
        // THE OCCLUSION MAP IS THE TEXTURE ALREADY IN HAND, AND THAT IS NOT A SHORTCUT - IT IS THE ORM CONVENTION.
        // Godot's BaseMaterial3D describes `OrmTexture` as "a more efficient replacement of AOTexture,
        // RoughnessTexture and MetallicTexture ... Ambient occlusion is stored in the red channel. Roughness map is
        // stored in the green channel. Metallic map is stored in the blue channel." That is exactly how this
        // project's character atlases are authored, and exactly what `set_glb_orm.py` writes: R = the occlusion baked
        // from the character's own geometry, G = roughness, B = metallic, one image on both the metallicRoughness and
        // occlusion slots.
        //
        // Reaching for `AOEnabled`/`AOTexture`/`OrmTexture` on the imported material was tried first and DOES NOT
        // WORK here: the GLB declared both slots on one texture and the material still arrived with AO off, which the
        // diagnostic printed on both arms of an A/B - the map was in the file and the render never saw it. Reading the
        // roughness texture and letting the shader take R is the same data by a route that is known to arrive, because
        // `texture_roughness` is already being bound and the shader already proves it reads it (its G is the
        // roughness the whole surface uses).
        //
        // It stays honest at the edges: a model with no roughness texture gets the flat white and keeps the old
        // behaviour, and a model whose roughness map is a plain grayscale one gets an AO of nearly 1.0 - which is the
        // same as no AO, which is what such a model should get.
        Texture2D? orm = source.RoughnessTexture;
        m.SetShaderParameter("ambient_occlusion_texture", orm ?? _white);
        m.SetShaderParameter("use_ambient_occlusion", orm is not null);
        // Said out loud, once, because the difference this makes is a term inside a shader and a silent failure to
        // find the map would look exactly like a map that changes nothing.
        if (!_reportedOcclusion)
        {
            _reportedOcclusion = true;
            GD.Print(orm is not null
                ? $"UNNAMED character materials: skin occlusion bound from the ORM atlas ({orm.ResourceName}) - "
                  + "the subsurface term is scaled per texel"
                : "UNNAMED character materials: skin has no occlusion atlas - the subsurface term stays flat");
        }
        // ONE TERM OFF AT A TIME, everything else byte-identical to the `human` arm. Each case below names the
        // single uniform it changes, so a frame difference is attributable to that uniform and nothing else.
        switch (arm)
        {
            case "no-sss":
                // The term the shader exists for. `subsurface_scattering_strength` is NOT set by this file, so it
                // takes its declared default of 0.4 and the fragment's SSS_STRENGTH is 0.4*2 = 0.8 - but that value
                // is only ever READ by the engine's own light function, and this shader replaces light() wholesale
                // and never mentions SSS_STRENGTH. This arm measures whether that inference is right.
                m.SetShaderParameter("use_subsurface_scattering", false);
                break;
            case "no-ao":
                // Gates four things at once: the AO output, AO_LIGHT_AFFECT, the specular occlusion and the light
                // warp's AO term. It is the one term whose input (the ORM atlas's RED channel) this file had to
                // reach for deliberately, so it is the one most likely to be inert for a reason of its own.
                m.SetShaderParameter("use_ambient_occlusion", false);
                break;
            case "no-microdetail":
                // A NO-OP ARM, AND DELIBERATELY SO. `use_micro_detail` and `micro_normal_strength` are already
                // false/0 above, so this arm renders exactly the `human` frame. It is kept as an accepted value
                // because the bisect ladder asked for it and an arm that is missing reads as an arm that was not
                // run; a sibling round can confirm it is identical and learn that the ladder is wired correctly.
                break;
            case "lightwarp":
                // Swaps the three-lobe wrapped diffuse (three mip levels of the normal map, a mip selected by
                // linear depth) for the lightwarp ramp texture. Both branches write DIFFUSE_LIGHT inside light(),
                // so this is the largest change available without touching the fragment stage.
                m.SetShaderParameter("old_lightwarp_fallof", true);
                break;
            case "red":
                // THE MUST-CHANGE POSITIVE CONTROL, and the most important arm in the set. `albedo` multiplies the
                // albedo texture, so pure red makes every skin texel unmistakably red. If the frames of `red` and
                // `engine` are indistinguishable, then NO other arm in this ladder can mean anything, because the
                // ShaderMaterial is not reaching the rasteriser at all - and that, not a shader term, would be the
                // finding. (TRAP 10's identical-image control, inverted: prove the instrument can see a change
                // before believing it cannot.)
                m.SetShaderParameter("albedo", new Color(1f, 0f, 0f));
                break;
        }

        mesh.SetSurfaceOverrideMaterial(surface, m);
        return true;
    }
}
