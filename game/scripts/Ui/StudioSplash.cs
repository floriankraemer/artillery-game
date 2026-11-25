using Godot;

namespace Artillery.Game.Ui;

/// <summary>Studio splash shown once at start-up (engine boot logo is off; black background matches). Fades in, holds, fades out. Any key or click skips it.</summary>
public partial class StudioSplash : Control
{
    private const string LogoPath = "res://art/ui/studio_logo.png";
    public const string StudioName = "High Oak Games Studio";

    public event Action? Finished;
    private Control _content = null!;
    private bool _done;

    public override void _Ready()
    {
        var bg = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        SetAnchorsPreset(LayoutPreset.FullRect);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            Modulate = new Color(1, 1, 1, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 28);
        _content = column;
        column.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(LogoPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            CustomMinimumSize = new Vector2(0, GetViewportRect().Size.Y * 0.42f),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var label = new Label { Text = StudioName, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 56);
        label.AddThemeColorOverride("font_color", Colors.White);
        column.AddChild(label);
        AddChild(column);

        var t = CreateTween();
        t.TweenProperty(_content, "modulate:a", 1.0f, 0.8f);
        t.TweenInterval(1.6f);
        t.TweenProperty(_content, "modulate:a", 0.0f, 0.8f);
        t.TweenCallback(Callable.From(Leave));
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } || e is InputEventMouseButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            Leave();
        }
    }

    private void Leave()
    {
        if (_done) return;
        _done = true;
        Finished?.Invoke();
    }
}
