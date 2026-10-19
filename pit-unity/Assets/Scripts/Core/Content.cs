// All content tables: balls, items, recipes, enemies, bosses, heroes, level. Names and copy are original.
using System;
using System.Collections.Generic;

namespace Pit.Core
{
    public enum Stat { End, Str, Lead, Spd, Dex, Int }

    public sealed class BallDef
    {
        public string Id, Name, Glyph;
        public uint Color;
        public Stats Base;
        public Stats[] Tiers;
        public bool Evolved;
        public string[] Recipe;
    }

    public sealed class ItemDef
    {
        public string Id, Name, Glyph, Needs;
        public uint Color;
        public float[] Vals;
        public bool NoPower, Evolved;
        public string[] Recipe;
        public Func<int, string> Desc;
    }

    public enum ShotKind { Arrow, Icicle, Fireball, Venom, Bolt, Boulder }

    public sealed class EnemyDef
    {
        public string Id, Name;
        public float W = 1, H = 1, Hp = 1, Spd = 1, Melee = 10, Weight = 1;
        public int From = 1;
        public uint Color;
        public bool Stackable, Elite, Spiked, Haste;
        public float Windup = 0.5f;                     // in turns
        // shooter
        public float[] ShootEvery; public ShotKind Shot; public float ShotDmg = 5, ShotSpeed = 3.5f; public int ShotCount = 1;
        // mortar
        public float[] LobEvery; public float LobWarn = 3, LobR = 1, LobDmg = 10;
        // laser
        public float[] LaserEvery; public float LaserWarn = 3, LaserW = 0.5f, LaserDmg = 10;
        // support
        public float[] HealEvery; public float HealRange = 2.5f, HealPct = 0.25f;
        public float[] HatchEvery;
        public int PopMin, PopMax;
        // movement styles
        public float[] FlyGround, FlyAir;               // turns resting / turns airborne
        public float[] BurrowUp, BurrowDown;            // turns surfaced / turns underground
        public float[] StepEvery;                       // side-stepper
        // defence
        public bool ShieldFront, ShieldSide;
        public int Swallow;
    }

    public enum BossKind { Bellwarden, Hivekeeper, Mawking }

    public sealed class BossDef
    {
        public string Id, Name, Blurb;
        public BossKind Kind;
        public float W, H, HpMult;
        public uint Color;
        public bool Final;
    }

    public enum Twist { None, Ricochet, Scatter, PierceToWall, Paddle, Fan }

    public sealed class HeroDef
    {
        public string Id, Name, Title, StartBall, TwistText;
        public uint Color;
        public int[] Stats;          // indexed by Stat
        public Twist Twist;
    }

    public sealed class LevelDef
    {
        public string Name;
        public float TurnLen, TargetSeconds, XpMult;
        public int Cols, Rows, D, L, GoldMin, GoldMax;
        public int[] FurnaceTurns, BossTurns;
        public string[] Bosses;
    }

    public static class Content
    {
        public static readonly Dictionary<string, BallDef> Balls = new Dictionary<string, BallDef>();
        public static readonly List<string> BaseBalls = new List<string>();
        public static readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public static readonly List<string> BaseItems = new List<string>();
        public static readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public static readonly Dictionary<string, BossDef> Bosses = new Dictionary<string, BossDef>();
        public static readonly List<HeroDef> Heroes = new List<HeroDef>();
        public static readonly string[] StatNames = { "Stamina", "Might", "Lead", "Speed", "Knack", "Wits" };
        public static readonly string[] StatShort = { "STA", "MGT", "LEAD", "SPD", "KNK", "WIT" };

        public static readonly LevelDef Level = new LevelDef
        {
            Name = "The Sunken Yard", TurnLen = 4.5f, TargetSeconds = 900, XpMult = 3,   // demo tuning: the spec curve with this demo's wave density
            Cols = 7, Rows = 9, D = 1, L = 0, GoldMin = 3, GoldMax = 6,
            FurnaceTurns = new[] { 15, 30, 45, 75, 90, 105, 140, 160 },
            BossTurns = new[] { 60, 120 },
            Bosses = new[] { "bellwarden", "hivekeeper", "mawking" }
        };

