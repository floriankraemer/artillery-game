using Artillery.Core.Combat;
using Artillery.Core.Items;
using Artillery.Core.Physics;
using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Match;

/// <summary>
/// Orchestrates a whole match: setup, shopping, rounds, turns and shot resolution
/// (GAME-MECHANICS §2, §5). Deterministic; all randomness via <see cref="MatchState.Rng"/>.
/// UI flow: StartMatch, [humans shop, StartRound], loop { BeginTurn; Fire or PlayComputerTurn } until Phase != Playing,
/// then RoundOver: BeginRoundSetup again; MatchOver: FinalRanking.
/// </summary>
public sealed class MatchEngine
{
    private readonly HashSet<int> _shopped = new();
    private bool _turnBegun;

    public MatchEngine(MatchSettings settings) { State = new MatchState(settings); }

    public MatchState State { get; }
    public MatchSettings Settings => State.Settings;

    /// <summary>AI decision hook (e.g. <c>AiPlayer.ChooseShot</c>). Null = built-in fallback.</summary>
    public Func<MatchState, int, ShotInput>? AiDecide { get; set; }

    /// <summary>Players who must visit the shop this round (everyone, in player order).</summary>
    public IReadOnlyList<int> ShoppingQueue => State.AllTanks.Where(t => t.Hull > 0).Select(t => t.Index).ToList();

    public int CurrentPlayer => State.CurrentPlayer;
    public Tank Current => State[State.CurrentPlayer];
    public bool IsComputerTurn => State.Phase == MatchPhase.Playing && State.CurrentPlayer > 0 && Current.IsComputer;

    // ---- Match / round setup ------------------------------------------------------------------

    /// <summary>Creates tanks from settings, gives start cash, then begins round 1 (Phase Shopping).</summary>
    public void StartMatch()
    {
        State.Tanks.RemoveRange(1, State.Tanks.Count - 1);
        int i = 1;
        foreach (var p in Settings.Players) State.Tanks.Add(new Tank { Index = i++, Name = p.Name, Type = p.Type });
        Economy.StartMatch(State);
        State.Round = 0;
        Array.Clear(State.LandscapeUsage);
        BeginRoundSetup();
    }

    /// <summary>Round++, pick landscape (least-used rotation, highest id wins ties), generate it, choose turn order, enter shopping.</summary>
    public void BeginRoundSetup()
    {
        var s = State;
        s.Round++;
        s.RoundAborted = false;
        s.RoundWinner = 0;
        _shopped.Clear();
        _turnBegun = false;

        var enabled = Settings.Landscapes.Where(id => id >= 1 && id <= LandscapeCatalog.All.Count).ToList();
        if (enabled.Count == 0) enabled.Add(13);
        int min = enabled.Min(id => s.LandscapeUsage[id]);
        int pick = enabled.Where(id => s.LandscapeUsage[id] == min).Max();
        s.LandscapeUsage[pick]++;
        TerrainGenerator.Generate(s.World, LandscapeCatalog.Get(pick), s.Rng);

        int n = s.PlayerCount;
        s.Order.Clear();
        var used = new bool[n + 1];
        for (int i = 1; i <= n; i++)
        {
            int o;
            do
            {
                o = s.Rng.Next(n) + 1;
                if (Settings.Order == PlayOrder.Sequential) o = i;
            } while (used[o]);
            used[o] = true;
            s.Order.Add(o);
        }
        foreach (var t in s.AllTanks) { t.Hull = 1; Shop.PrepareForShopping(t); }
        s.OrderPos = 0;
        s.TurnCounter = 0;
        s.Phase = MatchPhase.Shopping;
    }

    /// <summary>Runs the computer shopping script for one computer player (no-op for humans / repeats).</summary>
    public void AutoShop(int player)
    {
        var t = State[player];
        if (!t.IsComputer || !_shopped.Add(player)) return;
        Shop.AiShop(State, t);
    }

    /// <summary>Auto-shops every computer that has not shopped yet.</summary>
    public void AutoShopComputers() { foreach (var t in State.AllTanks) AutoShop(t.Index); }

