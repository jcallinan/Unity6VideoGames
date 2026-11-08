using System;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

// HUD data source. Unity 6 runtime data binding: [CreateProperty] exposes each value to the binding system,
// INotifyBindablePropertyChanged pushes per-property change notifications, and IDataSourceViewHashProvider
// lets the UI skip all binding work on frames where nothing changed.
public sealed class SkyfortHudState : INotifyBindablePropertyChanged, IDataSourceViewHashProvider
{
    private long version;
    private string score = "000000";
    private string highScore = "000000";
    private string level = "1";
    private string ships = "3";
    private string zone = "";
    private string banner = "";
    private string subBanner = "";
    private string altitudeReadout = "0";
    private float fuel = 1f;
    private float altitude;
    private float bossHealth;
    private bool bossVisible;
    private bool lowFuel;
    private float hull = 1f;
    private bool lowHull;
    private string powerStatus = "";
    private string toast = "";
    private Color toastColor = Color.white;

    public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

    [CreateProperty] public string Score { get => score; set => Set(ref score, value); }
    [CreateProperty] public string HighScore { get => highScore; set => Set(ref highScore, value); }
    [CreateProperty] public string Level { get => level; set => Set(ref level, value); }
    [CreateProperty] public string Ships { get => ships; set => Set(ref ships, value); }
    [CreateProperty] public string Zone { get => zone; set => Set(ref zone, value); }
    [CreateProperty] public string Banner { get => banner; set => Set(ref banner, value); }
    [CreateProperty] public string SubBanner { get => subBanner; set => Set(ref subBanner, value); }
    [CreateProperty] public string AltitudeReadout { get => altitudeReadout; set => Set(ref altitudeReadout, value); }
    [CreateProperty] public float Fuel { get => fuel; set => Set(ref fuel, value); }
    [CreateProperty] public float Altitude { get => altitude; set => Set(ref altitude, value); }
    [CreateProperty] public float BossHealth { get => bossHealth; set => Set(ref bossHealth, value); }
    [CreateProperty] public bool BossVisible { get => bossVisible; set => Set(ref bossVisible, value); }
    [CreateProperty] public bool LowFuel { get => lowFuel; set => Set(ref lowFuel, value); }
    [CreateProperty] public float Hull { get => hull; set => Set(ref hull, value); }
    [CreateProperty] public bool LowHull { get => lowHull; set => Set(ref lowHull, value); }
    [CreateProperty] public string PowerStatus { get => powerStatus; set => Set(ref powerStatus, value); }
    [CreateProperty] public string Toast { get => toast; set => Set(ref toast, value); }
    [CreateProperty] public Color ToastColor { get => toastColor; set => Set(ref toastColor, value); }

    public long GetViewHashCode() => version;

    private void Set<T>(ref T field, T value, [CallerMemberName] string property = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        version++;
        propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
    }
}

// Unity 6 [UxmlElement] custom control (source-generated, replaces UxmlFactory/UxmlTraits). It draws itself
// with the Painter2D vector API and exposes Value as a bindable [CreateProperty].
[UxmlElement]
public partial class SkyfortGauge : VisualElement
{
    private float value;

    [UxmlAttribute, CreateProperty]
    public float Value
    {
        get => value;
        set
        {
            var clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(clamped, this.value))
            {
                return;
            }

            this.value = clamped;
            MarkDirtyRepaint();
        }
    }

    [UxmlAttribute] public bool Vertical { get; set; }
    [UxmlAttribute] public int Ticks { get; set; } = 10;
    [UxmlAttribute] public Color FillColor { get; set; } = new(0.12f, 0.82f, 1f);
    [UxmlAttribute] public Color WarningColor { get; set; } = new(1f, 0.14f, 0.22f);
    [UxmlAttribute] public float WarningBelow { get; set; } = -1f;

    public SkyfortGauge()
    {
        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        var rect = contentRect;
        if (rect.width < 1f || rect.height < 1f)
        {
            return;
        }

        var painter = context.painter2D;
        painter.fillColor = new Color(0.02f, 0.05f, 0.1f, 0.8f);
        FillRect(painter, rect);

        var fill = rect;
        if (Vertical)
        {
            fill.yMin = rect.yMax - rect.height * value;
        }
        else
        {
            fill.width = rect.width * value;
        }

        painter.fillColor = value < WarningBelow ? WarningColor : FillColor;
        FillRect(painter, fill);

        painter.strokeColor = new Color(1f, 1f, 1f, 0.35f);
        painter.lineWidth = 1f;
        painter.BeginPath();
        for (var i = 1; i < Ticks; i++)
        {
            var t = (float)i / Ticks;
            if (Vertical)
            {
                var y = rect.yMax - rect.height * t;
                painter.MoveTo(new Vector2(rect.xMin, y));
                painter.LineTo(new Vector2(rect.xMin + rect.width * (i % 5 == 0 ? 1f : 0.4f), y));
            }
            else
            {
                var x = rect.xMin + rect.width * t;
                painter.MoveTo(new Vector2(x, rect.yMax));
                painter.LineTo(new Vector2(x, rect.yMax - rect.height * (i % 5 == 0 ? 1f : 0.4f)));
            }
        }

        painter.Stroke();
        painter.strokeColor = FillColor;
        painter.lineWidth = 2f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
        painter.LineTo(new Vector2(rect.xMax, rect.yMin));
        painter.LineTo(new Vector2(rect.xMax, rect.yMax));
        painter.LineTo(new Vector2(rect.xMin, rect.yMax));
        painter.ClosePath();
        painter.Stroke();
    }

    private static void FillRect(Painter2D painter, Rect rect)
    {
        painter.BeginPath();
        painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
        painter.LineTo(new Vector2(rect.xMax, rect.yMin));
        painter.LineTo(new Vector2(rect.xMax, rect.yMax));
        painter.LineTo(new Vector2(rect.xMin, rect.yMax));
        painter.ClosePath();
        painter.Fill();
    }
}

