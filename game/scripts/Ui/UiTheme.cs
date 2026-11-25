using Godot;

namespace Artillery.Game.Ui;

/// <summary>Shared look: dark slate panels, amber accent. Loads res://theme/main_theme.tres (baked from <see cref="Build"/>).</summary>
public static class UiTheme
{
    public static readonly Color Bg = new(0.07f, 0.09f, 0.11f), Panel = new(0.12f, 0.15f, 0.18f), PanelHi = new(0.17f, 0.21f, 0.25f),
        Border = new(0.28f, 0.34f, 0.40f), Text = new(0.90f, 0.93f, 0.95f), Dim = new(0.58f, 0.64f, 0.70f), Accent = new(0.95f, 0.70f, 0.20f),
        Good = new(0.45f, 0.85f, 0.50f), Bad = new(0.95f, 0.40f, 0.40f);

    public static Color PlayerColor(int index) => Math.Clamp(index, 1, 4) switch
    {
        1 => new Color(0.36f, 0.58f, 1f), 2 => new Color(1f, 0.38f, 0.38f), 3 => new Color(0.40f, 0.85f, 0.42f), _ => new Color(0.82f, 0.45f, 0.92f),
    };

    private static Theme? _theme;
    public const string Path = "res://theme/main_theme.tres";

