// The four demo elements and how the game's ball ids map to them (pit-unity Core/Content.cs: ember, frost, spark, ray).
using UnityEngine;

namespace RicochetVfx
{
    public enum VfxElement { Ember = 0, Rime = 1, Spark = 2, Ray = 3 }

    public static class VfxElements
    {
        public const int Count = 4;

        /// <summary>Ball id from Content.cs to element. "frost" is the id of the ball named Rime.</summary>
        public static bool TryParse(string ballId, out VfxElement e)
        {
            switch (ballId)
            {
                case "ember": e = VfxElement.Ember; return true;
                case "frost": case "rime": e = VfxElement.Rime; return true;
                case "spark": e = VfxElement.Spark; return true;
                case "ray": e = VfxElement.Ray; return true;
                default: e = VfxElement.Ember; return false;
            }
        }

        public static string BallId(VfxElement e)
        {
            switch (e)
            {
                case VfxElement.Rime: return "frost";
                case VfxElement.Spark: return "spark";
                case VfxElement.Ray: return "ray";
                default: return "ember";
            }
        }

        /// <summary>The ball colour from Content.cs (also the mid stop of the element's ramp).</summary>
        public static Color BaseColor(VfxElement e)
        {
            switch (e)
            {
                case VfxElement.Rime: return Hex(0x7fd8ff);
                case VfxElement.Spark: return Hex(0xffe14d);
                case VfxElement.Ray: return Hex(0xff9df0);
                default: return Hex(0xff7b39);
            }
        }

        public static Color Hex(uint rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
        }
    }
}
