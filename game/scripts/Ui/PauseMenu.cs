using Godot;

namespace Artillery.Game.Ui;

/// <summary>Pause overlay with confirmed destructive actions and persistent settings sliders.</summary>
public partial class PauseMenu : Control
{
    public event Action? Resume;
    public event Action? SelfDestruct;
    public event Action? EndRound;
    public event Action? EndGame;
    public event Action? QuitToTitle;
    /// <summary>Online match: the game keeps running behind the menu, End Round/End Game are not offered, quitting leaves for everyone.</summary>
    public bool Online { get; set; }

    private VBoxContainer _main = null!, _confirm = null!;
    private Label _confirmText = null!;
    private Action? _pending;

    public override void _Ready()
    {
        UiSettings.EnsureLoaded();
        Theme = UiTheme.Get();
        this.Full();
        ProcessMode = ProcessModeEnum.Always;
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.6f) }.Full());
        var center = UiKit.Centered(this);
        var panel = new PanelContainer(); center.AddChild(panel);
        var stack = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        panel.AddChild(stack);
        stack.AddChild(UiKit.Lbl("Paused", "Heading", HorizontalAlignment.Center));

        _main = new VBoxContainer(); stack.AddChild(_main);
        var resume = UiKit.Btn("Resume", () => Resume?.Invoke(), "Primary");
        _main.AddChild(resume);
        _main.AddChild(UiKit.Btn("Self-Destruct", () => Ask("Destroy your own tank?", () => SelfDestruct?.Invoke())));
        if (!Online) _main.AddChild(UiKit.Btn("End Round", () => Ask("End the current round now?", () => EndRound?.Invoke())));
        if (!Online) _main.AddChild(UiKit.Btn("End Game", () => Ask("End the whole game and show final results?", () => EndGame?.Invoke())));
        _main.AddChild(UiKit.Btn("Quit to Title", () => Ask(Online ? "Leave the online match? The match ends for all players." : "Leave the match and return to the title screen?", () => QuitToTitle?.Invoke()), "Danger"));
        _main.AddChild(new HSeparator());
        _main.AddChild(UiKit.Lbl("Settings", "Dim"));

        var g = new GridContainer { Columns = 3 };
        g.AddThemeConstantOverride("h_separation", 12);
        _main.AddChild(g);
        var speedVal = UiKit.Cell("", UiTheme.Accent, true, 56);
        var speed = Slider(g, "Animation speed", 0.25, 4, 0.25, UiSettings.AnimationSpeed, speedVal, v => { UiSettings.AnimationSpeed = (float)v; speedVal.Text = $"{v:0.##}x"; });
        g.AddChild(UiKit.Lbl("Camera follow"));
        var follow = new CheckBox { Text = "Follow projectile", ButtonPressed = UiSettings.FollowProjectile };
        follow.Toggled += on => { UiSettings.FollowProjectile = on; UiSettings.Save(); };
        g.AddChild(follow); g.AddChild(new Control());
        var volVal = UiKit.Cell("", UiTheme.Accent, true, 56);
        Slider(g, "Master volume", 0, 1, 0.05, UiSettings.MasterVolume, volVal, v => { UiSettings.MasterVolume = (float)v; volVal.Text = $"{(int)Math.Round(v * 100)}%"; });

        _confirm = new VBoxContainer { Visible = false }; stack.AddChild(_confirm);
        _confirmText = UiKit.Lbl("", null, HorizontalAlignment.Center);
        _confirmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _confirm.AddChild(_confirmText);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; _confirm.AddChild(row);
        row.AddChild(UiKit.Btn("Yes", () => { var a = _pending; Cancel(); a?.Invoke(); }, "Danger", 120));
        row.AddChild(UiKit.Btn("No", Cancel, null, 120));
        resume.GrabFocus();
    }

    private HSlider Slider(GridContainer g, string label, double min, double max, double step, double value, Label valLbl, Action<double> set)
    {
        g.AddChild(UiKit.Lbl(label));
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(180, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        s.ValueChanged += v => set(v);
        s.DragEnded += _ => UiSettings.Save();
        g.AddChild(s); g.AddChild(valLbl);
        set(value);
        return s;
    }

    private void Ask(string text, Action a)
    {
        _pending = a; _confirmText.Text = text;
        _main.Visible = false; _confirm.Visible = true;
    }

    private void Cancel() { _pending = null; _confirm.Visible = false; _main.Visible = true; }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Visible && _confirm.Visible && e.IsActionPressed("menu") && !e.IsEcho())
        {
            GetViewport().SetInputAsHandled();
            Cancel();
        }
    }
}
