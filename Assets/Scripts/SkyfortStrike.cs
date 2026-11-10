using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static SkyfortModels;
using Random = UnityEngine.Random;

// Skyfort Strike: an isometric, forward-scrolling altitude shooter. The fighter flies on its own; you steer
// left/right, climb/dive around pyramids, castles and force fields, shoot ground targets at their height, keep
// your fuel up by destroying fuel tanks, and face the Warden guardian at the end of each level.
//
// The camera, lights, fighter, Warden, effects and HUD live in the scene, and the enemies and projectiles are
// prefabs; this component only references them (see the Inspector). Only the level itself is streamed in at
// runtime, chunk by chunk, under the "Level" object. Edit the scene objects and prefabs directly in the Editor.
public sealed partial class SkyfortStrike : MonoBehaviour
{
    private const float CorridorHalfWidth = 6.2f;
    private const float WallHalfWidth = 8f;
    private const float MinAltitude = 0.7f;
    private const float MaxAltitude = 7f;
    private const float CeilingHeight = 9.5f;
    private const float ChunkLength = 18f;
    private const float SpawnAhead = 120f;
    private const float DespawnBehind = 30f;
    private const float BaseSpeed = 13f;
    private const float FuelMax = 100f;
    private const float FuelDrain = 3f;
    private const float FuelRefill = 32f;
    private const int StartingShips = 3;
    private const float LaserSpeed = 50f;
    private const float LaserRange = 48f;
    private const float FireInterval = 0.15f;
    private const float EnemyBoltSpeed = 20f;
    private const int LaserPoolSize = 32;
    private const int BoltPoolSize = 48;
    private const string HighScoreKey = "SkyfortStrike.HighScore";

    private static readonly Vector3 ShipHalf = new(0.7f, 0.28f, 0.8f);

    private enum ChunkKind { Entry, FieldLow, Pyramids, PyramidRow, CastleWall, CastleKeep, TurretRow, FuelDepot, RadarSite, Mixed, Exit, Space, Arena, BossArena, Open, Drift, Obelisks, IceSpires }
    private enum TargetKind { Turret, FuelTank, Radar, Drone }
    private enum Phase { Loading, Countdown, Playing, Respawning, GameOver }

    private sealed class Chunk
    {
        public Transform Root;
        public float StartZ;
        public bool Space;
        public SkyfortTheme Theme;
        public ChunkKind Kind;
        public bool Planned;      // came from the level plan (so it is put back when a crash discards it)
        public bool FirstPerson;  // flown through the cockpit camera
    }

    private struct Solid
    {
        public Bounds Bounds;
        public Chunk Chunk;
        public bool Pyramid;
    }

    private sealed class Target
    {
        public TargetKind Kind;
        public Transform Transform;
        public Transform Head;
        public Vector3 Half;
        public Vector3 Center;
        public Vector3 Origin;
        public int Health;
        public int Score;
        public float NextFire;
        public float Phase;
        public Chunk Chunk;
    }

    private sealed class Projectile
    {
        public Transform Transform;
        public Vector3 Velocity;
        public float Life;
        public bool Active;
    }

    private readonly List<Chunk> chunks = new();
    private readonly List<Solid> solids = new();
    private readonly List<Target> targets = new();
    private readonly List<Projectile> lasers = new();
    private readonly List<Projectile> bolts = new();
    private readonly Queue<(ChunkKind Kind, SkyfortTheme Theme, bool FirstPerson)> plan = new();
    private readonly SkyfortHudState hud = new();

    [Header("Scene objects")]
    [SerializeField] private Camera view;
    [Tooltip("Retro display filter on the camera (16-BIT / CRT / ARCADE / ATARI / MONO LCD / MODERN, cycled with M).")]
    [SerializeField] private SkyfortRetroFilter retroFilter;
    [SerializeField] private Rigidbody shipBody;
    [SerializeField] private Transform shipModel;
    [SerializeField] private Transform shipShadow;
    [SerializeField] private ParticleSystem engineFx;
    [SerializeField] private ParticleSystem explosionFx;
    [SerializeField] private Light flash;
    [SerializeField] private UIDocument hudDocument;
    [SerializeField] private AudioSource sfx;
    [SerializeField] private AudioSource engine;
    [Tooltip("Parent for the level chunks streamed in at runtime.")]
    [SerializeField] private Transform levelRoot;
    [Tooltip("Parent for the pooled lasers and enemy bolts.")]
    [SerializeField] private Transform poolRoot;

    [Header("The Warden (boss)")]
    [SerializeField] private Transform boss;
    [SerializeField] private Transform bossLeftCannon;
    [SerializeField] private Transform bossRightCannon;
    [SerializeField] private Renderer bossCore;

    [Header("Prefabs")]
    [SerializeField] private GameObject turretPrefab;
    [SerializeField] private GameObject fuelTankPrefab;
    [SerializeField] private GameObject radarPrefab;
    [SerializeField] private GameObject dronePrefab;
    [SerializeField] private GameObject laserPrefab;
    [SerializeField] private GameObject boltPrefab;
    [SerializeField] private Material forceFieldMaterial;

    private Transform ship;
    private Material fieldMaterial;
    private float bossDistance;
    private float bossEndsAt;
    private float bossNextFire;
    private int bossHealth;
    private int bossMaxHealth;
    private bool bossPending;
    private bool bossActive;
    private bool bossRetreating;
    private float bossTriggerZ;

    private SkyfortSounds sounds;

    private Phase phase = Phase.Loading;
    private int runId;
    private int level = 1;
    private int score;
    private int highScore;
    private int ships = StartingShips;
    private float fuel = FuelMax;
    private float speed = BaseSpeed;
    private float spawnZ;
    private float nextFire;
    private float invulnerableUntil;
    private float nextFuelAlarm;
    private bool paused;
    private int safeChunks;
    private float fpBlend;
    private bool cockpitActive;
    private Quaternion isoRotation = Quaternion.Euler(33f, -45f, 0f);
    private Transform reticle;
    private Vector3 bounce;
    private float nextBounceFx;
    private bool safeSpace;
    private SkyfortTheme lastTheme = SkyfortThemes.Skyfort;
    private Vector2 steer;

    // Game logic reads the physics position; the transform is interpolated for smooth visuals only.
    private Vector3 ShipPosition => shipBody.position;

    private void Awake()
    {
        ship = shipBody != null ? shipBody.transform : null;
        // Animate a copy so play mode never edits the material asset itself.
        fieldMaterial = forceFieldMaterial != null ? new Material(forceFieldMaterial) : null;
        QualitySettings.shadowDistance = 90f;
        if (shipModel != null)
        {
            SkyfortFighter.Build(shipModel);
        }

        if (view != null)
        {
            isoRotation = view.transform.rotation;
        }
    }

    private bool HasSceneReferences()
    {
        var missing = new List<string>();
        void Check(UnityEngine.Object value, string name)
        {
            if (value == null) missing.Add(name);
        }

        Check(view, nameof(view));
        Check(retroFilter, nameof(retroFilter));
        Check(shipBody, nameof(shipBody));
        Check(shipModel, nameof(shipModel));
        Check(shipShadow, nameof(shipShadow));
        Check(engineFx, nameof(engineFx));
        Check(explosionFx, nameof(explosionFx));
        Check(flash, nameof(flash));
        Check(hudDocument, nameof(hudDocument));
        Check(sfx, nameof(sfx));
        Check(engine, nameof(engine));
        Check(levelRoot, nameof(levelRoot));
        Check(poolRoot, nameof(poolRoot));
        Check(boss, nameof(boss));
        Check(bossLeftCannon, nameof(bossLeftCannon));
        Check(bossRightCannon, nameof(bossRightCannon));
        Check(bossCore, nameof(bossCore));
        Check(turretPrefab, nameof(turretPrefab));
        Check(fuelTankPrefab, nameof(fuelTankPrefab));
        Check(radarPrefab, nameof(radarPrefab));
        Check(dronePrefab, nameof(dronePrefab));
        Check(laserPrefab, nameof(laserPrefab));
        Check(boltPrefab, nameof(boltPrefab));
        Check(forceFieldMaterial, nameof(forceFieldMaterial));
        CheckPowerUpReferences(Check);
        if (missing.Count == 0) return true;

        Debug.LogError($"SkyfortStrike is missing scene references ({string.Join(", ", missing)}). " +
                       "Run Skyfort > Rebuild Scene and Prefabs to regenerate them.", this);
        return false;
    }

    private async void Start()
    {
        if (!HasSceneReferences())
        {
            enabled = false;
            return;
        }

        Application.targetFrameRate = 60;
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        boss.gameObject.SetActive(false);
        SkyfortHud.Build(hudDocument, hud);
        RefreshHud();
        hud.Banner = "SKYFORT STRIKE";
        hud.SubBanner = "Spinning up...";

        try
        {
            // Unity 6 Awaitable: synthesize audio on a worker thread while object pools prewarm asynchronously.
            var token = destroyCancellationToken;
            var audioTask = SkyfortAudio.LoadAsync(token);
            await PrewarmPoolsAsync();
            sounds = await audioTask;
            engine.clip = sounds.Engine;
            engine.Play();
            NewGame();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Update()
    {
        if (phase == Phase.Loading)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            NewGame();
            return;
        }

        // M (display Mode) is kept away from the WASD flight keys so it is not pressed by accident.
        if (Input.GetKeyDown(KeyCode.M))
        {
            ShowToast($"DISPLAY: {SkyfortRetroFilter.Label(retroFilter.Cycle())}", Cyan);
        }

        if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape))
        {
            paused = !paused;
            Time.timeScale = paused ? 0f : 1f;
            hud.Banner = paused ? "PAUSED" : (phase == Phase.Playing ? "" : hud.Banner);
            AudioListener.pause = paused;
        }

        if (paused)
        {
            return;
        }

