using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Util;

namespace Artillery.Core.Physics;

/// <summary>Tank falling, parachute, settling and drowning.</summary>
public static class TankPhysics
{
    /// <summary>
    /// Fall damage after terrain changes. <paramref name="oldZ"/> maps tank index to its z before the change.
    /// BUG FIX: only downward falls hurt. Fall under 6 is ignored; a parachute cancels a fall above 5. Does not move tanks (see <see cref="Settle"/>).
    /// </summary>
    public static void ApplyFalls(MatchState s, Dictionary<int, double> oldZ, List<GameEvent> ev)
    {
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive || !oldZ.TryGetValue(t.Index, out double z0)) continue;
            double ground = s.World.Height.HeightAt(t.X + 0.5, t.Y + 0.5);
            int fall = -Pas.Trunc(ground - Pas.Trunc(z0));
            if (fall <= 0) continue;
            if (t.Has(UtilityId.Parachute) && fall > Balance.ParachuteThreshold)
            {
                if (t.Utilities[(int)UtilityId.Parachute] != Tank.Infinite) t.Utilities[(int)UtilityId.Parachute]--;
                fall = 0;
            }
            if (fall < Balance.MinFallForDamage) continue;
            int hull0 = t.Hull, sh0 = t.Shield;
            t.ApplyDamage(fall * Balance.FallDamagePerUnit);
            int shD = sh0 - t.Shield;
            int hullD = hull0 - (t.Hull == Tank.HullDead ? 0 : t.Hull);
            ev.Add(new TankDamagedEvent(t.Index, hullD, shD, t.Hull, t.Shield));
        }
    }

    /// <summary>Drops every tank onto the terrain/water. Without Floats a tank below the water line drowns (hull = HullDead, shield 0).</summary>
    public static void Settle(MatchState s, List<GameEvent> ev)
    {
        var w = s.World;
        foreach (var t in s.AllTanks)
        {
            if (t.Hull <= Tank.HullOffMap) continue;
            var from = t.Position;
            double z = w.Height.HeightAt(t.X + 0.5, t.Y + 0.5) + 0.5;
            if (w.HasWater)
            {
                if (w.WaterLevel > z && t.Alive && !t.Has(UtilityId.Floats))
                {
                    z = w.WaterLevel;
                    ev.Add(new TankDamagedEvent(t.Index, t.Hull, t.Shield, Tank.HullDead, 0));
                    t.Shield = 0; t.Hull = Tank.HullDead;
                }
                if (w.WaterLevel >= z) z = w.WaterLevel + 0.5;
            }
            t.Z = z;
            if (Math.Abs(z - from.Z) > 1e-9)
                ev.Add(new TankMovedEvent(t.Index, from, t.Position, z < from.Z ? MoveKind.Fall : MoveKind.Settle));
        }
    }
}
