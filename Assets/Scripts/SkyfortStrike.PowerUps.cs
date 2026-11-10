using System;
using System.Collections.Generic;
using UnityEngine;
using static SkyfortModels;
using Random = UnityEngine.Random;

// Hull damage, power-ups and per-level atmosphere. Kept apart from the main flight/level code in SkyfortStrike.cs.
public sealed partial class SkyfortStrike
{
    private const int HullMax = 4;
    private const int MaxShips = 9;
    private const float HitInvulnerability = 1.4f;
    private const float ShieldDuration = 10f;
    private const float SpreadDuration = 15f;
    private const float PickupReachSide = 2.2f;
    private const float PickupReachHeight = 3f;
    private const float PickupReachDepth = 2.6f;
    private const float PickupMagnetRadius = 5f;
    private const float PickupMagnetSpeed = 9f;

    private enum PickupKind { Repair, Fuel, Shield, Spread, ExtraShip }

    private sealed class Pickup
    {
        public PickupKind Kind;
        public Transform Transform;
        public Vector3 Home;
        public float Phase;
        public Chunk Chunk;
    }

    [Header("Power-ups")]
    [SerializeField] private GameObject repairPickupPrefab;
    [SerializeField] private GameObject fuelPickupPrefab;
    [SerializeField] private GameObject shieldPickupPrefab;
    [SerializeField] private GameObject spreadPickupPrefab;
    [SerializeField] private GameObject extraShipPickupPrefab;
    [Tooltip("Bubble around the fighter shown while the Shield power-up is active.")]
    [SerializeField] private Renderer shieldBubble;

    [Header("Atmosphere")]
    [SerializeField] private Light keyLight;

    private readonly List<Pickup> pickups = new();
    private int hull = HullMax;
    private float shieldUntil;
    private float spreadUntil;
    private float nextDamageSmoke;
    private int shownShield = -1;
    private int shownSpread = -1;
    private int toastId;
    private Material shieldMaterial;

    private bool ShieldActive => Time.time < shieldUntil;
    private bool SpreadActive => Time.time < spreadUntil;

    private void CheckPowerUpReferences(Action<UnityEngine.Object, string> check)
    {
        check(repairPickupPrefab, nameof(repairPickupPrefab));
        check(fuelPickupPrefab, nameof(fuelPickupPrefab));
        check(shieldPickupPrefab, nameof(shieldPickupPrefab));
        check(spreadPickupPrefab, nameof(spreadPickupPrefab));
        check(extraShipPickupPrefab, nameof(extraShipPickupPrefab));
        check(shieldBubble, nameof(shieldBubble));
        check(keyLight, nameof(keyLight));
    }

    // ---------------------------------------------------------------- Damage

    // An enemy hit costs one hull point (or nothing while shielded); only an empty hull costs a ship.
    private void TakeHit(Vector3 point)
    {
        if (ShieldActive)
        {
            Explode(point, Cyan, 16, false);
            sfx.PlayOneShot(sounds.BossHit, 0.7f);
            return;
        }

        hull--;
        Explode(point, Red, 24, false);
        sfx.PlayOneShot(sounds.Hit, 0.9f);
        bounce += new Vector3(Random.Range(-3f, 3f), 3f, 0f);
        if (hull <= 0)
        {
            CrashAsync("SHOT DOWN");
            return;
        }

        invulnerableUntil = Time.time + HitInvulnerability;
        ShowToast(hull == 1 ? "HULL CRITICAL!" : "HULL DAMAGED", Red);
    }

    private void ResetPowerUps()
    {
        hull = HullMax;
        shieldUntil = 0f;
        spreadUntil = 0f;
        shownShield = shownSpread = -1;
        if (shieldBubble != null)
        {
            shieldBubble.gameObject.SetActive(false);
        }
    }

    // ---------------------------------------------------------------- Firing

    private void FireLasers()
    {
        var interval = SpreadActive ? FireInterval * 0.75f : FireInterval;
        nextFire = Time.time + interval;
        var fired = FireLaser(0f);
        if (SpreadActive)
        {
            fired |= FireLaser(-7f);
            fired |= FireLaser(7f);
        }

        if (fired)
        {
            sfx.PlayOneShot(sounds.Laser, 0.3f);
        }
    }

