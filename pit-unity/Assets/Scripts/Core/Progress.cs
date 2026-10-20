// Level-up offers and the Fuser (free upgrades / combine / evolve). Pure rules, no drawing.
using System.Collections.Generic;

namespace Pit.Core
{
    public sealed partial class Run
    {
        // ------------------------------------------------------------ eligibility (spec 5.2)
        bool OwnsBurn()
        {
            foreach (var b in Balls) if (b.Stats.Has(K.BurnMin) || b.Stats.Has(K.FfMin)) return true;
            return false;
        }

        static void AddIngredients(string id, HashSet<string> set)
        {
            var d = Content.Balls[id];
            if (d.Recipe == null) return;
            foreach (var r in d.Recipe) { set.Add(r); AddIngredients(r, set); }
        }

        void Candidates(bool ball, List<Option> ups, List<Option> news)
        {
            if (ball)
            {
                foreach (var b in Balls) if (b.Level < 3 && b.Fused == null) ups.Add(new Option { Kind = OptKind.BallUp, Ball = b });
                if (Balls.Count >= Slots) return;
                var taken = new HashSet<string>();
                foreach (var b in Balls) { taken.Add(b.Id); if (b.Fused != null) taken.Add(b.Fused); AddIngredients(b.Id, taken); }
                foreach (var id in Content.BaseBalls) if (!taken.Contains(id)) news.Add(new Option { Kind = OptKind.BallNew, Id = id });
            }
            else
            {
                foreach (var it in Items) if (it.Level < 3) ups.Add(new Option { Kind = OptKind.ItemUp, Item = it });
                if (Items.Count >= Slots) return;
                var banned = new HashSet<string>();
                foreach (var it in Items) { banned.Add(it.Id); if (it.Def.Recipe != null) foreach (var r in it.Def.Recipe) banned.Add(r); }
                foreach (var id in Content.BaseItems)
                {
                    if (banned.Contains(id)) continue;
                    if (Content.Items[id].Needs == "burn" && !OwnsBurn()) continue;
                    news.Add(new Option { Kind = OptKind.ItemNew, Id = id });
                }
            }
        }

        public List<Option> RollLevelOptions()
        {
            var rng = new Rng(Seed + 7919L * PointsUsed + 104729L * Rerolls + 1299709L * AdRerolls + 13);
            var opts = new List<Option>();
            var seen = new HashSet<string>();
            for (int n = 0; n < 3; n++)
            {
                var cats = new List<bool>();
                foreach (bool c in new[] { true, false })
                {
                    var u = new List<Option>(); var nw = new List<Option>();
                    Candidates(c, u, nw);
                    if (u.Exists(o => !seen.Contains(o.Key)) || nw.Exists(o => !seen.Contains(o.Key))) cats.Add(c);
                }
                if (cats.Count == 0) break;
                bool cat = rng.Pick(cats);
                var ups = new List<Option>(); var news = new List<Option>();
                Candidates(cat, ups, news);
                ups.RemoveAll(o => seen.Contains(o.Key)); news.RemoveAll(o => seen.Contains(o.Key));
                var pool = (ups.Count > 0 && (rng.Chance(0.4f) || news.Count == 0)) ? ups : news;
                if (pool.Count == 0) pool = ups;
                var pick = rng.Pick(pool);
                seen.Add(pick.Key);
                opts.Add(pick);
            }
            return opts;
        }

        public void ApplyOption(Option o)
        {
            switch (o.Kind)
            {
                case OptKind.BallNew: AddBall(o.Id); break;
                case OptKind.BallUp: LevelBall(o.Ball); break;
                case OptKind.ItemNew: AddItem(o.Id); break;
                default: LevelItem(o.Item); break;
            }
        }

        public int RerollCost { get { int n = Rerolls; return 5 * (n + 1) * (n + 2) / 2; } }

        // ------------------------------------------------------------ menu flow
        public void CheckMenus()
        {
            if (PendingFuser > 0) { PendingFuser--; OpenFuser(); return; }
            if (P.Points > 0 && AutoMenu) OpenLevelUp();
        }

        public void OpenLevelUp()
        {
            if (P.Points <= 0 || (Mode != Mode.Playing && Mode != Mode.LevelUp)) return;
            var opts = RollLevelOptions();
            if (opts.Count == 0)
            {
                // nothing left to offer: resources instead
                P.Points--; PointsUsed++;
                Gold += 25; P.Hp = System.Math.Min(P.MaxHp, P.Hp + 10);
                Text(P.X, P.Y - 1, "+25g +10hp", 0xffd84a);
                return;
            }
            Menu = new Menu { Type = MenuType.LevelUp, Options = opts };
            Mode = Mode.LevelUp;
        }

