using System;
using UnityEngine;
using UnityEngine.UIElements;

// Title screen overlay. The game itself plays behind it as a demo reel (see SkyfortStrike.Demo.cs): the fighter flies
// the real levels on autopilot while the camera cuts between shots. This overlay adds the letterbox, camera readout,
// a glitching logo, the start prompt and a button for the platformer scene. Built in code on the HUD's UIDocument.
public sealed class SkyfortMenu
{
    private static readonly Color Cyan = new(0.12f, 0.82f, 1f);
    private static readonly Color Magenta = new(1f, 0.25f, 0.8f);
    private static readonly Color Gold = new(1f, 0.75f, 0.2f);

    private readonly VisualElement root;
    private readonly VisualElement titleBlock;
    private readonly VisualElement flash;
    private readonly Label highScore;
    private readonly Label prompt;
    private readonly Label camLabel;
    private readonly SkyfortMenuOverlay overlay;
    private readonly Label[] logoCopies = new Label[2];
    private readonly Label[] strikeCopies = new Label[2];
    private float shownAt;
    private float flashAlpha;
    private float burstUntil;
    private float nextBurst;

    private SkyfortMenu(VisualElement root, VisualElement titleBlock, VisualElement flash, Label highScore, Label prompt, Label camLabel, SkyfortMenuOverlay overlay)
    {
        this.root = root;
        this.titleBlock = titleBlock;
        this.flash = flash;
        this.highScore = highScore;
        this.prompt = prompt;
        this.camLabel = camLabel;
        this.overlay = overlay;
    }

    public static SkyfortMenu Build(UIDocument document, Action onStart, Action onPlatformer)
    {
        var parent = document.rootVisualElement;
        parent.pickingMode = PickingMode.Ignore;
        parent.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
        parent.style.color = Color.white;
        parent.style.fontSize = 18;
        parent.style.unityFontStyleAndWeight = FontStyle.Bold;
        var root = new VisualElement { pickingMode = PickingMode.Ignore };
        Fill(root);
        root.style.display = DisplayStyle.None;
        parent.Add(root);

        var overlay = new SkyfortMenuOverlay { pickingMode = PickingMode.Ignore };
        Fill(overlay);
        root.Add(overlay);

        // Logo: the white title over a red and a cyan copy that jitter apart, like a damaged video signal.
        var titleBlock = new VisualElement { pickingMode = PickingMode.Ignore };
        titleBlock.style.position = Position.Absolute;
        titleBlock.style.left = 0;
        titleBlock.style.right = 0;
        titleBlock.style.top = new Length(12, LengthUnit.Percent);
        titleBlock.style.alignItems = Align.Center;
        root.Add(titleBlock);

        var menu = new SkyfortMenu(root, titleBlock, null, null, null, null, overlay);
        var logoRow = GlitchText(titleBlock, "SKYFORT", 128, 14, menu.logoCopies);
        var strikeRow = GlitchText(titleBlock, "STRIKE", 86, 40, menu.strikeCopies);
        strikeRow.style.marginTop = -30;
        var tagline = Text(titleBlock, "ALTITUDE ASSAULT", 22, Gold, 12);
        tagline.style.marginTop = 4;
        _ = logoRow;

        var bottom = new VisualElement { pickingMode = PickingMode.Ignore };
        bottom.style.position = Position.Absolute;
        bottom.style.left = 0;
        bottom.style.right = 0;
        bottom.style.bottom = 34;
        bottom.style.alignItems = Align.Center;
        root.Add(bottom);

        var prompt = Text(bottom, "LOADING...", 30, Color.white, 6);
        var highScore = Text(bottom, "HIGH SCORE  000000", 20, Gold, 4);
        highScore.style.marginTop = 6;

        var buttons = new VisualElement { pickingMode = PickingMode.Ignore };
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.marginTop = 14;
        bottom.Add(buttons);
        MenuButton(buttons, "START GAME  (SPACE)", Gold, onStart);
        MenuButton(buttons, "PIXEL RUNNER  (2)", Cyan, onPlatformer);

        var hint = Text(bottom, "M display mode     SPACE start     2 platformer", 13, new Color(1f, 1f, 1f, 0.6f), 1);
        hint.style.marginTop = 10;

        var camLabel = Text(root, "", 16, Cyan, 3);
        camLabel.style.position = Position.Absolute;
        camLabel.style.left = 40;
        camLabel.style.top = new Length(9, LengthUnit.Percent);

        var flash = new VisualElement { pickingMode = PickingMode.Ignore };
        Fill(flash);
        flash.style.backgroundColor = Color.white;
        flash.style.opacity = 0f;
        root.Add(flash);

        var built = new SkyfortMenu(root, titleBlock, flash, highScore, prompt, camLabel, overlay);
        for (var i = 0; i < 2; i++)
        {
            built.logoCopies[i] = menu.logoCopies[i];
            built.strikeCopies[i] = menu.strikeCopies[i];
        }

        root.schedule.Execute(built.Tick).Every(16);
        return built;
    }

