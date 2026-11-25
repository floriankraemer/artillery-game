using Godot;

namespace Artillery.Game.Ui;

/// <summary>User preferences persisted in user://settings.cfg, section "ui".</summary>
public static class UiSettings
{
    public const string FilePath = "user://settings.cfg";
    public static float AnimationSpeed { get; set; } = 1f;
    public static bool FollowProjectile { get; set; } = false;
    public static float MasterVolume { get; set; } = 1f;
    private static bool _loaded;

    public static void Load()
    {
        _loaded = true;
        var cf = new ConfigFile();
        if (cf.Load(FilePath) != Error.Ok) return;
        AnimationSpeed = Math.Clamp((float)(double)cf.GetValue("ui", "animation_speed", 1.0), 0.25f, 4f);
        FollowProjectile = (bool)cf.GetValue("ui", "camera_follow", false);
        MasterVolume = Math.Clamp((float)(double)cf.GetValue("ui", "master_volume", 1.0), 0f, 1f);
    }

    public static void EnsureLoaded() { if (!_loaded) Load(); }

    public static void Save()
    {
        var cf = new ConfigFile();
        cf.Load(FilePath); // keep other sections (saved match settings)
        cf.SetValue("ui", "animation_speed", (double)AnimationSpeed);
        cf.SetValue("ui", "camera_follow", FollowProjectile);
        cf.SetValue("ui", "master_volume", (double)MasterVolume);
        cf.Save(FilePath);
    }
}