// Builds the runtime UI Toolkit HUD in code (no UXML/USS assets needed) and binds it to SkyfortHudState.
public static class SkyfortHud
{
    private static readonly Color Cyan = new(0.12f, 0.82f, 1f);
    private static readonly Color Gold = new(1f, 0.62f, 0.12f);
    private static readonly Color Red = new(1f, 0.18f, 0.08f);

    // Fills the scene's HUD UIDocument (its PanelSettings asset lives in Assets/UI) and binds it to the state.
    public static void Build(UIDocument document, SkyfortHudState state)
    {
        var root = document.rootVisualElement;
        root.Clear();
        root.pickingMode = PickingMode.Ignore;
        root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
        root.style.color = Color.white;
        root.style.fontSize = 18;
        root.style.unityFontStyleAndWeight = FontStyle.Bold;
        root.dataSource = state;

        var top = Row(root);
        top.style.position = Position.Absolute;
        top.style.left = 24;
        top.style.right = 24;
        top.style.top = 16;
        top.style.justifyContent = Justify.SpaceBetween;
        top.style.alignItems = Align.FlexStart;

        var left = Column(top);
        Readout(left, "SCORE", nameof(SkyfortHudState.Score), Cyan, 30);
        Readout(left, "HIGH", nameof(SkyfortHudState.HighScore), Gold, 18);

        var center = Column(top);
        center.style.alignItems = Align.Center;
        var zone = BoundLabel(center, nameof(SkyfortHudState.Zone), 20, Gold);
        zone.style.letterSpacing = 4;
        var bossRow = Column(center);
        bossRow.style.alignItems = Align.Center;
        bossRow.style.marginTop = 6;
        Bind(bossRow, "style.display", nameof(SkyfortHudState.BossVisible), (ref bool visible) =>
            new StyleEnum<DisplayStyle>(visible ? DisplayStyle.Flex : DisplayStyle.None));
        var bossLabel = new Label("WARDEN CORE") { pickingMode = PickingMode.Ignore };
        bossLabel.style.fontSize = 13;
        bossLabel.style.color = Red;
        bossRow.Add(bossLabel);
        var bossGauge = Gauge(bossRow, nameof(SkyfortHudState.BossHealth), Red, false, 340, 12);
        bossGauge.Ticks = 20;

        // Short notices such as "SHIELD UP" or "HULL DAMAGED", colored by the event.
        var toast = BoundLabel(center, nameof(SkyfortHudState.Toast), 22, Color.white);
        toast.style.marginTop = 8;
        Bind(toast, "style.color", nameof(SkyfortHudState.ToastColor), (ref Color color) => new StyleColor(color));

        var right = Column(top);
        right.style.alignItems = Align.FlexEnd;
        Readout(right, "LEVEL", nameof(SkyfortHudState.Level), Cyan, 24).parent.style.alignItems = Align.FlexEnd;
        Readout(right, "SHIPS", nameof(SkyfortHudState.Ships), Cyan, 24).parent.style.alignItems = Align.FlexEnd;

        // Altimeter on the left edge: the key read-out for judging height in an isometric view.
        var altimeter = Column(root);
        altimeter.style.position = Position.Absolute;
        altimeter.style.left = 24;
        altimeter.style.top = 170;
        altimeter.style.alignItems = Align.Center;
        Caption(altimeter, "ALT");
        Gauge(altimeter, nameof(SkyfortHudState.Altitude), Lime(), true, 22, 280).Ticks = 10;
        BoundLabel(altimeter, nameof(SkyfortHudState.AltitudeReadout), 18, Color.white);

        var fuelRow = Row(root);
        fuelRow.style.position = Position.Absolute;
        fuelRow.style.left = 24;
        fuelRow.style.bottom = 22;
        fuelRow.style.alignItems = Align.Center;
        var fuelCaption = Caption(fuelRow, "FUEL");
        fuelCaption.style.marginRight = 10;
        Bind(fuelCaption, "style.color", nameof(SkyfortHudState.LowFuel), (ref bool low) =>
            new StyleColor(low ? Red : Color.white));
        var fuelGauge = Gauge(fuelRow, nameof(SkyfortHudState.Fuel), Gold, false, 300, 18);
        fuelGauge.WarningBelow = 0.25f;

        // Hull: each enemy hit costs a quarter; the ship is only lost when it's empty.
        var hullRow = Row(root);
        hullRow.style.position = Position.Absolute;
        hullRow.style.left = 24;
        hullRow.style.bottom = 50;
        hullRow.style.alignItems = Align.Center;
        var hullCaption = Caption(hullRow, "HULL");
        hullCaption.style.marginRight = 10;
        Bind(hullCaption, "style.color", nameof(SkyfortHudState.LowHull), (ref bool low) =>
            new StyleColor(low ? Red : Color.white));
        var hullGauge = Gauge(hullRow, nameof(SkyfortHudState.Hull), Lime(), false, 300, 18);
        hullGauge.Ticks = 4;
        hullGauge.WarningBelow = 0.3f;

        // Active timed power-ups, e.g. "SHIELD 7    SPREAD 12".
        var power = BoundLabel(root, nameof(SkyfortHudState.PowerStatus), 16, Cyan);
        power.style.position = Position.Absolute;
        power.style.left = 24;
        power.style.bottom = 80;

        var help = new Label("ARROWS/WASD fly   SPACE fire   M display mode   P pause   R restart") { pickingMode = PickingMode.Ignore };
        help.style.position = Position.Absolute;
        help.style.right = 24;
        help.style.bottom = 22;
        help.style.fontSize = 13;
        help.style.color = new Color(1f, 1f, 1f, 0.6f);
        root.Add(help);

        var bannerBox = Column(root);
        bannerBox.style.position = Position.Absolute;
        bannerBox.style.left = 0;
        bannerBox.style.right = 0;
        bannerBox.style.top = 250;
        bannerBox.style.alignItems = Align.Center;
        var banner = BoundLabel(bannerBox, nameof(SkyfortHudState.Banner), 44, Color.white);
        banner.style.unityTextOutlineColor = new Color(0f, 0.2f, 0.4f);
        banner.style.unityTextOutlineWidth = 1.5f;
        BoundLabel(bannerBox, nameof(SkyfortHudState.SubBanner), 20, Cyan);
    }

