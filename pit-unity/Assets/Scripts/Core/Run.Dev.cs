// Dev panel hooks (Game/DevPanel.cs): showcase controls that bend the run's rules. The sims and the bot never call
// them, so balance sweeps are unaffected.
using System.Collections.Generic;

namespace Pit.Core
{
    public sealed partial class Run
    {
        /// <summary>Dev: the hero takes no damage.</summary>
        public bool God;

        /// <summary>Dev: n level-ups, each with its pick (the level-up menu opens when play resumes).</summary>
        public void DevLevels(int n)
        {
            for (int i = 0; i < n; i++) GainXp(System.Math.Max(1, M.XpNeed(P.Level) - P.Xp));
        }

        public void DevHeal()
        {
            P.Hp = P.MaxHp;
            P.PoisonT = P.BurnT = P.RootT = P.SlowT = 0;
        }

        public void DevGold(int n) { Gold += n; }

        /// <summary>Dev: every foe on the board dies as if hit (gems drop); the boss is left alone.</summary>
        public void DevClear()
        {
            foreach (var e in new List<Enemy>(Enemies))
                if (!e.Dead && !e.Boss && e.Parent == null) KillEnemy(e, false);
        }

        public void DevKillBoss() { if (Boss != null && !Boss.Dead) KillEnemy(Boss, false); }

        /// <summary>
        /// Dev: straight to boss index (0 = the first of L.Bosses; the last is the final boss). The board empties without
        /// drops, the turn counter and the clock jump to that boss's schedule, and the usual expansion plays first.
        /// </summary>
        public void DevBoss(int index)
        {
            index = System.Math.Max(0, System.Math.Min(index, L.Bosses.Length - 1));
            Enemies.Clear();
            Boss = null; Expand = null; FinalStarted = false;
            Shots.Clear(); Hazards.Clear();
            BossesPassed = index;
            if (index < L.BossTurns.Length) Turn = System.Math.Max(Turn, L.BossTurns[index]);
            else
            {
                if (L.BossTurns.Length > 0) Turn = System.Math.Max(Turn, L.BossTurns[L.BossTurns.Length - 1] + 1);
                Skipped = M.Max(Skipped, L.TargetSeconds - 2 * L.TurnLen - Time);
            }
            if (Mode == Mode.Intro) Mode = Mode.Playing;
            StartBoss();
        }

        /// <summary>Dev: a ball in a free slot (false when the slots are full).</summary>
        public bool DevAddBall(string id)
        {
            if (Balls.Count >= Slots || !Content.Balls.ContainsKey(id)) return false;
            AddBall(id);
            return true;
        }

        public void DevRemoveBall(OwnedBall b)
        {
            Field.RemoveAll(f => f.Inst == b);
            if (Volley != null) Volley.Q.Remove(b);
            Balls.Remove(b);
        }

        public void DevLevelBall(OwnedBall b, int d)
        {
            b.Level = System.Math.Max(1, System.Math.Min(3, b.Level + d));
            b.Stats = BallStats(b.Id, b.Level);
        }

        public bool DevAddItem(string id)
        {
            if (Items.Count >= Slots || !Content.Items.ContainsKey(id) || ItemLv(id) > 0) return false;
            AddItem(id);
            return true;
        }

        public void DevRemoveItem(OwnedItem it) { Items.Remove(it); Derive(); }

        public void DevLevelItem(OwnedItem it, int d)
        {
            it.Level = System.Math.Max(1, System.Math.Min(3, it.Level + d));
            Derive();
        }
    }
}
