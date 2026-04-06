using CMS2026SimpleConsole;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CMS2026SimpleConsole
{
    public class ModInfo
    {
        /// <summary>Nazwa assembly do auto-detekcji (np. "TurboBoostMod")</summary>
        public string AssemblyName { get; set; }

        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string GitHubUrl { get; set; }
        public string NexusUrl { get; set; }

        /// <summary>
        /// Jeśli null — zostanie auto-wykryta z MelonInfo lub assembly version.
        /// Ustaw ręcznie tylko jeśli auto-detekcja nie działa.
        /// </summary>
        public string VersionOverride { get; set; }

        // ── Wypełniane przez ModRegistry, nie przez autora moda ──────────────
        public bool IsLoaded { get; internal set; }
        public string Version { get; internal set; }  // resolved
    }






    public static class ModRegistry
    {
        private static readonly List<ModInfo> _mods = new();

        /// <summary>Wywoływane gdy mod się dopisze przez API lub po załadowaniu fallbacków.</summary>
        internal static event Action<ModInfo> OnModRegistered;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Zewnętrzny mod rejestruje się w panelu Mods.
        /// Zwraca false jeśli AssemblyName już jest na liście.
        /// </summary>
        public static bool RegisterMod(ModInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Name)) return false;

            string key = (info.AssemblyName ?? info.Name).ToLowerInvariant();
            if (_mods.Any(m => (m.AssemblyName ?? m.Name).ToLowerInvariant() == key))
                return false;

            Resolve(info);
            _mods.Add(info);
            OnModRegistered?.Invoke(info);
            return true;
        }

        internal static IEnumerable<ModInfo> GetAll() => _mods;

        // ── Wewnętrzne ────────────────────────────────────────────────────────

        /// <summary>
        /// Ładuje KnownModsDatabase jako fallback.
        /// Pomija mody które już się zarejestrowały przez API.
        /// </summary>
        internal static void LoadFallbacks()
        {
            foreach (var known in KnownModsDatabase.Mods)
            {
                string key = (known.AssemblyName ?? known.Name).ToLowerInvariant();
                if (_mods.Any(m => (m.AssemblyName ?? m.Name).ToLowerInvariant() == key))
                    continue;   // mod już się zarejestrował przez API — pomijamy

                Resolve(known);

                // Fallback pokazuj tylko jeśli mod faktycznie jest załadowany
                if (!known.IsLoaded) continue;

                _mods.Add(known);
                OnModRegistered?.Invoke(known);
            }
        }

        /// <summary>Wykrywa czy mod jest załadowany i wyciąga wersję.</summary>
        private static void Resolve(ModInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.AssemblyName))
            {
                info.IsLoaded = true;
                info.Version = info.VersionOverride ?? "?";
                return;
            }

            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(
                    a.GetName().Name, info.AssemblyName,
                    StringComparison.OrdinalIgnoreCase));

            info.IsLoaded = asm != null;

            if (!info.IsLoaded)
            {
                info.Version = info.VersionOverride ?? "—";
                return;
            }

            // Priorytet: VersionOverride → MelonInfo → AssemblyVersion
            info.Version = info.VersionOverride
                        ?? TryGetMelonVersion(asm)
                        ?? asm.GetName().Version?.ToString(3)
                        ?? "?";
        }

        private static string TryGetMelonVersion(Assembly asm)
        {
            try
            {
                foreach (var attr in asm.GetCustomAttributes(false))
                {
                    var t = attr.GetType();
                    if (t.Name != "MelonInfoAttribute") continue;
                    return t.GetProperty("Version")?.GetValue(attr)?.ToString();
                }
            }
            catch { }
            return null;
        }
    }



    /// <summary>
    /// Ręcznie utrzymywana lista znanych modów — fallback gdy autor moda
    /// nie zarejestrował się przez ConsoleAPI.RegisterMod().
    /// </summary>
    internal static class KnownModsDatabase
    {
        internal static readonly ModInfo[] Mods =
        {
            //new ModInfo
            //{
            //    AssemblyName = "TurboBoostMod",
            //    Name         = "Turbo Boost Unlimited",
            //    Author       = "MaxRevs",
            //    Description  = "Removes turbo boost limits",
            //    GitHubUrl    = "https://github.com/...",
            //    NexusUrl     = "https://www.nexusmods.com/..."
            //},
            //new ModInfo
            //{
            //    AssemblyName = "CleanGaragePro",
            //    Name         = "Clean Garage Pro",
            //    Author       = "SpotlessStudio",
            //    Description  = "Garage cleanliness overhaul",
            //    NexusUrl     = "https://www.nexusmods.com/..."
            //},
            
        };
    }
}







//API HOW TO ADD YOURSELF


//var apiType = AppDomain.CurrentDomain.GetAssemblies()
//    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
//    .FirstOrDefault(t => t.FullName == "CMS2026SimpleConsole.ConsoleAPI");

//apiType?.GetMethod("RegisterMod")?.Invoke(null, new object[]
//{
//    "MyModAssembly",   // assembly name — for auto ref
//    "My Cool Mod",
//    "MyNick",
//    "Does cool things",
//    "https://github.com/...",
//    "https://nexusmods.com/...",
//    null               // version — null = auto
//});