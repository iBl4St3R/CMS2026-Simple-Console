using CMS2026SimpleConsole;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CMS2026SimpleConsole
{
    public class ReplEvaluator
    {
        private readonly Action<string> _log;
        private List<MetadataReference> _references;

        // Szablon — kod użytkownika trafia do Execute()
        // Print() dostępne jako helper do wypisywania
        private const string TEMPLATE = @"
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppCMS.Player;
using Il2CppCMS.Scenes.Loader;
using Il2CppCMS.Player.Controller;
using Il2CppCMS.Core.Car;
using Il2CppCMS.SaveSystem.Containers.Car;
using Il2CppInterop.Runtime;

public static class __Repl
{{
    public static Action<string> Print;
    public static Action<string> Log;

    public static void Execute()
    {{
        {0}
    }}
}}";

        public ReplEvaluator(Action<string> log)
        {
            _log = log;
            BuildReferences();
        }

        private void BuildReferences()
        {
            _references = new List<MetadataReference>();

            string gameDir = AppDomain.CurrentDomain.BaseDirectory;
            string melonDir = Path.Combine(gameDir, "MelonLoader");
            string interopDir = Path.Combine(melonDir, "Il2CppAssemblies");
            string net6Dir = Path.Combine(melonDir, "net6");
            string userLibsDir = Path.Combine(gameDir, "UserLibs");
            string modsDir = Path.Combine(gameDir, "Mods");   // ← NOWE

            _log($"[REPL] MelonDir:   {melonDir}");
            _log($"[REPL] InteropDir: {interopDir}");
            _log($"[REPL] UserLibs:   {userLibsDir}");

            var dlls = new[]
            {
        Path.Combine(interopDir, "Il2Cppmscorlib.dll"),
        Path.Combine(interopDir, "UnityEngine.CoreModule.dll"),
        Path.Combine(interopDir, "UnityEngine.IMGUIModule.dll"),
        Path.Combine(interopDir, "UnityEngine.InputLegacyModule.dll"),
        Path.Combine(interopDir, "UnityEngine.PhysicsModule.dll"),
        Path.Combine(interopDir, "Assembly-CSharp.dll"),
        Path.Combine(interopDir, "Il2CppUniTask.dll"),
        Path.Combine(interopDir, "Il2CppFusion.Runtime.dll"),
        Path.Combine(interopDir, "Il2CppFusion.Common.dll"),
        Path.Combine(interopDir, "Il2CppFusion.Addons.SimpleKCC.dll"),
        Path.Combine(interopDir, "UnityEngine.UIElementsModule.dll"),
        Path.Combine(interopDir, "UnityEngine.TextRenderingModule.dll"),
        Path.Combine(net6Dir,    "MelonLoader.dll"),
        Path.Combine(net6Dir,    "Il2CppInterop.Runtime.dll"),
        Path.Combine(net6Dir,    "Il2CppInterop.Common.dll"),
        Path.Combine(userLibsDir,"Microsoft.CodeAnalysis.dll"),
        Path.Combine(userLibsDir,"Microsoft.CodeAnalysis.CSharp.dll"),
    };

            int ok = 0, skip = 0;
            foreach (string path in dlls)
            {
                if (!File.Exists(path))
                {
                    _log($"[REPL] MISSING: {Path.GetFileName(path)}");
                    skip++;
                    continue;
                }
                try
                {
                    _references.Add(MetadataReference.CreateFromFile(path));
                    ok++;
                }
                catch (Exception ex)
                {
                    _log($"[REPL] Ref error {Path.GetFileName(path)}: {ex.Message}");
                    skip++;
                }
            }

            // ── Mods folder — ładuj każdy DLL który jest już załadowany w AppDomain ──
            // Sprawdzamy czy assembly z Mods jest faktycznie załadowane zanim dodamy
            // referencję — konsola działa bez frameworka, framework działa bez konsoli
            var loadedNames = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name),
                StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(modsDir))
            {
                foreach (string dllPath in Directory.GetFiles(modsDir, "*.dll"))
                {
                    string asmName = Path.GetFileNameWithoutExtension(dllPath);

                    // Pomiń siebie samego
                    if (asmName == "CMS2026SimpleConsole") continue;

                    // Dodaj tylko jeśli assembly jest faktycznie załadowane w procesie
                    if (!loadedNames.Contains(asmName)) continue;

                    try
                    {
                        using var stream = File.OpenRead(dllPath);
                        using var peReader = new System.Reflection.PortableExecutable
                                                 .PEReader(stream);
                        if (!peReader.HasMetadata) continue;

                        _references.Add(MetadataReference.CreateFromFile(dllPath));
                        _log($"[REPL] Mod ref added: {asmName}");
                        ok++;
                    }
                    catch { skip++; }
                }
            }

            // ── .NET runtime assemblies ──────────────────────────────────────────
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            foreach (string path in Directory.GetFiles(runtimeDir, "*.dll"))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
                    if (!peReader.HasMetadata) continue;
                    _references.Add(MetadataReference.CreateFromFile(path));
                    ok++;
                }
                catch { }
            }

            _log($"[REPL] References: {ok} OK, {skip} skiped.");
        }

        public void Evaluate(string userCode)
        {
            // Umożliwiamy dwa tryby:
            // 1. Samo wyrażenie/instrukcja:  "Print(Camera.main.transform.position.ToString())"
            // 2. Pełny blok z return:        if(...){ Print("tak"); } else { Print("nie"); }
            string fullCode = string.Format(TEMPLATE, userCode);

            var syntaxTree = CSharpSyntaxTree.ParseText(fullCode);

            var compilation = CSharpCompilation.Create(
                assemblyName: "__Repl_" + Guid.NewGuid().ToString("N"),
                syntaxTrees: new[] { syntaxTree },
                references: _references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug,
                    allowUnsafe: true)
            );

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            // Błędy kompilacji → wypisz i wyjdź
            if (!emitResult.Success)
            {
                foreach (var d in emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _log($"[REPL] Compile error: {d.GetMessage()}  ({d.Location.GetLineSpan()})");
                }
                return;
            }

            // Załaduj skompilowany assembly do pamięci i wywołaj Execute()
            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());
            var type = assembly.GetType("__Repl");

            // Podpinamy callbacka Print → AddLog w konsoli
            var printField = type.GetField("Print", BindingFlags.Public | BindingFlags.Static);
            printField?.SetValue(null, (Action<string>)_log);

            var logField = type.GetField("Log", BindingFlags.Public | BindingFlags.Static);
            logField?.SetValue(null, (Action<string>)_log);   //


            var method = type.GetMethod("Execute",
                BindingFlags.Public | BindingFlags.Static);

            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException tie)
            {
                _log($"[REPL] Runtime exception: {tie.InnerException?.Message}");
                _log($"[REPL] {tie.InnerException?.StackTrace?.Split('\n')[0]}");
            }
        }
    }
}