        static Stats S(params object[] kv)
        {
            var s = new Stats();
            for (int i = 0; i < kv.Length; i += 2) s[(string)kv[i]] = Convert.ToSingle(kv[i + 1]);
            return s;
        }

        static void Ball(string id, string name, uint color, string glyph, Stats b, Stats t1, Stats t2, params string[] recipe)
        {
            var d = new BallDef { Id = id, Name = name, Color = color, Glyph = glyph, Base = b, Tiers = new[] { t1, t2 } };
            if (recipe.Length > 0) { d.Evolved = true; d.Recipe = recipe; } else BaseBalls.Add(id);
            Balls[id] = d;
        }
        static Stats Evo(int tier) { return S(K.BonusMin, 6, K.BonusMax, 12); }
        static Stats EvoBase(int tier, params object[] kv)
        {
            int[][] t = { new[] { 36, 68 }, new[] { 42, 80 }, new[] { 48, 92 }, new[] { 54, 104 } };
            var s = S(kv); s[K.BonusMin] = t[tier][0]; s[K.BonusMax] = t[tier][1];
            return s;
        }

        static void Item(string id, string name, uint color, string glyph, float[] vals, Func<int, string> desc, bool noPower = false, string needs = null, params string[] recipe)
        {
            var d = new ItemDef { Id = id, Name = name, Color = color, Glyph = glyph, Vals = vals, Desc = desc, NoPower = noPower, Needs = needs };
            if (recipe.Length > 0) { d.Evolved = true; d.Recipe = recipe; } else BaseItems.Add(id);
            Items[id] = d;
        }

        static void Foe(EnemyDef d) { Enemies[d.Id] = d; }

