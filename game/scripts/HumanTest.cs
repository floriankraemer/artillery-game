using Godot;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Game.Battle;
using Artillery.Game.Ui;

namespace Artillery.Game;

/// <summary>
/// `-- --humantest`: scripted human path through every screen using the real UI controls (button signals, sliders, option buttons,
/// input actions). Logs "[HUMANTEST]" lines; any failed step pushes an error and quits with exit code 1.
/// </summary>
public partial class HumanTest : Node
{
    public Node Screens = null!;
    private Main _main = null!;

    public async Task Run(Main main)
    {
        _main = main;
        try
        {
            await Steps();
            Log("ALL STEPS PASSED");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[HUMANTEST] FAILED: {ex.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Log(string s) => GD.Print("[HUMANTEST] " + s);

    private async Task Frames(int n = 2) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private async Task<T> WaitNode<T>(string what, Func<T?> find, double timeout = 30) where T : class
    {
        var t0 = Time.GetTicksMsec();
        while (true)
        {
            var r = find();
            if (r != null) { await Frames(3); return r; }
            if ((Time.GetTicksMsec() - t0) / 1000.0 > timeout) throw new Exception($"timeout waiting for {what}");
            await Frames(1);
        }
    }

    private async Task WaitUntil(string what, Func<bool> cond, double timeout = 60)
        => await WaitNode(what, () => cond() ? new object() : null, timeout);

    private static IEnumerable<Node> All(Node root)
    {
        foreach (var c in root.GetChildren()) { yield return c; foreach (var d in All(c)) yield return d; }
    }

    private static T? Find<T>(Node root, Func<T, bool>? pred = null) where T : Node
        => All(root).OfType<T>().FirstOrDefault(n => pred == null || pred(n));

    private static Button? ButtonText(Node root, string text)
        => All(root).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());

    /// <summary>A shop buy row: a button without text whose label child shows the item name and a price tag (not "+$").</summary>
    private static Button? BuyRow(Node root, string name)
        => All(root).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && All(b).OfType<Label>().Any(l => l.Text == name) && !All(b).OfType<Label>().Any(l => l.Text.StartsWith("+$")));

    /// <summary>The sell button of a fitted-list row (label child with the item name plus a "+$" refund label).</summary>
    private static Button? FittedRow(Node root, string name)
        => All(root).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && All(b).OfType<Label>().Any(l => l.Text == name) && All(b).OfType<Label>().Any(l => l.Text.StartsWith("+$")));

    private async Task Press(Button b, string step)
    {
        if (b.Disabled) throw new Exception($"button for '{step}' is disabled");
        Log("press: " + step);
        b.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(3);
    }

