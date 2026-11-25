using Godot;
using Artillery.Core;
using Artillery.Core.Items;
using Artillery.Core.Match;

namespace Artillery.Game.Ui;

/// <summary>Shop for one tank: buy from three lists, sell from the fitted list. Uses <see cref="Shop"/> for every rule.</summary>
public partial class ShopScreen : Control
{
    public event Action? Done;

    private MatchState _state = null!;
    private Tank _tank = null!;
    private bool _ready;

    private Label _name = null!, _cash = null!, _free = null!, _round = null!, _msg = null!;
    private ColorRect _swatch = null!;
    private VBoxContainer _invBox = null!;
    private ScrollContainer _invScroll = null!;
    private readonly List<Action> _refreshers = new();

    /// <summary>Binds the match and the shopping tank.</summary>
    public void Init(MatchState s, Tank t)
    {
        _state = s; _tank = t;
        Shop.PrepareForShopping(t);
        if (_ready) Build();
    }

    public override void _Ready()
    {
        _ready = true;
        UiKit.Root(this);
        if (_tank != null) Build();
    }

    private void Build()
    {
        foreach (var c in GetChildren().Skip(1)) { RemoveChild(c); c.QueueFree(); }
        _refreshers.Clear();
        var margin = new MarginContainer().Full();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        AddChild(margin);
        var outer = new VBoxContainer(); margin.AddChild(outer);

        // header
        var hp = new PanelContainer(); outer.AddChild(hp);
        var h = new HBoxContainer(); hp.AddChild(h);
        _swatch = UiKit.Swatch(UiTheme.PlayerColor(_tank.Index), 34);
        h.AddChild(_swatch);
        _name = UiKit.Lbl("", "Heading"); h.AddChild(_name);
        h.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _round = UiKit.Lbl("", "Dim"); h.AddChild(_round);
        h.AddChild(new Control { CustomMinimumSize = new Vector2(30, 0) });
        _cash = UiKit.Lbl("", "Heading", col: UiTheme.Accent); h.AddChild(_cash);
        h.AddChild(new Control { CustomMinimumSize = new Vector2(30, 0) });
        _free = UiKit.Lbl("", "Heading"); h.AddChild(_free);

        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 14);
        outer.AddChild(cols);

        cols.AddChild(Column("Weapons", 1.25f, box =>
        {
            foreach (var w in ItemCatalog.ShopWeapons)
            {
                var d = w; var id = w.Id;
                AddBuyRow(box, d.Name, d.Price, d.Space, WeaponDesc(d),
                    () => Shop.CanBuy(_tank, id), () => Shop.Buy(_tank, id), () => _tank.Count(id), false);
            }
        }));
        cols.AddChild(Column("Utilities", 1.1f, box =>
        {
            foreach (var u in ItemCatalog.Utilities)
            {
                var d = u; var id = u.Id;
                AddBuyRow(box, d.Name, d.Price, d.Space, d.Description,
                    () => Shop.CanBuy(_tank, id), () => Shop.Buy(_tank, id), () => _tank.Count(id), true, () => d.MaxCount);
            }
        }));
        var casingCol = Column("Casings", 0.9f, box =>
        {
            foreach (var c in ItemCatalog.Casings)
            {
                var d = c; var id = c.Id;
                AddBuyRow(box, d.Name, d.Price, id == CasingId.Impact ? 0 : Balance.CasingSpace, CasingDesc(id),
                    () => Shop.CanBuy(_tank, id), () => Shop.Buy(_tank, id), () => _tank.Count(id), false);
            }
        });
        cols.AddChild(casingCol);

        var invCol = Column("Fitted (click to sell)", 1.0f, box => { _invBox = box; });
        cols.AddChild(invCol);
        _invScroll = (ScrollContainer)((Control)invCol.GetChild(0)).GetChild(1);

