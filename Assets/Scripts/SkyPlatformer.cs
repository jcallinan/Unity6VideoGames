using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SkyfortModels;

// PIXEL RUNNER: a Mario-style platformer in its own scene (Assets/Scenes/SkyPlatformer.unity). The scene holds one
// object with this component; the level, the player, the enemies, the lights and the camera are all built here when the
// scene starts, so nothing needs generating in the Editor.
//
// World 1-1 changes camera and controls as you advance:
//   side scroller  ->  first person  ->  side scroller  ->  top down  ->  side scroller  ->  flag
// Side: A/D run, Space (or W/Up) jump, Shift run faster.   First person: W/S walk, A/D strafe.   Top down: WASD move.
// Stomp the goombas from above, bump ? blocks from below, grab coins, reach the flag. Esc returns to the main menu.
public sealed class SkyPlatformer : MonoBehaviour
{
    private enum View { Side, First, Top }

    private sealed class Zone
    {
        public float From;
        public float To;
        public View View;
        public float ZHalf;
        public string Toast;
    }

    private sealed class Block
    {
        public Bounds Box;
        public bool Question;
        public bool Used;
        public Renderer Skin;
        public float Bump;
        public Vector3 Rest;
        public Transform Visual;
    }

    private sealed class Coin
    {
        public Transform Root;
        public bool Taken;
        public float Phase;
    }

    private sealed class Enemy
    {
        public Transform Root;
        public Vector3 Foot;
        public Vector3 Dir = Vector3.right;
        public float Min;
        public float Max;
        public float ZHalf;
        public float Timer;
        public float Squash;
        public bool Alive = true;
    }

    // ---- Tuning
    private const float PlayerWidth = 0.7f;
    private const float PlayerHeight = 1.5f;
    private const float Gravity = 34f;
    private const float JumpSpeed = 13f;
    private const float WalkSpeed = 7.5f;
    private const float RunSpeed = 11f;
    private const float FlagX = 293f;

    private static readonly Color Sky = new(0.36f, 0.62f, 0.95f);
    private static readonly Color Grass = new(0.35f, 0.75f, 0.25f);
    private static readonly Color Dirt = new(0.55f, 0.34f, 0.16f);
    private static readonly Color BrickColor = new(0.78f, 0.36f, 0.2f);
    private static readonly Color BrickEdge = new(0.45f, 0.18f, 0.1f);
    private static readonly Color QuestionColor = new(1f, 0.74f, 0.1f);
    private static readonly Color UsedColor = new(0.45f, 0.33f, 0.22f);
    private static readonly Color PipeColor = new(0.1f, 0.65f, 0.22f);
    private static readonly Color PipeDark = new(0.05f, 0.42f, 0.14f);
    private static readonly Color CoinColor = new(1f, 0.82f, 0.1f);
    private static readonly Color Skin = new(1f, 0.8f, 0.6f);
    private static readonly Color Overalls = new(0.15f, 0.25f, 0.85f);
    private static readonly Color Shirt = new(0.9f, 0.12f, 0.12f);

    private readonly List<Block> blocks = new();
    private readonly List<Coin> coins = new();
    private readonly List<Enemy> enemies = new();
    private readonly List<Zone> zones = new();

    private Camera cam;
    private Transform level;
    private Transform player;
    private Transform legLeft;
    private Transform legRight;
    private Renderer[] playerRenderers;
    private AudioSource audioOut;
    private AudioClip jumpClip, coinClip, stompClip, dieClip, winClip, bumpClip;

    private Vector3 foot;
    private Vector3 velocity;
    private bool grounded;
    private bool jumping;
    private float coyote;
    private float jumpBuffer;
    private float facing = 90f;
    private float stride;

    private int score;
    private int coinCount;
    private int lives = 3;
    private bool dying;
    private float deathTimer;
    private bool gameOver;
    private bool won;
    private Vector3 checkpoint = new(2f, 0.05f, 0f);

    private Zone zone;
    private string toast = "";
    private float toastUntil;
    private float sideFocusX;
    private float sideFocusY = 3f;
    private float blend = 1f;
    private Vector3 fromPosition;
    private Quaternion fromRotation = Quaternion.identity;
    private float fromFov = 40f;
    private GUIStyle style;

    private void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Application.targetFrameRate = 60;
        level = new GameObject("Level").transform;
        BuildWorld();
        BuildPlayer();
        BuildSounds();

