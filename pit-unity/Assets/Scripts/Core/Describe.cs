// Player-facing English descriptions for balls, items and level-up options.
using System.Collections.Generic;

namespace Pit.Core
{
    public struct OptionInfo
    {
        public string Title, Tag, Glyph, Desc;
        public uint Color;
        public bool IsItem;
    }

    public sealed partial class Run
    {
        static string Rng2(float a, float b)
        {
            int x = M.Round(a), y = M.Round(b);
            return x == y ? x.ToString() : x + "–" + y;
        }

        public static string DescribeBall(Stats st)
        {
            var o = new List<string>();
            if (st.Has(K.BonusMin)) o.Add("+" + Rng2(st.G(K.BonusMin), st.G(K.BonusMax)) + " damage");
            if (st.Has(K.Mult) && st.G(K.Mult) != 100) o.Add("Damage ×" + (st.G(K.Mult) / 100f).ToString("0.00"));
            if (st.Has(K.Heavy)) o.Add("Heavy: flies " + st.G(K.Heavy) + "% slower");
            if (st.Has(K.Pierce)) o.Add("Ghost: passes through foes and shields");
            if (st.Has(K.Backstab)) o.Add("Pierces foes from above for +30%");
            if (st.Has(K.BurnMin)) o.Add("Burn " + Rng2(st.G(K.BurnMin), st.G(K.BurnMax)) + "/s, " + st.G(K.BurnDur) + "s, " + st.G(K.BurnCap) + " stacks");
            if (st.Has(K.FfMin)) o.Add("Frostfire " + Rng2(st.G(K.FfMin), st.G(K.FfMax)) + "/s, " + st.G(K.FfDur) + "s, " + st.G(K.FfCap) + " stacks; +25% hit dmg");
            if (st.Has(K.PoisonMin)) o.Add("Poison " + Rng2(st.G(K.PoisonMin), st.G(K.PoisonMax)) + "/s, " + st.G(K.PoisonDur) + "s, " + st.G(K.PoisonCap) + " stacks");
            if (st.Has(K.BleedCap)) o.Add("Bleed +2 per hit (max " + st.G(K.BleedCap) + "); hits deal +stacks");
            if (st.Has(K.FreezeChance)) o.Add(st.G(K.FreezeChance) + "% to freeze for " + st.G(K.FreezeDur) + "s");
            if (st.Has(K.BlindDur)) o.Add("Blinds for " + st.G(K.BlindDur) + "s");
            if (st.Has(K.CharmChance)) o.Add(st.G(K.CharmChance).ToString("0.#") + "% to charm a foe to your side");
            if (st.Has(K.Leech)) o.Add(st.G(K.Leech).ToString("0.#") + "% to heal 1 on hit");
            if (st.Has(K.ChainMin)) o.Add("Arcs " + Rng2(st.G(K.ChainMin), st.G(K.ChainMax)) + " to " + st.G(K.ChainCount) + " nearby");
            if (st.Has(K.RowMin)) o.Add("Beam hits the row for " + Rng2(st.G(K.RowMin), st.G(K.RowMax)));
            if (st.Has(K.ColMin)) o.Add("Beam hits the column for " + Rng2(st.G(K.ColMin), st.G(K.ColMax)));
            if (st.Has(K.QuakeMin)) o.Add("Quake " + Rng2(st.G(K.QuakeMin), st.G(K.QuakeMax)) + " in a " + st.G(K.QuakeSize).ToString("0.#") + "u square");
            if (st.Has(K.BombMin)) o.Add("Bursts for " + Rng2(st.G(K.BombMin), st.G(K.BombMax)) + ", then reloads");
            if (st.Has(K.Nuke)) o.Add("Spreads its effects to every foe");
            if (st.Has(K.FlashMin)) o.Add("Flash: " + Rng2(st.G(K.FlashMin), st.G(K.FlashMax)) + " to every foe (1s cooldown)");
            if (st.Has(K.AuraR)) o.Add("Chill aura: " + Rng2(st.G(K.AuraMin), st.G(K.AuraMax)) + " and slows " + st.G(K.AuraSlow) + "%");
            if (st.Has(K.EggMin)) o.Add("Hatches " + Rng2(st.G(K.EggMin), st.G(K.EggMax)) + " small balls on hit");
            if (st.Has(K.SplitMax)) o.Add("Splits on hit, up to " + st.G(K.SplitMax) + " copies");
            if (st.Has(K.Cooldown)) o.Add(st.G(K.Cooldown) + "s reload");
            return o.Count == 0 ? "A plain ball." : string.Join(". ", o) + ".";
        }

        public static string BallName(OwnedBall b) { return b.Def.Name + (b.Fused != null ? " + " + Content.Balls[b.Fused].Name : ""); }

        public string DescribeItem(string id, int level) { return Content.Items[id].Desc(ItemValFor(id, level)); }

        public OptionInfo Info(Option o)
        {
            switch (o.Kind)
            {
                case OptKind.BallNew:
                    {
                        var d = Content.Balls[o.Id];
                        return new OptionInfo { Title = d.Name, Tag = "NEW BALL", Color = d.Color, Glyph = d.Glyph, Desc = DescribeBall(BallStats(o.Id, 1)) };
                    }
                case OptKind.BallUp:
                    {
                        var d = o.Ball.Def;
                        return new OptionInfo { Title = d.Name, Tag = "Lv " + o.Ball.Level + " → " + (o.Ball.Level + 1), Color = d.Color, Glyph = d.Glyph, Desc = DescribeBall(BallStats(o.Ball.Id, o.Ball.Level + 1)) };
                    }
                case OptKind.ItemNew:
                    {
                        var d = Content.Items[o.Id];
                        return new OptionInfo { Title = d.Name, Tag = "NEW ITEM", Color = d.Color, Glyph = d.Glyph, IsItem = true, Desc = DescribeItem(o.Id, 1) };
                    }
                default:
                    {
                        var d = o.Item.Def;
                        return new OptionInfo { Title = d.Name, Tag = "Lv " + o.Item.Level + " → " + (o.Item.Level + 1), Color = d.Color, Glyph = d.Glyph, IsItem = true, Desc = DescribeItem(o.Item.Id, o.Item.Level + 1) };
                    }
            }
        }
    }
}