    public static Theme Get()
    {
        if (_theme != null) return _theme;
        _theme = ResourceLoader.Exists(Path) ? GD.Load<Theme>(Path) : null;
        return _theme ??= Build();
    }

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 4, int pad = 8, int bw = 1)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg, BorderColor = border ?? bg, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = pad, ContentMarginRight = pad,
            ContentMarginTop = pad * 0.6f, ContentMarginBottom = pad * 0.6f,
        };
        s.SetBorderWidthAll(border.HasValue ? bw : 0);
        return s;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 18 };
        var dark = new Color(0.05f, 0.07f, 0.09f);
        foreach (var c in new[] { "Label", "Button", "CheckBox", "OptionButton", "LineEdit", "RichTextLabel", "TooltipLabel", "PopupMenu" })
            t.SetColor("font_color", c, Text);
        foreach (var c in new[] { "Button", "OptionButton", "CheckBox", "PopupMenu" }) t.SetColor("font_hover_color", c, Colors.White);
        foreach (var c in new[] { "Button", "OptionButton", "CheckBox" }) t.SetColor("font_pressed_color", c, Accent);
        t.SetColor("font_disabled_color", "Button", new Color(0.42f, 0.46f, 0.50f));
        t.SetColor("caret_color", "LineEdit", Accent);
        t.SetColor("font_placeholder_color", "LineEdit", Dim);
        foreach (var b in new[] { "Button", "OptionButton" })
        {
            t.SetStylebox("normal", b, Box(Panel, Border));
            t.SetStylebox("hover", b, Box(PanelHi, Accent));
            t.SetStylebox("pressed", b, Box(new Color(0.22f, 0.26f, 0.30f), Accent));
            t.SetStylebox("disabled", b, Box(new Color(0.10f, 0.12f, 0.14f), new Color(0.18f, 0.21f, 0.24f)));
            t.SetStylebox("focus", b, Box(new Color(0, 0, 0, 0), Accent));
        }
        foreach (var (n, on) in new[] { ("checked", true), ("unchecked", false), ("checked_disabled", true), ("unchecked_disabled", false) })
            t.SetIcon(n, "CheckBox", BoxIcon(on));
        var clear = Box(new Color(0, 0, 0, 0));
        t.SetStylebox("normal", "CheckBox", clear);
        t.SetStylebox("pressed", "CheckBox", clear);
        t.SetStylebox("disabled", "CheckBox", clear);
        t.SetStylebox("hover", "CheckBox", Box(new Color(1, 1, 1, 0.05f)));
        t.SetStylebox("hover_pressed", "CheckBox", Box(new Color(1, 1, 1, 0.05f)));
        t.SetStylebox("focus", "CheckBox", Box(new Color(0, 0, 0, 0), Accent));
        t.SetStylebox("normal", "LineEdit", Box(dark, Border));
        t.SetStylebox("focus", "LineEdit", Box(dark, Accent));
        t.SetStylebox("read_only", "LineEdit", Box(dark, Border));
        t.SetStylebox("panel", "PanelContainer", Box(Panel, Border, 6, 14));
        t.SetStylebox("panel", "Panel", Box(Panel, Border, 6, 14));
        t.SetStylebox("panel", "PopupPanel", Box(Panel, Border, 4, 4));
        t.SetStylebox("panel", "TooltipPanel", Box(dark, Accent, 4, 10));
        t.SetStylebox("panel", "PopupMenu", Box(Panel, Border, 4, 4));
        t.SetStylebox("hover", "PopupMenu", Box(PanelHi, null, 2, 4));
        var track = Box(dark, Border, 2, 0); track.ContentMarginTop = 5; track.ContentMarginBottom = 5;
        var fillBox = Box(Accent, null, 2, 0); fillBox.ContentMarginTop = 5; fillBox.ContentMarginBottom = 5;
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", fillBox);
        t.SetStylebox("grabber_area_highlight", "HSlider", fillBox);
        t.SetConstant("separation", "VBoxContainer", 8);
        t.SetConstant("separation", "HBoxContainer", 8);
        t.SetConstant("h_separation", "GridContainer", 8);
        t.SetConstant("v_separation", "GridContainer", 6);
        t.SetTypeVariation("Title", "Label"); t.SetFontSize("font_size", "Title", 56); t.SetColor("font_color", "Title", Accent);
        t.SetTypeVariation("Heading", "Label"); t.SetFontSize("font_size", "Heading", 28);
        t.SetTypeVariation("Dim", "Label"); t.SetColor("font_color", "Dim", Dim);
        t.SetTypeVariation("Primary", "Button");
        t.SetStylebox("normal", "Primary", Box(new Color(0.55f, 0.38f, 0.08f), Accent, 4, 14));
        t.SetStylebox("hover", "Primary", Box(new Color(0.70f, 0.48f, 0.10f), Colors.White, 4, 14));
        t.SetStylebox("pressed", "Primary", Box(new Color(0.45f, 0.30f, 0.06f), Accent, 4, 14));
        t.SetFontSize("font_size", "Primary", 22);
        t.SetTypeVariation("Danger", "Button");
        t.SetStylebox("normal", "Danger", Box(new Color(0.35f, 0.12f, 0.12f), Bad, 4, 14));
        t.SetStylebox("hover", "Danger", Box(new Color(0.50f, 0.16f, 0.16f), Colors.White, 4, 14));
        return t;
    }

    private static ImageTexture BoxIcon(bool on)
    {
        const int n = 22;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        {
            bool edge = x < 2 || y < 2 || x >= n - 2 || y >= n - 2;
            bool fill = on && x >= 5 && y >= 5 && x < n - 5 && y < n - 5;
            img.SetPixel(x, y, edge ? Border.Lerp(Colors.White, 0.4f) : fill ? Accent : new Color(0.05f, 0.07f, 0.09f));
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Saves the code-built theme to res://theme/main_theme.tres.</summary>
    public static void Bake() => ResourceSaver.Save(Build(), Path);
}

/// <summary>Small control factory helpers shared by the screens.</summary>
public static class UiKit
{
    public static Label Lbl(string text, string? variation = null, HorizontalAlignment al = HorizontalAlignment.Left, Color? col = null)
    {
        var l = new Label { Text = text, HorizontalAlignment = al };
        if (variation != null) l.ThemeTypeVariation = variation;
        if (col.HasValue) l.AddThemeColorOverride("font_color", col.Value);
        return l;
    }

    public static Button Btn(string text, Action onClick, string? variation = null, float minW = 0)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minW, 40) };
        if (variation != null) b.ThemeTypeVariation = variation;
        b.Pressed += onClick;
        return b;
    }

    public static ColorRect Swatch(Color c, float size = 22) => new() { Color = c, CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };

    public static T Full<T>(this T c) where T : Control { c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); return c; }

    /// <summary>Applies the shared theme, full-rect anchors and an optional dark background to a screen.</summary>
    public static void Root(Control host, bool background = true)
    {
        host.Theme = UiTheme.Get();
        host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        host.GrowHorizontal = Control.GrowDirection.Both; host.GrowVertical = Control.GrowDirection.Both;
        if (background) host.AddChild(new ColorRect { Color = UiTheme.Bg, MouseFilter = Control.MouseFilterEnum.Ignore }.Full());
    }

    public static CenterContainer Centered(Control host)
    {
        var c = new CenterContainer().Full();
        host.AddChild(c);
        return c;
    }

    public static void Clear(Node n) { foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); } }

    /// <summary>Table cell label.</summary>
    public static Label Cell(string text, Color? col = null, bool right = true, float minW = 0)
    {
        var l = Lbl(text, null, right ? HorizontalAlignment.Right : HorizontalAlignment.Left, col);
        l.CustomMinimumSize = new Vector2(minW, 0);
        return l;
    }
}
