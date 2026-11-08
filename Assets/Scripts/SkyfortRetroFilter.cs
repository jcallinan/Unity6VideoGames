using UnityEngine;

// Retro display filter on the game camera (Built-in Render Pipeline image effect using OnRenderImage).
// Modes cycle with M in game and the choice is remembered:
//   16-BIT  - 16-bit-console look: ~360-line pixels with lightly softened edges, 15-bit color, neon glow
//   CRT     - on a gently curved tube: soft scanlines, faint RGB mask, slight color fringing, stronger glow
//   ARCADE  - early-80s arcade: chunkier pixels, fewer colors, CRT traits
//   MODERN  - no filter
// The modes sit between razor-sharp pixels and a blurry image: edges stay crisp with just a touch of softness.
[RequireComponent(typeof(Camera))]
public sealed class SkyfortRetroFilter : MonoBehaviour
{
    public enum Mode { SixteenBit, Crt, Arcade, Modern }

    private struct Settings
    {
        public int Lines;
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
    }

    private const string PrefsKey = "SkyfortStrike.RetroFilter";

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

    private Material material;

    public Mode Current => mode;

    public static string Label(Mode value) => value switch
    {
        Mode.SixteenBit => "16-BIT",
        Mode.Crt => "CRT",
        Mode.Arcade => "ARCADE",
        _ => "MODERN"
    };

    private void Awake()
    {
        mode = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, (int)mode), 0, 3);
    }

    public Mode Cycle()
    {
        mode = (Mode)(((int)mode + 1) % 4);
        PlayerPrefs.SetInt(PrefsKey, (int)mode);
        PlayerPrefs.Save();
        return mode;
    }

    private static Settings For(Mode value) => value switch
    {
        Mode.Crt => new Settings
        {
            Lines = 300, Softness = 0.18f, Levels = 31f, Dither = 0.08f, Scanlines = 0.2f, Curvature = 0.03f,
            Mask = 0.1f, Vignette = 0.3f, Glow = 0.9f, Fringe = 0.1f, Saturation = 1.15f, Contrast = 1.2f
        },
        Mode.Arcade => new Settings
        {
            Lines = 240, Softness = 0.06f, Levels = 15f, Dither = 0.2f, Scanlines = 0.25f, Curvature = 0.045f,
            Mask = 0.15f, Vignette = 0.35f, Glow = 1.0f, Fringe = 0.08f, Saturation = 1.2f, Contrast = 1.25f
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
        material.SetVector(ResolutionId, new Vector2(Mathf.Round(settings.Lines * aspect), settings.Lines));
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
