// The only place that knows which platform the game runs on. Everything platform-specific (safe area, the TikTok
// mini-game APIs) goes through IPlatform so the game code never calls a host API directly.
using System;
using UnityEngine;

namespace Pit.Game
{
    public interface IPlatform
    {
        string Name { get; }
        /// <summary>Safe-area insets in screen pixels (top, bottom).</summary>
        Vector2 SafeInsets { get; }
        void OnBoot();

        // ---- hooks for the six TikTok mini-game APIs. Not used by the demo (no login, ads or missions yet);
        // they exist so later work has one place to wire them. Each completes through its callback.
        void Login(Action<bool, string> done);                       // login
        void ShowRewardedVideo(string adUnitId, Action<bool> done);  // createRewardedVideoAd
        void AddShortcut(Action<bool> done);                         // addShortcut
        void ClaimShortcutReward(Action<bool> done);                 // getShortcutMissionReward
        void StartEntranceMission(Action<bool> done);                // startEntranceMission
        void ClaimEntranceReward(Action<bool> done);                 // getEntranceMissionReward
    }

    /// <summary>Editor, desktop and plain WebGL: no host APIs, every hook reports "not available".</summary>
    public sealed class LocalPlatform : IPlatform
    {
        public string Name { get { return "local"; } }
        public Vector2 SafeInsets
        {
            get
            {
                var sa = Screen.safeArea;
                return new Vector2(Mathf.Max(0, Screen.height - sa.yMax), Mathf.Max(0, sa.yMin));
            }
        }
        public void OnBoot() { }
        public void Login(Action<bool, string> done) { done(false, null); }
        public void ShowRewardedVideo(string adUnitId, Action<bool> done) { done(false); }
        public void AddShortcut(Action<bool> done) { done(false); }
        public void ClaimShortcutReward(Action<bool> done) { done(false); }
        public void StartEntranceMission(Action<bool> done) { done(false); }
        public void ClaimEntranceReward(Action<bool> done) { done(false); }
    }

    /// <summary>
    /// TikTok mini-game host. Unwired on purpose: the demo ships without login, ads or missions. When they are added,
    /// implement each hook with the TikTok Unity SDK that the TikTok mini-game conversion tool provides (see
    /// TikTok/README.md); until then it behaves like LocalPlatform.
    /// </summary>
    public sealed class TikTokPlatform : IPlatform
    {
        readonly LocalPlatform local = new LocalPlatform();
        public string Name { get { return "tiktok"; } }
        public Vector2 SafeInsets { get { return local.SafeInsets; } }
        public void OnBoot() { }
        public void Login(Action<bool, string> done) { done(false, null); }
        public void ShowRewardedVideo(string adUnitId, Action<bool> done) { done(false); }
        public void AddShortcut(Action<bool> done) { done(false); }
        public void ClaimShortcutReward(Action<bool> done) { done(false); }
        public void StartEntranceMission(Action<bool> done) { done(false); }
        public void ClaimEntranceReward(Action<bool> done) { done(false); }
    }

    public static class PlatformHost
    {
        static IPlatform current;
        public static IPlatform Current
        {
            get
            {
                if (current == null)
                {
#if PIT_TIKTOK
                    current = new TikTokPlatform();
#else
                    current = new LocalPlatform();
#endif
                }
                return current;
            }
        }
    }
}
