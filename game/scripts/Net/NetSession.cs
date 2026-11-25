using System.Globalization;
using Godot;
using Artillery.Core.Items;
using Artillery.Core.Match;

namespace Artillery.Game.Net;

/// <summary>
/// Online play over ENet (direct IP, one peer hosts), adapted from the Wargame project's NetworkManager/LobbyNode.
/// Model: the sim is deterministic (seeded TpRandom, no wall-clock), so every peer runs its own <see cref="MatchEngine"/>
/// and only *inputs* travel: shots, self-destruct and finished shop carts. The host is the hub: guests send to the host,
/// the host validates the sender owns the seat and relays to everyone else. Lives at /root/Main/Net on every peer
/// (RPC paths must match). Human seat k belongs to lobby member k (host = member 0); other seats are CPUs.
/// </summary>
public partial class NetSession : Node
{
    public const int DefaultPort = 7000;
    public const int MaxMembers = 4;

    public enum Role { None, Host, Client }

    public sealed record Member(long PeerId, string Name);

    public Role NetRole { get; private set; }
    public bool IsHost => NetRole == Role.Host;
    public bool IsActive => NetRole != Role.None;
    public string LocalName { get; set; } = "";
    public int Port { get; private set; } = DefaultPort;

    /// <summary>Lobby members in join order (index 0 = host).</summary>
    public IReadOnlyList<Member> Members => _members;
    /// <summary>Host-chosen match settings, null until the host has configured them.</summary>
    public MatchSettings? Settings { get; private set; }
    /// <summary>Seat (player index 1..n) to owning peer id; 0 = computer. Index 0 unused. Valid once a match started.</summary>
    public long[] SeatPeers { get; private set; } = Array.Empty<long>();
    public bool MatchActive { get; private set; }

    public long LocalPeerId => IsActive ? Multiplayer.GetUniqueId() : 0;
    public bool OwnsSeat(int seat) => seat >= 1 && seat < SeatPeers.Length && SeatPeers[seat] != 0 && SeatPeers[seat] == LocalPeerId;
    public bool IsRemoteHuman(int seat) => seat >= 1 && seat < SeatPeers.Length && SeatPeers[seat] != 0 && SeatPeers[seat] != LocalPeerId;

    /// <summary>Lobby (members or settings) changed.</summary>
    public event Action? LobbyChanged;
    /// <summary>Host pressed Start (also raised on the host itself).</summary>
    public event Action<MatchSettings>? MatchStarting;
    /// <summary>Guest: connect attempt failed.</summary>
    public event Action? ConnectFailed;
    /// <summary>Connection to the lobby lost while no match runs (guest: host closed it).</summary>
    public event Action<string>? LobbyClosed;
    /// <summary>A peer dropped during a match: the match cannot continue.</summary>
    public event Action<string>? Aborted;

    private readonly List<Member> _members = new();
    private readonly Dictionary<(char, int), Queue<string>> _queues = new();
    private readonly Dictionary<(char, int), TaskCompletionSource<string?>> _waiters = new();
    private bool _aborted;

    // ---- connection ---------------------------------------------------------------------

