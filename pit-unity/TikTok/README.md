# Shipping to TikTok

Status: **plan only, nothing here is verified.** No Unity Editor has run this project yet, and no TikTok build has been made.

## What TikTok's docs say

Checked 2026-10-19 on these pages:

- [Build mini games with Unity](https://developers.tiktok.com/docs/en/build-mini-games-with-unity)
- [Unity packaging optimization](https://developers.tiktok.com/docs/en/unity-packaging-optimization)
- [Mini games technical overview](https://developers.tiktok.com/docs/en/mini-games-technical-overview)

The rules they state:

- The path is the **TikTok SDK for Unity**, a `.unitypackage`. You get it from your **TikTok operations representative**; it isn't a public download. At the time of checking, the recommended version was 1.1.6-Release.
- Supported editors are Unity 2021 or 2022. The project targets **2022.3 LTS**. Users need TikTok client 4310 or newer on Android or iOS.
- The build writes a `tt-minigame/` folder. You upload that whole folder, either with `ttmg dev` run from inside it or with the web uploader.
- **The uploaded Unity package must be 60 MB or smaller.** The 4 MB main / 30 MB total limits apply to JS mini games, not to Unity ones.
- TikTok rejects a plain Unity WebGL build that hasn't been adapted. It has to go through their SDK.
- The packaging guide recommends:
  - ASTC textures;
  - WebGL 2 only, with Auto Graphics API off;
  - Managed Strip Level High, plus `link.xml` where code must be kept;
  - Strip Engine Code;
  - disabling unused modules;
  - external debug symbols;
  - "Explicitly Thrown Exceptions Only";
  - loading large audio and video from a CDN, or through Addressables or AutoStreaming.

## How this project fits

- **Size.** There are no textures, audio or imported assets. Everything is drawn from code with vertex-coloured meshes and the built-in font, so the build is mostly the IL2CPP wasm and the framework. That should land far below 60 MB. The real number only comes from the first build.
- **Remote loading.** Per the user, a few seconds of loading is fine, so the main package doesn't need to be squeezed. TikTok's docs don't describe loading the main `.wasm`/`.data` from your own CDN. Whether the SDK supports that, and how its loading screen and progress bar work, are **open questions for the TikTok rep**. With nothing big to stream, the safe default is to ship one package under 60 MB and use whatever loading screen the SDK provides. If content is added later (audio, art), it should go to a CDN through Addressables, as the guide suggests. The CDN must be an allow-listed HTTPS domain, and no remote JS may be loaded.
- **Platform APIs.** All host calls go through `Assets/Scripts/Game/Platform.cs` (`IPlatform`). The `PIT_TIKTOK` define picks `TikTokPlatform`, whose hooks for the six required APIs are still stubs that report "not available":
  - `Login` → login
  - `ShowRewardedVideo` → createRewardedVideoAd
  - `AddShortcut` → addShortcut
  - `ClaimShortcutReward` → getShortcutMissionReward
  - `StartEntranceMission` → startEntranceMission
  - `ClaimEntranceReward` → getEntranceMissionReward

  They get wired to the TikTok Unity SDK once that is imported. Per the current order, there are still no ads or login.

## Steps once an Editor and the SDK are available

1. Open `pit-unity/` in Unity 2022.3 LTS.
2. Run **Pit → Create Boot Scene**, then **Pit → Apply WebGL Size Settings**.
3. Switch the platform to WebGL. Set texture compression to ASTC; the project has no textures, but follow the guide anyway.
4. Import the TikTok Unity SDK `.unitypackage`. Follow its build menu, and use **Pit → Build WebGL (TikTok define)** if the SDK builds on top of a normal WebGL build.
5. Check the size of `tt-minigame/`, then run `ttmg dev` inside it and test on a device.
6. Before review, wire the six APIs in `TikTokPlatform`. That only happens once the user asks for it.
