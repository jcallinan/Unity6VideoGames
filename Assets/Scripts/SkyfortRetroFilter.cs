using UnityEngine;

// Retro display filter on the game camera (Built-in Render Pipeline image effect using OnRenderImage).
// Modes cycle with F in game and the choice is remembered:
//   16-BIT  - SNES-style: ~224-line pixel grid, 15-bit color with light dithering
//   CRT     - 16-BIT on a curved tube with scanlines, RGB mask, glow and vignette
//   ARCADE  - early-80s arcade: chunkier pixels, 8 levels per channel, strong dither, CRT
//   MODERN  - no filter
[RequireComponent(typeof(Camera))]
public sealed class SkyfortRetroFilter : MonoBehaviour
{
    public enum Mode { SixteenBit, Crt, Arcade, Modern }

    private struct Settings
    {
        public int Lines;
        public float Levels;
        public float Dither;
        public float Scanlines;
        public float Curvature;
        public float Mask;
        public float Vignette;
        public float Glow;
        public float Saturation;
    }

    private const string PrefsKey = "SkyfortStrike.RetroFilter";

    [Tooltip("Hidden/Skyfort/Retro (Assets/Shaders/SkyfortRetro.shader). Referenced here so builds include it.")]
    [SerializeField] private Shader shader;
    [SerializeField] private Mode mode = Mode.SixteenBit;

    private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
    private static readonly int LevelsId = Shader.PropertyToID("_Levels");
    private static readonly int DitherId = Shader.PropertyToID("_Dither");
    private static readonly int ScanlinesId = Shader.PropertyToID("_Scanlines");
    private static readonly int CurvatureId = Shader.PropertyToID("_Curvature");
    private static readonly int MaskId = Shader.PropertyToID("_Mask");
    private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int SaturationId = Shader.PropertyToID("_Saturation");

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
        Mode.Crt => new Settings { Lines = 224, Levels = 31f, Dither = 0.6f, Scanlines = 0.5f, Curvature = 0.07f, Mask = 0.35f, Vignette = 0.4f, Glow = 0.7f, Saturation = 1.15f },
        Mode.Arcade => new Settings { Lines = 192, Levels = 7f, Dither = 1f, Scanlines = 0.55f, Curvature = 0.09f, Mask = 0.4f, Vignette = 0.45f, Glow = 0.9f, Saturation = 1.35f },
        _ => new Settings { Lines = 224, Levels = 31f, Dither = 0.5f, Scanlines = 0f, Curvature = 0f, Mask = 0f, Vignette = 0f, Glow = 0.35f, Saturation = 1.1f }
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
        material.SetFloat(LevelsId, settings.Levels);
        material.SetFloat(DitherId, settings.Dither);
        material.SetFloat(ScanlinesId, settings.Scanlines);
        material.SetFloat(CurvatureId, settings.Curvature);
        material.SetFloat(MaskId, settings.Mask);
        material.SetFloat(VignetteId, settings.Vignette);
        material.SetFloat(GlowId, settings.Glow);
        material.SetFloat(SaturationId, settings.Saturation);
        source.filterMode = FilterMode.Point;
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