        public void ChooseLevel(int i)
        {
            if (Menu == null || Menu.Type != MenuType.LevelUp || i < 0 || i >= Menu.Options.Count) return;
            ApplyOption(Menu.Options[i]);
            P.Points--; PointsUsed++;
            AutoMenu = true;
            CloseMenu();
        }

        public bool RerollLevel()
        {
            if (Menu == null || Gold < RerollCost) return false;
            Stats.GoldSpent += RerollCost; Gold -= RerollCost; Rerolls++;
            Menu.Options = RollLevelOptions();
            return true;
        }

        /// <summary>Rewarded reroll (ads.md R2): free, and the paid reroll price doesn't move.</summary>
        public bool RerollFree()
        {
            if (Menu == null || Menu.Type != MenuType.LevelUp) return false;
            AdRerolls++; AdWatched = true;
            Menu.Options = RollLevelOptions();
            return true;
        }

        public void LaterLevel() { AutoMenu = false; CloseMenu(); }

        public void CloseMenu()
        {
            Menu = null; Mode = Mode.Playing;
            CheckMenus();
        }

        // ------------------------------------------------------------ fuser (spec 5.4)
        static string RecipeFor(string a, string b)
        {
            foreach (var kv in Content.Balls)
            {
                var r = kv.Value.Recipe;
                if (r != null && r.Length == 2 && ((r[0] == a && r[1] == b) || (r[0] == b && r[1] == a))) return kv.Key;
            }
            return null;
        }
        static bool IsIngredient(string a, string b)
        {
            var ra = Content.Balls[a].Recipe; var rb = Content.Balls[b].Recipe;
            return (ra != null && System.Array.IndexOf(ra, b) >= 0) || (rb != null && System.Array.IndexOf(rb, a) >= 0);
        }
        static string PairKey(string a, string b) { return string.CompareOrdinal(a, b) < 0 ? a + "+" + b : b + "+" + a; }

        /// <summary>Ordered (host, partner) pairs that may be combined.</summary>
        public List<OwnedBall[]> ComboPairs()
        {
            var pairs = new List<OwnedBall[]>();
            foreach (var a in Balls)
                foreach (var b in Balls)
                {
                    if (a == b || a.Level < 3 || b.Level < 3 || a.Fused != null || b.Fused != null || a.Id == b.Id) continue;
                    if (IsIngredient(a.Id, b.Id) || RecipeFor(a.Id, b.Id) != null || BannedCombos.Contains(PairKey(a.Id, b.Id))) continue;
                    pairs.Add(new[] { a, b });
                }
            return pairs;
        }

        public List<Recipe> EvolveRecipes()
        {
            var outp = new List<Recipe>();
            foreach (var kv in Content.Balls)
            {
                var r = kv.Value.Recipe;
                if (r == null) continue;
                var parts = new List<OwnedBall>();
                foreach (var x in r)
                {
                    var p = Balls.Find(b => b.Id == x && b.Level >= 3 && b.Fused == null);
                    if (p == null) { parts = null; break; }
                    parts.Add(p);
                }
                if (parts != null) outp.Add(new Recipe { IsBall = true, Id = kv.Key, BallParts = parts });
            }
            foreach (var kv in Content.Items)
            {
                var r = kv.Value.Recipe;
                if (r == null) continue;
                var parts = new List<OwnedItem>();
                foreach (var x in r)
                {
                    var p = Items.Find(it => it.Id == x && it.Level >= 3);
                    if (p == null) { parts = null; break; }
                    parts.Add(p);
                }
                if (parts != null) outp.Add(new Recipe { IsBall = false, Id = kv.Key, ItemParts = parts });
            }
            return outp;
        }

        public void OpenFuser()
        {
            Menu = new Menu { Type = MenuType.Fuser, View = FuserView.Main };
            Mode = Mode.Fuser;
        }

        /// <summary>The Free Upgrades piece count (spec §5.4 ①), with the streak corrections.</summary>
        int RollFreeCount()
        {
            double r = R.NextD();
            int n = r < 0.25 ? 1 : r < 0.6 ? 2 : r < 0.8 ? 3 : r < 0.95 ? 4 : 5;
            if (LastFreeCount == 1) n += 1;
            if (LastFreeCount == 5) n = System.Math.Min(n, 4);
            return M.Clamp(n, 1, 5);
        }

        /// <summary>Crucible R3 (ads.md): reveals a 4th card, Free Upgrades rolled again under the same rules, count shown.</summary>
        public void FuserExtraCard()
        {
            if (Menu == null || Menu.Type != MenuType.Fuser || Menu.ExtraFree > 0) return;
            Menu.ExtraFree = RollFreeCount(); AdWatched = true;
        }

