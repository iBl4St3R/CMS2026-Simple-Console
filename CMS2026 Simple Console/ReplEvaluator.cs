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

        // ── Custom namespaces registered by external mods ─────────────────────
        private static readonly List<string> _customUsings = new List<string>();
        private static string _runtimeTemplate = null; // cached after first injection

        private const string BASE_TEMPLATE = @"
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
using UnityEngine.UI;
using System.Runtime.InteropServices;
{CUSTOM_USINGS}

// ── PDH GPU helper — available in every REPL script ──────────────────────
// [... GPU PDH code bez zmian ...]
// ──────────────────────────────────────────────────────────────────────────

public static class __Repl
{{
    public static Action<string> Print;
    public static Action<string> Log;

    public static void Execute()
    {{
        {0}
    }}
}}";

        // ── Public API for external mods ───────────────────────────────────────

        /// <summary>
        /// Register a namespace to be injected into REPL template.
        /// Call this from your mod's OnInitializeMelon or OnSceneWasInitialized.
        /// Thread-safe, idempotent.
        /// </summary>
        public static void RegisterNamespace(string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(namespaceName)) return;

            lock (_customUsings)
            {
                if (_customUsings.Contains(namespaceName)) return;
                _customUsings.Add(namespaceName);
                _runtimeTemplate = null; // invalidate cache
            }
        }

        /// <summary>
        /// Add an assembly reference for REPL compilation.
        /// Call after RegisterNamespace to ensure the types are resolvable.
        /// </summary>
        public void AddReference(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                return;

            try
            {
                var metaRef = MetadataReference.CreateFromFile(assemblyPath);
                lock (_references)
                {
                    // Avoid duplicates
                    if (_references.Any(r => r.Display == metaRef.Display))
                        return;

                    _references.Add(metaRef);
                }
                _log($"[REPL] Reference added: {Path.GetFileName(assemblyPath)}");
            }
            catch (Exception ex)
            {
                _log($"[REPL] AddReference failed for {Path.GetFileName(assemblyPath)}: {ex.Message}");
            }
        }

        // ── Constructor ────────────────────────────────────────────────────────

        public ReplEvaluator(Action<string> log)
        {
            _log = log;
            BuildReferences();
        }


        // ── Template builder with custom usings ────────────────────────────────

        private string GetTemplate()
        {
            if (_runtimeTemplate != null) return _runtimeTemplate;

            lock (_customUsings)
            {
                string customUsingsBlock = _customUsings.Count > 0
                    ? string.Join("\n", _customUsings.Select(ns => $"using {ns};"))
                    : "";

                _runtimeTemplate = BASE_TEMPLATE.Replace("{CUSTOM_USINGS}", customUsingsBlock);
                return _runtimeTemplate;
            }
        }

        // ── BuildReferences (bez zmian, z PDH GPU code) ────────────────────────

        private void BuildReferences()
        {
            _references = new List<MetadataReference>();

            string gameDir = AppDomain.CurrentDomain.BaseDirectory;
            string melonDir = Path.Combine(gameDir, "MelonLoader");
            string interopDir = Path.Combine(melonDir, "Il2CppAssemblies");
            string net6Dir = Path.Combine(melonDir, "net6");
            string userLibsDir = Path.Combine(gameDir, "UserLibs");
            string modsDir = Path.Combine(gameDir, "Mods");

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
                Path.Combine(interopDir, "UnityEngine.UIModule.dll"),
                Path.Combine(interopDir, "UnityEngine.UI.dll"),
                Path.Combine(interopDir, "Unity.TextMeshPro.dll"),
                Path.Combine(interopDir, "UnityEngine.ImageConversionModule.dll"),
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

            // ── Mods folder — tylko assemblies loaded in AppDomain ─────────────
            var loadedNames = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name),
                StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(modsDir))
            {
                foreach (string dllPath in Directory.GetFiles(modsDir, "*.dll"))
                {
                    string asmName = Path.GetFileNameWithoutExtension(dllPath);

                    if (asmName == "CMS2026SimpleConsole") continue;
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

            // ── .NET runtime assemblies ────────────────────────────────────────
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

            _log($"[REPL] References: {ok} OK, {skip} skipped.");
        }

        // ── Evaluate (używa GetTemplate() zamiast stałej TEMPLATE) ─────────────

        public void Evaluate(string userCode)
        {
            string processedCode = userCode.Trim();

            if (processedCode.EndsWith(";"))
                processedCode = processedCode.Substring(0, processedCode.Length - 1).Trim();

            bool isSingleExpr = !processedCode.Contains("\n")
                             && !processedCode.StartsWith("var ")
                             && !processedCode.StartsWith("Print(");

            if (isSingleExpr)
            {
                bool isAssignment = processedCode.Contains("=") && !processedCode.Contains("==");

                if (!isAssignment)
                {
                    bool isVoid = IsVoidExpression(processedCode);

                    if (isVoid)
                    {
                        // Void method — wywołaj bez print
                        processedCode += ";";
                    }
                    else
                    {
                        // Value/reference type — print result
                        processedCode = $@"
                {{
                    var __r = ({processedCode});
                    Print(__r == null ? ""null"" : __r.ToString());
                }}";
                    }
                }
                else
                {
                    processedCode += ";";
                }
            }
            else
            {
                processedCode += ";";
            }

            string fullCode = string.Format(GetTemplate(), processedCode);

            var syntaxTree = CSharpSyntaxTree.ParseText(fullCode);

            var compilation = CSharpCompilation.Create(
                assemblyName: "__Repl_" + Guid.NewGuid().ToString("N"),
                syntaxTrees: new[] { syntaxTree },
                references: _references,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug,
                    allowUnsafe: true)
            );

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                foreach (var d in emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _log($"[REPL] Compile error: {d.GetMessage()}  ({d.Location.GetLineSpan()})");
                }
                return;
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());
            var type = assembly.GetType("__Repl");

            var printField = type.GetField("Print", BindingFlags.Public | BindingFlags.Static);
            printField?.SetValue(null, (Action<string>)_log);

            var logField = type.GetField("Log", BindingFlags.Public | BindingFlags.Static);
            logField?.SetValue(null, (Action<string>)_log);

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


        private bool IsVoidExpression(string expr)
        {
            try
            {
                string testCode = $"var __test = ({expr});";
                string fullTest = string.Format(GetTemplate(), testCode);
                var tree = CSharpSyntaxTree.ParseText(fullTest);
                var comp = CSharpCompilation.Create("__VoidCheck", new[] { tree }, _references);
                var errors = comp.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error);

                foreach (var err in errors)
                {
                    string msg = err.GetMessage().ToLower();
                    if (msg.Contains("void") ||
                        msg.Contains("cannot assign") ||
                        msg.Contains("implicitly-typed"))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}