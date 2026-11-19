using System;
using System.Collections.Generic;
using System.Text;
using Boll.Core.Game;
using Newtonsoft.Json;

namespace Boll.Core.Save
{
    /// <summary>Файловая система приложения (в Unity — persistentDataPath).</summary>
    public interface IFileSystem
    {
        bool Exists(string name);
        string Read(string name);
        void Write(string name, string text);
        void Move(string from, string to);   // с заменой
        void Delete(string name);
    }

    public sealed class MemoryFs : IFileSystem
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool Exists(string n) => Files.ContainsKey(n);
        public string Read(string n) => Files[n];
        public void Write(string n, string t) => Files[n] = t;
        public void Move(string f, string t) { Files[t] = Files[f]; Files.Remove(f); }
        public void Delete(string n) => Files.Remove(n);
    }

    public sealed class DiskFs : IFileSystem
    {
        readonly string dir;
        public DiskFs(string directory) { dir = directory; System.IO.Directory.CreateDirectory(dir); }
        string P(string n) => System.IO.Path.Combine(dir, n);
        public bool Exists(string n) => System.IO.File.Exists(P(n));
        public string Read(string n) => System.IO.File.ReadAllText(P(n), Encoding.UTF8);
        public void Write(string n, string t) => System.IO.File.WriteAllText(P(n), t, new UTF8Encoding(false));
        public void Move(string f, string t) { if (System.IO.File.Exists(P(t))) System.IO.File.Delete(P(t)); System.IO.File.Move(P(f), P(t)); }
        public void Delete(string n) { if (System.IO.File.Exists(P(n))) System.IO.File.Delete(P(n)); }
    }

    public sealed class Counter { public int Games, Wins; }

    public sealed class Stats
    {
        public Counter Total = new Counter();
        public Counter[] ByBot = { new Counter(), new Counter(), new Counter(), new Counter(), new Counter(), new Counter(), new Counter(), new Counter() };
        public Dictionary<string, Counter> ByKind = new Dictionary<string, Counter>();
    }

    public sealed class Settings
    {
        public bool AimLine, BounceLine;
        public string Physics = "medium";
        public int Master = 100, Music = 70, Effects = 100;
    }

    public sealed class ActiveMatch
    {
        public bool Free;            // свободный режим
        public int BotLevel = 1;
        public MatchSnapshot Snapshot;
    }

    public sealed class SaveData
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public int Stars;
        public Dictionary<string, int> Owned = new Dictionary<string, int>(); // купленный скин → уплаченная цена
        public string Cloth = "cloth_green", Cue = "cue_classic";
        public int BotsUnlocked = 4;
        public Stats Stats = new Stats();
        public Settings Settings = new Settings();
        public Dictionary<string, string> LastMap = new Dictionary<string, string>();
        public ActiveMatch Active;
    }

    public enum LoadStatus { Fresh, Ok, Recovered, Corrupt }

    /// <summary>Один JSON-файл с версией; запись через временный файл, прежняя копия хранится.</summary>
    public sealed class SaveStore
    {
        public const string Main = "save.json", Tmp = "save.tmp", Bak = "save.bak";
        readonly IFileSystem fs;
        public SaveStore(IFileSystem fs) { this.fs = fs; }

        static readonly JsonSerializerSettings Js = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore };

        bool TryParse(string file, out SaveData d)
        {
            d = null;
            if (!fs.Exists(file)) return false;
            try
            {
                d = JsonConvert.DeserializeObject<SaveData>(fs.Read(file), Js);
                if (d == null || d.Version < 1 || d.Version > SaveData.CurrentVersion) { d = null; return false; }
                if (d.Version < SaveData.CurrentVersion) { d.Active = null; d.Version = SaveData.CurrentVersion; } // несовместимая партия — без зачёта
                if (d.Stats == null || d.Stats.ByBot == null || d.Stats.ByBot.Length != 8 || d.Settings == null || d.Owned == null) { d = null; return false; }
                return true;
            }
            catch (Exception) { d = null; return false; }
        }

        public SaveData Load(out LoadStatus status)
        {
            if (TryParse(Main, out var d)) { status = LoadStatus.Ok; return d; }
            if (TryParse(Bak, out d)) { status = LoadStatus.Recovered; Save(d); return d; }
            bool any = fs.Exists(Main) || fs.Exists(Bak);
            status = any ? LoadStatus.Corrupt : LoadStatus.Fresh;
            return new SaveData();
        }

        public void Save(SaveData d)
        {
            fs.Write(Tmp, JsonConvert.SerializeObject(d));
            if (TryParse(Main, out _)) fs.Write(Bak, fs.Read(Main));
            fs.Move(Tmp, Main);
        }
    }

    /// <summary>Журнал ошибок до 256 КБ. Без персональных данных.</summary>
    public sealed class ErrorLog
    {
        public const int MaxBytes = 256 * 1024;
        readonly IFileSystem fs; readonly Func<string> now; readonly string file;
        string text = "";

        public ErrorLog(IFileSystem fs, Func<string> now, string file = "log.txt")
        {
            this.fs = fs; this.now = now; this.file = file;
            if (fs.Exists(file)) { try { text = fs.Read(file); } catch (Exception) { text = ""; } }
        }

        public string Text => text;

        /// <summary>true — прошлая сессия завершилась аварийно (нет записи CLOSE после START).</summary>
        public bool Start(string device, string appVersion, string engineVersion)
        {
            bool crashed = false;
            int s = text.LastIndexOf(" START ", StringComparison.Ordinal), c = text.LastIndexOf(" CLOSE", StringComparison.Ordinal);
            if (s >= 0 && c < s) crashed = true;
            if (crashed) Append("CRASH previous session ended abnormally");
            Append("START " + device + "; app " + appVersion + "; engine " + engineVersion);
            return crashed;
        }

        public void Close() => Append("CLOSE");
        public void Exception(string message, string stack) => Append("EXCEPTION " + message + "\n" + stack);

        void Append(string line)
        {
            text += "[" + now() + "] " + line + "\n";
            while (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            {
                int cut = text.IndexOf('\n', Math.Min(text.Length - 1, 4096));
                text = cut < 0 ? "" : text.Substring(cut + 1);
            }
            try { fs.Write(file, text); } catch (Exception) { /* журнал не должен ронять игру */ }
        }
    }
}