    public void Show(int best, string promptText)
    {
        root.style.display = DisplayStyle.Flex;
        highScore.text = $"HIGH SCORE  {best:000000}";
        prompt.text = promptText;
        overlay.Active = true;
        shownAt = Time.realtimeSinceStartup;
        nextBurst = shownAt + 2.2f;
    }

    public void SetPrompt(string promptText)
    {
        prompt.text = promptText;
    }

    // Called on every camera cut: a white flash and a new readout.
    public void Cut(int index, string shotName)
    {
        flashAlpha = 0.85f;
        camLabel.text = $"REC  CAM {index:00}  {shotName}";
    }

    // A short message shown for a couple of seconds, e.g. the display mode after pressing M.
    public void Notice(string text)
    {
        camLabel.text = text;
        flashAlpha = Mathf.Max(flashAlpha, 0.12f);
    }

    public void Hide()
    {
        root.style.display = DisplayStyle.None;
        overlay.Active = false;
    }

    private void Tick()
    {
        if (root.resolvedStyle.display == DisplayStyle.None)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        var age = now - shownAt;

        // Intro: the logo slams in from large and transparent.
        var intro = Mathf.Clamp01(age / 0.8f);
        var eased = 1f - Mathf.Pow(1f - intro, 3f);
        var scale = Mathf.Lerp(1.7f, 1f, eased);
        titleBlock.style.scale = new Scale(new Vector3(scale, scale, 1f));
        titleBlock.style.opacity = eased;

        // Glitch: always a small drift, and every few seconds a short violent burst.
        if (now >= nextBurst)
        {
            burstUntil = now + 0.16f;
            nextBurst = now + UnityEngine.Random.Range(2.5f, 5f);
        }

        var burst = now < burstUntil || age < 0.9f;
        var spread = burst ? UnityEngine.Random.Range(8f, 20f) : 2f + Mathf.Sin(now * 3f);
        var lift = burst ? UnityEngine.Random.Range(-4f, 4f) : 0f;
        Place(logoCopies, spread, lift);
        Place(strikeCopies, spread * 0.7f, lift);

        prompt.style.opacity = 0.55f + 0.45f * Mathf.Sin(now * 4f);

        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - Time.unscaledDeltaTime * 3.5f);
            flash.style.opacity = flashAlpha;
        }
    }

    private static void Place(Label[] copies, float spread, float lift)
    {
        copies[0].style.left = -spread;
        copies[0].style.top = lift;
        copies[1].style.left = spread;
        copies[1].style.top = -lift;
    }

    // A label with two absolutely placed color copies behind it (red-magenta and cyan).
    private static VisualElement GlitchText(VisualElement parent, string text, int size, float spacing, Label[] copies)
    {
        var holder = new VisualElement { pickingMode = PickingMode.Ignore };
        parent.Add(holder);
        var colors = new[] { new Color(1f, 0.15f, 0.55f, 0.85f), new Color(0.1f, 0.9f, 1f, 0.85f) };
        for (var i = 0; i < 2; i++)
        {
            var copy = Text(holder, text, size, colors[i], spacing);
            copy.style.position = Position.Absolute;
            copies[i] = copy;
        }

        var main = Text(holder, text, size, Color.white, spacing);
        main.style.unityTextOutlineColor = new Color(0.02f, 0.05f, 0.15f);
        main.style.unityTextOutlineWidth = 2.5f;
        return holder;
    }

    private static void MenuButton(VisualElement parent, string text, Color accent, Action onClick)
    {
        var rest = new Color(0.04f, 0.08f, 0.16f, 0.85f);
        var hover = Color.Lerp(rest, accent, 0.5f);
        var button = new Button(onClick) { text = text };
        button.style.marginLeft = 8;
        button.style.marginRight = 8;
        button.style.fontSize = 20;
        button.style.paddingLeft = 26;
        button.style.paddingRight = 26;
        button.style.paddingTop = 8;
        button.style.paddingBottom = 8;
        button.style.color = Color.white;
        button.style.backgroundColor = rest;
        button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = accent;
        button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 2;
        button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 4;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.pickingMode = PickingMode.Position;
        button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = hover);
        button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = rest);
        parent.Add(button);
    }

    private static void Fill(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = 0;
        element.style.right = 0;
        element.style.top = 0;
        element.style.bottom = 0;
    }

    private static Label Text(VisualElement parent, string text, int size, Color color, float spacing)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.style.fontSize = size;
        label.style.color = color;
        label.style.letterSpacing = spacing;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        parent.Add(label);
        return label;
    }
}

