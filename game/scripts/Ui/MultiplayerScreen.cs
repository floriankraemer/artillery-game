using Godot;
using Artillery.Core.Match;
using Artillery.Game.Net;

namespace Artillery.Game.Ui;

/// <summary>
/// Online play: host/join form, then the lobby (member list, match summary, host controls). The match itself starts through
/// <see cref="NetSession.MatchStarting"/>, which Main listens to. Rebuilds from the session state, so it can be re-shown after the settings screen.
/// </summary>
public partial class MultiplayerScreen : Control
{
    public event Action? BackRequested;
    /// <summary>Host pressed "Match Settings".</summary>
    public event Action? ConfigureRequested;

    private NetSession _net = null!;
    private bool _lobbyView;
    private bool _connecting;
    private string _error = "";
    private int _attempt;

    private LineEdit _name = null!, _addr = null!;
    private SpinBox _hostPort = null!, _joinPort = null!;
    private Label _status = null!;
    private Button _hostBtn = null!, _joinBtn = null!;
    private Control _body = null!;

    public MultiplayerScreen Init(NetSession net) { _net = net; return this; }

    public override void _Ready()
    {
        UiKit.Root(this);
        _body = UiKit.Centered(this);
        _net.LobbyChanged += OnLobbyChanged;
        _net.ConnectFailed += OnConnectFailed;
        _net.LobbyClosed += OnLobbyClosed;
        _lobbyView = _net.IsActive && _net.Members.Count > 0;
        Rebuild();
    }

    public override void _ExitTree()
    {
        _net.LobbyChanged -= OnLobbyChanged;
        _net.ConnectFailed -= OnConnectFailed;
        _net.LobbyClosed -= OnLobbyClosed;
    }

    private void Rebuild()
    {
        UiKit.Clear(_body);
        if (_lobbyView) BuildLobby(); else BuildForm();
    }

    // ---- shared look --------------------------------------------------------------------

    /// <summary>Framed card: the single centered panel every state of this screen lives in.</summary>
    private VBoxContainer Card(float width)
    {
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        var sb = UiTheme.Box(UiTheme.Panel, UiTheme.Accent, 8, 28, 2);
        sb.ContentMarginTop = 22; sb.ContentMarginBottom = 22;
        frame.AddThemeStyleboxOverride("panel", sb);
        _body.AddChild(frame);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        frame.AddChild(v);
        return v;
    }

