using Godot;
using Artillery.Core.Items;
using Artillery.Core.Match;

namespace Artillery.Game.Ui;

/// <summary>
/// Dev harness: Tab cycles every screen with a demo match. User args (after "--"):
/// --screen=title|setup|shop|round|final|pause  --shot=ABS.png (save and quit)  --bake-theme (write res://theme/main_theme.tres and quit).
/// Screenshots need a rendering window (not --headless: the dummy renderer has no pixels).
/// </summary>
public partial class UiGallery : Control
{
    private static readonly string[] Names = { "title", "setup", "shop", "round", "final", "pause" };
    private int _idx;
    private Control? _current;
    private MatchEngine _engine = null!;
    private string? _shot;
    private int _frames;
    private Label _hint = null!;

    public override void _Ready()
    {
        string? screen = null;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shot = a[7..];
            else if (a.StartsWith("--screen=")) screen = a[9..];
            else if (a == "--bake-theme") { UiTheme.Bake(); GD.Print("theme baked"); GetTree().Quit(); return; }
        }
        Theme = UiTheme.Get();
        UiSettings.Load();
        var settings = new MatchSettings
        {
            Seed = 777, Rounds = 5, StartCash = 6000,
            Players = new List<PlayerSetup> { new("Alice", PlayerType.Human), new("Bob", PlayerType.Human), new("CPU 3", PlayerType.Goldfish), new("Fox", PlayerType.Fox) },
            Landscapes = new HashSet<int> { 14, 20, 13 },
        };
        _engine = new MatchEngine(settings);
        _engine.StartMatch();
        var s = _engine.State;
        s[1].Cash = 3800; Shop.Buy(s[1], WeaponId.Missile); Shop.Buy(s[1], WeaponId.Missile); Shop.Buy(s[1], WeaponId.Nuke);
        Shop.Buy(s[1], UtilityId.ShieldGenerator); Shop.Buy(s[1], CasingId.Bouncing); Shop.Buy(s[1], UtilityId.Parachute);
        s.Round = 2; s.RoundWinner = 2;
        s[1].TotalScore = 340; s[2].TotalScore = 1210; s[2].Wins = 1; s[3].TotalScore = 95; s[4].TotalScore = 410; s[4].Cash = 7420;
        _hint = UiKit.Lbl("Tab: next screen", "Dim");
        _hint.ZIndex = 100;
        _hint.Position = new Vector2(8, 4);
        _idx = Math.Max(0, Array.IndexOf(Names, screen ?? "title"));
        Show(_idx);
        if (_shot != null) { _hint.Visible = false; }
    }

    private void Show(int i)
    {
        _idx = (i % Names.Length + Names.Length) % Names.Length;
        if (_current != null) { RemoveChild(_current); _current.QueueFree(); }
        var s = _engine.State;
        Control c;
        switch (Names[_idx])
        {
            case "title":
                c = Load<TitleScreen>("TitleScreen");
                break;
            case "setup":
                var su = Load<SetupScreen>("SetupScreen");
                su.Init(new MatchSettings { Landscapes = new HashSet<int> { 14, 20, 13, 1 }, Players = new List<PlayerSetup> { new("Alice", PlayerType.Human), new("", PlayerType.Goldfish), new("", PlayerType.Owl) } });
                su.StartRequested += m => GD.Print("Start " + m.Players.Count + " players");
                c = su;
                Callable.From(su.ShowPreview).CallDeferred();
                break;
            case "shop":
                var sh = Load<ShopScreen>("ShopScreen");
                sh.Init(s, s[1]);
                c = sh;
                break;
            case "round":
                var rr = Load<RoundResultsScreen>("RoundResultsScreen");
                s.Round = 2; s.RoundWinner = 2;
                s[1].TotalScore = 340; s[2].TotalScore = 1210; s[2].Wins = 1; s[3].TotalScore = 95; s[4].TotalScore = 410; s[4].Cash = 7420;
                rr.Init(s, new List<(int, long)> { (1, 120), (2, 910), (3, -15), (4, 260) });
                c = rr;
                break;
            case "final":
                var fr = Load<FinalResultsScreen>("FinalResultsScreen");
                fr.Init(s);
                c = fr;
                break;
            default:
                var pm = Load<PauseMenu>("PauseMenu");
                c = pm;
                break;
        }
        _current = c;
        AddChild(c);
        MoveChild(c, 0);
        if (_hint.GetParent() == null) AddChild(_hint);
        _frames = 0;
    }

    private static T Load<T>(string scene) where T : Control => GD.Load<PackedScene>($"res://scenes/Ui/{scene}.tscn").Instantiate<T>();

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab }) { Show(_idx + 1); GetViewport().SetInputAsHandled(); }
    }

    public override void _Process(double delta)
    {
        if (_shot == null || ++_frames < 25) return;
        var img = GetViewport().GetTexture()?.GetImage();
        if (img == null || img.IsEmpty()) { GD.PrintErr("No image (headless renderer?)"); GetTree().Quit(1); return; }
        img.SavePng(_shot);
        GD.Print("saved " + _shot);
        GetTree().Quit();
    }
}
