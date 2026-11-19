using UnityEngine;

namespace Boll.App
{
    /// <summary>Звуки и музыка, синтезируются при запуске (внешних файлов нет — ресурсы CC0 не нужны).</summary>
    public sealed class Sfx : MonoBehaviour
    {
        public static Sfx I;
        const int Rate = 44100;
        AudioClip hit, rail, pocket, cue, click, music;
        AudioSource[] pool; int next; AudioSource musicSrc;
        System.Random rnd = new System.Random(7);
        float effects = 1, musicVol = 0.7f;

        public static void Create(Transform parent)
        {
            var go = new GameObject("Sfx"); go.transform.SetParent(parent);
            I = go.AddComponent<Sfx>(); I.Init();
        }

        void Init()
        {
            hit = Clip("hit", 0.14f, (t, n) => Mathf.Sin(2 * Mathf.PI * 2600 * t) * Mathf.Exp(-t * 55) * 0.55f + n * Mathf.Exp(-t * 160) * 0.5f);
            rail = Clip("rail", 0.18f, (t, n) => Mathf.Sin(2 * Mathf.PI * 170 * t) * Mathf.Exp(-t * 30) * 0.8f + n * Mathf.Exp(-t * 90) * 0.25f);
            pocket = Clip("pocket", 0.45f, (t, n) => Mathf.Sin(2 * Mathf.PI * (130 - 60 * t) * t) * Mathf.Exp(-t * 12) * 0.7f + n * Mathf.Exp(-t * 18) * 0.2f * Mathf.Abs(Mathf.Sin(t * 70)));
            cue = Clip("cue", 0.1f, (t, n) => n * Mathf.Exp(-t * 80) * 0.7f + Mathf.Sin(2 * Mathf.PI * 900 * t) * Mathf.Exp(-t * 70) * 0.4f);
            click = Clip("click", 0.06f, (t, n) => Mathf.Sin(2 * Mathf.PI * 1300 * t) * Mathf.Exp(-t * 70) * 0.5f);
            music = Music();
            pool = new AudioSource[10];
            for (int i = 0; i < pool.Length; i++) { pool[i] = gameObject.AddComponent<AudioSource>(); pool[i].playOnAwake = false; }
            musicSrc = gameObject.AddComponent<AudioSource>(); musicSrc.clip = music; musicSrc.loop = true; musicSrc.playOnAwake = false;
            musicSrc.Play();
        }

        delegate float Fn(float t, float noise);

        AudioClip Clip(string name, float len, Fn f)
        {
            int n = (int)(len * Rate); var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate, (float)(rnd.NextDouble() * 2 - 1)), -1f, 1f);
            var c = AudioClip.Create(name, n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Мягкая петля из четырёх аккордов (Am F C G), 32 с.</summary>
        AudioClip Music()
        {
            float[][] chords = { new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 261.6f, 329.6f }, new[] { 196f, 246.9f, 293.7f } };
            float seg = 8f; int n = (int)(seg * 4 * Rate); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate; int ci = (int)(t / seg); float lt = t - ci * seg;
                float env = Mathf.Min(1, lt / 1.5f) * Mathf.Min(1, (seg - lt) / 1.5f);   // плавные стыки
                float s = 0;
                foreach (float f in chords[ci]) s += Mathf.Sin(2 * Mathf.PI * f * 0.5f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * t) * 0.2f;
                float arp = chords[ci][(int)(lt * 2) % 3] * 2f;
                s += Mathf.Sin(2 * Mathf.PI * arp * t) * 0.25f * Mathf.Exp(-(lt * 2 % 1) * 4);
                d[i] = s * env * 0.12f;
            }
            var c = AudioClip.Create("music", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        public void Apply(int master, int music, int fx)
        {
            AudioListener.volume = master / 100f; musicVol = music / 100f; effects = fx / 100f;
            if (musicSrc != null) musicSrc.volume = musicVol * 0.6f;
        }

        void Play(AudioClip c, float vol, float pitch)
        {
            if (c == null || pool == null) return;
            AudioSource s = pool[next]; next = (next + 1) % pool.Length;
            s.pitch = pitch; s.PlayOneShot(c, Mathf.Clamp01(vol) * effects);
        }

        static float Vol(double speed) => Mathf.Clamp01(0.2f + (float)speed * 0.35f);

        public static void Hit(double speed) => I?.Play(I.hit, Vol(speed), 0.92f + (float)I.rnd.NextDouble() * 0.16f);
        public static void Rail(double speed) => I?.Play(I.rail, Vol(speed), 0.95f + (float)I.rnd.NextDouble() * 0.1f);
        public static void Pocket() => I?.Play(I.pocket, 0.8f, 1f);
        public static void CueHit(double power) => I?.Play(I.cue, 0.3f + (float)power * 0.7f, 0.95f + (float)power * 0.1f);
        public static void Click() => I?.Play(I.click, 0.6f, 1f);
    }
}