        if (phase == Phase.GameOver && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
        {
            NewGame();
            return;
        }

        AnimateWorld();
        UpdateProjectiles();
        if (phase == Phase.Playing)
        {
            UpdateShip();
            UpdateTargets();
            UpdateBoss();
            CheckShipCollisions();
            UpdatePickups();
            UpdatePowerUps();
        }

        StreamLevel();
        RefreshHud();
    }

    private void FixedUpdate()
    {
        if (phase != Phase.Playing || shipBody == null)
        {
            return;
        }

        shipBody.interpolation = RigidbodyInterpolation.Interpolate;

        // Unity 6 Rigidbody API: linearVelocity (formerly velocity).
        var position = shipBody.position;
        var velocity = new Vector3(steer.x * 9f, steer.y * 6.5f, speed) + bounce;
        bounce *= Mathf.Exp(-3.2f * Time.fixedDeltaTime);
        if ((position.x <= -CorridorHalfWidth && velocity.x < 0f) || (position.x >= CorridorHalfWidth && velocity.x > 0f))
        {
            velocity.x = 0f;
        }

        if ((position.y <= MinAltitude && velocity.y < 0f) || (position.y >= MaxAltitude && velocity.y > 0f))
        {
            velocity.y = 0f;
        }

        shipBody.position = new Vector3(
            Mathf.Clamp(position.x, -CorridorHalfWidth, CorridorHalfWidth),
            Mathf.Clamp(position.y, MinAltitude, MaxAltitude),
            position.z);
        shipBody.linearVelocity = velocity;
    }

    // Cockpit stretches blend the isometric orthographic camera into a perspective view from the ship's nose and
    // back, by interpolating the projection matrix, camera position and rotation together.
    private void UpdateCameraBlend()
    {
        var aheadChunk = ChunkAt(ShipPosition.z + 12f);
        var wantCockpit = aheadChunk != null && aheadChunk.FirstPerson;
        fpBlend = Mathf.MoveTowards(fpBlend, wantCockpit ? 1f : 0f, Time.deltaTime / 1.2f);
        var t = fpBlend * fpBlend * (3f - 2f * fpBlend);

        if (wantCockpit != cockpitActive)
        {
            cockpitActive = wantCockpit;
            if (phase == Phase.Playing)
            {
                ShowToast(wantCockpit ? "COCKPIT VIEW" : "TACTICAL VIEW", Cyan);
            }
        }

        var focus = new Vector3(0f, 2.5f, ship.position.z + 7f);
        var isoPosition = focus - isoRotation * Vector3.forward * 60f;
        var cockpitPosition = ship.position + new Vector3(0f, 0.45f, 1.1f);
        var cockpitRotation = Quaternion.Euler(-steer.y * 3f, steer.x * 2f, -steer.x * 6f);

        view.transform.SetPositionAndRotation(
            Vector3.Lerp(isoPosition, cockpitPosition, t),
            Quaternion.Slerp(isoRotation, cockpitRotation, t));

        if (t <= 0.0005f)
        {
            view.ResetProjectionMatrix();
        }
        else
        {
            var aspect = view.aspect;
            var size = view.orthographicSize;
            var ortho = Matrix4x4.Ortho(-size * aspect, size * aspect, -size, size, 1f, 200f);
            var persp = Matrix4x4.Perspective(78f, aspect, 0.3f, 110f);
            var blended = new Matrix4x4();
            for (var i = 0; i < 16; i++)
            {
                blended[i] = Mathf.Lerp(ortho[i], persp[i], t);
            }

            view.projectionMatrix = blended;
        }

        // The ship model would sit right in front of the lens; a reticle shows where the lasers go.
        var blinking = Time.time < invulnerableUntil && Mathf.Repeat(Time.time * 10f, 1f) < 0.35f;
        shipModel.gameObject.SetActive(t < 0.7f && !blinking);
        if (reticle == null)
        {
            reticle = BuildReticle();
        }

        reticle.gameObject.SetActive(t > 0.7f && phase == Phase.Playing);
        reticle.position = ship.position + new Vector3(0f, 0f, 14f);
    }

    private Transform BuildReticle()
    {
        var root = Group("Reticle", null, Vector3.zero);
        var color = new Color(0.3f, 1f, 0.5f);
        Part(PrimitiveType.Cube, "Reticle bar", root, new Vector3(0f, 0.55f, 0f), new Vector3(0.06f, 0.4f, 0.06f), color, true);
        Part(PrimitiveType.Cube, "Reticle bar", root, new Vector3(0f, -0.55f, 0f), new Vector3(0.06f, 0.4f, 0.06f), color, true);
        Part(PrimitiveType.Cube, "Reticle bar", root, new Vector3(0.55f, 0f, 0f), new Vector3(0.4f, 0.06f, 0.06f), color, true);
        Part(PrimitiveType.Cube, "Reticle bar", root, new Vector3(-0.55f, 0f, 0f), new Vector3(0.4f, 0.06f, 0.06f), color, true);
        return root;
    }

    private void LateUpdate()
    {
        if (view == null || ship == null)
        {
            return;
        }

        UpdateCameraBlend();
        shipShadow.position = new Vector3(ship.position.x, 0.03f, ship.position.z);
        var shadowScale = Mathf.Lerp(1f, 0.55f, (ship.position.y - MinAltitude) / (MaxAltitude - MinAltitude));
        shipShadow.localScale = new Vector3(1.5f * shadowScale, 0.01f, 1.9f * shadowScale);
        shipShadow.gameObject.SetActive(ship.gameObject.activeSelf);
    }

    // ---------------------------------------------------------------- Game flow

