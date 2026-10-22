// -pitScreen readme -pitKind fight|boss|ray|levelup|close [-pitExposure 0.9] [-pitBoss 0] [-pitFrom 25 -pitTo 40] [-pitLead 30] [-pitLevels 12] [-pitPick 0]
// Store / README shots. The bot plays a fixed run (seed 1234) with Ember, Rime, Spark and Ray at level 3 in the four
// slots, +Lead (more baby balls, so the board is busy) and the hero in god mode. Pass 1 plays the run in the core alone and scores every frame of the shot window
// (balls in flight, a launch in the last 0.1 s so the muzzle flash shows, burning and frozen foes, recent chains and row
// beams); pass 2 replays the same run through the views up to the best frame. The exposure lift is a capture-only
// volume; nothing in the game changes.
using System;
using System.Collections.Generic;
using Pit.Core;
using Pit.Game;
using UnityEngine;
#if PIT_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Pit.EditorTools
{
    public static partial class PitCapture
    {
        static readonly string[] ReadmeBalls = { "ember", "frost", "spark", "ray" };

        static void ReadmeLoadout(Pit.Core.Run r)
        {
            foreach (var b in new List<OwnedBall>(r.Balls))
                if (Array.IndexOf(ReadmeBalls, b.Id) < 0 || b.Fused != null) r.DevRemoveBall(b);
            foreach (var id in ReadmeBalls)
                if (!r.Balls.Exists(b => b.Id == id)) r.DevAddBall(id);
            foreach (var b in r.Balls) if (b.Level < 3) r.DevLevelBall(b, 3);
        }

        /// <summary>The scripted run, identical in both passes. Returns the run after frame index `stop` (or the window's end).</summary>
        sealed class ReadmeScript
        {
            public string Kind;
            public int From, To, BossIndex, Lead, Levels;
            public bool Loaded, BossCalled;
            public int Frame;
            public int LastLaunch = -999, LastArc = -999, LastRow = -999, RowTargets, Launches;
            readonly HashSet<Beam> seen = new HashSet<Beam>();

            /// <summary>One step of the run; false when the window is over. Picks do not count as frames.</summary>
            public bool Step(Pit.Core.Run r)
            {
                if (r.IsOver || r.Time > 3000) return false;
                if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) { Bot.AutoPick(r); ReadmeLoadout(r); return true; }
                if (!Loaded && r.Mode == Mode.Playing && r.Balls.Count > 0) { Loaded = true; r.God = true; ReadmeLoadout(r); r.P.Stats[(int)Stat.Lead] += Lead; r.Derive(); r.DevLevels(Levels); }
                if (Kind == "boss" && Loaded && !BossCalled && r.Turn >= From) { BossCalled = true; r.DevBoss(BossIndex); }
                if (Kind == "boss" && r.Boss != null && !r.Boss.Dead) r.Boss.Hp = Math.Max(r.Boss.Hp, r.Boss.MaxHp * 0.6f);
                r.P.Hp = r.P.MaxHp;
                Bot.Drive(r);
                r.Step(1 / 60f);
                Frame++;
                if (r.Launches != Launches) { Launches = r.Launches; LastLaunch = Frame; }
                foreach (var b in r.Beams)
                {
                    if (!seen.Add(b)) continue;
                    if (b.Kind == BeamKind.Arc) LastArc = Frame;
                    if (b.Kind == BeamKind.Row) { LastRow = Frame; RowTargets = b.Targets != null ? b.Targets.Count : 0; }
                }
                seen.RemoveWhere(b => !r.Beams.Contains(b));
                return true;
            }

            public bool InWindow(Pit.Core.Run r)
            {
                if (r.Mode != Mode.Playing || !Loaded) return false;
                if (Kind == "boss") return BossCalled && r.Boss != null && !r.Boss.Dead && r.Expand == null && r.Boss.Entry <= 0;
                return r.Turn >= From && r.Turn <= To;
            }

            public float Score(Pit.Core.Run r)
            {
                int field = 0, burning = 0, frozen = 0, foes = 0;
                foreach (var b in r.Field) if (!b.Gone) field++;
                foreach (var e in r.Enemies)
                {
                    if (e.Dead) continue;
                    foes++;
                    if (r.Has(e, "burn")) burning++;
                    if (r.Has(e, "freeze")) frozen++;
                }
                bool flash = Frame - LastLaunch <= 5;
                if (Kind == "ray")
                {
                    // the beam 5 frames old: fully drawn, its hits flashing
                    if (Frame - LastRow != 5 || RowTargets < 2) return -1;
                    return RowTargets * 6 + Math.Min(burning, 6) * 3 + Math.Min(frozen, 6) * 3 + Math.Min(field, 20);
                }
                if (field < (Kind == "boss" ? 10 : 14)) return -1;
                float s = Math.Min(field, 25) * 3 + Math.Min(burning, 5) * 4 + Math.Min(frozen, 4) * 10 + Math.Min(foes, 16) * 2;
                if (Frame - LastArc <= 20) s += 8;
                if (Frame - LastRow <= 25) s += 8;
                if (flash) s += 20; else if (Kind == "close" || Kind == "fight") return -1;
                return s;
            }
        }

        static ReadmeScript NewScript(string kind)
        {
            return new ReadmeScript
            {
                Kind = kind,
                From = int.Parse(Arg("-pitFrom", kind == "boss" ? "6" : "25")),
                To = int.Parse(Arg("-pitTo", "40")),
                BossIndex = int.Parse(Arg("-pitBoss", "0")),
                Lead = int.Parse(Arg("-pitLead", "30")),
                Levels = int.Parse(Arg("-pitLevels", kind == "levelup" ? "5" : "12")),
            };
        }

        /// <summary>Plays `r` (fresh) to the chosen frame with the views drawn over the last 4 s, so trails and statuses are live.</summary>
        static void Readme(Pit.Core.Run r, string heroId, Board3D board, WorldView world, Gfx gfx, Ui ui, Screens screens, float w, float h, float scale, Transform root)
        {
            string kind = Arg("-pitKind", "fight");
            int pick = int.Parse(Arg("-pitPick", "0"));   // 0 = best frame, n = the n-th best (another moment, same run)

            // pass 1: the core alone
            var scan = new Pit.Core.Run(r.Seed, heroId);
            var s1 = NewScript(kind);
            var best = new List<KeyValuePair<float, int>>();
            int lastIn = -1;
            int maxField = 0;
            while (s1.Step(scan))
            {
                if (!s1.InWindow(scan)) { if (lastIn >= 0 && kind != "boss" && scan.Turn > s1.To) break; continue; }
                lastIn = s1.Frame;
                maxField = Math.Max(maxField, scan.Field.Count);
                if (kind == "boss" && s1.Frame > 60 * 400) break;
                float sc = s1.Score(scan);
                if (sc > 0) best.Add(new KeyValuePair<float, int>(sc, s1.Frame));
            }
            if (best.Count == 0) throw new Exception("readme " + kind + ": no frame in the window scored (turn " + scan.Turn + " frame " + s1.Frame + " mode " + scan.Mode + " launches " + s1.Launches + " max field " + maxField + " balls " + scan.Balls.Count + " babies " + scan.Babies + " over " + scan.IsOver + ")");
            best.Sort((a, b) => b.Key.CompareTo(a.Key));
            // distinct moments: skip frames within 1 s of a better one
            var picks = new List<KeyValuePair<float, int>>();
            foreach (var kv in best)
            {
                if (picks.Exists(p => Math.Abs(p.Value - kv.Value) < 60)) continue;
                picks.Add(kv);
                if (picks.Count > pick) break;
            }
            int target = picks[Math.Min(pick, picks.Count - 1)].Value;
            Debug.Log("PitCapture: readme " + kind + " candidates " + best.Count + " pick frame " + target + " score " + picks[Math.Min(pick, picks.Count - 1)].Key);

            // pass 2: the same run, drawn
            RicochetVfx.VfxBank.Ensure().manualTick = true;
            var s2 = NewScript(kind);
            while (s2.Frame < target && s2.Step(r))
            {
                if (target - s2.Frame > 240) continue;
                gfx.Time = r.Time; gfx.Begin(w, h, scale); ui.BeginFrame();
                world.Layout(r, screens.HudTop, screens.HudBottom); board.Frame(r, world, gfx); world.Draw(r);
                bool p = false; screens.Hud(r, world, ref p);
                ui.EndFrame(); gfx.End();
                RicochetVfx.VfxBank.Instance.Tick(1 / 60f);
            }
            int field = 0; foreach (var b in r.Field) if (!b.Gone) field++;
            Debug.Log("PitCapture: readme " + kind + " frame " + s2.Frame + " turn " + r.Turn + " field " + field + " foes " + r.Enemies.Count +
                      " launch age " + (s2.Frame - s2.LastLaunch) + " row age " + (s2.Frame - s2.LastRow) + " row targets " + s2.RowTargets +
                      " burning " + board.VfxBurning + " frozen " + board.VfxFrozen + " boss " + (r.Boss != null ? r.Boss.Type : "-") + " babies " + r.Babies + " cols " + r.Cols);
            if (kind == "levelup") { r.DevLevels(1); r.OpenLevelUp(); Debug.Log("PitCapture: readme levelup mode " + r.Mode + " options " + (r.Menu != null ? r.Menu.Options.Count : 0)); }

#if PIT_URP
            float ev = float.Parse(Arg("-pitExposure", "0.9"), System.Globalization.CultureInfo.InvariantCulture);
            var vg = new GameObject("Capture Exposure");
            vg.transform.SetParent(root, false);
            var vol = vg.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 2;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(ev);
            ca.saturation.Override(float.Parse(Arg("-pitSat", "8"), System.Globalization.CultureInfo.InvariantCulture));
            vol.sharedProfile = profile;
#endif
        }
    }
}
