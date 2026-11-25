using Godot;
using Artillery.Core.Ai;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Game.Battle;
using Artillery.Game.Camera;
using Artillery.Game.Net;
using Artillery.Game.Ui;

namespace Artillery.Game;

/// <summary>
/// Scene flow controller: Title, Setup, then per round shop screens (living humans in order; computers auto-shop), Battle,
/// round results; finally FinalResults and back to Title. ESC in battle opens the pause menu.
/// Command line (after "--"): --autoplay (4 CPUs, 2 rounds, 8 shots, 8x, no input, quits at the end);
/// --humantest (scripted human path through all screens, see HumanTest.cs);
/// --shot=ABS.png [--after=N] [--view=side|iso|plan] [--info] [--seed=N] (screenshot of the battle HUD on the first human turn after N shots).
/// </summary>
public partial class Main : Node
{
	private enum Outcome { RoundDone, EndGame, QuitToTitle }

	private bool _autoplay, _humantest;
	private string? _shotPath;
	private int _shotAfter = 3;
	private bool _shotInfo;
	private ViewPreset? _shotView;
	private uint _shotSeed = 4242;
	private Node? _screen;
	private CanvasLayer _layer = null!;
	private MatchEngine? _engine;
	private NetSession _net = null!;
	private bool _online, _netAborted;
	private string? _mpTest, _mpAddr, _mpShot, _mpScreen;   // headless loopback test: --mphost / --mpjoin=IP (humans are played by the AI)
	private string? _notice;

