using Godot;

namespace Artillery.Game.Ui;

/// <summary>Title screen: New Game / Quit.</summary>
public partial class TitleScreen : Control
{
	public event Action? NewGameRequested;
	public event Action? QuitRequested;
	public event Action? MultiplayerRequested;
	public event Action? SettingsRequested;

	public override void _Ready()
	{
		UiKit.Root(this);
		var c = UiKit.Centered(this);
		var v = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		v.AddThemeConstantOverride("separation", 14);
		c.AddChild(v);
		v.AddChild(UiKit.Lbl("Artillery3D", "Title", HorizontalAlignment.Center));
		v.AddChild(UiKit.Lbl("Turn-based 3D artillery for 2 to 4 commanders", "Dim", HorizontalAlignment.Center));
		v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30) });
		var ng = UiKit.Btn("New Game", () => NewGameRequested?.Invoke(), "Primary");
		v.AddChild(ng);
		v.AddChild(UiKit.Btn("Multiplayer", () => MultiplayerRequested?.Invoke()));
		v.AddChild(UiKit.Btn("Settings", () => SettingsRequested?.Invoke()));
		v.AddChild(UiKit.Btn("Quit", () => QuitRequested?.Invoke()));
		_box = v;

		// Credits footer, small white text pinned to the bottom centre.
		var credits = UiKit.Lbl(
			"Based on a game by PyroSoft, James Payne and Paul Lowton",
			null, HorizontalAlignment.Center, Colors.White);
		credits.AddThemeFontSizeOverride("font_size", 12);
		credits.MouseFilter = MouseFilterEnum.Ignore;
		credits.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
		credits.OffsetTop = -52;
		credits.OffsetBottom = -10;
		credits.GrowVertical = GrowDirection.Begin;
		AddChild(credits);

		ng.GrabFocus();
	}

	/// <summary>Shows a one-line message under the buttons (e.g. why an online match ended).</summary>
	public void SetNotice(string text)
	{
		_box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
		var l = UiKit.Lbl(text, null, HorizontalAlignment.Center, UiTheme.Accent);
		l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_box.AddChild(l);
	}

	private VBoxContainer _box = null!;
}
