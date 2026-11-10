using UnityEngine;

// Look-and-feel for one level. The game cycles through the themes: Skyfort, Dune Ruins, Ice Citadel, Violet Bastion, Azure Causeway, Brick Bulwark, Refinery, then repeats
// faster. Each theme recolors the corridor, the obstacles, the sky and the light, and SkyfortStrike gives each its
// own obstacle mix and mid-level zone.
public enum SkyfortLandmark { Pyramid, Spire, Tank, Bastion }

public sealed class SkyfortTheme
{
    public string Name;
    public string MidZoneName;
    public string Tagline;

    public Color Background;
    public Color SunColor;
    public float SunIntensity;
    public Color Ambient;

    public Color Deck;
    public Color DeckLine;
    public Color Rampart;
    public Color RampartGlow;

    public Color StoneMain;
    public Color StoneBase;
    public Color StoneGlow;
    public Color CastleStone;
    public Color CastleTrim;
    public Color Roof;
    public Color WindowGlow;

    public Color MidFloor;
    public Color MidSpeck;
    public bool MidSpecksGlow;

    // Neon accents: Neon traces the deck grid, edges and the ship; NeonAlt is the contrasting second color.
    public Color Neon;
    public Color NeonAlt;

    // Shape of the big landmark obstacle, whether castle walls get brick courses, and the extra space-zone dressing.
    public SkyfortLandmark Landmark = SkyfortLandmark.Pyramid;
    public bool Bricks;
    public Color[] StarColors;
    public Color PlanetColor = new Color(0.2f, 0.3f, 0.9f);
    public int Planets;

    public Color ArenaDeck => Color.Lerp(Deck, Color.black, 0.4f);
}

public static class SkyfortThemes
{
    public static readonly SkyfortTheme Skyfort = new()
    {
        Name = "SKYFORT",
        MidZoneName = "OPEN SPACE",
        Tagline = "Storm the floating fortress",
        // Early-80s arcade palette: black space, saturated royal-blue fortress, cyan grid, lavender-grey stone.
        Background = new Color(0f, 0f, 0.02f),
        SunColor = Color.white,
        SunIntensity = 1.2f,
        Ambient = new Color(0.22f, 0.24f, 0.4f),
        Deck = new Color(0.08f, 0.12f, 0.62f),
        DeckLine = new Color(0.25f, 0.5f, 1f),
        Rampart = new Color(0.15f, 0.25f, 0.85f),
        RampartGlow = new Color(0.3f, 0.9f, 1f),
        StoneMain = new Color(0.72f, 0.6f, 0.4f),
        StoneBase = new Color(0.5f, 0.38f, 0.24f),
        StoneGlow = new Color(1f, 0.85f, 0.15f),
        CastleStone = new Color(0.6f, 0.62f, 0.85f),
        CastleTrim = new Color(0.38f, 0.4f, 0.7f),
        Roof = new Color(0.85f, 0.15f, 0.2f),
        WindowGlow = new Color(1f, 0.85f, 0.15f),
        MidFloor = new Color(0.015f, 0.02f, 0.06f),
        MidSpeck = new Color(0.5f, 0.7f, 1f),
        MidSpecksGlow = true,
        Neon = new Color(0.2f, 0.95f, 1f),
        NeonAlt = new Color(0.2f, 1f, 0.45f)
    };

    public static readonly SkyfortTheme DuneRuins = new()
    {
        Name = "DUNE RUINS",
        MidZoneName = "SANDSTORM FLATS",
        Tagline = "Obelisks and tombs under a burning sky",
        Background = new Color(0.17f, 0.08f, 0.05f),
        SunColor = new Color(1f, 0.82f, 0.6f),
        SunIntensity = 1.3f,
        Ambient = new Color(0.32f, 0.23f, 0.16f),
        Deck = new Color(0.44f, 0.33f, 0.19f),
        DeckLine = new Color(0.53f, 0.4f, 0.24f),
        Rampart = new Color(0.5f, 0.37f, 0.22f),
        RampartGlow = new Color(1f, 0.62f, 0.12f),
        StoneMain = new Color(0.76f, 0.6f, 0.37f),
        StoneBase = new Color(0.52f, 0.39f, 0.24f),
        StoneGlow = new Color(0.12f, 0.82f, 1f),
        CastleStone = new Color(0.62f, 0.49f, 0.31f),
        CastleTrim = new Color(0.46f, 0.35f, 0.21f),
        Roof = new Color(0.18f, 0.44f, 0.5f),
        WindowGlow = new Color(0.12f, 0.82f, 1f),
        MidFloor = new Color(0.36f, 0.26f, 0.15f),
        MidSpeck = new Color(0.25f, 0.17f, 0.1f),
        MidSpecksGlow = false,
        Neon = new Color(0.15f, 0.9f, 1f),
        NeonAlt = new Color(0.55f, 1f, 0.2f)
    };

