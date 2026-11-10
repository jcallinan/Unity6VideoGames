using UnityEngine;

// Retro display filter on the game camera (Built-in Render Pipeline image effect using OnRenderImage).
// Modes cycle with M in game and the choice is remembered. Each one is meant to look clearly different:
//   16-BIT  - clean 16-bit-console look: fine pixels, 15-bit color, neon glow, no scanlines
//   CRT     - curved tube: visible scanlines, RGB mask, color fringing, heavy glow and vignette
//   ARCADE  - early-80s cabinet: chunky pixels, 512 colors, deep scanlines, punchy saturation
//   ATARI   - 2600-style: 320 x 240 pixels, a limited set of flat colors, lightly streaked rows with dark gaps
//   MONO    - four-shade green handheld LCD
//   MODERN  - no filter
[RequireComponent(typeof(Camera))]
public sealed class SkyfortRetroFilter : MonoBehaviour
{
    public enum Mode { SixteenBit, Crt, Arcade, Atari, Mono, Modern }

    private const int ModeCount = 6;

    private struct Settings
    {
        public int Lines;
        public int Width;        // 0 = follow the screen aspect
        public float Softness;
        public float Levels;
        public float Dither;
        public float Scanlines;
        public float Curvature;
        public float Mask;
        public float Vignette;
        public float Glow;
        public float Fringe;
        public float Saturation;
        public float Contrast;
        public float Smear;
        public float Mono;
        public Color MonoDark;
        public Color MonoLight;
        public float Tint;
        public Color TintColor;
    }

    private const string PrefsKey = "SkyfortStrike.RetroFilter2";

    [Tooltip("Hidden/Skyfort/Retro (Assets/Shaders/SkyfortRetro.shader). Referenced here so builds include it.")]
    [SerializeField] private Shader shader;
    [SerializeField] private Mode mode = Mode.SixteenBit;

    private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int LevelsId = Shader.PropertyToID("_Levels");
    private static readonly int DitherId = Shader.PropertyToID("_Dither");
    private static readonly int ScanlinesId = Shader.PropertyToID("_Scanlines");
    private static readonly int CurvatureId = Shader.PropertyToID("_Curvature");
    private static readonly int MaskId = Shader.PropertyToID("_Mask");
    private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int FringeId = Shader.PropertyToID("_Fringe");
    private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
    private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
    private static readonly int SmearId = Shader.PropertyToID("_Smear");
    private static readonly int MonoId = Shader.PropertyToID("_Mono");
    private static readonly int MonoDarkId = Shader.PropertyToID("_MonoDark");
    private static readonly int MonoLightId = Shader.PropertyToID("_MonoLight");
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

    private Material material;

    public Mode Current => mode;

    public static string Label(Mode value) => value switch
    {
        Mode.SixteenBit => "16-BIT",
        Mode.Crt => "CRT",
        Mode.Arcade => "ARCADE",
        Mode.Atari => "ATARI",
        Mode.Mono => "MONO LCD",
        _ => "MODERN"
    };

    private void Awake()
    {
        mode = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, (int)mode), 0, ModeCount - 1);
    }

    public Mode Cycle()
    {
        mode = (Mode)(((int)mode + 1) % ModeCount);
        PlayerPrefs.SetInt(PrefsKey, (int)mode);
        PlayerPrefs.Save();
        return mode;
    }

    private static Settings For(Mode value) => value switch
    {
        Mode.Crt => new Settings
        {
            Lines = 300, Softness = 0.35f, Levels = 31f, Dither = 0.08f, Scanlines = 0.55f, Curvature = 0.09f,
            Mask = 0.4f, Vignette = 0.55f, Glow = 1.4f, Fringe = 0.3f, Saturation = 1.2f, Contrast = 1.2f
        },
        Mode.Arcade => new Settings
        {
            Lines = 224, Softness = 0.04f, Levels = 7f, Dither = 0.5f, Scanlines = 0.75f, Curvature = 0.05f,
            Mask = 0.35f, Vignette = 0.45f, Glow = 1.2f, Fringe = 0.1f, Saturation = 1.5f, Contrast = 1.35f
        },
        Mode.Atari => new Settings
        {
            Lines = 240, Width = 320, Softness = 0f, Levels = 5f, Dither = 0f, Scanlines = 0.5f, Curvature = 0.01f,
            Mask = 0f, Vignette = 0.15f, Glow = 0f, Fringe = 0f, Saturation = 1.4f, Contrast = 1.35f, Smear = 1f
        },
        Mode.Mono => new Settings
        {
            Lines = 144, Softness = 0.08f, Levels = 255f, Dither = 0f, Scanlines = 0.18f, Curvature = 0f,
            Mask = 0f, Vignette = 0.3f, Glow = 0f, Fringe = 0f, Saturation = 1f, Contrast = 1.25f,
            Mono = 1f, MonoDark = new Color(0.05f, 0.2f, 0.06f), MonoLight = new Color(0.62f, 0.76f, 0.07f)
        },
        _ => new Settings
        {
            Lines = 360, Softness = 0.1f, Levels = 31f, Dither = 0.06f, Scanlines = 0f, Curvature = 0f,
            Mask = 0f, Vignette = 0.1f, Glow = 0.7f, Fringe = 0f, Saturation = 1.1f, Contrast = 1.2f
        }
    };

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (mode == Mode.Modern || shader == null || !shader.isSupported)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (material == null)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        var settings = For(mode);
        var aspect = (float)source.width / Mathf.Max(1, source.height);
        var width = settings.Width > 0 ? settings.Width : Mathf.Round(settings.Lines * aspect);
        material.SetVector(ResolutionId, new Vector2(width, settings.Lines));
        material.SetFloat(SoftnessId, settings.Softness);
        material.SetFloat(LevelsId, settings.Levels);
        material.SetFloat(DitherId, settings.Dither);
        material.SetFloat(ScanlinesId, settings.Scanlines);
        material.SetFloat(CurvatureId, settings.Curvature);
        material.SetFloat(MaskId, settings.Mask);
        material.SetFloat(VignetteId, settings.Vignette);
        material.SetFloat(GlowId, settings.Glow);
        material.SetFloat(FringeId, settings.Fringe);
        material.SetFloat(SaturationId, settings.Saturation);
        material.SetFloat(ContrastId, settings.Contrast);
        material.SetFloat(SmearId, settings.Smear);
        material.SetFloat(MonoId, settings.Mono);
        material.SetColor(MonoDarkId, settings.MonoDark);
        material.SetColor(MonoLightId, settings.MonoLight);
        material.SetFloat(TintId, settings.Tint);
        material.SetColor(TintColorId, settings.TintColor);
        source.filterMode = FilterMode.Bilinear; // the soft blur relies on filtered sampling
        Graphics.Blit(source, destination, material);
    }

    private void OnDestroy()
    {
        if (material != null)
        {
            Destroy(material);
        }
    }
}
