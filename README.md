# Ricochet Depths

An original ball-bouncing roguelite for TikTok mini games (Unity, portrait, English UI).

Under the hill town of Peal, an old bell foundry slid into a sinkhole. A crew of misfits goes down the shaft with launchers that throw enchanted shot, and each bounce breaks the slag creatures climbing up from below.

## Layout

| Folder | Owner | Contents |
|---|---|---|
| `ballpit-spec/` | analysis side | Clean-room specs: `spec.md` (rules and numbers), `style.md` (visual parameters) |
| `pit-unity/` | implementation | Unity project (URP, WebGL → TikTok mini game) |
| `pit-art/` | art | Art bible, concept images, low-poly character/enemy models, tools |
| `pit-scene/` | scene | Level 1 modular environment kit, lighting, assembly notes |
| `pit-ui/` | UI | Pixel UI atlas, sliced assets, `UI-SPEC.md` |
| `pit-audio/` | audio | SFX, battle loop, `DELIVERY.md`, licences |

## Clean-room rule

Everything here is designed only from `ballpit-spec/`. The analysis material from the reference game is **not** in this repository and must never be added. `.gitignore` uses a whitelist to enforce that.
