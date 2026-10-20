// Ad placements (ballpit-spec/ads.md). The game only talks to IAdService; AdConfig holds every cap and interval.
// PlaceholderAds is the current implementation: a full-screen "AD" panel with a 2 s countdown (Screens.AdPanel draws
// it). A TikTok implementation replaces it later (TT.CreateRewardedVideoAd / TT.CreateInterstitialAd: a new instance per
// show, Load() then Show(), Destroy() afterwards; only isEnded == true counts as watched).
using System;
using UnityEngine;

namespace Pit.Game
{
    public interface IAdService
    {
        /// <summary>False when no rewarded ad can be shown now: the button greys out with "Ad not available".</summary>
        bool RewardedReady(string placement);
        /// <summary>Shows a rewarded ad; done(true) only when it was watched to the end.</summary>
        void ShowRewarded(string placement, Action<bool> done);
        /// <summary>Shows an interstitial; done() always runs, also when it failed to load (then at once).</summary>
        void ShowInterstitial(string placement, Action done);
        /// <summary>True while an ad covers the screen: the run's clock and the audio are paused.</summary>
        bool Showing { get; }
    }

    /// <summary>Caps and intervals (ads.md §1–§3).</summary>
    public sealed class AdConfig
    {
        public static readonly AdConfig Default = new AdConfig();

        public int RevivesPerRun = 1;               // R1
        public float ReviveHp = 0.5f;               // of max HP
        public int ReviveRows = 2;                  // nearest occupied rows cleared, no rewards
        public int RerollsPerRun = 3;               // R2
        public int ExtraCardsPerCrucible = 1;       // R3
        public float InterstitialAfterLaunch = 180; // s since launch before the first one
        public float InterstitialGap = 180;         // s between two
        public int InterstitialFirstRun = 2;        // never after the first run's results
        public float PlaceholderSeconds = 2;

        public const string Revive = "revive", Reroll = "levelup_reroll", Crucible = "crucible_option", Results = "results";
    }

    /// <summary>
    /// The 2-second placeholder. Rewarded: Skip during the countdown reports not watched, the × after it reports watched.
    /// Interstitial: closes itself after the countdown. FailLoads (Editor and development builds, F8) makes every load
    /// fail, to test the "not available" paths.
    /// </summary>
    public sealed class PlaceholderAds : IAdService
    {
        public bool FailLoads;
        public float Now;              // real time, set by the host each frame
        public readonly AdConfig Cfg;

        public string Placement { get; private set; }
        public bool Rewarded { get; private set; }
        float start;
        Action<bool> doneRewarded;
        Action doneInterstitial;

        public PlaceholderAds(AdConfig cfg) { Cfg = cfg ?? AdConfig.Default; }

        public bool Showing { get { return Placement != null; } }
        public float Left { get { return Mathf.Max(0, Cfg.PlaceholderSeconds - (Now - start)); } }
        public bool CanClose { get { return Showing && Left <= 0; } }
        public static bool DebugToggleAllowed { get { return Application.isEditor || Debug.isDebugBuild; } }

        public bool RewardedReady(string placement) { return !FailLoads && !Showing; }

        public void ShowRewarded(string placement, Action<bool> done)
        {
            if (!RewardedReady(placement)) { done(false); return; }
            Placement = placement; Rewarded = true; start = Now; doneRewarded = done;
        }

        public void ShowInterstitial(string placement, Action done)
        {
            if (FailLoads || Showing) { done(); return; }
            Placement = placement; Rewarded = false; start = Now; doneInterstitial = done;
        }

        /// <summary>Per frame: the interstitial closes itself when the countdown ends.</summary>
        public void Tick() { if (Showing && !Rewarded && Left <= 0) Finish(true); }

        /// <summary>The rewarded panel's buttons: Skip (during the countdown) or × (after it).</summary>
        public void Skip() { if (Showing && Rewarded) Finish(false); }
        public void Close() { if (CanClose) Finish(true); }

        void Finish(bool watched)
        {
            var r = doneRewarded; var i = doneInterstitial;
            Placement = null; doneRewarded = null; doneInterstitial = null;
            if (r != null) r(watched);
            if (i != null) i();
        }
    }
}
