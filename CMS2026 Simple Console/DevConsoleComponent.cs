using Il2CppCMS.Player.Controller;
using Il2CppCMS.Scenes.Loader;
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

        // ── Input blocking ───────────────────────────────────────────────────────
        private object _uiCommonMap;
        private Il2CppCMS.Player.Controller.PlayerInput _playerInput;
        private bool _inputLocked = false;


        // ── Renderer ─────────────────────────────────────────────────────────────
        // false = IMGUI fallback (zmień ręcznie gdy UIToolkit nie działa)
        private bool _useUIToolkit = true;
        private IConsoleRenderer _renderer;

        private ReplEvaluator _repl;

        private readonly List<string> _logLines = new List<string>();
        private const int MaxLogLines = 2000;
       
        private string[] _cmdParts;

        private string DumpDir =>
            Path.Combine(ConsolePlugin.ModDir, "CMS2026SimpleConsole");


        public IConsoleRenderer Renderer => _renderer;
        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void Awake()
        {
            Directory.CreateDirectory(DumpDir);
            ConsolePlugin.ConsoleComponent = this;  // ← rejestracja

            InitRenderer();

            AddLog("[CMS2026SimpleConsole] Awake OK  F7=toggle");
            AddLog("[CMS2026SimpleConsole] Unity " + Application.unityVersion);
            AddLog("[CMS2026SimpleConsole] Renderer: " +
                (_renderer is UIToolkitConsoleRenderer ? "UIToolkit" : "IMGUI"));

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
            }

            InitInputBlocking();
        }


        private void InitRenderer()
        {
            // Zawsze startuj na IMGUI — działa natychmiast
            var imguiRenderer = new IMGUIConsoleRenderer(_logLines);
            imguiRenderer.OnCommandSubmitted += HandleCommand;
            imguiRenderer.Initialize();
            _renderer = imguiRenderer;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
                _renderer.SetVisible(!_renderer.IsVisible);

            _renderer.OnUpdate();
        }

        private void OnGUI()
        {
            _renderer.OnGUI();
        }

        private void OnDestroy()
        {
            ConsolePlugin.ConsoleComponent = null;
            if (_inputLocked) SetGameInputEnabled(true);
            _renderer?.Destroy();
        }

        // ── Command handler (wspólny dla obu rendererów) ─────────────────────────
        private void HandleCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (raw == "__lockinput") { ToggleInputLock(); return; }

            if (raw == "__clear") { _logLines.Clear(); _renderer.ClearLines(); return; }
            if (raw == "__copylog") { CopyToClipboard(); return; }
            if (raw == "__switchrenderer") { SwitchRenderer(); return; }

            ExecuteCommand(raw);
        }


        private void SwitchRenderer()
        {
            if (_renderer is IMGUIConsoleRenderer)
            {
                var uitR = new UIToolkitConsoleRenderer(AddLog, _logLines);
                uitR.OnCommandSubmitted += HandleCommand;
                if (uitR.TryInit())
                {
                    _renderer.Destroy();
                    _renderer = uitR;
                    AddLog("[Renderer] Przełączono na UIToolkit");
                }
                else
                {
                    AddLog("[Renderer] UIToolkit niedostępny");
                }
            }
            else
            {
                _renderer.Destroy();
                var imgui = new IMGUIConsoleRenderer(_logLines);
                imgui.OnCommandSubmitted += HandleCommand;
                imgui.Initialize();
                _renderer = imgui;
                AddLog("[Renderer] Przełączono na IMGUI");
            }
        }

        // ── Interpreter komend ───────────────────────────────────────────────────
        private void ExecuteCommand(string raw)
        {
            AddLog("> " + raw);

            _cmdParts = raw.Split(new char[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (_cmdParts.Length == 0) return;

            string cmd = _cmdParts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "resetscene":
                        AddLog("Reloading garage scene...");
                        if (SceneLoader.Instance != null)
                            SceneLoader.Instance.LoadScene("Garage");
                        else
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        break;

                    case "eval":
                    case "run":
                        string code = raw.Substring(cmd.Length).Trim();
                        if (_repl == null) { AddLog("[eval] ERROR: REPL not init!"); break; }
                        if (string.IsNullOrEmpty(code)) AddLog("[eval] Usage: eval <C# code>");
                        else _repl.Evaluate(code);
                        break;

                    case "charspeed":
                        if (_cmdParts.Length > 1 &&
                            float.TryParse(_cmdParts[1], out float spd))
                        {
                            var mv = UnityEngine.Object.FindObjectOfType<PlayerMovement>();
                            if (mv?.settings != null)
                            {
                                mv.settings.MaxWalkingSpeed = spd;
                                mv.settings.MaxRunningSpeed = spd * 1.5f;
                                AddLog($"Walk={spd}  Run={spd * 1.5f}");
                            }
                            else AddLog("PlayerMovement not found.");
                        }
                        else AddLog("Usage: charspeed <value>");
                        break;

                    case "addexp":
                        int exp = 1000;
                        if (_cmdParts.Length > 1) int.TryParse(_cmdParts[1], out exp);
                        Il2CppCMS.Player.PlayerData.AddPlayerExp(exp, true);
                        AddLog($"Added {exp} EXP. Level: {Il2CppCMS.Player.PlayerData.PlayerLevel}");
                        break;

                    case "removedemowall":
                    case "removedemo":
                        RemoveDemoWalls();
                        break;

                    case "find":
                        if (_cmdParts.Length > 1)
                        {
                            string frag = _cmdParts[1].ToLower();
                            int n = 0;
                            foreach (var go in GameObject.FindObjectsOfType<GameObject>(true))
                            {
                                if (go.name.ToLower().Contains(frag))
                                {
                                    AddLog($"  {go.name} | active={go.activeSelf}");
                                    n++;
                                }
                            }
                            AddLog($"Found: {n}");
                        }
                        else AddLog("Usage: find <name>");
                        break;

                    case "scenes":
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                        {
                            var s = SceneManager.GetSceneAt(i);
                            AddLog($"  [{i}] \"{s.name}\" idx={s.buildIndex} loaded={s.isLoaded}");
                        }
                        break;

                    case "renderer":
                        AddLog("Renderer: " +
                            (_renderer is UIToolkitConsoleRenderer ? "UIToolkit" : "IMGUI"));
                        break;

                    case "help":
                        AddLog("Komendy:");
                        AddLog("  resetscene           – reload sceny garazu");
                        AddLog("  eval <C# code>       – uruchom C# w runtime");
                        AddLog("  charspeed <n>        – predkosc gracza");
                        AddLog("  addexp [n]           – dodaj EXP (domyslnie 1000)");
                        AddLog("  removedemowall       – wylacz limity demo");
                        AddLog("  find <name>          – szukaj GameObjects");
                        AddLog("  scenes               – lista scen");
                        AddLog("  renderer             – pokaz aktywny renderer");
                        break;

                    default:
                        AddLog("[?] Nieznana komenda: " + cmd + "  (wpisz 'help')");
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog("[ERR] " + ex.Message);
                ConsolePlugin.Log.Error(ex.ToString());
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void CopyToClipboard()
        {
            if (_logLines.Count == 0) { AddLog("Brak logow."); return; }
            var sb = new StringBuilder();
            foreach (var l in _logLines) sb.AppendLine(l);
            GUIUtility.systemCopyBuffer = sb.ToString();
            AddLog("Log skopiowany do schowka.");
        }

        private void RemoveDemoWalls()
        {
            var targets = new[]
            {
                "DemoWalls", "DemoVehiclesDetector",
                "Garage_Exterior_Demo_Collider",
                "Garage_Exterior_Demo_Wall_Blocked_1"
            };
            int disabled = 0;
            foreach (var name in targets)
            {
                var go = GameObject.Find(name);
                if (go != null) { go.SetActive(false); AddLog($"  Disabled: {name}"); disabled++; }
                else AddLog($"  Not found: {name}");
            }
            AddLog($"RemoveDemoWalls: {disabled}/{targets.Length}");
        }

        private void AddLog(string line)
        {
            string entry = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line;
            _logLines.Add(entry);
            if (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);

            _renderer?.AddLine(entry);
            ConsolePlugin.Log.Msg(line);
        }


        private void InitInputBlocking()
        {
            // Tylko logujemy że system jest gotowy — właściwe szukanie obiektów
            // odbywa się lazy w SetGameInputEnabled przy pierwszym wywołaniu
            var assetType = System.Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
            if (assetType == null)
                AddLog("[InputBlock] BRAK: Unity.InputSystem");
            else
                AddLog("[InputBlock] OK — przycisk 'Lock Input' gotowy");
        }


        public void ToggleInputLock()
        {
            _inputLocked = !_inputLocked;
            SetGameInputEnabled(!_inputLocked);
            AddLog(_inputLocked ? "[InputBlock] Input ZABLOKOWANY" : "[InputBlock] Input ODBLOKOWANY");
        }

        private void SetGameInputEnabled(bool enabled)
        {
            // ── Lazy find: PlayerInput ───────────────────────────────────────────
            if (_playerInput == null)
                _playerInput = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerInput>();

            if (_playerInput != null)
                _playerInput.enabled = enabled;
            else
                AddLog("[InputBlock] PlayerInput nie znaleziony (gracz nie zaladowany?)");

            // ── Lazy find: UI Common action map ─────────────────────────────────
            // Szukamy za każdym razem — referencja może być przestarzała po reload sceny
            _uiCommonMap = null;
            try
            {
                var assetType = System.Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
                if (assetType == null) return;

                var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(assetType));
                foreach (var raw in all)
                {
                    var asset = Activator.CreateInstance(assetType, new object[] { raw.Pointer });
                    var maps = assetType.GetProperty("actionMaps").GetValue(asset);
                    var indexer = maps.GetType().GetProperty("Item");
                    int count = (int)maps.GetType().GetProperty("Count").GetValue(maps);
                    for (int i = 0; i < count; i++)
                    {
                        var m = indexer.GetValue(maps, new object[] { i });
                        var name = (string)m.GetType().GetProperty("name").GetValue(m);
                        if (name == "UI Common") { _uiCommonMap = m; break; }
                    }
                    if (_uiCommonMap != null) break;
                }
            }
            catch (Exception ex)
            {
                AddLog("[InputBlock] Blad szukania mapy: " + ex.Message);
                return;
            }

            if (_uiCommonMap != null)
                _uiCommonMap.GetType().GetMethod(enabled ? "Enable" : "Disable").Invoke(_uiCommonMap, null);
            else
                AddLog("[InputBlock] UI Common map nie znaleziona");
        }

    }
}
//```

//---

//### Co gdzie i dlaczego

//**Przepływ sterowania: **
//```
//F7 → _renderer.SetVisible()
//Klawiatura (UIToolkit) → HandleKeyboard() w OnUpdate()
//Klawiatura (IMGUI) → GUI.TextField w OnGUI()
//Oba → OnCommandSubmitted → HandleCommand() w komponencie