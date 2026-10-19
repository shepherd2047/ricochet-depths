// Runtime entity types. Plain data; behaviour lives in Run.*.cs and Bosses.cs.
using System.Collections.Generic;

namespace Pit.Core
{
    /// <summary>Sound cues the sim raises; the view drains Run.Sounds each frame and maps them to clips.</summary>
    public enum Sfx { Launch, Wall, Hit, Crit, Catch, Death, Hurt, LevelUp, Xp }

    public enum Mode { Title, Intro, Playing, LevelUp, Fuser, Paused, Dying, GameOver, Clearing, Victory }
    public enum BallState { Ready, Field, Cool }

    public sealed class Status
    {
        public float T, Dmg, Acc, Pct;
        public int Stacks;
    }

    public sealed class Attack
    {
        public bool Lunge;
        public float T, Ox, Oy;
    }

    public sealed class Enemy
    {
        public int Id;
        public string Type;
        public EnemyDef Def;
        public BossDef BossDef;
        public BossBrain Brain;
        public bool Boss, Pod, Dead;
        public float X, Y, W, H, Hp, MaxHp;
        public int Turn, WaveId;
        public float Entry, Flash, LastAtk = -9, Ox, Oy;
        public readonly Dictionary<string, Status> St = new Dictionary<string, Status>();
        public float Bleed;
        public Attack Atk;
        public float ShootT, AbilT, LaserT, HatchT;
        // movement styles
        public bool Flying, Under;
        public float StyleT, SpitT = -1;
        public float StepT, StepFromX, StepToX, StepProg = -1;
        public int StepDir = 1;
        public bool Falling;
        public float FallY;
        public Enemy Mount;               // the foe this one is stacked on
        public float HasteT, SpikeCd, CharmTick;
        // defence
        public bool ShieldL, ShieldR, ShieldF;
        public readonly List<FieldBall> Swallowed = new List<FieldBall>();
        // boss bookkeeping
        public Enemy Parent;
        public float Orbit;
        public int XpTotal, XpLeft, NextShed = 5;

        public float Cx { get { return X + W / 2; } }
        public float Cy { get { return Y + H / 2; } }
        public float Bottom { get { return Y + H; } }
        public string Name { get { return Boss ? BossDef.Name : Def.Name; } }
        public uint Color { get { return Boss ? BossDef.Color : Def.Color; } }
    }

    public sealed class OwnedBall
    {
        public int Uid;
        public string Id;
        public int Level = 1;
        public string Fused;
        public Stats Stats;
        public BallState State;
        public float Cd, CdMax;
        public BallDef Def { get { return Content.Balls[Id]; } }
    }

    public sealed class OwnedItem
    {
        public string Id;
        public int Level = 1;
        public ItemDef Def { get { return Content.Items[Id]; } }
    }

    public sealed class FieldBall
    {
        public OwnedBall Inst;
        public bool Baby, Temp, Clone;
        public float X, Y, Vx, Vy, V0, R;
        public uint Color, Color2;
        public bool Recall, Gone, Top, WallTouched, Swallowed;
        public int Idle, Bounces, Splits, Paddle;
        public float AuraT, BossCd, FlashCd, RecallT, SpeedMul = 1;
        public readonly HashSet<int> Hit = new HashSet<int>();
        public readonly HashSet<int> Aura = new HashSet<int>();
        public readonly List<float> Trail = new List<float>();
        public Stats St { get { return Inst != null && !Baby ? Inst.Stats : null; } }
    }

    public sealed class Shot
    {
        public float X, Y, Vx, Vy, R, Dmg;
        public int Hp;                   // -1: cannot be destroyed by balls
        public ShotKind Kind;
        public bool Dead;
    }

    public enum HazardKind { Blast, Lance, Beam, Sweep }

    public sealed class Hazard
    {
        public HazardKind Kind;
        public float X, Y, R, W, T, Max, Dmg, Dx, Dy;
        public bool Done, Small;
        public float FromX = float.NaN, FromY;
        public string Src = "blast";
    }

    public enum DropKind { Gem, Gold, Heal, Fuser }

    public sealed class Drop
    {
        public DropKind Kind;
        public float X, Y, Age;
        public int V;
        public bool Flying, Got;
    }

    public sealed class Particle { public float X, Y, Vx, Vy, T; public uint Color; }
    public sealed class FloatText { public float X, Y, T, Size; public string S; public uint Color; }

    public enum BeamKind { Arc, Row, Col, Ring, Lance, Square, Laser, Flash, Sweep }

    public sealed class Beam
    {
        public BeamKind Kind;
        public float X1, Y1, X2, Y2, X, Y, R, W, T;
        public uint Color;
    }

    public sealed class Wave
    {
        public int Id, Turn, H, Alive;
        public bool Furnace, Dropped;
    }

    public sealed class Player
    {
        public float X, Y, R = 0.38f;
        public int[] Stats;
        public int Level, Xp, Points, Hp = 1, MaxHp = 1;
        public float MoveLock, ContactCd, VolleySlow, Aim = 90, HurtT;
        public int Target = -1;
        // statuses from enemy shots
        public float RootT, SlowT, SlowPct, PoisonT, BurnT, DotAcc;
    }

    public enum OptKind { BallNew, BallUp, ItemNew, ItemUp }

    public sealed class Option
    {
        public OptKind Kind;
        public string Id;
        public OwnedBall Ball;
        public OwnedItem Item;
        public string Key { get { return Kind + ":" + (Id ?? (Ball != null ? Ball.Uid.ToString() : Item.Id)); } }
    }

    public sealed class ResultLine { public string Text; public uint Color; public bool Small; }

    public enum MenuType { LevelUp, Fuser }
    public enum FuserView { Main, Combo, Evo, Result }

    public sealed class Menu
    {
        public MenuType Type;
        public FuserView View;
        public List<Option> Options;
        public OwnedBall Host;
        public string Title;
        public List<ResultLine> Results;
    }

    public sealed class Banner { public string Title, Sub; public uint Color; public float T; }

    public sealed class Expansion
    {
        public float T, Dur = 3;
        public int FromCols, FromRows, ToCols, ToRows;
        public string BossId;
    }

    public sealed class Volley
    {
        public readonly List<OwnedBall> Q = new List<OwnedBall>();   // null entries are baby balls
        public float T;
    }

    public sealed class RunStats
    {
        public long Dmg;
        public int Clears, Fusers, BossesKilled, BestHit, GoldSpent;
        public readonly Dictionary<string, int> Hurt = new Dictionary<string, int>();
        public readonly List<string> Log = new List<string>();
    }

    public struct Recipe
    {
        public bool IsBall;
        public string Id;
        public List<OwnedBall> BallParts;
        public List<OwnedItem> ItemParts;
    }
}
