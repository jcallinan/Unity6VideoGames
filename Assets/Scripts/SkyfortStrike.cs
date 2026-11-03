using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static SkyfortModels;
using Random = UnityEngine.Random;

// Skyfort Strike: an isometric, forward-scrolling altitude shooter. The fighter flies on its own; you steer
// left/right, climb/dive to clear walls and force fields, shoot ground targets at their height, keep your
// fuel up by destroying fuel tanks, and face the Warden guardian at the end of each level.
// Everything (models, level, audio, HUD) is generated at runtime by this component.
public sealed class SkyfortStrike : MonoBehaviour
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
    private const float FuelDrain = 3.4f;
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

    private enum ChunkKind { Entry, LowWall, Gate, FieldLow, FieldHigh, TurretRow, FuelDepot, RadarSite, Mixed, Exit, Space, Arena, BossArena, Open, Drift }
    private enum TargetKind { Turret, FuelTank, Radar, Drone }
    private enum Phase { Loading, Countdown, Playing, Respawning, GameOver }

    private sealed class Chunk
    {
        public Transform Root;
        public float StartZ;
        public bool Space;
    }

    private struct Solid
    {
        public Bounds Bounds;
        public Chunk Chunk;
        public string Reason;
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
    private readonly Queue<ChunkKind> plan = new();
    private readonly SkyfortHudState hud = new();

    private Transform templates;
    private GameObject turretTemplate;
    private GameObject fuelTemplate;
    private GameObject radarTemplate;
    private GameObject droneTemplate;
    private GameObject laserTemplate;
    private GameObject boltTemplate;
    private Material fieldMaterial;

    private Camera view;
    private Transform ship;
    private Transform shipModel;
    private Transform shipShadow;
    private Rigidbody shipBody;
    private ParticleSystem explosionFx;
    private ParticleSystem engineFx;
    private Light flash;

    private Transform boss;
    private Transform bossLeftCannon;
    private Transform bossRightCannon;
    private Renderer bossCore;
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
    private AudioSource sfx;
    private AudioSource engine;

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
    private bool safeSpace;
    private Vector2 steer;

    // Game logic reads the physics position; the transform is interpolated for smooth visuals only.
    private Vector3 ShipPosition => shipBody.position;

    private async void Start()
    {
        Application.targetFrameRate = 60;
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        BuildWorld();
        SkyfortHud.Create(transform, hud);
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
            BuildAudio();
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
        var velocity = new Vector3(steer.x * 9f, steer.y * 6.5f, speed);
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

    private void LateUpdate()
    {
        if (view == null || ship == null)
        {
            return;
        }

        var focus = new Vector3(0f, 2.5f, ship.position.z + 7f);
        view.transform.position = focus - view.transform.forward * 60f;
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
        StreamLevel();

        try
        {
            await CountdownAsync(id, $"LEVEL {level}");
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
            TrimChunksBeyond(z - ChunkLength);
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
        // Teleport without interpolation, otherwise the transform sweeps from the old pose through the new course.
        // FixedUpdate turns interpolation back on once the ship is flying again.
        shipBody.interpolation = RigidbodyInterpolation.None;
        var spawn = new Vector3(0f, 3f, z);
        shipBody.position = spawn;
        ship.position = spawn;
        shipBody.linearVelocity = Vector3.zero;
        fuel = FuelMax;
        nextFire = 0f;
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
        hud.Zone = bossActive ? "WARDEN" : CurrentChunk()?.Space == true ? "OPEN SPACE" : "SKYFORT";
    }

    // ---------------------------------------------------------------- Ship

    private void UpdateShip()
    {
        var x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        var y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        steer = Vector2.MoveTowards(steer, new Vector2(x, y), Time.deltaTime * 8f);
        shipModel.localRotation = Quaternion.Euler(-steer.y * 14f, 0f, -steer.x * 28f);

        var blinking = Time.time < invulnerableUntil && Mathf.Repeat(Time.time * 10f, 1f) < 0.35f;
        shipModel.gameObject.SetActive(!blinking);

        var firing = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.LeftControl);
        if (firing && Time.time >= nextFire)
        {
            nextFire = Time.time + FireInterval;
            var laser = Take(lasers);
            if (laser != null)
            {
                laser.Transform.position = ShipPosition + new Vector3(0f, 0f, 1.2f);
                laser.Velocity = new Vector3(0f, 0f, LaserSpeed + speed);
                laser.Life = LaserRange / LaserSpeed;
                sfx.PlayOneShot(sounds.Laser, 0.55f);
            }
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

    private void CheckShipCollisions()
    {
        if (Time.time < invulnerableUntil)
        {
            return;
        }

        var shipBounds = new Bounds(ShipPosition, ShipHalf * 2f);
        foreach (var solid in solids)
        {
            if (solid.Bounds.Intersects(shipBounds))
            {
                CrashAsync(solid.Reason);
                return;
            }
        }

        foreach (var target in targets)
        {
            if (new Bounds(target.Center, target.Half * 2f).Intersects(shipBounds))
            {
                CrashAsync("COLLISION");
                return;
            }
        }

        if (bossActive && !bossRetreating && new Bounds(boss.position + new Vector3(0f, 3.5f, 0f), new Vector3(5.4f, 7f, 2.4f)).Intersects(shipBounds))
        {
            CrashAsync("COLLISION");
        }
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
                CrashAsync("SHOT DOWN");
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
            if (solid.Bounds.IntersectRay(ray, out var d) && d <= closest)
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
            if (solid.Bounds.Contains(position)) return true;
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
        var laserOp = InstantiateAsync(laserTemplate, LaserPoolSize, templates.parent);
        var boltOp = InstantiateAsync(boltTemplate, BoltPoolSize, templates.parent);
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
        _ = FlashBannerAsync(runId, defeated ? "WARDEN DESTROYED" : $"LEVEL {level}", defeated ? $"+{5000 * (level - 1)}  -  Level {level} ahead" : "Speed increasing");
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
        var fortressA = new[] { ChunkKind.LowWall, ChunkKind.Gate, ChunkKind.FieldLow, ChunkKind.TurretRow, ChunkKind.RadarSite };
        var fortressB = new[] { ChunkKind.Gate, ChunkKind.FieldLow, ChunkKind.FieldHigh, ChunkKind.TurretRow, ChunkKind.Mixed, ChunkKind.RadarSite, ChunkKind.LowWall };

        plan.Enqueue(ChunkKind.Arena);
        plan.Enqueue(ChunkKind.Entry);
        AddFortress(fortressA, 6 + number);
        plan.Enqueue(ChunkKind.Exit);
        for (var i = 0; i < 4 + number; i++)
        {
            plan.Enqueue(ChunkKind.Space);
        }

        plan.Enqueue(ChunkKind.Entry);
        AddFortress(fortressB, 7 + number);
        plan.Enqueue(ChunkKind.Exit);
        plan.Enqueue(ChunkKind.BossArena);

        void AddFortress(ChunkKind[] pool, int count)
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

                plan.Enqueue(next);
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
            if (safeChunks > 0)
            {
                safeChunks--;
                kind = safeSpace ? ChunkKind.Drift : ChunkKind.Open;
            }
            else if (plan.Count > 0)
            {
                kind = plan.Dequeue();
            }
            else
            {
                kind = ChunkKind.Arena;
            }

            BuildChunk(kind, spawnZ);
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

    private Chunk CurrentChunk()
    {
        if (ship == null) return null;
        var z = ShipPosition.z;
        foreach (var chunk in chunks)
        {
            if (z >= chunk.StartZ && z < chunk.StartZ + ChunkLength) return chunk;
        }

        return null;
    }

    private void TrimChunksBeyond(float z)
    {
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            if (chunks[i].StartZ > z)
            {
                RemoveChunk(chunks[i]);
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
        Destroy(chunk.Root.gameObject);
    }

    private void ClearWorld()
    {
        while (chunks.Count > 0)
        {
            RemoveChunk(chunks[0]);
        }
    }

    private void BuildChunk(ChunkKind kind, float z0)
    {
        var chunk = new Chunk
        {
            Root = Group($"Chunk {kind} @ {z0:0}", null, new Vector3(0f, 0f, z0)),
            StartZ = z0,
            Space = kind is ChunkKind.Space or ChunkKind.Drift
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
                BuildGate(chunk, z0 + 6f, 0f, 2.2f, 3.4f);
                break;
            case ChunkKind.Exit:
                BuildGate(chunk, z0 + 12f, Random.Range(-3f, 3f), Random.Range(1f, 3.5f), 3.2f);
                AddTarget(chunk, TargetKind.Turret, turretTemplate, new Vector3(Random.Range(-4f, 4f), 0f, z0 + 4f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.LowWall:
                BuildBarrier(chunk, z0 + 9f, Random.Range(1.8f, 3.3f));
                AddTarget(chunk, TargetKind.Turret, turretTemplate, new Vector3(Random.Range(-5f, 5f), 0f, z0 + 15f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.Gate:
                BuildGate(chunk, z0 + 9f, Random.Range(-3.8f, 3.8f), Random.Range(MinAltitude, 4f), Mathf.Max(2.4f, 3.2f - level * 0.15f));
                break;
            case ChunkKind.FieldLow:
                BuildField(chunk, z0 + 9f, 0f, Random.Range(2.6f, 4.6f));
                break;
            case ChunkKind.FieldHigh:
                BuildField(chunk, z0 + 9f, Random.Range(2.8f, 4.6f), CeilingHeight);
                break;
            case ChunkKind.TurretRow:
                for (var i = 0; i < 2 + Mathf.Min(level, 3); i++)
                {
                    var x = Mathf.Lerp(-5f, 5f, (i + 0.5f) / (2 + Mathf.Min(level, 3)));
                    AddTarget(chunk, TargetKind.Turret, turretTemplate, new Vector3(x, 0f, z0 + 5f + (i % 2) * 6f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                }

                break;
            case ChunkKind.FuelDepot:
                AddTarget(chunk, TargetKind.FuelTank, fuelTemplate, new Vector3(Random.Range(-4.5f, -1f), 0f, z0 + 6f), new Vector3(0.75f, 0.9f, 0.75f), 0.9f, 1, 300);
                AddTarget(chunk, TargetKind.FuelTank, fuelTemplate, new Vector3(Random.Range(1f, 4.5f), 0f, z0 + 12f), new Vector3(0.75f, 0.9f, 0.75f), 0.9f, 1, 300);
                break;
            case ChunkKind.RadarSite:
                AddTarget(chunk, TargetKind.Radar, radarTemplate, new Vector3(Random.Range(-4f, 4f), 0f, mid), new Vector3(0.9f, 1.3f, 0.9f), 1.3f, 2, 1000);
                AddTarget(chunk, TargetKind.Turret, turretTemplate, new Vector3(Random.Range(-5f, 5f), 0f, z0 + 3f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.Mixed:
                BuildBarrier(chunk, z0 + 5f, Random.Range(1.6f, 2.6f));
                AddTarget(chunk, TargetKind.FuelTank, fuelTemplate, new Vector3(Random.Range(-4f, 4f), 0f, z0 + 10f), new Vector3(0.75f, 0.9f, 0.75f), 0.9f, 1, 300);
                AddTarget(chunk, TargetKind.Turret, turretTemplate, new Vector3(Random.Range(-5f, 5f), 0f, z0 + 15f), new Vector3(0.7f, 0.65f, 0.7f), 0.65f, 1, 150);
                break;
            case ChunkKind.Space:
                for (var i = 0; i < 2 + Mathf.Min(level, 2); i++)
                {
                    var origin = new Vector3(Random.Range(-4f, 4f), Random.Range(1.5f, 5.5f), z0 + 8f + i * 5f);
                    AddTarget(chunk, TargetKind.Drone, droneTemplate, origin, new Vector3(0.9f, 0.4f, 0.7f), 0f, 1, 250);
                }

                break;
            case ChunkKind.BossArena:
                bossPending = true;
                bossTriggerZ = z0;
                break;
        }
    }

    private void AddSolid(Chunk chunk, Vector3 center, Vector3 size, Color color, float bevel = 0.12f)
    {
        if (size.x <= 0.01f || size.y <= 0.01f) return;
        var local = center - chunk.Root.position;
        Beveled("Fortress wall", chunk.Root, local, size, color, bevel);
        Beveled("Wall trim", chunk.Root, local + new Vector3(0f, size.y * 0.5f - 0.08f, -size.z * 0.5f - 0.02f), new Vector3(size.x - 0.1f, 0.08f, 0.04f), Cyan, 0.02f, true);
        solids.Add(new Solid { Bounds = new Bounds(center, size), Chunk = chunk, Reason = "HIT THE WALL" });
    }

    private void BuildBarrier(Chunk chunk, float z, float height)
    {
        AddSolid(chunk, new Vector3(0f, height * 0.5f, z), new Vector3(WallHalfWidth * 2f, height, 1.2f), Armor);
        // Height ladder painted on the wall face helps read its altitude.
        for (var y = 1f; y < height; y += 1f)
        {
            Part(PrimitiveType.Cube, "Height mark", chunk.Root, new Vector3(-WallHalfWidth + 0.6f, y, z - chunk.StartZ - 0.62f), new Vector3(0.6f, 0.06f, 0.02f), Gold, true);
        }
    }

    private void BuildGate(Chunk chunk, float z, float gapX, float gapY, float gapHeight)
    {
        const float gapWidth = 4.4f;
        var left = gapX - gapWidth * 0.5f;
        var right = gapX + gapWidth * 0.5f;
        var top = gapY + gapHeight;
        AddSolid(chunk, new Vector3((-WallHalfWidth + left) * 0.5f, CeilingHeight * 0.5f, z), new Vector3(left + WallHalfWidth, CeilingHeight, 1.2f), Hull);
        AddSolid(chunk, new Vector3((WallHalfWidth + right) * 0.5f, CeilingHeight * 0.5f, z), new Vector3(WallHalfWidth - right, CeilingHeight, 1.2f), Hull);
        AddSolid(chunk, new Vector3(gapX, gapY * 0.5f, z), new Vector3(gapWidth, gapY, 1.2f), Armor);
        AddSolid(chunk, new Vector3(gapX, (top + CeilingHeight) * 0.5f, z), new Vector3(gapWidth, CeilingHeight - top, 1.2f), Armor);

        // Glowing frame around the opening.
        var localZ = z - chunk.StartZ - 0.64f;
        var frameMid = gapY + gapHeight * 0.5f;
        Part(PrimitiveType.Cube, "Gate frame", chunk.Root, new Vector3(gapX, gapY, localZ), new Vector3(gapWidth, 0.08f, 0.04f), Gold, true);
        Part(PrimitiveType.Cube, "Gate frame", chunk.Root, new Vector3(gapX, top, localZ), new Vector3(gapWidth, 0.08f, 0.04f), Gold, true);
        Part(PrimitiveType.Cube, "Gate frame", chunk.Root, new Vector3(left, frameMid, localZ), new Vector3(0.08f, gapHeight, 0.04f), Gold, true);
        Part(PrimitiveType.Cube, "Gate frame", chunk.Root, new Vector3(right, frameMid, localZ), new Vector3(0.08f, gapHeight, 0.04f), Gold, true);
    }

    private void BuildField(Chunk chunk, float z, float bottom, float top)
    {
        var localZ = z - chunk.StartZ;
        foreach (var side in new[] { -1f, 1f })
        {
            var pylon = Group("Field pylon", chunk.Root, new Vector3(side * 7.4f, 0f, localZ));
            Beveled("Pylon base", pylon, new Vector3(0f, 0.3f, 0f), new Vector3(1.2f, 0.6f, 1.2f), Hull, 0.12f);
            Part(PrimitiveType.Cylinder, "Pylon mast", pylon, new Vector3(0f, CeilingHeight * 0.5f, 0f), new Vector3(0.45f, CeilingHeight * 0.5f, 0.45f), Armor);
            Ring("Emitter", pylon, new Vector3(0f, bottom + 0.1f, 0f), 0.4f, 0.07f, Magenta);
            Ring("Emitter", pylon, new Vector3(0f, Mathf.Min(top, CeilingHeight) - 0.1f, 0f), 0.4f, 0.07f, Magenta);
        }

        for (var y = bottom + 0.2f; y < top - 0.1f; y += 0.42f)
        {
            var beam = Part(PrimitiveType.Cylinder, "Force beam", chunk.Root, new Vector3(0f, y, localZ), new Vector3(0.05f, 7.3f, 0.05f), Magenta, true, new Vector3(0f, 0f, 90f));
            beam.sharedMaterial = fieldMaterial;
        }

        var height = top - bottom;
        solids.Add(new Solid { Bounds = new Bounds(new Vector3(0f, bottom + height * 0.5f, z), new Vector3(WallHalfWidth * 2f, height, 0.5f)), Chunk = chunk, Reason = "FORCE FIELD!" });
    }

    private void BuildDeck(Chunk chunk, bool arena)
    {
        var root = chunk.Root;
        var mid = ChunkLength * 0.5f;
        Part(PrimitiveType.Cube, "Deck plate", root, new Vector3(0f, -0.25f, mid), new Vector3(WallHalfWidth * 2f + 1f, 0.5f, ChunkLength), arena ? new Color(0.06f, 0.05f, 0.1f) : Deck);
        for (var z = 1.5f; z < ChunkLength; z += 3f)
        {
            Part(PrimitiveType.Cube, "Deck seam", root, new Vector3(0f, 0.005f, z), new Vector3(WallHalfWidth * 2f, 0.02f, 0.05f), DeckLine);
        }

        for (var x = -6f; x <= 6f; x += 3f)
        {
            Part(PrimitiveType.Cube, "Deck lane", root, new Vector3(x, 0.006f, mid), new Vector3(0.05f, 0.02f, ChunkLength), DeckLine);
        }

        // Tall far wall with altitude rungs, low near wall so it never hides the ship.
        Beveled("Far rampart", root, new Vector3(-WallHalfWidth - 0.6f, 2.5f, mid), new Vector3(1.2f, 5f, ChunkLength - 0.1f), Hull, 0.15f);
        Beveled("Near rampart", root, new Vector3(WallHalfWidth + 0.6f, 0.35f, mid), new Vector3(1.2f, 0.7f, ChunkLength - 0.1f), Hull, 0.15f);
        Part(PrimitiveType.Cube, "Near rampart light", root, new Vector3(WallHalfWidth + 0.02f, 0.62f, mid), new Vector3(0.05f, 0.06f, ChunkLength - 0.4f), arena ? Red : Cyan, true);
        for (var y = 1; y <= 5; y++)
        {
            var color = y == 5 ? Gold : Cyan;
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
        Part(PrimitiveType.Cube, "Void plate", root, new Vector3(0f, -0.25f, mid), new Vector3(WallHalfWidth * 2f + 1f, 0.5f, ChunkLength), VoidColor);
        for (var i = 0; i < 14; i++)
        {
            var star = new Vector3(Random.Range(-WallHalfWidth, WallHalfWidth), 0.02f, Random.Range(0f, ChunkLength));
            Part(PrimitiveType.Sphere, "Star", root, star, Vector3.one * Random.Range(0.05f, 0.14f), new Color(0.5f, 0.7f, 1f), true);
        }

        for (var z = 0f; z < ChunkLength; z += 6f)
        {
            Part(PrimitiveType.Cube, "Nav grid", root, new Vector3(0f, 0.01f, z), new Vector3(WallHalfWidth * 2f, 0.01f, 0.03f), new Color(0.08f, 0.12f, 0.25f));
        }
    }

    // ---------------------------------------------------------------- Effects

    private void AnimateWorld()
    {
        var pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 18f);
        fieldMaterial.SetColor("_EmissionColor", Magenta * (1.2f + pulse * 1.6f));
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

    // ---------------------------------------------------------------- World construction

    private void BuildWorld()
    {
        var cameraObject = new GameObject("Isometric Camera") { tag = "MainCamera" };
        view = cameraObject.AddComponent<Camera>();
        view.orthographic = true;
        view.orthographicSize = 9.5f;
        view.nearClipPlane = 1f;
        view.farClipPlane = 200f;
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.01f, 0.015f, 0.04f);
        view.allowHDR = true;
        cameraObject.transform.rotation = Quaternion.Euler(33f, -45f, 0f);
        cameraObject.AddComponent<AudioListener>();

        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.8f;
        sun.transform.rotation = Quaternion.Euler(58f, -20f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.3f);
        QualitySettings.shadowDistance = 90f;

        flash = new GameObject("Explosion Flash").AddComponent<Light>();
        flash.type = LightType.Point;
        flash.range = 9f;
        flash.intensity = 0f;

        fieldMaterial = new Material(Mat(Magenta, true)) { name = "Force field" };

        templates = Group("Templates", transform, Vector3.zero);
        templates.gameObject.SetActive(false);
        turretTemplate = BuildTurret();
        fuelTemplate = BuildFuelTank();
        radarTemplate = BuildRadar();
        droneTemplate = BuildDrone();
        laserTemplate = BuildLaser();
        boltTemplate = BuildBolt();

        BuildShip();
        BuildBoss();
        explosionFx = BuildExplosionFx();
    }

    private void BuildAudio()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.volume = 0.8f;

        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = sounds.Engine;
        engine.loop = true;
        engine.volume = 0.25f;
        engine.Play();
    }

    private void BuildShip()
    {
        ship = Group("Strike Fighter", null, new Vector3(0f, 3f, 0f));
        shipBody = ship.gameObject.AddComponent<Rigidbody>();
        shipBody.useGravity = false;
        shipBody.isKinematic = false;
        shipBody.interpolation = RigidbodyInterpolation.Interpolate;
        shipBody.constraints = RigidbodyConstraints.FreezeRotation;
        shipBody.detectCollisions = false;

        shipModel = Group("Fighter model", ship, Vector3.zero);
        Beveled("Fuselage", shipModel, new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.4f, 2f), Bone, 0.14f, false, new Vector3(0f, 0f, 0f));
        Beveled("Nose", shipModel, new Vector3(0f, -0.02f, 1.15f), new Vector3(0.4f, 0.26f, 0.6f), Bone, 0.1f);
        Part(PrimitiveType.Sphere, "Nose cone", shipModel, new Vector3(0f, -0.02f, 1.45f), new Vector3(0.28f, 0.2f, 0.4f), Red);
        Part(PrimitiveType.Sphere, "Canopy", shipModel, new Vector3(0f, 0.22f, 0.45f), new Vector3(0.34f, 0.22f, 0.7f), Cyan, true);
        Beveled("Spine", shipModel, new Vector3(0f, 0.24f, -0.35f), new Vector3(0.3f, 0.14f, 0.9f), Hull, 0.05f);
        var wing = CreateWingMesh(1.35f, 1.3f, 0.35f, 0.75f, 0.08f);
        MeshPart("Right wing", shipModel, new Vector3(0.28f, -0.05f, -0.3f), Vector3.zero, wing, Hull, false);
        var leftWing = MeshPart("Left wing", shipModel, new Vector3(-0.28f, -0.05f, -0.3f), Vector3.zero, wing, Hull, false);
        leftWing.transform.localScale = new Vector3(-1f, 1f, 1f);
        foreach (var side in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cube, "Wing stripe", shipModel, new Vector3(side * 1.05f, 0f, -0.75f), new Vector3(0.5f, 0.1f, 0.12f), Gold, true);
            Part(PrimitiveType.Cylinder, "Wing cannon", shipModel, new Vector3(side * 1.55f, -0.02f, -0.45f), new Vector3(0.09f, 0.45f, 0.09f), Armor, false, new Vector3(90f, 0f, 0f));
            Beveled("Tail fin", shipModel, new Vector3(side * 0.3f, 0.38f, -0.8f), new Vector3(0.06f, 0.5f, 0.45f), Hull, 0.02f, false, new Vector3(0f, 0f, side * -18f));
            Part(PrimitiveType.Cylinder, "Engine pod", shipModel, new Vector3(side * 0.42f, -0.06f, -0.85f), new Vector3(0.28f, 0.35f, 0.28f), Armor, false, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Afterburner", shipModel, new Vector3(side * 0.42f, -0.06f, -1.22f), new Vector3(0.24f, 0.24f, 0.12f), Cyan, true);
            Part(PrimitiveType.Sphere, "Nav light", shipModel, new Vector3(side * 1.6f, 0.02f, -0.9f), Vector3.one * 0.1f, side < 0 ? Red : Lime, true);
        }

        DisableShadows(shipModel);
        var shadow = Part(PrimitiveType.Cylinder, "Ground shadow", null, Vector3.zero, new Vector3(1.5f, 0.01f, 1.9f), ShadowTint);
        shadow.shadowCastingMode = ShadowCastingMode.Off;
        shipShadow = shadow.transform;

        engineFx = new GameObject("Engine trail").AddComponent<ParticleSystem>();
        engineFx.transform.SetParent(ship, false);
        engineFx.transform.localPosition = new Vector3(0f, -0.06f, -1.3f);
        engineFx.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var main = engineFx.main;
        main.startLifetime = 0.35f;
        main.startSpeed = 4f;
        main.startSize = 0.22f;
        main.startColor = new Color(0.3f, 0.85f, 1f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = engineFx.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.2f;
        var size = engineFx.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        engineFx.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial();
    }

    private ParticleSystem BuildExplosionFx()
    {
        var fx = new GameObject("Explosion FX").AddComponent<ParticleSystem>();
        fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = fx.main;
        main.loop = false;
        main.playOnAwake = false;
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
        fx.Play();
        return fx;
    }

    private GameObject BuildTurret()
    {
        var root = Group("Turret", templates, Vector3.zero);
        Beveled("Turret base", root, new Vector3(0f, 0.25f, 0f), new Vector3(1.3f, 0.5f, 1.3f), Hull, 0.18f);
        Ring("Base glow", root, new Vector3(0f, 0.52f, 0f), 0.5f, 0.05f, Red);
        var head = Group("Head", root, new Vector3(0f, 0.8f, 0f));
        Part(PrimitiveType.Sphere, "Turret dome", head, Vector3.zero, new Vector3(0.9f, 0.6f, 0.9f), Armor);
        Part(PrimitiveType.Cylinder, "Barrel", head, new Vector3(0f, 0.05f, 0.55f), new Vector3(0.14f, 0.4f, 0.14f), Hull, false, new Vector3(90f, 0f, 0f));
        Part(PrimitiveType.Sphere, "Sensor", head, new Vector3(0f, 0.2f, 0.32f), Vector3.one * 0.16f, Red, true);
        return root.gameObject;
    }

    private GameObject BuildFuelTank()
    {
        var root = Group("Fuel tank", templates, Vector3.zero);
        Beveled("Tank cradle", root, new Vector3(0f, 0.12f, 0f), new Vector3(1.5f, 0.24f, 1.5f), Hull, 0.1f);
        Part(PrimitiveType.Cylinder, "Tank", root, new Vector3(0f, 0.95f, 0f), new Vector3(1.15f, 0.7f, 1.15f), Gold);
        Part(PrimitiveType.Sphere, "Tank cap", root, new Vector3(0f, 1.65f, 0f), new Vector3(1.1f, 0.45f, 1.1f), Gold);
        Ring("Fuel band", root, new Vector3(0f, 0.7f, 0f), 0.6f, 0.05f, Red);
        Ring("Fuel band", root, new Vector3(0f, 1.2f, 0f), 0.6f, 0.05f, Red);
        Part(PrimitiveType.Cube, "Fuel glyph", root, new Vector3(0f, 0.95f, -0.58f), new Vector3(0.36f, 0.36f, 0.04f), Red, true);
        return root.gameObject;
    }

    private GameObject BuildRadar()
    {
        var root = Group("Radar dish", templates, Vector3.zero);
        Beveled("Radar plinth", root, new Vector3(0f, 0.2f, 0f), new Vector3(1.6f, 0.4f, 1.6f), Hull, 0.15f);
        Part(PrimitiveType.Cylinder, "Mast", root, new Vector3(0f, 1f, 0f), new Vector3(0.2f, 0.7f, 0.2f), Armor);
        var head = Group("Head", root, new Vector3(0f, 1.8f, 0f));
        Part(PrimitiveType.Sphere, "Dish", head, new Vector3(0f, 0.2f, 0.1f), new Vector3(1.7f, 1.2f, 0.25f), Bone, false, new Vector3(-20f, 0f, 0f));
        Part(PrimitiveType.Cylinder, "Feed horn", head, new Vector3(0f, 0.25f, -0.35f), new Vector3(0.06f, 0.35f, 0.06f), Armor, false, new Vector3(70f, 0f, 0f));
        Part(PrimitiveType.Sphere, "Emitter", head, new Vector3(0f, 0.36f, -0.7f), Vector3.one * 0.18f, Lime, true);
        return root.gameObject;
    }

    private GameObject BuildDrone()
    {
        var root = Group("Drone", templates, Vector3.zero);
        Beveled("Drone hull", root, Vector3.zero, new Vector3(0.9f, 0.4f, 1f), Hull, 0.14f);
        Part(PrimitiveType.Sphere, "Drone eye", root, new Vector3(0f, 0f, 0.5f), new Vector3(0.36f, 0.26f, 0.12f), Red, true);
        foreach (var side in new[] { -1f, 1f })
        {
            Beveled("Drone wing", root, new Vector3(side * 0.75f, 0f, -0.1f), new Vector3(0.7f, 0.08f, 0.6f), Armor, 0.03f, false, new Vector3(0f, side * 20f, side * -10f));
            Part(PrimitiveType.Sphere, "Wing tip", root, new Vector3(side * 1.08f, 0f, -0.2f), Vector3.one * 0.14f, Magenta, true);
        }

        Ring("Drone rotor", root, new Vector3(0f, 0.25f, 0f), 0.35f, 0.04f, Magenta);
        return root.gameObject;
    }

    private GameObject BuildLaser()
    {
        var root = Group("Laser", templates, Vector3.zero);
        Part(PrimitiveType.Capsule, "Laser bolt", root, Vector3.zero, new Vector3(0.12f, 0.6f, 0.12f), Lime, true, new Vector3(90f, 0f, 0f));
        return root.gameObject;
    }

    private GameObject BuildBolt()
    {
        var root = Group("Enemy bolt", templates, Vector3.zero);
        Part(PrimitiveType.Sphere, "Plasma", root, Vector3.zero, Vector3.one * 0.38f, Red, true);
        Part(PrimitiveType.Sphere, "Plasma core", root, Vector3.zero, Vector3.one * 0.2f, Bone, true);
        return root.gameObject;
    }

    private void BuildBoss()
    {
        // The Warden: a towering original guardian mech with a glowing core eye as its weak point.
        boss = Group("The Warden", null, Vector3.zero);
        Beveled("Hips", boss, new Vector3(0f, 1.6f, 0f), new Vector3(2.4f, 0.8f, 1.4f), Hull, 0.2f);
        foreach (var side in new[] { -1f, 1f })
        {
            Beveled("Leg", boss, new Vector3(side * 1.1f, 0.8f, 0f), new Vector3(0.8f, 1.6f, 1f), Armor, 0.18f);
            Beveled("Foot", boss, new Vector3(side * 1.1f, 0.15f, -0.2f), new Vector3(1.1f, 0.3f, 1.6f), Hull, 0.1f);
            Beveled("Shoulder", boss, new Vector3(side * 2.15f, 5.2f, 0f), new Vector3(1.2f, 1f, 1.3f), Armor, 0.2f);
            var cannon = Group(side < 0 ? "Left cannon" : "Right cannon", boss, new Vector3(side * 2.35f, 4.1f, 0f));
            Beveled("Arm", cannon, new Vector3(0f, 0f, 0f), new Vector3(0.75f, 1.6f, 0.75f), Hull, 0.15f);
            Part(PrimitiveType.Cylinder, "Cannon barrel", cannon, new Vector3(0f, -0.4f, 0.9f), new Vector3(0.4f, 0.8f, 0.4f), Armor, false, new Vector3(90f, 0f, 0f));
            Ring("Muzzle glow", cannon, new Vector3(0f, -0.4f, 1.7f), 0.22f, 0.06f, Red, true, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Shoulder lamp", boss, new Vector3(side * 2.15f, 5.8f, 0.55f), Vector3.one * 0.25f, Gold, true);
            if (side < 0) bossLeftCannon = cannon;
            else bossRightCannon = cannon;
        }

        Beveled("Torso", boss, new Vector3(0f, 3.9f, 0f), new Vector3(3.2f, 3.2f, 1.8f), Armor, 0.35f);
        Beveled("Chest plate", boss, new Vector3(0f, 3.9f, 0.88f), new Vector3(2.4f, 2.4f, 0.15f), Hull, 0.25f);
        Ring("Core housing", boss, new Vector3(0f, 4.1f, 0.98f), 0.75f, 0.12f, Gold, true, new Vector3(90f, 0f, 0f));
        bossCore = Part(PrimitiveType.Sphere, "Core eye", boss, new Vector3(0f, 4.1f, 1.05f), Vector3.one * 1.1f, Red, true);
        Beveled("Head", boss, new Vector3(0f, 6.1f, 0.1f), new Vector3(1.3f, 0.9f, 1.1f), Hull, 0.2f);
        Part(PrimitiveType.Cube, "Visor", boss, new Vector3(0f, 6.15f, 0.66f), new Vector3(0.9f, 0.16f, 0.04f), Red, true);
        Part(PrimitiveType.Cylinder, "Antenna", boss, new Vector3(0.4f, 6.9f, 0f), new Vector3(0.05f, 0.45f, 0.05f), Bone);
        Part(PrimitiveType.Sphere, "Antenna tip", boss, new Vector3(0.4f, 7.35f, 0f), Vector3.one * 0.16f, Red, true);
        boss.gameObject.SetActive(false);
    }
}
