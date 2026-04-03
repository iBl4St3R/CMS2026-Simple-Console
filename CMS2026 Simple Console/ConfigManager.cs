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

        private static readonly Dictionary<string, string> Defaults = new()
        {
            { "uitoolkit_priority",    "true"  },
            { "show_timestamps",       "true"  },
            { "lock_input_when_open",  "true"  },
            { "max_log_lines",         "2000"  },
        };

        public ConfigManager(string modDir, Action<string> log)
        {
            _log = log;
            string dir = Path.Combine(modDir, "CMS2026SimpleConsole");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "CMS2026SimpleConsole.cfg");

            LoadDefaults();
            Load();
        }

        public string ConfigFilePath => _path;
        public string ConfigFolderPath => Path.GetDirectoryName(_path);

        // ── Read ─────────────────────────────────────────────────────────────────
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

        // ── Write ────────────────────────────────────────────────────────────────
        public void Set(string key, string value)
        {
            _values[key.ToLower()] = value.Trim();
            Save();
        }

        public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

        public void RestoreDefaults()
        {
            LoadDefaults();
            Save();
            _log("[Config] Defaults restored.");
        }

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
                Save();
                _log($"[Config] New config created: {_path}");
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
            _log($"[Config] Loaded {loaded} options from {_path}");
        }

        private void Save()
        {
            var lines = new System.Collections.Generic.List<string>
            {
                "# CMS2026 Simple Console - configuration file",
                "# Edit manually or use the in-game config panel.",
                "# Boolean values: true / false",
                ""
            };

            foreach (var kv in _values)
                lines.Add($"{kv.Key} = {kv.Value}");

            File.WriteAllLines(_path, lines);
        }

        public void PrintAll(Action<string> print)
        {
            print("[Config] Current settings:");
            foreach (var kv in _values)
                print($"  {kv.Key} = {kv.Value}");
        }
    }
}