	public override void _Ready()
	{
		UiSettings.EnsureLoaded();
		_layer = new CanvasLayer { Name = "Screens", Layer = 10 };
		AddChild(_layer);
		_net = new NetSession { Name = "Net" };   // must sit at /root/Main/Net on every peer for RPCs
		AddChild(_net);
		_net.Aborted += why => { _netAborted = true; _notice = why; };
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a == "--autoplay") _autoplay = true;
			else if (a == "--humantest") _humantest = true;
			else if (a == "--mphost") _mpTest = "host";
			else if (a.StartsWith("--mpshot=")) _mpShot = a[9..];
			else if (a.StartsWith("--mpscreen=")) _mpScreen = a[11..];
			else if (a.StartsWith("--mpjoin=")) { _mpTest = "join"; _mpAddr = a[9..]; }
			else if (a.StartsWith("--shot=")) _shotPath = a[7..];
			else if (a.StartsWith("--after=")) _shotAfter = int.Parse(a[8..]);
			else if (a.StartsWith("--seed=")) _shotSeed = uint.Parse(a[7..]);
			else if (a.StartsWith("--view=")) _shotView = Enum.Parse<ViewPreset>(a[7..], true);
			else if (a == "--info") _shotInfo = true;
		}
		if (_humantest)
		{
			var ht = new HumanTest { Name = "HumanTest", Screens = _layer, ProcessMode = ProcessModeEnum.Always };
			AddChild(ht);
			_ = ht.Run(this);
		}
		if (_mpShot != null) { _ = MpShot(); return; }
		_ = Flow();
	}

	/// <summary>F11 toggles between fullscreen (the default) and a normal window.</summary>
	public override void _UnhandledKeyInput(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
		{
			bool fs = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
			DisplayServer.WindowSetMode(fs ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
			GetViewport().SetInputAsHandled();
		}
	}

	private async Task Flow()
	{
		try
		{
			if (_autoplay || _shotPath != null || _humantest || _mpTest != null) UiSettings.AnimationSpeed = 8f;
			while (true)
			{
				_online = false; _netAborted = false;
				var settings = _shotPath != null ? ShotSettings() : _mpTest != null ? await MpTestLobby() : await TitleAndSetup();
				if (settings == null) { GetTree().Quit(); return; }
				await PlayMatch(settings);
				if (_online) { _net.Disconnect(); _online = false; }
				if (_autoplay || _shotPath != null || _mpTest != null) { GD.Print("AUTOPLAY DONE"); GetTree().Quit(); return; }
			}
		}
		catch (Exception ex) { GD.PushError($"Main flow failed: {ex}"); GetTree().Quit(1); }
	}

	private MatchSettings ShotSettings() => new()
	{
		Players = new() { new("Alpha", PlayerType.Human), new("Bravo", PlayerType.Fox), new("Charlie", PlayerType.Dolphin), new("Delta", PlayerType.Human) },
		Rounds = 2, MaxShots = 12, StartCash = 20000, Seed = _shotSeed, Order = PlayOrder.Sequential,
		Landscapes = new() { 14 },
	};

	// ---------------------------------------------------------------- screens

	private T Show<T>(string scene) where T : Node
	{
		_screen?.QueueFree();
		var n = GD.Load<PackedScene>($"res://scenes/Ui/{scene}.tscn").Instantiate<T>();
		_layer.AddChild(n); _screen = n;
		return n;
	}

	/// <summary>Shows a screen that is built in code (no .tscn).</summary>
	private T ShowNode<T>(T n) where T : Node
	{
		_screen?.QueueFree();
		_layer.AddChild(n); _screen = n;
		return n;
	}

	/// <summary>Full-screen "waiting" message (online: other players are still shopping).</summary>
	private void ShowWait(string text)
	{
		var c = new Control();
		UiKit.Root(c);
		var box = UiKit.Centered(c);
		box.AddChild(UiKit.Lbl(text, "Heading", HorizontalAlignment.Center));
		ShowNode(c);
	}

	private void Clear() { _screen?.QueueFree(); _screen = null; }

	/// <summary>Waits for a screen event; in autoplay completes by itself after a short delay (no input needed).</summary>
	private async Task Await(Action<Action> subscribe)
	{
		var tcs = new TaskCompletionSource();
		subscribe(() => tcs.TrySetResult());
		if (_autoplay || _mpTest != null) { _ = Delay(0.4, tcs); }
		await tcs.Task;
	}

	private async Task Delay(double sec, TaskCompletionSource tcs)
	{
		await ToSignal(GetTree().CreateTimer(sec), SceneTreeTimer.SignalName.Timeout);
		tcs.TrySetResult();
	}

	private async Task<MatchSettings?> TitleAndSetup()
	{
		while (true)
		{
			var title = Show<TitleScreen>("TitleScreen");
			if (_notice != null) { title.SetNotice(_notice); _notice = null; }
			bool newGame = false, online = false, settingsOpen = false;
			await Await(done =>
			{
				title.NewGameRequested += () => { newGame = true; done(); };
				title.MultiplayerRequested += () => { online = true; done(); };
				title.SettingsRequested += () => { settingsOpen = true; done(); };
				title.QuitRequested += () => done();
			});
			if (_autoplay) newGame = true;
			if (settingsOpen)
			{
				var st = ShowNode(new SettingsScreen());
				var back = new TaskCompletionSource();
				st.BackRequested += () => back.TrySetResult();
				await back.Task;
				continue;
			}
			if (online)
			{
				var net = await OnlineLobby();
				if (net == null) continue;
				_online = true;
				Clear();
				return net;
			}
			if (!newGame) return null;

			var setup = Show<SetupScreen>("SetupScreen");
			setup.Init(SetupScreen.LoadSaved() ?? SetupScreen.DefaultSettings());
			MatchSettings? chosen = null;
			await Await(done => { setup.StartRequested += s => { chosen = s; done(); }; setup.BackRequested += () => done(); });
			if (_autoplay) chosen = AutoSettings();
			if (chosen == null) continue;
			Clear();
			return chosen;
		}
	}

	/// <summary>Dev aid: renders the multiplayer screen to a PNG (--mpshot=ABS.png) and quits.</summary>
	private async Task MpShot()
	{
		if (_mpScreen == "setup") { var su = Show<SetupScreen>("SetupScreen"); su.Init(SetupScreen.DefaultSettings()); su.UseForLobby(); }
		else if (_mpScreen == "settings") ShowNode(new SettingsScreen());
		else ShowNode(new MultiplayerScreen().Init(_net));
		for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		GetViewport().GetTexture().GetImage().SavePng(_mpShot);
		GD.Print($"viewport {GetViewport().GetVisibleRect().Size} window {DisplayServer.WindowGetSize()}");
		GetTree().Quit();
	}

	/// <summary>Headless online test: host waits for one guest and starts a 2-human match; guest joins and waits.</summary>
	private async Task<MatchSettings?> MpTestLobby()
	{
		var tcs = new TaskCompletionSource<MatchSettings?>();
		_net.MatchStarting += s => tcs.TrySetResult(s);
		if (_mpTest == "host")
		{
			bool started = false;
			_net.Host(NetSession.DefaultPort, "Hosty");
			_net.LobbyChanged += () =>
			{
				if (_net.Members.Count < 2) return;
				if (_net.Settings == null)
					_net.SetSettings(new MatchSettings
					{
						Players = new() { new("a", PlayerType.Human), new("b", PlayerType.Human), new("c", PlayerType.Fox) },
						Rounds = 2, MaxShots = 6, StartCash = 8000, Seed = 99, Order = PlayOrder.Random, AirResistance = true, Wind = true, WindChangeEvery = 2, Landscapes = new() { 14, 3 },
					});
				else if (!started && _net.StartBlocker() == null) { started = true; _net.StartMatch(); }
			};
		}
		else
		{
			var err = _net.Join(_mpAddr!, NetSession.DefaultPort, "Guesty");
			GD.Print($"join: {err}");
		}
		_online = true;
		return await tcs.Task;
	}

	/// <summary>Connect form + lobby. Returns the match settings once the host starts the match, or null on Back / disconnect.</summary>
	private async Task<MatchSettings?> OnlineLobby()
	{
		var tcs = new TaskCompletionSource<MatchSettings?>();
		void OnStart(MatchSettings s) => tcs.TrySetResult(s);
		_net.MatchStarting += OnStart;
		try
		{
			while (true)
			{
				var screen = ShowNode(new MultiplayerScreen().Init(_net));   // Init must precede AddChild (_Ready uses it)
				var step = new TaskCompletionSource<bool>();    // true = configure, false = back
				screen.BackRequested += () => step.TrySetResult(false);
				screen.ConfigureRequested += () => step.TrySetResult(true);
				var first = await Task.WhenAny(tcs.Task, step.Task);
				if (first == tcs.Task) return await tcs.Task;
				if (!step.Task.Result) { _net.Disconnect(); return null; }

				var setup = Show<SetupScreen>("SetupScreen");
				setup.Init(_net.Settings ?? SetupScreen.DefaultSettings());
				setup.UseForLobby();
				var saved = new TaskCompletionSource<MatchSettings?>();
				setup.StartRequested += s => saved.TrySetResult(s);
				setup.BackRequested += () => saved.TrySetResult(null);
				var r = await Task.WhenAny(tcs.Task, saved.Task);
				if (r == tcs.Task) return await tcs.Task;
				if (saved.Task.Result is { } chosen) _net.SetSettings(chosen);
			}
		}
		finally { _net.MatchStarting -= OnStart; Clear(); }
	}

	private static MatchSettings AutoSettings() => new()
	{
		Players = Enumerable.Range(1, 4).Select(i => new PlayerSetup($"CPU {i}", i % 2 == 0 ? PlayerType.Fox : PlayerType.Dolphin)).ToList(),
		Rounds = 2, MaxShots = 8, StartCash = 5000, Seed = 777, Order = PlayOrder.Random,
		Landscapes = new() { 14, 3, 5 },
	};

	// ---------------------------------------------------------------- match

	private async Task PlayMatch(MatchSettings settings)
	{
		var engine = _engine = new MatchEngine(settings) { AiDecide = AiPlayer.ChooseShot };
		engine.StartMatch();
		var s = engine.State;
		bool quit = false, endGame = false;
		while (!quit)
		{
			// shopping: every living human in order; computers shop inside StartRound
			if (_shotPath != null) foreach (var t in s.AllTanks) { Shop.AiShop(s, t); if (!t.IsComputer) t.Utilities[(int)UtilityId.AimComputer] = 1; }
			else
			{
				// own seats first (online: seats owned by other peers shop on their machines at the same time)
				foreach (int p in engine.ShoppingQueue)
				{
					var t = s[p];
					if (t.IsComputer || _autoplay) continue;
					if (_online && !_net.OwnsSeat(p)) continue;
					if (_mpTest != null) { _net.Send('S', p, NetSession.EncodeShop(t)); continue; }
					var shop = Show<ShopScreen>("ShopScreen");
					shop.Init(s, t);
					Action<string>? onAbort = null;
					await Await(done =>
					{
						shop.Done += done;
						if (_online) { onAbort = _ => done(); _net.Aborted += onAbort; }
					});
					if (onAbort != null) _net.Aborted -= onAbort;
					if (_netAborted) { Clear(); _engine = null; return; }
					if (_online) _net.Send('S', p, NetSession.EncodeShop(t));
				}
				if (_online)
				{
					foreach (int p in engine.ShoppingQueue)
					{
						var t = s[p];
						if (_net.OwnsSeat(p) || t.IsComputer) continue;
						ShowWait($"Waiting for {t.Name} to finish shopping…");
						var cart = await _net.Next('S', p);
						if (cart == null || _netAborted || !NetSession.ApplyShop(t, cart)) { _netAborted = true; Clear(); _engine = null; return; }
					}
				}
			}
			Clear();
			engine.StartRound();

			var outcome = await RunBattle(engine);
			GD.Print($"Round {s.Round} finished: phase={s.Phase} winner={s.RoundWinner} outcome={outcome} scores={string.Join(",", s.AllTanks.Select(t => t.TotalScore))} cash={string.Join(",", s.AllTanks.Select(t => t.Cash))}");
			if (outcome == Outcome.QuitToTitle) { quit = true; break; }
			if (outcome == Outcome.EndGame) endGame = true;

			if (!endGame)
			{
				var rr = Show<RoundResultsScreen>("RoundResultsScreen");
				rr.Init(s, s.AllTanks.Select(t => (t.Index, t.RoundScore)).ToList());
				await Await(done => rr.Continue += done);
			}
			if (endGame || s.Phase == MatchPhase.MatchOver)
			{
				if (_online) _net.EndMatch();
				var fr = Show<FinalResultsScreen>("FinalResultsScreen");
				fr.Init(s);
				await Await(done => fr.Done += done);
				break;
			}
			engine.BeginRoundSetup();
		}
		Clear();
		_engine = null;
	}


	private async Task<Outcome> RunBattle(MatchEngine engine)
	{
		var battle = GD.Load<PackedScene>("res://scenes/Battle.tscn").Instantiate<BattleScene>();
		battle.Init(engine, _autoplay, _online ? _net : null); battle.AutoHuman = _mpTest != null;
		battle.ScreenshotPath = _shotPath; battle.ScreenshotAfterShots = _shotAfter; battle.ScreenshotInfo = _shotInfo; battle.InitialView = _shotView;
		AddChild(battle);
		var tcs = new TaskCompletionSource<Outcome>();
		PauseMenu? menu = null;

		void Close() { if (menu != null) { menu.QueueFree(); menu = null; GetTree().Paused = false; } }
		battle.Finished += () => { Close(); tcs.TrySetResult(Outcome.RoundDone); };
		battle.MenuRequested += () =>
		{
			if (menu != null) return;
			menu = GD.Load<PackedScene>("res://scenes/Ui/PauseMenu.tscn").Instantiate<PauseMenu>();
			menu.ProcessMode = ProcessModeEnum.Always;
			menu.Online = _online;
			_layer.AddChild(menu);
			if (!_online) GetTree().Paused = true;   // online: the other players keep playing
			menu.Resume += Close;
			menu.SelfDestruct += () => { Close(); battle.RequestSelfDestruct(); };
			menu.EndRound += () => { Close(); battle.RequestEndRound(); };
			menu.EndGame += () => { Close(); tcs.TrySetResult(Outcome.EndGame); engine.EndRoundNow(); };
			menu.QuitToTitle += () => { Close(); if (_online) { _notice = "You left the online match."; _net.Disconnect(); } tcs.TrySetResult(Outcome.QuitToTitle); };
		};
		Action<string> onAbort = _ => { Close(); tcs.TrySetResult(Outcome.QuitToTitle); };
		if (_online) _net.Aborted += onAbort;
		var o = await tcs.Task;
		if (_online) _net.Aborted -= onAbort;
		Close();
		battle.QueueFree();
		return o;
	}
}
