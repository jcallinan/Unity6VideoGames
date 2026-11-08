using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using static SkyfortModels;

// Builds the Skyfort Strike scene hierarchy and prefabs, saving their procedural meshes and materials as real
// assets, so the whole layout can be inspected and tweaked in the Editor. At runtime the game only streams the
// level; everything else is already in the scene.
//
// Menu: Skyfort > Rebuild Scene and Prefabs
// Batch: Unity.exe -batchmode -quit -projectPath <project> -executeMethod SkyfortSceneBuilder.Rebuild
public static class SkyfortSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/SkyfortStrike.unity";
    private const string ArtFolder = "Assets/Art";
    private const string MeshFolder = "Assets/Art/Meshes";
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string UiFolder = "Assets/UI";

    [MenuItem("Skyfort/Rebuild Scene and Prefabs")]
    public static void RebuildFromMenu()
    {
        var message = $"This regenerates {ScenePath}, the prefabs in {PrefabFolder} and the generated meshes and materials in {ArtFolder}. Manual edits to those will be replaced.";
        if (!EditorUtility.DisplayDialog("Rebuild Skyfort Strike", message, "Rebuild", "Cancel"))
        {
            return;
        }

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Rebuild();
        }
    }

    public static void Rebuild()
    {
        ResetCaches();
        AssetDatabase.DeleteAsset(ArtFolder);
        AssetDatabase.DeleteAsset(PrefabFolder);
        EnsureFolder(ArtFolder);
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder(UiFolder);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Prefabs spawned while the level streams.
        var turret = SavePrefab(BuildTurret(), "Turret");
        var fuelTank = SavePrefab(BuildFuelTank(), "Fuel Tank");
        var radar = SavePrefab(BuildRadar(), "Radar Dish");
        var drone = SavePrefab(BuildDrone(), "Drone");
        var laser = SavePrefab(BuildLaser(), "Laser");
        var bolt = SavePrefab(BuildBolt(), "Enemy Bolt");
        var forceField = SaveMaterial(new Material(Mat(NeonGreen, true)) { name = "Force Field" });
        var repairPickup = SavePrefab(BuildRepairPickup(), "PowerUp Repair");
        var fuelPickup = SavePrefab(BuildFuelPickup(), "PowerUp Fuel");
        var shieldPickup = SavePrefab(BuildShieldPickup(), "PowerUp Shield");
        var spreadPickup = SavePrefab(BuildSpreadPickup(), "PowerUp Spread");
        var extraShipPickup = SavePrefab(BuildExtraShipPickup(), "PowerUp Extra Ship");

        // Scene hierarchy.
        var game = new GameObject("Skyfort Strike").AddComponent<SkyfortStrike>();
        var audio = Group("Audio", game.transform, Vector3.zero);
        var sfx = new GameObject("SFX").AddComponent<AudioSource>();
        sfx.transform.SetParent(audio, false);
        sfx.playOnAwake = false;
        sfx.volume = 0.8f;
        var engine = new GameObject("Engine Loop").AddComponent<AudioSource>();
        engine.transform.SetParent(audio, false);
        engine.playOnAwake = false;
        engine.loop = true;
        engine.volume = 0.25f;

        var view = BuildCamera();
        var keyLight = BuildKeyLight();
        var flash = new GameObject("Explosion Flash").AddComponent<Light>();
        flash.type = LightType.Point;
        flash.range = 9f;
        flash.intensity = 0f;

        var ship = BuildShip(out var shipModel, out var engineTrail);
        var shadow = Part(PrimitiveType.Cylinder, "Ground Shadow", null, new Vector3(0f, 0.03f, 0f), new Vector3(1.5f, 0.01f, 1.9f), ShadowTint);
        shadow.shadowCastingMode = ShadowCastingMode.Off;
        var boss = BuildBoss();
        var explosions = BuildExplosionFx();
        var hud = BuildHud();
        var level = new GameObject("Level (streamed at runtime)").transform;
        var pools = new GameObject("Projectile Pools (filled at runtime)").transform;

        foreach (var root in scene.GetRootGameObjects())
        {
            PersistAssets(root);
        }

        var wiring = new SerializedObject(game);
        Set(wiring, "view", view);
        Set(wiring, "retroFilter", view.GetComponent<SkyfortRetroFilter>());
        Set(wiring, "shipBody", ship);
        Set(wiring, "shipModel", shipModel);
        Set(wiring, "shipShadow", shadow.transform);
        Set(wiring, "engineFx", engineTrail);
        Set(wiring, "explosionFx", explosions);
        Set(wiring, "flash", flash);
        Set(wiring, "hudDocument", hud);
        Set(wiring, "sfx", sfx);
        Set(wiring, "engine", engine);
        Set(wiring, "levelRoot", level);
        Set(wiring, "poolRoot", pools);
        Set(wiring, "boss", boss);
        Set(wiring, "bossLeftCannon", boss.Find("Left cannon"));
        Set(wiring, "bossRightCannon", boss.Find("Right cannon"));
        Set(wiring, "bossCore", boss.Find("Core eye").GetComponent<Renderer>());
        Set(wiring, "turretPrefab", turret);
        Set(wiring, "fuelTankPrefab", fuelTank);
        Set(wiring, "radarPrefab", radar);
        Set(wiring, "dronePrefab", drone);
        Set(wiring, "laserPrefab", laser);
        Set(wiring, "boltPrefab", bolt);
        Set(wiring, "forceFieldMaterial", forceField);
        Set(wiring, "repairPickupPrefab", repairPickup);
        Set(wiring, "fuelPickupPrefab", fuelPickup);
        Set(wiring, "shieldPickupPrefab", shieldPickup);
        Set(wiring, "spreadPickupPrefab", spreadPickup);
        Set(wiring, "extraShipPickupPrefab", extraShipPickup);
        Set(wiring, "shieldBubble", ship.transform.Find("Shield Bubble").GetComponent<Renderer>());
        Set(wiring, "keyLight", keyLight);
        wiring.ApplyModifiedPropertiesWithoutUndo();

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.3f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        ResetCaches();
        Debug.Log($"Skyfort Strike: rebuilt {ScenePath} and prefabs.");
    }

    // ---------------------------------------------------------------- Scene objects

    private static Camera BuildCamera()
    {
        var cameraObject = new GameObject("Isometric Camera") { tag = "MainCamera" };
        var view = cameraObject.AddComponent<Camera>();
        view.orthographic = true;
        view.orthographicSize = 9.5f;
        view.nearClipPlane = 1f;
        view.farClipPlane = 200f;
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.01f, 0.015f, 0.04f);
        view.allowHDR = true;
        cameraObject.transform.rotation = Quaternion.Euler(33f, -45f, 0f);
        // Same framing the game uses (it follows the ship along Z at runtime).
        cameraObject.transform.position = new Vector3(0f, 2.5f, 7f) - cameraObject.transform.forward * 60f;
        cameraObject.AddComponent<AudioListener>();
        // Retro display filter (16-BIT / CRT / ARCADE / MODERN). Its shader reference makes builds include the shader.
        var retro = cameraObject.AddComponent<SkyfortRetroFilter>();
        var retroWiring = new SerializedObject(retro);
        Set(retroWiring, "shader", AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/SkyfortRetro.shader"));
        retroWiring.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    private static Light BuildKeyLight()
    {
        // Its color and intensity are eased toward each level theme at runtime.
        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Hard; // crisp, pixel-friendly shadow edges
        sun.shadowStrength = 0.8f;
        sun.transform.rotation = Quaternion.Euler(58f, -20f, 0f);
        return sun;
    }

    private static Rigidbody BuildShip(out Transform model, out ParticleSystem engineTrail)
    {
        var ship = Group("Strike Fighter", null, new Vector3(0f, 3f, 0f));
        var body = ship.gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.detectCollisions = false;

        model = Group("Fighter model", ship, Vector3.zero);
        Beveled("Fuselage", model, Vector3.zero, new Vector3(0.62f, 0.4f, 2f), Bone, 0.14f);
        Beveled("Nose", model, new Vector3(0f, -0.02f, 1.15f), new Vector3(0.4f, 0.26f, 0.6f), Bone, 0.1f);
        Part(PrimitiveType.Sphere, "Nose cone", model, new Vector3(0f, -0.02f, 1.45f), new Vector3(0.28f, 0.2f, 0.4f), Red);
        Part(PrimitiveType.Sphere, "Canopy", model, new Vector3(0f, 0.22f, 0.45f), new Vector3(0.34f, 0.22f, 0.7f), Cyan, true);
        Beveled("Spine", model, new Vector3(0f, 0.24f, -0.35f), new Vector3(0.3f, 0.14f, 0.9f), Hull, 0.05f);
        var wing = CreateWingMesh(1.35f, 1.3f, 0.35f, 0.75f, 0.08f);
        MeshPart("Right wing", model, new Vector3(0.28f, -0.05f, -0.3f), Vector3.zero, wing, Hull, false);
        var leftWing = MeshPart("Left wing", model, new Vector3(-0.28f, -0.05f, -0.3f), Vector3.zero, wing, Hull, false);
        leftWing.transform.localScale = new Vector3(-1f, 1f, 1f);
        foreach (var side in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cube, "Wing stripe", model, new Vector3(side * 1.05f, 0f, -0.75f), new Vector3(0.5f, 0.1f, 0.12f), Gold, true);
            Part(PrimitiveType.Cylinder, "Wing cannon", model, new Vector3(side * 1.55f, -0.02f, -0.45f), new Vector3(0.09f, 0.45f, 0.09f), Armor, false, new Vector3(90f, 0f, 0f));
            Beveled("Tail fin", model, new Vector3(side * 0.3f, 0.38f, -0.8f), new Vector3(0.06f, 0.5f, 0.45f), Hull, 0.02f, false, new Vector3(0f, 0f, side * -18f));
            Part(PrimitiveType.Cylinder, "Engine pod", model, new Vector3(side * 0.42f, -0.06f, -0.85f), new Vector3(0.28f, 0.35f, 0.28f), Armor, false, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Afterburner", model, new Vector3(side * 0.42f, -0.06f, -1.22f), new Vector3(0.24f, 0.24f, 0.12f), Cyan, true);
            Part(PrimitiveType.Sphere, "Nav light", model, new Vector3(side * 1.6f, 0.02f, -0.9f), Vector3.one * 0.12f, side < 0 ? Red : Lime, true);

            // Neon trim: fuselage side strips, wing leading edges and rings around the engine nozzles.
            Part(PrimitiveType.Cube, "Neon fuselage strip", model, new Vector3(side * 0.32f, 0.02f, 0.1f), new Vector3(0.04f, 0.05f, 1.8f), NeonGreen, true);
            var wingStart = new Vector3(side * 0.28f, -0.01f, 0.35f);
            var wingEnd = new Vector3(side * 1.63f, -0.01f, -0.4f);
            var edge = Part(PrimitiveType.Cube, "Neon wing edge", model, (wingStart + wingEnd) * 0.5f, new Vector3((wingEnd - wingStart).magnitude, 0.04f, 0.05f), Cyan, true);
            edge.transform.localRotation = Quaternion.FromToRotation(Vector3.right * side, (wingEnd - wingStart) * side);
            Ring("Neon nozzle", model, new Vector3(side * 0.42f, -0.06f, -1.05f), 0.15f, 0.03f, NeonGreen, true, new Vector3(90f, 0f, 0f));
        }

        Part(PrimitiveType.Cube, "Neon spine", model, new Vector3(0f, 0.32f, -0.35f), new Vector3(0.04f, 0.03f, 0.85f), Cyan, true);

        // The ground shadow is a separate object straight below the ship, so the model casts none of its own.
        DisableShadows(model);

        // Soft green underglow (alpha-blended, unlit) that follows the ship.
        var underglow = Part(PrimitiveType.Cylinder, "Neon underglow", model, new Vector3(0f, -0.22f, -0.2f), new Vector3(1.5f, 0.005f, 1.9f), NeonGreen, true);
        underglow.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Neon Underglow", color = new Color(0.1f, 1f, 0.5f, 0.14f) };

        // Translucent bubble shown while the Shield power-up is active (Sprites/Default is alpha-blended and unlit).
        var bubble = Part(PrimitiveType.Sphere, "Shield Bubble", ship, Vector3.zero, new Vector3(3.8f, 1.5f, 3.4f), Cyan, true);
        bubble.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Shield Bubble", color = new Color(0.3f, 0.85f, 1f, 0.22f) };
        bubble.gameObject.SetActive(false);

        engineTrail = new GameObject("Engine trail").AddComponent<ParticleSystem>();
        engineTrail.transform.SetParent(ship, false);
        engineTrail.transform.localPosition = new Vector3(0f, -0.06f, -1.3f);
        engineTrail.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var main = engineTrail.main;
        main.startLifetime = 0.35f;
        main.startSpeed = 4f;
        main.startSize = 0.22f;
        main.startColor = new Color(0.3f, 0.85f, 1f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = engineTrail.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.2f;
        var size = engineTrail.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        // Neon trail: hot cyan at the nozzle fading to green.
        var trailColor = engineTrail.colorOverLifetime;
        trailColor.enabled = true;
        var trailGradient = new Gradient();
        trailGradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.4f, 1f, 1f), 0f), new GradientColorKey(new Color(0.15f, 1f, 0.45f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        trailColor.color = trailGradient;
        main.startColor = Color.white;
        engineTrail.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial();
        return body;
    }

    private static ParticleSystem BuildExplosionFx()
    {
        // A single world-space system; the game emits bursts into it wherever something blows up.
        var fx = new GameObject("Explosion FX").AddComponent<ParticleSystem>();
        var main = fx.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = 3000;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.45f);
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = fx.emission;
        emission.enabled = false;
        var shape = fx.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;
        var size = fx.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var color = fx.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        fx.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial();
        return fx;
    }

    private static UIDocument BuildHud()
    {
        var themePath = $"{UiFolder}/SkyfortTheme.tss";
        if (!File.Exists(themePath))
        {
            File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(themePath);
        }

        var panelPath = $"{UiFolder}/SkyfortPanelSettings.asset";
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
        if (panel == null)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, panelPath);
        }

        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1280, 720);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;
        EditorUtility.SetDirty(panel);

        // The HUD's elements are created and data-bound by SkyfortHud when the game starts.
        var hud = new GameObject("HUD").AddComponent<UIDocument>();
        hud.panelSettings = panel;
        return hud;
    }

    private static Transform BuildBoss()
    {
        // The Warden: a towering original guardian mech with a glowing core eye as its weak point. It stands where
        // it holds position during the fight (facing the ship); the game hides it until the end of each level.
        var boss = Group("The Warden", null, new Vector3(0f, 0f, 27f), new Vector3(0f, 180f, 0f));
        Beveled("Hips", boss, new Vector3(0f, 1.6f, 0f), new Vector3(2.4f, 0.8f, 1.4f), Hull, 0.2f);
        foreach (var side in new[] { -1f, 1f })
        {
            Beveled("Leg", boss, new Vector3(side * 1.1f, 0.8f, 0f), new Vector3(0.8f, 1.6f, 1f), Armor, 0.18f);
            Beveled("Foot", boss, new Vector3(side * 1.1f, 0.15f, -0.2f), new Vector3(1.1f, 0.3f, 1.6f), Hull, 0.1f);
            Beveled("Shoulder", boss, new Vector3(side * 2.15f, 5.2f, 0f), new Vector3(1.2f, 1f, 1.3f), Armor, 0.2f);
            var cannon = Group(side < 0 ? "Left cannon" : "Right cannon", boss, new Vector3(side * 2.35f, 4.1f, 0f));
            Beveled("Arm", cannon, Vector3.zero, new Vector3(0.75f, 1.6f, 0.75f), Hull, 0.15f);
            Part(PrimitiveType.Cylinder, "Cannon barrel", cannon, new Vector3(0f, -0.4f, 0.9f), new Vector3(0.4f, 0.8f, 0.4f), Armor, false, new Vector3(90f, 0f, 0f));
            Ring("Muzzle glow", cannon, new Vector3(0f, -0.4f, 1.7f), 0.22f, 0.06f, Red, true, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Shoulder lamp", boss, new Vector3(side * 2.15f, 5.8f, 0.55f), Vector3.one * 0.25f, Gold, true);
        }

        Beveled("Torso", boss, new Vector3(0f, 3.9f, 0f), new Vector3(3.2f, 3.2f, 1.8f), Armor, 0.35f);
        Beveled("Chest plate", boss, new Vector3(0f, 3.9f, 0.88f), new Vector3(2.4f, 2.4f, 0.15f), Hull, 0.25f);
        Ring("Core housing", boss, new Vector3(0f, 4.1f, 0.98f), 0.75f, 0.12f, Gold, true, new Vector3(90f, 0f, 0f));
        Part(PrimitiveType.Sphere, "Core eye", boss, new Vector3(0f, 4.1f, 1.05f), Vector3.one * 1.1f, Red, true);
        Beveled("Head", boss, new Vector3(0f, 6.1f, 0.1f), new Vector3(1.3f, 0.9f, 1.1f), Hull, 0.2f);
        Part(PrimitiveType.Cube, "Visor", boss, new Vector3(0f, 6.15f, 0.66f), new Vector3(0.9f, 0.16f, 0.04f), Red, true);
        Part(PrimitiveType.Cylinder, "Antenna", boss, new Vector3(0.4f, 6.9f, 0f), new Vector3(0.05f, 0.45f, 0.05f), Bone);
        Part(PrimitiveType.Sphere, "Antenna tip", boss, new Vector3(0.4f, 7.35f, 0f), Vector3.one * 0.16f, Red, true);
        return boss;
    }

    // ---------------------------------------------------------------- Prefab models

    private static GameObject BuildTurret()
    {
        var root = Group("Turret", null, Vector3.zero);
        Beveled("Turret base", root, new Vector3(0f, 0.25f, 0f), new Vector3(1.3f, 0.5f, 1.3f), Hull, 0.18f);
        Ring("Base glow", root, new Vector3(0f, 0.52f, 0f), 0.5f, 0.05f, Red);
        var head = Group("Head", root, new Vector3(0f, 0.8f, 0f));
        Part(PrimitiveType.Sphere, "Turret dome", head, Vector3.zero, new Vector3(0.9f, 0.6f, 0.9f), Armor);
        Part(PrimitiveType.Cylinder, "Barrel", head, new Vector3(0f, 0.05f, 0.55f), new Vector3(0.14f, 0.4f, 0.14f), Hull, false, new Vector3(90f, 0f, 0f));
        Part(PrimitiveType.Sphere, "Sensor", head, new Vector3(0f, 0.2f, 0.32f), Vector3.one * 0.16f, Red, true);
        return root.gameObject;
    }

    private static GameObject BuildFuelTank()
    {
        var root = Group("Fuel Tank", null, Vector3.zero);
        Beveled("Tank cradle", root, new Vector3(0f, 0.12f, 0f), new Vector3(1.5f, 0.24f, 1.5f), Hull, 0.1f);
        Part(PrimitiveType.Cylinder, "Tank", root, new Vector3(0f, 0.95f, 0f), new Vector3(1.15f, 0.7f, 1.15f), Gold);
        Part(PrimitiveType.Sphere, "Tank cap", root, new Vector3(0f, 1.65f, 0f), new Vector3(1.1f, 0.45f, 1.1f), Gold);
        Ring("Fuel band", root, new Vector3(0f, 0.7f, 0f), 0.6f, 0.05f, Red);
        Ring("Fuel band", root, new Vector3(0f, 1.2f, 0f), 0.6f, 0.05f, Red);
        Part(PrimitiveType.Cube, "Fuel glyph", root, new Vector3(0f, 0.95f, -0.58f), new Vector3(0.36f, 0.36f, 0.04f), Red, true);
        return root.gameObject;
    }

    private static GameObject BuildRadar()
    {
        var root = Group("Radar Dish", null, Vector3.zero);
        Beveled("Radar plinth", root, new Vector3(0f, 0.2f, 0f), new Vector3(1.6f, 0.4f, 1.6f), Hull, 0.15f);
        Part(PrimitiveType.Cylinder, "Mast", root, new Vector3(0f, 1f, 0f), new Vector3(0.2f, 0.7f, 0.2f), Armor);
        var head = Group("Head", root, new Vector3(0f, 1.8f, 0f));
        Part(PrimitiveType.Sphere, "Dish", head, new Vector3(0f, 0.2f, 0.1f), new Vector3(1.7f, 1.2f, 0.25f), Bone, false, new Vector3(-20f, 0f, 0f));
        Part(PrimitiveType.Cylinder, "Feed horn", head, new Vector3(0f, 0.25f, -0.35f), new Vector3(0.06f, 0.35f, 0.06f), Armor, false, new Vector3(70f, 0f, 0f));
        Part(PrimitiveType.Sphere, "Emitter", head, new Vector3(0f, 0.36f, -0.7f), Vector3.one * 0.18f, Lime, true);
        return root.gameObject;
    }

    private static GameObject BuildDrone()
    {
        var root = Group("Drone", null, Vector3.zero);
        Beveled("Drone hull", root, Vector3.zero, new Vector3(0.9f, 0.4f, 1f), Hull, 0.14f);
        Part(PrimitiveType.Sphere, "Drone eye", root, new Vector3(0f, 0f, 0.5f), new Vector3(0.36f, 0.26f, 0.12f), Red, true);
        foreach (var side in new[] { -1f, 1f })
        {
            Beveled("Drone wing", root, new Vector3(side * 0.75f, 0f, -0.1f), new Vector3(0.7f, 0.08f, 0.6f), Armor, 0.03f, false, new Vector3(0f, side * 20f, side * -10f));
            Part(PrimitiveType.Sphere, "Wing tip", root, new Vector3(side * 1.08f, 0f, -0.2f), Vector3.one * 0.14f, NeonGreen, true);
        }

        Ring("Drone rotor", root, new Vector3(0f, 0.25f, 0f), 0.35f, 0.04f, NeonGreen);
        return root.gameObject;
    }

    private static GameObject BuildLaser()
    {
        var root = Group("Laser", null, Vector3.zero);
        Part(PrimitiveType.Capsule, "Laser bolt", root, Vector3.zero, new Vector3(0.12f, 0.6f, 0.12f), Lime, true, new Vector3(90f, 0f, 0f));
        return root.gameObject;
    }

    private static GameObject BuildBolt()
    {
        var root = Group("Enemy Bolt", null, Vector3.zero);
        Part(PrimitiveType.Sphere, "Plasma", root, Vector3.zero, Vector3.one * 0.38f, Red, true);
        Part(PrimitiveType.Sphere, "Plasma core", root, Vector3.zero, Vector3.one * 0.2f, Bone, true);
        return root.gameObject;
    }

    // Power-ups: a spinning colored halo around a symbol. The game spins and bobs the whole prefab.
    private static Transform PickupBase(string name, Color color)
    {
        var root = Group(name, null, Vector3.zero);
        Ring("Halo", root, Vector3.zero, 0.75f, 0.07f, color, true, new Vector3(90f, 0f, 0f));
        Ring("Inner halo", root, Vector3.zero, 0.55f, 0.03f, Bone, true, new Vector3(90f, 0f, 0f));
        return root;
    }

    private static GameObject BuildRepairPickup()
    {
        var root = PickupBase("PowerUp Repair", Red);
        Part(PrimitiveType.Cube, "Cross bar", root, Vector3.zero, new Vector3(0.7f, 0.22f, 0.22f), Bone, true);
        Part(PrimitiveType.Cube, "Cross post", root, Vector3.zero, new Vector3(0.22f, 0.7f, 0.22f), Bone, true);
        return root.gameObject;
    }

    private static GameObject BuildFuelPickup()
    {
        var root = PickupBase("PowerUp Fuel", Gold);
        Part(PrimitiveType.Cylinder, "Fuel cell", root, Vector3.zero, new Vector3(0.42f, 0.3f, 0.42f), Gold, true);
        Part(PrimitiveType.Cylinder, "Fuel cap", root, new Vector3(0f, 0.36f, 0f), new Vector3(0.2f, 0.06f, 0.2f), Bone, true);
        return root.gameObject;
    }

    private static GameObject BuildShieldPickup()
    {
        var root = PickupBase("PowerUp Shield", Cyan);
        Part(PrimitiveType.Sphere, "Shield orb", root, Vector3.zero, Vector3.one * 0.55f, Cyan, true);
        Ring("Orbit", root, Vector3.zero, 0.42f, 0.03f, Bone, true);
        return root.gameObject;
    }

    private static GameObject BuildSpreadPickup()
    {
        var root = PickupBase("PowerUp Spread", NeonGreen);
        var crystal = CreateCrystalMesh(0.14f, 0.4f, 0.2f);
        crystal.name = "Spread crystal";
        foreach (var angle in new[] { -32f, 0f, 32f })
        {
            MeshPart("Spread shard", root, Vector3.zero, new Vector3(0f, 0f, angle), crystal, NeonGreen, true);
        }

        return root.gameObject;
    }

    private static GameObject BuildExtraShipPickup()
    {
        var root = PickupBase("PowerUp Extra Ship", Bone);
        Part(PrimitiveType.Cube, "Chevron left", root, new Vector3(-0.15f, 0f, 0f), new Vector3(0.5f, 0.14f, 0.14f), Bone, true, new Vector3(0f, 0f, 35f));
        Part(PrimitiveType.Cube, "Chevron right", root, new Vector3(0.15f, 0f, 0f), new Vector3(0.5f, 0.14f, 0.14f), Bone, true, new Vector3(0f, 0f, -35f));
        Part(PrimitiveType.Sphere, "Star", root, new Vector3(0f, -0.25f, 0f), Vector3.one * 0.18f, Gold, true);
        return root.gameObject;
    }

    // ---------------------------------------------------------------- Asset helpers

    private static GameObject SavePrefab(GameObject root, string name)
    {
        PersistAssets(root);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // Procedural meshes and runtime-created materials only live in memory; save them so scene objects and
    // prefabs keep their references.
    private static void PersistAssets(GameObject root)
    {
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh != null && !EditorUtility.IsPersistent(mesh))
            {
                AssetDatabase.CreateAsset(mesh, UniquePath(MeshFolder, mesh.name, "asset"));
            }
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                SaveMaterial(material);
            }
        }
    }

    private static Material SaveMaterial(Material material)
    {
        if (material != null && !EditorUtility.IsPersistent(material))
        {
            AssetDatabase.CreateAsset(material, UniquePath(MaterialFolder, material.name, "mat"));
        }

        return material;
    }

    private static string UniquePath(string folder, string name, string extension)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.{extension}");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void Set(SerializedObject target, string field, Object value)
    {
        var property = target.FindProperty(field);
        if (property == null)
        {
            throw new System.InvalidOperationException($"{target.targetObject.GetType().Name} has no serialized field '{field}'.");
        }

        property.objectReferenceValue = value;
    }
}
