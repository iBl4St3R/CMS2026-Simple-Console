using Il2CppCMS.Player.Controller;
using Il2CppCMS.Scenes.Loader;
using Il2CppCMS.UI.SceneLoading;
using CMS2026SimpleConsole;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;

namespace CMS2026SimpleConsole
{
    public class CMS2026SimpleConsoleComponent : MonoBehaviour
    {
        public CMS2026SimpleConsoleComponent(IntPtr ptr) : base(ptr) { }

        private ReplEvaluator _repl;

        // ── Stan okna ────────────────────────────────────────────────────────────
        private bool _visible = true;
        private Rect _windowRect = new Rect(20f, 20f, 640f, 500f);
        private Vector2 _logScroll = Vector2.zero;
        private string _cmdInput = "";
        private const int WindowId = 9871;

        private readonly List<string> _logLines = new List<string>();
        private const int MaxLogLines = 2000;

        // FIX: StringBuilder i parts jako pola — nie przekazujemy ich jako parametrów
        // (IL2CPP nie obsługuje string[] / StringBuilder jako parametrów metod instancji)
        private StringBuilder _dumpSb;
        private string[] _cmdParts;

        private string DumpDir => Path.Combine(ConsolePlugin.ModDir, "CMS2026CMS2026SimpleConsole");

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void Awake()
        {
            Directory.CreateDirectory(DumpDir);
            AddLog("[CMS2026SimpleConsole] Awake OK F7=toggle");
            AddLog("[CMS2026SimpleConsole] Unity " + Application.unityVersion);
            AddLog("[CMS2026SimpleConsole] Scenes: " + SceneManager.sceneCount);

            try
            {
                AddLog("[REPL] Initializing...");
                _repl = new ReplEvaluator(AddLog);
                AddLog("[REPL] OK");
            }
            catch (Exception ex)
            {
                AddLog("[REPL] INIT ERROR: " + ex.GetType().Name);
                AddLog("[REPL] " + ex.Message);
                AddLog("[REPL] " + (ex.StackTrace?.Split('\n')[0] ?? "no stacktrace"));
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _visible = !_visible;
                AddLog(_visible ? "[UI] Visible" : "[UI] Hidden");
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            // Zapisujemy oryginalny kolor i ustawiamy nasz (czarny z 85% nieprzezroczystości)
            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);

            _windowRect = GUI.Window(
                WindowId,
                _windowRect,
                (GUI.WindowFunction)DrawWindow,
                "CMS2026 Simple Console  [F7=hide]"
            );

            // Przywracamy domyślny kolor dla reszty gry
            GUI.backgroundColor = originalBg;
        }

        // ── Rysowanie okna ───────────────────────────────────────────────────────

        private void DrawWindow(int id)
        {
            // 1. Obszar dla logów (bez zmian)
            float logAreaHeight = _windowRect.height - 100f;
            Rect logRect = new Rect(10, 20, _windowRect.width - 20, logAreaHeight);

            int linesToShow = 15;
            int startIdx = Math.Max(0, _logLines.Count - linesToShow);
            string visibleText = "";
            for (int i = startIdx; i < _logLines.Count; i++)
            {
                visibleText += _logLines[i] + "\n";
            }

            GUI.Label(logRect, visibleText);

            // 2. Obszar wejścia (Input) (bez zmian)
            Rect inputRect = new Rect(10, _windowRect.height - 70, _windowRect.width - 100, 25);
            Rect btnRect = new Rect(_windowRect.width - 85, _windowRect.height - 70, 75, 25);

            _cmdInput = GUI.TextField(inputRect, _cmdInput);

            if (GUI.Button(btnRect, "CmdInput") ||
                (Event.current.isKey && Event.current.keyCode == KeyCode.Return && _cmdInput != ""))
            {
                ExecuteCommand(_cmdInput);
                _cmdInput = "";
                Event.current.Use();
            }

            // 3. Przyciski funkcyjne na dole (Poprawione pozycje i zmiana na Help)
            if (GUI.Button(new Rect(10, _windowRect.height - 35, 80, 25), "Clear"))
                _logLines.Clear();

            // Zmiana: Przycisk Help, wywołuje komendę "help"
            if (GUI.Button(new Rect(100, _windowRect.height - 35, 80, 25), "Help"))
                ExecuteCommand("help");

            // Poprawka: Przesunięto X na 190 (poprzedni przycisk kończył się na 180)
            if (GUI.Button(new Rect(190, _windowRect.height - 35, 100, 25), "Copy log"))
                CopyToClipboard();

            // 4. Twój podpis w prawym dolnym rogu
            GUIStyle signatureStyle = new GUIStyle(GUI.skin.label);
            signatureStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1.0f);

