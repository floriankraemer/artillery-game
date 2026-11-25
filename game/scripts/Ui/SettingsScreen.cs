using Godot;

namespace Artillery.Game.Ui;

/// <summary>Settings from the title screen: camera follow, animation speed, volume. Saved immediately to user://settings.cfg.</summary>
public partial class SettingsScreen : Control
{
    public event Action? BackRequested;

    public override void _Ready()
    {
        UiSettings.EnsureLoaded();
        UiKit.Root(this);
        var center = UiKit.Centered(this);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(480, 0) };
        var sb = UiTheme.Box(UiTheme.Panel, UiTheme.Accent, 8, 28, 2);
        sb.ContentMarginTop = 22; sb.ContentMarginBottom = 22;
        frame.AddThemeStyleboxOverride("panel", sb);
        center.AddChild(frame);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        frame.AddChild(v);

        v.AddChild(UiKit.Lbl("Settings", "Title", HorizontalAlignment.Center));
        v.AddChild(new HSeparator());

        var follow = new CheckBox { Text = "Camera follows the projectile", ButtonPressed = UiSettings.FollowProjectile };
        follow.Toggled += on => { UiSettings.FollowProjectile = on; UiSettings.Save(); };
        v.AddChild(follow);
        v.AddChild(UiKit.Lbl("Off: the camera stays where you put it during shots.", "Dim"));

        var g = new GridContainer { Columns = 3 };
        g.AddThemeConstantOverride("h_separation", 12);
        g.AddThemeConstantOverride("v_separation", 10);
        v.AddChild(g);
        var speedVal = UiKit.Cell("", UiTheme.Accent, true, 56);
        Slider(g, "Animation speed", 0.25, 4, 0.25, UiSettings.AnimationSpeed, speedVal, x => { UiSettings.AnimationSpeed = (float)x; speedVal.Text = $"{x:0.##}x"; });
        var volVal = UiKit.Cell("", UiTheme.Accent, true, 56);
        Slider(g, "Master volume", 0, 1, 0.05, UiSettings.MasterVolume, volVal, x => { UiSettings.MasterVolume = (float)x; volVal.Text = $"{(int)Math.Round(x * 100)}%"; });

        v.AddChild(new HSeparator());
        var back = UiKit.Btn("Back", () => BackRequested?.Invoke(), "Primary");
        v.AddChild(back);
        back.GrabFocus();
    }

    private static void Slider(GridContainer g, string label, double min, double max, double step, double value, Label valLbl, Action<double> set)
    {
        g.AddChild(UiKit.Lbl(label));
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(180, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        s.ValueChanged += x => set(x);
        s.DragEnded += _ => UiSettings.Save();
        g.AddChild(s); g.AddChild(valLbl);
        set(value);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("menu")) { GetViewport().SetInputAsHandled(); BackRequested?.Invoke(); }
    }
}
