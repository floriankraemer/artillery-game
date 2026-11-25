using Godot;
using Artillery.Core.Match;

namespace Artillery.Game.Ui;

/// <summary>Final ranking by total score.</summary>
public partial class FinalResultsScreen : Control
{
    public event Action? Done;
    private MatchState? _s;
    private bool _ready;

    public void Init(MatchState s) { _s = s; if (_ready) Build(); }

    public override void _Ready() { _ready = true; UiKit.Root(this); if (_s != null) Build(); }

    private void Build()
    {
        foreach (var c in GetChildren().Skip(1)) { RemoveChild(c); c.QueueFree(); }
        var s = _s!;
        var ranked = s.AllTanks.OrderByDescending(t => t.TotalScore).ToList();
        var center = UiKit.Centered(this);
        var panel = new PanelContainer(); center.AddChild(panel);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        v.AddThemeConstantOverride("separation", 16);
        panel.AddChild(v);
        v.AddChild(UiKit.Lbl("Final Results", "Title", HorizontalAlignment.Center));
        bool tie = ranked.Count > 1 && ranked[0].TotalScore == ranked[1].TotalScore;
        v.AddChild(!tie && ranked.Count > 0
            ? UiKit.Lbl($"{ranked[0].Name} wins the war!", "Heading", HorizontalAlignment.Center, UiTheme.PlayerColor(ranked[0].Index))
            : UiKit.Lbl("No winner: the top scores are tied", "Heading", HorizontalAlignment.Center, UiTheme.Dim));
        int rank = 0;
        var rows = ranked.Select(t => (t.Index, $"{++rank}. {t.Name}", new[] { "", t.TotalScore.ToString(), "$" + t.Cash, t.Wins.ToString() }));
        v.AddChild(ResultsTable.Build(new[] { "Rank", "Score", "Cash", "Wins" }, rows));
        var btn = UiKit.Btn("Back to Title", () => Done?.Invoke(), "Primary", 220);
        btn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        v.AddChild(btn);
        btn.GrabFocus();
    }
}