    private async Task Act(string name)
    {
        Log("input action: " + name);
        Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = false });
        await Frames(2);
    }

    private static void Expect(bool ok, string msg) { if (!ok) throw new Exception("expectation failed: " + msg); }

    private static void SelectOption(OptionButton o, string text)
    {
        int i = Enumerable.Range(0, o.ItemCount).First(k => o.GetItemText(k) == text);
        o.Select(i); o.EmitSignal(OptionButton.SignalName.ItemSelected, i);
    }

    private async Task Steps()
    {
        // ---- Title
        var title = await WaitNode("title screen", () => Find<TitleScreen>(Screens));
        Log("title shown");
        await Press(ButtonText(title, "New Game")!, "New Game");

        // ---- Setup: 1 Human + 1 Fox
        var setup = await WaitNode("setup screen", () => Find<SetupScreen>(Screens));
        Log("setup shown");
        while (All(setup).OfType<Button>().Any(b => b.Text == "Remove" && !b.Disabled))
            await Press(All(setup).OfType<Button>().First(b => b.Text == "Remove" && !b.Disabled), "Remove player");
        var typeBoxes = All(setup).OfType<OptionButton>().Where(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == "Fox")).ToList();
        Expect(typeBoxes.Count == 2, $"2 player type boxes, found {typeBoxes.Count}");
        SelectOption(typeBoxes[0], "Human"); SelectOption(typeBoxes[1], "Fox");
        SelectOption(All(setup).OfType<OptionButton>().First(o => o.ItemCount == 2 && o.GetItemText(0) == "Sequential"), "Sequential");
        var spins = All(setup).OfType<SpinBox>().ToList();
        spins[0].Value = 2; spins[1].Value = 10; // rounds, max shots
        await Frames(2);
        await Press(ButtonText(setup, "Start")!, "Start");

        // ---- Shop
        var shop = await WaitNode("shop screen", () => Find<ShopScreen>(Screens));
        Log("shop shown");
        await Press(BuyRow(shop, "Missile") ?? throw new Exception("Missile row not found"), "buy Missile");
        Expect(FittedRow(shop, "Missile") != null, "Missile in fitted list after buying");
        await Press(BuyRow(shop, "Shield Generator") ?? throw new Exception("Shield Generator row not found"), "buy Shield Generator");
        await Press(FittedRow(shop, "Shield Generator") ?? throw new Exception("Shield Generator not in fitted list"), "sell Shield Generator");
        Expect(FittedRow(shop, "Shield Generator") == null, "Shield Generator removed from fitted list after selling");
        Expect(FittedRow(shop, "Missile") != null, "Missile still fitted");
        await Press(ButtonText(shop, "Done")!, "Done (shop)");

        // ---- Battle
        var battle = await WaitNode("battle scene", () => _main.GetChildren().OfType<BattleScene>().FirstOrDefault());
        var hud = await WaitNode("battle hud", () => Find<BattleHud>(battle));
        Log("battle started");
        await WaitUntil("human turn (awaiting input)", () => battle.AwaitingInput);
        Log($"human turn; hud weapon={hud.Weapon}");
        var sliders = All(hud).OfType<HSlider>().ToList();
        Expect(sliders.Count >= 3, "aim sliders present");
        sliders[0].Value = 60; await Frames(2); Expect(Math.Abs(hud.Power - 60) < 0.5, $"power set (is {hud.Power})");
        sliders[1].Value = 123; await Frames(2); Expect(Math.Abs(hud.Bearing - 123) < 0.5, $"bearing set (is {hud.Bearing})");
        sliders[2].Value = 50; await Frames(2); Expect(Math.Abs(hud.Elevation - 50) < 0.5, $"elevation set (is {hud.Elevation})");
        Log($"aim power={hud.Power} bearing={hud.Bearing} elevation={hud.Elevation}");
        var weapons = All(hud).OfType<OptionButton>().First(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i).StartsWith("Missile")));
        int mi = Enumerable.Range(0, weapons.ItemCount).First(i => weapons.GetItemText(i).StartsWith("Missile"));
        weapons.Select(mi); weapons.EmitSignal(OptionButton.SignalName.ItemSelected, mi);
        await Frames(2);
        Expect(hud.Weapon == WeaponId.Missile, $"Missile selected (is {hud.Weapon})");
        Log("weapon selected: " + hud.Weapon);
        var tank = battle.Engine.State.AllTanks.First(t => !t.IsComputer);
        int before = tank.Count(WeaponId.Missile);
        await Act("fire");
        await WaitUntil("shot to start", () => !battle.AwaitingInput);
        Log("fired");
        await WaitUntil("CPU turn done and human turn again", () => battle.AwaitingInput, 90);
        Log($"CPU turn finished; missiles {before} -> {tank.Count(WeaponId.Missile)}");
        Expect(tank.Count(WeaponId.Missile) == before - 1, "missile ammo consumed");

        // ---- pause / resume
        await Act("menu");
        var pause = await WaitNode("pause menu", () => Find<PauseMenu>(Screens));
        Expect(GetTree().Paused, "tree paused");
        await Press(ButtonText(pause, "Resume")!, "Resume");
        await WaitUntil("pause menu closed", () => Find<PauseMenu>(Screens) == null);
        Expect(!GetTree().Paused, "tree unpaused");
        Log("resumed");

        // ---- end game
        await Act("menu");
        pause = await WaitNode("pause menu (2)", () => Find<PauseMenu>(Screens));
        await Press(ButtonText(pause, "End Game")!, "End Game");
        await Press(ButtonText(pause, "Yes")!, "confirm Yes");
        var final = await WaitNode("final results", () => Find<FinalResultsScreen>(Screens));
        Log("final results shown");
        await Press(ButtonText(final, "Back to Title")!, "Back to Title");
        title = await WaitNode("title screen (2)", () => Find<TitleScreen>(Screens));
        Log("back at title");
        await Press(ButtonText(title, "Quit")!, "Quit");
        await Frames(5);
    }
}