    public static readonly SkyfortTheme IceCitadel = new()
    {
        Name = "ICE CITADEL",
        MidZoneName = "AURORA FIELD",
        Tagline = "Crystal spires on a frozen night",
        Background = new Color(0.02f, 0.04f, 0.1f),
        SunColor = new Color(0.75f, 0.86f, 1f),
        SunIntensity = 1.1f,
        Ambient = new Color(0.22f, 0.28f, 0.42f),
        Deck = new Color(0.5f, 0.62f, 0.75f),
        DeckLine = new Color(0.68f, 0.84f, 0.95f),
        Rampart = new Color(0.38f, 0.5f, 0.66f),
        RampartGlow = new Color(0.4f, 0.95f, 1f),
        StoneMain = new Color(0.62f, 0.83f, 0.96f),
        StoneBase = new Color(0.42f, 0.58f, 0.74f),
        StoneGlow = new Color(0.45f, 1f, 0.9f),
        CastleStone = new Color(0.72f, 0.8f, 0.9f),
        CastleTrim = new Color(0.5f, 0.6f, 0.74f),
        Roof = new Color(0.22f, 0.32f, 0.62f),
        WindowGlow = new Color(0.4f, 0.95f, 1f),
        MidFloor = new Color(0.04f, 0.07f, 0.14f),
        MidSpeck = new Color(0.4f, 1f, 0.7f),
        MidSpecksGlow = true,
        Neon = new Color(0.4f, 1f, 0.9f),
        NeonAlt = new Color(0.25f, 1f, 0.6f)
    };

    // Violet floor striped like a 2600 cart, orange crystal pyramids, a galaxy of tiny blue stars.
    public static readonly SkyfortTheme VioletBastion = new()
    {
        Name = "VIOLET BASTION",
        MidZoneName = "GALAXY DRIFT",
        Tagline = "Striped violet decks over a starless gulf",
        Background = new Color(0f, 0f, 0f),
        SunColor = new Color(0.9f, 0.8f, 1f),
        SunIntensity = 1.2f,
        Ambient = new Color(0.3f, 0.2f, 0.45f),
        Deck = new Color(0.26f, 0.12f, 0.62f),
        DeckLine = new Color(0.5f, 0.3f, 0.95f),
        Rampart = new Color(0.2f, 0.2f, 0.75f),
        RampartGlow = new Color(0.55f, 0.45f, 1f),
        StoneMain = new Color(0.95f, 0.6f, 0.15f),
        StoneBase = new Color(0.6f, 0.32f, 0.1f),
        StoneGlow = new Color(1f, 0.9f, 0.3f),
        CastleStone = new Color(0.4f, 0.25f, 0.75f),
        CastleTrim = new Color(0.28f, 0.17f, 0.55f),
        Roof = new Color(0.95f, 0.5f, 0.12f),
        WindowGlow = new Color(1f, 0.85f, 0.3f),
        MidFloor = new Color(0f, 0f, 0.02f),
        MidSpeck = new Color(0.4f, 0.6f, 0.95f),
        MidSpecksGlow = true,
        Neon = new Color(0.75f, 0.4f, 1f),
        NeonAlt = new Color(1f, 0.65f, 0.2f),
        StarColors = new[] { new Color(0.4f, 0.65f, 1f), new Color(0.55f, 0.8f, 1f), new Color(0.8f, 0.4f, 1f) },
        PlanetColor = new Color(0.25f, 0.35f, 0.95f),
        Planets = 2
    };

    // Royal-blue causeway lined with golden pillars and a cyan border wall.
    public static readonly SkyfortTheme AzureCauseway = new()
    {
        Name = "AZURE CAUSEWAY",
        MidZoneName = "COMET LANE",
        Tagline = "Golden pillars along a glowing blue road",
        Background = new Color(0f, 0f, 0.01f),
        SunColor = new Color(1f, 0.95f, 0.85f),
        SunIntensity = 1.25f,
        Ambient = new Color(0.25f, 0.3f, 0.5f),
        Deck = new Color(0.12f, 0.22f, 0.9f),
        DeckLine = new Color(0.42f, 0.35f, 0.95f),
        Rampart = new Color(0.05f, 0.7f, 0.95f),
        RampartGlow = new Color(0.4f, 0.95f, 1f),
        StoneMain = new Color(0.9f, 0.68f, 0.1f),
        StoneBase = new Color(0.6f, 0.42f, 0.05f),
        StoneGlow = new Color(1f, 0.95f, 0.5f),
        CastleStone = new Color(0.8f, 0.6f, 0.12f),
        CastleTrim = new Color(0.55f, 0.4f, 0.08f),
        Roof = new Color(0.1f, 0.6f, 0.95f),
        WindowGlow = new Color(0.4f, 0.95f, 1f),
        MidFloor = new Color(0f, 0f, 0.01f),
        MidSpeck = new Color(1f, 0.3f, 0.3f),
        MidSpecksGlow = true,
        Neon = new Color(0.4f, 0.9f, 1f),
        NeonAlt = new Color(1f, 0.85f, 0.2f),
        StarColors = new[] { new Color(1f, 0.25f, 0.25f), new Color(0.3f, 1f, 0.4f), new Color(0.35f, 0.5f, 1f), new Color(1f, 0.9f, 0.3f) },
        PlanetColor = new Color(0.9f, 0.25f, 0.2f),
        Planets = 1
    };

