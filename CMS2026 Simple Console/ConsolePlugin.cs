using Il2Cpp;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using UnityEngine;

[assembly: MelonInfo(typeof(CMS2026SimpleConsole.ConsolePlugin),
    "CMS2026 Simple  Console", "1.1.0", "Blaster")]
[assembly: MelonGame("Red Dot Games", "Car Mechanic Simulator 2026 Demo")]
[assembly: MelonGame("Red Dot Games", "Car Mechanic Simulator 2026")]

namespace CMS2026SimpleConsole
{
    public class ConsolePlugin : MelonMod
    {
        public const string Version = "1.1.0";

        public static string ModDir { get; private set; }
        public static MelonLogger.Instance Log => Melon<ConsolePlugin>.Logger;
        public static ConfigManager Config { get; set; }

        private static string _userLibsDir;
        private static GameObject _consoleHost;

        public static CMS2026SimpleConsoleComponent ConsoleComponent { get; set; }

        public override void OnInitializeMelon()
        {
            ModDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _userLibsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserLibs");

            var modAlc = AssemblyLoadContext.GetLoadContext(typeof(ConsolePlugin).Assembly);
            if (modAlc != null)
            {
                modAlc.Resolving += OnAlcResolving;
                Log.Msg($"[Resolver] Registered on ALC: {modAlc.Name ?? "unnamed"}");
            }

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            ClassInjector.RegisterTypeInIl2Cpp<CMS2026SimpleConsoleComponent>();
            Log.Msg("CMS2026 Simple Console loaded!");
        }

        private static Assembly OnAlcResolving(AssemblyLoadContext alc, AssemblyName assemblyName)
        {
            string path = Path.Combine(_userLibsDir, assemblyName.Name + ".dll");
            if (File.Exists(path))
            {
                Log.Msg($"[Resolver ALC] {assemblyName.Name}");
                return alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
            }
            return null;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(_userLibsDir, name + ".dll");
            if (File.Exists(path))
            {
                Log.Msg($"[Resolver AppDomain] {name}");
                return Assembly.Load(File.ReadAllBytes(path));
            }
            return null;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (_consoleHost != null) return;

            _consoleHost = new GameObject("CMS2026_SimpleConsole");
            _consoleHost.AddComponent<CMS2026SimpleConsoleComponent>();
            UnityEngine.Object.DontDestroyOnLoad(_consoleHost);

            Log.Msg($"CMS2026SimpleConsoleComponent added (scene: {sceneName})");

            if (ConsoleComponent != null && ConsoleComponent.Renderer is UIToolkitConsoleRenderer uitR)
                uitR.ReapplyTopmost();
        }
    }
}