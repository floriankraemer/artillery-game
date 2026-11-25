using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Util;

namespace Artillery.Core.Combat;

/// <summary>Death handler.</summary>
public static class Deaths
{
    /// <summary>
    /// Processes tanks with Hull == HullDead: tally +200, clear armour and dead-man's bomb, explode (Crater + StandardBlast of radius
    /// <c>DeathRadius</c>, 25 with a dead-man's bomb, credited to <paramref name="currentShooter"/>; chain reactions are handled by rescanning),
    /// mark HullGone, emit TankDiedEvent. Afterwards every living tank gets +200 per death. Off-map tanks (HullOffMap) emit
    /// TankDiedEvent(offMap: true) once, no explosion, no bonus.
    /// </summary>
    public static void Resolve(MatchState s, int currentShooter, List<GameEvent> ev)
    {
        int deaths = 0;
        bool again = true;
        while (again)
        {
            again = false;
            foreach (var t in s.AllTanks)
            {
                if (t.Hull == Tank.HullOffMap)
                {
                    t.Hull = Tank.HullGone;
                    ev.Add(new TankDiedEvent(t.Index, true));
                    continue;
                }
                if (t.Hull != Tank.HullDead) continue;
                deaths++;
                int r = t.DeathRadius;
                if (t.Has(UtilityId.DeadMansBomb)) r = Math.Max(r, Balance.DeathNukeRadius);
                t.Utilities[(int)UtilityId.HullArmour] = 0;
                t.Utilities[(int)UtilityId.DeadMansBomb] = 0;
                var at = new Vec3d(t.X, t.Y, t.Z);
                Blast.Crater(s, at, r, ev);
                Blast.StandardBlast(s, currentShooter, Blast.BonusDef(s, currentShooter), at, r, ev);
                t.Hull = Tank.HullGone;
                t.Shield = 0;
                ev.Add(new TankDiedEvent(t.Index, false));
                again = true;
                break;     // rescan from player 1
            }
        }
        if (deaths == 0) return;
        foreach (var t in s.LivingTanks)
            Blast.AddScore(s, t.Index, (long)Balance.DeathBonusToSurvivors * deaths, "tank destroyed", ev);
    }
}
