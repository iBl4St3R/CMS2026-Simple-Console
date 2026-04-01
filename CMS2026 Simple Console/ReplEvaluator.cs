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
            // ZMIANA: Roslyn siedzi w UserLibs, nie w folderze moda
            string userLibsDir = Path.Combine(gameDir, "UserLibs");

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
        Path.Combine(net6Dir, "MelonLoader.dll"),
        Path.Combine(net6Dir, "Il2CppInterop.Runtime.dll"),
        Path.Combine(net6Dir, "Il2CppInterop.Common.dll"),
        // ZMIANA: UserLibs zamiast folderu moda
        Path.Combine(userLibsDir, "Microsoft.CodeAnalysis.dll"),
        Path.Combine(userLibsDir, "Microsoft.CodeAnalysis.CSharp.dll"),
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

            // Dodatkowo: standardowe .NET assemblies z runtime
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            foreach (string path in Directory.GetFiles(runtimeDir, "*.dll"))
            {
                try
                {
                    // Sprawdź czy to managed assembly zanim dodasz do referencji
                    using var stream = File.OpenRead(path);
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
                    if (!peReader.HasMetadata) continue; // natywny DLL — pomiń

                    _references.Add(MetadataReference.CreateFromFile(path));
                    ok++;
                }
                catch { }
            }

            _log($"[REPL] References: {ok} OK, {skip} pominięto");
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
            var printField = type.GetField("Print",
                BindingFlags.Public | BindingFlags.Static);
            printField?.SetValue(null, (Action<string>)_log);

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