    private async void NewGame()
    {
        var id = ++runId;
        paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        level = 1;
        score = 0;
        fpBlend = 0f;
        cockpitActive = false;
        ships = StartingShips;
        speed = BaseSpeed;
        ClearWorld();
        ResetBoss();
        plan.Clear();
        EnqueueLevel(level);
        ResetShip(0f);
        invulnerableUntil = 0f;
        safeChunks = 0;
        spawnZ = -10f;
        lastTheme = SkyfortThemes.ForLevel(level);
        StreamLevel();
        UpdateAtmosphere(true);

        try
        {
            await CountdownAsync(id, $"LEVEL {level}  -  {lastTheme.Name}");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Awaitable CountdownAsync(int id, string title)
    {
        phase = Phase.Countdown;
        engine.volume = 0.25f;
        for (var count = 3; count > 0; count--)
        {
            hud.Banner = count.ToString();
            hud.SubBanner = title;
            sfx.PlayOneShot(sounds.Beep);
            await Awaitable.WaitForSecondsAsync(0.8f, destroyCancellationToken);
            if (id != runId) return;
        }

        hud.Banner = "";
        hud.SubBanner = "";
        sfx.PlayOneShot(sounds.Go);
        phase = Phase.Playing;
    }

    private async void CrashAsync(string reason)
    {
        if (phase != Phase.Playing)
        {
            return;
        }

        var id = runId;
        phase = Phase.Respawning;
        Explode(ShipPosition, Cyan, 70, true);
        ship.gameObject.SetActive(false);
        shipBody.linearVelocity = Vector3.zero;
        ships--;
        hud.Banner = reason;
        hud.SubBanner = ships > 0 ? $"{ships} ship{(ships == 1 ? "" : "s")} left" : "";

        try
        {
            await Awaitable.WaitForSecondsAsync(1.8f, destroyCancellationToken);
            if (id != runId) return;

            if (ships <= 0)
            {
                phase = Phase.GameOver;
                SaveHighScore();
                sfx.PlayOneShot(sounds.GameOver);
                hud.Banner = "GAME OVER";
                hud.SubBanner = "Press SPACE to fly again";
                engine.volume = 0f;
                return;
            }

            // Drop the section we crashed in (and everything ahead), respawn on two clear chunks, then resume.
            var z = ShipPosition.z;
            safeSpace = CurrentChunk()?.Space == true;
            // The discarded sections go back to the front of the plan, so their enemies are not lost.
            TrimChunksBeyond(z - ChunkLength, true);
            safeChunks = 2;
            ResetShip(spawnZ + 3f);
            StreamLevel();
            await CountdownAsync(id, "GET READY");
            if (id == runId)
            {
                invulnerableUntil = Time.time + 2.5f;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ResetShip(float z)
    {
        ship.gameObject.SetActive(true);
        shipModel.gameObject.SetActive(true);
        shipModel.localRotation = Quaternion.identity;
        steer = Vector2.zero;
        bounce = Vector3.zero;
        // Teleport without interpolation, otherwise the transform sweeps from the old pose through the new course.
        // FixedUpdate turns interpolation back on once the ship is flying again.
        shipBody.interpolation = RigidbodyInterpolation.None;
        var spawn = new Vector3(0f, 3f, z);
        shipBody.position = spawn;
        ship.position = spawn;
        shipBody.linearVelocity = Vector3.zero;
        fuel = FuelMax;
        nextFire = 0f;
        ResetPowerUps();
        foreach (var bolt in bolts)
        {
            Deactivate(bolt);
        }

        foreach (var laser in lasers)
        {
            Deactivate(laser);
        }
    }

    private void SaveHighScore()
    {
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
            PlayerPrefs.Save();
        }
    }

    private void AddScore(int points)
    {
        score += points;
        if (score > highScore)
        {
            highScore = score;
        }
    }

    private void RefreshHud()
    {
        hud.Score = score.ToString("000000");
        hud.HighScore = highScore.ToString("000000");
        hud.Level = level.ToString();
        hud.Ships = Mathf.Max(ships, 0).ToString();
        hud.Fuel = fuel / FuelMax;
        hud.LowFuel = fuel < FuelMax * 0.25f;
        if (ship != null)
        {
            var altitude = (ShipPosition.y - MinAltitude) / (MaxAltitude - MinAltitude);
            hud.Altitude = altitude;
            hud.AltitudeReadout = Mathf.RoundToInt(ShipPosition.y * 100f).ToString("0000");
        }

        hud.BossVisible = bossActive;
        hud.BossHealth = bossMaxHealth > 0 ? (float)bossHealth / bossMaxHealth : 0f;
        var chunk = CurrentChunk();
        var theme = chunk?.Theme ?? lastTheme;
        hud.Zone = bossActive ? "WARDEN" : chunk?.Space == true ? theme.MidZoneName : theme.Name;
        hud.Hull = (float)Mathf.Max(hull, 0) / HullMax;
        hud.LowHull = hull <= 1;
    }

    // ---------------------------------------------------------------- Ship

    private void UpdateShip()
    {
        var x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        var y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        steer = Vector2.MoveTowards(steer, new Vector2(x, y), Time.deltaTime * 8f);
        // Nose pitches up and the wings wobble while a bounce is still carrying the ship.
        var wobble = Mathf.Sin(Time.time * 38f) * Mathf.Min(bounce.magnitude, 12f) * 1.6f;
        shipModel.localRotation = Quaternion.Euler(-steer.y * 14f - bounce.y * 2.2f, 0f, -steer.x * 28f + wobble);

        var firing = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.LeftControl);
        if (firing && Time.time >= nextFire)
        {
            FireLasers();
        }

        // Fuel only burns in the fortress and open space; the Warden arena has no depots, so it is exempt.
        if (!bossActive)
        {
            fuel -= FuelDrain * Time.deltaTime;
        }

        if (fuel < FuelMax * 0.25f && Time.time >= nextFuelAlarm)
        {
            nextFuelAlarm = Time.time + 1.2f;
            sfx.PlayOneShot(sounds.LowFuel, 0.7f);
        }

        if (fuel <= 0f)
        {
            fuel = 0f;
            CrashAsync("OUT OF FUEL");
            return;
        }

        engine.pitch = 0.85f + (ShipPosition.y - MinAltitude) * 0.04f + (speed - BaseSpeed) * 0.01f;
        var emission = engineFx.emission;
        emission.rateOverTime = 40f + steer.y * 20f;
    }

    // Running into scenery never costs a ship: the fighter rebounds back and up instead.
    // (Enemy fire and running out of fuel still do.)
    private void CheckShipCollisions()
    {
        var shipBounds = new Bounds(ShipPosition, ShipHalf * 2f);
        foreach (var solid in solids)
        {
            if (SolidOverlaps(solid, shipBounds))
            {
                Bounce(solid.Bounds, solid.Pyramid);
                return;
            }
        }

        foreach (var target in targets)
        {
            var bounds = new Bounds(target.Center, target.Half * 2f);
            if (bounds.Intersects(shipBounds))
            {
                Bounce(bounds, false);
                return;
            }
        }

        var bossBody = new Bounds(boss.position + new Vector3(0f, 3.5f, 0f), new Vector3(5.4f, 7f, 2.4f));
        if (bossActive && !bossRetreating && bossBody.Intersects(shipBounds))
        {
            Bounce(bossBody, false);
        }
    }

    private void Bounce(Bounds obstacle, bool sloped)
    {
        var position = ShipPosition;
        var side = position.x >= obstacle.center.x ? 1f : -1f;
        var fix = position;
        if (sloped)
        {
            // Pyramid slope: lift clear and kick up and slightly sideways.
            fix.y = Mathf.Min(MaxAltitude, position.y + 0.35f);
            bounce = new Vector3(side * 3f, 11f, -speed * 0.35f);
        }
        else if (position.y - ShipHalf.y > obstacle.max.y - 0.6f)
        {
            // Scraped the top edge: pop upward.
            fix.y = Mathf.Min(MaxAltitude, obstacle.max.y + ShipHalf.y + 0.05f);
            bounce = new Vector3(0f, 10f, -speed * 0.2f);
        }
        else if (position.z - ShipHalf.z < obstacle.min.z + 0.4f)
        {
            // Flew into the front face: knocked back and up.
            fix.z = obstacle.min.z - ShipHalf.z - 0.05f;
            bounce = new Vector3(side * 1.5f, 10f, -(speed + 8f));
        }
        else
        {
            // Clipped a side: shoved sideways and up.
            fix.x = side > 0f ? obstacle.max.x + ShipHalf.x + 0.05f : obstacle.min.x - ShipHalf.x - 0.05f;
            bounce = new Vector3(side * 8f, 8f, -speed * 0.3f);
        }

        fix.x = Mathf.Clamp(fix.x, -CorridorHalfWidth, CorridorHalfWidth);
        shipBody.position = fix;

        if (Time.time >= nextBounceFx)
        {
            nextBounceFx = Time.time + 0.2f;
            Explode(position + Vector3.forward * 0.8f, Gold, 14, false);
            sfx.PlayOneShot(sounds.Bounce, 0.8f);
        }
    }

    // Pyramids collide by their actual slopes; everything else is a box.
    private static float PyramidHeightAt(in Solid solid, float x, float z)
    {
        var bounds = solid.Bounds;
        var reach = Mathf.Max(Mathf.Abs(x - bounds.center.x) / bounds.extents.x, Mathf.Abs(z - bounds.center.z) / bounds.extents.z);
        return reach >= 1f ? float.NegativeInfinity : bounds.min.y + bounds.size.y * (1f - reach);
    }

    private static bool SolidContains(in Solid solid, Vector3 point)
    {
        return solid.Bounds.Contains(point) && (!solid.Pyramid || point.y < PyramidHeightAt(solid, point.x, point.z));
    }

    private static bool SolidOverlaps(in Solid solid, Bounds ship)
    {
        if (!solid.Bounds.Intersects(ship)) return false;
        if (!solid.Pyramid) return true;
        // The highest slope point under the ship is the footprint point nearest the apex.
        var center = solid.Bounds.center;
        var x = Mathf.Clamp(center.x, ship.min.x, ship.max.x);
        var z = Mathf.Clamp(center.z, ship.min.z, ship.max.z);
        return PyramidHeightAt(solid, x, z) > ship.min.y;
    }

    private static bool SolidRay(in Solid solid, Ray ray, float maxDistance, out float distance)
    {
        distance = 0f;
        if (!solid.Bounds.IntersectRay(ray, out var enter) || enter > maxDistance) return false;
        enter = Mathf.Max(enter, 0f);
        if (!solid.Pyramid)
        {
            distance = enter;
            return true;
        }

        for (var d = enter; d <= maxDistance; d += 0.2f)
        {
            if (SolidContains(solid, ray.GetPoint(d)))
            {
                distance = d;
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- Projectiles

    private void UpdateProjectiles()
    {
        var dt = Time.deltaTime;
        foreach (var laser in lasers)
        {
            if (!laser.Active) continue;
            var from = laser.Transform.position;
            var step = laser.Velocity * dt;
            laser.Transform.position = from + step;
            laser.Life -= dt;
            if (laser.Life <= 0f || LaserHits(from, step))
            {
                Deactivate(laser);
            }
        }

        var shipBounds = ship != null ? new Bounds(ShipPosition, ShipHalf * 2f + Vector3.one * 0.2f) : default;
        foreach (var bolt in bolts)
        {
            if (!bolt.Active) continue;
            var position = bolt.Transform.position + bolt.Velocity * dt;
            bolt.Transform.position = position;
            bolt.Life -= dt;
            if (bolt.Life <= 0f || position.y < 0f || InsideSolid(position))
            {
                Deactivate(bolt);
                continue;
            }

            if (phase == Phase.Playing && Time.time >= invulnerableUntil && shipBounds.Contains(position))
            {
                Deactivate(bolt);
                TakeHit(position);
            }
        }
    }

    private bool LaserHits(Vector3 from, Vector3 step)
    {
        var distance = step.magnitude;
        var ray = new Ray(from, step / Mathf.Max(distance, 0.0001f));
        var closest = distance;
        Target hitTarget = null;
        var hitSolid = false;
        var hitBoss = false;

        foreach (var target in targets)
        {
            var bounds = new Bounds(target.Center, target.Half * 2f + new Vector3(0.25f, 0.25f, 0f));
            if (bounds.IntersectRay(ray, out var d) && d <= closest)
            {
                closest = d;
                hitTarget = target;
            }
        }

        foreach (var solid in solids)
        {
            if (SolidRay(solid, ray, closest, out var d))
            {
                closest = d;
                hitTarget = null;
                hitSolid = true;
            }
        }

        if (bossActive && !bossRetreating)
        {
            var core = new Bounds(bossCore.transform.position, new Vector3(1.8f, 1.8f, 1f));
            var body = new Bounds(boss.position + new Vector3(0f, 3.5f, 0.4f), new Vector3(5.4f, 7f, 2f));
            if (core.IntersectRay(ray, out var dc) && dc <= closest)
            {
                closest = dc;
                hitTarget = null;
                hitSolid = false;
                hitBoss = true;
            }
            else if (body.IntersectRay(ray, out var db) && db <= closest)
            {
                closest = db;
                hitTarget = null;
                hitSolid = true;
            }
        }

        var point = ray.GetPoint(closest);
        if (hitTarget != null)
        {
            DamageTarget(hitTarget);
            return true;
        }

        if (hitBoss)
        {
            DamageBoss(point);
            return true;
        }

        if (hitSolid)
        {
            Explode(point, Gold, 6, false);
            return true;
        }

        return false;
    }

    private bool InsideSolid(Vector3 position)
    {
        foreach (var solid in solids)
        {
            if (SolidContains(solid, position)) return true;
        }

        return false;
    }

    private void FireBolt(Vector3 origin, Vector3 aimAt, float spreadDegrees = 0f)
    {
        var bolt = Take(bolts);
        if (bolt == null) return;
        var direction = (aimAt - origin).normalized;
        if (!Mathf.Approximately(spreadDegrees, 0f))
        {
            direction = Quaternion.Euler(0f, spreadDegrees, 0f) * direction;
        }

        bolt.Transform.position = origin;
        bolt.Velocity = direction * (EnemyBoltSpeed + level * 1.5f);
        bolt.Life = 3.5f;
    }

    private static Projectile Take(List<Projectile> pool)
    {
        foreach (var projectile in pool)
        {
            if (projectile.Active) continue;
            projectile.Active = true;
            projectile.Transform.gameObject.SetActive(true);
            return projectile;
        }

        return null;
    }

    private static void Deactivate(Projectile projectile)
    {
        projectile.Active = false;
        projectile.Transform.gameObject.SetActive(false);
    }

    // Unity 6 Object.InstantiateAsync: clone the pool templates in one batched asynchronous operation.
    private async Awaitable PrewarmPoolsAsync()
    {
        var laserOp = InstantiateAsync(laserPrefab, LaserPoolSize, poolRoot);
        var boltOp = InstantiateAsync(boltPrefab, BoltPoolSize, poolRoot);
        await laserOp;
        await boltOp;
        Fill(lasers, laserOp.Result, "Laser");
        Fill(bolts, boltOp.Result, "Enemy bolt");

        static void Fill(List<Projectile> pool, GameObject[] objects, string name)
        {
            foreach (var instance in objects)
            {
                instance.name = name;
                instance.SetActive(false);
                pool.Add(new Projectile { Transform = instance.transform });
            }
        }
    }

    // ---------------------------------------------------------------- Targets

    private void UpdateTargets()
    {
        var shipPosition = ShipPosition;
        var fireScale = Mathf.Max(0.45f, 1f - (level - 1) * 0.12f);
        for (var i = targets.Count - 1; i >= 0; i--)
        {
            var target = targets[i];
            var ahead = target.Transform.position.z - shipPosition.z;
            switch (target.Kind)
            {
                case TargetKind.Turret:
                    if (target.Head != null && ahead > 0f)
                    {
                        var look = shipPosition - target.Head.position;
                        look.y = 0f;
                        if (look.sqrMagnitude > 0.01f)
                        {
                            target.Head.rotation = Quaternion.Slerp(target.Head.rotation, Quaternion.LookRotation(look), Time.deltaTime * 4f);
                        }
                    }

                    if (ahead is > 9f and < 48f && Time.time >= target.NextFire)
                    {
                        target.NextFire = Time.time + Random.Range(1.6f, 2.6f) * fireScale;
                        var muzzle = target.Head.position + target.Head.forward * 0.9f;
                        FireBolt(muzzle, shipPosition + new Vector3(0f, 0f, speed * 0.6f));
                        sfx.PlayOneShot(sounds.EnemyShot, 0.35f);
                    }

                    break;
                case TargetKind.Radar:
                    target.Head.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
                    break;
                case TargetKind.Drone:
                    target.Phase += Time.deltaTime;
                    var p = target.Phase;
                    var position = target.Origin + new Vector3(Mathf.Sin(p * 1.3f) * 3.5f, Mathf.Sin(p * 0.9f) * 1.6f, -p * 5f);
                    position.x = Mathf.Clamp(position.x, -CorridorHalfWidth, CorridorHalfWidth);
                    position.y = Mathf.Clamp(position.y, MinAltitude + 0.2f, MaxAltitude - 0.2f);
                    target.Transform.position = position;
                    target.Transform.rotation = Quaternion.Euler(0f, 180f, Mathf.Sin(p * 1.3f) * 25f);
                    target.Center = position;
                    if (ahead is > 10f and < 40f && Time.time >= target.NextFire)
                    {
                        target.NextFire = Time.time + Random.Range(2.2f, 3.4f) * fireScale;
                        FireBolt(position + Vector3.back, shipPosition + new Vector3(0f, 0f, speed * 0.5f));
                        sfx.PlayOneShot(sounds.EnemyShot, 0.3f);
                    }

                    break;
            }
        }
    }

    private void DamageTarget(Target target)
    {
        target.Health--;
        if (target.Health > 0)
        {
            Explode(target.Center, Gold, 8, false);
            sfx.PlayOneShot(sounds.BossHit, 0.5f);
            return;
        }

        targets.Remove(target);
        AddScore(target.Score);
        switch (target.Kind)
        {
            case TargetKind.FuelTank:
                fuel = Mathf.Min(FuelMax, fuel + FuelRefill);
                sfx.PlayOneShot(sounds.Fuel);
                Explode(target.Center, Gold, 40, true);
                break;
            case TargetKind.Radar:
                sfx.PlayOneShot(sounds.Radar);
                Explode(target.Center, Lime, 40, true);
                break;
            default:
                Explode(target.Center, Red, 34, true);
                break;
        }

        MaybeDropPickup(target);
        Destroy(target.Transform.gameObject);
    }

    private Target AddTarget(Chunk chunk, TargetKind kind, GameObject template, Vector3 position, Vector3 half, float centerHeight, int health, int points)
    {
        var instance = Instantiate(template, position, kind == TargetKind.Drone ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, chunk.Root);
        instance.SetActive(true);
        var target = new Target
        {
            Kind = kind,
            Transform = instance.transform,
            Head = instance.transform.Find("Head"),
            Half = half,
            Center = position + Vector3.up * centerHeight,
            Origin = position,
            Health = health,
            Score = points,
            NextFire = Time.time + Random.Range(0.6f, 2f),
            Phase = Random.Range(0f, 6f),
            Chunk = chunk
        };
        targets.Add(target);
        return target;
    }

    // ---------------------------------------------------------------- Boss

    private void StartBoss()
    {
        bossPending = false;
        bossActive = true;
        bossRetreating = false;
        bossMaxHealth = 10 + level * 4;
        bossHealth = bossMaxHealth;
        bossDistance = 70f;
        bossEndsAt = Time.time + 24f;
        bossNextFire = Time.time + 2.5f;
        boss.gameObject.SetActive(true);
        boss.position = new Vector3(0f, 0f, ShipPosition.z + bossDistance);
        sfx.PlayOneShot(sounds.BossAlarm, 0.8f);
        _ = FlashBannerAsync(runId, "WARNING", "The Warden guards the core - hit its glowing eye!");
    }

    private void UpdateBoss()
    {
        if (bossPending && ShipPosition.z >= bossTriggerZ)
        {
            StartBoss();
        }

        if (!bossActive)
        {
            return;
        }

        var t = Time.time;
        if (bossRetreating)
        {
            bossDistance += Time.deltaTime * 35f;
            if (bossDistance > 110f)
            {
                EndBoss(false);
                return;
            }
        }
        else
        {
            bossDistance = Mathf.MoveTowards(bossDistance, 20f, Time.deltaTime * 14f);
        }

        boss.position = new Vector3(Mathf.Sin(t * 0.6f) * 3.2f, 0f, ShipPosition.z + bossDistance);
        boss.rotation = Quaternion.Euler(0f, 180f + Mathf.Sin(t * 0.6f) * 8f, 0f);
        bossLeftCannon.localRotation = Quaternion.Euler(Mathf.Sin(t * 2.1f) * 20f, 0f, 0f);
        bossRightCannon.localRotation = Quaternion.Euler(Mathf.Sin(t * 2.1f + 1.4f) * 20f, 0f, 0f);
        var pulse = 0.6f + 0.4f * Mathf.Sin(t * 8f);
        bossCore.transform.localScale = Vector3.one * (1.1f + pulse * 0.15f);

        if (bossRetreating)
        {
            return;
        }

        if (t >= bossEndsAt)
        {
            bossRetreating = true;
            _ = FlashBannerAsync(runId, "THE WARDEN WITHDRAWS", "No core bonus this time");
            return;
        }

        if (bossDistance < 40f && t >= bossNextFire)
        {
            bossNextFire = t + Mathf.Max(0.55f, 1.25f - level * 0.1f);
            var aim = ShipPosition + new Vector3(0f, 0f, speed * 0.5f);
            var spread = level >= 3 ? new[] { -14f, -7f, 0f, 7f, 14f } : new[] { -10f, 0f, 10f };
            var cannon = Mathf.Repeat(t, 2f) < 1f ? bossLeftCannon : bossRightCannon;
            var muzzle = cannon.position + cannon.forward * 1.4f;
            foreach (var angle in spread)
            {
                FireBolt(muzzle, aim, angle);
            }

            sfx.PlayOneShot(sounds.EnemyShot, 0.6f);
        }
    }

    private void DamageBoss(Vector3 point)
    {
        bossHealth--;
        AddScore(100);
        Explode(point, Red, 12, false);
        sfx.PlayOneShot(sounds.BossHit, 0.8f);
        if (bossHealth <= 0)
        {
            AddScore(5000 * level);
            _ = BossDeathAsync(runId, boss.position);
            EndBoss(true);
        }
    }

    private void EndBoss(bool defeated)
    {
        bossActive = false;
        bossRetreating = false;
        // Hide immediately; when defeated, BossDeathAsync keeps the fireworks going at the last position.
        boss.gameObject.SetActive(false);
        level++;
        speed = BaseSpeed * (1f + (level - 1) * 0.1f);
        TrimChunksBeyond(ShipPosition.z + 40f);
        EnqueueLevel(level);
        var next = SkyfortThemes.ForLevel(level);
        _ = FlashBannerAsync(runId, defeated ? "WARDEN DESTROYED" : $"LEVEL {level}", defeated ? $"+{5000 * (level - 1)}  -  Level {level}: {next.Name}" : $"Next: {next.Name} - {next.Tagline}");
    }

    private async Awaitable BossDeathAsync(int id, Vector3 position)
    {
        try
        {
            sfx.PlayOneShot(sounds.BigExplosion);
            for (var i = 0; i < 7; i++)
            {
                if (id != runId) return;
                var offset = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(1f, 6.5f), Random.Range(-1f, 1f));
                Explode(position + offset, i % 2 == 0 ? Red : Gold, 60, true);
                await Awaitable.WaitForSecondsAsync(0.18f, destroyCancellationToken);
            }

            sfx.PlayOneShot(sounds.LevelClear);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Awaitable FlashBannerAsync(int id, string banner, string sub)
    {
        hud.Banner = banner;
        hud.SubBanner = sub;
        try
        {
            await Awaitable.WaitForSecondsAsync(2.6f, destroyCancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (id == runId && phase == Phase.Playing && hud.Banner == banner)
        {
            hud.Banner = "";
            hud.SubBanner = "";
        }
    }

    private void ResetBoss()
    {
        bossPending = false;
        bossActive = false;
        bossRetreating = false;
        bossMaxHealth = 0;
        boss.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- Level streaming

    private void EnqueueLevel(int number)
    {
        // Each level has its own theme and obstacle mix: first half, a mid-level drone zone, second half, then the Warden.
        var theme = SkyfortThemes.ForLevel(number);
        // Levels 2 and 3 fly one stretch of the second half through the cockpit camera.
        var cockpit = theme == SkyfortThemes.DuneRuins || theme == SkyfortThemes.IceCitadel;
        ChunkKind[] firstHalf;
        ChunkKind[] secondHalf;
        if (theme == SkyfortThemes.DuneRuins)
        {
            firstHalf = new[] { ChunkKind.Pyramids, ChunkKind.Obelisks, ChunkKind.PyramidRow, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.Mixed };
            secondHalf = new[] { ChunkKind.Obelisks, ChunkKind.Pyramids, ChunkKind.CastleWall, ChunkKind.CastleKeep, ChunkKind.PyramidRow, ChunkKind.TurretRow, ChunkKind.Mixed, ChunkKind.RadarSite };
        }
        else if (theme == SkyfortThemes.VioletBastion)
        {
            firstHalf = new[] { ChunkKind.Pyramids, ChunkKind.PyramidRow, ChunkKind.FieldLow, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.Mixed };
            secondHalf = new[] { ChunkKind.Pyramids, ChunkKind.CastleWall, ChunkKind.PyramidRow, ChunkKind.FieldLow, ChunkKind.Mixed, ChunkKind.TurretRow, ChunkKind.RadarSite };
        }
        else if (theme == SkyfortThemes.AzureCauseway)
        {
            firstHalf = new[] { ChunkKind.Obelisks, ChunkKind.CastleWall, ChunkKind.Obelisks, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.Mixed };
            secondHalf = new[] { ChunkKind.Obelisks, ChunkKind.CastleKeep, ChunkKind.CastleWall, ChunkKind.FieldLow, ChunkKind.Mixed, ChunkKind.TurretRow, ChunkKind.RadarSite };
        }
        else if (theme == SkyfortThemes.BrickBulwark)
        {
            firstHalf = new[] { ChunkKind.CastleWall, ChunkKind.PyramidRow, ChunkKind.CastleKeep, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.Mixed };
            secondHalf = new[] { ChunkKind.CastleKeep, ChunkKind.CastleWall, ChunkKind.Pyramids, ChunkKind.CastleWall, ChunkKind.Mixed, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.FieldLow };
        }
        else if (theme == SkyfortThemes.Refinery)
        {
            firstHalf = new[] { ChunkKind.Pyramids, ChunkKind.PyramidRow, ChunkKind.Obelisks, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.Mixed };
            secondHalf = new[] { ChunkKind.Pyramids, ChunkKind.PyramidRow, ChunkKind.CastleWall, ChunkKind.Obelisks, ChunkKind.Mixed, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.FieldLow };
        }
        else if (theme == SkyfortThemes.IceCitadel)
        {
            firstHalf = new[] { ChunkKind.IceSpires, ChunkKind.CastleWall, ChunkKind.FieldLow, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.Mixed };
            secondHalf = new[] { ChunkKind.IceSpires, ChunkKind.CastleKeep, ChunkKind.CastleWall, ChunkKind.FieldLow, ChunkKind.Mixed, ChunkKind.TurretRow, ChunkKind.RadarSite };
        }
        else
        {
            firstHalf = new[] { ChunkKind.Pyramids, ChunkKind.CastleWall, ChunkKind.PyramidRow, ChunkKind.FieldLow, ChunkKind.TurretRow, ChunkKind.RadarSite };
            secondHalf = new[] { ChunkKind.Pyramids, ChunkKind.CastleKeep, ChunkKind.CastleWall, ChunkKind.PyramidRow, ChunkKind.Mixed, ChunkKind.TurretRow, ChunkKind.RadarSite, ChunkKind.FieldLow };
        }

        var extra = Mathf.Min(number, 4);
        Add(ChunkKind.Arena);
        Add(ChunkKind.Entry);
        AddFortress(firstHalf, 6 + extra);
        Add(ChunkKind.Exit);
        for (var i = 0; i < 4 + extra; i++)
        {
            Add(ChunkKind.Space);
        }

        Add(ChunkKind.Entry);
        AddFortress(secondHalf, 7 + extra, cockpit);
        Add(ChunkKind.Exit);
        Add(ChunkKind.BossArena);

        void Add(ChunkKind kind, bool firstPerson = false) => plan.Enqueue((kind, theme, firstPerson));

        void AddFortress(ChunkKind[] pool, int count, bool cockpitRun = false)
        {
            var last = ChunkKind.Entry;
            for (var i = 0; i < count; i++)
            {
                ChunkKind next;
                if (i % 3 == 1)
                {
                    next = ChunkKind.FuelDepot;
                }
                else
                {
                    do next = pool[Random.Range(0, pool.Length)];
                    while (next == last);
                }

                Add(next, cockpitRun && i >= 2 && i <= 7);
                last = next;
            }
        }
    }

    private void StreamLevel()
    {
        if (ship == null) return;
        var shipZ = ShipPosition.z;
        while (spawnZ < shipZ + SpawnAhead)
        {
            ChunkKind kind;
            var theme = lastTheme;
            var planned = false;
            var firstPerson = false;
            if (safeChunks > 0)
            {
                safeChunks--;
                kind = safeSpace ? ChunkKind.Drift : ChunkKind.Open;
            }
            else if (plan.Count > 0)
            {
                (kind, theme, firstPerson) = plan.Dequeue();
                planned = true;
            }
            else
            {
                kind = ChunkKind.Arena;
            }

            lastTheme = theme;
            BuildChunk(kind, theme, spawnZ, planned, firstPerson);
            spawnZ += ChunkLength;
        }

        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            if (chunks[i].StartZ + ChunkLength < shipZ - DespawnBehind)
            {
                RemoveChunk(chunks[i]);
            }
        }
    }

    private Chunk CurrentChunk() => ship == null ? null : ChunkAt(ShipPosition.z);

    private Chunk ChunkAt(float z)
    {
        foreach (var chunk in chunks)
        {
            if (z >= chunk.StartZ && z < chunk.StartZ + ChunkLength) return chunk;
        }

        return null;
    }

    private void TrimChunksBeyond(float z, bool requeue = false)
    {
        var removed = new List<Chunk>();
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            if (chunks[i].StartZ > z)
            {
                // A Warden arena that has already triggered the boss is not replayed.
                if (requeue && chunks[i].Planned && !(chunks[i].Kind == ChunkKind.BossArena && !bossPending))
                {
                    removed.Add(chunks[i]);
                }

                RemoveChunk(chunks[i]);
            }
        }

        if (removed.Count > 0)
        {
            removed.Sort((a, b) => a.StartZ.CompareTo(b.StartZ));
            var rest = plan.ToArray();
            plan.Clear();
            foreach (var chunk in removed)
            {
                plan.Enqueue((chunk.Kind, chunk.Theme, chunk.FirstPerson));
                if (chunk.Kind == ChunkKind.BossArena)
                {
                    bossPending = false; // set again when the arena is rebuilt
                }
            }

            foreach (var entry in rest)
            {
                plan.Enqueue(entry);
            }
        }

        var end = z;
        foreach (var chunk in chunks)
        {
            end = Mathf.Max(end, chunk.StartZ + ChunkLength);
        }

        spawnZ = end;
    }

    private void RemoveChunk(Chunk chunk)
    {
        chunks.Remove(chunk);
        solids.RemoveAll(s => s.Chunk == chunk);
        targets.RemoveAll(t => t.Chunk == chunk);
        pickups.RemoveAll(p => p.Chunk == chunk);
        Destroy(chunk.Root.gameObject);
    }

    private void ClearWorld()
    {
        while (chunks.Count > 0)
        {
            RemoveChunk(chunks[0]);
        }
    }

    private void BuildChunk(ChunkKind kind, SkyfortTheme theme, float z0, bool planned, bool firstPerson)
    {
        var chunk = new Chunk
        {
            Root = Group($"{theme.Name} {kind} @ {z0:0}", levelRoot, new Vector3(0f, 0f, z0)),
            StartZ = z0,
            Space = kind is ChunkKind.Space or ChunkKind.Drift,
            Theme = theme,
            Kind = kind,
            Planned = planned,
            FirstPerson = firstPerson
        };
        chunks.Add(chunk);
        var mid = z0 + ChunkLength * 0.5f;

        if (chunk.Space)
        {
            BuildSpaceFloor(chunk);
        }
        else
        {
            BuildDeck(chunk, kind is ChunkKind.Arena or ChunkKind.BossArena);
        }

        switch (kind)
        {
            case ChunkKind.Entry:
                // Castle rampart: a low curtain wall to hop over, flanked by two big towers.
                BuildCastleWall(chunk, z0 + 8f, -WallHalfWidth, WallHalfWidth, 1.9f);
                BuildTower(chunk, -6.9f, z0 + 8f, 0.95f, 4.8f);
                BuildTower(chunk, 6.9f, z0 + 8f, 0.95f, 4.8f);
                break;
            case ChunkKind.Exit:
                BuildLandmark(chunk, -3.6f, z0 + 11f, 4.6f, Random.Range(3f, 5f));
                BuildLandmark(chunk, 3.6f, z0 + 11f, 4.6f, Random.Range(3f, 5f));
                AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(Random.Range(-4f, 4f), 0f, z0 + 4f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.Pyramids:
                for (var i = 0; i < 2 + Mathf.Min(level, 2) / 2; i++)
                {
                    var size = Random.Range(3f, 5.2f);
                    BuildPyramid(chunk, Random.Range(-4.5f, 4.5f), z0 + 4f + i * 5.5f, size, Random.Range(2.4f, Mathf.Min(6f, size * 1.25f)));
                }

                break;
            case ChunkKind.PyramidRow:
            {
                // Three pyramids across the corridor; one is always low enough to skim over.
                var low = Random.Range(0, 3);
                for (var i = 0; i < 3; i++)
                {
                    BuildPyramid(chunk, (i - 1) * 4.7f, z0 + 9f, 4.4f, i == low ? 2.3f : Random.Range(4.4f, 6f));
                }

                break;
            }
            case ChunkKind.CastleWall:
            {
                var wallHeight = Random.Range(2.1f, 3f);
                BuildCastleWall(chunk, z0 + 10f, -WallHalfWidth, WallHalfWidth, wallHeight);
                var towerX = Random.Range(-4.5f, -1.5f);
                BuildTower(chunk, towerX, z0 + 10f, 0.8f, Random.Range(5f, 5.9f));
                BuildTower(chunk, towerX + Random.Range(3.5f, 6f), z0 + 10f, 0.8f, Random.Range(5f, 5.9f));
                if (Random.value < 0.6f)
                {
                    AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(Random.Range(-5.5f, 5.5f), wallHeight + 0.12f, z0 + 10f + 0.2f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 200);
                }

                break;
            }
            case ChunkKind.CastleKeep:
                BuildKeep(chunk, Random.Range(-3f, 3f), z0 + 9f);
                break;
            case ChunkKind.Obelisks:
            {
                // Tall stone needles in two staggered rows: weave between them or climb over the caps.
                for (var i = 0; i < 4; i++)
                {
                    var x = (i % 2 == 0 ? -1f : 1f) * Random.Range(1.2f, 4.6f);
                    BuildObelisk(chunk, x, z0 + 3.5f + i * 3.8f, Random.Range(4.6f, 6.1f));
                }

                AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(Random.Range(-1f, 1f), 0f, z0 + 9f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            }
            case ChunkKind.IceSpires:
                for (var i = 0; i < 3 + Mathf.Min(level, 3) / 2; i++)
                {
                    BuildSpire(chunk, Random.Range(-5f, 5f), z0 + 3f + i * 4.2f, Random.Range(1.6f, 2.6f), Random.Range(3.4f, 6.1f));
                }

                break;
            case ChunkKind.FieldLow:
                BuildField(chunk, z0 + 9f, 0f, Random.Range(2.6f, 4.6f));
                break;
            case ChunkKind.TurretRow:
                for (var i = 0; i < 2 + Mathf.Min(level, 3); i++)
                {
                    var x = Mathf.Lerp(-5f, 5f, (i + 0.5f) / (2 + Mathf.Min(level, 3)));
                    AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(x, 0f, z0 + 5f + (i % 2) * 6f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                }

                break;
            case ChunkKind.FuelDepot:
                AddTarget(chunk, TargetKind.FuelTank, fuelTankPrefab, new Vector3(Random.Range(-4.5f, -1f), 0f, z0 + 6f), new Vector3(0.75f, 0.9f, 0.75f), 0.9f, 1, 300);
                AddTarget(chunk, TargetKind.FuelTank, fuelTankPrefab, new Vector3(Random.Range(1f, 4.5f), 0f, z0 + 12f), new Vector3(0.75f, 0.9f, 0.75f), 0.9f, 1, 300);
                break;
            case ChunkKind.RadarSite:
                AddTarget(chunk, TargetKind.Radar, radarPrefab, new Vector3(Random.Range(-4f, 4f), 0f, mid), new Vector3(0.9f, 1.3f, 0.9f), 1.3f, 2, 1000);
                AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(Random.Range(-5f, 5f), 0f, z0 + 3f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.Mixed:
                BuildLandmark(chunk, Random.Range(-4f, 4f), z0 + 5f, 4f, Random.Range(3f, 5f));
                AddTarget(chunk, TargetKind.FuelTank, fuelTankPrefab, new Vector3(Random.Range(-4f, 4f), 0f, z0 + 10f), new Vector3(0.75f, 0.9f, 0.75f), 0.9f, 1, 300);
                AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(Random.Range(-5f, 5f), 0f, z0 + 15f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.Space:
                for (var i = 0; i < 2 + Mathf.Min(level, 2); i++)
                {
                    var origin = new Vector3(Random.Range(-4f, 4f), Random.Range(1.5f, 5.5f), z0 + 8f + i * 5f);
                    AddTarget(chunk, TargetKind.Drone, dronePrefab, origin, new Vector3(0.9f, 0.4f, 0.7f), 0f, 1, 250);
                }

                break;
            case ChunkKind.BossArena:
                bossPending = true;
                bossTriggerZ = z0;
                break;
        }

        // Free-floating power-ups: common in the obstacle sections, occasional in the drone zones.
        var pickupChance = kind switch
        {
            ChunkKind.Space => 0.3f,
            ChunkKind.Arena or ChunkKind.BossArena or ChunkKind.Open or ChunkKind.Drift or ChunkKind.Entry => 0f,
            _ => 0.35f
        };
        if (Random.value < pickupChance)
        {
            SpawnPickup(chunk, RandomPickupKind(), new Vector3(Random.Range(-4.5f, 4.5f), Random.Range(1.4f, 4.8f), z0 + 16f));
        }
    }

    // Skyfort and Dune Ruins use pyramids for landmarks; the Ice Citadel uses ice spires.
    private void BuildLandmark(Chunk chunk, float x, float z, float baseSize, float height)
    {
        if (chunk.Theme == SkyfortThemes.IceCitadel)
        {
            BuildSpire(chunk, x, z, baseSize * 0.5f, height + 1f);
        }
        else
        {
            BuildPyramid(chunk, x, z, baseSize, height);
        }
    }

    private void BuildObelisk(Chunk chunk, float x, float z, float height)
    {
        var theme = chunk.Theme;
        var obelisk = Group("Obelisk", chunk.Root, new Vector3(x, 0f, z - chunk.StartZ));
        const float width = 0.9f;
        var shaft = height - 0.7f;
        Part(PrimitiveType.Cube, "Obelisk base", obelisk, new Vector3(0f, 0.15f, 0f), new Vector3(width + 0.5f, 0.3f, width + 0.5f), theme.StoneBase);
        Part(PrimitiveType.Cube, "Obelisk shaft", obelisk, new Vector3(0f, shaft * 0.5f, 0f), new Vector3(width, shaft, width), theme.StoneMain);
        Pyramid("Obelisk cap", obelisk, new Vector3(0f, shaft, 0f), new Vector3(width, 0.7f, width), theme.StoneGlow, true);
        for (var y = 1f; y < shaft - 0.6f; y += 1.1f)
        {
            Part(PrimitiveType.Cube, "Glyph", obelisk, new Vector3(0f, y, -width * 0.5f - 0.01f), new Vector3(0.3f, 0.5f, 0.02f), theme.StoneBase);
        }

        // Neon traced up the four corners.
        foreach (var cx in new[] { -1f, 1f })
        {
            foreach (var cz in new[] { -1f, 1f })
            {
                var corner = new Vector3(cx * width * 0.5f, 0f, cz * width * 0.5f);
                NeonLine(obelisk, corner + Vector3.up * 0.3f, corner + Vector3.up * shaft, 0.05f, theme.Neon);
            }
        }

        AddBox(chunk, new Vector3(x, height * 0.5f, z), new Vector3(width, height, width));
    }

    // A thin glowing bar from a to b (local space of parent).
    private static void NeonLine(Transform parent, Vector3 a, Vector3 b, float thickness, Color color)
    {
        var line = Part(PrimitiveType.Cube, "Neon", parent, (a + b) * 0.5f, new Vector3(thickness, (b - a).magnitude, thickness), color, true);
        line.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
    }

    // Neon along the four sloped edges of a pyramid whose base center is at basePosition.
    private static void NeonPyramidEdges(Transform parent, Vector3 basePosition, float baseSize, float height, Color color)
    {
        var apex = basePosition + Vector3.up * height;
        var half = baseSize * 0.5f;
        foreach (var cx in new[] { -1f, 1f })
        {
            foreach (var cz in new[] { -1f, 1f })
            {
                NeonLine(parent, basePosition + new Vector3(cx * half, 0f, cz * half), apex, 0.06f, color);
            }
        }
    }

    private void BuildSpire(Chunk chunk, float x, float z, float baseSize, float height)
    {
        var theme = chunk.Theme;
        var local = new Vector3(x, 0f, z - chunk.StartZ);
        Pyramid("Ice spire", chunk.Root, local, new Vector3(baseSize, height, baseSize), theme.StoneMain);
        Pyramid("Ice spire inner", chunk.Root, local + new Vector3(baseSize * 0.18f, 0f, -baseSize * 0.12f), new Vector3(baseSize * 0.6f, height * 0.72f, baseSize * 0.6f), theme.StoneBase);
        var cap = 0.2f;
        Pyramid("Spire tip", chunk.Root, local + new Vector3(0f, height * (1f - cap), 0f), new Vector3(baseSize * cap * 1.05f, height * cap * 1.05f, baseSize * cap * 1.05f), theme.StoneGlow, true);
        NeonPyramidEdges(chunk.Root, local, baseSize, height, theme.Neon);
        solids.Add(new Solid
        {
            Bounds = new Bounds(new Vector3(x, height * 0.5f, z), new Vector3(baseSize, height, baseSize)),
            Chunk = chunk,
            Pyramid = true
        });
    }

    // Every obstacle tops out below MaxAltitude, so flying high always clears it; flying around works too.
    private void AddBox(Chunk chunk, Vector3 center, Vector3 size)
    {
        solids.Add(new Solid { Bounds = new Bounds(center, size), Chunk = chunk });
    }

    // Fuel tank: red body with white bands, a dome, a grey base ring and a white pipe running to the side.
    private void BuildTank(Chunk chunk, float x, float z, float baseSize, float height)
    {
        var theme = chunk.Theme;
        height = Mathf.Min(height, 5.4f);
        var radius = baseSize * 0.42f;
        var tank = Group("Storage tank", chunk.Root, new Vector3(x, 0f, z - chunk.StartZ));
        Part(PrimitiveType.Cylinder, "Tank base", tank, new Vector3(0f, 0.15f, 0f), new Vector3(radius * 2.2f, 0.15f, radius * 2.2f), theme.StoneBase);
        var body = height - 0.5f;
        Part(PrimitiveType.Cylinder, "Tank body", tank, new Vector3(0f, 0.3f + body * 0.5f, 0f), new Vector3(radius * 2f, body * 0.5f, radius * 2f), theme.StoneMain);
        for (var y = 0.9f; y < body; y += 1.3f)
        {
            Part(PrimitiveType.Cylinder, "Tank band", tank, new Vector3(0f, y, 0f), new Vector3(radius * 2.06f, 0.06f, radius * 2.06f), Color.white);
        }

        Part(PrimitiveType.Sphere, "Tank dome", tank, new Vector3(0f, 0.3f + body, 0f), new Vector3(radius * 2f, 0.7f, radius * 2f), theme.StoneBase);
        Part(PrimitiveType.Cylinder, "Tank hatch", tank, new Vector3(0f, 0.3f + body + 0.32f, 0f), new Vector3(radius * 0.5f, 0.06f, radius * 0.5f), theme.StoneGlow, true);
        Part(PrimitiveType.Cube, "Tank pipe", tank, new Vector3(radius + 0.7f, 0.2f, 0f), new Vector3(1.4f, 0.12f, 0.12f), Color.white);
        AddBox(chunk, new Vector3(x, height * 0.5f, z), new Vector3(radius * 2f, height, radius * 2f));
    }

    // Brick bastion: a squat grey block with mortar courses and a lit slit.
    private void BuildBastion(Chunk chunk, float x, float z, float baseSize, float height)
    {
        var theme = chunk.Theme;
        height = Mathf.Min(height, 5.2f);
        var width = baseSize * 0.9f;
        var local = new Vector3(x, 0f, z - chunk.StartZ);
        Part(PrimitiveType.Cube, "Bastion", chunk.Root, local + new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, width), theme.CastleStone);
        Part(PrimitiveType.Cube, "Bastion cap", chunk.Root, local + new Vector3(0f, height + 0.1f, 0f), new Vector3(width + 0.3f, 0.2f, width + 0.3f), theme.CastleTrim);
        Part(PrimitiveType.Cube, "Bastion slit", chunk.Root, local + new Vector3(0f, height * 0.6f, -width * 0.5f - 0.01f), new Vector3(0.18f, 0.8f, 0.04f), theme.WindowGlow, true);
        AddBricks(chunk.Root, local.x, local.z - width * 0.5f - 0.01f, width, height, theme.CastleTrim);
        AddBox(chunk, new Vector3(x, (height + 0.2f) * 0.5f, z), new Vector3(width + 0.3f, height + 0.2f, width + 0.3f));
    }

    // Mortar courses across a wall face: thin dark rows with staggered vertical joints.
    private static void AddBricks(Transform root, float centerX, float faceZ, float width, float height, Color mortar)
    {
        var row = 0;
        for (var y = 0.45f; y < height - 0.1f; y += 0.45f, row++)
        {
            Part(PrimitiveType.Cube, "Mortar", root, new Vector3(centerX, y, faceZ), new Vector3(width, 0.035f, 0.02f), mortar);
            for (var jx = -width * 0.5f + (row % 2 == 0 ? 0.5f : 1.3f); jx < width * 0.5f; jx += 1.6f)
            {
                Part(PrimitiveType.Cube, "Mortar joint", root, new Vector3(centerX + jx, y - 0.225f, faceZ), new Vector3(0.035f, 0.45f, 0.02f), mortar);
            }
        }
    }

    private void BuildPyramid(Chunk chunk, float x, float z, float baseSize, float height)
    {
        if (chunk.Theme.Landmark == SkyfortLandmark.Tank)
        {
            BuildTank(chunk, x, z, baseSize, height);
            return;
        }

        if (chunk.Theme.Landmark == SkyfortLandmark.Bastion)
        {
            BuildBastion(chunk, x, z, baseSize, height);
            return;
        }

        var root = chunk.Root;
        var local = new Vector3(x, 0f, z - chunk.StartZ);
        Part(PrimitiveType.Cube, "Pyramid plinth", root, local + new Vector3(0f, 0.1f, 0f), new Vector3(baseSize + 0.5f, 0.2f, baseSize + 0.5f), chunk.Theme.StoneBase);
        Pyramid("Pyramid", root, local + new Vector3(0f, 0.2f, 0f), new Vector3(baseSize, height - 0.2f, baseSize), chunk.Theme.StoneMain);
        // Glowing capstone (the top 15% of the same slope) and a band of light around the plinth.
        var cap = 0.15f;
        Pyramid("Capstone", root, local + new Vector3(0f, 0.2f + (height - 0.2f) * (1f - cap), 0f), new Vector3(baseSize * cap * 1.04f, (height - 0.2f) * cap * 1.04f, baseSize * cap * 1.04f), chunk.Theme.StoneGlow, true);
        Part(PrimitiveType.Cube, "Plinth glow", root, local + new Vector3(0f, 0.2f, -(baseSize + 0.5f) * 0.5f - 0.01f), new Vector3(baseSize + 0.5f, 0.05f, 0.03f), chunk.Theme.Neon, true);
        Part(PrimitiveType.Cube, "Plinth glow", root, local + new Vector3((baseSize + 0.5f) * 0.5f + 0.01f, 0.2f, 0f), new Vector3(0.03f, 0.05f, baseSize + 0.5f), chunk.Theme.Neon, true);
        NeonPyramidEdges(root, local + new Vector3(0f, 0.2f, 0f), baseSize, height - 0.2f, chunk.Theme.NeonAlt);
        solids.Add(new Solid
        {
            Bounds = new Bounds(new Vector3(x, height * 0.5f, z), new Vector3(baseSize, height, baseSize)),
            Chunk = chunk,
            Pyramid = true
        });
    }

    private void BuildTower(Chunk chunk, float x, float z, float radius, float height)
    {
        var tower = Group("Castle tower", chunk.Root, new Vector3(x, 0f, z - chunk.StartZ));
        var body = height - 1.2f;
        Part(PrimitiveType.Cylinder, "Tower body", tower, new Vector3(0f, body * 0.5f, 0f), new Vector3(radius * 2f, body * 0.5f, radius * 2f), chunk.Theme.CastleStone);
        Part(PrimitiveType.Cylinder, "Tower parapet", tower, new Vector3(0f, body + 0.06f, 0f), new Vector3(radius * 2.35f, 0.1f, radius * 2.35f), chunk.Theme.CastleTrim);
        Ring("Neon ring", tower, new Vector3(0f, body + 0.13f, 0f), radius * 1.2f, 0.045f, chunk.Theme.NeonAlt);
        Ring("Neon band", tower, new Vector3(0f, body * 0.5f, 0f), radius * 1.02f, 0.035f, chunk.Theme.Neon);
        Pyramid("Tower roof", tower, new Vector3(0f, body + 0.15f, 0f), new Vector3(radius * 2.1f, 1f, radius * 2.1f), chunk.Theme.Roof);
        Part(PrimitiveType.Sphere, "Roof beacon", tower, new Vector3(0f, body + 1.18f, 0f), Vector3.one * 0.18f, chunk.Theme.WindowGlow, true);
        for (var y = 1.1f; y < body - 0.5f; y += 1.4f)
        {
            Part(PrimitiveType.Cube, "Window slit", tower, new Vector3(0f, y, -radius - 0.01f), new Vector3(0.14f, 0.45f, 0.04f), chunk.Theme.WindowGlow, true);
            Part(PrimitiveType.Cube, "Window slit", tower, new Vector3(radius + 0.01f, y, 0f), new Vector3(0.04f, 0.45f, 0.14f), chunk.Theme.WindowGlow, true);
        }

        AddBox(chunk, new Vector3(x, height * 0.5f, z), new Vector3(radius * 2f, height, radius * 2f));
    }

    private void BuildCastleWall(Chunk chunk, float z, float fromX, float toX, float height)
    {
        var root = chunk.Root;
        var width = toX - fromX;
        var centerX = (fromX + toX) * 0.5f;
        var localZ = z - chunk.StartZ;
        Part(PrimitiveType.Cube, "Curtain wall", root, new Vector3(centerX, height * 0.5f, localZ), new Vector3(width, height, 1.1f), chunk.Theme.CastleStone);
        Part(PrimitiveType.Cube, "Wall walk", root, new Vector3(centerX, height + 0.05f, localZ), new Vector3(width, 0.1f, 1.3f), chunk.Theme.CastleTrim);
        if (chunk.Theme.Bricks)
        {
            AddBricks(root, centerX, localZ - 0.56f, width, height, chunk.Theme.CastleTrim);
        }
        else
        {
            Part(PrimitiveType.Cube, "Wall glow", root, new Vector3(centerX, height * 0.55f, localZ - 0.56f), new Vector3(width - 0.2f, 0.06f, 0.02f), chunk.Theme.Neon, true);
        }
        Part(PrimitiveType.Cube, "Wall top neon", root, new Vector3(centerX, height + 0.1f, localZ - 0.67f), new Vector3(width, 0.05f, 0.05f), chunk.Theme.NeonAlt, true);
        for (var x = fromX + 0.35f; x < toX - 0.2f; x += 0.9f)
        {
            Part(PrimitiveType.Cube, "Merlon", root, new Vector3(x, height + 0.3f, localZ - 0.45f), new Vector3(0.45f, 0.4f, 0.3f), chunk.Theme.CastleStone);
        }

        AddBox(chunk, new Vector3(centerX, (height + 0.5f) * 0.5f, z), new Vector3(width, height + 0.5f, 1.1f));
    }

    private void BuildKeep(Chunk chunk, float x, float z)
    {
        const float width = 4.2f;
        const float depth = 3.6f;
        const float height = 3.6f;
        var root = chunk.Root;
        var local = new Vector3(x, 0f, z - chunk.StartZ);
        Part(PrimitiveType.Cube, "Keep", root, local + new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth), chunk.Theme.CastleStone);
        Part(PrimitiveType.Cube, "Keep roof", root, local + new Vector3(0f, height + 0.05f, 0f), new Vector3(width + 0.2f, 0.1f, depth + 0.2f), chunk.Theme.CastleTrim);
        Part(PrimitiveType.Cube, "Keep door", root, local + new Vector3(0f, 0.7f, -depth * 0.5f - 0.01f), new Vector3(1f, 1.4f, 0.04f), chunk.Theme.WindowGlow, true);
        Part(PrimitiveType.Cube, "Keep roof neon", root, local + new Vector3(0f, height + 0.1f, -depth * 0.5f - 0.11f), new Vector3(width + 0.2f, 0.05f, 0.05f), chunk.Theme.NeonAlt, true);
        Part(PrimitiveType.Cube, "Keep side neon", root, local + new Vector3(width * 0.5f + 0.11f, height + 0.1f, 0f), new Vector3(0.05f, 0.05f, depth + 0.2f), chunk.Theme.NeonAlt, true);
        for (var mx = -width * 0.5f + 0.3f; mx < width * 0.5f; mx += 0.8f)
        {
            Part(PrimitiveType.Cube, "Merlon", root, local + new Vector3(mx, height + 0.3f, -depth * 0.5f + 0.15f), new Vector3(0.4f, 0.4f, 0.3f), chunk.Theme.CastleStone);
        }

        AddBox(chunk, new Vector3(x, (height + 0.5f) * 0.5f, z), new Vector3(width, height + 0.5f, depth));
        foreach (var cx in new[] { -1f, 1f })
        {
            foreach (var cz in new[] { -1f, 1f })
            {
                BuildTower(chunk, x + cx * width * 0.5f, z + cz * depth * 0.5f, 0.7f, 5.3f);
            }
        }

        // A turret guards the keep roof; dive to just above the battlements to hit it.
        AddTarget(chunk, TargetKind.Turret, turretPrefab, new Vector3(x, height + 0.1f, z), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 250);
    }

    private void BuildField(Chunk chunk, float z, float bottom, float top)
    {
        var localZ = z - chunk.StartZ;
        foreach (var side in new[] { -1f, 1f })
        {
            var pylon = Group("Field pylon", chunk.Root, new Vector3(side * 7.4f, 0f, localZ));
            Beveled("Pylon base", pylon, new Vector3(0f, 0.3f, 0f), new Vector3(1.2f, 0.6f, 1.2f), Hull, 0.12f);
            Part(PrimitiveType.Cylinder, "Pylon mast", pylon, new Vector3(0f, CeilingHeight * 0.5f, 0f), new Vector3(0.45f, CeilingHeight * 0.5f, 0.45f), Armor);
            Ring("Emitter", pylon, new Vector3(0f, bottom + 0.1f, 0f), 0.4f, 0.07f, NeonGreen);
            Ring("Emitter", pylon, new Vector3(0f, Mathf.Min(top, CeilingHeight) - 0.1f, 0f), 0.4f, 0.07f, NeonGreen);
        }

        for (var y = bottom + 0.2f; y < top - 0.1f; y += 0.42f)
        {
            var beam = Part(PrimitiveType.Cylinder, "Force beam", chunk.Root, new Vector3(0f, y, localZ), new Vector3(0.05f, 7.3f, 0.05f), NeonGreen, true, new Vector3(0f, 0f, 90f));
            beam.sharedMaterial = fieldMaterial;
        }

        var height = top - bottom;
        solids.Add(new Solid { Bounds = new Bounds(new Vector3(0f, bottom + height * 0.5f, z), new Vector3(WallHalfWidth * 2f, height, 0.5f)), Chunk = chunk });
    }

    private void BuildDeck(Chunk chunk, bool arena)
    {
        var root = chunk.Root;
        var mid = ChunkLength * 0.5f;
        Part(PrimitiveType.Cube, "Deck plate", root, new Vector3(0f, -0.25f, mid), new Vector3(WallHalfWidth * 2f + 1f, 0.5f, ChunkLength), arena ? chunk.Theme.ArenaDeck : chunk.Theme.Deck);
        for (var z = 1.5f; z < ChunkLength; z += 3f)
        {
            Part(PrimitiveType.Cube, "Deck seam", root, new Vector3(0f, 0.005f, z), new Vector3(WallHalfWidth * 2f, 0.02f, 0.05f), chunk.Theme.DeckLine);
        }

        for (var x = -6f; x <= 6f; x += 3f)
        {
            Part(PrimitiveType.Cube, "Deck lane neon", root, new Vector3(x, 0.006f, mid), new Vector3(0.06f, 0.02f, ChunkLength), arena ? Red : chunk.Theme.Neon, true);
        }

        // Tall far wall with altitude rungs, low near wall so it never hides the ship.
        Beveled("Far rampart", root, new Vector3(-WallHalfWidth - 0.6f, 2.5f, mid), new Vector3(1.2f, 5f, ChunkLength - 0.1f), chunk.Theme.Rampart, 0.15f);
        Beveled("Near rampart", root, new Vector3(WallHalfWidth + 0.6f, 0.35f, mid), new Vector3(1.2f, 0.7f, ChunkLength - 0.1f), chunk.Theme.Rampart, 0.15f);
        Part(PrimitiveType.Cube, "Near rampart light", root, new Vector3(WallHalfWidth + 0.02f, 0.62f, mid), new Vector3(0.05f, 0.06f, ChunkLength - 0.4f), arena ? Red : chunk.Theme.RampartGlow, true);
        for (var y = 1; y <= 5; y++)
        {
            var color = y == 5 ? Gold : chunk.Theme.RampartGlow;
            Part(PrimitiveType.Cube, "Altitude rung", root, new Vector3(-WallHalfWidth + 0.01f, y - 0.03f, mid), new Vector3(0.04f, 0.05f, ChunkLength - 0.4f), color, true);
        }

        if (!arena)
        {
            Part(PrimitiveType.Cylinder, "Rampart lamp", root, new Vector3(-WallHalfWidth - 0.6f, 5.15f, mid), new Vector3(0.5f, 0.15f, 0.5f), Gold, true);
        }
    }

    private void BuildSpaceFloor(Chunk chunk)
    {
        var root = chunk.Root;
        var mid = ChunkLength * 0.5f;
        Part(PrimitiveType.Cube, "Void plate", root, new Vector3(0f, -0.25f, mid), new Vector3(WallHalfWidth * 2f + 1f, 0.5f, ChunkLength), chunk.Theme.MidFloor);
        var colors = chunk.Theme.StarColors;
        var starCount = colors != null ? 26 : 14;
        for (var i = 0; i < starCount; i++)
        {
            var star = new Vector3(Random.Range(-WallHalfWidth, WallHalfWidth), 0.02f, Random.Range(0f, ChunkLength));
            var color = colors != null ? colors[Random.Range(0, colors.Length)] : chunk.Theme.MidSpeck;
            Part(PrimitiveType.Sphere, "Star", root, star, Vector3.one * Random.Range(0.05f, 0.14f), color, chunk.Theme.MidSpecksGlow);
        }

        // Distant planets and colored comet streaks lying on the void floor.
        for (var i = 0; i < chunk.Theme.Planets; i++)
        {
            var size = Random.Range(0.5f, 1.1f);
            var planet = new Vector3(Random.Range(-WallHalfWidth, WallHalfWidth), 0.05f, Random.Range(2f, ChunkLength - 2f));
            Part(PrimitiveType.Sphere, "Planet", root, planet, new Vector3(size, size * 0.2f, size), chunk.Theme.PlanetColor, true);
        }

        if (colors != null)
        {
            for (var i = 0; i < 3; i++)
            {
                var streak = new Vector3(Random.Range(-WallHalfWidth, WallHalfWidth), 0.03f, Random.Range(1f, ChunkLength - 1f));
                Part(PrimitiveType.Cube, "Comet streak", root, streak, new Vector3(0.06f, 0.02f, Random.Range(0.7f, 1.4f)), colors[Random.Range(0, colors.Length)], true, new Vector3(0f, Random.Range(-35f, 35f), 0f));
            }
        }

        for (var z = 0f; z < ChunkLength; z += 6f)
        {
            Part(PrimitiveType.Cube, "Nav grid neon", root, new Vector3(0f, 0.01f, z), new Vector3(WallHalfWidth * 2f, 0.01f, 0.04f), Color.Lerp(Color.black, chunk.Theme.Neon, 0.45f), true);
        }
    }

    // ---------------------------------------------------------------- Effects

    private void AnimateWorld()
    {
        var pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 18f);
        fieldMaterial.SetColor("_EmissionColor", NeonGreen * (1.2f + pulse * 1.6f));
        UpdateAtmosphere(false);
        if (flash.intensity > 0f)
        {
            flash.intensity = Mathf.Max(0f, flash.intensity - Time.deltaTime * 30f);
        }
    }

    private void Explode(Vector3 position, Color color, int count, bool big)
    {
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true,
            startColor = color
        };
        explosionFx.Emit(emit, count);
        emit.startColor = Color.white;
        explosionFx.Emit(emit, count / 4);
        if (big)
        {
            flash.transform.position = position + Vector3.up;
            flash.color = color;
            flash.intensity = 8f;
            if (sounds != null) sfx.PlayOneShot(sounds.Explosion, 0.9f);
        }
    }
}