        static Content()
        {
            // ------------------------------------------------------------ base balls (Lv1 base, +tier1 at Lv2, +tier2 at Lv3)
            Ball("ember", "Ember", 0xff7b39, "flame", S(K.BurnMin, 4, K.BurnMax, 8, K.BurnDur, 3, K.BurnCap, 3), S(K.BurnMin, 1, K.BurnMax, 2), S(K.BurnMin, 1, K.BurnMax, 2, K.BurnCap, 1));
            Ball("frost", "Rime", 0x7fd8ff, "snow", S(K.FreezeChance, 8, K.FreezeDur, 3), S(K.FreezeChance, 3), S(K.FreezeChance, 3, K.FreezeDur, 1));
            Ball("spark", "Spark", 0xffe14d, "bolt", S(K.ChainMin, 10, K.ChainMax, 18, K.ChainCount, 2), S(K.ChainMin, 3, K.ChainMax, 5, K.ChainCount, 1), S(K.ChainMin, 3, K.ChainMax, 5, K.ChainCount, 1));
            Ball("venom", "Venom", 0x7ee35a, "drop", S(K.PoisonMin, 1, K.PoisonMax, 4, K.PoisonDur, 6, K.PoisonCap, 5), S(K.PoisonMin, 1, K.PoisonMax, 1), S(K.PoisonMax, 2, K.PoisonCap, 2));
            Ball("anvil", "Anvil", 0xb4bccb, "square", S(K.BonusMin, 12, K.BonusMax, 20, K.Heavy, 40), S(K.BonusMin, 8, K.BonusMax, 12), S(K.BonusMin, 8, K.BonusMax, 12));
            Ball("wisp", "Wisp", 0xd8c2ff, "ring", S(K.Pierce, 1, K.Mult, 80), S(K.Mult, 10), S(K.Mult, 10));
            Ball("thorn", "Thorn", 0xff5d7a, "spike", S(K.BleedCap, 4), S(K.BleedCap, 2), S(K.BleedCap, 2));
            Ball("ray", "Ray", 0xff9df0, "hline", S(K.RowMin, 8, K.RowMax, 14), S(K.RowMin, 4, K.RowMax, 6), S(K.RowMin, 4, K.RowMax, 6));
            Ball("pillar", "Pillar", 0x9dc2ff, "vline", S(K.ColMin, 8, K.ColMax, 14), S(K.ColMin, 4, K.ColMax, 6), S(K.ColMin, 4, K.ColMax, 6));
            Ball("tremor", "Tremor", 0xc89a62, "quake", S(K.QuakeMin, 12, K.QuakeMax, 20, K.QuakeSize, 2.25f), S(K.QuakeMin, 4, K.QuakeMax, 6), S(K.QuakeMin, 4, K.QuakeMax, 6, K.QuakeSize, 0.75f));
            Ball("brood", "Brood", 0xf0c8a0, "egg", S(K.EggMin, 2, K.EggMax, 3, K.SelfDestruct, 1), S(K.EggMax, 1), S(K.EggMin, 1));
            Ball("twin", "Twin", 0x8ff0c0, "twin", S(K.SplitMax, 2), S(K.SplitMax, 1), S(K.SplitMax, 1));
            Ball("leech", "Leech", 0xd0465a, "fang", S(K.Leech, 4.5f, K.BonusMin, 4, K.BonusMax, 8), S(K.Leech, 1.5f, K.BonusMin, 2, K.BonusMax, 4), S(K.Leech, 1.5f, K.BonusMin, 2, K.BonusMax, 4));
            Ball("glare", "Glare", 0xfff2b0, "eye", S(K.BlindDur, 3, K.CharmChance, 0.4f), S(K.BlindDur, 0.5f, K.CharmChance, 0.3f), S(K.BlindDur, 0.5f, K.CharmChance, 0.3f));

            // ------------------------------------------------------------ evolutions (two Lv3 ingredients -> new ball at Lv1)
            Ball("rimeburn", "Rimeburn", 0xc58bff, "flame", EvoBase(0, K.FfMin, 8, K.FfMax, 12, K.FfDur, 20, K.FfCap, 4), Evo(0), Evo(0), "ember", "frost");
            Ball("powderkeg", "Powder Keg", 0xff4a2a, "square", EvoBase(0, K.BombMin, 150, K.BombMax, 300, K.BombR, 1.6f, K.SelfDestruct, 1), Evo(0), Evo(0), "ember", "anvil");
            Ball("hailstorm", "Hailstorm", 0xa8f0ff, "snow", EvoBase(0, K.AuraR, 1.4f, K.AuraMin, 6, K.AuraMax, 10, K.AuraSlow, 30, K.FreezeChance, 6, K.FreezeDur, 2), Evo(0), Evo(0), "spark", "frost");
            Ball("nightpin", "Nightpin", 0x8e7dff, "spike", EvoBase(1, K.Backstab, 1), Evo(1), Evo(1), "anvil", "wisp");
            Ball("rotbriar", "Rotbriar", 0xb6e13c, "drop", EvoBase(0, K.PoisonMin, 3, K.PoisonMax, 6, K.PoisonDur, 6, K.PoisonCap, 6, K.BleedCap, 8), Evo(0), Evo(0), "venom", "thorn");
            Ball("crossfire", "Crossfire", 0xffd0f6, "cross", EvoBase(1, K.RowMin, 14, K.RowMax, 22, K.ColMin, 14, K.ColMax, 22), Evo(1), Evo(1), "ray", "pillar");
            Ball("cinderquake", "Cinderquake", 0xff9a3c, "quake", EvoBase(1, K.QuakeMin, 20, K.QuakeMax, 32, K.QuakeSize, 3.4f, K.BurnMin, 6, K.BurnMax, 10, K.BurnDur, 3, K.BurnCap, 4), Evo(1), Evo(1), "ember", "tremor");
            Ball("grapeshot", "Grapeshot", 0xd9d0c0, "egg", EvoBase(1, K.EggMin, 4, K.EggMax, 6, K.SelfDestruct, 1, K.Heavy, 20), Evo(1), Evo(1), "anvil", "brood");
            Ball("hivemother", "Hivemother", 0xb0f070, "twin", EvoBase(2, K.SplitMax, 3, K.EggMin, 1, K.EggMax, 1), Evo(2), Evo(2), "brood", "twin");
            Ball("redtithe", "Redtithe", 0xa0203a, "fang", EvoBase(1, K.Leech, 10, K.BleedCap, 8), Evo(1), Evo(1), "leech", "thorn");
            Ball("noonflash", "Noonflash", 0xfff6d0, "eye", EvoBase(1, K.FlashMin, 6, K.FlashMax, 10, K.BlindDur, 2, K.ChainMin, 12, K.ChainMax, 20, K.ChainCount, 2), Evo(1), Evo(1), "glare", "spark");
            // three-tier: an evolved ball plus a base ball
            Ball("blightbomb", "Blightbomb", 0x9cff3c, "skull", EvoBase(3, K.BombMin, 150, K.BombMax, 300, K.BombR, 1.8f, K.Nuke, 1, K.PoisonMin, 4, K.PoisonMax, 8, K.PoisonDur, 6, K.PoisonCap, 6, K.SelfDestruct, 1, K.Cooldown, 7), Evo(3), Evo(3), "powderkeg", "venom");

            // ------------------------------------------------------------ items: vals[level-1], most scale with Item Power (Wits)
            Item("pin", "Clover Pin", 0x5ee08a, "clover", new float[] { 4, 7, 10 }, v => "+" + v + "% critical hit chance.");
            Item("knuckles", "Brass Knuckles", 0xe0b04a, "fist", new float[] { 10, 20, 30 }, v => "Hero balls deal +" + v + "% damage.");
            Item("mitt", "Big Mitt", 0xffa86b, "mitt", new float[] { 40, 70, 100 }, v => "Catch zone +" + v + "% wider.");
            Item("trigger", "Hair Trigger", 0xff6b8b, "arrow", new float[] { 10, 20, 30 }, v => "Fire rate +" + v + "%.");
            Item("boots", "Quickstep Boots", 0x6bc6ff, "boot", new float[] { 12, 24, 36 }, v => "Move speed +" + v + "%.");
            Item("locket", "Iron Locket", 0xc9c9d9, "heart", new float[] { 15, 30, 45 }, v => "+" + v + " max HP.", noPower: true);
            Item("echo", "Echo Shell", 0x9ee6d6, "shell", new float[] { 25, 40, 55 }, v => "Clean catches have a " + v + "% chance to fire a bonus echo ball.");
            Item("kindling", "Kindling", 0xff9447, "flame", new float[] { 40, 80, 120 }, v => "Burn and frostfire damage +" + v + "%. Needs a burning ball.", needs: "burn");
            Item("lens", "Prism Lens", 0xb0a0ff, "gem", new float[] { 20, 40, 60 }, v => "Area damage (arcs, beams, blasts, quakes) +" + v + "%.");
            Item("lodestone", "Lodestone", 0xff7070, "magnet", new float[] { 60, 120, 180 }, v => "Pickup radius +" + v + "%.");
            Item("veil", "Ghost Veil", 0xc8d0ff, "veil", new float[] { 0, 10, 20 }, v => "Balls pass through foes until they first touch the top wall" + (v > 0 ? ", dealing +" + v + "% while passing." : "."));
            // evolved items
            Item("signet", "Headsman's Signet", 0xffd84a, "crown", new float[] { 15, 20, 25 }, v => "+" + v + "% crit chance, crits deal triple damage, hero balls +30% damage.", recipe: new[] { "pin", "knuckles" });
            Item("halo", "Rebound Halo", 0xa9fff0, "ring", new float[] { 120, 150, 180 }, v => "Catch zone +" + v + "% wider. Every clean catch fires an echo ball.", recipe: new[] { "mitt", "echo" });
            Item("mantle", "Wraith Mantle", 0xe0d0ff, "veil", new float[] { 20, 30, 40 }, v => "Balls pass through foes until they touch the top wall, dealing +30%. Move speed +" + v + "%.", recipe: new[] { "veil", "boots" });

            // ------------------------------------------------------------ enemies (From: first turn it may appear)
            Foe(new EnemyDef { Id = "grunt", Name = "Shambler", Color = 0x6f86ff, From = 1, Weight = 10, Stackable = true });
            Foe(new EnemyDef { Id = "mite", Name = "Mite", W = 0.5f, H = 0.5f, Hp = 0.5f, Spd = 1.25f, Melee = 5, Color = 0x9fb3ff, From = 1, Weight = 3 });
            Foe(new EnemyDef { Id = "brick", Name = "Brute", Hp = 2, Color = 0x4f5fbf, From = 5, Weight = 4, Stackable = true });
            Foe(new EnemyDef { Id = "archer", Name = "Spitter", Color = 0x3fc9a5, From = 8, Weight = 2, ShootEvery = new[] { 0.5f, 1f }, Shot = ShotKind.Arrow, ShotDmg = 5, ShotSpeed = 3.5f });
            Foe(new EnemyDef { Id = "slab", Name = "Slab", W = 2, Hp = 3, Color = 0x8a6bff, From = 12, Weight = 2 });
            Foe(new EnemyDef { Id = "popper", Name = "Brood Pod", Color = 0xd96bd0, From = 16, Weight = 2, PopMin = 3, PopMax = 5 });
            Foe(new EnemyDef { Id = "flyer", Name = "Flitter", Color = 0x7ad0ff, From = 18, Weight = 1.6f, FlyGround = new[] { 1f, 2f }, FlyAir = new[] { 0.5f, 1f } });
            Foe(new EnemyDef { Id = "pavise", Name = "Pavise", Hp = 2, Color = 0x8f9bb8, From = 20, Weight = 1.5f, ShieldFront = true });
            Foe(new EnemyDef { Id = "mender", Name = "Mender", Color = 0x5be07a, From = 22, Weight = 1.5f, HealEvery = new[] { 1f, 2f } });
            Foe(new EnemyDef { Id = "skitter", Name = "Skitter", Hp = 2, Color = 0xc0e060, From = 24, Weight = 1.5f, StepEvery = new[] { 1f, 2f } });
            Foe(new EnemyDef { Id = "mortar", Name = "Lobber", Color = 0xe0a03f, From = 28, Weight = 1.5f, LobEvery = new[] { 1.5f, 2.5f } });
            Foe(new EnemyDef { Id = "nester", Name = "Nester", Color = 0xe08fb8, From = 32, Weight = 1, HatchEvery = new[] { 2f, 3f } });
            Foe(new EnemyDef { Id = "chiller", Name = "Chiller", Color = 0x9feaff, From = 35, Weight = 1.2f, ShootEvery = new[] { 0.6f, 1.1f }, Shot = ShotKind.Icicle, ShotDmg = 5, ShotSpeed = 3.2f });
            Foe(new EnemyDef { Id = "delver", Name = "Delver", Color = 0xa08060, From = 38, Weight = 1.5f, BurrowUp = new[] { 1f, 2f }, BurrowDown = new[] { 0.6f, 1f }, Shot = ShotKind.Venom, ShotDmg = 5, ShotSpeed = 3f });
            Foe(new EnemyDef { Id = "lancer", Name = "Lancer", Color = 0xff5d8f, From = 42, Weight = 1.2f, LaserEvery = new[] { 2f, 3f } });
            Foe(new EnemyDef { Id = "burr", Name = "Burr", Color = 0xd0a040, From = 45, Weight = 1.5f, Spiked = true });
            Foe(new EnemyDef { Id = "dasher", Name = "Dasher", Spd = 2, Color = 0xffc04d, From = 50, Weight = 1.2f, Haste = true });
            Foe(new EnemyDef { Id = "gulper", Name = "Gulper", W = 2, Hp = 2, Color = 0x9a5ad0, From = 55, Weight = 1, Swallow = 1 });
            Foe(new EnemyDef { Id = "flanker", Name = "Flanker", Hp = 2, Color = 0x7c8fa0, From = 65, Weight = 1.2f, ShieldSide = true });
            Foe(new EnemyDef { Id = "hurler", Name = "Hurler", W = 2, Hp = 1.5f, Spd = 0.7f, Color = 0xff7043, From = 70, Weight = 1, ShootEvery = new[] { 1.2f, 1.8f }, Shot = ShotKind.Fireball, ShotDmg = 5, ShotSpeed = 2.6f });
            // elites: big, slow, double damage, longer wind-up, survive their attack
            Foe(new EnemyDef { Id = "hulk", Name = "Hulk", W = 2, H = 2, Hp = 16, Spd = 0.7f, Melee = 20, Color = 0x5a4fd0, From = 80, Weight = 0.35f, Elite = true, Windup = 1 });
            Foe(new EnemyDef { Id = "volleyer", Name = "Volleyer", W = 2, H = 2, Hp = 15, Spd = 0.7f, Melee = 20, Color = 0x2fa985, From = 100, Weight = 0.3f, Elite = true, Windup = 1, ShootEvery = new[] { 0.8f, 1.2f }, Shot = ShotKind.Arrow, ShotDmg = 10, ShotSpeed = 3.5f, ShotCount = 3 });

            // ------------------------------------------------------------ bosses
            Bosses["bellwarden"] = new BossDef { Id = "bellwarden", Kind = BossKind.Bellwarden, Name = "The Bellwarden", W = 5, H = 4, HpMult = 18, Color = 0x5a4a7a,
                Blurb = "Its brass crown is the weak spot. Bounce shots over the top." };
            Bosses["hivekeeper"] = new BossDef { Id = "hivekeeper", Kind = BossKind.Hivekeeper, Name = "The Hivekeeper", W = 3, H = 3, HpMult = 16, Color = 0x6a8a3a,
                Blurb = "Its core is sealed while the orbiting pods live. Pop them first." };
            Bosses["mawking"] = new BossDef { Id = "mawking", Kind = BossKind.Mawking, Name = "The Mawking", W = 7, H = 7, HpMult = 30, Color = 0x3a2a4a, Final = true,
                Blurb = "Its stone plates turn to face you. Strike where it is bare." };

            // ------------------------------------------------------------ heroes: six stats, a starting ball and a rule twist
            Heroes.Add(new HeroDef { Id = "tinker", Name = "Tinker", Title = "the Steady", Color = 0xffd36b, StartBall = "ember",
                Stats = new[] { 5, 3, 2, 4, 4, 3 }, Twist = Twist.None, TwistText = "No tricks. Balanced stats and a reliable burn." });
            Heroes.Add(new HeroDef { Id = "ricco", Name = "Ricco", Title = "the Caromist", Color = 0xff9df0, StartBall = "spark",
                Stats = new[] { 4, 4, 1, 5, 5, 2 }, Twist = Twist.Ricochet, TwistText = "+5% damage per bounce (max +100%). Balls return as soon as they touch the top wall." });
            Heroes.Add(new HeroDef { Id = "scatter", Name = "Scatterjack", Title = "the Hasty", Color = 0x7ee35a, StartBall = "venom",
                Stats = new[] { 4, 2, 3, 3, 6, 3 }, Twist = Twist.Scatter, TwistText = "Fires twice as fast and never stops firing, but every launch and bounce veers a little." });
            Heroes.Add(new HeroDef { Id = "needle", Name = "Needle", Title = "the Lancer", Color = 0xff5d7a, StartBall = "thorn",
                Stats = new[] { 4, 5, 1, 4, 4, 2 }, Twist = Twist.PierceToWall, TwistText = "Balls pass through every foe until they first touch a wall." });
            Heroes.Add(new HeroDef { Id = "bulwark", Name = "Bulwark", Title = "the Wall", Color = 0x7fd8ff, StartBall = "frost",
                Stats = new[] { 7, 3, 1, 3, 3, 3 }, Twist = Twist.Paddle, TwistText = "Catching swats the ball back up instead: each swat adds +100% damage (max +300%)." });
            Heroes.Add(new HeroDef { Id = "fanfare", Name = "Fanfare", Title = "the Loud", Color = 0xffa86b, StartBall = "ray",
                Stats = new[] { 5, 3, 3, 3, 3, 4 }, Twist = Twist.Fan, TwistText = "Each volley fires every ball at once in a wide fan." });
        }

        public static HeroDef Hero(string id) { return Heroes.Find(h => h.Id == id) ?? Heroes[0]; }
    }
}
