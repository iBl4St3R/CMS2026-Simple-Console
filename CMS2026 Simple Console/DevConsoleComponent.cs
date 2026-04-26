using CMS2026SimpleConsole;
using Il2Cpp;
using Il2CppCMS.Player.Controller;
using Il2CppCMS.Scenes.Loader;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace CMS2026SimpleConsole
{
    public class CMS2026SimpleConsoleComponent : MonoBehaviour
    {
        public CMS2026SimpleConsoleComponent(IntPtr ptr) : base(ptr) { }

        // ── Input blocking ───────────────────────────────────────────────────────
        private Il2CppCMS.Player.Controller.PlayerInput _playerInput;
        private bool _inputLocked = false;

        private string _lastNonUIMode = "";
        private bool _consoleUsesHarmony = false;
        private bool _isNormalMode = false;
        private bool _isHarmonyMode = false;

        // ── Standalone input lock ─────────────────────────────────────────────
        private bool _standaloneLockActive = false;

        // ── Framework CursorManager bridge ───────────────────────────────────────
        private static System.Reflection.MethodInfo _fwRequest;
        private static System.Reflection.MethodInfo _fwRelease;
        private static bool _fwCursorResolved = false;
        private bool _cursorRequested = false;   // czy konsola aktualnie trzyma żądanie


        // ── Key binding ───────────────────────────────────────────────────────
        private bool _waitingForKey = false;
        private string _bindingConfigKey = "";
        private bool _bindingAllowDisable = true;
        private Action _bindingOnComplete = null;
        private string _bindingPromptText = "";

        // ── Renderer ─────────────────────────────────────────────────────────────
        private IConsoleRenderer _renderer;
        private ReplEvaluator _repl;
        private ConfigManager _config;

        private readonly List<string> _logLines = new List<string>();
        private string[] _cmdParts;

        // ── History ───────────────────────────────────────────────────────────────
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;
        private const int MaxHistory = 200;

        // ── Autocomplete ──────────────────────────────────────────────────────────
        private static readonly string[] _builtinCommands = new[]
 {
    "help", "helpadv", "exit", "save", "resetscene",
    "gamelocation", "savelocation", "modslocation", "carslocation",
    "charspeed", "addmoney", "setmoney", "addexp",
    "allskills", "timescale", "settime", "addtime", 
    "savetp", "tp", "listtp", "removetp",
    "stealcustomercar", "fixcar", "removedemowalls",
    "showgaragecars", "showparkingcars",
    "storecarinparking", "unstorecarfromparking",
    "find", "inspectobj", "dumpobj", "scenes",
    "run", "eval", "runfile"
};

        // ── Autocomplete cycling ───────────────────────────────────────────────
        private List<string> _acMatches = new List<string>();
        private int _acIndex = -1;
        private string _acPrefix = null;   // null = brak aktywnej sesji AC
        private bool _suppressResetAc = false;


        // ── Teleporty ─────────────────────────────────────────────────────────────
        private readonly Dictionary<string, Vector3> _teleports = new();
        private string TpFilePath => Path.Combine(ConsolePlugin.Config?.ConfigFolderPath?? Path.Combine(ConsolePlugin.ModDir, "CMS2026SimpleConsole"),"tps.txt");

        private int MaxLogLines => int.TryParse(_config?.GetString("max_log_lines", "2000"), out int v) && v >= 100 ? v : 2000;

        // ── Base path helper ──────────────────────────────────────────────────────────
        // ModDir = <GameDir>\Mods\CMS2026SimpleConsole  →  dwa poziomy wyżej = GameDir
        private string GameDir => Path.GetDirectoryName(Path.GetDirectoryName(ConsolePlugin.ModDir));

        private string DumpDir => Path.Combine(ConsolePlugin.ModDir, "CMS2026SimpleConsole");

        [HideFromIl2Cpp]
        public IConsoleRenderer Renderer => _renderer;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            Directory.CreateDirectory(DumpDir);
            ConsolePlugin.ConsoleComponent = this;

            // Config must come first so InitRenderer can read it
            _config = new ConfigManager(ConsolePlugin.ModDir, AddLog);
            ConsolePlugin.Config = _config;

            InitRenderer();


            // Subskrybuj panel Mods przed załadowaniem fallbacków
            if (_renderer is UIToolkitConsoleRenderer uitR2)
                ModRegistry.OnModRegistered += _ => uitR2.RebuildModsPanel();

            // Załaduj znane mody (fallback) — po chwili, żeby zewnętrzne mody zdążyły się dopisać
            MelonLoader.MelonCoroutines.Start(LoadFallbacksDelayed());

            AddLog("[CMS2026SimpleConsole] Awake OK  F7=toggle");
            AddLog("[CMS2026SimpleConsole] Unity " + Application.unityVersion);
            AddLog("[CMS2026SimpleConsole] Renderer: " +
                (_renderer is UIToolkitConsoleRenderer ? "UIToolkit" : "IMGUI"));
            AddLog($"[Config] uitoolkit_priority = {_config.GetBool("uitoolkit_priority", true)}");

            Application.add_logMessageReceived(new Action<string, string, LogType>(OnUnityLog));
            bool logsOn = _config.GetBool("capture_unity_logs", true);
            AddLog($"[Console] Unity log capture: {(logsOn ? "ON" : "OFF")}");


            if (_config.GetBool("repl_enabled", false))
            {
                if (CheckReplDependencies())
                {
                    try
                    {
                        AddLog("[REPL] Initializing...");
                        _repl = new ReplEvaluator(AddLog);
                        AddLog("[REPL] OK — run / eval / runfile available.");
                    }
                    catch (Exception ex)
                    {
                        AddLog("[REPL] INIT ERROR: " + ex.GetType().Name);
                        AddLog("[REPL] " + ex.Message);
                    }
                }
            }
            else
            {
                AddLog("[REPL] Disabled in config (repl_enabled = false).");
            }

            // Subscribe so registered commands show up in log immediately
            ConsoleAPI.OnCommandRegistered += (name, desc) => AddLog($"[API] Command registered: '{name}' — {desc}");


            //sub msg zewnetrznych
            ConsoleAPI.OnPrint += (msg, src) =>
            {
                string prefix = string.IsNullOrEmpty(src) ? "" : $"[{src}] ";
                AddLog($"{prefix}{msg}");
            };


            // Ukryj konsolę na starcie jeśli opcja wyłączona
            if (!(_config?.GetBool("show_at_startup", true) ?? true))
                _renderer.SetVisible(false);


            LoadTeleports();
        }

        private void InitRenderer()
        {
            // Always start on IMGUI — works immediately
            var imguiRenderer = new IMGUIConsoleRenderer(_logLines);
            imguiRenderer.OnCommandSubmitted += HandleCommand;
            imguiRenderer.Initialize();
            _renderer = imguiRenderer;

            // If UIToolkit priority is enabled, attempt to switch right away
            if (_config.GetBool("uitoolkit_priority", true))
            {
                var uitR = new UIToolkitConsoleRenderer(AddLog, _logLines);
                uitR.OnCommandSubmitted += HandleCommand;
                if (uitR.TryInit())
                {
                    _renderer.Destroy();
                    _renderer = uitR;
                    // AddLog here goes to the new UIToolkit renderer
                }
                // On failure we stay on IMGUI — UIToolkitConsoleRenderer logs its own errors
            }
        }

        private void Update()
        {
            // ── Key-binding capture — blokuje resztę Update ───────────────────
            if (_waitingForKey)
            {
                foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (!Input.GetKeyDown(kc)) continue;

                    if (kc == KeyCode.Escape)
                    {
                        _waitingForKey = false;
                        AddLog("[KeyBind] Cancelled.");
                    }
                    else if (kc == KeyCode.Delete && _bindingAllowDisable)
                    {
                        _config.Set(_bindingConfigKey, "None");
                        _waitingForKey = false;
                        AddLog($"[KeyBind] {_bindingConfigKey} disabled.");
                        _bindingOnComplete?.Invoke();
                    }
                    else if (kc != KeyCode.Delete)
                    {
                        _config.Set(_bindingConfigKey, kc.ToString());
                        _waitingForKey = false;
                        AddLog($"[KeyBind] {_bindingConfigKey} = {kc}");
                        _bindingOnComplete?.Invoke();
                    }
                    return;
                }
                _renderer.OnUpdate();
                return;
            }

            // ── Standalone lock toggle ────────────────────────────────────────
            KeyCode standaloneKey = ParseKey(_config?.GetString("standalone_lock_key", "None"));
            if (standaloneKey != KeyCode.None && Input.GetKeyDown(standaloneKey))
            {
                // Konsola z lock_input_when_open=ON ma priorytet — nie zmieniamy stanu
                bool consoleForcesLock = _renderer.IsVisible &&
                                         (_config?.GetBool("lock_input_when_open", true) ?? true);
                if (!consoleForcesLock)
                {
                    _standaloneLockActive = !_standaloneLockActive;
                    AddLog($"[Input] Standalone lock: {(_standaloneLockActive ? "ON" : "OFF")}");
                }
            }


            // ── Historia komend — Up/Down ─────────────────────────────────────────
            if (_renderer.IsVisible && _history.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    _historyIndex = Mathf.Clamp(_historyIndex + 1, 0, _history.Count - 1);
                    _renderer.CommandInput = _history[_history.Count - 1 - _historyIndex];
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    _historyIndex--;
                    if (_historyIndex < 0)
                    {
                        _historyIndex = -1;
                        _renderer.CommandInput = "";
                    }
                    else
                    {
                        _renderer.CommandInput = _history[_history.Count - 1 - _historyIndex];
                    }
                }
            }

            // ── Autocomplete — Tab (tylko IMGUI, UIToolkit wysyła __tab przez event) ──
            if (_renderer is IMGUIConsoleRenderer && _renderer.IsVisible && Input.GetKeyDown(KeyCode.Tab))
            {
                HandleAutocomplete();
            }




            // ── Toggle console ────────────────────────────────────────────────
            KeyCode toggleKey = ParseKey(_config?.GetString("toggle_console_key", "F7"), KeyCode.F7);
            if (Input.GetKeyDown(toggleKey))
            {
                if (!_renderer.IsVisible)
                {
                    bool inMenuScene = string.IsNullOrEmpty(_lastNonUIMode);
                    if (_isNormalMode || _isHarmonyMode || inMenuScene)
                    {
                        _consoleUsesHarmony = _isHarmonyMode;
                        _renderer.SetVisible(true);
                    }
                    else
                    {
                        ErrorMsg(_lastNonUIMode);
                    }
                }
                else
                {
                    _renderer.SetVisible(false);
                }
            }

            _renderer.OnUpdate();
        }

        // ── Harmony patch — blokuj lockState gdy konsola otwarta w aucie ──────
        private static HarmonyLib.Harmony _harmony;
        private static bool _blockCursorLock = false;

        private void ErrorMsg(string mode)
        {
            MelonLoader.MelonCoroutines.Start(ErrorMsgRoutine(mode));
            AddLog($"[Console] Unavailable in mode: {mode}");
        }

        [HideFromIl2Cpp]
        private System.Collections.IEnumerator ErrorMsgRoutine(string mode)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Il2CppCMS.UI.UIManager.Get().ShowPopup(
                        $"<color=#ff6060>Console unavailable in: {mode}</color>",
                        Il2Cpp.PopupType.Normal);
                }
                catch { }
                yield return new UnityEngine.WaitForSeconds(0.5f);
            }
        }

        private static void PatchCursor()
        {
            if (_harmony != null) return;
            _harmony = new HarmonyLib.Harmony("cms2026simpleconsole.cursorpatch");

            var original = typeof(UnityEngine.Cursor)
                .GetProperty("lockState")
                .GetSetMethod();

            var prefix = typeof(CMS2026SimpleConsoleComponent)
                .GetMethod(nameof(CursorLockPrefix),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            _harmony.Patch(original, new HarmonyLib.HarmonyMethod(prefix));
        }

        private static bool CursorLockPrefix(ref CursorLockMode value)
        {
            if (_blockCursorLock && value == CursorLockMode.Locked)
            {
                value = CursorLockMode.None;
                return true;
            }
            return true;
        }

        private void LateUpdate()
        {
            string rawMode = GetCurrentGameMode();
            if (rawMode != "UI" && rawMode != "")
                _lastNonUIMode = rawMode;

            _isNormalMode = _lastNonUIMode == "Garage"
                         || _lastNonUIMode == "GarageCustomization"
                         || _lastNonUIMode == "GarageAssemble"
                         || _lastNonUIMode == "GarageDisassemble"
                         || _lastNonUIMode == "Interior";

            _isHarmonyMode = _lastNonUIMode == "CarDrive"
                          || _lastNonUIMode == "GarageDrive";

            bool inCar = _isHarmonyMode; 


            TryResolveFrameworkCursor();
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool inGameScene = sceneName == "garage" || sceneName == "Garage";
            bool lockWhenOpen = _config?.GetBool("lock_input_when_open", true) ?? true;
            bool consoleLocks = _renderer.IsVisible && lockWhenOpen && !inCar;
            bool effectiveLock = (consoleLocks && inGameScene) || _standaloneLockActive;
            if (effectiveLock != _inputLocked)
            {
                _inputLocked = effectiveLock;
                SetGameInputEnabled(!effectiveLock);
            }
            if (!inGameScene && lockWhenOpen)
                SetMenuNavigationEnabled(!_renderer.IsVisible);

            bool wantCursor = _renderer.IsVisible;

            if (!inCar)
            {
                // Poza autem — normalna ścieżka przez framework
                if (wantCursor && !_cursorRequested)
                {
                    _cursorRequested = true;
                    if (inGameScene) CursorRequest();
                }
                else if (!wantCursor && _cursorRequested)
                {
                    _cursorRequested = false;
                    if (inGameScene) CursorRelease();
                }
                if (!HasFrameworkCursor && _renderer.IsVisible && inGameScene)
                {
                    if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                    if (Cursor.visible) Cursor.visible = false;
                }
                // FIX: wymuś odblokowanie kursora każdą klatkę gdy konsola widoczna
                if (_renderer.IsVisible && inGameScene)
                {
                    if (Cursor.lockState != CursorLockMode.None)
                        Cursor.lockState = CursorLockMode.None;
                }
            }
            else
            {
                if (wantCursor)
                {
                    PatchCursor();
                    _blockCursorLock = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    _blockCursorLock = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }


        private bool _menuNavLocked = false;

        private void SetMenuNavigationEnabled(bool enabled)
        {
            if (_menuNavLocked == !enabled) return;
            _menuNavLocked = !enabled;

            try
            {
                var assetType = System.Type.GetType(
                    "UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
                if (assetType == null) return;

                var all = Resources.FindObjectsOfTypeAll(
                    Il2CppInterop.Runtime.Il2CppType.From(assetType));

                foreach (var raw in all)
                {
                    var asset = Activator.CreateInstance(assetType, new object[] { raw.Pointer });
                    var maps = assetType.GetProperty("actionMaps").GetValue(asset);
                    int count = (int)maps.GetType().GetProperty("Count").GetValue(maps);
                    var indexer = maps.GetType().GetProperty("Item");

                    for (int i = 0; i < count; i++)
                    {
                        var m = indexer.GetValue(maps, new object[] { i });
                        var name = (string)m.GetType().GetProperty("name").GetValue(m);

                        // Blokujemy tylko mapy nawigacyjne — nie Gameplay, nie Car itp.
                        if (name != "UI Common" && name != "Main Menu") continue;

                        m.GetType().GetMethod(enabled ? "Enable" : "Disable").Invoke(m, null);
                    }
                }
            }
            catch (Exception ex) { AddLog("[InputBlock] MenuNav: " + ex.Message); }
        }


        private static Type _gameModeType;

        private static string GetCurrentGameMode()
        {
            try
            {
                if (_gameModeType == null)
                    _gameModeType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                        .FirstOrDefault(t => t.FullName == "Il2CppCMS.Core.GameMode");
                if (_gameModeType == null) return "";
                var inst = _gameModeType.GetProperty("instance").GetValue(null);
                if (inst == null) return "";
                return _gameModeType.GetProperty("CurrentMode").GetValue(inst)?.ToString() ?? "";
            }
            catch { return ""; }
        }

        [HideFromIl2Cpp]
        private System.Collections.IEnumerator LoadFallbacksDelayed()
        {
            // Czekamy jedną klatkę — zewnętrzne mody rejestrują się w OnSceneWasInitialized
            yield return null;
            yield return null;
            ModRegistry.LoadFallbacks();
        }

        private void OnGUI()
        {
            _renderer.OnGUI();

            if (!_waitingForKey) return;

            // Przyciemnienie tła
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float bw = 500f, bh = 120f;
            float bx = (Screen.width - bw) * 0.5f;
            float by = (Screen.height - bh) * 0.5f;

            GUI.backgroundColor = new Color(0.08f, 0.10f, 0.18f, 1f);
            GUI.Box(new Rect(bx, by, bw, bh), "");

            var title = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(bx, by + 16f, bw, 38f), _bindingPromptText, title);

            var hint = new GUIStyle(title)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.65f, 0.80f, 1f, 1f) }
            };
            string hintText = _bindingAllowDisable
                ? "ESC — cancel   |   DELETE — disable key"
                : "ESC — cancel";
            GUI.Label(new Rect(bx, by + 68f, bw, 28f), hintText, hint);
        }

        private void OnDestroy()
        {
            ConsolePlugin.ConsoleComponent = null;
            if (_inputLocked) SetGameInputEnabled(true);

            if (_cursorRequested)   
            {
                _cursorRequested = false;
                CursorRelease();
            }

            _blockCursorLock = false;
            _harmony?.UnpatchSelf();

            _renderer?.Destroy();
            Application.remove_logMessageReceived(new Action<string, string, LogType>(OnUnityLog));
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (!(_config?.GetBool("capture_unity_logs", true) ?? true)) return;
            if (condition.StartsWith("[CMS2026") || condition.StartsWith(">")) return;

            string prefix = type switch
            {
                LogType.Error => "<color=#c85050>[Unity:ERR]</color> ",
                LogType.Exception => "<color=#e04040>[Unity:EXC]</color> ",
                LogType.Warning => "<color=#c8922a>[Unity:WRN]</color> ",
                LogType.Assert => "<color=#a060c0>[Unity:AST]</color> ",
                _ => "<color=#606878>[Unity:LOG]</color> "
            };

            AddLog(prefix + condition);

            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
            {
                string firstLine = stackTrace.Split('\n')[0].Trim();
                if (!string.IsNullOrEmpty(firstLine))
                    AddLog($"<color=#804040>         at {firstLine}</color>");
            }
        }


        // ── Command handler ───────────────────────────────────────────────────────
        private void HandleCommand(string raw)
        {
            if (raw == "__resetac") { ResetAc(); return; }



            if (string.IsNullOrWhiteSpace(raw)) return;

            if (raw == "__clear") { _logLines.Clear(); _renderer.ClearLines(); return; }
            if (raw == "__copylog") { CopyToClipboard(); return; }
            if (raw == "__switchrenderer") { SwitchRenderer(); return; }
            if (raw == "__openconfig") { OpenConfigFile(); return; }
            if (raw == "__openfolder") { OpenModFolder(); return; }
            if (raw == "__applyconfig") { ApplyConfig(); return; }
            if (raw == "__tab") { HandleAutocomplete(); return; }

            if (raw.StartsWith("__startbind:"))
            {
                string cfgKey = raw.Substring("__startbind:".Length).Trim();
                bool allowDis = cfgKey != "toggle_console_key";
                StartKeyBinding(cfgKey, allowDis, () =>
                {
                    if (_renderer is UIToolkitConsoleRenderer uitR)
                        uitR.RefreshKeybindLabels();
                });
                return;
            }

            ExecuteCommand(raw);
        }

        // ── Config actions ────────────────────────────────────────────────────────
        private void OpenConfigFile()
        {
            try
            {
                string cfgPath = _config?.ConfigFilePath
                    ?? Path.Combine(ConsolePlugin.ModDir, "CMS2026SimpleConsole", "CMS2026SimpleConsole.cfg");
                // Select the file in Explorer
                Process.Start("explorer.exe", $"/select,\"{cfgPath}\"");
                AddLog($"[Config] Opened folder: {Path.GetDirectoryName(cfgPath)}");
            }
            catch (Exception ex) { AddLog("[Config] Failed to open folder: " + ex.Message); }
        }

        private void HandleAutocomplete()
        {
            string current = _renderer.CommandInput?.Trim() ?? "";
            if (current.Length == 0)
            {
                ResetAc();
                return;
            }

            bool isCycling = _acPrefix != null && _acMatches.Contains(current);

            if (!isCycling)
            {
                _acPrefix = current;
                _acMatches = GetAllAutocompleteMatches(_acPrefix);
                _acIndex = -1;
            }

            if (_acMatches.Count == 0) return;

            _acIndex = (_acIndex + 1) % _acMatches.Count;

            _suppressResetAc = true;                          // ← zablokuj reset
            _renderer.CommandInput = _acMatches[_acIndex];
            _suppressResetAc = false;                         // ← odblokuj

            if (_acMatches.Count > 1)
                ShowAcHint();
        }

        private void ResetAc()
        {
            if (_suppressResetAc) return;                     // ← ignoruj jeśli my ustawiamy

            string current = _renderer.CommandInput?.Trim() ?? "";
            if (_acPrefix == null) return;
            if (_acMatches.Contains(current)) return;

            _acMatches.Clear();
            _acIndex = -1;
            _acPrefix = null;
        }

        private void ShowAcHint()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#4a7fa5>[Tab]</color>  ");
            for (int i = 0; i < _acMatches.Count; i++)
            {
                bool active = i == _acIndex;
                sb.Append(active
                    ? $"<color=#7ec8a0><b>{_acMatches[i]}</b></color>"
                    : $"<color=#607870>{_acMatches[i]}</color>");
                if (i < _acMatches.Count - 1) sb.Append("  ");
            }
            AddLog(sb.ToString());
        }

        [HideFromIl2Cpp]
        private List<string> GetAllAutocompleteMatches(string prefix)
        {
            prefix = prefix.ToLowerInvariant();
            var results = new List<string>();

            foreach (string cmd in _builtinCommands)
                if (cmd.StartsWith(prefix) && cmd != prefix)
                    results.Add(cmd);

            foreach (var (name, _) in ConsoleAPI.GetAll())
                if (name.StartsWith(prefix) && name != prefix)
                    results.Add(name);

            return results;
        }

        private bool CheckReplDependencies()
        {
            string userLibs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserLibs");
            var required = new[]
            {
        "Microsoft.CodeAnalysis.dll",
        "Microsoft.CodeAnalysis.CSharp.dll",
    };

            bool ok = true;
            foreach (string dll in required)
            {
                string path = Path.Combine(userLibs, dll);
                if (!File.Exists(path))
                {
                    AddLog($"[REPL] MISSING dependency: {dll}");
                    AddLog($"[REPL] Place it in: {userLibs}");
                    ok = false;
                }
            }

            if (!ok)
                AddLog("[REPL] REPL disabled — install missing libraries and restart.");

            return ok;
        }

        [HideFromIl2Cpp]
        public ReplEvaluator Repl => _repl;
        private void OpenModFolder()
        {
            try
            {
                string folder = _config?.ConfigFolderPath
                    ?? Path.Combine(ConsolePlugin.ModDir, "CMS2026SimpleConsole");
                Process.Start("explorer.exe", folder);
                AddLog($"[Config] Opened mod folder: {folder}");
            }
            catch (Exception ex) { AddLog("[Config] Failed to open folder: " + ex.Message); }
        }

        private void OpenExplorerFolder(string path, string label)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", $"\"{path}\""); // ← cudzysłowy
                    AddLog($"[{label}] {path}");
                }
                else
                {
                    AddLog($"[{label}] Folder not found: {path}");
                }
            }
            catch (Exception ex) { AddLog($"[{label}] ERR: " + ex.Message); }
        }


        private void ApplyConfig()
        {
            if (_config != null &&
                int.TryParse(_config.GetString("max_log_lines", "2000"), out int maxLines) &&
                maxLines >= 100)
            {
                while (_logLines.Count > maxLines)
                    _logLines.RemoveAt(0);

                // Odśwież renderer żeby pokazał przycięte logi
                _renderer.ClearLines();
                foreach (var line in _logLines)
                    _renderer.AddLine(line);
            }
            AddLog("[Config] Settings applied.");
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
                    AddLog("[Renderer] Switched to UIToolkit");
                }
                else
                {
                    AddLog("[Renderer] UIToolkit unavailable — staying on IMGUI");
                }
            }
            else
            {
                _renderer.Destroy();
                var imgui = new IMGUIConsoleRenderer(_logLines);
                imgui.OnCommandSubmitted += HandleCommand;
                imgui.Initialize();
                _renderer = imgui;
                AddLog("[Renderer] Switched to IMGUI");
            }
        }


        [HideFromIl2Cpp]
        private void StartKeyBinding(string configKey, bool allowDisable, Action onComplete = null)
        {

            _bindingConfigKey = configKey;
            _bindingAllowDisable = allowDisable;
            _bindingOnComplete = onComplete;
            _bindingPromptText = $"Press the key for:  [{configKey}]";
            _waitingForKey = true;
            AddLog($"[KeyBind] Waiting for the key...  " +
                   $"(ESC=cancel{(allowDisable ? ", DEL=disable" : "")})");
        }

        private static KeyCode ParseKey(string value, KeyCode fallback = KeyCode.None)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return (KeyCode)System.Enum.Parse(typeof(KeyCode), value, true); }
            catch { return fallback; }
        }

        private string ColorizeLog(string line)
        {
            // ── Command echo ──────────────────────────────────────────────────────────
            if (line.StartsWith("> "))
                return $"<color=#7ec8a0>></color><color=#a8d8b8>{line.Substring(1)}</color>";

            // ── Prefix detection ──────────────────────────────────────────────────────
            if (!line.StartsWith("[")) return line;

            int end = line.IndexOf(']');
            if (end < 1) return line;

            string tag = line.Substring(0, end + 1);
            string rest = line.Substring(end + 1);
            string col = GetTagColor(tag);

            return col != null
                ? $"<color={col}>{tag}</color>{rest}"
                : line;
        }
        private static string GetTagColor(string tag) => tag switch
        {
            "[CMS2026SimpleConsole]" => "#5b8ab0",
            "[Console]" => "#5b8ab0",
            "[Config]" => "#5ba8a8",
            "[REPL]" => "#9b80c8",
            "[eval]" => "#9b80c8",
            "[runfile]" => "#9b80c8",
            "[Renderer]" => "#7878c0",
            "[UIToolkit]" => "#7878c0",
            "[API]" => "#50a878",
            "[Save]" => "#6898c0",
            "[Game]" => "#6898c0",
            "[Saves]" => "#6898c0",
            "[Mods]" => "#6898c0",
            "[Cars]" => "#6898c0",
            "[Input]" => "#8888a0",
            "[InputBlock]" => "#8888a0",
            "[KeyBind]" => "#a0a860",
            "[Inspect]" => "#78b890",
            "[Dump]" => "#78b890",
            "[GarageCars]" => "#78a080",
            "[Parking]" => "#78a080",
            "[Store]" => "#78a080",
            "[Unstore]" => "#78a080",
            "[fixcar]" => "#78a080",
            "[stealcustomercar]" => "#c87840",
            "[Heart]" => "#e87898",
            "[ERR]" => "#e05555",
            "[?]" => "#c87840",
            "[settime]" => "#78a8c8",
            "[addtime]" => "#78a8c8",
            "[timescale]" => "#78a8c8",
            "[noclip]" => "#a8c878",
            "[allskills]" => "#a8c878",
            "[TP]" => "#c8a870",
            "[savetp]" => "#c8a870",
            "[tp]" => "#c8a870",
            _ => null
        };


        // ── AddLog — respects show_timestamps config ──────────────────────────────
        private void AddLog(string line)
        {
            bool showTs = _config?.GetBool("show_timestamps", true) ?? true;

            string colored = ColorizeLog(line);

            string entry = showTs
                ? $"<color=#4a7fa5>[{DateTime.Now:HH:mm:ss}]</color> {colored}"
                : colored;

            _logLines.Add(entry);
            if (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);

            _renderer?.AddLine(entry);
            ConsolePlugin.Log.Msg(line);   // MelonLoader dostaje czysty string
        }

        // ── Command interpreter ───────────────────────────────────────────────────
        private void ExecuteCommand(string raw)
        {
            HardResetAc();
            AddLog("> " + raw);

            if (_history.Count == 0 || _history[_history.Count - 1] != raw)
            {
                _history.Add(raw);
                if (_history.Count > MaxHistory)
                    _history.RemoveAt(0);
            }
            _historyIndex = -1;

            _cmdParts = raw.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (_cmdParts.Length == 0) return;

            string cmd = _cmdParts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "run":
                    case "eval":
                        if (_repl == null)
                        {
                            AddLog("[REPL] REPL is not active.");
                            AddLog("[REPL] Enable 'repl_enabled' in Config panel and restart.");
                            break;
                        }
                        string code = raw.Substring(cmd.Length).Trim();
                        if (string.IsNullOrEmpty(code)) AddLog("[eval] Usage: eval <C# code>");
                        else _repl.Evaluate(code);
                        break;

                    case "runfile":
                        if (_repl == null)
                        {
                            AddLog("[REPL] REPL is not active.");
                            AddLog("[REPL] Enable 'repl_enabled' in Config panel and restart.");
                            break;
                        }
                        string fileName = string.Join(" ", _cmdParts.Skip(1));
                        string filePath = Path.IsPathRooted(fileName)
                            ? fileName
                            : Path.Combine(DumpDir, fileName);
                        if (!File.Exists(filePath)) { AddLog($"[runfile] File not found: {filePath}"); break; }
                        AddLog($"[runfile] Running: {Path.GetFileName(filePath)}");
                        _repl.Evaluate(File.ReadAllText(filePath));
                        break;

                    case "resetscene":
                        MelonLoader.MelonCoroutines.Start(SaveAndResetRoutine());
                        break;

                    case "charspeed":
                        if (_cmdParts.Length > 1 && float.TryParse(_cmdParts[1], out float spd))
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

                    case "addmoney":
                        int moneyAdd = 10000;
                        if (_cmdParts.Length > 1) int.TryParse(_cmdParts[1], out moneyAdd);
                        var sgdmAdd = Il2CppCMS.Shared.SharedGameDataManager.Instance;
                        if (sgdmAdd == null) { AddLog("[addmoney] SharedGameDataManager not in scene."); break; }
                        sgdmAdd.AddMoneyRpc(moneyAdd);
                        AddLog($"Added ${moneyAdd}. Balance: ${sgdmAdd.money}");
                        break;

                    case "setmoney":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int moneySet))
                        {
                            var sgdm = Il2CppCMS.Shared.SharedGameDataManager.Instance;
                            sgdm.AddMoneyRpc(moneySet - (int)sgdm.money);
                            AddLog($"Money set to ${moneySet}. Balance: ${sgdm.money}");
                        }
                        else AddLog("Usage: setmoney <amount>");
                        break;

                    case "addexp":
                        int exp = 1000;
                        if (_cmdParts.Length > 1) int.TryParse(_cmdParts[1], out exp);
                        Il2CppCMS.Player.PlayerData.AddPlayerExp(exp, true);
                        AddLog($"Added {exp} EXP. Level: {Il2CppCMS.Player.PlayerData.PlayerLevel}");
                        break;

                    case "removedemowalls":
                        RemoveDemoWalls();
                        break;

                    case "stealcustomercar":
                        if (_cmdParts.Length < 2 || !int.TryParse(_cmdParts[1], out int stealIdx))
                        { AddLog("Usage: stealcustomercar <index>  (use showgaragecars)"); break; }
                        CmdStealCustomerCar(stealIdx);
                        break;

                    case "find":
                        if (_cmdParts.Length > 1)
                        {
                            string frag = _cmdParts[1].ToLower();
                            int n = 0;
                            foreach (var go in GameObject.FindObjectsOfType<GameObject>(true))
                                if (go.name.ToLower().Contains(frag))
                                { AddLog($"  {go.name} | active={go.activeSelf}"); n++; }
                            AddLog($"Found: {n}");
                        }
                        else AddLog("Usage: find <name>");
                        break;

                    case "inspectobj":
                        InspectAtCursor();
                        break;

                    case "dumpobj":
                        DumpCarHierarchyToClipboard();
                        break;

                    case "scenes":
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                        {
                            var sc = SceneManager.GetSceneAt(i);
                            AddLog($"  [{i}] \"{sc.name}\" idx={sc.buildIndex} loaded={sc.isLoaded}");
                        }
                        break;

                    case "help":
                        AddLog("Commands:");
                        AddLog("");
                        AddLog("  helpadv                 – show advaced commads");
                        AddLog("  exit                    – quit the game");
                        AddLog("  save                    – save current game state");
                        AddLog("  resetscene              – reload garage scene");
                        AddLog("  gamelocation            – open game install folder");
                        AddLog("  savelocation            – open save files folder");
                        AddLog("  modslocation            – open Mods folder");
                        AddLog("  carslocation            – open Cars (StreamingAssets) folder");
                        AddLog("  carspawner.cs           – opens car spawner panel - need REPL + UITK Framework");
                        AddLog("  perfmonitor.cs          – opens performance monitor REPL + UITK Framework");
                        AddLog("  charspeed <n>           – player walk speed");
                        AddLog("  addmoney [n]            – add money (default 10000)");
                        AddLog("  setmoney <n>            – set exact money amount");
                        AddLog("  addexp [n]              – add EXP (default 1000)");
                        AddLog("  stealcustomercar <idx>  – take ownership of customer car");
                        AddLog("  fixcar <idx>            – this is old use carspawner.cs instead");
                        AddLog("  removedemowalls         – disable demo map walls");
                        AddLog("  showgaragecars          – list cars in garage");
                        AddLog("  showparkingcars         – list cars on parking lot");
                        AddLog("  allskills               – unlock all player skills");
                        AddLog("  timescale <n>           – set game speed (0.1–10.0)");
                        AddLog("  settime <HHMM>          – set in-game time (e.g. settime 1320)");
                        AddLog("  savetp <name>           – save current position as teleport");
                        AddLog("  tp <name>               – teleport to saved position");
                        AddLog("  listtp                  – list all saved teleports");
                        AddLog("  removetp <name>         – remove saved teleport");
                        AddLog("  addtime <n>             – skip forward/back N hours (e.g. addtime 2  or  addtime -1)");

                        var externalCmds = ConsoleAPI.GetAll().ToList();
                        if (externalCmds.Count > 0)
                        {
                            AddLog("");
                            AddLog("Mod commands:");
                            foreach (var (name, desc) in externalCmds)
                                AddLog($"  {name,-24}– {desc}");
                        }
                        break;

                    case "helpadv":
                        AddLog("Advanced Commands:");
                        AddLog("");
                        AddLog("  run <C# code>           – compile and run C# code");
                        AddLog("  runfile <file.cs>       – run script from mod folder");
                        AddLog("  find <name>             – search GameObjects by name");
                        AddLog("  inspectobj              – inspect object under crosshair");
                        AddLog("  dumpobj                 – copy object hierarchy to clipboard");
                        AddLog("  scenes                  – list loaded scenes");
                        break;

                    case "showgaragecars": CmdShowGarageCars(); break;
                    case "showparkingcars": CmdShowParkingCars(); break;

                    case "storecarinparking":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int storeIdx))
                            CmdStoreCarInParking(storeIdx);
                        else AddLog("Usage: storecarinparking <index>");
                        break;

                    case "fixcar":
                        if (_cmdParts.Length < 2 || !int.TryParse(_cmdParts[1], out int fixIdx))
                        { AddLog("Usage: fixcar <index>  (use showgaragecars)"); break; }
                        CmdFixCar(fixIdx);
                        break;

                    case "unstorecarfromparking":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int unstoreIdx))
                            CmdUnstoreCarFromParking(unstoreIdx);
                        else AddLog("Usage: unstorecarfromparking <index>");
                        break;

                    case "exit":
                        AddLog("Exiting game...");
                        Application.Quit();
                        break;

                    case "save":
                        PerformSave();
                        break;

                    case "gamelocation":
                        OpenExplorerFolder(
                            Path.GetDirectoryName(Path.GetDirectoryName(ConsolePlugin.ModDir)),
                            "Game");
                        break;

                    case "savelocation":
                        OpenExplorerFolder(
                            Application.persistentDataPath.Replace('/', '\\'),
                            "Saves");
                        break;

                    case "modslocation":
                        OpenExplorerFolder(
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods"), "Mods");
                        break;

                    case "carslocation":
                        OpenExplorerFolder(
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                "Car Mechanic Simulator 2026 Demo_Data",
                                "StreamingAssets", "Cars"), "Cars");
                        break;


                    case "allskills":
                        CmdAllSkills();
                        break;

                    case "timescale":
                        if (_cmdParts.Length > 1 &&
                            float.TryParse(_cmdParts[1],
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out float tScale))
                        {
                            tScale = Mathf.Clamp(tScale, 0.1f, 10f);
                            Time.timeScale = tScale;
                            AddLog($"[timescale] <color=#7ec8a0>{tScale:F2}x</color>");
                        }
                        else AddLog("Usage: timescale <0.1–10.0>  (e.g. timescale 2  or  timescale 0.5)");
                        break;

                    case "settime":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int rawT))
                        {
                            int th = rawT / 100, tm = rawT % 100;
                            if (th >= 0 && th <= 23 && tm >= 0 && tm <= 59)
                                CmdSetTime(th, tm);
                            else
                                AddLog("[settime] Invalid value. Use HHMM, e.g. settime 1320 = 13:20");
                        }
                        else AddLog("Usage: settime <HHMM>  (e.g. settime 0800  or  settime 2230)");
                        break;


                    case "savetp":
                        if (_cmdParts.Length > 1)
                            CmdSaveTp(string.Join("_", _cmdParts.Skip(1)).ToLowerInvariant());
                        else
                            AddLog("Usage: savetp <name>  (e.g. savetp garage4)");
                        break;

                    case "tp":
                        if (_cmdParts.Length > 1)
                            MelonLoader.MelonCoroutines.Start(
                                CmdTpRoutine(string.Join("_", _cmdParts.Skip(1)).ToLowerInvariant()));
                        else
                        {
                            if (_teleports.Count == 0)
                                AddLog("[TP] No teleports saved. Use: savetp <name>");
                            else
                                AddLog("[TP] Saved: " + string.Join(", ", _teleports.Keys));
                        }
                        break;

                    case "listtp":
                        CmdListTp();
                        break;

                    case "removetp":
                        if (_cmdParts.Length > 1)
                        {
                            string rKey = _cmdParts[1].ToLowerInvariant();
                            if (_teleports.Remove(rKey)) { SaveTeleports(); AddLog($"[TP] Removed: {rKey}"); }
                            else AddLog($"[TP] Not found: {rKey}");
                        }
                        else AddLog("Usage: removetp <name>");
                        break;

                    case "addtime":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int addH))
                            CmdAddTime(addH);
                        else AddLog("Usage: addtime <hours>  (e.g. addtime 2  or  addtime -1)");
                        break;


                    default:
                        // ── .cs shortcut — wpisanie nazwy pliku uruchamia runfile ──────
                        if (cmd.EndsWith(".cs"))
                        {
                            if (_repl == null)
                            {
                                AddLog("[REPL] REPL is not active.");
                                AddLog("[REPL] Enable 'repl_enabled' in Config panel and restart.");
                                break;
                            }
                            string shortFilePath = Path.IsPathRooted(raw)
                                ? raw
                                : Path.Combine(DumpDir, raw);
                            if (!File.Exists(shortFilePath))
                            {
                                AddLog($"[runfile] File not found: {shortFilePath}");
                                break;
                            }
                            AddLog($"[runfile] Running: {Path.GetFileName(shortFilePath)}");
                            _repl.Evaluate(File.ReadAllText(shortFilePath));
                            break;
                        }

                        // ── Check external commands registered via ConsoleAPI ──────────
                        if (ConsoleAPI.TryExecute(cmd, _cmdParts, out string apiError))
                        {
                            if (apiError != null)
                                AddLog($"[ERR] {cmd}: {apiError}");
                        }
                        else
                        {
                            AddLog("[?] Unknown command: " + cmd + "  (type 'help')");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog("[ERR] " + ex.Message);
                ConsolePlugin.Log.Error(ex.ToString());
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private void CopyToClipboard()
        {
            if (_logLines.Count == 0) { AddLog("No logs."); return; }
            var sb = new StringBuilder();
            foreach (var l in _logLines) sb.AppendLine(l);
            GUIUtility.systemCopyBuffer = sb.ToString();
            AddLog("Log copied to clipboard.");
        }

        private void CmdAddTime(int hours)
        {
            try
            {
                var tmType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.FullName == "Il2CppCMS.Core.TimeManagement.TimeManager");

                if (tmType == null) { AddLog("[addtime] TimeManager not found."); return; }

                var il2T = Il2CppInterop.Runtime.Il2CppType.From(tmType);
                var objs = UnityEngine.Object.FindObjectsOfType(il2T, true);
                if (objs.Length == 0) { AddLog("[addtime] Not in scene."); return; }

                var inst = Activator.CreateInstance(tmType, new object[] { objs[0].Pointer });

                var method = tmType.GetMethod("SkipTime",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(int) },
                    null);

                if (method == null) { AddLog("[addtime] SkipTime(int) not found."); return; }

                method.Invoke(inst, new object[] { hours });
                AddLog($"[addtime] <color=#7ec8a0>{(hours >= 0 ? "+" : "")}{hours}h</color> ✓");
            }
            catch (Exception ex) { AddLog("[addtime] ERR: " + ex.Message); }
        }
        private static void TryResolveFrameworkCursor()
        {
            if (_fwCursorResolved) return;
            _fwCursorResolved = true;
            try
            {
                var t = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(x => x.FullName == "CMS2026UITKFramework.CursorManager");
                if (t == null) return;
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                _fwRequest = t.GetMethod("Request", flags);
                _fwRelease = t.GetMethod("Release", flags);
            }
            catch { }
        }

        private static bool HasFrameworkCursor => _fwRequest != null && _fwRelease != null;

        private void CursorRequest()
        {
            if (HasFrameworkCursor) { try { _fwRequest.Invoke(null, null); } catch { } }
            else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void CursorRelease()
        {
            if (HasFrameworkCursor) { try { _fwRelease.Invoke(null, null); } catch { } }
            // bez framework: gra sama przywróci kursor przy powrocie do gameMode
        }



        [HideFromIl2Cpp]
        private void DumpCarHierarchyToClipboard()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
            if (UnityEngine.Physics.Raycast(ray, out var hit, 15f))
            {
                Transform root = hit.collider.transform;
                while (root.parent != null && !root.name.ToLower().StartsWith("car_"))
                    root = root.parent;
                var sb = new StringBuilder();
                sb.AppendLine($"=== HIERARCHY DUMP: {root.name} ===");
                sb.AppendLine($"Dump Date: {DateTime.Now}");
                sb.AppendLine("-----------------------------------");
                BuildHierarchyString(root, sb, 0);
                GUIUtility.systemCopyBuffer = sb.ToString();
                AddLog($">>> SUCCESS: Structure '{root.name}' copied to clipboard.");
            }
            else AddLog("[Dump] No object hit. Aim for the car.");
        }

        [HideFromIl2Cpp]
        private void BuildHierarchyString(Transform t, StringBuilder sb, int indent)
        {
            string space = new string(' ', indent * 2);
            string hidden = t.gameObject.activeSelf ? "" : " [HIDDEN]";
            sb.AppendLine($"{space}- {t.name}{hidden} (L:{t.gameObject.layer})");
            for (int i = 0; i < t.childCount; i++)
                BuildHierarchyString(t.GetChild(i), sb, indent + 1);
        }

        [HideFromIl2Cpp]
        private void InspectAtCursor()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) { AddLog("[Inspect] Main camera not found."); return; }
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
            if (UnityEngine.Physics.Raycast(ray, out var hit, 20f))
            {
                GameObject go = hit.collider.gameObject;
                Transform t = go.transform;
                AddLog(" ");
                AddLog(">>> INSPECTION REPORT <<<");
                AddLog($"Object Name: {go.name}");
                AddLog($"Layer: {go.layer}");
                AddLog($"Distance: {hit.distance:F2}m");
                AddLog($"World Position: {t.position.x:F2}, {t.position.y:F2}, {t.position.z:F2}");
                AddLog($"Parent: {(t.parent != null ? t.parent.name : "None (Root)")}");
                if (t.childCount > 0)
                {
                    var names = new List<string>();
                    for (int i = 0; i < t.childCount; i++) names.Add(t.GetChild(i).name);
                    AddLog($"Children ({t.childCount}): {string.Join(", ", names)}");
                }
                else AddLog("Children: None");
                var comps = go.GetComponents<Component>();
                var compList = new List<string>();
                foreach (var c in comps)
                    if (c != null) compList.Add(c.GetIl2CppType().Name);
                AddLog($"Components: {string.Join(", ", compList)}");
                AddLog("-------------------------");
            }
            else AddLog("[Inspect] No object within 20m range.");
        }

        private void RemoveDemoWalls()
        {
            var targets = new[] {
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

        // ── Garage / parking / fix-car — unchanged from original ─────────────────

        [HideFromIl2Cpp]
        private Assembly GameAsm =>
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

        [HideFromIl2Cpp]
        private object GetSaveManagerInstance()
        {
            var smType = GameAsm?.GetType("Il2CppCMS.SaveSystem.SaveManager");
            return smType?.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        [HideFromIl2Cpp]
        private object GetStateFromDict(string key)
        {
            var smInst = GetSaveManagerInstance();
            if (smInst == null) return null;
            var psRaw = smInst.GetType().GetProperty("profileStates").GetValue(smInst);
            var p0 = psRaw.GetType().GetProperty("Item").GetValue(psRaw, new object[] { 0 });
            var dict = p0.GetType().GetProperty("States").GetValue(p0);
            var args = new object[] { key, null };
            dict.GetType().GetMethod("TryGetValue").Invoke(dict, args);
            return args[1];
        }

        [HideFromIl2Cpp]
        private object WrapGarageLoader()
        {
            var glType = GameAsm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
            var il2gl = Il2CppInterop.Runtime.Il2CppType.From(glType);
            var gls = UnityEngine.Object.FindObjectsOfType(il2gl, true);
            if (gls.Length == 0) return null;
            return Activator.CreateInstance(glType, new object[] { gls[0].Pointer });
        }

        // ── allskills ─────────────────────────────────────────────────────────────
        private void CmdAllSkills()
        {
            try
            {
                var tsType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.Name == "TestScript");

                if (tsType == null)
                {
                    AddLog("[allskills] TestScript type not found — load a Garage save first.");
                    return;
                }

                var il2T = Il2CppInterop.Runtime.Il2CppType.From(tsType);
                var objs = UnityEngine.Object.FindObjectsOfType(il2T, true);
                if (objs.Length == 0)
                {
                    AddLog("[allskills] TestScript not found in scene — load a Garage save first.");
                    return;
                }

                var ts = Activator.CreateInstance(tsType, new object[] { objs[0].Pointer });
                tsType.GetMethod("UnlockAllSkills")?.Invoke(ts, null);
                AddLog("[allskills] All skills unlocked!");
            }
            catch (Exception ex) { AddLog("[allskills] ERR: " + ex.Message); }
        }

        // ── settime ───────────────────────────────────────────────────────────────
        private void CmdSetTime(int hour, int minute)
        {
            try
            {
                var tmType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.FullName == "Il2CppCMS.Core.TimeManagement.TimeManager");

                if (tmType == null) { AddLog("[settime] TimeManager type not found."); return; }

                var il2T = Il2CppInterop.Runtime.Il2CppType.From(tmType);
                var objs = UnityEngine.Object.FindObjectsOfType(il2T, true);
                if (objs.Length == 0) { AddLog("[settime] TimeManager not in scene — load Garage first."); return; }

                var inst = Activator.CreateInstance(tmType, new object[] { objs[0].Pointer });

                // Wybierz przeciążenie SetTime(Byte hour, Byte minute, Boolean sendEvents)
                var method = tmType.GetMethod("SetTime",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(byte), typeof(byte), typeof(bool) },
                    null);

                if (method == null) { AddLog("[settime] SetTime(byte,byte,bool) not found."); return; }

                method.Invoke(inst, new object[] { (byte)hour, (byte)minute, true });

                AddLog($"[settime] <color=#7ec8a0>{hour:D2}:{minute:D2}</color> ✓");
            }
            catch (Exception ex) { AddLog("[settime] ERR: " + ex.Message); }
        }
        [HideFromIl2Cpp]
        private bool TryInvokeSetTime(object obj, Type type,
            int hour, int minute, float hourFloat)
        {
            string[] names =
            {
        "SetTime", "SetCurrentTime", "SetTimeOfDay",
        "SetHour", "ForceSetTime", "SetGameTime"
    };
            foreach (string n in names)
            {
                var m = type.GetMethod(n,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) continue;
                var p = m.GetParameters();
                try
                {
                    if (p.Length == 1) { m.Invoke(obj, new object[] { hourFloat }); }
                    else if (p.Length == 2) { m.Invoke(obj, new object[] { hour, minute }); }
                    else continue;

                    AddLog($"[settime] <color=#7ec8a0>{hour:D2}:{minute:D2}</color>" +
                           $" via {type.Name}.{n}");
                    return true;
                }
                catch { }
            }
            return false;
        }

        

        // ── Teleporty ─────────────────────────────────────────────────────────────
        private void LoadTeleports()
        {
            try
            {
                if (!File.Exists(TpFilePath)) return;
                int n = 0;
                foreach (string line in File.ReadAllLines(TpFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string name = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string[] xyz = line.Substring(eq + 1).Trim().Split(',');
                    if (xyz.Length == 3
                        && float.TryParse(xyz[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(xyz[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y)
                        && float.TryParse(xyz[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float z))
                    {
                        _teleports[name] = new Vector3(x, y, z);
                        n++;
                    }
                }
                if (n > 0) AddLog($"[TP] Loaded {n} teleport(s) from tps.txt");
            }
            catch (Exception ex) { AddLog("[TP] Load error: " + ex.Message); }
        }

        private void SaveTeleports()
        {
            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var lines = new List<string>
        {
            "# CMS2026 Simple Console — saved teleports",
            "# Format: name = x,y,z",
            ""
        };
                foreach (var kv in _teleports)
                    lines.Add($"{kv.Key} = " +
                              $"{kv.Value.x.ToString("F3", ci)}," +
                              $"{kv.Value.y.ToString("F3", ci)}," +
                              $"{kv.Value.z.ToString("F3", ci)}");
                File.WriteAllLines(TpFilePath, lines);
            }
            catch (Exception ex) { AddLog("[TP] Save error: " + ex.Message); }
        }

        private void CmdSaveTp(string name)
        {
            try
            {
                var pcType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.FullName == "Il2CppCMS.Player.Controller.PlayerController");

                if (pcType == null) { AddLog("[savetp] PlayerController type not found."); return; }

                var il2T = Il2CppInterop.Runtime.Il2CppType.From(pcType);
                var objs = UnityEngine.Object.FindObjectsOfType(il2T, true);
                if (objs.Length == 0) { AddLog("[savetp] PlayerController not in scene."); return; }

                var inst = Activator.CreateInstance(pcType, new object[] { objs[0].Pointer });
                var comp = (UnityEngine.Component)(object)inst;
                Vector3 pos = comp.transform.position;

                _teleports[name] = pos;
                SaveTeleports();
                AddLog($"[savetp] '<color=#c8d870>{name}</color>' → ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) ✓");
            }
            catch (Exception ex) { AddLog("[savetp] ERR: " + ex.Message); }
        }
        [HideFromIl2Cpp]
        private System.Collections.IEnumerator CmdTpRoutine(string name)
        {
            if (!_teleports.TryGetValue(name, out Vector3 pos))
            {
                AddLog($"[tp] '{name}' not found.  " +
                       $"Available: {(_teleports.Count > 0 ? string.Join(", ", _teleports.Keys) : "(none)")}");
                yield break;
            }

            var pcType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
                .FirstOrDefault(t => t.FullName == "Il2CppCMS.Player.Controller.PlayerController");

            if (pcType == null) { AddLog("[tp] PlayerController type not found."); yield break; }

            var il2Pc = Il2CppInterop.Runtime.Il2CppType.From(pcType);
            var pcObjs = UnityEngine.Object.FindObjectsOfType(il2Pc, true);
            if (pcObjs.Length == 0) { AddLog("[tp] PlayerController not in scene."); yield break; }

            var pcInst = Activator.CreateInstance(pcType, new object[] { pcObjs[0].Pointer });
            var pcComp = (UnityEngine.Component)(object)pcInst;

            var pmType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
                .FirstOrDefault(t => t.FullName == "Il2CppCMS.Player.Controller.PlayerMovement");

            if (pmType == null) { AddLog("[tp] PlayerMovement type not found."); yield break; }

            var il2Pm = Il2CppInterop.Runtime.Il2CppType.From(pmType);
            var pmObjs = UnityEngine.Object.FindObjectsOfType(il2Pm, true);
            if (pmObjs.Length == 0) { AddLog("[tp] PlayerMovement not in scene."); yield break; }

            var pmInst = Activator.CreateInstance(pmType, new object[] { pmObjs[0].Pointer });

            var queueTp = pmType.GetMethod("QueueTeleport",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                new System.Type[] { typeof(Vector3), typeof(Quaternion) },
                null);

            if (queueTp == null) { AddLog("[tp] QueueTeleport not found."); yield break; }

            // ── Zamknij konsolę i odblokuj input ────────────────────────────────
            _renderer.SetVisible(false);
            SetGameInputEnabled(true);

            yield return null;
            yield return null;
            yield return new UnityEngine.WaitForFixedUpdate();

            queueTp.Invoke(pmInst, new object[] { pos, pcComp.transform.rotation });

            for (int i = 0; i < 8; i++)
                yield return new UnityEngine.WaitForFixedUpdate();

            float diff = UnityEngine.Vector3.Distance(pos, pcComp.transform.position);
            AddLog($"[tp] → '<color=#c8d870>{name}</color>' ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})" +
                   $"  diff={diff:F2}  {(diff < 1f ? "<color=#7ec8a0>✓</color>" : "<color=#e05555>fail</color>")}");
        }

        private void CmdListTp()
        {
            if (_teleports.Count == 0)
            { AddLog("[TP] No teleports saved. Use: savetp <name>"); return; }

            AddLog($"[TP] Saved teleports ({_teleports.Count}):");
            foreach (var kv in _teleports)
                AddLog($"  <color=#c8d870>{kv.Key,-20}</color> " +
                       $"({kv.Value.x:F2}, {kv.Value.y:F2}, {kv.Value.z:F2})");
            AddLog($"  File: {TpFilePath}");
        }


        private void PerformSave()
        {
            try
            {
                // Krok 1: Stan garażu
                var gl = UnityEngine.Object.FindObjectOfType<Il2CppCMS.SceneLoaders.GarageLoader>();
                if (gl != null)
                {
                    gl.SaveState();
                    AddLog("[Save] Garage state updated.");
                }

                // Krok 2: Profil gracza
                var smInst = GetSaveManagerInstance();
                if (smInst != null)
                {
                    smInst.GetType().GetMethod("SaveCurrentProfile")?.Invoke(smInst, null);
                    AddLog("[Save] Game profile saved successfully.");
                }
                else
                {
                    AddLog("[Save] SaveManager not found.");
                }
            }
            catch (Exception ex)
            {
                AddLog("[Save] ERR: " + ex.Message);
            }
        }
        [HideFromIl2Cpp]
        private System.Collections.IEnumerator SaveAndResetRoutine()
        {
            AddLog("[Reset] Initiating auto-save before reload...");
            PerformSave();

            // Krótkie oczekiwanie (np. 0.5 sekundy), aby operacje I/O mogły się zakończyć
            yield return new WaitForSeconds(0.5f);

            AddLog("Reloading garage scene...");
            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadScene("Garage");
            else
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private string GetAutocompleteMatch(string prefix)
        {
            prefix = prefix.ToLowerInvariant();

            // Szukaj najpierw w builtinach
            foreach (string cmd in _builtinCommands)
                if (cmd.StartsWith(prefix) && cmd != prefix)
                    return cmd;

            // Potem w komendach z modów
            foreach (var (name, _) in ConsoleAPI.GetAll())
                if (name.StartsWith(prefix) && name != prefix)
                    return name;

            return null;
        }

        private void CmdShowGarageCars()
        {
            try
            {
                var asm = GameAsm;
                var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
                var gcsType = asm.GetType("Il2CppCMS.SaveSystem.Containers.GarageCarsState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");
                var glW = WrapGarageLoader();
                if (glW == null) { AddLog("[GarageCars] GarageLoader not found"); return; }
                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glW, pars);
                var carsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                var getItem = carsRaw.GetType().GetMethod("get_Item");
                AddLog($"[GarageCars] Slots: {count}");
                int found = 0;
                for (int i = 0; i < count; i++)
                {
                    var c = getItem.Invoke(carsRaw, new object[] { i });
                    if (c == null) continue;
                    var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                   (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                    var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                    var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                    if (string.IsNullOrEmpty(carId)) continue;
                    var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                    var isCustomer = (bool)csdType.GetProperty("CustomerCar").GetValue(csd);
                    AddLog($"  [{i}] {carId}  UID={uid}  Customer={isCustomer}");
                    found++;
                }
                AddLog($"[GarageCars] Total: {found}");
            }
            catch (Exception ex) { AddLog("[GarageCars] ERR: " + ex.Message); }
        }

        private void CmdStealCustomerCar(int garageIndex)
        {
            try
            {
                var asm = GameAsm;
                var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
                var gcsType = asm.GetType("Il2CppCMS.SaveSystem.Containers.GarageCarsState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");
                var glW = WrapGarageLoader();
                if (glW == null) { AddLog("[stealcustomercar] GarageLoader not found"); return; }
                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glW, pars);
                var carsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                var getItem = carsRaw.GetType().GetMethod("get_Item");
                var filled = new List<(int slot, string carId, string uid, bool isCustomer)>();
                for (int i = 0; i < count; i++)
                {
                    var c = getItem.Invoke(carsRaw, new object[] { i });
                    if (c == null) continue;
                    var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                   (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                    var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                    var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                    if (string.IsNullOrEmpty(carId)) continue;
                    var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                    var isCust = (bool)csdType.GetProperty("CustomerCar").GetValue(csd);
                    filled.Add((i, carId, uid, isCust));
                }
                if (garageIndex < 0 || garageIndex >= filled.Count)
                { AddLog($"[stealcustomercar] Index {garageIndex} out of range"); return; }
                var (slot, targetCarId, targetUid, targetIsCustomer) = filled[garageIndex];
                if (!targetIsCustomer)
                { AddLog($"[stealcustomercar] [{garageIndex}] {targetCarId} is already yours"); return; }
                var loaders = UnityEngine.Object.FindObjectsOfType<Il2CppCMS.Core.Car.CarLoader>(true);
                Il2CppCMS.Core.Car.CarLoader target = null;
                foreach (var l in loaders)
                    if (!string.IsNullOrEmpty(l.CarID) && l.CarID == targetCarId && l.CustomerCar)
                    { target = l; break; }
                if (target == null) { AddLog($"[stealcustomercar] CarLoader not found"); return; }
                int jobId = target.orderConnection;
                target.SetCustomerCar(false, 0);
                AddLog($"[stealcustomercar] SetCustomerCar OK → Customer={target.CustomerCar}");
                var og = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Core.OrderGenerator>();
                if (og != null && jobId > 0)
                    og.CancelJob(jobId, false);
                var gl = UnityEngine.Object.FindObjectOfType<Il2CppCMS.SceneLoaders.GarageLoader>();
                if (gl != null) { gl.SaveState(); AddLog("[stealcustomercar] SaveState OK"); }
                AddLog($"[stealcustomercar] Done! {targetCarId} is now yours. Use resetscene.");
            }
            catch (Exception ex) { AddLog("[stealcustomercar] ERR: " + ex.Message); }
        }

        private void CmdShowParkingCars()
        {
            try
            {
                var asm = GameAsm;
                var psType = asm.GetType("Il2CppCMS.SaveSystem.Containers.ParkingState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");
                var raw = GetStateFromDict("ParkingState.dat");
                if (raw == null) { AddLog("[Parking] ParkingState not found"); return; }
                var psPtr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                  (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)raw);
                var ps = Activator.CreateInstance(psType, new object[] { psPtr });
                var carsRaw = psType.GetProperty("Cars").GetValue(ps);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                var getItem = carsRaw.GetType().GetMethod("get_Item");
                int filled = 0;
                for (int i = 0; i < count; i++)
                {
                    var c = getItem.Invoke(carsRaw, new object[] { i });
                    if (c == null) continue;
                    var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                   (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                    var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                    var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                    if (string.IsNullOrEmpty(carId)) continue;
                    var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                    AddLog($"  [slot {i}] {carId}  UID={uid}");
                    filled++;
                }
                AddLog($"[Parking] Cars on parking: {filled}");
            }
            catch (Exception ex) { AddLog("[Parking] ERR: " + ex.Message); }
        }

        private void HardResetAc()
        {
            _acMatches.Clear();
            _acIndex = -1;
            _acPrefix = null;
        }

        private void CmdFixCar(int garageIndex)
        {
            try
            {
                var asm = GameAsm;
                var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
                var gcsType = asm.GetType("Il2CppCMS.SaveSystem.Containers.GarageCarsState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");
                var glW = WrapGarageLoader();
                if (glW == null) { AddLog("[fixcar] GarageLoader not found"); return; }
                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glW, pars);
                var carsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                var getItem = carsRaw.GetType().GetMethod("get_Item");
                var filled = new List<(string carId, string uid, bool isCustomer)>();
                for (int i = 0; i < count; i++)
                {
                    var c = getItem.Invoke(carsRaw, new object[] { i });
                    if (c == null) continue;
                    var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                   (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                    var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                    var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                    if (string.IsNullOrEmpty(carId)) continue;
                    var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                    var isCust = (bool)csdType.GetProperty("CustomerCar").GetValue(csd);
                    filled.Add((carId, uid, isCust));
                }
                if (garageIndex < 0 || garageIndex >= filled.Count)
                { AddLog($"[fixcar] Index {garageIndex} out of range (0–{filled.Count - 1})"); return; }
                var (targetCarId, targetUid, targetIsCustomer) = filled[garageIndex];
                AddLog($"[fixcar] Repairing [{garageIndex}] {targetCarId}...");
                var loaders = UnityEngine.Object.FindObjectsOfType<Il2CppCMS.Core.Car.CarLoader>(true);
                Il2CppCMS.Core.Car.CarLoader target = null;
                foreach (var l in loaders)
                    if (!string.IsNullOrEmpty(l.CarID) && l.CarID == targetCarId && l.CustomerCar == targetIsCustomer)
                    { target = l; break; }
                if (target == null) { AddLog($"[fixcar] CarLoader not found"); return; }
                target.Dev_RepairAllBody();
                typeof(Il2CppCMS.Core.Car.CarLoader).GetMethod("SetConditionAll")
                    .Invoke(target, new object[] { 1f });
                target.ClearEnginePartsConditionCache();
                var gl = UnityEngine.Object.FindObjectOfType<Il2CppCMS.SceneLoaders.GarageLoader>();
                if (gl != null) gl.SaveState();
                AddLog($"[fixcar] Done! {targetCarId} repaired to 100%.");
            }
            catch (Exception ex) { AddLog("[fixcar] ERR: " + ex.Message); }
        }

        private void CmdStoreCarInParking(int garageIndex)
        {
            try
            {
                var asm = GameAsm;
                var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
                var gcsType = asm.GetType("Il2CppCMS.SaveSystem.Containers.GarageCarsState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");
                var psType = asm.GetType("Il2CppCMS.SaveSystem.Containers.ParkingState");
                var clType = asm.GetType("Il2CppCMS.Core.Car.CarLoader");
                var glW = WrapGarageLoader();
                if (glW == null) { AddLog("[Store] GarageLoader not found"); return; }
                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glW, pars);
                var gcsCarsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int gcsCount = (int)gcsCarsRaw.GetType().GetProperty("Length").GetValue(gcsCarsRaw);
                if (garageIndex < 0 || garageIndex >= gcsCount)
                { AddLog($"[Store] Index {garageIndex} out of range"); return; }
                var targetRaw = gcsCarsRaw.GetType().GetMethod("get_Item").Invoke(gcsCarsRaw, new object[] { garageIndex });
                if (targetRaw == null) { AddLog($"[Store] Slot {garageIndex} is empty"); return; }
                var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)targetRaw);
                var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                if (string.IsNullOrEmpty(carId)) { AddLog($"[Store] Slot has no CarID"); return; }
                var raw = GetStateFromDict("ParkingState.dat");
                if (raw == null) { AddLog("[Store] ParkingState not found"); return; }
                var psPtr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                   (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)raw);
                var ps = Activator.CreateInstance(psType, new object[] { psPtr });
                var pCarsRaw = psType.GetProperty("Cars").GetValue(ps);
                int pCount = (int)pCarsRaw.GetType().GetProperty("Length").GetValue(pCarsRaw);
                var pGetItem = pCarsRaw.GetType().GetMethod("get_Item");
                var pSetItem = pCarsRaw.GetType().GetMethod("set_Item");
                int freeSlot = -1;
                for (int i = 0; i < pCount; i++)
                {
                    var c = pGetItem.Invoke(pCarsRaw, new object[] { i });
                    if (c == null) { freeSlot = i; break; }
                    var cp = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                  (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                    var cs = Activator.CreateInstance(csdType, new object[] { cp });
                    if (string.IsNullOrEmpty(csdType.GetProperty("CarID").GetValue(cs)?.ToString()))
                    { freeSlot = i; break; }
                }
                if (freeSlot == -1) { AddLog("[Store] Parking is full!"); return; }
                pSetItem.Invoke(pCarsRaw, new object[] { freeSlot, targetRaw });
                psType.GetProperty("IsDirty")?.SetValue(ps, true);
                AddLog($"[Store] {carId} (UID={uid}) → parking slot {freeSlot}");
                var il2cl = Il2CppInterop.Runtime.Il2CppType.From(clType);
                var cls = UnityEngine.Object.FindObjectsOfType(il2cl, true);
                bool loaderReset = false;
                foreach (var rawLoader in cls)
                {
                    var cl = Activator.CreateInstance(clType, new object[] { rawLoader.Pointer });
                    var lCarId = clType.GetProperty("CarID").GetValue(cl)?.ToString();
                    var lUid = clType.GetProperty("UID")?.GetValue(cl)?.ToString();
                    bool match = (!string.IsNullOrEmpty(lUid) && lUid == uid)
                              || (string.IsNullOrEmpty(lUid) && lCarId == carId);
                    if (!match) continue;
                    clType.GetMethod("ResetCarID").Invoke(cl, null);
                    loaderReset = true;
                    break;
                }
                if (!loaderReset) AddLog("[Store] WARNING: CarLoader not found in scene");
                glType.GetMethod("SaveState").Invoke(glW, null);
                AddLog("[Store] Done. Use 'resetscene' to reload.");
            }
            catch (Exception ex) { AddLog("[Store] ERR: " + ex.Message); }
        }

        private void CmdUnstoreCarFromParking(int parkingIndex)
        {
            try
            {
                var asm = GameAsm;
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");
                var psType = asm.GetType("Il2CppCMS.SaveSystem.Containers.ParkingState");
                var raw = GetStateFromDict("ParkingState.dat");
                if (raw == null) { AddLog("[Unstore] ParkingState not found"); return; }
                var psPtr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                  (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)raw);
                var ps = Activator.CreateInstance(psType, new object[] { psPtr });
                var carsRaw = psType.GetProperty("Cars").GetValue(ps);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                if (parkingIndex < 0 || parkingIndex >= count)
                { AddLog($"[Unstore] Index {parkingIndex} out of range"); return; }
                var c = carsRaw.GetType().GetMethod("get_Item").Invoke(carsRaw, new object[] { parkingIndex });
                if (c == null) { AddLog($"[Unstore] Slot {parkingIndex} already empty"); return; }
                var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                carsRaw.GetType().GetMethod("set_Item").Invoke(carsRaw, new object[] { parkingIndex, null });
                AddLog($"[Unstore] {carId} (UID={uid}) removed from slot {parkingIndex}");
                GetSaveManagerInstance()?.GetType().GetMethod("SaveCurrentProfile")
                    ?.Invoke(GetSaveManagerInstance(), null);
                AddLog("[Unstore] Save written.");
            }
            catch (Exception ex) { AddLog("[Unstore] ERR: " + ex.Message); }
        }

        private void SetGameInputEnabled(bool enabled)
        {
            // ── PlayerInput + InputManager ───────────────────────────────────────
            try
            {
                var pi = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerInput>();
                if (pi != null)
                {
                    var im = pi.GetType().GetProperty("inputManager").GetValue(pi);
                    im?.GetType().GetMethod("EnableGameplay").Invoke(im, new object[] { enabled });
                }
            }
            catch (Exception ex) { AddLog("[InputBlock] InputManager: " + ex.Message); }

            if (_playerInput == null)
                _playerInput = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerInput>();
            if (_playerInput != null)
                _playerInput.enabled = enabled;

            // ── UI Common — tylko gdy gracz jest w scenie garage ─────────────────
            // W menu gra sama zarządza UI Common — nie ingerujemy
            bool playerInScene = _playerInput != null ||
                                 UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerInput>() != null;

            if (!playerInScene) return;

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
                        if (name != "UI Common") continue;
                        m.GetType().GetMethod(enabled ? "Enable" : "Disable").Invoke(m, null);
                        break;
                    }
                }
            }
            catch (Exception ex) { AddLog("[InputBlock] UICommon: " + ex.Message); }
        }
    }
}