using Godot;
using Artillery.Core.Match;

namespace Artillery.Game.Ui;

internal static class ResultsTable
{
    /// <summary>Builds a header + rows grid. Columns after the first two are right-aligned numbers.</summary>
    public static GridContainer Build(string[] headers, IEnumerable<(int tank, string name, string[] cells)> rows)
    {
        var g = new GridContainer { Columns = headers.Length + 1 };
        g.AddThemeConstantOverride("h_separation", 28);
        g.AddThemeConstantOverride("v_separation", 10);
        g.AddChild(new Control());
        for (int i = 0; i < headers.Length; i++)
            g.AddChild(UiKit.Cell(headers[i], UiTheme.Dim, i > 0, i > 0 ? 100 : 200));
        foreach (var (tank, name, cells) in rows)
        {
            g.AddChild(UiKit.Swatch(UiTheme.PlayerColor(tank), 22));
            g.AddChild(UiKit.Cell(name, UiTheme.PlayerColor(tank), false, 200));
            for (int i = 1; i < cells.Length; i++) g.AddChild(UiKit.Cell(cells[i], null, true, 100));
        }
        return g;
    }
}

/// <summary>End-of-round summary table.</summary>
public partial class RoundResultsScreen : Control
{
    public event Action? Continue;
    private MatchState? _s;
    private IReadOnlyList<(int tank, long roundScore)> _scores = Array.Empty<(int, long)>();
    private bool _ready;

    /// <param name="roundScores">Round scores captured before Economy.EndRound zeroed them.</param>
    public void Init(MatchState s, IReadOnlyList<(int tank, long roundScore)> roundScores)
    {
        _s = s; _scores = roundScores;
        if (_ready) Build();
    }

    public override void _Ready() { _ready = true; UiKit.Root(this); if (_s != null) Build(); }

    private void Build()
    {
        foreach (var c in GetChildren().Skip(1)) { RemoveChild(c); c.QueueFree(); }
        var s = _s!;
        var center = UiKit.Centered(this);
        var panel = new PanelContainer(); center.AddChild(panel);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        v.AddThemeConstantOverride("separation", 16);
        panel.AddChild(v);
        v.AddChild(UiKit.Lbl($"Round {s.Round} of {s.Settings.Rounds} complete", "Heading", HorizontalAlignment.Center));
        string winner = s.RoundWinner > 0 && s.RoundWinner < s.Tanks.Count ? s[s.RoundWinner].Name : "";
        v.AddChild(winner.Length > 0
            ? UiKit.Lbl("Winner: " + winner, "Heading", HorizontalAlignment.Center, UiTheme.PlayerColor(s.RoundWinner))
            : UiKit.Lbl("No winner", "Heading", HorizontalAlignment.Center, UiTheme.Dim));
        var rows = s.AllTanks.Select(t =>
        {
            long rs = _scores.FirstOrDefault(x => x.tank == t.Index).roundScore;
            return (t.Index, t.Name, new[] { "", rs.ToString(), "$" + t.Cash, t.Wins.ToString(), t.TotalScore.ToString() });
        });
        var table = ResultsTable.Build(new[] { "Player", "Round", "Cash", "Wins", "Total" }, rows);
        v.AddChild(table);
        var btn = UiKit.Btn(s.Round >= s.Settings.Rounds ? "Final Results" : "Next Round", () => Continue?.Invoke(), "Primary", 220);
        btn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        v.AddChild(btn);
        btn.GrabFocus();
    }
}