    /// <summary>Per-tank round setup, tank placement, first turn. Computers auto-shop first if they have not.</summary>
    public void StartRound()
    {
        var s = State;
        AutoShopComputers();
        foreach (var t in s.AllTanks)
        {
            int armour = t.Count(UtilityId.HullArmour);
            t.Hull = Balance.BaseHull + Balance.HullPerArmour * armour;
            t.MaxHull = t.Hull;
            t.Shield = Balance.ShieldPerGenerator * t.Count(UtilityId.ShieldGenerator);
            t.DeathRadius = t.Has(UtilityId.DeadMansBomb) ? Balance.DeathNukeRadius : Balance.DeathBlastRadius;
            if (t.Utilities[(int)UtilityId.SatelliteLaser] > 0)
            {
                t.Weapons[(int)WeaponId.SatelliteLaunch] = 1;
                t.Utilities[(int)UtilityId.SatelliteLaser] = 0;
            }
            t.Weapons[(int)WeaponId.SatelliteAim] = 0;
            t.SatelliteTargetX = t.SatelliteTargetY = t.SatelliteFireTurn = -1;
            t.TeleportedThisRound = false;
            t.Ai.Target = 0; t.Ai.ShotsAtTarget = 0; t.Ai.LastTarget = 0;
            t.Power = 50; t.Elevation = 45;
            t.SelectedWeapon = WeaponId.BasicShell; t.SelectedCasing = CasingId.Impact;
        }
        s.WindAge = -1; s.WindStrength = 0;
        TankPlacement.Place(s);
        foreach (var t in s.AllTanks) t.Bearing = BearingTo(t.X, t.Y, 25, 25);
        s.TurnCounter = 1;
        s.OrderPos = 0;
        while (!s[s.Order[s.OrderPos]].Alive) s.OrderPos++;
        _turnBegun = false;
        s.Phase = MatchPhase.Playing;
    }

    private static double BearingTo(double x0, double y0, double x1, double y1)
    {
        double a = Math.Atan2(y1 - y0, x1 - x0) * 180 / Math.PI;
        return Math.Round(((360 - a) % 360 + 360) % 360) % 360;
    }

    // ---- Turns --------------------------------------------------------------------------------

    /// <summary>
    /// Start of the current player's turn: due satellite strike, per-turn upkeep, terrain morph. May end the round
    /// (check <see cref="MatchState.Phase"/>). Idempotent per turn: a second call returns an empty list.
    /// </summary>
    public List<GameEvent> BeginTurn()
    {
        var ev = new List<GameEvent>();
        if (State.Phase != MatchPhase.Playing || _turnBegun) return ev;
        _turnBegun = true;
        var s = State;
        var me = Current;
        UpdateWind(ev);
        if (me.SatelliteFireTurn == s.TurnCounter)
        {
            Specials.SatelliteStrike(s, me, ev);
            Resolve(me.Index, ev);
        }
        Upkeep.StartOfTurn(s, ev);
        if (s.World.Landscape.Morphs && s.TurnCounter > 1)
        {
            TerrainGenerator.Morph(s.World, s.Rng);
            ev.Add(new TerrainChangedEvent());
            Resolve(me.Index, ev);
        }
        if (s.LivingTanks.Count() <= 1) EndRound(ev, false);
        return ev;
    }

    /// <summary>Rolls a new random wind on the first turn of a round and then every <see cref="MatchSettings.WindChangeEvery"/> turns.</summary>
    private void UpdateWind(List<GameEvent> ev)
    {
        var s = State;
        if (!Settings.Wind) { s.WindStrength = 0; return; }
        if (s.WindAge < 0 || s.WindAge >= Math.Max(1, Settings.WindChangeEvery))
        {
            s.WindBearing = s.Rng.Next(360);
            s.WindStrength = Math.Min(s.Rng.Next(Balance.MaxWind + 1), s.Rng.Next(Balance.MaxWind + 1));   // lower of two rolls: strong wind is rare
            s.WindAge = 0;
            ev.Add(new MessageEvent(s.WindStrength == 0 ? "Wind: calm" : $"Wind changed: strength {s.WindStrength}, blowing toward {s.WindBearing}°"));
        }
        s.WindAge++;
    }