        zone = ZoneAt(foot.x);
        sideFocusX = foot.x + 2.5f;
        cam.transform.position = SideTarget(out var rotation, out _);
        cam.transform.rotation = rotation;
        Toast("WORLD 1-1  -  SIDE SCROLLER");
    }

    // ================================================================ World

    private void BuildWorld()
    {
        cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<AudioListener>();
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Sky;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400f;
        cam.orthographic = false;
        cam.fieldOfView = 40f;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        QualitySettings.shadowDistance = 60f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.76f);

        audioOut = gameObject.AddComponent<AudioSource>();

        zones.Add(new Zone { From = 0f, To = 64f, View = View.Side, Toast = "SIDE SCROLLER" });
        zones.Add(new Zone { From = 64f, To = 134f, View = View.First, ZHalf = 6f, Toast = "FIRST PERSON!  W/S walk, A/D strafe, Space jump" });
        zones.Add(new Zone { From = 134f, To = 176f, View = View.Side, Toast = "BACK TO SIDE SCROLLER" });
        zones.Add(new Zone { From = 176f, To = 236f, View = View.Top, ZHalf = 11f, Toast = "TOP DOWN!  WASD to move" });
        zones.Add(new Zone { From = 236f, To = 400f, View = View.Side, Toast = "SIDE SCROLLER  -  reach the flag!" });

        BuildScenery();

        // ---- Section A: classic side scroller
        Ground(0f, 20f, 2.5f);
        Ground(24f, 46f, 2.5f);
        Ground(50f, 64f, 2.5f);
        Question(10f, 3f);
        Brick(11f, 3f);
        Question(12f, 3f);
        Brick(13f, 3f);
        for (var x = 28f; x <= 31f; x += 1f)
        {
            Brick(x, 3f);
        }

        Pipe(34f, 1.6f);
        Pipe(40f, 2.2f);
        Step(56.5f, 1f);
        Step(57.5f, 2f);
        Step(58.5f, 3f);
        Gate(64f);
        for (var x = 4f; x <= 8f; x += 1f) AddCoin(x, 1f, 0f);
        AddCoin(21f, 3.4f, 0f);
        AddCoin(22f, 4.2f, 0f);
        AddCoin(23f, 3.4f, 0f);
        AddCoin(29.5f, 4.6f, 0f);
        AddCoin(47f, 3.4f, 0f);
        AddCoin(48f, 4.2f, 0f);
        AddCoin(49f, 3.4f, 0f);
        Goomba(14f, 7f, 18f);
        Goomba(31f, 25f, 32.5f);
        Goomba(43f, 41.5f, 45.5f);
        Goomba(53f, 51f, 55f);

        // ---- Section B: first-person corridor (a wide, 3D course with boxes to jump on and around)
        Ground(64f, 92f, 6f);
        Ground(96f, 116f, 6f);
        Ground(119f, 134f, 6f);
        Box(72f, -3f, 2f, 1.2f, 2f);
        Box(76f, 3f, 2f, 2f, 2f);
        Box(80f, 0f, 3f, 1f, 3f);
        Box(84f, -3f, 2f, 2f, 2f);
        Box(87f, 3f, 2f, 1.2f, 2f);
        Box(99f, -3f, 3f, 1.2f, 3f);
        Box(103f, 2f, 3f, 2f, 3f);
        Box(107f, -2f, 2f, 1.2f, 2f);
        Box(111f, 3f, 2f, 2f, 2f);
        Box(123f, 0f, 2f, 1.2f, 6f);
        Box(128f, -3f, 2f, 2f, 2f);
        Rails(64f, 134f, 6f);
        for (var x = 68f; x < 90f; x += 3f) AddCoin(x, 1f, Mathf.Sin(x) * 2f);
        AddCoin(72f, 2.4f, -3f);
        AddCoin(76f, 3.2f, 3f);
        AddCoin(84f, 3.2f, -3f);
        AddCoin(93f, 3f, 0f);
        AddCoin(94f, 3.6f, 0f);
        AddCoin(95f, 3f, 0f);
        AddCoin(99f, 2.4f, -3f);
        AddCoin(103f, 3.2f, 2f);
        AddCoin(111f, 3.2f, 3f);
        for (var x = 117f; x < 133f; x += 3f) AddCoin(x, 1f, Mathf.Cos(x) * 3f);
        Goomba(75f, 66f, 90f, 6f);
        Goomba(89f, 66f, 90f, 6f);
        Goomba(101f, 96f, 115f, 6f);
        Goomba(125f, 119f, 133f, 6f);
        Gate(134f);

        // ---- Section C: back to the side scroller
        Ground(134f, 150f, 6f);
        Ground(154f, 176f, 2.5f);
        Question(140f, 3f);
        Brick(141f, 3f);
        Question(142f, 3f);
        Pipe(146f, 1.6f);
        Pipe(160f, 2.2f);
        Brick(164f, 3f);
        Brick(165f, 3f);
        Question(166f, 3f);
        AddCoin(151f, 3.4f, 0f);
        AddCoin(152f, 4.2f, 0f);
        AddCoin(153f, 3.4f, 0f);
        for (var x = 168f; x <= 172f; x += 1f) AddCoin(x, 1f, 0f);
        Goomba(138f, 136f, 144f);
        Goomba(157f, 155f, 159f);
        Goomba(170f, 162f, 174f);
        Gate(176f);

        // ---- Section D: top-down arena
        Ground(176f, 236f, 11f);
        Box(184f, -6f, 4f, 2.5f, 4f);
        Box(184f, 6f, 4f, 2.5f, 4f);
        Box(194f, 0f, 4f, 2.5f, 8f);
        Box(204f, -7f, 6f, 2.5f, 3f);
        Box(204f, 7f, 6f, 2.5f, 3f);
        Box(214f, 0f, 3f, 2.5f, 3f);
        Box(222f, -5f, 4f, 2.5f, 4f);
        Box(222f, 5f, 4f, 2.5f, 4f);
        Box(230f, 0f, 2f, 2.5f, 10f);
        Rails(176f, 236f, 11f);
        var spots = new[]
        {
            new Vector2(182f, 0f), new Vector2(190f, -8f), new Vector2(190f, 8f), new Vector2(199f, -5f), new Vector2(199f, 5f),
            new Vector2(208f, 0f), new Vector2(209f, -8f), new Vector2(209f, 8f), new Vector2(218f, -6f), new Vector2(218f, 6f),
            new Vector2(226f, 0f), new Vector2(227f, 8f), new Vector2(227f, -8f), new Vector2(234f, 6f), new Vector2(234f, -6f)
        };
        foreach (var spot in spots) AddCoin(spot.x, 1f, spot.y);
        Goomba(188f, 178f, 200f, 11f);
        Goomba(200f, 178f, 210f, 11f);
        Goomba(210f, 196f, 222f, 11f);
        Goomba(216f, 206f, 226f, 11f);
        Goomba(226f, 214f, 234f, 11f);
        Goomba(232f, 220f, 235f, 11f);
        Gate(236f);

        // ---- Section E: the run to the flag
        Ground(236f, 256f, 11f);
        Ground(260f, 280f, 2.5f);
        Ground(283f, 310f, 2.5f);
        Pipe(246f, 2f);
        Question(250f, 3f);
        Brick(251f, 3f);
        Question(252f, 3f);
        Brick(253f, 3f);
        Pipe(266f, 1.6f);
        Pipe(272f, 2.2f);
        for (var i = 0; i < 5; i++) Step(285.5f + i, i + 1f);
        AddCoin(257f, 3.4f, 0f);
        AddCoin(258f, 4.2f, 0f);
        AddCoin(259f, 3.4f, 0f);
        AddCoin(281f, 3.4f, 0f);
        AddCoin(282f, 4.2f, 0f);
        AddCoin(238f, 1f, 0f);
        AddCoin(240f, 1f, 0f);
        for (var x = 262f; x <= 264f; x += 1f) AddCoin(x, 1f, 0f);
        Goomba(241f, 238f, 245f);
        Goomba(263f, 261f, 265f);
        Goomba(277f, 273.5f, 279f);
        BuildFlag();
    }

    private void BuildScenery()
    {
        for (var x = -10f; x < 330f; x += 24f)
        {
            if (x > 168f && x < 244f) continue; // keep the sky clear above the top-down arena
            var cloud = Group("Cloud", level, new Vector3(x, 9f + Mathf.Sin(x) * 1.5f, 13f));
            Part(PrimitiveType.Sphere, "Puff", cloud, Vector3.zero, new Vector3(4f, 1.8f, 1.2f), Color.white);
            Part(PrimitiveType.Sphere, "Puff", cloud, new Vector3(-2.2f, -0.2f, 0f), new Vector3(2.6f, 1.4f, 1.1f), Color.white);
            Part(PrimitiveType.Sphere, "Puff", cloud, new Vector3(2.4f, -0.1f, 0f), new Vector3(3f, 1.5f, 1.1f), Color.white);
        }

        for (var x = 0f; x < 330f; x += 30f)
        {
            if (x > 160f && x < 250f) continue;
            Part(PrimitiveType.Sphere, "Hill", level, new Vector3(x, -1f, 16f), new Vector3(16f, 8f, 6f), new Color(0.3f, 0.65f, 0.25f));
            Part(PrimitiveType.Sphere, "Hill", level, new Vector3(x + 14f, -1.5f, 20f), new Vector3(12f, 6f, 5f), new Color(0.22f, 0.55f, 0.2f));
        }
    }

    private void Ground(float x0, float x1, float zHalf)
    {
        var size = new Vector3(x1 - x0, 2f, zHalf * 2f);
        var center = new Vector3((x0 + x1) * 0.5f, -1f, 0f);
        Part(PrimitiveType.Cube, "Ground", level, center, size, Dirt);
        Part(PrimitiveType.Cube, "Grass", level, new Vector3(center.x, 0.06f, 0f), new Vector3(size.x, 0.12f, size.z), Grass);
        blocks.Add(new Block { Box = new Bounds(center, size) });
    }

    private Block AddBlock(Vector3 center, Vector3 size, Color color, bool question)
    {
        var visual = Beveled(question ? "Question block" : "Brick", level, center, size, color, 0.07f);
        var block = new Block
        {
            Box = new Bounds(center, size),
            Question = question,
            Skin = visual,
            Rest = center,
            Visual = visual.transform
        };
        blocks.Add(block);
        return block;
    }

    private void Brick(float x, float y)
    {
        AddBlock(new Vector3(x, y + 0.5f, 0f), Vector3.one, BrickColor, false);
        Part(PrimitiveType.Cube, "Mortar", level, new Vector3(x, y + 0.5f, -0.51f), new Vector3(0.9f, 0.04f, 0.02f), BrickEdge);
    }

    private void Question(float x, float y)
    {
        var block = AddBlock(new Vector3(x, y + 0.5f, 0f), Vector3.one, QuestionColor, true);
        var mark = Part(PrimitiveType.Cube, "Question mark", block.Visual, new Vector3(0f, 0f, -0.52f), new Vector3(0.28f, 0.45f, 0.05f), Color.white, true);
        mark.transform.SetParent(block.Visual, false);
    }

    private void Box(float x, float z, float width, float height, float depth)
    {
        var center = new Vector3(x, height * 0.5f, z);
        var size = new Vector3(width, height, depth);
        Beveled("Crate", level, center, size, BrickColor, 0.1f);
        Part(PrimitiveType.Cube, "Crate top", level, new Vector3(x, height + 0.01f, z), new Vector3(width - 0.2f, 0.04f, depth - 0.2f), BrickEdge);
        blocks.Add(new Block { Box = new Bounds(center, size) });
    }

    private void Step(float x, float height)
    {
        var center = new Vector3(x, height * 0.5f, 0f);
        var size = new Vector3(1f, height, 5f);
        Beveled("Step", level, center, size, BrickEdge, 0.06f);
        Part(PrimitiveType.Cube, "Step top", level, new Vector3(x, height + 0.01f, 0f), new Vector3(0.9f, 0.04f, 4.8f), BrickColor);
        blocks.Add(new Block { Box = new Bounds(center, size) });
    }

    private void Pipe(float x, float height)
    {
        Part(PrimitiveType.Cylinder, "Pipe", level, new Vector3(x, height * 0.5f, 0f), new Vector3(1.6f, height * 0.5f, 1.6f), PipeColor);
        Part(PrimitiveType.Cylinder, "Pipe lip", level, new Vector3(x, height - 0.2f, 0f), new Vector3(1.9f, 0.2f, 1.9f), PipeDark);
        blocks.Add(new Block { Box = new Bounds(new Vector3(x, height * 0.5f, 0f), new Vector3(1.6f, height, 1.6f)) });
    }

    // A glowing ring that marks where the camera and controls change.
    private void Gate(float x)
    {
        Ring("Gate ring", level, new Vector3(x, 2f, 0f), 1.8f, 0.09f, new Color(0.3f, 1f, 1f), true, new Vector3(0f, 0f, 90f));
        Ring("Gate ring", level, new Vector3(x, 2f, 0f), 1.4f, 0.05f, new Color(1f, 0.4f, 0.9f), true, new Vector3(0f, 0f, 90f));
    }

    private void Rails(float x0, float x1, float zHalf)
    {
        foreach (var side in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cube, "Rail", level, new Vector3((x0 + x1) * 0.5f, 0.45f, side * (zHalf + 0.15f)), new Vector3(x1 - x0, 0.1f, 0.1f), Color.white);
            for (var x = x0; x <= x1; x += 4f)
            {
                Part(PrimitiveType.Cube, "Post", level, new Vector3(x, 0.3f, side * (zHalf + 0.15f)), new Vector3(0.12f, 0.6f, 0.12f), Color.white);
            }
        }
    }

    private void BuildFlag()
    {
        Part(PrimitiveType.Cylinder, "Flag pole", level, new Vector3(FlagX, 4.2f, 0f), new Vector3(0.14f, 4.2f, 0.14f), Color.white);
        Part(PrimitiveType.Sphere, "Flag ball", level, new Vector3(FlagX, 8.5f, 0f), Vector3.one * 0.45f, QuestionColor, true);
        Part(PrimitiveType.Cube, "Flag", level, new Vector3(FlagX - 0.7f, 7.6f, 0f), new Vector3(1.3f, 0.9f, 0.05f), new Color(0.15f, 0.8f, 0.3f), true);
        Beveled("Castle", level, new Vector3(300f, 2.5f, 0f), new Vector3(5f, 5f, 3f), BrickColor, 0.1f);
        Part(PrimitiveType.Cube, "Castle door", level, new Vector3(300f, 1f, -1.52f), new Vector3(1.2f, 2f, 0.05f), Color.black);
        for (var i = -2; i <= 2; i += 2)
        {
            Part(PrimitiveType.Cube, "Merlon", level, new Vector3(300f + i, 5.3f, 0f), new Vector3(0.9f, 0.6f, 2.6f), BrickColor);
        }
    }

    private void AddCoin(float x, float y, float z)
    {
        var root = Group("Coin", level, new Vector3(x, y, z));
        Part(PrimitiveType.Cylinder, "Coin disc", root, Vector3.zero, new Vector3(0.55f, 0.05f, 0.55f), CoinColor, true, new Vector3(90f, 0f, 0f));
        coins.Add(new Coin { Root = root, Phase = Random.Range(0f, 6f) });
    }

    private void Goomba(float x, float min, float max, float zHalf = 0f)
    {
        var root = Group("Goomba", level, new Vector3(x, 0f, 0f));
        var brown = new Color(0.6f, 0.35f, 0.15f);
        Part(PrimitiveType.Sphere, "Cap", root, new Vector3(0f, 0.55f, 0f), new Vector3(0.95f, 0.8f, 0.95f), brown);
        Part(PrimitiveType.Cube, "Stalk", root, new Vector3(0f, 0.22f, 0f), new Vector3(0.5f, 0.3f, 0.5f), new Color(1f, 0.85f, 0.65f));
        foreach (var side in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cube, "Foot", root, new Vector3(side * 0.22f, 0.08f, 0.05f), new Vector3(0.3f, 0.16f, 0.45f), new Color(0.25f, 0.12f, 0.05f));
            foreach (var face in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cube, "Eye", root, new Vector3(side * 0.18f, 0.62f, face * 0.43f), new Vector3(0.2f, 0.24f, 0.05f), Color.white);
                Part(PrimitiveType.Cube, "Pupil", root, new Vector3(side * 0.18f, 0.6f, face * 0.455f), new Vector3(0.08f, 0.14f, 0.03f), Color.black);
                Part(PrimitiveType.Cube, "Brow", root, new Vector3(side * 0.18f, 0.78f, face * 0.44f), new Vector3(0.24f, 0.05f, 0.04f), Color.black, false, new Vector3(0f, 0f, side * -18f));
            }
        }

        enemies.Add(new Enemy
        {
            Root = root,
            Foot = new Vector3(x, 0f, 0f),
            Min = min,
            Max = max,
            ZHalf = zHalf,
            Dir = Random.value < 0.5f ? Vector3.left : Vector3.right
        });
    }

    // ================================================================ Player

    private void BuildPlayer()
    {
        player = Group("Player", null, Vector3.zero);
        Beveled("Overalls", player, new Vector3(0f, 0.52f, 0f), new Vector3(0.55f, 0.5f, 0.4f), Overalls, 0.06f);
        Beveled("Shirt", player, new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.28f, 0.42f), Shirt, 0.06f);
        Part(PrimitiveType.Sphere, "Head", player, new Vector3(0f, 1.2f, 0f), Vector3.one * 0.5f, Skin);
        Part(PrimitiveType.Sphere, "Cap", player, new Vector3(0f, 1.36f, 0f), new Vector3(0.54f, 0.34f, 0.54f), Shirt);
        Part(PrimitiveType.Cube, "Brim", player, new Vector3(0f, 1.3f, 0.3f), new Vector3(0.46f, 0.06f, 0.3f), Shirt);
        Part(PrimitiveType.Sphere, "Nose", player, new Vector3(0f, 1.17f, 0.27f), Vector3.one * 0.14f, Skin);
        Part(PrimitiveType.Cube, "Mustache", player, new Vector3(0f, 1.08f, 0.24f), new Vector3(0.3f, 0.06f, 0.08f), new Color(0.2f, 0.1f, 0.05f));
        foreach (var side in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cube, "Arm", player, new Vector3(side * 0.38f, 0.8f, 0f), new Vector3(0.16f, 0.4f, 0.18f), Shirt);
            Part(PrimitiveType.Sphere, "Glove", player, new Vector3(side * 0.38f, 0.58f, 0f), Vector3.one * 0.2f, Color.white);
            var leg = Group(side < 0 ? "Left leg" : "Right leg", player, new Vector3(side * 0.14f, 0.34f, 0f));
            Part(PrimitiveType.Cube, "Leg", leg, new Vector3(0f, -0.1f, 0f), new Vector3(0.2f, 0.34f, 0.22f), Overalls);
            Part(PrimitiveType.Cube, "Shoe", leg, new Vector3(0f, -0.27f, 0.05f), new Vector3(0.24f, 0.12f, 0.34f), new Color(0.4f, 0.2f, 0.08f));
            if (side < 0) legLeft = leg; else legRight = leg;
        }

        playerRenderers = player.GetComponentsInChildren<Renderer>();
        foot = checkpoint;
    }

    private void BuildSounds()
    {
        jumpClip = Tone("Jump", 330f, 760f, 0.2f, 0.22f, true);
        coinClip = Tone("Coin", 988f, 1319f, 0.2f, 0.25f, false);
        stompClip = Tone("Stomp", 320f, 80f, 0.16f, 0.35f, true);
        bumpClip = Tone("Bump", 160f, 120f, 0.1f, 0.4f, true);
        dieClip = Tone("Die", 640f, 90f, 0.75f, 0.28f, true);
        winClip = Tone("Win", 523f, 1568f, 1f, 0.28f, false);
    }

    private static AudioClip Tone(string name, float from, float to, float length, float volume, bool square)
    {
        const int rate = 44100;
        var count = (int)(length * rate);
        var data = new float[count];
        var phase = 0f;
        for (var i = 0; i < count; i++)
        {
            var t = (float)i / count;
            phase += 2f * Mathf.PI * Mathf.Lerp(from, to, t) / rate;
            var wave = square ? Mathf.Sign(Mathf.Sin(phase)) : Mathf.Sin(phase);
            data[i] = wave * (1f - t) * volume;
        }

        var clip = AudioClip.Create(name, count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Play(AudioClip clip)
    {
        audioOut.PlayOneShot(clip);
    }

    // ================================================================ Update

    private Zone ZoneAt(float x)
    {
        foreach (var candidate in zones)
        {
            if (x >= candidate.From && x < candidate.To) return candidate;
        }

        return zones[zones.Count - 1];
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SceneManager.LoadScene("SkyfortIntro");
            return;
        }

        if ((gameOver || won) && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)))
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        var dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        AnimateWorld(dt);
        if (gameOver || won)
        {
            return;
        }

        if (dying)
        {
            UpdateDying(dt);
        }
        else
        {
            StepPlayer(dt);
            CheckCoins();
            UpdateEnemies(dt);
        }

        var current = ZoneAt(foot.x);
        if (current != zone)
        {
            zone = current;
            if (!dying)
            {
                checkpoint = new Vector3(zone.From + 2f, 0.05f, 0f);
            }

            Toast(zone.Toast);
            StartBlend();
        }

        UpdatePlayerVisual(dt);
    }

    private void StepPlayer(float dt)
    {
        var right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        var left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        var up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        var down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        var h = (right ? 1f : 0f) - (left ? 1f : 0f);
        var v = (up ? 1f : 0f) - (down ? 1f : 0f);

        Vector2 wish;
        switch (zone.View)
        {
            case View.First:
                wish = new Vector2(v, -h); // forward is +X, and the right-hand side is -Z
                break;
            case View.Top:
                wish = new Vector2(h, v);
                break;
            default:
                wish = new Vector2(h, 0f);
                break;
        }

        if (wish.sqrMagnitude > 1f)
        {
            wish.Normalize();
        }

        var speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? RunSpeed : WalkSpeed;
        var accel = wish.sqrMagnitude > 0.01f ? (grounded ? 48f : 30f) : (grounded ? 60f : 8f);
        velocity.x = Mathf.MoveTowards(velocity.x, wish.x * speed, accel * dt);
        if (zone.View == View.Side)
        {
            velocity.z = Mathf.Clamp(-foot.z * 8f, -7f, 7f); // glide back onto the 2D lane
        }
        else
        {
            velocity.z = Mathf.MoveTowards(velocity.z, wish.y * speed, accel * dt);
        }

        // Jump: buffered and with a little coyote time; releasing early cuts the jump short.
        var jumpHeld = Input.GetKey(KeyCode.Space) || (zone.View == View.Side && up);
        var jumpPressed = Input.GetKeyDown(KeyCode.Space) || (zone.View == View.Side && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)));
        jumpBuffer = jumpPressed ? 0.12f : jumpBuffer - dt;
        coyote = grounded ? 0.1f : coyote - dt;
        if (jumpBuffer > 0f && coyote > 0f)
        {
            velocity.y = JumpSpeed;
            jumping = true;
            jumpBuffer = 0f;
            coyote = 0f;
            grounded = false;
            Play(jumpClip);
        }

        if (jumping && !jumpHeld && velocity.y > 4.5f)
        {
            velocity.y = 4.5f;
            jumping = false;
        }

        velocity.y = Mathf.Max(velocity.y - Gravity * dt, -32f);

        // Move one axis at a time against the level boxes.
        foot.x += velocity.x * dt;
        ResolveSide(0);
        foot.z += velocity.z * dt;
        ResolveSide(2);
        var limit = zone.View == View.Side ? 2.4f : zone.ZHalf - 0.4f;
        foot.z = Mathf.Clamp(foot.z, -limit, limit);
        foot.x = Mathf.Max(foot.x, 0.5f);

        grounded = false;
        foot.y += velocity.y * dt;
        ResolveVertical();

        if (foot.y < -10f)
        {
            Die();
        }

        if (!won && foot.x >= FlagX)
        {
            won = true;
            score += 1000 + Mathf.RoundToInt(Mathf.Clamp(foot.y, 0f, 8f) * 250f);
            Play(winClip);
            Toast("COURSE CLEAR!");
        }
    }

    private Bounds PlayerBounds()
    {
        return new Bounds(foot + Vector3.up * (PlayerHeight * 0.5f), new Vector3(PlayerWidth, PlayerHeight, PlayerWidth));
    }

    private static bool Overlaps(Bounds a, Bounds b)
    {
        const float e = 0.0005f;
        return a.min.x < b.max.x - e && a.max.x > b.min.x + e
            && a.min.y < b.max.y - e && a.max.y > b.min.y + e
            && a.min.z < b.max.z - e && a.max.z > b.min.z + e;
    }

    private void ResolveSide(int axis)
    {
        var half = PlayerWidth * 0.5f + 0.001f;
        foreach (var block in blocks)
        {
            if (!Overlaps(PlayerBounds(), block.Box)) continue;
            if (axis == 0)
            {
                foot.x = foot.x < block.Box.center.x ? block.Box.min.x - half : block.Box.max.x + half;
                velocity.x = 0f;
            }
            else
            {
                foot.z = foot.z < block.Box.center.z ? block.Box.min.z - half : block.Box.max.z + half;
                velocity.z = 0f;
            }
        }
    }

    private void ResolveVertical()
    {
        foreach (var block in blocks)
        {
            if (!Overlaps(PlayerBounds(), block.Box)) continue;
            if (velocity.y <= 0f)
            {
                foot.y = block.Box.max.y;
                grounded = true;
                jumping = false;
            }
            else
            {
                foot.y = block.Box.min.y - PlayerHeight - 0.001f;
                BumpBlock(block);
            }

            velocity.y = 0f;
        }
    }

    private void BumpBlock(Block block)
    {
        block.Bump = 0.18f;
        if (block.Question && !block.Used)
        {
            block.Used = true;
            block.Skin.sharedMaterial = Mat(UsedColor, false);
            coinCount++;
            score += 200;
            Play(coinClip);
        }
        else
        {
            Play(bumpClip);
        }
    }

    private void CheckCoins()
    {
        var center = foot + Vector3.up * 0.8f;
        foreach (var coin in coins)
        {
            if (coin.Taken) continue;
            if ((coin.Root.position - center).sqrMagnitude < 1.1f * 1.1f)
            {
                coin.Taken = true;
                coin.Root.gameObject.SetActive(false);
                coinCount++;
                score += 100;
                Play(coinClip);
            }
        }
    }

    private void UpdateEnemies(float dt)
    {
        var bounds = PlayerBounds();
        foreach (var enemy in enemies)
        {
            if (!enemy.Alive)
            {
                if (enemy.Squash > 0f)
                {
                    enemy.Squash -= dt;
                    if (enemy.Squash <= 0f) enemy.Root.gameObject.SetActive(false);
                }

                continue;
            }

            MoveEnemy(enemy, dt);
            enemy.Root.position = enemy.Foot;
            enemy.Root.rotation = Quaternion.LookRotation(enemy.Dir);

            var enemyBounds = new Bounds(enemy.Foot + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f));
            if (!Overlaps(bounds, enemyBounds)) continue;
            if (velocity.y < -0.5f && foot.y > enemy.Foot.y + 0.35f)
            {
                enemy.Alive = false;
                enemy.Squash = 0.5f;
                enemy.Root.localScale = new Vector3(1.2f, 0.2f, 1.2f);
                velocity.y = 10f;
                jumping = false;
                score += 100;
                Play(stompClip);
            }
            else
            {
                Die();
                return;
            }
        }
    }

    private void MoveEnemy(Enemy enemy, float dt)
    {
        if (enemy.ZHalf <= 0f)
        {
            enemy.Foot.x += enemy.Dir.x * 1.8f * dt;
            if (enemy.Foot.x < enemy.Min) enemy.Dir = Vector3.right;
            if (enemy.Foot.x > enemy.Max) enemy.Dir = Vector3.left;
            return;
        }

        // 3D sections: wander, and in the top-down arena chase the player when close.
        enemy.Timer -= dt;
        var toPlayer = foot - enemy.Foot;
        toPlayer.y = 0f;
        var chase = ZoneAt(enemy.Foot.x).View == View.Top && toPlayer.sqrMagnitude < 10f * 10f;
        var speed = chase ? 3.2f : 1.6f;
        if (chase)
        {
            enemy.Dir = toPlayer.normalized;
        }
        else if (enemy.Timer <= 0f)
        {
            var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            enemy.Dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            enemy.Timer = Random.Range(1.5f, 3f);
        }

        var next = enemy.Foot + enemy.Dir * speed * dt;
        next.x = Mathf.Clamp(next.x, enemy.Min, enemy.Max);
        next.z = Mathf.Clamp(next.z, -enemy.ZHalf + 0.6f, enemy.ZHalf - 0.6f);
        var box = new Bounds(next + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f));
        foreach (var block in blocks)
        {
            if (block.Box.max.y > 0.2f && Overlaps(box, block.Box))
            {
                enemy.Timer = 0f;
                if (!chase)
                {
                    return;
                }

                // Slide along the wall: try each axis on its own.
                var slideX = new Vector3(next.x, enemy.Foot.y, enemy.Foot.z);
                var slideZ = new Vector3(enemy.Foot.x, enemy.Foot.y, next.z);
                var boxX = new Bounds(slideX + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f));
                enemy.Foot = HitsBlock(boxX) ? (HitsBlock(new Bounds(slideZ + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f))) ? enemy.Foot : slideZ) : slideX;
                return;
            }
        }

        enemy.Foot = next;
    }

    private bool HitsBlock(Bounds box)
    {
        foreach (var block in blocks)
        {
            if (block.Box.max.y > 0.2f && Overlaps(box, block.Box)) return true;
        }

        return false;
    }

    private void Die()
    {
        if (dying || won)
        {
            return;
        }

        dying = true;
        deathTimer = 1.4f;
        velocity = new Vector3(0f, 12f, 0f);
        lives--;
        Play(dieClip);
    }

    private void UpdateDying(float dt)
    {
        velocity.y -= Gravity * dt;
        foot.y += velocity.y * dt;
        deathTimer -= dt;
        if (deathTimer > 0f)
        {
            return;
        }

        if (lives <= 0)
        {
            gameOver = true;
            return;
        }

        dying = false;
        foot = checkpoint;
        velocity = Vector3.zero;
        zone = ZoneAt(foot.x);
        StartBlend();
    }

    private void AnimateWorld(float dt)
    {
        foreach (var coin in coins)
        {
            if (coin.Taken) continue;
            coin.Phase += dt * 3f;
            coin.Root.rotation = Quaternion.Euler(0f, coin.Phase * 120f, 0f);
        }

        foreach (var block in blocks)
        {
            if (block.Visual == null) continue;
            block.Bump = Mathf.Max(0f, block.Bump - dt);
            block.Visual.position = block.Rest + Vector3.up * (Mathf.Sin(block.Bump / 0.18f * Mathf.PI) * 0.25f);
        }
    }

    private void UpdatePlayerVisual(float dt)
    {
        player.position = foot;
        var moving = new Vector2(velocity.x, velocity.z);
        if (zone.View == View.First)
        {
            facing = 90f;
        }
        else if (moving.sqrMagnitude > 0.5f)
        {
            facing = Mathf.LerpAngle(facing, Mathf.Atan2(moving.x, moving.y) * Mathf.Rad2Deg, 1f - Mathf.Exp(-18f * dt));
        }

        player.rotation = Quaternion.Euler(0f, facing, dying ? deathTimer * 500f : 0f);
        stride += moving.magnitude * dt * 1.6f;
        var swing = grounded ? Mathf.Sin(stride * 6f) * Mathf.Min(moving.magnitude / WalkSpeed, 1f) * 45f : 25f;
        legLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);
        legRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);

        var visible = !(zone.View == View.First && blend > 0.5f) || dying;
        if (playerRenderers[0].enabled != visible)
        {
            foreach (var renderer in playerRenderers)
            {
                renderer.enabled = visible;
            }
        }
    }

    // ================================================================ Camera

    private void StartBlend()
    {
        fromPosition = cam.transform.position;
        fromRotation = cam.transform.rotation;
        fromFov = cam.fieldOfView;
        blend = 0f;
    }

    private Vector3 SideTarget(out Quaternion rotation, out float fov)
    {
        var focus = new Vector3(sideFocusX, sideFocusY, 0f);
        var position = focus + new Vector3(0f, 0.5f, -15f);
        rotation = Quaternion.LookRotation(focus - position);
        fov = 38f;
        return position;
    }

    private void LateUpdate()
    {
        if (cam == null || zone == null)
        {
            return;
        }

        var dt = Time.deltaTime;
        sideFocusX = Mathf.Lerp(sideFocusX, foot.x + 2.5f, 1f - Mathf.Exp(-8f * dt));
        sideFocusY = Mathf.Lerp(sideFocusY, Mathf.Max(3f, foot.y + 2.5f), 1f - Mathf.Exp(-4f * dt));

        Vector3 position;
        Quaternion rotation;
        float fov;
        switch (zone.View)
        {
            case View.First:
                position = foot + new Vector3(0.1f, PlayerHeight - 0.1f, 0f);
                rotation = Quaternion.Euler(0f, 90f, 0f);
                fov = 82f;
                break;
            case View.Top:
                position = new Vector3(foot.x, 17f, foot.z - 6.5f);
                rotation = Quaternion.Euler(68f, 0f, 0f);
                fov = 45f;
                break;
            default:
                position = SideTarget(out rotation, out fov);
                break;
        }

        blend = Mathf.Min(1f, blend + dt / 0.9f);
        var t = blend * blend * (3f - 2f * blend);
        cam.transform.SetPositionAndRotation(
            Vector3.Lerp(fromPosition, position, t),
            Quaternion.Slerp(fromRotation, rotation, t));
        cam.fieldOfView = Mathf.Lerp(fromFov, fov, t);
    }

    // ================================================================ HUD

    private void Toast(string text)
    {
        toast = text;
        toastUntil = Time.time + 3.5f;
    }

    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 26 };
        }

        var scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        var width = Screen.width / scale;

        Shadowed(new Rect(24f, 12f, 360f, 40f), $"PIXEL RUNNER   SCORE {score:000000}", Color.white, TextAnchor.UpperLeft, 26);
        Shadowed(new Rect(width - 384f, 12f, 360f, 40f), $"COINS x{coinCount:00}    LIVES x{Mathf.Max(lives, 0)}", new Color(1f, 0.9f, 0.3f), TextAnchor.UpperRight, 26);
        Shadowed(new Rect(0f, 52f, width, 30f), zone != null ? ViewName(zone.View) : "", new Color(0.8f, 0.95f, 1f), TextAnchor.UpperCenter, 18);

        if (Time.time < toastUntil)
        {
            Shadowed(new Rect(0f, 110f, width, 60f), toast, Color.white, TextAnchor.UpperCenter, 36);
        }

        Shadowed(new Rect(0f, 690f, width, 26f), "A/D move   SPACE jump   SHIFT run   (first person: W/S walk, A/D strafe)   (top down: WASD)   ESC menu", new Color(1f, 1f, 1f, 0.75f), TextAnchor.UpperCenter, 14);

        if (won)
        {
            Shadowed(new Rect(0f, 280f, width, 80f), "COURSE CLEAR!", new Color(1f, 0.9f, 0.3f), TextAnchor.UpperCenter, 64);
            Shadowed(new Rect(0f, 360f, width, 40f), $"SCORE {score:000000}      ENTER to play again, ESC for menu", Color.white, TextAnchor.UpperCenter, 24);
        }
        else if (gameOver)
        {
            Shadowed(new Rect(0f, 280f, width, 80f), "GAME OVER", new Color(1f, 0.3f, 0.2f), TextAnchor.UpperCenter, 64);
            Shadowed(new Rect(0f, 360f, width, 40f), "ENTER to try again, ESC for menu", Color.white, TextAnchor.UpperCenter, 24);
        }
    }

    private static string ViewName(View view) => view switch
    {
        View.First => "- FIRST PERSON -",
        View.Top => "- TOP DOWN -",
        _ => "- SIDE SCROLLER -"
    };

    private void Shadowed(Rect rect, string text, Color color, TextAnchor anchor, int size)
    {
        style.fontSize = size;
        style.alignment = anchor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }
}
