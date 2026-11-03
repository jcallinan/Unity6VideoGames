using System.Collections.Generic;
using UnityEngine;

public sealed class RiftRunner : MonoBehaviour
{
    private const float ArenaHalfSize = 7.5f;
    private const int TargetScore = 8;
    private const float RoundDuration = 60f;

    private static readonly Color FloorColor = new(0.035f, 0.055f, 0.12f);
    private static readonly Color TrimColor = new(0.08f, 0.2f, 0.32f);
    private static readonly Color Cyan = new(0.12f, 0.82f, 1f);
    private static readonly Color Gold = new(1f, 0.62f, 0.12f);
    private static readonly Color Red = new(1f, 0.12f, 0.22f);
    private static readonly Color White = new(0.72f, 0.9f, 1f);

    private readonly Vector3[] pickupPositions =
    {
        new(-5f, 0.8f, -5f),
        new(-1.7f, 0.8f, -5f),
        new(1.7f, 0.8f, -5f),
        new(5f, 0.8f, -5f),
        new(-5f, 0.8f, 0f),
        new(-1.7f, 0.8f, 0f),
        new(1.7f, 0.8f, 0f),
        new(5f, 0.8f, 0f)
    };

    private readonly Transform[] pickups = new Transform[TargetScore];
    private readonly Transform[] hazards = new Transform[3];
    private readonly Vector3[] hazardOrigins = new Vector3[3];
    private readonly Dictionary<Color, Material> materials = new();
    private Transform player;
    private Transform playerModel;
    private Rigidbody playerPhysics;
    private Renderer playerBodyRenderer;
    private int score;
    private float timeRemaining = RoundDuration;
    private float roundStartedAt;
    private bool roundActive;
    private int roundId;
    private string message = "Collect all 8 energy crystals. Avoid the sentinels!";

    private void Start()
    {
        BuildArena();
        _ = StartRoundAsync(++roundId);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            _ = StartRoundAsync(++roundId);
            return;
        }

        if (!roundActive)
        {
            return;
        }

        MovePlayer();
        MoveHazards();
        AnimatePickups();
        CollectPickups();
        CheckHazardCollisions();