    private bool FireLaser(float yaw)
    {
        var laser = Take(lasers);
        if (laser == null) return false;
        var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        laser.Transform.position = ShipPosition + new Vector3(yaw * 0.04f, 0f, 1.2f);
        laser.Transform.rotation = Quaternion.LookRotation(direction);
        laser.Velocity = direction * LaserSpeed + Vector3.forward * speed;
        laser.Life = LaserRange / LaserSpeed;
        return true;
    }

    // ---------------------------------------------------------------- Pickups

    private PickupKind RandomPickupKind()
    {
        // Repairs get likelier the more damaged the hull is; an extra ship is rare.
        var repair = hull < HullMax ? 30f + (HullMax - hull) * 12f : 12f;
        var fuelWeight = fuel < FuelMax * 0.5f ? 34f : 22f;
        const float shield = 20f;
        const float spread = 22f;
        const float extraShip = 5f;
        var roll = Random.value * (repair + fuelWeight + shield + spread + extraShip);
        if ((roll -= repair) < 0f) return PickupKind.Repair;
        if ((roll -= fuelWeight) < 0f) return PickupKind.Fuel;
        if ((roll -= shield) < 0f) return PickupKind.Shield;
        if ((roll -= spread) < 0f) return PickupKind.Spread;
        return PickupKind.ExtraShip;
    }

    private void SpawnPickup(Chunk chunk, PickupKind kind, Vector3 position)
    {
        var prefab = kind switch
        {
            PickupKind.Repair => repairPickupPrefab,
            PickupKind.Fuel => fuelPickupPrefab,
            PickupKind.Shield => shieldPickupPrefab,
            PickupKind.Spread => spreadPickupPrefab,
            _ => extraShipPickupPrefab
        };
        position.y = Mathf.Clamp(position.y, MinAltitude + 0.5f, MaxAltitude - 1f);
        var instance = Instantiate(prefab, position, Quaternion.identity, chunk.Root);
        // A glowing pole and a ring on the deck show how high the pickup floats.
        var markerColor = kind switch
        {
            PickupKind.Repair => Lime,
            PickupKind.Fuel => Gold,
            PickupKind.Shield => Cyan,
            PickupKind.Spread => NeonGreen,
            _ => Bone
        };
        var height = position.y;
        Part(PrimitiveType.Cube, "Altitude marker", instance.transform, new Vector3(0f, -height * 0.5f, 0f), new Vector3(0.04f, height, 0.04f), markerColor, true);
        Ring("Ground ring", instance.transform, new Vector3(0f, -height + 0.05f, 0f), 0.6f, 0.04f, markerColor);
        pickups.Add(new Pickup { Kind = kind, Transform = instance.transform, Home = position, Phase = Random.Range(0f, 6f), Chunk = chunk });
    }

    // Destroyed targets sometimes leave a power-up behind (radar dishes always do).
    private void MaybeDropPickup(Target target)
    {
        var chance = target.Kind switch
        {
            TargetKind.Radar => 1f,
            TargetKind.Drone => 0.2f,
            TargetKind.Turret => 0.15f,
            _ => 0f
        };
        if (target.Chunk != null && chunks.Contains(target.Chunk) && Random.value < chance)
        {
            SpawnPickup(target.Chunk, RandomPickupKind(), target.Center + Vector3.up * 1.2f);
        }
    }

    private void UpdatePickups()
    {
        var shipPosition = ShipPosition;
        for (var i = pickups.Count - 1; i >= 0; i--)
        {
            var pickup = pickups[i];
            pickup.Phase += Time.deltaTime;
            var position = pickup.Home + Vector3.up * (Mathf.Sin(pickup.Phase * 2.4f) * 0.25f);
            pickup.Transform.position = position;
            pickup.Transform.rotation = Quaternion.Euler(0f, pickup.Phase * 110f, 0f);
            // Height is hard to judge in the isometric view, so the capture zone is a generous ellipsoid (taller than
            // it is wide), and a pickup drifts toward the fighter once it is close.
            var delta = position - shipPosition;
            if (delta.sqrMagnitude < PickupMagnetRadius * PickupMagnetRadius)
            {
                pickup.Home = Vector3.MoveTowards(pickup.Home, shipPosition, PickupMagnetSpeed * Time.deltaTime);
                delta = pickup.Home - shipPosition;
            }

            var nx = delta.x / PickupReachSide;
            var ny = delta.y / PickupReachHeight;
            var nz = delta.z / PickupReachDepth;
            if (nx * nx + ny * ny + nz * nz < 1f)
            {
                pickups.RemoveAt(i);
                Destroy(pickup.Transform.gameObject);
                Collect(pickup.Kind, position);
            }
        }
    }