            // Przesunąłem X na -120 i zmniejszyłem szerokość, by tekst sam "dobił" do prawej krawędzi
            Rect signatureRect = new Rect(_windowRect.width - 80, _windowRect.height - 35, 110, 25);
            GUI.Label(signatureRect, "by Blaster", signatureStyle);

            // Pozwala przesuwać okno
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        // ── Interpreter komend ───────────────────────────────────────────────────
        private void CopyToClipboard()
        {
            try
            {
                if (_logLines.Count == 0)
                {
                    AddLog("No logs to copy.");
                    return;
                }

                // Sklejamy wszystkie linie w jeden ciąg tekstowy
                StringBuilder sb = new StringBuilder();
                foreach (var line in _logLines)
                {
                    sb.AppendLine(line);
                }

                // Magiczna linia Unity, która robi całą robotę:
                GUIUtility.systemCopyBuffer = sb.ToString();

                AddLog("Log copied to clipboard!");
            }
            catch (Exception e)
            {
                AddLog("Error: " + e.Message);
            }
        }
        private void ExecuteCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            AddLog("> " + raw);

            _cmdParts = raw.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (_cmdParts.Length == 0) return;

            string cmd = _cmdParts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    // ── Scene loading ────────────────────────────────────────────
                    case "resetscene":
                        AddLog("Reloading garage scene...");
                        if (Il2CppCMS.Scenes.Loader.SceneLoader.Instance != null)
                            Il2CppCMS.Scenes.Loader.SceneLoader.Instance.LoadScene("Garage");
                        else
                        {
                            // Fallback: reload current scene by build index
                            int idx = SceneManager.GetActiveScene().buildIndex;
                            SceneManager.LoadScene(idx);
                        }
                        break;

                    // ── REPL ─────────────────────────────────────────────────────
                    case "eval":
                    case "run":
                        string code = raw.Substring(cmd.Length).Trim();
                        if (_repl == null) { AddLog("[eval] ERROR: ReplEvaluator not initialized!"); break; }
                        if (string.IsNullOrEmpty(code)) AddLog("[eval] Usage: eval <C# code>");
                        else _repl.Evaluate(code);
                        break;

                    // ── Player ───────────────────────────────────────────────────
                    case "charspeed":
                        if (_cmdParts.Length > 1 && float.TryParse(_cmdParts[1], out float newSpeed))
                        {
                            var movement = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerMovement>();
                            if (movement != null && movement.settings != null)
                            {
                                movement.settings.MaxWalkingSpeed = newSpeed;
                                movement.settings.MaxRunningSpeed = newSpeed * 1.5f;
                                AddLog($"Walk speed set to: {newSpeed}  (run: {newSpeed * 1.5f})");
                            }
                            else AddLog("PlayerMovement not found.");
                        }
                        else AddLog("Usage: charspeed <value>");
                        break;

                    case "addexp":
                        int expAmount = 1000;
                        if (_cmdParts.Length > 1) int.TryParse(_cmdParts[1], out expAmount);
                        Il2CppCMS.Player.PlayerData.AddPlayerExp(expAmount, true);
                        AddLog($"Added {expAmount} EXP. Level: {Il2CppCMS.Player.PlayerData.PlayerLevel}");
                        break;


                    // ── Demo restrictions ────────────────────────────────────────
                    case "removedemowall":
                    case "removedemo":
                        RemoveDemoWalls();
                        break;