    public Error Host(int port, string name)
    {
        Disconnect();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, MaxMembers - 1);
        if (err != Error.Ok) return err;
        Multiplayer.MultiplayerPeer = peer;
        NetRole = Role.Host; Port = port; LocalName = Clean(name);
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        _members.Add(new Member(1, LocalName));
        LobbyChanged?.Invoke();
        return Error.Ok;
    }

    public Error Join(string address, int port, string name)
    {
        Disconnect();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(address, port);
        if (err != Error.Ok) return err;
        Multiplayer.MultiplayerPeer = peer;
        NetRole = Role.Client; Port = port; LocalName = Clean(name);
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        return Error.Ok;
    }

    /// <summary>Tears the connection down and forgets lobby and match state. Safe to call at any time.</summary>
    public void Disconnect()
    {
        switch (NetRole)
        {
            case Role.Host:
                Multiplayer.PeerConnected -= OnPeerConnected;
                Multiplayer.PeerDisconnected -= OnPeerDisconnected;
                break;
            case Role.Client:
                Multiplayer.ConnectedToServer -= OnConnectedToServer;
                Multiplayer.ConnectionFailed -= OnConnectionFailed;
                Multiplayer.ServerDisconnected -= OnServerDisconnected;
                break;
        }
        if (NetRole != Role.None) Multiplayer.MultiplayerPeer = null;
        NetRole = Role.None;
        _members.Clear(); Settings = null; SeatPeers = Array.Empty<long>(); MatchActive = false;
        FlushWaiters();
    }

    public static string LocalAddressHint()
    {
        foreach (var a in IP.GetLocalAddresses())
            if (a.Contains('.') && !a.StartsWith("127.") && !a.StartsWith("169.254.")) return a;
        return "127.0.0.1";
    }

    private static string Clean(string n)
    {
        n = string.Join(' ', (n ?? "").Split(new[] { ' ', ';', '|', ',', '~' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (n.Length == 0) n = "Player";
        return n.Length > 12 ? n[..12] : n;
    }

    // ---- lobby (host side) --------------------------------------------------------------

    /// <summary>Number of human seats in the given settings.</summary>
    public static int HumanSeats(MatchSettings s) => s.Players.Count(p => p.Type == PlayerType.Human);

    /// <summary>Why the host cannot start yet, or null when it can.</summary>
    public string? StartBlocker()
    {
        if (!IsHost) return "Only the host can start.";
        if (Settings == null) return "Configure the match settings first.";
        int h = HumanSeats(Settings);
        if (h < _members.Count) return $"Settings have {h} human seat(s) for {_members.Count} players. Change player types.";
        if (h > _members.Count) return $"Waiting for players: {_members.Count} of {h} human seats filled.";
        return null;
    }

    public void SetSettings(MatchSettings s)
    {
        if (!IsHost) return;
        Settings = s;
        Publish();
    }

    public bool StartMatch()
    {
        if (!IsHost || StartBlocker() != null) return false;
        var s = NameHumanSeats(Settings!);
        var seats = BuildSeats(s);
        Settings = s;
        Publish();
        Rpc(MethodName.ReceiveStart, Encode(s), string.Join(",", seats));
        BeginLocal(s, seats);
        return true;
    }

    /// <summary>Human seats take the names of the lobby members, in order.</summary>
    private MatchSettings NameHumanSeats(MatchSettings src)
    {
        var s = Clone(src);
        int k = 0;
        for (int i = 0; i < s.Players.Count; i++)
            if (s.Players[i].Type == PlayerType.Human) s.Players[i] = new PlayerSetup(_members[k++].Name, PlayerType.Human);
        return s;
    }

    private long[] BuildSeats(MatchSettings s)
    {
        var seats = new long[s.Players.Count + 1];
        int k = 0;
        for (int i = 0; i < s.Players.Count; i++)
            if (s.Players[i].Type == PlayerType.Human) seats[i + 1] = _members[k++].PeerId;
        return seats;
    }

    private void BeginLocal(MatchSettings s, long[] seats)
    {
        Settings = s; SeatPeers = seats; MatchActive = true; _aborted = false;
        _queues.Clear(); FlushWaiters();
        MatchStarting?.Invoke(s);
    }

    /// <summary>Leaving the final results: the match is over, drops no longer abort anything.</summary>
    public void EndMatch() { MatchActive = false; }

    private void Publish()
    {
        LobbyChanged?.Invoke();
        if (!IsHost) return;
        Rpc(MethodName.ReceiveLobby, string.Join(";", _members.Select(m => $"{m.PeerId},{Uri.EscapeDataString(m.Name)}")), Settings == null ? "" : Encode(Settings));
    }

    // ---- peer lifecycle -----------------------------------------------------------------

    private void OnPeerConnected(long id)
    {
        if (!IsHost) return;
        if (MatchActive || _members.Count >= MaxMembers) { (Multiplayer.MultiplayerPeer as ENetMultiplayerPeer)?.DisconnectPeer((int)id); return; }
        // seated when its Hello arrives (it carries the name)
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsHost) return;
        var m = _members.FirstOrDefault(x => x.PeerId == id);
        if (m == null) return;
        if (MatchActive) { Abort($"{m.Name} left the match."); return; }
        _members.Remove(m);
        Publish();
    }

    private void OnConnectedToServer() => RpcId(1, MethodName.Hello, LocalName);

    private void OnConnectionFailed() { Disconnect(); ConnectFailed?.Invoke(); }

    private void OnServerDisconnected()
    {
        bool inMatch = MatchActive;
        Disconnect();
        if (inMatch) { _aborted = true; FlushWaiters(); Aborted?.Invoke("The host left the match."); }
        else LobbyClosed?.Invoke("The host closed the lobby.");
    }

    private void Abort(string why)
    {
        if (_aborted) return;
        _aborted = true;
        FlushWaiters();
        Aborted?.Invoke(why);
    }

    // ---- RPCs: lobby --------------------------------------------------------------------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(string name)
    {
        if (!IsHost) return;
        long id = Multiplayer.GetRemoteSenderId();
        if (MatchActive || _members.Count >= MaxMembers) { (Multiplayer.MultiplayerPeer as ENetMultiplayerPeer)?.DisconnectPeer((int)id); return; }
        _members.RemoveAll(m => m.PeerId == id);
        string n = Clean(name);
        if (_members.Any(m => m.Name == n)) n = Clean(n.Length > 10 ? n[..10] : n) + (_members.Count + 1);
        _members.Add(new Member(id, n));
        Publish();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLobby(string members, string settings)
    {
        if (IsHost) return;
        _members.Clear();
        foreach (var part in members.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(',');
            if (f.Length == 2 && long.TryParse(f[0], out var id)) _members.Add(new Member(id, Uri.UnescapeDataString(f[1])));
        }
        Settings = settings.Length == 0 ? null : Decode(settings);
        LobbyChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveStart(string settings, string seats)
    {
        if (IsHost) return;
        var s = Decode(settings);
        if (s == null) return;
        var sp = seats.Split(',').Select(x => long.TryParse(x, out var v) ? v : 0).ToArray();
        if (sp.Length != s.Players.Count + 1) return;
        BeginLocal(s, sp);
    }

    // ---- RPCs: in-match message relay ---------------------------------------------------

    /// <summary>
    /// Sends a message for a seat this peer owns ('A' = turn action, 'S' = finished shop cart). Not delivered back to the sender.
    /// </summary>
    public void Send(char kind, int seat, string payload)
    {
        if (!IsActive) return;
        if (IsHost) Rpc(MethodName.Deliver, kind.ToString(), seat, payload);
        else RpcId(1, MethodName.Submit, kind.ToString(), seat, payload);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Submit(string kind, int seat, string payload)
    {
        if (!IsHost || kind.Length != 1) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (!MatchActive || seat < 1 || seat >= SeatPeers.Length || SeatPeers[seat] != sender) return;
        Enqueue(kind[0], seat, payload);
        foreach (var m in _members)
            if (m.PeerId != 1 && m.PeerId != sender) RpcId(m.PeerId, MethodName.Deliver, kind, seat, payload);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Deliver(string kind, int seat, string payload)
    {
        if (IsHost || kind.Length != 1) return;
        Enqueue(kind[0], seat, payload);
    }

    private void Enqueue(char kind, int seat, string payload)
    {
        var key = (kind, seat);
        if (_waiters.Remove(key, out var w)) { w.TrySetResult(payload); return; }
        if (!_queues.TryGetValue(key, out var q)) _queues[key] = q = new Queue<string>();
        q.Enqueue(payload);
    }

    /// <summary>Waits for the next message of this kind from this seat; null when the session was aborted.</summary>
    public Task<string?> Next(char kind, int seat)
    {
        var key = (kind, seat);
        if (_aborted || !IsActive) return Task.FromResult<string?>(null);
        if (_queues.TryGetValue(key, out var q) && q.Count > 0) return Task.FromResult<string?>(q.Dequeue());
        var tcs = new TaskCompletionSource<string?>();
        _waiters[key] = tcs;
        return tcs.Task;
    }

    private void FlushWaiters()
    {
        foreach (var w in _waiters.Values) w.TrySetResult(null);
        _waiters.Clear();
    }

    // ---- wire formats -------------------------------------------------------------------

    private static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    private static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    public static string EncodeShot(ShotInput i, int stateHash) =>
        string.Join("|", "S", (int)i.Weapon, (int)i.Casing, F(i.Power), F(i.Bearing), F(i.Elevation),
            i.Target is { } t ? $"{t.X}:{t.Y}" : "", stateHash);

    public const string SelfDestructMsg = "D";

    /// <summary>Parses a turn action. Returns (input, hash) for a shot, (null, 0) for self-destruct, or null when malformed.</summary>
    public static (ShotInput? input, int hash)? DecodeAction(string msg)
    {
        try
        {
            if (msg == SelfDestructMsg) return (null, 0);
            var f = msg.Split('|');
            if (f.Length != 8 || f[0] != "S") return null;
            (int, int)? target = null;
            if (f[6].Length > 0) { var t = f[6].Split(':'); target = (int.Parse(t[0]), int.Parse(t[1])); }
            var si = new ShotInput((WeaponId)int.Parse(f[1]), (CasingId)int.Parse(f[2]), P(f[3]), P(f[4]), P(f[5]), target);
            return (si, int.Parse(f[7]));
        }
        catch (Exception) { return null; }
    }

    /// <summary>Cheap desync detector: hash of every tank's position, hull, shield and cash.</summary>
    public static int StateHash(MatchState s)
    {
        uint h = 2166136261;   // FNV-1a; System.HashCode is randomised per process and would never match across peers
        void Add(long v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 16777619; } }
        Add(s.Round); Add(s.TurnCounter); Add(s.CurrentPlayer); Add(s.WindBearing); Add(s.WindStrength);
        foreach (var t in s.AllTanks)
        {
            Add(BitConverter.DoubleToInt64Bits(t.X)); Add(BitConverter.DoubleToInt64Bits(t.Y)); Add(BitConverter.DoubleToInt64Bits(t.Z));
            Add(t.Hull); Add(t.Shield); Add(t.Cash);
        }
        return (int)h;
    }

    public static string EncodeShop(Tank t) =>
        string.Join("|", t.Cash, string.Join(",", t.Weapons), string.Join(",", t.Utilities), string.Join(",", t.Casings));

    public static bool ApplyShop(Tank t, string msg)
    {
        try
        {
            var f = msg.Split('|');
            if (f.Length != 4) return false;
            var w = f[1].Split(',').Select(int.Parse).ToArray();
            var u = f[2].Split(',').Select(int.Parse).ToArray();
            var c = f[3].Split(',').Select(int.Parse).ToArray();
            if (w.Length != t.Weapons.Length || u.Length != t.Utilities.Length || c.Length != t.Casings.Length) return false;
            t.Cash = long.Parse(f[0]);
            w.CopyTo(t.Weapons, 0); u.CopyTo(t.Utilities, 0); c.CopyTo(t.Casings, 0);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static string Encode(MatchSettings s) =>
        string.Join("|", s.Rounds, s.MaxShots, s.StartCash, (int)s.Order, s.AirResistance ? 1 : 0, s.Seed,
            string.Join(",", s.Landscapes.OrderBy(x => x)),
            string.Join(";", s.Players.Select(p => $"{Uri.EscapeDataString(p.Name)},{(int)p.Type}")),
            s.Wind ? 1 : 0, s.WindChangeEvery);

    public static MatchSettings? Decode(string text)
    {
        try
        {
            var f = text.Split('|');
            var s = new MatchSettings
            {
                Rounds = int.Parse(f[0]), MaxShots = int.Parse(f[1]), StartCash = int.Parse(f[2]), Order = (PlayOrder)int.Parse(f[3]),
                AirResistance = f[4] == "1", Seed = uint.Parse(f[5]),
                Landscapes = new HashSet<int>(f[6].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)),
                Players = new List<PlayerSetup>(),
            };
            foreach (var p in f[7].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var q = p.Split(',');
                s.Players.Add(new PlayerSetup(Uri.UnescapeDataString(q[0]), (PlayerType)int.Parse(q[1])));
            }
            if (f.Length >= 10) { s.Wind = f[8] == "1"; s.WindChangeEvery = Math.Clamp(int.Parse(f[9]), 1, 10); }
            return s.Players.Count is >= 2 and <= 4 && s.Landscapes.Count > 0 ? s : null;
        }
        catch (Exception) { return null; }
    }

    private static MatchSettings Clone(MatchSettings s) => Decode(Encode(s))!;
}