    /// <summary>Darker inset box for a group of related rows.</summary>
    private static VBoxContainer Inset(Control parent)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Bg, UiTheme.Border, 6, 12));
        parent.AddChild(p);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        p.AddChild(v);
        return v;
    }

    private static void Row(Control parent, string label, Control field, float labelW = 90)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 10);
        var l = UiKit.Lbl(label); l.CustomMinimumSize = new Vector2(labelW, 0); l.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        r.AddChild(l);
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        r.AddChild(field);
        parent.AddChild(r);
    }

    // ---- form ---------------------------------------------------------------------------

    private bool _joinTab;
    private Control _hostPane = null!, _joinPane = null!;
    private Button _tabHost = null!, _tabJoin = null!;

    private void BuildForm()
    {
        var v = Card(480);
        v.AddChild(UiKit.Lbl("Multiplayer", "Title", HorizontalAlignment.Center));
        var sub = UiKit.Lbl("Play over a direct connection. One player hosts, up to 3 others join.", "Dim", HorizontalAlignment.Center);
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(sub);
        v.AddChild(new HSeparator());

        _name = new LineEdit { Text = NetPrefs.Name, MaxLength = 12, PlaceholderText = NetPrefs.DefaultName() };
        Row(v, "Your name", _name);

        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 8); v.AddChild(tabs);
        _tabHost = new Button { Text = "Host a game", ToggleMode = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 40) };
        _tabJoin = new Button { Text = "Join a game", ToggleMode = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 40) };
        _tabHost.Pressed += () => SelectTab(false);
        _tabJoin.Pressed += () => SelectTab(true);
        tabs.AddChild(_tabHost); tabs.AddChild(_tabJoin);

        var box = new VBoxContainer(); v.AddChild(box);
        var hp = Inset(box); _hostPane = (Control)hp.GetParent();
        _hostPort = new SpinBox { MinValue = 1024, MaxValue = 65535, Step = 1, Value = NetPrefs.Port };
        Row(hp, "Port", _hostPort);
        var hint = UiKit.Lbl("You become player 1 and choose the match settings in the lobby. For internet play, forward this UDP port on your router.", "Dim");
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hp.AddChild(hint);
        _hostBtn = UiKit.Btn("Open Lobby", OnHost, "Primary"); hp.AddChild(_hostBtn);

        var jp = Inset(box); _joinPane = (Control)jp.GetParent();
        _addr = new LineEdit { Text = NetPrefs.Address, PlaceholderText = "host IP address or name" };
        Row(jp, "Address", _addr);
        _joinPort = new SpinBox { MinValue = 1024, MaxValue = 65535, Step = 1, Value = NetPrefs.Port };
        Row(jp, "Port", _joinPort);
        _addr.TextSubmitted += _ => OnJoin();
        _joinBtn = UiKit.Btn("Connect", OnJoin, "Primary"); jp.AddChild(_joinBtn);

        _status = UiKit.Lbl(_error, null, HorizontalAlignment.Center, UiTheme.Bad);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(_status);
        v.AddChild(new HSeparator());
        v.AddChild(UiKit.Btn("Back", () => BackRequested?.Invoke()));
        SelectTab(_joinTab);
        _name.GrabFocus();
        SetBusy(_connecting);
    }

    private void SelectTab(bool join)
    {
        _joinTab = join;
        _tabHost.SetPressedNoSignal(!join); _tabJoin.SetPressedNoSignal(join);
        _hostPane.Visible = !join; _joinPane.Visible = join;
    }

    private void SetBusy(bool busy)
    {
        _connecting = busy;
        if (_hostBtn == null || !IsInstanceValid(_hostBtn)) return;
        _hostBtn.Disabled = busy; _joinBtn.Disabled = busy;
        if (busy) { _status.Text = "Connecting…"; _status.AddThemeColorOverride("font_color", UiTheme.Dim); }
        else _status.AddThemeColorOverride("font_color", UiTheme.Bad);
    }

    private void SavePrefs(bool join)
    {
        NetPrefs.Name = _name.Text.Trim();
        if (join) { NetPrefs.Address = _addr.Text.Trim(); NetPrefs.Port = (int)_joinPort.Value; }
        else NetPrefs.Port = (int)_hostPort.Value;
        NetPrefs.Save();
    }

    private string LocalName() => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : NetPrefs.DefaultName();

    private void OnHost()
    {
        SavePrefs(false);
        var err = _net.Host((int)_hostPort.Value, LocalName());
        if (err != Error.Ok) { _error = $"Could not open port {(int)_hostPort.Value} ({err})."; _status.Text = _error; return; }
        _error = ""; _lobbyView = true; Rebuild();
    }

    private void OnJoin()
    {
        SavePrefs(true);
        if (_addr.Text.Trim().Length == 0) { _status.Text = "Enter the host address."; return; }
        var err = _net.Join(_addr.Text.Trim(), (int)_joinPort.Value, LocalName());
        if (err != Error.Ok) { _error = $"Could not connect ({err})."; _status.Text = _error; return; }
        SetBusy(true);
        int attempt = ++_attempt;
        string target = $"{_addr.Text.Trim()}:{(int)_joinPort.Value}";
        GetTree().CreateTimer(10).Timeout += () =>
        {
            if (attempt != _attempt || !_connecting || _lobbyView) return;
            _net.Disconnect();
            Fail($"No answer from {target} after 10 s.");
        };
    }

    private void Fail(string msg)
    {
        _error = msg; _lobbyView = false; _connecting = false;
        Rebuild();
    }

    private void OnConnectFailed() => Fail("Connection failed. Check address, port and firewall.");
    private void OnLobbyClosed(string why) => Fail(why);

    // ---- lobby --------------------------------------------------------------------------

    private void OnLobbyChanged()
    {
        if (!_lobbyView && _net.IsActive && _net.Members.Count > 0) { _connecting = false; _error = ""; _lobbyView = true; }
        if (_lobbyView) Rebuild();
    }

    private void BuildLobby()
    {
        var v = Card(620);
        v.AddChild(UiKit.Lbl("Lobby", "Title", HorizontalAlignment.Center));
        if (_net.IsHost)
        {
            var addr = $"{NetSession.LocalAddressHint()}:{_net.Port}";
            var ar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; v.AddChild(ar);
            ar.AddThemeConstantOverride("separation", 10);
            ar.AddChild(UiKit.Lbl("Players join at", "Dim"));
            ar.AddChild(UiKit.Lbl(addr, null, HorizontalAlignment.Left, UiTheme.Accent));
            ar.AddChild(UiKit.Btn("Copy", () => DisplayServer.ClipboardSet(addr), null, 80));
        }
        else v.AddChild(UiKit.Lbl("Connected. The host starts the match.", "Dim", HorizontalAlignment.Center));
        v.AddChild(new HSeparator());

        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 14); v.AddChild(cols);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        left.AddChild(UiKit.Lbl($"Players  {_net.Members.Count}/{NetSession.MaxMembers}", "Heading"));
        var mv = Inset(left);
        for (int i = 0; i < _net.Members.Count; i++)
        {
            var m = _net.Members[i];
            bool me = m.PeerId == _net.LocalPeerId;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); mv.AddChild(row);
            row.AddChild(UiKit.Swatch(UiTheme.PlayerColor(i + 1), 22));
            var n = UiKit.Lbl(m.Name, null, HorizontalAlignment.Left, me ? UiTheme.Accent : null); n.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(n);
            row.AddChild(UiKit.Lbl(i == 0 ? (me ? "host (you)" : "host") : me ? "you" : "", "Dim"));
        }
        for (int i = _net.Members.Count; i < NetSession.MaxMembers; i++)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); mv.AddChild(row);
            row.AddChild(UiKit.Swatch(UiTheme.Border, 22));
            row.AddChild(UiKit.Lbl("open slot", "Dim"));
        }

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        right.AddChild(UiKit.Lbl("Match", "Heading"));
        var sv = Inset(right);
        var s = _net.Settings;
        if (s == null) sv.AddChild(UiKit.Lbl(_net.IsHost ? "Not configured yet.\nPress Match Settings." : "The host is configuring the match…", "Dim"));
        else
        {
            sv.AddChild(UiKit.Lbl($"{s.Rounds} rounds · {s.MaxShots} shots · ${s.StartCash}"));
            sv.AddChild(UiKit.Lbl($"Air resistance {(s.AirResistance ? "on" : "off")} · {s.Landscapes.Count} landscape(s)", "Dim"));
            sv.AddChild(new HSeparator());
            for (int i = 0; i < s.Players.Count; i++)
            {
                var p = s.Players[i];
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); sv.AddChild(row);
                row.AddChild(UiKit.Swatch(UiTheme.PlayerColor(i + 1), 18));
                bool human = p.Type == PlayerType.Human;
                row.AddChild(UiKit.Lbl(human ? "Online player" : $"Computer ({p.Type})", null, HorizontalAlignment.Left, human ? null : UiTheme.Dim));
            }
        }

        var blocker = _net.IsHost ? _net.StartBlocker() : null;
        var st = UiKit.Lbl(blocker ?? (_net.IsHost ? "Ready to start." : ""), null, HorizontalAlignment.Center, blocker == null ? UiTheme.Good : UiTheme.Dim);
        st.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(st);
        v.AddChild(new HSeparator());

        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 10); v.AddChild(bar);
        bar.AddChild(UiKit.Btn(_net.IsHost ? "Close Lobby" : "Leave", Leave, null, 130));
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        if (_net.IsHost)
        {
            bar.AddChild(UiKit.Btn("Match Settings", () => ConfigureRequested?.Invoke(), null, 160));
            var start = UiKit.Btn("Start Match", () => _net.StartMatch(), "Primary", 170);
            start.Disabled = blocker != null;
            bar.AddChild(start);
        }
    }

    private void Leave()
    {
        _net.Disconnect();
        _lobbyView = false; _connecting = false; _error = "";
        BackRequested?.Invoke();
    }
}

/// <summary>Remembers the player name and last join address between sessions (user://lobby.cfg).</summary>
public static class NetPrefs
{
    private const string FilePath = "user://lobby.cfg";
    public static string Name { get; set; } = "";
    public static string Address { get; set; } = "";
    public static int Port { get; set; } = NetSession.DefaultPort;
    private static bool _loaded;

    public static string DefaultName()
    {
        Load();
        if (Name.Length > 0) return Name;
        try { var u = System.Environment.UserName.Trim(); return u.Length > 0 ? u : "Player"; } catch (Exception) { return "Player"; }
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cf = new ConfigFile();
        if (cf.Load(FilePath) != Error.Ok) return;
        Name = (string)cf.GetValue("player", "name", "");
        Address = (string)cf.GetValue("join", "address", "");
        Port = (int)cf.GetValue("join", "port", NetSession.DefaultPort);
    }

    public static void Save()
    {
        var cf = new ConfigFile();
        cf.SetValue("player", "name", Name); cf.SetValue("join", "address", Address); cf.SetValue("join", "port", Port);
        cf.Save(FilePath);
    }

    static NetPrefs() { Load(); }
}
