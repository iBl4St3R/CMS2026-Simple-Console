using CMS2026SimpleConsole;
using Il2Cpp;
using Il2CppCMS.Player.Controller;
using Il2CppCMS.Scenes.Loader;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        private string DumpDir => Path.Combine(ConsolePlugin.ModDir, "CMS2026SimpleConsole");


        private ConfigManager _config;
        private CursorLockMode _savedLockState;
        private bool _savedCursorVisible;
        private bool _cursorOverrideActive = false;


        [HideFromIl2Cpp]
        public IConsoleRenderer Renderer => _renderer;
        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void Awake()
        {
            Directory.CreateDirectory(DumpDir);
            ConsolePlugin.ConsoleComponent = this;  // ← rejestracja

            InitRenderer();
            //_config = new ConfigManager(ConsolePlugin.ModDir, AddLog);
            //AddLog($"[Config] autolock = {_config.GetBool("autolock")}");

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

        private void LateUpdate()
        {
            if (_renderer.IsVisible)
            {
                // Konsola widoczna — blokuj grę, pokaż kursor
                if (!_inputLocked)
                {
                    _inputLocked = true;
                    SetGameInputEnabled(false);
                }
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
            }
            else
            {
                // Konsola schowana — odblokuj grę, ukryj kursor
                if (_inputLocked)
                {
                    _inputLocked = false;
                    SetGameInputEnabled(true);
                }
            }
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
                    AddLog("[Renderer] Switched to UIToolkit");
                }
                else
                {
                    AddLog("[Renderer] UIToolkit inaccessible");
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

                    case "run":
                    case "eval":
                        string code = raw.Substring(cmd.Length).Trim();
                        if (_repl == null) { AddLog("[eval] ERROR: REPL not init!"); break; }
                        if (string.IsNullOrEmpty(code)) AddLog("[eval] Usage: eval <C# code>");
                        else _repl.Evaluate(code);
                        break;


                    //case "setconfig":
                    //    if (_cmdParts.Length < 3)
                    //    {
                    //        AddLog("Usage: setconfig <klucz> <wartość>");
                    //        AddLog("Przykład: setconfig autolock true");
                    //        break;
                    //    }
                    //    _config.Set(_cmdParts[1], _cmdParts[2]);
                    //    AddLog($"[Config] {_cmdParts[1]} = {_cmdParts[2]} (zapisano)");
                    //    ApplyConfig(); // zastosuj od razu
                    //    break;

                    //case "config":
                    //    _config.PrintAll(AddLog);
                    //    break;


                    case "resetscene":
                        AddLog("Reloading garage scene...");
                        if (SceneLoader.Instance != null)
                            SceneLoader.Instance.LoadScene("Garage");
                        else
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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


                    case "addmoney":
                        int moneyAdd = 10000;
                        if (_cmdParts.Length > 1) int.TryParse(_cmdParts[1], out moneyAdd);
                        Il2CppCMS.Shared.SharedGameDataManager.Instance.AddMoneyRpc(moneyAdd);
                        AddLog($"Added ${moneyAdd}. Balance: ${Il2CppCMS.Shared.SharedGameDataManager.Instance.money}");
                        break;

                    case "setmoney":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int moneySet))
                        {
                            var sgdm = Il2CppCMS.Shared.SharedGameDataManager.Instance;
                            int diff = moneySet - (int)sgdm.money;
                            sgdm.AddMoneyRpc(diff);
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
                        {
                            AddLog("Usage: stealcustomercar <index>  (use showgaragecars to see indices)");
                            break;
                        }
                        CmdStealCustomerCar(stealIdx);
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

                    case "inspectobj": 
                        InspectAtCursor();
                        break;

                    case "dumpobj":
                        DumpCarHierarchyToClipboard();
                        break;

                    case "scenes":
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                        {
                            var s = SceneManager.GetSceneAt(i);
                            AddLog($"  [{i}] \"{s.name}\" idx={s.buildIndex} loaded={s.isLoaded}");
                        }
                        break;


                    case "help":
                        AddLog("Commands:");
                        AddLog("  run <C# code>       – Compile and run the C# code.");
                        //AddLog("  setconfig <k> <v>   – ustaw opcję i zapisz do pliku");
                        //AddLog("  config               – pokaż wszystkie opcje");
                        //AddLog("  Opcje: autolock=true/false");
                        AddLog("  resetscene           – reload garage scene");
                        AddLog("  charspeed <n>        – player walk speed");
                        AddLog("  addmoney [n]         – add money (default: 10000)");
                        AddLog("  setmoney <n>         – set money to exact amount");
                        AddLog("  addexp [n]           – add EXP (default: 1000)");
                        AddLog("  stealcustomercar <idx>      – take ownership of customer car");
                        AddLog("  fixcar <idx>               – repair all parts of a car to 100%");
                        AddLog("  removedemowalls       – turn off demo walls");
                        AddLog("  find <name>          – search for game objects by name");
                        AddLog("inspectobj                - Displays detailed information about the object under the crosshair.");
                        AddLog("dumpobj                    - Copies the structure of the object under the crosshair directly to the clipboard.");
                        AddLog("  scenes               – scene List");
                        AddLog("  showgaragecars              – list cars in garage");
                        AddLog("  showparkingcars             – list cars on parking");
                        //AddLog("  storecarinparking <idx>     – move garage car[idx] to parking"); //narazie chowamy komende przed publicznym uzyciem bo nie dziala  unstorecarfromparking
                        //AddLog("  unstorecarfromparking <idx> – remove parking car[idx] from parking");
                        break;

                    case "showgaragecars":
                        CmdShowGarageCars();
                        break;

                    case "showparkingcars":
                        CmdShowParkingCars();
                        break;

                    case "storecarinparking":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int storeIdx))
                            CmdStoreCarInParking(storeIdx);
                        else
                            AddLog("Usage: storecarinparking <index>  (use showgaragecars to see indices)");
                        break;

                    case "fixcar":
                        if (_cmdParts.Length < 2 || !int.TryParse(_cmdParts[1], out int fixIdx))
                        {
                            AddLog("Usage: fixcar <index>  (use showgaragecars to see indices)");
                            break;
                        }
                        CmdFixCar(fixIdx);
                        break;

                    case "unstorecarfromparking":
                        if (_cmdParts.Length > 1 && int.TryParse(_cmdParts[1], out int unstoreIdx))
                            CmdUnstoreCarFromParking(unstoreIdx);
                        else
                            AddLog("Usage: unstorecarfromparking <index>  (use showparkingcars to see indices)");
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

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void CopyToClipboard()
        {
            if (_logLines.Count == 0) { AddLog("No logs."); return; }
            var sb = new StringBuilder();
            foreach (var l in _logLines) sb.AppendLine(l);
            GUIUtility.systemCopyBuffer = sb.ToString();
            AddLog("Log copied to clipboard.");
        }


        [HideFromIl2Cpp]
        private void DumpCarHierarchyToClipboard()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            // Raycast na 15 metrów
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
            if (UnityEngine.Physics.Raycast(ray, out var hit, 15f))
            {
                // Próbujemy znaleźć główne drzewo auta
                Transform root = hit.collider.transform;
                // Szukamy w górę aż znajdziemy obiekt z CarLoader lub o nazwie car_
                while (root.parent != null && !root.name.ToLower().StartsWith("car_"))
                {
                    root = root.parent;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"=== HIERARCHY DUMP: {root.name} ===");
                sb.AppendLine($"Dump Date: {DateTime.Now}");
                sb.AppendLine("-----------------------------------");

                // Rekurencyjne budowanie drzewa
                BuildHierarchyString(root, sb, 0);

                // KLUCZ: Kopiowanie do schowka systemowego Windows/Unity
                GUIUtility.systemCopyBuffer = sb.ToString();

                AddLog($" ");
                AddLog($">>> SUCCESS: Structure '{root.name}' Copied to clipboard");
                AddLog($">>> You can now paste it (Ctrl+V) into Notepad.");
            }
            else
            {
                AddLog("[Dump] No object was hit. Aim for the car.");
            }
        }

        [HideFromIl2Cpp]
        private void BuildHierarchyString(Transform t, StringBuilder sb, int indent)
        {
            string space = new string(' ', indent * 2);
            string activeStatus = t.gameObject.activeSelf ? "" : " [HIDDEN]";

            // Dodajemy nazwę obiektu i jego tag/warstwę dla lepszego info
            sb.AppendLine($"{space}- {t.name}{activeStatus} (L:{t.gameObject.layer})");

            for (int i = 0; i < t.childCount; i++)
            {
                BuildHierarchyString(t.GetChild(i), sb, indent + 1);
            }
        }

        [HideFromIl2Cpp]
        private void InspectAtCursor()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                AddLog("[Inspect] Error: Main camera not found.");
                return;
            }

            // Raycast ze środka ekranu (celownika)
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));

            // Zwiększyłem zasięg do 20m, żeby łapało obiekty w garażu bez podchodzenia pod samą blachę
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

                // Hierarchia - Rodzic
                AddLog($"Parent: {(t.parent != null ? t.parent.name : "None (Root)")}");

                // Hierarchia - Dzieci
                if (t.childCount > 0)
                {
                    var childrenNames = new List<string>();
                    for (int i = 0; i < t.childCount; i++)
                        childrenNames.Add(t.GetChild(i).name);

                    AddLog($"Children ({t.childCount}): {string.Join(", ", childrenNames)}");
                }
                else
                {
                    AddLog("Children: None");
                }

                
                var components = go.GetComponents<Component>();
                var compList = new List<string>();
                foreach (var c in components)
                {
                    if (c != null)
                        compList.Add(c.GetIl2CppType().Name);
                }
                AddLog($"Components: {string.Join(", ", compList)}");

                AddLog("-------------------------");
            }
            else
            {
                AddLog("[Inspect] No object detected within 20m range.");
            }
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

        private void CmdFixCar(int garageIndex)
        {
            try
            {
                var asm = GameAsm;
                var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
                var gcsType = asm.GetType("Il2CppCMS.SaveSystem.Containers.GarageCarsState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");

                // ── Ten sam snapshot co showgaragecars ────────────────────────────────
                var glWrapped = WrapGarageLoader();
                if (glWrapped == null) { AddLog("[fixcar] GarageLoader not found"); return; }

                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glWrapped, pars);

                var carsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                var getItem = carsRaw.GetType().GetMethod("get_Item");

                // ── Buduj listę identycznie jak showgaragecars ────────────────────────
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
                    var isCustomer = (bool)csdType.GetProperty("CustomerCar").GetValue(csd);
                    filled.Add((carId, uid, isCustomer));
                }

                if (garageIndex < 0 || garageIndex >= filled.Count)
                {
                    AddLog($"[fixcar] Index {garageIndex} out of range (0–{filled.Count - 1})");
                    AddLog("[fixcar] Use showgaragecars to see indices");
                    return;
                }

                var (targetCarId, targetUid, targetIsCustomer) = filled[garageIndex];
                AddLog($"[fixcar] Repairing [{garageIndex}] {targetCarId} (UID={targetUid})...");

                // ── Znajdź CarLoader w scenie (CarID + CustomerCar = jednoznaczne) ────
                var loaders = UnityEngine.Object.FindObjectsOfType<Il2CppCMS.Core.Car.CarLoader>(true);
                Il2CppCMS.Core.Car.CarLoader target = null;
                foreach (var l in loaders)
                {
                    if (string.IsNullOrEmpty(l.CarID)) continue;
                    if (l.CarID == targetCarId && l.CustomerCar == targetIsCustomer)
                    { target = l; break; }
                }

                if (target == null)
                {
                    AddLog($"[fixcar] CarLoader for {targetCarId} (Customer={targetIsCustomer}) not found in scene");
                    return;
                }

                // ── Naprawa ───────────────────────────────────────────────────────────
                target.Dev_RepairAllBody();
                AddLog("[fixcar] Dev_RepairAllBody() OK");

                typeof(Il2CppCMS.Core.Car.CarLoader)
                    .GetMethod("SetConditionAll")
                    .Invoke(target, new object[] { 1f });
                AddLog("[fixcar] SetConditionAll(1.0) OK");

                target.ClearEnginePartsConditionCache();
                AddLog("[fixcar] Engine cache cleared");

                // ── Zapisz ───────────────────────────────────────────────────────────
                var gl = UnityEngine.Object.FindObjectOfType<Il2CppCMS.SceneLoaders.GarageLoader>();
                if (gl != null) { gl.SaveState(); AddLog("[fixcar] SaveState OK"); }

                AddLog($"[fixcar] Done! {targetCarId} repaired to 100%.");
            }
            catch (Exception ex) { AddLog("[fixcar] ERR: " + ex.Message); }
        }

        private void ApplyConfig()
        {
            // na razie tylko autolock — tu będziemy dodawać kolejne opcje
            bool autolock = _config.GetBool("autolock");
            AddLog($"[Config] autolock = {autolock}");
        }

        private void InitInputBlocking()
        {
            // Tylko logujemy że system jest gotowy — właściwe szukanie obiektów
            // odbywa się lazy w SetGameInputEnabled przy pierwszym wywołaniu
            var assetType = System.Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
            if (assetType == null)
                AddLog("[InputBlock] MISSING: Unity.InputSystem");
            else
                AddLog("[InputBlock] OK — 'Lock Input' button ready");
        }


        public void ToggleInputLock()
        {
            _inputLocked = !_inputLocked;
            SetGameInputEnabled(!_inputLocked);

        }

        // ── Save system helpers ──────────────────────────────────────────────────


        [HideFromIl2Cpp]
        private Assembly GameAsm =>AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

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
            var asm = GameAsm;
            var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
            var il2gl = Il2CppInterop.Runtime.Il2CppType.From(glType);
            var gls = UnityEngine.Object.FindObjectsOfType(il2gl, true);
            if (gls.Length == 0) return null;
            return Activator.CreateInstance(glType, new object[] { gls[0].Pointer });
        }

        // ── Garage / Parking commands ────────────────────────────────────────────

        private void CmdShowGarageCars()
        {
            try
            {
                var asm = GameAsm;
                var glType = asm.GetType("Il2CppCMS.SceneLoaders.GarageLoader");
                var gcsType = asm.GetType("Il2CppCMS.SaveSystem.Containers.GarageCarsState");
                var csdType = asm.GetType("Il2CppCMS.SaveSystem.Containers.Car.CarSaveData");

                var glWrapped = WrapGarageLoader();
                if (glWrapped == null) { AddLog("[GarageCars] GarageLoader not found"); return; }

                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glWrapped, pars);

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

                // Ten sam snapshot co showgaragecars
                var glWrapped = WrapGarageLoader();
                if (glWrapped == null) { AddLog("[stealcustomercar] GarageLoader not found"); return; }

                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glWrapped, pars);

                var carsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int count = (int)carsRaw.GetType().GetProperty("Length").GetValue(carsRaw);
                var getItem = carsRaw.GetType().GetMethod("get_Item");

                // Zbuduj listę filled tak samo jak showgaragecars
                var filled = new System.Collections.Generic.List<(int slot, string carId, string uid, bool isCustomer)>();
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
                    filled.Add((i, carId, uid, isCustomer));
                }

                if (garageIndex < 0 || garageIndex >= filled.Count)
                {
                    AddLog($"[stealcustomercar] Index {garageIndex} out of range (0-{filled.Count - 1})");
                    AddLog("[stealcustomercar] Use showgaragecars to see indices");
                    return;
                }

                var (slot, targetCarId, targetUid, targetIsCustomer) = filled[garageIndex];

                if (!targetIsCustomer)
                {
                    AddLog($"[stealcustomercar] [{garageIndex}] {targetCarId} is already yours (Customer=False)");
                    return;
                }

                // Znajdź CarLoader w scenie po UID
                var loaders = UnityEngine.Object.FindObjectsOfType<Il2CppCMS.Core.Car.CarLoader>(true);
                Il2CppCMS.Core.Car.CarLoader target = null;
                foreach (var l in loaders)
                    if (!string.IsNullOrEmpty(l.CarID) && l.CarID == targetCarId && l.CustomerCar)
                    { target = l; break; }

                if (target == null)
                {
                    AddLog($"[stealcustomercar] CarLoader for {targetCarId} (UID={targetUid}) not found in scene");
                    return;
                }

                int jobId = target.orderConnection;
                AddLog($"[stealcustomercar] [{garageIndex}] {targetCarId} Customer=True orderConn={jobId} → stealing...");

                target.SetCustomerCar(false, 0);
                AddLog($"[stealcustomercar] SetCustomerCar OK → Customer={target.CustomerCar}");

                var og = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Core.OrderGenerator>();
                if (og != null && jobId > 0)
                {
                    og.CancelJob(jobId, false);
                    AddLog($"[stealcustomercar] CancelJob({jobId}) OK — AvailableJobs={og.AvailableJobsAmount}");
                }

                var gl = UnityEngine.Object.FindObjectOfType<Il2CppCMS.SceneLoaders.GarageLoader>();
                if (gl != null) { gl.SaveState(); AddLog("[stealcustomercar] SaveState OK"); }

                AddLog($"[stealcustomercar] Done! {targetCarId} is now yours. Use resetscene to reload.");
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

                // 1. Snapshot GarageCarsState przez GarageLoader (live scene)
                var glWrapped = WrapGarageLoader();
                if (glWrapped == null) { AddLog("[Store] GarageLoader not found"); return; }

                var gcsInst = Activator.CreateInstance(gcsType);
                var pars = new object[] { gcsInst };
                glType.GetMethod("SaveCarsState").Invoke(glWrapped, pars);

                var gcsCarsRaw = gcsType.GetProperty("Cars").GetValue(pars[0]);
                int gcsCount = (int)gcsCarsRaw.GetType().GetProperty("Length").GetValue(gcsCarsRaw);

                if (garageIndex < 0 || garageIndex >= gcsCount)
                {
                    AddLog($"[Store] Index {garageIndex} out of range (0-{gcsCount - 1})");
                    return;
                }

                var targetRaw = gcsCarsRaw.GetType().GetMethod("get_Item")
                                    .Invoke(gcsCarsRaw, new object[] { garageIndex });
                if (targetRaw == null) { AddLog($"[Store] Slot {garageIndex} is empty"); return; }

                var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)targetRaw);
                var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();
                if (string.IsNullOrEmpty(carId)) { AddLog($"[Store] Slot {garageIndex} has no CarID"); return; }

                // 2. Znajdź wolny slot w ParkingState i wstaw CarSaveData
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
                    {
                        freeSlot = i; break;
                    }
                }
                if (freeSlot == -1) { AddLog("[Store] Parking is full!"); return; }

                pSetItem.Invoke(pCarsRaw, new object[] { freeSlot, targetRaw });

                // IsDirty = true — bez tego serializer pomija zapis
                psType.GetProperty("IsDirty")?.SetValue(ps, true);

                AddLog($"[Store] {carId} (UID={uid}) → parking slot {freeSlot}");

                // 3. ResetCarID na CarLoaderze w scenie
                var il2cl = Il2CppInterop.Runtime.Il2CppType.From(clType);
                var cls = UnityEngine.Object.FindObjectsOfType(il2cl, true);
                bool loaderReset = false;
                foreach (var rawLoader in cls)
                {
                    var cl = Activator.CreateInstance(clType, new object[] { rawLoader.Pointer });
                    var lCarId = clType.GetProperty("CarID").GetValue(cl)?.ToString();
                    var lUid = clType.GetProperty("UID")?.GetValue(cl)?.ToString();
                    // dopasuj po UID jeśli dostępne, fallback po CarID
                    bool match = (!string.IsNullOrEmpty(lUid) && lUid == uid)
                              || (string.IsNullOrEmpty(lUid) && lCarId == carId);
                    if (!match) continue;

                    clType.GetMethod("ResetCarID").Invoke(cl, null);
                    AddLog($"[Store] ResetCarID on {rawLoader.name}");
                    loaderReset = true;
                    break;
                }
                if (!loaderReset) AddLog("[Store] WARNING: CarLoader not found in scene");

                // 4. GarageLoader.SaveState() — czyta ze sceny (teraz pusty loader)
                glType.GetMethod("SaveState").Invoke(glWrapped, null);
                AddLog("[Store] GarageLoader.SaveState() called");

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
                {
                    AddLog($"[Unstore] Index {parkingIndex} out of range (use showparkingcars)");
                    return;
                }

                var c = carsRaw.GetType().GetMethod("get_Item").Invoke(carsRaw, new object[] { parkingIndex });
                if (c == null) { AddLog($"[Unstore] Parking slot {parkingIndex} is already empty"); return; }

                var cptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(
                                (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)c);
                var csd = Activator.CreateInstance(csdType, new object[] { cptr });
                var carId = csdType.GetProperty("CarID").GetValue(csd)?.ToString();
                var uid = csdType.GetProperty("UID").GetValue(csd)?.ToString();

                carsRaw.GetType().GetMethod("set_Item").Invoke(carsRaw, new object[] { parkingIndex, null });
                AddLog($"[Unstore] {carId} (UID={uid}) removed from parking slot {parkingIndex}");

                var smInst = GetSaveManagerInstance();
                smInst?.GetType().GetMethod("SaveCurrentProfile")?.Invoke(smInst, null);
                AddLog("[Unstore] Save written.");
            }
            catch (Exception ex) { AddLog("[Unstore] ERR: " + ex.Message); }
        }

        private void SetGameInputEnabled(bool enabled)
        {
            // ── Metoda 1: InputManager.EnableGameplay (blokuje input bez UI) ─────────
            try
            {
                var pi = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerInput>();
                if (pi != null)
                {
                    var im = pi.GetType().GetProperty("inputManager").GetValue(pi);
                    if (im != null)
                        im.GetType().GetMethod("EnableGameplay").Invoke(im, new object[] { enabled });
                }
            }
            catch (Exception ex)
            {
                AddLog("[InputBlock] InputManager error: " + ex.Message);
            }

            // ── Metoda 2: stara — PlayerInput.enabled + UI Common map (blokuje UI) ───
            if (_playerInput == null)
                _playerInput = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Player.Controller.PlayerInput>();
            if (_playerInput != null)
                _playerInput.enabled = enabled;

            try
            {
                var assetType = System.Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
                if (assetType != null)
                {
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
                            if (name == "UI Common")
                            {
                                m.GetType().GetMethod(enabled ? "Enable" : "Disable").Invoke(m, null);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("[InputBlock] UICommon error: " + ex.Message);
            }

        }

    }
}