                    // ── World ────────────────────────────────────────────────────
                    case "find":
                        if (_cmdParts.Length > 1)
                        {
                            string search = _cmdParts[1].ToLower();
                            var allObjects = GameObject.FindObjectsOfType<GameObject>(true);
                            int count = 0;
                            foreach (var go in allObjects)
                            {
                                if (go.name.ToLower().Contains(search))
                                {
                                    AddLog($"  {go.name} | active={go.activeSelf}");
                                    count++;
                                }
                            }
                            AddLog($"Found: {count}");
                        }
                        else AddLog("Usage: find <name>");
                        break;

                    case "scenes":
                        LogSceneInfo();
                        break;

                    case "help":
                        AddLog("Commands:");
                        AddLog("  resetscene           – reload garage scene");
                        AddLog("  eval <C# code>       – execute C# at runtime");
                        AddLog("  charspeed <n>        – set player walk speed");
                        AddLog("  addexp [n]           – add EXP (default 1000)");
                        AddLog("  removedemowall       – disable demo area limits");
                        AddLog("  find <name>          – find GameObjects by name");
                        AddLog("  scenes               – list loaded scenes");
                        break;

                    default:
                        AddLog("[?] Unknown command: " + cmd + "  (type 'help')");
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog("[ERR] " + ex.Message);
                ConsolePlugin.Log.Error(ex.ToString());
            }
        }

        private void RemoveDemoWalls()
        {
            // Nazwy obiektów blokujących demo — rozszerz jeśli znajdziesz więcej
            var targets = new[]
            {
                "DemoWalls",
                "DemoVehiclesDetector",
                "Garage_Exterior_Demo_Collider",
                "Garage_Exterior_Demo_Wall_Blocked_1"
            };

            int disabled = 0;
            foreach (var name in targets)
            {
                var go = GameObject.Find(name);
                if (go != null)
                {
                    go.SetActive(false);
                    AddLog($"  Disabled: {name}");
                    disabled++;
                }
                else
                {
                    AddLog($"  Not found: {name}");
                }
            }
            AddLog($"RemoveDemoWalls: {disabled}/{targets.Length} objects disabled.");
        }


        // FIX: brak parametrów — dane z pola _cmdParts
        private void CmdFind()
        {
            if (_cmdParts.Length < 2) { AddLog("[find] Uzycie: find <fragment>"); return; }
            string frag = _cmdParts[1];
            int found = 0;

            // FIX: Action<> usunięte — inline iteracja po hierarchii
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                Scene scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                    FindInHierarchy(root, frag, ref found);
            }

            AddLog(found == 0
                ? "[find] Brak wynikow dla: " + frag
                : "[find] Znaleziono: " + found);
        }

        // FIX: rekurencja bez delegata Action<>
        private void FindInHierarchy(GameObject go, string frag, ref int found)
        {
            if (go.name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddLog("  OK " + BuildPath(go.transform) + " [active=" + go.activeSelf + "]");
                found++;
            }
            for (int i = 0; i < go.transform.childCount; i++)
                FindInHierarchy(go.transform.GetChild(i).gameObject, frag, ref found);
        }

        private void CmdGet()
        {
            if (_cmdParts.Length < 3) { AddLog("[get] Uzycie: get <goName> <ComponentType>"); return; }
            var go = GameObject.Find(_cmdParts[1]);
            if (go == null) { AddLog("[get] Nie znaleziono GO: " + _cmdParts[1]); return; }

            UnityEngine.Component found = null;
            foreach (var c in go.GetComponents<UnityEngine.Component>())
            {
                if (c.GetType().Name.Equals(_cmdParts[2], StringComparison.OrdinalIgnoreCase))
                { found = c; break; }
            }
            if ((object)found == null) { AddLog("[get] Brak komponentu: " + _cmdParts[2]); return; }

            Type type = found.GetType();
            AddLog("[get] " + type.FullName + " na " + go.name + ":");
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { AddLog("  ." + f.Name + " = " + f.GetValue(found)); }
                catch { AddLog("  ." + f.Name + " = <blad odczytu>"); }
            }
        }

        private void CmdSet()
        {
            if (_cmdParts.Length < 5) { AddLog("[set] Uzycie: set <goName> <ComponentType> <field> <value>"); return; }
            var go = GameObject.Find(_cmdParts[1]);
            if (go == null) { AddLog("[set] Nie znaleziono GO: " + _cmdParts[1]); return; }

            UnityEngine.Component found = null;
            foreach (var c in go.GetComponents<UnityEngine.Component>())
            {
                if (c.GetType().Name.Equals(_cmdParts[2], StringComparison.OrdinalIgnoreCase))
                { found = c; break; }
            }
            if ((object)found == null) { AddLog("[set] Brak komponentu: " + _cmdParts[2]); return; }

            FieldInfo field = found.GetType().GetField(
                _cmdParts[3], BindingFlags.Public | BindingFlags.Instance);
            if (field == null) { AddLog("[set] Pole nie istnieje: " + _cmdParts[3]); return; }

            object parsed = ParseValue(_cmdParts[4], field.FieldType);
            field.SetValue(found, parsed);
            AddLog("[set] OK " + _cmdParts[2] + "." + _cmdParts[3] + " = " + _cmdParts[4]);
        }

        // ── Scene Dump ───────────────────────────────────────────────────────────

        private void DumpAllScenes()
        {
            // FIX: StringBuilder jako pole — nie przekazywany jako parametr
            _dumpSb = new StringBuilder();
            _dumpSb.AppendLine("=== CMS2026 Scene Dump ===");
            _dumpSb.AppendLine("Timestamp : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _dumpSb.AppendLine("Unity     : " + Application.unityVersion);
            _dumpSb.AppendLine("SceneCount: " + SceneManager.sceneCount);
            _dumpSb.AppendLine();

            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                _dumpSb.AppendLine("Scene [" + i + "]: \"" + scene.name
                    + "\" buildIndex=" + scene.buildIndex
                    + " loaded=" + scene.isLoaded);

                if (!scene.isLoaded) { _dumpSb.AppendLine("  <nie zaladowana>"); continue; }

                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                    DumpGameObject(root, 1);
                _dumpSb.AppendLine();
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filePath = Path.Combine(DumpDir, "dump_" + stamp + ".txt");
            File.WriteAllText(filePath, _dumpSb.ToString());
            _dumpSb = null;

            string msg = "[Dump] Zapisano: " + filePath;
            AddLog(msg);
            ConsolePlugin.Log.Msg(msg);
        }

        // FIX: StringBuilder usunięty z parametrów — używamy pola _dumpSb
        private void DumpGameObject(GameObject go, int depth)
        {
            string pad = new string(' ', depth * 2);
            _dumpSb.Append(pad + "[" + go.name + "]");
            if (!go.activeSelf) _dumpSb.Append(" (inactive)");
            _dumpSb.AppendLine();

            foreach (var c in go.GetComponents<UnityEngine.Component>())
            {
                string typeName;
                try { typeName = c.GetType().Name; }
                catch { typeName = "<unknown>"; }
                _dumpSb.AppendLine(pad + "  # " + typeName);
            }

            for (int i = 0; i < go.transform.childCount; i++)
                DumpGameObject(go.transform.GetChild(i).gameObject, depth + 1);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void LogSceneInfo()
        {
            AddLog("[Scenes] Loaded: " + SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                AddLog($"  [{i}] \"{s.name}\" idx={s.buildIndex} loaded={s.isLoaded}");
            }
        }

        private static string BuildPath(Transform t)
        {
            var parts = new List<string>();
            while ((object)t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static object ParseValue(string raw, Type targetType)
        {
            if (targetType == typeof(float)) return float.Parse(raw);
            if (targetType == typeof(int)) return int.Parse(raw);
            if (targetType == typeof(bool)) return bool.Parse(raw);
            if (targetType == typeof(string)) return raw;
            throw new NotSupportedException("Nieobslugiwany typ: " + targetType.Name);
        }

        private void AddLog(string line)
        {
            string entry = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line;
            _logLines.Add(entry);
            if (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);

            // Przekaż też do MelonLoader logger
            ConsolePlugin.Log.Msg(line);
        }
    }
}