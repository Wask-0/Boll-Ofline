using UnityEngine;

namespace Boll.App
{
    /// <summary>Звуки и музыка, синтезируются при запуске (внешних файлов нет — ресурсы CC0 не нужны).</summary>
    public sealed class Sfx : MonoBehaviour
    {
        public static Sfx I;
        const int Rate = 44100;
        AudioClip hit, rail, bumper, click, music;
        float lastHit, lastRail, lastBumper;
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
            // только чистые короткие «тоны», без шума: шар о шар, шар о борт, шар о бампер
            hit = Clip("hit", 0.16f, (t, n) => (Mathf.Sin(2 * Mathf.PI * 1900 * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * 3300 * t) * 0.25f) * Mathf.Exp(-t * 48) * Mathf.Min(1, t * 900));
            rail = Clip("rail", 0.2f, (t, n) => (Mathf.Sin(2 * Mathf.PI * 150 * t) * 0.7f + Mathf.Sin(2 * Mathf.PI * 310 * t) * 0.25f) * Mathf.Exp(-t * 26) * Mathf.Min(1, t * 700));
            bumper = Clip("bumper", 0.22f, (t, n) => Mathf.Sin(2 * Mathf.PI * (520 - 140 * t) * t) * Mathf.Exp(-t * 20) * 0.75f * Mathf.Min(1, t * 700));
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
                d[i] = s * env * 0.07f;
            }
            var c = AudioClip.Create("music", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        public void Apply(int master, int music, int fx)
        {
            AudioListener.volume = master / 100f; musicVol = music / 100f; effects = fx / 100f;
            if (musicSrc != null) musicSrc.volume = musicVol * 0.5f;
        }

        void Play(AudioClip c, float vol, float pitch)
        {
            if (c == null || pool == null) return;
            AudioSource s = pool[next]; next = (next + 1) % pool.Length;
            s.pitch = pitch; s.PlayOneShot(c, Mathf.Clamp01(vol) * effects);
        }

        const float MinSpeed = 0.12f;      // тише — это качение вдоль борта или касание покоящихся шаров, звука нет
        static float Vol(double speed) => Mathf.Clamp01(0.12f + (float)speed * 0.3f);

        public static void Hit(double speed)
        {
            if (I == null || speed < MinSpeed || Time.unscaledTime - I.lastHit < 0.04f) return;
            I.lastHit = Time.unscaledTime; I.Play(I.hit, Vol(speed), 0.94f + (float)I.rnd.NextDouble() * 0.12f);
        }

        public static void Rail(double speed)
        {
            if (I == null || speed < MinSpeed || Time.unscaledTime - I.lastRail < 0.05f) return;
            I.lastRail = Time.unscaledTime; I.Play(I.rail, Vol(speed), 0.96f + (float)I.rnd.NextDouble() * 0.08f);
        }

        public static void Bumper(double speed)
        {
            if (I == null || speed < MinSpeed || Time.unscaledTime - I.lastBumper < 0.05f) return;
            I.lastBumper = Time.unscaledTime; I.Play(I.bumper, Vol(speed), 1f);
        }
        public static void Click() => I?.Play(I.click, 0.6f, 1f);
    }
}