        public void FuserFree() { FuserFree(0); }

        /// <summary>Free Upgrades; count > 0 applies a pre-rolled count (the ad card) instead of rolling.</summary>
        public void FuserFree(int count)
        {
            if (Menu == null) return;
            int n = count > 0 ? count : RollFreeCount();
            var cand = new List<object>();
            foreach (var b in Balls) if (b.Level < 3 && b.Fused == null) cand.Add(b);
            foreach (var it in Items) if (it.Level < 3) cand.Add(it);
            R.Shuffle(cand);
            if (cand.Count > 5) cand.RemoveRange(5, cand.Count - 5);
            var results = new List<ResultLine>();
            if (cand.Count == 0)
            {
                int g = R.Int(75, 150);
                Gold += g;
                results.Add(new ResultLine { Text = "+" + g + " gold (nothing left to upgrade)", Color = 0xffd84a });
            }
            else
            {
                int take = System.Math.Min(n, cand.Count), spare = n - take;
                for (int i = 0; i < take; i++)
                {
                    var c = cand[i];
                    var ob = c as OwnedBall; var oi = c as OwnedItem;
                    int lv = ob != null ? ob.Level : oi.Level, step = 1;
                    if (spare > 0 && lv == 1) { step = 2; spare--; }
                    if (ob != null) LevelBall(ob, step); else LevelItem(oi, step);
                    string name = ob != null ? ob.Def.Name : oi.Def.Name;
                    uint col = ob != null ? ob.Def.Color : oi.Def.Color;
                    results.Add(new ResultLine { Text = name + "  Lv " + lv + " → " + (ob != null ? ob.Level : oi.Level), Color = col });
                }
            }
            LastFreeCount = n;
            Menu.View = FuserView.Result;
            Menu.Title = n + " FREE UPGRADE" + (n > 1 ? "S" : "");
            Menu.Results = results;
        }

        public void FuserCombine(OwnedBall host, OwnedBall partner)
        {
            var a = host.Stats; var b = partner.Stats; var m = new Stats(a);
            foreach (var kv in b)
            {
                if (kv.Key == K.BonusMin || kv.Key == K.BonusMax) m[kv.Key] = a.G(kv.Key) + kv.Value;
                else if (kv.Key == K.Mult) m[K.Mult] = M.Round(a.G(K.Mult) * kv.Value / 100f);
                else if (!a.ContainsKey(kv.Key)) m[kv.Key] = kv.Value;
            }
            host.Stats = m;
            host.Fused = partner.Id;
            Balls.Remove(partner);
            foreach (var f in Field) if (f.Inst == partner) { f.Inst = null; f.Temp = true; f.Baby = true; }
            Menu.View = FuserView.Result;
            Menu.Title = "COMBINED";
            Menu.Results = new List<ResultLine>
            {
                new ResultLine { Text = BallName(host) + " (complete)", Color = host.Def.Color },
                new ResultLine { Text = DescribeBall(m), Color = 0xcfd6ff, Small = true }
            };
        }

        public void FuserEvolve(Recipe rec)
        {
            string name; uint col; string desc;
            if (rec.IsBall)
            {
                int slot = Balls.IndexOf(rec.BallParts[0]);
                foreach (var p in rec.BallParts)
                {
                    foreach (var f in Field) if (f.Inst == p) { f.Inst = null; f.Temp = true; f.Baby = true; }
                    Balls.Remove(p);
                }
                AddBall(rec.Id, System.Math.Min(slot, Balls.Count));
                var d = Content.Balls[rec.Id];
                name = d.Name; col = d.Color; desc = DescribeBall(BallStats(rec.Id, 1));
            }
            else
            {
                int slot = Items.IndexOf(rec.ItemParts[0]);
                foreach (var p in rec.ItemParts) Items.Remove(p);
                AddItem(rec.Id, System.Math.Min(slot, Items.Count));
                var d = Content.Items[rec.Id];
                name = d.Name; col = d.Color; desc = DescribeItem(rec.Id, 1);
            }
            Menu.View = FuserView.Result;
            Menu.Title = "EVOLVED";
            Menu.Results = new List<ResultLine>
            {
                new ResultLine { Text = name + "  Lv 1", Color = col },
                new ResultLine { Text = desc, Color = 0xcfd6ff, Small = true }
            };
        }

        public void FuserAbandon()
        {
            var combos = ComboPairs();
            if (combos.Count > 0 || EvolveRecipes().Count > 0) { Gold += 50; Text(P.X, P.Y - 1, "+50g", 0xffd84a); }
            // abandoned combinations never come back (spec 5.4)
            foreach (var p in combos) BannedCombos.Add(PairKey(p[0].Id, p[1].Id));
            CloseMenu();
        }
    }
}