    private void Collect(PickupKind kind, Vector3 position)
    {
        sfx.PlayOneShot(sounds.PowerUp, 0.9f);
        AddScore(100);
        switch (kind)
        {
            case PickupKind.Repair:
                hull = Mathf.Min(HullMax, hull + 2);
                Explode(position, Lime, 24, false);
                ShowToast("HULL REPAIRED", Lime);
                break;
            case PickupKind.Fuel:
                fuel = Mathf.Min(FuelMax, fuel + 40f);
                Explode(position, Gold, 24, false);
                ShowToast("FUEL +40", Gold);
                break;
            case PickupKind.Shield:
                shieldUntil = Time.time + ShieldDuration;
                Explode(position, Cyan, 24, false);
                ShowToast("SHIELD UP", Cyan);
                break;
            case PickupKind.Spread:
                spreadUntil = Time.time + SpreadDuration;
                Explode(position, NeonGreen, 24, false);
                ShowToast("SPREAD SHOT", NeonGreen);
                break;
            case PickupKind.ExtraShip:
                ships = Mathf.Min(MaxShips, ships + 1);
                Explode(position, Bone, 30, false);
                ShowToast("EXTRA SHIP!", Bone);
                break;
        }
    }

    // ---------------------------------------------------------------- Per-frame status

    private void UpdatePowerUps()
    {
        var shielded = ShieldActive;
        if (shieldBubble.gameObject.activeSelf != shielded)
        {
            shieldBubble.gameObject.SetActive(shielded);
        }

        if (shielded)
        {
            shieldMaterial ??= shieldBubble.material;
            var remaining = shieldUntil - Time.time;
            // Flicker during the last two seconds as a warning.
            var alpha = remaining < 2f && Mathf.Repeat(Time.time * 8f, 1f) < 0.4f ? 0.08f : 0.22f + Mathf.Sin(Time.time * 6f) * 0.05f;
            shieldMaterial.color = new Color(0.3f, 0.85f, 1f, alpha);
        }

        // A badly damaged hull trails smoke and sparks.
        if (hull <= 2 && Time.time >= nextDamageSmoke)
        {
            nextDamageSmoke = Time.time + (hull == 1 ? 0.12f : 0.3f);
            var emit = new ParticleSystem.EmitParams
            {
                position = ShipPosition + new Vector3(Random.Range(-0.4f, 0.4f), 0.1f, -0.8f),
                applyShapeToPosition = true,
                startColor = hull == 1 ? new Color(1f, 0.35f, 0.1f) : new Color(0.35f, 0.35f, 0.4f)
            };
            explosionFx.Emit(emit, 2);
        }

        var shield = shielded ? Mathf.CeilToInt(shieldUntil - Time.time) : 0;
        var spread = SpreadActive ? Mathf.CeilToInt(spreadUntil - Time.time) : 0;
        if (shield != shownShield || spread != shownSpread)
        {
            shownShield = shield;
            shownSpread = spread;
            var parts = new List<string>(2);
            if (shield > 0) parts.Add($"SHIELD {shield}");
            if (spread > 0) parts.Add($"SPREAD {spread}");
            hud.PowerStatus = string.Join("    ", parts);
        }
    }

    private async void ShowToast(string text, Color color)
    {
        var id = ++toastId;
        hud.Toast = text;
        hud.ToastColor = color;
        try
        {
            await Awaitable.WaitForSecondsAsync(1.6f, destroyCancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (id == toastId)
        {
            hud.Toast = "";
        }
    }

    // ---------------------------------------------------------------- Atmosphere

    // Sky color, sunlight and ambient ease toward the theme of the section the ship is flying through.
    private void UpdateAtmosphere(bool instant)
    {
        var theme = CurrentChunk()?.Theme ?? lastTheme;
        var blend = instant ? 1f : 1f - Mathf.Exp(-1.5f * Time.deltaTime);
        view.backgroundColor = Color.Lerp(view.backgroundColor, theme.Background, blend);
        keyLight.color = Color.Lerp(keyLight.color, theme.SunColor, blend);
        keyLight.intensity = Mathf.Lerp(keyLight.intensity, theme.SunIntensity, blend);
        RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, theme.Ambient, blend);
    }
}