    // Bright cyan floor under grey brick ramparts and bastions.
    public static readonly SkyfortTheme BrickBulwark = new()
    {
        Name = "BRICK BULWARK",
        MidZoneName = "SCRAPYARD ORBIT",
        Tagline = "Grey brick walls on an electric sea",
        Background = new Color(0f, 0f, 0.015f),
        SunColor = new Color(0.95f, 0.97f, 1f),
        SunIntensity = 1.3f,
        Ambient = new Color(0.3f, 0.34f, 0.45f),
        Deck = new Color(0.05f, 0.5f, 0.98f),
        DeckLine = new Color(0.1f, 0.35f, 0.8f),
        Rampart = new Color(0.55f, 0.58f, 0.6f),
        RampartGlow = new Color(0.3f, 0.7f, 1f),
        StoneMain = new Color(0.58f, 0.6f, 0.62f),
        StoneBase = new Color(0.38f, 0.4f, 0.44f),
        StoneGlow = new Color(1f, 0.6f, 0.2f),
        CastleStone = new Color(0.6f, 0.62f, 0.64f),
        CastleTrim = new Color(0.4f, 0.42f, 0.46f),
        Roof = new Color(0.25f, 0.3f, 0.55f),
        WindowGlow = new Color(1f, 0.75f, 0.2f),
        MidFloor = new Color(0.01f, 0.03f, 0.1f),
        MidSpeck = new Color(0.7f, 0.75f, 0.8f),
        MidSpecksGlow = false,
        Neon = new Color(0.5f, 0.85f, 1f),
        NeonAlt = new Color(1f, 0.65f, 0.2f),
        Landmark = SkyfortLandmark.Bastion,
        Bricks = true,
        StarColors = new[] { new Color(0.8f, 0.85f, 1f), new Color(0.4f, 0.6f, 1f) },
        PlanetColor = new Color(0.3f, 0.45f, 0.85f),
        Planets = 1
    };

    // Industrial blue plain: banded red tanks, grey silos and white pipelines.
    public static readonly SkyfortTheme Refinery = new()
    {
        Name = "REFINERY",
        MidZoneName = "ASTEROID BELT",
        Tagline = "Fuel tanks, silos and pipelines",
        Background = new Color(0f, 0f, 0.03f),
        SunColor = new Color(1f, 0.97f, 0.9f),
        SunIntensity = 1.2f,
        Ambient = new Color(0.22f, 0.26f, 0.5f),
        Deck = new Color(0.06f, 0.12f, 0.7f),
        DeckLine = new Color(0.85f, 0.9f, 1f),
        Rampart = new Color(0.12f, 0.2f, 0.78f),
        RampartGlow = new Color(0.9f, 0.95f, 1f),
        StoneMain = new Color(0.88f, 0.22f, 0.12f),
        StoneBase = new Color(0.6f, 0.62f, 0.68f),
        StoneGlow = new Color(1f, 0.55f, 0.1f),
        CastleStone = new Color(0.62f, 0.7f, 0.74f),
        CastleTrim = new Color(0.42f, 0.5f, 0.56f),
        Roof = new Color(0.55f, 0.58f, 0.64f),
        WindowGlow = new Color(1f, 0.7f, 0.15f),
        MidFloor = new Color(0.01f, 0.01f, 0.05f),
        MidSpeck = new Color(0.6f, 0.65f, 0.75f),
        MidSpecksGlow = false,
        Neon = new Color(0.9f, 0.95f, 1f),
        NeonAlt = new Color(1f, 0.5f, 0.15f),
        Landmark = SkyfortLandmark.Tank,
        StarColors = new[] { new Color(0.35f, 0.5f, 1f), new Color(1f, 0.3f, 0.2f), new Color(0.9f, 0.9f, 1f) },
        PlanetColor = new Color(0.55f, 0.35f, 0.2f),
        Planets = 3
    };

    public static readonly SkyfortTheme[] All = { Skyfort, DuneRuins, IceCitadel, VioletBastion, AzureCauseway, BrickBulwark, Refinery };

    public static SkyfortTheme ForLevel(int level) => All[(level - 1) % All.Length];
}
