using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static SkyfortModels;
using Random = UnityEngine.Random;

// The title screen scene (Assets/Scenes/SkyfortIntro.unity). It is a demo reel of the game: the fighter flies an endless
// run through fortresses in every level theme on autopilot, weaving around obstacles and shooting turrets, while the
// camera cuts between a chase, an orbit, a fly-by, an overhead view and the cockpit. The retro display filter is live
// (press M), the title music plays, and the UI overlay offers Start Game and the platformer.
//
// The scene holds only a camera (with the filter), a UI document and one object running this component; everything
// else is built here at startup, like the other scenes.
public sealed class SkyfortIntro : MonoBehaviour
{
    private const float ChunkLength = 18f;
    private const float SpawnAhead = 130f;
    private const float DespawnBehind = 40f;
    private const float FlySpeed = 14f;
    private const float WallHalf = 8f;
    private const float Corridor = 5.8f;
    private const float MinAlt = 1.4f;
    private const float MaxAlt = 6.9f;
    private const int SegmentLength = 10;
    private const string HighScoreKey = "SkyfortStrike.HighScore";

    private enum Kind { Landmarks, Wall, Obelisks, Turrets, Mixed, Keep }

    private sealed class Chunk
    {
        public Transform Root;
        public float Start;
        public SkyfortTheme Theme;
    }

    private sealed class Obstacle
    {
        public Bounds Box;
        public Chunk Chunk;
    }

    private sealed class Turret
    {
        public Transform Root;
        public Transform Head;
        public Vector3 Center;
        public Chunk Chunk;
        public bool Alive = true;
    }

    private sealed class Bolt
    {
        public Transform Transform;
        public Vector3 Velocity;
        public float Life;
        public bool Active;
    }

    private readonly List<Chunk> chunks = new();
    private readonly List<Obstacle> obstacles = new();
    private readonly List<Turret> turrets = new();
    private readonly List<Bolt> bolts = new();

    private Camera cam;
    private SkyfortRetroFilter retro;
    private Light sun;
    private SkyfortMenu menu;
    private AudioSource music;
    private Transform world;
    private Transform ship;
    private Transform shipModel;
    private ParticleSystem explosions;

    private float spawnZ = -10f;
    private int chunkIndex;
    private Kind lastKind = Kind.Keep;
    private SkyfortTheme currentTheme = SkyfortThemes.Skyfort;
    private float shipX;
    private float shipY = 3.5f;
    private float demoTime;
    private float nextShot;
    private float nextFire;
    private int shot = -1;
    private float shotStart;
    private Vector3 flybyAnchor;
    private bool leaving;

    private static readonly string[] ShotNames = { "CHASE", "ORBIT", "FLY-BY", "OVERHEAD", "COCKPIT" };

    // ================================================================ Setup

    private void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Application.targetFrameRate = 60;

        cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<AudioListener>();
        }

        retro = cam.GetComponent<SkyfortRetroFilter>();
        cam.orthographic = false;
        cam.fieldOfView = 62f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 220f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = currentTheme.Background;
        cam.allowHDR = true;

        sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Hard;
        sun.shadowStrength = 0.8f;
        sun.transform.rotation = Quaternion.Euler(58f, -20f, 0f);
        QualitySettings.shadowDistance = 90f;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = currentTheme.Ambient;

        world = new GameObject("World").transform;
        BuildShip();
        BuildExplosions();
        BuildBolts();

        var document = FindFirstObjectByType<UIDocument>();
        if (document != null)
        {
            menu = SkyfortMenu.Build(document, StartGame, OpenPlatformer);
            menu.Show(PlayerPrefs.GetInt(HighScoreKey, 0), "PRESS SPACE TO START");
        }

        while (spawnZ < SpawnAhead)
        {
            BuildChunk();
        }

        NextShot();
        LoadMusic();
    }

    private async void LoadMusic()
    {
        try
        {
            var clip = await SkyfortAudio.LoadThemeAsync(destroyCancellationToken);
            music = gameObject.AddComponent<AudioSource>();
            music.clip = clip;
            music.loop = true;
            music.volume = 0f;
            music.Play();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void BuildShip()
    {
        ship = Group("Intro fighter", null, new Vector3(0f, shipY, 0f));
        shipModel = Group("Fighter model", ship, Vector3.zero);
        SkyfortFighter.Build(shipModel);

        var trail = new GameObject("Engine trail").AddComponent<ParticleSystem>();
        trail.transform.SetParent(ship, false);
        trail.transform.localPosition = new Vector3(0f, -0.06f, -1.3f);
        trail.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var main = trail.main;
        main.startLifetime = 0.35f;
        main.startSpeed = 4f;
        main.startSize = 0.22f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = trail.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.2f;
        var size = trail.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var color = trail.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.4f, 1f, 1f), 0f), new GradientColorKey(new Color(0.15f, 1f, 0.45f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        trail.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial();
    }

    private void BuildExplosions()
    {
        explosions = new GameObject("Explosions").AddComponent<ParticleSystem>();
        var main = explosions.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = 2500;
        main.startSpeed = 0f;
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = explosions.emission;
        emission.enabled = false;
        var shape = explosions.shape;
        shape.enabled = false;
        var size = explosions.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        explosions.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial();
        explosions.Play();
    }

    private void BuildBolts()
    {
        for (var i = 0; i < 14; i++)
        {
            var bolt = Part(PrimitiveType.Cube, "Laser", null, Vector3.zero, new Vector3(0.08f, 0.08f, 1f), NeonGreen, true);
            bolt.gameObject.SetActive(false);
            bolts.Add(new Bolt { Transform = bolt.transform });
        }
    }

    private void Explode(Vector3 position, Color color, int count)
    {
        var emit = new ParticleSystem.EmitParams { startColor = color };
        for (var i = 0; i < count; i++)
        {
            emit.position = position;
            emit.velocity = Random.onUnitSphere * Random.Range(2f, 9f);
            emit.startSize = Random.Range(0.15f, 0.5f);
            emit.startLifetime = Random.Range(0.35f, 0.9f);
            explosions.Emit(emit, 1);
        }
    }

    // ================================================================ World streaming

    private void BuildChunk()
    {
        var theme = SkyfortThemes.All[chunkIndex / SegmentLength % SkyfortThemes.All.Length];
        var inSegment = chunkIndex % SegmentLength;
        var chunk = new Chunk
        {
            Root = Group($"{theme.Name} {chunkIndex}", world, new Vector3(0f, 0f, spawnZ)),
            Start = spawnZ,
            Theme = theme
        };
        chunks.Add(chunk);

        if (inSegment >= SegmentLength - 3)
        {
            BuildSpace(chunk);
        }
        else
        {
            BuildDeck(chunk);
            Kind kind;
            do kind = (Kind)Random.Range(0, 6);
            while (kind == lastKind);
            lastKind = kind;
            BuildObstacles(chunk, kind);
        }

        chunkIndex++;
        spawnZ += ChunkLength;
    }

    private void BuildDeck(Chunk chunk)
    {
        var theme = chunk.Theme;
        var root = chunk.Root;
        var mid = ChunkLength * 0.5f;
        Part(PrimitiveType.Cube, "Deck plate", root, new Vector3(0f, -0.25f, mid), new Vector3(WallHalf * 2f + 1f, 0.5f, ChunkLength), theme.Deck);
        for (var z = 1.5f; z < ChunkLength; z += 3f)
        {
            Part(PrimitiveType.Cube, "Deck seam", root, new Vector3(0f, 0.005f, z), new Vector3(WallHalf * 2f, 0.02f, 0.05f), theme.DeckLine);
        }

        for (var x = -6f; x <= 6f; x += 3f)
        {
            Part(PrimitiveType.Cube, "Lane neon", root, new Vector3(x, 0.006f, mid), new Vector3(0.06f, 0.02f, ChunkLength), theme.Neon, true);
        }

        Beveled("Far rampart", root, new Vector3(-WallHalf - 0.6f, 2.5f, mid), new Vector3(1.2f, 5f, ChunkLength - 0.1f), theme.Rampart, 0.15f);
        Beveled("Near rampart", root, new Vector3(WallHalf + 0.6f, 0.35f, mid), new Vector3(1.2f, 0.7f, ChunkLength - 0.1f), theme.Rampart, 0.15f);
        Part(PrimitiveType.Cube, "Rampart light", root, new Vector3(WallHalf + 0.02f, 0.62f, mid), new Vector3(0.05f, 0.06f, ChunkLength - 0.4f), theme.RampartGlow, true);
        for (var y = 1; y <= 5; y++)
        {
            Part(PrimitiveType.Cube, "Altitude rung", root, new Vector3(-WallHalf + 0.01f, y - 0.03f, mid), new Vector3(0.04f, 0.05f, ChunkLength - 0.4f), y == 5 ? Gold : theme.RampartGlow, true);
        }
    }

    private void BuildSpace(Chunk chunk)
    {
        var theme = chunk.Theme;
        var root = chunk.Root;
        var mid = ChunkLength * 0.5f;
        Part(PrimitiveType.Cube, "Void plate", root, new Vector3(0f, -0.25f, mid), new Vector3(WallHalf * 2f + 1f, 0.5f, ChunkLength), theme.MidFloor);
        var colors = theme.StarColors;
        for (var i = 0; i < (colors != null ? 26 : 14); i++)
        {
            var star = new Vector3(Random.Range(-WallHalf, WallHalf), 0.02f, Random.Range(0f, ChunkLength));
            var color = colors != null ? colors[Random.Range(0, colors.Length)] : theme.MidSpeck;
            Part(PrimitiveType.Sphere, "Star", root, star, Vector3.one * Random.Range(0.05f, 0.14f), color, theme.MidSpecksGlow);
        }

        for (var i = 0; i < theme.Planets; i++)
        {
            var planetSize = Random.Range(0.5f, 1.1f);
            Part(PrimitiveType.Sphere, "Planet", root, new Vector3(Random.Range(-WallHalf, WallHalf), 0.05f, Random.Range(2f, ChunkLength - 2f)), new Vector3(planetSize, planetSize * 0.2f, planetSize), theme.PlanetColor, true);
        }

        for (var z = 0f; z < ChunkLength; z += 6f)
        {
            Part(PrimitiveType.Cube, "Nav grid", root, new Vector3(0f, 0.01f, z), new Vector3(WallHalf * 2f, 0.01f, 0.04f), Color.Lerp(Color.black, theme.Neon, 0.45f), true);
        }

        // Drifting wreckage keeps the empty stretches busy: a couple of glowing crystals to weave around.
        for (var i = 0; i < 2; i++)
        {
            var x = Random.Range(-5f, 5f);
            var z = Random.Range(3f, ChunkLength - 3f);
            var h = Random.Range(2.5f, 5f);
            Pyramid("Crystal", root, new Vector3(x, 0f, z), new Vector3(1.2f, h, 1.2f), theme.StoneGlow, true);
            AddObstacle(chunk, new Vector3(x, h * 0.5f, z), new Vector3(1.2f, h, 1.2f));
        }
    }

    private void AddObstacle(Chunk chunk, Vector3 localCenter, Vector3 size)
    {
        obstacles.Add(new Obstacle { Box = new Bounds(localCenter + new Vector3(0f, 0f, chunk.Start), size), Chunk = chunk });
    }

    private void BuildObstacles(Chunk chunk, Kind kind)
    {
        switch (kind)
        {
            case Kind.Landmarks:
                for (var i = 0; i < 3; i++)
                {
                    Landmark(chunk, Random.Range(-4.5f, 4.5f), 3.5f + i * 5.5f, Random.Range(3f, 4.8f), Random.Range(2.6f, 5.4f));
                }

                break;
            case Kind.Wall:
                Wall(chunk, 10f, -WallHalf, WallHalf, Random.Range(2.1f, 3f));
                Tower(chunk, Random.Range(-5f, -2f), 10f, 0.8f, Random.Range(5f, 5.9f));
                Tower(chunk, Random.Range(2f, 5f), 10f, 0.8f, Random.Range(5f, 5.9f));
                AddTurret(chunk, Random.Range(-5f, 5f), 4f);
                break;
            case Kind.Obelisks:
                for (var i = 0; i < 4; i++)
                {
                    Obelisk(chunk, (i % 2 == 0 ? -1f : 1f) * Random.Range(1.2f, 4.6f), 3.5f + i * 3.8f, Random.Range(4.6f, 6.1f));
                }

                break;
            case Kind.Turrets:
                for (var i = 0; i < 4; i++)
                {
                    AddTurret(chunk, Mathf.Lerp(-5f, 5f, (i + 0.5f) / 4f), 4f + (i % 2) * 7f);
                }

                break;
            case Kind.Mixed:
                Landmark(chunk, Random.Range(-4f, 4f), 5f, 4f, Random.Range(3f, 5f));
                AddTurret(chunk, Random.Range(-5f, 5f), 11f);
                AddTurret(chunk, Random.Range(-5f, 5f), 15f);
                break;
            default:
                Keep(chunk, Random.Range(-3f, 3f), 9f);
                break;
        }
    }

    private void Landmark(Chunk chunk, float x, float z, float baseSize, float height)
    {
        var theme = chunk.Theme;
        var style = theme == SkyfortThemes.IceCitadel ? SkyfortLandmark.Spire : theme.Landmark;
        var root = chunk.Root;
        switch (style)
        {
            case SkyfortLandmark.Spire:
            {
                var width = baseSize * 0.5f;
                height += 1f;
                Pyramid("Ice spire", root, new Vector3(x, 0f, z), new Vector3(width, height, width), theme.StoneMain);
                Pyramid("Spire tip", root, new Vector3(x, height * 0.8f, z), new Vector3(width * 0.21f, height * 0.21f, width * 0.21f), theme.StoneGlow, true);
                AddObstacle(chunk, new Vector3(x, height * 0.5f, z), new Vector3(width, height, width));
                break;
            }
            case SkyfortLandmark.Tank:
            {
                height = Mathf.Min(height, 5.2f);
                var radius = baseSize * 0.42f;
                Part(PrimitiveType.Cylinder, "Tank", root, new Vector3(x, height * 0.5f, z), new Vector3(radius * 2f, height * 0.5f, radius * 2f), theme.StoneMain);
                for (var y = 0.9f; y < height - 0.4f; y += 1.3f)
                {
                    Part(PrimitiveType.Cylinder, "Tank band", root, new Vector3(x, y, z), new Vector3(radius * 2.06f, 0.06f, radius * 2.06f), Color.white);
                }

                Part(PrimitiveType.Sphere, "Tank dome", root, new Vector3(x, height, z), new Vector3(radius * 2f, 0.7f, radius * 2f), theme.StoneBase);
                AddObstacle(chunk, new Vector3(x, height * 0.5f, z), new Vector3(radius * 2f, height, radius * 2f));
                break;
            }
            case SkyfortLandmark.Bastion:
            {
                height = Mathf.Min(height, 5.2f);
                var width = baseSize * 0.9f;
                Part(PrimitiveType.Cube, "Bastion", root, new Vector3(x, height * 0.5f, z), new Vector3(width, height, width), theme.CastleStone);
                Part(PrimitiveType.Cube, "Bastion cap", root, new Vector3(x, height + 0.1f, z), new Vector3(width + 0.3f, 0.2f, width + 0.3f), theme.CastleTrim);
                Part(PrimitiveType.Cube, "Bastion slit", root, new Vector3(x, height * 0.6f, z - width * 0.5f - 0.01f), new Vector3(0.18f, 0.8f, 0.04f), theme.WindowGlow, true);
                AddObstacle(chunk, new Vector3(x, height * 0.5f, z), new Vector3(width + 0.3f, height, width + 0.3f));
                break;
            }
            default:
            {
                Pyramid("Pyramid", root, new Vector3(x, 0.2f, z), new Vector3(baseSize, height - 0.2f, baseSize), theme.StoneMain);
                Pyramid("Capstone", root, new Vector3(x, 0.2f + (height - 0.2f) * 0.85f, z), new Vector3(baseSize * 0.16f, (height - 0.2f) * 0.16f, baseSize * 0.16f), theme.StoneGlow, true);
                Part(PrimitiveType.Cube, "Plinth glow", root, new Vector3(x, 0.2f, z - baseSize * 0.5f - 0.2f), new Vector3(baseSize + 0.5f, 0.05f, 0.03f), theme.Neon, true);
                AddObstacle(chunk, new Vector3(x, height * 0.5f, z), new Vector3(baseSize, height, baseSize));
                break;
            }
        }
    }

    private void Tower(Chunk chunk, float x, float z, float radius, float height)
    {
        var theme = chunk.Theme;
        var tower = Group("Tower", chunk.Root, new Vector3(x, 0f, z));
        var body = height - 1.2f;
        Part(PrimitiveType.Cylinder, "Body", tower, new Vector3(0f, body * 0.5f, 0f), new Vector3(radius * 2f, body * 0.5f, radius * 2f), theme.CastleStone);
        Ring("Neon band", tower, new Vector3(0f, body * 0.5f, 0f), radius * 1.02f, 0.035f, theme.Neon);
        Pyramid("Roof", tower, new Vector3(0f, body + 0.15f, 0f), new Vector3(radius * 2.1f, 1f, radius * 2.1f), theme.Roof);
        Part(PrimitiveType.Sphere, "Beacon", tower, new Vector3(0f, body + 1.18f, 0f), Vector3.one * 0.18f, theme.WindowGlow, true);
        for (var y = 1.1f; y < body - 0.5f; y += 1.4f)
        {
            Part(PrimitiveType.Cube, "Window", tower, new Vector3(0f, y, -radius - 0.01f), new Vector3(0.14f, 0.45f, 0.04f), theme.WindowGlow, true);
        }

        AddObstacle(chunk, new Vector3(x, height * 0.5f, z), new Vector3(radius * 2f, height, radius * 2f));
    }

    private void Wall(Chunk chunk, float z, float fromX, float toX, float height)
    {
        var theme = chunk.Theme;
        var width = toX - fromX;
        var centerX = (fromX + toX) * 0.5f;
        Part(PrimitiveType.Cube, "Curtain wall", chunk.Root, new Vector3(centerX, height * 0.5f, z), new Vector3(width, height, 1.1f), theme.CastleStone);
        Part(PrimitiveType.Cube, "Wall walk", chunk.Root, new Vector3(centerX, height + 0.05f, z), new Vector3(width, 0.1f, 1.3f), theme.CastleTrim);
        if (theme.Bricks)
        {
            for (var y = 0.45f; y < height - 0.1f; y += 0.45f)
            {
                Part(PrimitiveType.Cube, "Mortar", chunk.Root, new Vector3(centerX, y, z - 0.56f), new Vector3(width, 0.035f, 0.02f), theme.CastleTrim);
            }
        }
        else
        {
            Part(PrimitiveType.Cube, "Wall glow", chunk.Root, new Vector3(centerX, height * 0.55f, z - 0.56f), new Vector3(width - 0.2f, 0.06f, 0.02f), theme.Neon, true);
        }

        Part(PrimitiveType.Cube, "Wall top neon", chunk.Root, new Vector3(centerX, height + 0.1f, z - 0.67f), new Vector3(width, 0.05f, 0.05f), theme.NeonAlt, true);
        for (var x = fromX + 0.35f; x < toX - 0.2f; x += 0.9f)
        {
            Part(PrimitiveType.Cube, "Merlon", chunk.Root, new Vector3(x, height + 0.3f, z - 0.45f), new Vector3(0.45f, 0.4f, 0.3f), theme.CastleStone);
        }

        AddObstacle(chunk, new Vector3(centerX, (height + 0.5f) * 0.5f, z), new Vector3(width, height + 0.5f, 1.1f));
    }

    private void Keep(Chunk chunk, float x, float z)
    {
        const float width = 4.2f;
        const float depth = 3.6f;
        const float height = 3.6f;
        var theme = chunk.Theme;
        Part(PrimitiveType.Cube, "Keep", chunk.Root, new Vector3(x, height * 0.5f, z), new Vector3(width, height, depth), theme.CastleStone);
        Part(PrimitiveType.Cube, "Keep roof", chunk.Root, new Vector3(x, height + 0.05f, z), new Vector3(width + 0.2f, 0.1f, depth + 0.2f), theme.CastleTrim);
        Part(PrimitiveType.Cube, "Keep door", chunk.Root, new Vector3(x, 0.7f, z - depth * 0.5f - 0.01f), new Vector3(1f, 1.4f, 0.04f), theme.WindowGlow, true);
        AddObstacle(chunk, new Vector3(x, (height + 0.5f) * 0.5f, z), new Vector3(width, height + 0.5f, depth));
        foreach (var cx in new[] { -1f, 1f })
        {
            foreach (var cz in new[] { -1f, 1f })
            {
                Tower(chunk, x + cx * width * 0.5f, z + cz * depth * 0.5f, 0.7f, 5.3f);
            }
        }
    }

    private void Obelisk(Chunk chunk, float x, float z, float height)
    {
        var theme = chunk.Theme;
        const float width = 0.9f;
        var shaft = height - 0.7f;
        Part(PrimitiveType.Cube, "Obelisk", chunk.Root, new Vector3(x, shaft * 0.5f, z), new Vector3(width, shaft, width), theme.StoneMain);
        Pyramid("Obelisk cap", chunk.Root, new Vector3(x, shaft, z), new Vector3(width, 0.7f, width), theme.StoneGlow, true);
        Part(PrimitiveType.Cube, "Obelisk neon", chunk.Root, new Vector3(x, shaft * 0.5f, z - width * 0.5f - 0.01f), new Vector3(0.06f, shaft, 0.03f), theme.Neon, true);
        AddObstacle(chunk, new Vector3(x, height * 0.5f, z), new Vector3(width, height, width));
    }

    private void AddTurret(Chunk chunk, float x, float z)
    {
        var root = Group("Turret", chunk.Root, new Vector3(x, 0f, z));
        Part(PrimitiveType.Cylinder, "Base", root, new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.25f, 0.9f), Armor);
        var head = Group("Head", root, new Vector3(0f, 0.65f, 0f));
        Part(PrimitiveType.Cube, "Body", head, Vector3.zero, new Vector3(0.6f, 0.35f, 0.6f), Hull);
        Part(PrimitiveType.Cube, "Barrel", head, new Vector3(0f, 0f, 0.6f), new Vector3(0.12f, 0.12f, 0.8f), Bone);
        Part(PrimitiveType.Sphere, "Sensor", head, new Vector3(0f, 0.2f, 0.2f), Vector3.one * 0.16f, Red, true);
        var center = new Vector3(x, 0.65f, chunk.Start + z);
        turrets.Add(new Turret { Root = root, Head = head, Center = center, Chunk = chunk });
        AddObstacle(chunk, new Vector3(x, 0.65f, z), new Vector3(0.9f, 1.3f, 0.9f));
    }

    private void RemoveOldChunks(float shipZ)
    {
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            var chunk = chunks[i];
            if (chunk.Start + ChunkLength >= shipZ - DespawnBehind)
            {
                continue;
            }

            obstacles.RemoveAll(o => o.Chunk == chunk);
            turrets.RemoveAll(t => t.Chunk == chunk);
            Destroy(chunk.Root.gameObject);
            chunks.RemoveAt(i);
        }
    }

    // ================================================================ Flight

    private void Update()
    {
        if (leaving)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            StartGame();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            OpenPlatformer();
            return;
        }

        if (Input.GetKeyDown(KeyCode.M) && retro != null)
        {
            menu?.Notice($"DISPLAY: {SkyfortRetroFilter.Label(retro.Cycle())}");
        }

        var dt = Time.deltaTime;
        demoTime += dt;
        FlyShip(dt);
        UpdateBolts(dt);
        UpdateTurrets();
        while (spawnZ < ship.position.z + SpawnAhead)
        {
            BuildChunk();
        }

        RemoveOldChunks(ship.position.z);
        UpdateAtmosphere(dt);

        if (music != null && music.volume < 0.6f)
        {
            music.volume = Mathf.Min(0.6f, music.volume + dt * 0.25f);
        }
    }

    private bool Blocked(float x, float y, float z)
    {
        foreach (var obstacle in obstacles)
        {
            var box = obstacle.Box;
            if (box.max.z < z + 0.5f || box.min.z > z + 11f)
            {
                continue;
            }

            if (Mathf.Abs(x - box.center.x) < box.extents.x + 1.3f && y < box.max.y + 0.9f)
            {
                return true;
            }
        }

        return false;
    }

    // Autopilot: weave gently, dodge sideways around anything ahead, and climb over walls that span the corridor.
    private void FlyShip(float dt)
    {
        var z = ship.position.z;
        var targetX = Mathf.Sin(demoTime * 0.55f) * 3.4f;
        var targetY = 3.6f + Mathf.Sin(demoTime * 0.9f) * 0.9f;
        if (Blocked(targetX, targetY, z))
        {
            var found = false;
            for (var d = 0.8f; d <= 9f && !found; d += 0.8f)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var candidate = Mathf.Clamp(targetX + side * d, -Corridor, Corridor);
                    if (!Blocked(candidate, targetY, z))
                    {
                        targetX = candidate;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                targetY = MaxAlt;
            }
        }

        var previous = new Vector2(shipX, shipY);
        shipX = Mathf.Lerp(shipX, targetX, 1f - Mathf.Exp(-3f * dt));
        shipY = Mathf.Lerp(shipY, Mathf.Clamp(targetY, MinAlt, MaxAlt), 1f - Mathf.Exp(-3.5f * dt));
        var steer = new Vector2(
            Mathf.Clamp((shipX - previous.x) / Mathf.Max(dt, 0.0001f) / 9f, -1f, 1f),
            Mathf.Clamp((shipY - previous.y) / Mathf.Max(dt, 0.0001f) / 6.5f, -1f, 1f));
        ship.position = new Vector3(shipX, shipY, z + FlySpeed * dt);
        shipModel.localRotation = Quaternion.Euler(-steer.y * 14f, 0f, -steer.x * 28f);

        // Shoot the nearest turret ahead.
        if (Time.time >= nextFire)
        {
            var best = (Turret)null;
            var bestDistance = 32f;
            foreach (var turret in turrets)
            {
                var ahead = turret.Center.z - ship.position.z;
                if (turret.Alive && ahead > 7f && ahead < bestDistance)
                {
                    best = turret;
                    bestDistance = ahead;
                }
            }

            if (best != null)
            {
                FireAt(best.Center);
                nextFire = Time.time + 0.22f;
            }
        }
    }

    private void FireAt(Vector3 target)
    {
        foreach (var bolt in bolts)
        {
            if (bolt.Active)
            {
                continue;
            }

            var muzzle = ship.position + new Vector3(0f, -0.1f, 1.2f);
            var direction = (target - muzzle).normalized;
            bolt.Active = true;
            bolt.Life = 1.2f;
            bolt.Velocity = direction * 75f;
            bolt.Transform.position = muzzle;
            bolt.Transform.rotation = Quaternion.LookRotation(direction);
            bolt.Transform.gameObject.SetActive(true);
            return;
        }
    }

    private void UpdateBolts(float dt)
    {
        foreach (var bolt in bolts)
        {
            if (!bolt.Active)
            {
                continue;
            }

            bolt.Transform.position += bolt.Velocity * dt;
            bolt.Life -= dt;
            var hit = false;
            foreach (var turret in turrets)
            {
                if (turret.Alive && (turret.Center - bolt.Transform.position).sqrMagnitude < 1.1f * 1.1f)
                {
                    turret.Alive = false;
                    turret.Root.gameObject.SetActive(false);
                    Explode(turret.Center, Red, 34);
                    Explode(turret.Center, Gold, 18);
                    hit = true;
                    break;
                }
            }

            if (hit || bolt.Life <= 0f)
            {
                bolt.Active = false;
                bolt.Transform.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateTurrets()
    {
        foreach (var turret in turrets)
        {
            if (!turret.Alive)
            {
                continue;
            }

            var look = ship.position - turret.Head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
            {
                turret.Head.rotation = Quaternion.Slerp(turret.Head.rotation, Quaternion.LookRotation(look), Time.deltaTime * 4f);
            }
        }
    }

    private void UpdateAtmosphere(float dt)
    {
        var z = ship.position.z;
        foreach (var chunk in chunks)
        {
            if (z >= chunk.Start && z < chunk.Start + ChunkLength)
            {
                currentTheme = chunk.Theme;
                break;
            }
        }

        var blend = 1f - Mathf.Exp(-1.5f * dt);
        cam.backgroundColor = Color.Lerp(cam.backgroundColor, currentTheme.Background, blend);
        sun.color = Color.Lerp(sun.color, currentTheme.SunColor, blend);
        sun.intensity = Mathf.Lerp(sun.intensity, currentTheme.SunIntensity, blend);
        RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, currentTheme.Ambient, blend);
    }

    // ================================================================ Camera

    private void NextShot()
    {
        shot = (shot + 1) % ShotNames.Length;
        shotStart = Time.time;
        nextShot = Time.time + (shot == 2 ? 4.5f : 6.5f);
        if (shot == 2)
        {
            // The fly-by camera stays put in the world while the fighter streaks past.
            var side = Random.value < 0.5f ? -1f : 1f;
            flybyAnchor = new Vector3(shipX + side * 3.2f, 1.4f, ship.position.z + 26f);
        }

        menu?.Cut(shot + 1, ShotNames[shot]);
    }

    private void LateUpdate()
    {
        if (cam == null || ship == null)
        {
            return;
        }

        if (Time.time >= nextShot)
        {
            NextShot();
        }

        var p = ship.position;
        var t = Time.time - shotStart;
        Vector3 position;
        Vector3 target;
        var fov = 62f;
        var roll = 0f;
        switch (shot)
        {
            case 0:
                position = p + new Vector3(Mathf.Sin(Time.time * 0.5f) * 1.2f, 1.3f, -5.5f);
                target = p + new Vector3(0f, 0.2f, 9f);
                roll = (shipX - Mathf.Sin(demoTime * 0.55f) * 3.4f) * 1.5f;
                break;
            case 1:
                position = p + Quaternion.Euler(0f, 65f + t * 20f, 0f) * new Vector3(0f, 1.4f, -6.5f);
                target = p;
                fov = 48f;
                break;
            case 2:
                position = flybyAnchor;
                target = p;
                fov = 55f;
                break;
            case 3:
                position = p + new Vector3(0f, 13f, -2f);
                target = p + new Vector3(0f, 0f, 3f);
                fov = 55f;
                break;
            default:
                position = p + new Vector3(0f, 0.45f, 1.1f);
                target = p + new Vector3(shipX * -0.1f, 0.3f, 20f);
                fov = 80f;
                break;
        }

        cam.transform.position = position;
        cam.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        cam.fieldOfView = fov;
        shipModel.gameObject.SetActive(shot != 4);
        if (retro != null)
        {
            retro.Cockpit = Mathf.MoveTowards(retro.Cockpit, shot == 4 ? 1f : 0f, Time.deltaTime * 2f);
        }
    }

    // ================================================================ Navigation

    private void StartGame()
    {
        if (leaving) return;
        leaving = true;
        SceneManager.LoadScene("SkyfortStrike");
    }

    private void OpenPlatformer()
    {
        if (leaving) return;
        leaving = true;
        SceneManager.LoadScene("SkyPlatformer");
    }
}