// Letterbox bars, a darkening vignette for legibility, camera-style corner brackets, a slow scan line and scanlines.
public sealed class SkyfortMenuOverlay : VisualElement
{
    public bool Active { get; set; }

    public SkyfortMenuOverlay()
    {
        generateVisualContent += Draw;
        schedule.Execute(() =>
        {
            if (Active)
            {
                MarkDirtyRepaint();
            }
        }).Every(33);
    }

    private void Draw(MeshGenerationContext context)
    {
        var rect = contentRect;
        var w = rect.width;
        var h = rect.height;
        if (w < 2f || h < 2f)
        {
            return;
        }

        var p = context.painter2D;
        var t = Time.realtimeSinceStartup;
        var bar = h * 0.07f;

        // Soft darkening toward the top and bottom, so the logo and prompt read over any scenery.
        const int steps = 16;
        for (var i = 0; i < steps; i++)
        {
            var k = 1f - (float)i / steps;
            var alpha = 0.55f * k * k;
            var band = h * 0.3f / steps;
            Box(p, 0f, bar + i * band, w, band + 1f, new Color(0f, 0f, 0.03f, alpha));
            Box(p, 0f, h - bar - (i + 1) * band, w, band + 1f, new Color(0f, 0f, 0.03f, alpha));
        }

        Box(p, 0f, 0f, w, bar, Color.black);
        Box(p, 0f, h - bar, w, bar, Color.black);

        // Corner brackets, like a viewfinder.
        var cyan = new Color(0.2f, 0.9f, 1f, 0.8f);
        var inset = 34f;
        var arm = 40f;
        p.lineWidth = 3f;
        p.strokeColor = cyan;
        p.BeginPath();
        Bracket(p, inset, bar + inset, arm, 1f, 1f);
        Bracket(p, w - inset, bar + inset, arm, -1f, 1f);
        Bracket(p, inset, h - bar - inset, arm, 1f, -1f);
        Bracket(p, w - inset, h - bar - inset, arm, -1f, -1f);
        p.Stroke();

        // A slow scan line sweeping down the picture.
        var y = Mathf.Repeat(t * 0.12f, 1f) * h;
        Box(p, 0f, y, w, 2f, new Color(0.3f, 0.9f, 1f, 0.08f));

        // Blinking recording dot.
        if (Mathf.Repeat(t, 1.2f) < 0.7f)
        {
            p.fillColor = new Color(1f, 0.1f, 0.1f);
            p.BeginPath();
            p.Arc(new Vector2(w - 60f, bar + 56f), 7f, 0f, 360f);
            p.ClosePath();
            p.Fill();
        }

        // Fine scanlines over everything.
        p.lineWidth = 1f;
        p.strokeColor = new Color(0f, 0f, 0f, 0.14f);
        p.BeginPath();
        for (var line = 0f; line < h; line += 3f)
        {
            p.MoveTo(new Vector2(0f, line));
            p.LineTo(new Vector2(w, line));
        }

        p.Stroke();
    }

    private static void Bracket(Painter2D p, float x, float y, float arm, float dx, float dy)
    {
        p.MoveTo(new Vector2(x + dx * arm, y));
        p.LineTo(new Vector2(x, y));
        p.LineTo(new Vector2(x, y + dy * arm));
    }

    private static void Box(Painter2D p, float x, float y, float width, float height, Color color)
    {
        p.fillColor = color;
        p.BeginPath();
        p.MoveTo(new Vector2(x, y));
        p.LineTo(new Vector2(x + width, y));
        p.LineTo(new Vector2(x + width, y + height));
        p.LineTo(new Vector2(x, y + height));
        p.ClosePath();
        p.Fill();
    }
}
