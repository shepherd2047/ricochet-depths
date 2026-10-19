# Audio delivery: batch 1 (approved 2026-10-19)

The user auditioned batch 1 and approved it. Ready for the game side to wire in.

## What to ship

| Path | Contents | Size |
|---|---|---|
| `sfx/*.mp3` | 10 events, 24 variation files, mono 32 kHz | 74 KB |
| `music/mus_battle_loop.mp3` | electro battle loop, 30 s, stereo 44.1 kHz, seamless | 814 KB |
| `manifest.json` | key → files, durations, sizes, loop flag | — |
| **Total** | | **0.85 MiB** |

Don't ship `masters/` (WAV import sources), `_drafts/` (unapproved), `tools/` or `preview.html`.

## Event → key map

| Game event | Key | Variations | Runtime rule |
|---|---|---|---|
| Ball launched (each ball in a salvo) | `ball_launch` | 3 | random variation, ±3 % pitch |
| Ball hits side/top wall | `ball_wall` | 3 | max 4 voices, drop retriggers < 30 ms |
| Ball hits enemy (no crit) | `ball_hit` | 4 | max 6 voices |
| Ball crit | `ball_crit` | 2 | plays *instead of* `ball_hit` |
| Good catch | `ball_catch` | 2 | |
| Enemy dies | `en_death` | 3 | max 3 voices; skip during the win "pop all" sweep or limit it to 1 every 80 ms |
| Player takes damage | `pl_hurt` | 2 | |
| Level-up | `pl_levelup` | 1 | ±1.5 % pitch only (tuned) |
| XP gem collected | `pk_xp` | 3 | pitch +1 semitone per gem within 0.4 s, reset after; cap +12 |
| Any UI button | `ui_click` | 1 | |
| In battle | `mus_battle_loop` | 1 | loop; music bus ≈ −6 dB under SFX |

Events without a key (elements, enemy attacks, boss, Fuser, gold, stings and so on) stay silent for now. See the ⬜/📝 rows in `audio-list.md`.

## Unity drop-in

1. Copy `sfx/` and `music/` into the project. With the Resources-based setup in `pit-unity/Assets/Resources/`, use `Assets/Resources/Audio/sfx/` and `Assets/Resources/Audio/music/`, then load with `Resources.Load<AudioClip>("Audio/sfx/ball_hit_2")`. Use the WAVs from `masters/` instead for best quality, since Unity re-encodes to AAC on WebGL either way.
2. Import settings: see "Unity WebGL import settings" in `audio-list.md`. SFX: Force To Mono, Decompress On Load, 32 kHz override. Music: stereo, Compressed In Memory, Load In Background.
3. Start audio only after the first tap (WebGL/TikTok autoplay rule).
4. Size impact is 0.85 MiB, well inside the 60 MB TikTok package limit.

A minimal bank, for reference only (adapt to the project's style):

```csharp
static readonly Dictionary<string, AudioClip[]> bank = new();
public static void Play(AudioSource src, string key, float pitchJitter = 0.03f) {
    if (!bank.TryGetValue(key, out var clips)) {
        var list = new List<AudioClip>();
        for (int i = 1; ; i++) { var c = Resources.Load<AudioClip>($"Audio/sfx/{key}_{i}"); if (!c) break; list.Add(c); }
        bank[key] = clips = list.ToArray();
    }
    if (clips.Length == 0) return;
    src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
    src.PlayOneShot(clips[Random.Range(0, clips.Length)]);
}
```

## Licence

All files are original procedural synthesis, owned by the project owner. No third-party material. See `LICENSES.md`.