    private static Color Lime() => new(0.45f, 1f, 0.35f);

    private static VisualElement Row(VisualElement parent)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        parent.Add(row);
        return row;
    }

    private static VisualElement Column(VisualElement parent)
    {
        var column = new VisualElement { pickingMode = PickingMode.Ignore };
        column.style.flexDirection = FlexDirection.Column;
        parent.Add(column);
        return column;
    }

    private static Label Caption(VisualElement parent, string text)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.style.fontSize = 13;
        label.style.color = new Color(1f, 1f, 1f, 0.7f);
        label.style.letterSpacing = 2;
        parent.Add(label);
        return label;
    }

    private static Label Readout(VisualElement parent, string caption, string property, Color color, int size)
    {
        var block = Column(parent);
        block.style.marginBottom = 4;
        Caption(block, caption);
        return BoundLabel(block, property, size, color);
    }

    private static Label BoundLabel(VisualElement parent, string property, int size, Color color)
    {
        var label = new Label { pickingMode = PickingMode.Ignore };
        label.style.fontSize = size;
        label.style.color = color;
        label.SetBinding("text", new DataBinding
        {
            dataSourcePath = PropertyPath.FromName(property),
            bindingMode = BindingMode.ToTarget
        });
        parent.Add(label);
        return label;
    }

    private static SkyfortGauge Gauge(VisualElement parent, string property, Color color, bool vertical, float width, float height)
    {
        var gauge = new SkyfortGauge { Vertical = vertical, FillColor = color, pickingMode = PickingMode.Ignore };
        gauge.style.width = width;
        gauge.style.height = height;
        gauge.style.marginTop = 4;
        gauge.style.marginBottom = 4;
        gauge.SetBinding(nameof(SkyfortGauge.Value), new DataBinding
        {
            dataSourcePath = PropertyPath.FromName(property),
            bindingMode = BindingMode.ToTarget
        });
        parent.Add(gauge);
        return gauge;
    }

    // Binding with a local source-to-UI converter (Unity 6 ConverterGroup API), e.g. bool -> style value.
    private static void Bind<TSource, TValue>(VisualElement element, string targetProperty, string property, TypeConverter<TSource, TValue> converter)
    {
        var binding = new DataBinding
        {
            dataSourcePath = PropertyPath.FromName(property),
            bindingMode = BindingMode.ToTarget
        };
        binding.sourceToUiConverters.AddConverter(converter);
        element.SetBinding(targetProperty, binding);
    }
}