        var bar = new HBoxContainer(); outer.AddChild(bar);
        _msg = UiKit.Lbl("Click an item to buy it. Prices are refunded in full when selling.", "Dim");
        _msg.SizeFlagsHorizontal = SizeFlags.ExpandFill; _msg.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        bar.AddChild(_msg);
        var done = UiKit.Btn("Done", () => Done?.Invoke(), "Primary", 200);
        bar.AddChild(done);
        Refresh();
    }

    private Control Column(string title, float stretch, Action<VBoxContainer> fill)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = stretch };
        var v = new VBoxContainer(); p.AddChild(v);
        v.AddChild(UiKit.Lbl(title, "Heading"));
        var sc = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(sc);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 3);
        sc.AddChild(box);
        fill(box);
        return p;
    }

    private static string WeaponDesc(WeaponDef d)
    {
        string eff = d.Effect switch
        {
            WeaponEffect.Explosive => "Explosive shell",
            WeaponEffect.ShieldBreaker => "Drains enemy shields",
            WeaponEffect.Napalm => "Spreads burning fuel",
            WeaponEffect.SuperNapalm => "Spreads a large burning fuel pool",
            WeaponEffect.Goo => "Sticky goo that blocks tanks",
            WeaponEffect.Teleport => "Teleports your tank to the impact point",
            WeaponEffect.Scatter => "Splits into several bomblets",
            WeaponEffect.AcidBomb => "Dissolves terrain",
            WeaponEffect.AcidFlood => "Dissolves a wide area of terrain",
            WeaponEffect.Flamethrower => "Short range flame, no flight",
            WeaponEffect.Earthquake => "Shakes the terrain flat",
            WeaponEffect.Extinguisher => "Puts out fires",
            WeaponEffect.WallMaker => "Raises a wall of earth",
            WeaponEffect.AirStrike => "Calls bombers over the target",
            WeaponEffect.HillMaker => "Raises a hill",
            WeaponEffect.Shockwave => "Pushes terrain and tanks away",
            WeaponEffect.Volcano => "Creates an erupting volcano",
            _ => d.Name,
        };
        var s = eff;
        if (d.Radius > 0) s += $"\nRadius {d.Radius}";
        if (!d.Projectile) s += "\nNo projectile flight";
        return s;
    }

    private static string CasingDesc(CasingId c) => c switch
    {
        CasingId.Impact => "Explodes on first contact (always available).",
        CasingId.Bouncing => "Bounces across the ground before exploding.",
        CasingId.Rolling => "Rolls downhill after landing.",
        CasingId.HeatSeeking => "Steers toward the nearest tank.",
        CasingId.Tunneling => "Burrows into the ground before exploding.",
        CasingId.Cruise => "Holds altitude and dives on the target.",
        _ => "Cruise casing with improved targeting.",
    };

    private void AddBuyRow(VBoxContainer box, string name, int price, int space, string tip,
        Func<bool> canBuy, Func<bool> buy, Func<int> count, bool utility, Func<int>? max = null)
    {
        var b = new Button { CustomMinimumSize = new Vector2(0, 32), TooltipText = $"{name}\n{tip}", Alignment = HorizontalAlignment.Left };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }.Full();
        row.AddThemeConstantOverride("separation", 6);
        row.OffsetLeft = 8; row.OffsetRight = -8;
        b.AddChild(row);
        var n = UiKit.Lbl(name); n.SizeFlagsHorizontal = SizeFlags.ExpandFill; n.MouseFilter = MouseFilterEnum.Ignore; n.ClipText = true;
        var p = UiKit.Cell(price == 0 ? "free" : "$" + price, null, true, 58); p.MouseFilter = MouseFilterEnum.Ignore;
        var s = UiKit.Cell(space == 0 ? "-" : space.ToString(), UiTheme.Dim, true, 32); s.MouseFilter = MouseFilterEnum.Ignore;
        var o = UiKit.Cell("", UiTheme.Accent, true, 44); o.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(n); row.AddChild(p); row.AddChild(s); row.AddChild(o);
        b.Pressed += () =>
        {
            if (!buy()) _msg.Text = "Cannot buy " + name + ".";
            else _msg.Text = $"Bought {name} for ${price}.";
            Refresh();
        };
        box.AddChild(b);
        _refreshers.Add(() =>
        {
            int c = count();
            bool atMax = utility && max != null && c >= max();
            o.Text = atMax ? "MAX" : c == Tank.Infinite ? "inf" : c > 0 ? "x" + c : "";
            bool ok = canBuy();
            b.Disabled = !ok;
            var dim = new Color(0.45f, 0.49f, 0.53f);
            n.AddThemeColorOverride("font_color", ok ? UiTheme.Text : dim);
            p.AddThemeColorOverride("font_color", ok ? UiTheme.Text : dim);
        });
    }

    private void Refresh()
    {
        _name.Text = _tank.Name;
        _name.AddThemeColorOverride("font_color", UiTheme.PlayerColor(_tank.Index));
        _round.Text = $"Round {_state.Round} of {_state.Settings.Rounds}";
        _cash.Text = "Cash $" + _tank.Cash;
        int free = Shop.FreeSpace(_tank);
        _free.Text = "Free space " + free;
        _free.AddThemeColorOverride("font_color", free < 10 ? UiTheme.Bad : UiTheme.Text);
        foreach (var r in _refreshers) r();
        RebuildInventory();
    }

    private void RebuildInventory()
    {
        int scroll = _invScroll.ScrollVertical;
        UiKit.Clear(_invBox);
        void Header(string t) => _invBox.AddChild(UiKit.Lbl(t, "Dim"));
        void Row(string name, int count, int price, Func<bool> sell)
        {
            bool inf = count == Tank.Infinite;
            var b = new Button { Text = "", CustomMinimumSize = new Vector2(0, 30), Disabled = inf, TooltipText = inf ? name + " (always available)" : $"Sell {name} for ${price}" };
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }.Full();
            row.OffsetLeft = 8; row.OffsetRight = -8;
            var n = UiKit.Lbl(name); n.SizeFlagsHorizontal = SizeFlags.ExpandFill; n.MouseFilter = MouseFilterEnum.Ignore; n.ClipText = true;
            var c = UiKit.Cell(inf ? "inf" : "x" + count, UiTheme.Accent, true, 44); c.MouseFilter = MouseFilterEnum.Ignore;
            var pr = UiKit.Cell(inf ? "" : "+$" + price, UiTheme.Good, true, 62); pr.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(n); row.AddChild(c); row.AddChild(pr); b.AddChild(row);
            b.Pressed += () =>
            {
                _msg.Text = sell() ? $"Sold {name} for ${price}." : $"Cannot sell {name} (cargo space would go negative).";
                Refresh();
            };
            _invBox.AddChild(b);
        }
        bool any = false;
        bool first = true;
        foreach (var w in ItemCatalog.ShopWeapons)
            if (_tank.Count(w.Id) > 0) { if (first) { Header("Weapons"); first = false; } any = true; var id = w.Id; Row(w.Name, _tank.Count(id), w.Price, () => Shop.Sell(_tank, id)); }
        first = true;
        foreach (var u in ItemCatalog.Utilities)
            if (_tank.Count(u.Id) > 0) { if (first) { Header("Utilities"); first = false; } any = true; var id = u.Id; Row(u.Name, _tank.Count(id), u.Price, () => Shop.Sell(_tank, id)); }
        first = true;
        foreach (var c in ItemCatalog.Casings)
            if (_tank.Count(c.Id) > 0) { if (first) { Header("Casings"); first = false; } any = true; var id = c.Id; Row(c.Name, _tank.Count(id), c.Price, () => Shop.Sell(_tank, id)); }
        if (!any) _invBox.AddChild(UiKit.Lbl("Nothing fitted yet.", "Dim"));
        Callable.From(() => _invScroll.ScrollVertical = scroll).CallDeferred();
    }
}
