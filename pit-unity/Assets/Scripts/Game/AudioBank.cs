// Plays the pit-audio batch 1 sounds (pit-audio/DELIVERY.md). The sim raises cues in Run.Sounds; this maps each cue to
// a clip key and applies the delivery's runtime rules: random variation, pitch jitter, per-key voice caps, the wall
// retrigger gate, crit instead of hit, the death rate limit during the win sweep, and the rising XP chime. Music is
// the battle loop on its own source about 6 dB under the SFX. Nothing plays before the first tap (WebGL/TikTok
// autoplay rule). Clips load lazily from Resources/Audio; a missing clip is skipped silently.
using System.Collections.Generic;
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public sealed class AudioBank
    {
        const int Voices = 16;
        const float MusicVolume = 0.5f;     // about -6 dB under SFX at 1.0

        struct Rule { public string Key; public int Max; public float Jitter; public Rule(string k, int m, float j) { Key = k; Max = m; Jitter = j; } }

        // indexed by Sfx
        static readonly Rule[] Rules =
        {
            new Rule("ball_launch", 6, 0.03f),
            new Rule("ball_wall", 4, 0.03f),
            new Rule("ball_hit", 6, 0.03f),
            new Rule("ball_crit", 6, 0.03f),
            new Rule("ball_catch", 3, 0.03f),
            new Rule("en_death", 3, 0.03f),
            new Rule("pl_hurt", 2, 0.03f),
            new Rule("pl_levelup", 1, 0.015f),
            new Rule("pk_xp", 4, 0f),
        };

        readonly AudioSource[] voices = new AudioSource[Voices];
        readonly Sfx[] voiceKey = new Sfx[Voices];
        readonly float[] voiceEnd = new float[Voices];
        readonly AudioSource music;
        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly float[] lastPlay = new float[Rules.Length];
        AudioClip musicClip, click;
        bool unlocked, musicLoaded, musicOn;
        int xpStep; float xpLast = -10;
        readonly System.Random rng = new System.Random(7);

        public AudioBank(GameObject host)
        {
            if (host.GetComponent<AudioListener>() == null) host.AddComponent<AudioListener>();
            for (int i = 0; i < Voices; i++)
            {
                voices[i] = host.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voiceEnd[i] = -1;
            }
            music = host.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.volume = MusicVolume;
            for (int i = 0; i < lastPlay.Length; i++) lastPlay[i] = -10;
        }

        /// <summary>Call on the first tap; until then every request is dropped.</summary>
        public void Unlock() { unlocked = true; }
        public bool Unlocked { get { return unlocked; } }

        /// <summary>Plays the run's queued cues and clears the queue. sweep is true during the win "pop all" sweep.</summary>
        public void Drain(Run r, float now)
        {
            if (r == null) return;
            if (unlocked)
                foreach (var s in r.Sounds) Play(s, now, r.Mode == Mode.Clearing);
            r.Sounds.Clear();
        }

        public void Play(Sfx s, float now, bool sweep = false)
        {
            if (!unlocked) return;
            int k = (int)s;
            var rule = Rules[k];
            if (s == Sfx.Wall && now - lastPlay[k] < 0.03f) return;
            if (s == Sfx.Death && sweep && now - lastPlay[k] < 0.08f) return;
            int busy = 0;
            for (int i = 0; i < Voices; i++) if (voiceKey[i] == s && voiceEnd[i] > now) busy++;
            if (busy >= rule.Max) return;
            var set = Clips(rule.Key);
            if (set.Length == 0) return;

            float pitch = 1 + ((float)rng.NextDouble() * 2 - 1) * rule.Jitter;
            if (s == Sfx.Xp)
            {
                xpStep = now - xpLast < 0.4f ? Mathf.Min(12, xpStep + 1) : 0;
                xpLast = now;
                pitch = Mathf.Pow(2, xpStep / 12f);
            }
            var clip = set[rng.Next(set.Length)];
            int v = FreeVoice(now);
            voices[v].pitch = pitch;
            voices[v].clip = clip;
            voices[v].volume = 1;
            voices[v].Play();
            voiceKey[v] = s;
            voiceEnd[v] = now + Len(clip) / Mathf.Max(0.1f, pitch);
            lastPlay[k] = now;
        }

        public void Click(float now)
        {
            if (!unlocked) return;
            if (click == null) { var c = Clips("ui_click"); if (c.Length == 0) return; click = c[0]; }
            int v = FreeVoice(now);
            voices[v].pitch = 1; voices[v].volume = 1; voices[v].clip = click; voices[v].Play();
            voiceKey[v] = (Sfx)(-1); voiceEnd[v] = now + Len(click);
        }

        /// <summary>Battle loop on while in a run, paused while the run is paused, off elsewhere.</summary>
        public void Music(bool inBattle, bool paused)
        {
            if (!unlocked) return;
            if (inBattle && !musicLoaded) { musicClip = Resources.Load<AudioClip>("Audio/music/mus_battle_loop"); musicLoaded = true; music.clip = musicClip; }
            if (musicClip == null) return;
            if (!inBattle) { if (musicOn) { music.Stop(); musicOn = false; } return; }
            if (paused) { if (music.isPlaying) music.Pause(); return; }
            if (!musicOn) { music.Play(); musicOn = true; }
            else if (!music.isPlaying) music.UnPause();
        }

        // WebGL reports length 0 until a clip has finished decoding; assume a short SFX so the voice caps still hold
        static float Len(AudioClip c) { return c.length > 0 ? c.length : 0.2f; }

        int FreeVoice(float now)
        {
            int best = 0; float bestEnd = float.MaxValue;
            for (int i = 0; i < Voices; i++)
            {
                if (voiceEnd[i] <= now) return i;
                if (voiceEnd[i] < bestEnd) { bestEnd = voiceEnd[i]; best = i; }
            }
            return best;
        }

        AudioClip[] Clips(string key)
        {
            AudioClip[] set;
            if (clips.TryGetValue(key, out set)) return set;
            var list = new List<AudioClip>();
            for (int i = 1; i < 10; i++)
            {
                var c = Resources.Load<AudioClip>("Audio/sfx/" + key + "_" + i);
                if (c == null) break;
                list.Add(c);
            }
            clips[key] = set = list.ToArray();
            return set;
        }
    }
}