    /// <summary>Plays the current computer player's turn through <see cref="AiDecide"/> (or the fallback).</summary>
    public List<GameEvent> PlayComputerTurn()
    {
        var ev = BeginTurn();
        if (State.Phase != MatchPhase.Playing) return ev;
        var input = AiDecide?.Invoke(State, CurrentPlayer) ?? Fallback(CurrentPlayer);
        ev.AddRange(Fire(input));
        return ev;
    }

    private ShotInput Fallback(int p)
    {
        var me = State[p];
        var foe = State.LivingTanks.Where(t => t.Index != p)
            .OrderBy(t => (t.X - me.X) * (t.X - me.X) + (t.Y - me.Y) * (t.Y - me.Y)).FirstOrDefault();
        double bearing = foe != null ? BearingTo(me.X, me.Y, foe.X, foe.Y) : me.Bearing;
        return new ShotInput(WeaponId.BasicShell, CasingId.Impact, 60, bearing, 45);
    }

    /// <summary>Resolves the current player's shot in the order of GAME-MECHANICS §5, then advances the turn.</summary>
    public List<GameEvent> Fire(ShotInput input)
    {
        var ev = new List<GameEvent>();
        if (State.Phase != MatchPhase.Playing) return ev;
        if (!_turnBegun) { ev.AddRange(BeginTurn()); if (State.Phase != MatchPhase.Playing) return ev; }
        var s = State;
        var t = Current;
        int p = t.Index;

        // validate / clamp, store aim
        var wid = input.Weapon;
        if (wid == WeaponId.None) { Advance(ev); return ev; }
        if (!Enum.IsDefined(wid) || !t.Has(wid)) wid = WeaponId.BasicShell;
        var w = ItemCatalog.Get(wid);
        var casing = input.Casing;
        if (!Enum.IsDefined(casing) || t.Casings[(int)casing] <= 0) casing = CasingId.Impact;
        if (!w.Projectile || wid == WeaponId.SatelliteLaunch) casing = CasingId.Impact;
        t.Power = Math.Clamp(input.Power, 0, 100);
        t.Bearing = ((input.Bearing % 360) + 360) % 360;
        t.Elevation = Math.Clamp(input.Elevation, t.MinElevation, t.MaxElevation);
        t.SelectedWeapon = wid; t.SelectedCasing = casing;
        double elev = casing is CasingId.Cruise or CasingId.SmartCruise ? 0 : t.Elevation;

        // 2. sound + flight
        if (wid != WeaponId.SatelliteAim && wid != WeaponId.AirStrike && w.FireSound != 0) ev.Add(new SoundEvent((SoundId)w.FireSound));
        Vec3d impact = t.Position;
        bool offMap = false, orbit = false;
        if (w.Projectile)
        {
            var fr = Ballistics.Fly(s, p, wid, casing, Ballistics.LaunchPoint(s, t),
                Ballistics.LaunchVelocity(t, t.Power, t.Bearing, elev), true);
            impact = fr.Impact; offMap = fr.OffMap; orbit = fr.ReachedOrbit;
            ev.Add(new ProjectileFlightEvent(p, wid, casing, fr.Path, fr.OffMap));
        }
        // 3. inventory (SatelliteAim is decremented inside Specials)
        if (t.Weapons[(int)wid] != Tank.Infinite && wid != WeaponId.BasicShell && wid != WeaponId.SatelliteAim) t.Weapons[(int)wid]--;
        if (casing != CasingId.Impact && t.Casings[(int)casing] != Tank.Infinite) t.Casings[(int)casing]--;

        var oldZ = s.LivingTanks.ToDictionary(x => x.Index, x => x.Z);

        if (wid == WeaponId.SatelliteLaunch)
        {
            if (orbit)
            {
                t.Weapons[(int)WeaponId.SatelliteAim] = 3;
                ev.Add(new MessageEvent("Satellite in orbit: 3 strikes available."));
            }
            else ev.Add(new MessageEvent("Satellite launch failed."));
        }
        else
        {
            // 4-6. explosion / specials / standard blast
            if (!offMap && w.ImpactSound != 0) ev.Add(new SoundEvent((SoundId)w.ImpactSound));
            if (w.Effect == WeaponEffect.Explosive)
            {
                if (!offMap)
                {
                    Blast.Crater(s, impact, w.Radius, ev);
                    if (w.StandardBlast) Blast.StandardBlast(s, p, Blast.BonusDef(s, p), impact, w.Radius, ev);
                }
            }
            else
            {
                Specials.Apply(s, p, w, impact, input.Target, ev);
                if (w.StandardBlast && !offMap) Blast.StandardBlast(s, p, Blast.BonusDef(s, p), impact, w.Radius, ev);
            }
        }

        // 7-9. falls, effects, deaths, settle, deaths
        TankPhysics.ApplyFalls(s, oldZ, ev);
        if (t.Alive) EffectTicker.Tick(s, ev);
        Resolve(p, ev);
        Advance(ev);
        return ev;
    }