        timeRemaining = Mathf.Max(0f, RoundDuration - (Time.time - roundStartedAt));
        if (timeRemaining <= 0f)
        {
            EndRound(false);
        }
    }

    private void OnGUI()
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 16, 300, 30), $"CRYSTALS  {score}/{TargetScore}");
        GUI.Label(new Rect(20, 42, 300, 30), $"TIME  {Mathf.CeilToInt(timeRemaining)}");
        GUI.Label(new Rect(20, 72, 560, 30), message);

        if (!roundActive)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, 48), message, style);
            GUI.Label(new Rect(0, Screen.height * 0.42f + 52, Screen.width, 30), "A new run starts shortly...", CenteredLabel());
        }

        GUI.Label(new Rect(20, Screen.height - 34, 400, 24), "WASD / Arrow Keys  ·  R to restart");
    }

    private async Awaitable StartRoundAsync(int runId)
    {
        roundActive = false;
        message = "3";
        await Awaitable.WaitForSecondsAsync(1f);
        if (runId != roundId) return;
        message = "2";
        await Awaitable.WaitForSecondsAsync(1f);
        if (runId != roundId) return;
        message = "1";
        await Awaitable.NextFrameAsync();
        if (runId != roundId) return;

        score = 0;
        timeRemaining = RoundDuration;
        roundStartedAt = Time.time;
        player.position = new Vector3(0f, 0.5f, -6f);
        playerPhysics.linearVelocity = Vector3.zero;
        playerModel.localRotation = Quaternion.identity;
        playerBodyRenderer.material.color = Cyan;
        for (var i = 0; i < pickups.Length; i++)
        {
            pickups[i].position = pickupPositions[i];
            pickups[i].gameObject.SetActive(true);
        }

        roundActive = true;
        message = "Collect all 8 energy crystals. Avoid the sentinels!";
    }

    private async void EndRound(bool won)
    {
        if (!roundActive)
        {
            return;
        }

        roundActive = false;
        message = won ? "RIFT STABILIZED!" : "TIME'S UP — RUN AGAIN!";
        var endedRoundId = roundId;
        await Awaitable.WaitForSecondsAsync(2f);
        if (endedRoundId == roundId)
        {
            _ = StartRoundAsync(++roundId);
        }
    }

    private void BuildArena()
    {
        var cameraObject = new GameObject("Showcase Camera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 20f, -8f);
        cameraObject.transform.LookAt(new Vector3(0f, 0f, 0f));
        camera.orthographic = true;
        camera.orthographicSize = 11f;
        camera.backgroundColor = new Color(0.012f, 0.02f, 0.055f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.allowHDR = true;
        cameraObject.AddComponent<AudioListener>();

        var lightObject = new GameObject("Arena Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        var arena = new GameObject("Arena Architecture").transform;
        CreateBlock("Arena foundation", arena, new Vector3(0f, -0.3f, 0f), new Vector3(16f, 0.5f, 16f), FloorColor);
        CreateBlock("North barrier", arena, new Vector3(0f, 0.25f, 8f), new Vector3(16f, 1.1f, 0.5f), TrimColor);
        CreateBlock("South barrier", arena, new Vector3(0f, 0.25f, -8f), new Vector3(16f, 1.1f, 0.5f), TrimColor);
        CreateBlock("East barrier", arena, new Vector3(8f, 0.25f, 0f), new Vector3(0.5f, 1.1f, 16f), TrimColor);
        CreateBlock("West barrier", arena, new Vector3(-8f, 0.25f, 0f), new Vector3(0.5f, 1.1f, 16f), TrimColor);

        for (var offset = -6f; offset <= 6f; offset += 2f)
        {
            CreateBlock("Floor inlay", arena, new Vector3(offset, -0.04f, 0f), new Vector3(0.025f, 0.02f, 14f), TrimColor);
            CreateBlock("Floor inlay", arena, new Vector3(0f, -0.04f, offset), new Vector3(14f, 0.02f, 0.025f), TrimColor);
        }

        for (var x = -1; x <= 1; x += 2)
        {
            for (var z = -1; z <= 1; z += 2)
            {
                var pylon = CreateGroup("Beacon pylon", arena, new Vector3(x * 6.8f, 0.8f, z * 6.8f));
                CreatePart(PrimitiveType.Cylinder, "Pylon", pylon, Vector3.zero, new Vector3(0.45f, 0.8f, 0.45f), TrimColor);
                CreatePart(PrimitiveType.Sphere, "Beacon", pylon, new Vector3(0f, 0.92f, 0f), Vector3.one * 0.28f, Cyan, true);
                var beaconLight = pylon.gameObject.AddComponent<Light>();
                beaconLight.type = LightType.Point;
                beaconLight.color = Cyan;
                beaconLight.intensity = 1.4f;
                beaconLight.range = 3.5f;
            }
        }

        player = CreateGroup("Runner", null, new Vector3(0f, 0.5f, -6f));
        playerPhysics = player.gameObject.AddComponent<Rigidbody>();
        playerPhysics.useGravity = false;
        playerPhysics.interpolation = RigidbodyInterpolation.Interpolate;
        playerPhysics.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
        playerModel = CreateGroup("Runner model", player, Vector3.zero);
        CreateBeveledPart("Torso armor", playerModel, new Vector3(0f, 0.62f, 0f), new Vector3(0.72f, 0.76f, 0.48f), TrimColor, 0.12f);
        CreateBeveledPart("Chest plate", playerModel, new Vector3(0f, 0.79f, -0.26f), new Vector3(0.5f, 0.34f, 0.08f), White, 0.045f);
        CreateBeveledPart("Chest insignia", playerModel, new Vector3(0f, 0.8f, -0.31f), new Vector3(0.19f, 0.14f, 0.035f), Cyan, 0.025f, true);
        CreateBeveledPart("Utility belt", playerModel, new Vector3(0f, 0.27f, 0f), new Vector3(0.7f, 0.15f, 0.46f), Cyan, 0.045f);
        CreateRingPart("Neck seal", playerModel, new Vector3(0f, 1.04f, 0f), 0.23f, 0.045f, TrimColor);
        CreatePart(PrimitiveType.Sphere, "Helmet", playerModel, new Vector3(0f, 1.25f, 0f), new Vector3(0.58f, 0.52f, 0.52f), White);
        CreateBeveledPart("Visor frame", playerModel, new Vector3(0f, 1.27f, -0.24f), new Vector3(0.44f, 0.2f, 0.11f), TrimColor, 0.07f);
        CreateBeveledPart("Visor", playerModel, new Vector3(0f, 1.28f, -0.3f), new Vector3(0.34f, 0.11f, 0.035f), Cyan, 0.04f, true);
        CreateBeveledPart("Life-support pack", playerModel, new Vector3(0f, 0.68f, 0.34f), new Vector3(0.58f, 0.62f, 0.28f), TrimColor, 0.08f);
        CreateBeveledPart("Pack light", playerModel, new Vector3(0f, 0.77f, 0.5f), new Vector3(0.2f, 0.1f, 0.04f), Cyan, 0.025f, true);
        CreatePart(PrimitiveType.Cylinder, "Left oxygen canister", playerModel, new Vector3(-0.2f, 0.69f, 0.52f), new Vector3(0.1f, 0.25f, 0.1f), White);
        CreatePart(PrimitiveType.Cylinder, "Right oxygen canister", playerModel, new Vector3(0.2f, 0.69f, 0.52f), new Vector3(0.1f, 0.25f, 0.1f), White);

        for (var side = -1; side <= 1; side += 2)
        {
            CreateBeveledPart("Shoulder guard", playerModel, new Vector3(side * 0.46f, 0.79f, 0f), new Vector3(0.3f, 0.32f, 0.36f), Cyan, 0.09f);
            CreatePart(PrimitiveType.Capsule, "Arm", playerModel, new Vector3(side * 0.5f, 0.36f, 0f), new Vector3(0.17f, 0.4f, 0.17f), White, false, new Vector3(0f, 0f, side * -10f));
            CreateBeveledPart("Forearm gauntlet", playerModel, new Vector3(side * 0.55f, 0.3f, -0.025f), new Vector3(0.2f, 0.22f, 0.22f), TrimColor, 0.05f);
            CreateRingPart("Wrist seal", playerModel, new Vector3(side * 0.56f, 0.2f, 0f), 0.13f, 0.025f, Cyan, new Vector3(0f, 0f, 90f));
            CreateBeveledPart("Boot", playerModel, new Vector3(side * 0.19f, 0.03f, -0.035f), new Vector3(0.22f, 0.36f, 0.3f), TrimColor, 0.06f);
            CreateBeveledPart("Boot sole", playerModel, new Vector3(side * 0.19f, -0.16f, -0.045f), new Vector3(0.24f, 0.07f, 0.32f), Cyan, 0.025f);
            CreateRingPart("Helmet comms", playerModel, new Vector3(side * 0.3f, 1.25f, 0f), 0.11f, 0.045f, TrimColor, new Vector3(0f, 0f, 90f));
            CreatePart(PrimitiveType.Sphere, "Comms beacon", playerModel, new Vector3(side * 0.37f, 1.26f, -0.015f), Vector3.one * 0.09f, Cyan, true);
        }
        playerBodyRenderer = playerModel.Find("Torso armor").GetComponent<Renderer>();

        for (var i = 0; i < pickups.Length; i++)
        {
            pickups[i] = CreateGroup($"Energy crystal {i + 1}", null, pickupPositions[i]);
            CreateCrystal(pickups[i], Gold);
            CreatePart(PrimitiveType.Sphere, "Crystal core", pickups[i], Vector3.zero, Vector3.one * 0.26f, new Color(1f, 0.82f, 0.25f), true);
        }

        for (var i = 0; i < hazards.Length; i++)
        {
            hazardOrigins[i] = new Vector3(-4.5f + i * 4.5f, 0.65f, 2f);
            hazards[i] = CreateGroup($"Sentinel {i + 1}", null, hazardOrigins[i]);
            BuildSentinel(hazards[i]);
        }
    }

    private void BuildSentinel(Transform sentinel)
    {
        CreateBeveledPart("Sentinel hull", sentinel, Vector3.zero, new Vector3(0.9f, 0.56f, 0.72f), TrimColor, 0.14f);
        CreateRingPart("Optic housing", sentinel, new Vector3(0f, 0.02f, -0.37f), 0.23f, 0.07f, Red, new Vector3(90f, 0f, 0f));
        CreatePart(PrimitiveType.Sphere, "Core lens", sentinel, new Vector3(0f, 0.02f, -0.415f), new Vector3(0.32f, 0.24f, 0.08f), Red, true);
        CreateBeveledPart("Top armor", sentinel, new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.16f, 0.48f), Red, 0.045f);
        CreateBeveledPart("Rear engine block", sentinel, new Vector3(0f, 0f, 0.42f), new Vector3(0.48f, 0.35f, 0.22f), TrimColor, 0.06f);

        for (var side = -1; side <= 1; side += 2)
        {
            CreateBeveledPart("Armored wing", sentinel, new Vector3(side * 0.64f, 0f, 0.04f), new Vector3(0.78f, 0.16f, 0.34f), TrimColor, 0.05f, false, new Vector3(0f, side * -12f, side * -8f));
            CreatePart(PrimitiveType.Sphere, "Wing light", sentinel, new Vector3(side * 0.96f, 0f, -0.02f), Vector3.one * 0.14f, Red, true);
            CreateRingPart("Engine nozzle", sentinel, new Vector3(side * 0.64f, 0f, 0.24f), 0.12f, 0.035f, Cyan, new Vector3(90f, 0f, 0f));
            CreatePart(PrimitiveType.Cylinder, "Engine thruster", sentinel, new Vector3(side * 0.64f, 0f, 0.3f), new Vector3(0.11f, 0.13f, 0.11f), Cyan, true, new Vector3(90f, 0f, 0f));
        }

        CreatePart(PrimitiveType.Cylinder, "Sensor mast", sentinel, new Vector3(0f, 0.43f, 0.1f), new Vector3(0.04f, 0.16f, 0.04f), White);
        CreatePart(PrimitiveType.Sphere, "Sensor beacon", sentinel, new Vector3(0f, 0.62f, 0.1f), Vector3.one * 0.11f, Red, true);
    }

    private void MovePlayer()
    {
        var horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        var vertical = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        var direction = new Vector3(horizontal, 0f, vertical).normalized;
        var velocity = direction * 6f;
        if ((player.position.x <= -ArenaHalfSize && velocity.x < 0f)
            || (player.position.x >= ArenaHalfSize && velocity.x > 0f))
        {
            velocity.x = 0f;
        }

        if ((player.position.z <= -ArenaHalfSize && velocity.z < 0f)
            || (player.position.z >= ArenaHalfSize && velocity.z > 0f))
        {
            velocity.z = 0f;
        }

        playerPhysics.position = new Vector3(
            Mathf.Clamp(player.position.x, -ArenaHalfSize, ArenaHalfSize),
            player.position.y,
            Mathf.Clamp(player.position.z, -ArenaHalfSize, ArenaHalfSize));
        playerPhysics.linearVelocity = velocity;
        playerModel.localRotation = Quaternion.Euler(vertical * 8f, 0f, -horizontal * 10f);
    }

    private void MoveHazards()
    {
        for (var i = 0; i < hazards.Length; i++)
        {
            var phase = Time.time * (0.8f + i * 0.17f) + i * 2f;
            hazards[i].position = hazardOrigins[i] + new Vector3(Mathf.Sin(phase) * 2.5f, Mathf.Sin(phase * 2f) * 0.12f, Mathf.Cos(phase * 0.7f) * 1.7f);
            hazards[i].rotation = Quaternion.Euler(0f, Mathf.Sin(phase) * 20f, 0f);
        }
    }

    private void AnimatePickups()
    {
        for (var i = 0; i < pickups.Length; i++)
        {
            if (!pickups[i].gameObject.activeSelf)
            {
                continue;
            }

            var phase = Time.time * 2.3f + i;
            pickups[i].position = pickupPositions[i] + Vector3.up * (Mathf.Sin(phase) * 0.12f);
            pickups[i].rotation = Quaternion.Euler(0f, Time.time * 75f + i * 24f, Mathf.Sin(phase) * 8f);
        }
    }

    private void CollectPickups()
    {
        for (var i = 0; i < pickups.Length; i++)
        {
            if (pickups[i].gameObject.activeSelf && Vector3.Distance(player.position, pickups[i].position) < 0.95f)
            {
                pickups[i].gameObject.SetActive(false);
                score++;
                if (score >= TargetScore)
                {
                    EndRound(true);
                    return;
                }
            }
        }
    }

    private void CheckHazardCollisions()
    {
        foreach (var hazard in hazards)
        {
            if (Vector3.Distance(player.position, hazard.position) < 1.25f)
            {
                playerBodyRenderer.material.color = Red;
                message = "Careful — the sentinels are closing in!";
                return;
            }
        }

        playerBodyRenderer.material.color = Cyan;
    }

    private Transform CreateGroup(string objectName, Transform parent, Vector3 position)
    {
        var group = new GameObject(objectName).transform;
        group.SetParent(parent, false);
        if (parent == null)
        {
            group.position = position;
        }
        else
        {
            group.localPosition = position;
        }

        return group;
    }

    private void CreatePart(
        PrimitiveType type,
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Color color,
        bool emissive = false,
        Vector3 localEulerAngles = default)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localEulerAngles = localEulerAngles;
        part.GetComponent<Renderer>().sharedMaterial = GetMaterial(color, emissive);
        var collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
    }

    private void CreateBeveledPart(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Vector3 size,
        Color color,
        float bevel,
        bool emissive = false,
        Vector3 localEulerAngles = default)
    {
        var part = new GameObject(objectName);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localEulerAngles = localEulerAngles;
        var filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateBeveledBoxMesh(size, bevel);
        part.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color, emissive);
    }

    private void CreateRingPart(
        string objectName,
        Transform parent,
        Vector3 localPosition,
        float radius,
        float thickness,
        Color color,
        Vector3 localEulerAngles = default)
    {
        var ring = new GameObject(objectName);
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = localPosition;
        ring.transform.localEulerAngles = localEulerAngles;
        var filter = ring.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateRingMesh(radius, thickness);
        ring.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color, color == Cyan || color == Red);
    }

    private static Mesh CreateBeveledBoxMesh(Vector3 size, float bevel)
    {
        var half = size * 0.5f;
        var cut = Mathf.Clamp(bevel, 0.001f, Mathf.Min(size.x, size.y) * 0.45f);
        var outline = new[]
        {
            new Vector2(-half.x + cut, -half.y),
            new Vector2(half.x - cut, -half.y),
            new Vector2(half.x, -half.y + cut),
            new Vector2(half.x, half.y - cut),
            new Vector2(half.x - cut, half.y),
            new Vector2(-half.x + cut, half.y),
            new Vector2(-half.x, half.y - cut),
            new Vector2(-half.x, -half.y + cut)
        };
        var vertices = new List<Vector3>(50);
        var triangles = new List<int>(84);

        AddFlatFace(vertices, triangles, outline, half.z, false);
        AddFlatFace(vertices, triangles, outline, -half.z, true);

        for (var i = 0; i < outline.Length; i++)
        {
            var next = (i + 1) % outline.Length;
            var start = vertices.Count;
            vertices.Add(new Vector3(outline[i].x, outline[i].y, half.z));
            vertices.Add(new Vector3(outline[i].x, outline[i].y, -half.z));
            vertices.Add(new Vector3(outline[next].x, outline[next].y, -half.z));
            vertices.Add(new Vector3(outline[next].x, outline[next].y, half.z));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        var mesh = new Mesh { name = "Beveled armor mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddFlatFace(List<Vector3> vertices, List<int> triangles, Vector2[] outline, float z, bool reverse)
    {
        var center = Vector3.zero;
        center.z = z;
        var first = vertices.Count;
        vertices.Add(center);
        for (var i = 0; i < outline.Length; i++)
        {
            vertices.Add(new Vector3(outline[i].x, outline[i].y, z));
        }

        for (var i = 0; i < outline.Length; i++)
        {
            var current = first + 1 + i;
            var next = first + 1 + (i + 1) % outline.Length;
            if (reverse)
            {
                triangles.Add(first);
                triangles.Add(next);
                triangles.Add(current);
            }
            else
            {
                triangles.Add(first);
                triangles.Add(current);
                triangles.Add(next);
            }
        }
    }

    private static Mesh CreateRingMesh(float radius, float thickness)
    {
        const int radialSegments = 12;
        const int tubeSegments = 6;
        var vertices = new Vector3[radialSegments * tubeSegments];
        var triangles = new int[radialSegments * tubeSegments * 6];
        for (var radial = 0; radial < radialSegments; radial++)
        {
            var around = radial * Mathf.PI * 2f / radialSegments;
            for (var tube = 0; tube < tubeSegments; tube++)
            {
                var section = tube * Mathf.PI * 2f / tubeSegments;
                var ringRadius = radius + Mathf.Cos(section) * thickness;
                vertices[radial * tubeSegments + tube] = new Vector3(
                    Mathf.Cos(around) * ringRadius,
                    Mathf.Sin(section) * thickness,
                    Mathf.Sin(around) * ringRadius);

                var nextRadial = (radial + 1) % radialSegments;
                var nextTube = (tube + 1) % tubeSegments;
                var triangle = (radial * tubeSegments + tube) * 6;
                triangles[triangle] = radial * tubeSegments + tube;
                triangles[triangle + 1] = radial * tubeSegments + nextTube;
                triangles[triangle + 2] = nextRadial * tubeSegments + tube;
                triangles[triangle + 3] = radial * tubeSegments + nextTube;
                triangles[triangle + 4] = nextRadial * tubeSegments + nextTube;
                triangles[triangle + 5] = nextRadial * tubeSegments + tube;
            }
        }

        var mesh = new Mesh { name = "Segmented ring mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void CreateBlock(string objectName, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        CreatePart(PrimitiveType.Cube, objectName, parent, position, scale, color);
    }

    private void CreateCrystal(Transform parent, Color color)
    {
        var crystal = new GameObject("Faceted crystal");
        crystal.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "Energy crystal mesh" };
        mesh.vertices = new[]
        {
            new Vector3(0f, 0.62f, 0f),
            new Vector3(0.34f, 0.12f, 0f),
            new Vector3(0f, 0.12f, 0.34f),
            new Vector3(-0.34f, 0.12f, 0f),
            new Vector3(0f, 0.12f, -0.34f),
            new Vector3(0f, -0.58f, 0f)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4,
            5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1
        };
        mesh.RecalculateNormals();
        var filter = crystal.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        crystal.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color, true);
    }

    private Material GetMaterial(Color color, bool emissive)
    {
        if (materials.TryGetValue(color, out var cachedMaterial))
        {
            return cachedMaterial;
        }

        var shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        var material = new Material(shader)
        {
            color = color
        };
        if (emissive)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.8f);
        }

        materials.Add(color, material);
        return material;
    }

    private static GUIStyle CenteredLabel()
    {
        return new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
    }
}
