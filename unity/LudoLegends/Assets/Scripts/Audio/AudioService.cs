using System;
using System.Collections.Generic;
using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// Sound and haptics without shipping audio assets.
    ///
    /// Every sound is synthesised at startup (short, cheap waveforms) which keeps the APK
    /// small and avoids licence questions entirely. Volumes are user settings, persisted
    /// with PlayerPrefs.
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        public static AudioService Instance { get; private set; }

        private const string SfxKey = "ludo.sfx";
        private const string MusicKey = "ludo.music";
        private const string HapticsKey = "ludo.haptics";

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _sfx;
        private AudioSource _music;

        public bool SfxEnabled { get; private set; } = true;
        public bool MusicEnabled { get; private set; } = true;
        public bool HapticsEnabled { get; private set; } = true;

        public static AudioService Ensure(GameObject host)
        {
            if (Instance != null)
            {
                return Instance;
            }

            Instance = host.AddComponent<AudioService>();
            return Instance;
        }

        private void Awake()
        {
            Instance = this;
            SfxEnabled = PlayerPrefs.GetInt(SfxKey, 1) == 1;
            MusicEnabled = PlayerPrefs.GetInt(MusicKey, 1) == 1;
            HapticsEnabled = PlayerPrefs.GetInt(HapticsKey, 1) == 1;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.volume = 0.7f;

            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.volume = 0.18f;

            _clips["click"] = Tone("click", 660f, 0.05f, WaveType.Square, 0.35f);
            _clips["dice"] = Tone("dice", 320f, 0.12f, WaveType.Noise, 0.5f);
            _clips["step"] = Tone("step", 520f, 0.06f, WaveType.Sine, 0.5f);
            _clips["capture"] = Sweep("capture", 300f, 120f, 0.28f);
            _clips["home"] = Arpeggio("home", new[] { 523f, 659f, 784f, 1046f }, 0.12f);
            _clips["win"] = Arpeggio("win", new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.16f);
            _clips["error"] = Sweep("error", 240f, 160f, 0.18f);

            if (MusicEnabled)
            {
                _music.clip = Loop("music", new[] { 196f, 246f, 293f, 246f }, 2.0f);
                _music.Play();
            }
        }

        public void PlayClick()
        {
            Play("click");
        }

        public void PlayDice()
        {
            Play("dice");
        }

        public void PlayStep()
        {
            Play("step");
        }

        public void PlayCapture()
        {
            Play("capture");
            Tap(35);
        }

        public void PlayHome()
        {
            Play("home");
        }

        public void PlayWin()
        {
            Play("win");
            Tap(60);
        }

        public void PlayError()
        {
            Play("error");
        }

        public void Tap(int milliseconds = 18)
        {
            if (!HapticsEnabled)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator != null)
                    {
                        vibrator.Call("vibrate", (long)milliseconds);
                    }
                }
            }
            catch (Exception)
            {
                // Haptics are a nicety; never let them break gameplay.
            }
#endif
        }

        public void SetSfx(bool enabled)
        {
            SfxEnabled = enabled;
            PlayerPrefs.SetInt(SfxKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetMusic(bool enabled)
        {
            MusicEnabled = enabled;
            PlayerPrefs.SetInt(MusicKey, enabled ? 1 : 0);
            PlayerPrefs.Save();

            if (_music == null)
            {
                return;
            }

            if (enabled && !_music.isPlaying)
            {
                _music.clip = _music.clip ?? Loop("music", new[] { 196f, 246f, 293f, 246f }, 2.0f);
                _music.Play();
            }
            else if (!enabled && _music.isPlaying)
            {
                _music.Pause();
            }
        }

        public void SetHaptics(bool enabled)
        {
            HapticsEnabled = enabled;
            PlayerPrefs.SetInt(HapticsKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void Play(string key)
        {
            if (!SfxEnabled || _sfx == null)
            {
                return;
            }

            if (_clips.TryGetValue(key, out AudioClip clip))
            {
                _sfx.PlayOneShot(clip);
            }
        }

        private enum WaveType
        {
            Sine,
            Square,
            Noise
        }

        private static AudioClip Tone(string name, float frequency, float seconds, WaveType wave, float gain)
        {
            const int sampleRate = 22050;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var data = new float[samples];
            var random = new System.Random(7);

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-6f * t / seconds);
                float value;
                switch (wave)
                {
                    case WaveType.Square:
                        value = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t));
                        break;
                    case WaveType.Noise:
                        value = (float)(random.NextDouble() * 2.0 - 1.0) * 0.6f
                                + Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.4f;
                        break;
                    default:
                        value = Mathf.Sin(2f * Mathf.PI * frequency * t);
                        break;
                }

                data[i] = value * envelope * gain;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Sweep(string name, float from, float to, float seconds)
        {
            const int sampleRate = 22050;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var data = new float[samples];
            float phase = 0f;

            for (int i = 0; i < samples; i++)
            {
                float progress = i / (float)samples;
                float frequency = Mathf.Lerp(from, to, progress);
                phase += 2f * Mathf.PI * frequency / sampleRate;
                data[i] = Mathf.Sin(phase) * Mathf.Exp(-4f * progress) * 0.5f;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Arpeggio(string name, float[] notes, float noteSeconds)
        {
            const int sampleRate = 22050;
            int perNote = Mathf.RoundToInt(sampleRate * noteSeconds);
            var data = new float[perNote * notes.Length];

            for (int n = 0; n < notes.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    float t = i / (float)sampleRate;
                    float envelope = Mathf.Exp(-5f * i / (float)perNote);
                    data[n * perNote + i] = Mathf.Sin(2f * Mathf.PI * notes[n] * t) * envelope * 0.45f;
                }
            }

            AudioClip clip = AudioClip.Create(name, data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Loop(string name, float[] notes, float noteSeconds)
        {
            const int sampleRate = 22050;
            int perNote = Mathf.RoundToInt(sampleRate * noteSeconds);
            var data = new float[perNote * notes.Length];

            for (int n = 0; n < notes.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    float t = i / (float)sampleRate;
                    float fade = Mathf.Sin(Mathf.PI * i / perNote);
                    data[n * perNote + i] = Mathf.Sin(2f * Mathf.PI * notes[n] * t) * fade * 0.25f;
                }
            }

            AudioClip clip = AudioClip.Create(name, data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
