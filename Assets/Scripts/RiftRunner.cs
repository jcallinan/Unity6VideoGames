using UnityEngine;

public sealed class RiftRunner : MonoBehaviour
{
    private const float ArenaHalfSize = 7.5f;
    private const int TargetScore = 8;

    private readonly Vector3[] pickupPositions =
    {
        new(-5f, 0.25f, -5f),
        new(-1.7f, 0.25f, -5f),
        new(1.7f, 0.25f, -5f),
        new(5f, 0.25f, -5f),
        new(-5f, 0.25f, 0f),
        new(-1.7f, 0.25f, 0f),
        new(1.7f, 0.25f, 0f),
        new(5f, 0.25f, 0f)
    };

    private readonly Transform[] pickups = new Transform[TargetScore];
    private readonly Transform[] hazards = new Transform[3];
    private readonly Vector3[] hazardOrigins = new Vector3[3];
    private Transform player;
    private Material playerMaterial;
    private int score;
    private float timeRemaining = 60f;
    private float roundStartedAt;
    private bool roundActive;
    private int roundId;
    private string message = "Collect all 8 energy orbs. Avoid the sentinels!";

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
        CollectPickups();
        CheckHazardCollisions();

        timeRemaining = Mathf.Max(0f, 60f - (Time.time - roundStartedAt));
        if (timeRemaining <= 0f)
        {
            EndRound(false);
        }
    }

    private void OnGUI()
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 16, 300, 30), $"ORBS  {score}/{TargetScore}");
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
        timeRemaining = 60f;
        roundStartedAt = Time.time;
        player.position = new Vector3(0f, 0.55f, -6f);
        playerMaterial.color = new Color(0.18f, 0.85f, 1f);
        for (var i = 0; i < pickups.Length; i++)
        {
            pickups[i].gameObject.SetActive(true);
        }

        roundActive = true;
        message = "Collect all 8 energy orbs. Avoid the sentinels!";
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
        camera.backgroundColor = new Color(0.025f, 0.035f, 0.09f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.AddComponent<AudioListener>();

        var lightObject = new GameObject("Arena Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        CreateBlock("Arena", new Vector3(0f, -0.3f, 0f), new Vector3(16f, 0.5f, 16f), new Color(0.075f, 0.095f, 0.18f));
        CreateBlock("North wall", new Vector3(0f, 0.3f, 8f), new Vector3(16f, 1.2f, 0.5f), new Color(0.12f, 0.22f, 0.4f));
        CreateBlock("South wall", new Vector3(0f, 0.3f, -8f), new Vector3(16f, 1.2f, 0.5f), new Color(0.12f, 0.22f, 0.4f));
        CreateBlock("East wall", new Vector3(8f, 0.3f, 0f), new Vector3(0.5f, 1.2f, 16f), new Color(0.12f, 0.22f, 0.4f));
        CreateBlock("West wall", new Vector3(-8f, 0.3f, 0f), new Vector3(0.5f, 1.2f, 16f), new Color(0.12f, 0.22f, 0.4f));

        player = CreateSphere("Runner", new Vector3(0f, 0.55f, -6f), 0.8f, new Color(0.18f, 0.85f, 1f));
        playerMaterial = player.GetComponent<Renderer>().material;

        for (var i = 0; i < pickups.Length; i++)
        {
            pickups[i] = CreateSphere($"Energy orb {i + 1}", pickupPositions[i], 0.38f, new Color(1f, 0.72f, 0.15f));
        }

        for (var i = 0; i < hazards.Length; i++)
        {
            hazardOrigins[i] = new Vector3(-4.5f + i * 4.5f, 0.55f, 2f);
            hazards[i] = CreateSphere($"Sentinel {i + 1}", hazardOrigins[i], 0.95f, new Color(1f, 0.18f, 0.28f));
        }
    }

    private void MovePlayer()
    {
        var horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        var vertical = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        var direction = new Vector3(horizontal, 0f, vertical).normalized;
        player.position += direction * (6f * Time.deltaTime);
        player.position = new Vector3(
            Mathf.Clamp(player.position.x, -ArenaHalfSize, ArenaHalfSize),
            player.position.y,
            Mathf.Clamp(player.position.z, -ArenaHalfSize, ArenaHalfSize));
    }

    private void MoveHazards()
    {
        for (var i = 0; i < hazards.Length; i++)
        {
            var phase = Time.time * (0.8f + i * 0.17f) + i * 2f;
            hazards[i].position = hazardOrigins[i] + new Vector3(Mathf.Sin(phase) * 2.5f, 0f, Mathf.Cos(phase * 0.7f) * 1.7f);
        }
    }

    private void CollectPickups()
    {
        for (var i = 0; i < pickups.Length; i++)
        {
            if (pickups[i].gameObject.activeSelf && Vector3.Distance(player.position, pickups[i].position) < 0.8f)
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
            if (Vector3.Distance(player.position, hazard.position) < 1.1f)
            {
                playerMaterial.color = new Color(1f, 0.18f, 0.28f);
                message = "Careful — the sentinels are closing in!";
                return;
            }
        }

        playerMaterial.color = new Color(0.18f, 0.85f, 1f);
    }

    private static Transform CreateSphere(string objectName, Vector3 position, float scale, Color color)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = objectName;
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * scale;
        sphere.GetComponent<Renderer>().material = CreateMaterial(color);
        return sphere.transform;
    }

    private static void CreateBlock(string objectName, Vector3 position, Vector3 scale, Color color)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = objectName;
        block.transform.position = position;
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().material = CreateMaterial(color);
    }

    private static Material CreateMaterial(Color color)
    {
        var shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        var material = new Material(shader);
        material.color = color;
        return material;
    }

    private static GUIStyle CenteredLabel()
    {
        return new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
    }
}