    /// <summary>Deaths, then settle/drown, then deaths again.</summary>
    private void Resolve(int shooter, List<GameEvent> ev)
    {
        Deaths.Resolve(State, shooter, ev);
        TankPhysics.Settle(State, ev);
        Deaths.Resolve(State, shooter, ev);
    }

    /// <summary>Menu "Destruct": the current tank is destroyed (and may take others with it); the turn is consumed.</summary>
    public List<GameEvent> SelfDestruct()
    {
        var ev = new List<GameEvent>();
        if (State.Phase != MatchPhase.Playing) return ev;
        if (!_turnBegun) ev.AddRange(BeginTurn());
        if (State.Phase != MatchPhase.Playing) return ev;
        Current.Hull = Tank.HullDead;
        Resolve(CurrentPlayer, ev);
        Advance(ev);
        return ev;
    }

    /// <summary>Menu "End Round": no winner, no interest.</summary>
    public List<GameEvent> EndRoundNow()
    {
        var ev = new List<GameEvent>();
        if (State.Phase == MatchPhase.Playing) EndRound(ev, true);
        return ev;
    }

    private void Advance(List<GameEvent> ev)
    {
        var s = State;
        _turnBegun = false;
        if (s.LivingTanks.Count() <= 1) { EndRound(ev, false); return; }
        int n = s.Order.Count;
        do
        {
            s.OrderPos++;
            if (s.OrderPos >= n)
            {
                s.OrderPos = 0;
                s.TurnCounter++;
                if (s.TurnCounter > Settings.MaxShots)
                {
                    ev.Add(new SoundEvent(SoundId.TimeUp));
                    EndRound(ev, false);
                    return;
                }
            }
        } while (!s[s.Order[s.OrderPos]].Alive);
    }

    private void EndRound(List<GameEvent> ev, bool aborted)
    {
        var s = State;
        s.RoundAborted = aborted;
        var alive = s.LivingTanks.ToList();
        s.RoundWinner = !aborted && alive.Count == 1 ? alive[0].Index : 0;
        if (s.RoundWinner != 0)
        {
            var win = s[s.RoundWinner];
            Blast.AddScore(s, win.Index, win.Hull + Balance.WinnerBonus, "round winner", ev);
            win.Wins++;
            ev.Add(new SoundEvent(SoundId.Victory));
        }
        Economy.EndRound(s);
        s.Phase = s.Round >= Settings.Rounds ? MatchPhase.MatchOver : MatchPhase.RoundOver;
    }

    /// <summary>Players ordered by total score, highest first (stable).</summary>
    public List<Tank> FinalRanking() => State.AllTanks.OrderByDescending(t => t.TotalScore).ToList();
}
