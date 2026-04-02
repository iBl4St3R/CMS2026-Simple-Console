using System;
using System.Collections.Generic;
using System.IO;

namespace CMS2026SimpleConsole
{
    public class ConfigManager
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new();
        private readonly Action<string> _log;

        // ── Domyślne wartości ────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> Defaults = new()
        {
            { "autolock", "false" },
        };

        public ConfigManager(string modDir, Action<string> log)
        {
            _log = log;
            string dir = Path.Combine(modDir, "CMS2026SimpleConsole");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "config.txt");

            LoadDefaults();
            Load();
        }

        // ── Odczyt ──────────────────────────────────────────────────────────────
        public bool GetBool(string key, bool fallback = false)
        {
            if (_values.TryGetValue(key.ToLower(), out string v))
                return v.Trim().ToLower() == "true";
            return fallback;
        }

        public string GetString(string key, string fallback = "")
        {
            return _values.TryGetValue(key.ToLower(), out string v) ? v : fallback;
        }

        // ── Zapis ────────────────────────────────────────────────────────────────
        public void Set(string key, string value)
        {
            _values[key.ToLower()] = value.Trim();
            Save();
        }

        public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

        // ── I/O ──────────────────────────────────────────────────────────────────
        private void LoadDefaults()
        {
            foreach (var kv in Defaults)
                _values[kv.Key] = kv.Value;
        }

        private void Load()
        {
            if (!File.Exists(_path))
            {
                Save(); // utwórz plik z domyślnymi
                _log($"[Config] Utworzono nowy config: {_path}");
                return;
            }

            int loaded = 0;
            foreach (string line in File.ReadAllLines(_path))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 1) continue;

                string key = trimmed.Substring(0, eq).Trim().ToLower();
                string val = trimmed.Substring(eq + 1).Trim();
                _values[key] = val;
                loaded++;
            }
            _log($"[Config] Załadowano {loaded} opcji z {_path}");
        }

        private void Save()
        {
            var lines = new List<string>
            {
                "# CMS2026 Simple Console - config",
                "# Edytuj ręcznie lub używaj komendy: setconfig <klucz> <wartość>",
                ""
            };

            foreach (var kv in _values)
                lines.Add($"{kv.Key} = {kv.Value}");

            File.WriteAllLines(_path, lines);
        }

        public void PrintAll(Action<string> print)
        {
            print("[Config] Aktualne ustawienia:");
            foreach (var kv in _values)
                print($"  {kv.Key} = {kv.Value}");
        }
    }
}