using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CMS2026SimpleConsole
{
    public class UIToolkitConsoleRenderer : IConsoleRenderer
    {
        private readonly Action<string> _log;
        private readonly List<string> _logLines;

        // ── Assemblies ──────────────────────────────────────────────────────────
        private Assembly _ueAsm;
        private Assembly _trAsm;

        // ── Types ───────────────────────────────────────────────────────────────
        private Type _veType, _lblType, _btnType, _clickableType;
        private Type _sType, _slType, _scType;
        private Type _posType, _spType, _ofType, _soType;
        private Type _fontDefType, _sfdType;
        private Type _displayType, _sdType;
        private Type _alignType, _saType, _justifyType, _sjType;
        private Type _taType, _staType;
        private Type _tfType;
        private Type _sfType;

        // ── Constructors ────────────────────────────────────────────────────────
        private ConstructorInfo _slCtor, _scCtor, _spCtor, _soCtor;
        private ConstructorInfo _sfdCtor, _sdCtor, _saCtor, _sjCtor;
        private ConstructorInfo _staCtor, _sfCtor;

        // ── Font ────────────────────────────────────────────────────────────────
        private object _fontDef;

        // ── Layout ──────────────────────────────────────────────────────────────
        private float PanelW => ConsolePlugin.Config != null && float.TryParse(ConsolePlugin.Config.GetString("panel_width", "660"), out float _pw) ? Mathf.Clamp(_pw, 560f, 1400f) : 660f;
        private float PanelH => ConsolePlugin.Config != null&& float.TryParse(ConsolePlugin.Config.GetString("panel_height", "500"), out float _ph)? Mathf.Clamp(_ph, 300f, 1000f) : 500f;
        private const float TitleH = 24f;
        private float LogViewH => PanelH - TitleH - InputH - BtnBarH - Pad * 5f;
        private const float InputH = 28f;
        private const float BtnBarH = 26f;
        private const float LineH = 22f;
        private const float Pad = 4f;
        private const int MaxLabels = 300;

        private float _panelX = 20f;
        private float _panelY = 20f;

        // ── IL2CPP pointers ─────────────────────────────────────────────────────
        private IntPtr _panelPtr;
        private IntPtr _contentPtr;
        private IntPtr _textFieldPtr;
        private GameObject _go;
        private IntPtr _psPtr;
        private IntPtr _rootPtr;        
        private IntPtr _panelWidthValuePtr = IntPtr.Zero;   
        private IntPtr _panelHeightValuePtr = IntPtr.Zero; 

        // ── Log scroll ──────────────────────────────────────────────────────────
        private float _scrollY;
        private float _currentY;
        private IntPtr _logViewportPtr;

        // ── Config panel ─────────────────────────────────────────────────────────
        private IntPtr _configPanelPtr;
        private IntPtr _configContentPtr;
        private IntPtr _configBtnPtr;
        private float _configScrollY = 0f;
        private float _configContentH = 0f;

        // ── Heart panel ──────────────────────────────────────────────────────────────
        private IntPtr _heartPanelPtr;
        private IntPtr _heartContentPtr;
        private IntPtr _heartBtnPtr;
        private float _heartAnimProgress = 0f;
        private float _heartAnimTarget = 0f;
        private float _heartContentH = 0f;
        private float _heartScrollY = 0f;



        // Config toggle button pointers keyed by config key
        private readonly Dictionary<string, IntPtr> _cfgToggleBtns = new Dictionary<string, IntPtr>();

        private readonly Dictionary<string, IntPtr> _keybindLabelPtrs = new Dictionary<string, IntPtr>();
        private IntPtr _maxLogValuePtr = IntPtr.Zero;
        private float _pendingPanelWidth = 0f;   
        private float _pendingHeight = 0f;   
        private IntPtr _pendingWidthLabelPtr = IntPtr.Zero;  
        private IntPtr _pendingHeightLabelPtr = IntPtr.Zero;

        // ── Opacity pending ───────────────────────────────────────────────────────
        private float _pendingOpacity = 0.93f;
        private IntPtr _pendingOpacityLabelPtr = IntPtr.Zero;

        // ── Animation ────────────────────────────────────────────────────────────
        private float _animProgress = 0f;
        private float _animTarget = 0f;
        private const float AnimSpeed = 5f;

        // ── Style float ──────────────────────────────────────────────────────────
        // (opacity)

        // ── Drag ────────────────────────────────────────────────────────────────
        private bool _dragging;
        private Vector2 _dragOffset;

        // ── State ────────────────────────────────────────────────────────────────
        private bool _initialized;
        private bool _initFailed;
        private bool _visible = true;
        private string _commandInput = "";

        public bool InitFailed => _initFailed;

        // ── Interface ─────────────────────────────────────────────────────────────
        public bool IsVisible => _visible;
        public string CommandInput { get => _commandInput; set => _commandInput = value; }
        public event Action<string> OnCommandSubmitted;

        private bool _inputLocked = false;
        private IntPtr _lockBtnPtr;

        // ──kopiowanie logow───────────────────────────────────────────────────────
        private readonly Queue<(IntPtr ptr, float expireAt)> _flashQueue = new();

        // ─────────────────────────────────────────────────────────────────────────
        public UIToolkitConsoleRenderer(Action<string> log, List<string> logLines)
        {
            _log = log;
            _logLines = logLines;
        }

        // ── Initialize ───────────────────────────────────────────────────────────
        public void Initialize() { }

        // ── TryInit ──────────────────────────────────────────────────────────────
        public bool TryInit()
        {
            try
            {
                var allAsm = AppDomain.CurrentDomain.GetAssemblies();
                _log($"[UIToolkit] Loaded assemblies: {allAsm.Length}");

                _ueAsm = allAsm.FirstOrDefault(a => a.GetName().Name == "UnityEngine.UIElementsModule");
                _trAsm = allAsm.FirstOrDefault(a => a.GetName().Name == "UnityEngine.TextRenderingModule");

                if (_ueAsm == null) { _log("[UIToolkit] MISSING: UnityEngine.UIElementsModule"); return false; }
                if (_trAsm == null) { _log("[UIToolkit] MISSING: UnityEngine.TextRenderingModule"); return false; }

                var psType = _ueAsm.GetType("UnityEngine.UIElements.PanelSettings");
                if (psType == null) { _log("[UIToolkit] MISSING type: PanelSettings"); return false; }

                Il2CppSystem.Type il2cppPsType;
                try { il2cppPsType = Il2CppInterop.Runtime.Il2CppType.From(psType); }
                catch (Exception ex) { _log($"[UIToolkit] Il2CppType.From failed: {ex.Message}"); return false; }

                ResolveTypes();
                ResolveCtors();
                SetupFont();
                BuildUI();

                _initialized = true;
                RebuildAllLines();
                _log("[UIToolkit] Renderer initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException;
                while (inner?.InnerException != null) inner = inner.InnerException;
                _log($"[UIToolkit] Init error: {ex.GetType().Name}: {ex.Message}");
                if (inner != null && inner != ex)
                    _log($"[UIToolkit] Inner: {inner.GetType().Name}: {inner.Message}");
                _log($"[UIToolkit] Stack: {(inner ?? ex).StackTrace?.Split('\n')[0]}");
                _initFailed = true;
                return false;
            }
        }

        // ── Type resolution ──────────────────────────────────────────────────────
        private void ResolveTypes()
        {
            _veType = _ueAsm.GetType("UnityEngine.UIElements.VisualElement");
            _lblType = _ueAsm.GetType("UnityEngine.UIElements.Label");
            _btnType = _ueAsm.GetType("UnityEngine.UIElements.Button");
            _clickableType = _ueAsm.GetType("UnityEngine.UIElements.Clickable");
            _sType = _ueAsm.GetType("UnityEngine.UIElements.IStyle");
            _slType = _ueAsm.GetType("UnityEngine.UIElements.StyleLength");
            _scType = _ueAsm.GetType("UnityEngine.UIElements.StyleColor");
            _fontDefType = _ueAsm.GetType("UnityEngine.UIElements.FontDefinition");
            _sfdType = _ueAsm.GetType("UnityEngine.UIElements.StyleFontDefinition");
            _tfType = _ueAsm.GetType("UnityEngine.UIElements.TextField");

            _posType = _ueAsm.GetType("UnityEngine.UIElements.Position");
            _spType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_posType);

            _ofType = _ueAsm.GetType("UnityEngine.UIElements.Overflow");
            _soType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_ofType);

            _displayType = _ueAsm.GetType("UnityEngine.UIElements.DisplayStyle");
            _sdType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_displayType);

            _alignType = _ueAsm.GetType("UnityEngine.UIElements.Align");
            _saType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_alignType);
            _justifyType = _ueAsm.GetType("UnityEngine.UIElements.Justify");
            _sjType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_justifyType);

            _taType = typeof(UnityEngine.TextAnchor);
            _staType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_taType);

            _sfType = _ueAsm.GetType("UnityEngine.UIElements.StyleFloat");
        }

        private void ResolveCtors()
        {
            _slCtor = _slType.GetConstructor(new Type[] { typeof(float) });
            _scCtor = _scType.GetConstructor(new Type[] { typeof(Color) });
            _spCtor = _spType.GetConstructor(new Type[] { _posType });
            _soCtor = _soType.GetConstructor(new Type[] { _ofType });
            _sfdCtor = _sfdType.GetConstructor(new Type[] { _fontDefType });
            _sdCtor = _sdType.GetConstructor(new Type[] { _displayType });
            _saCtor = _saType.GetConstructor(new Type[] { _alignType });
            _sjCtor = _sjType.GetConstructor(new Type[] { _justifyType });
            _staCtor = _staType.GetConstructor(new Type[] { _taType });
            _sfCtor = _sfType.GetConstructor(new[] { typeof(float) });
        }

        private void SetupFont()
        {
            var fontType = _trAsm.GetType("UnityEngine.Font");
            var builtinFont = Resources.GetBuiltinResource(
                Il2CppInterop.Runtime.Il2CppType.From(fontType), "LegacyRuntime.ttf");
            var fontWrapped = Activator.CreateInstance(fontType, new object[] { builtinFont.Pointer });
            _fontDef = _fontDefType.GetMethod("FromFont").Invoke(null, new object[] { fontWrapped });
        }

        // ── Build UI ─────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            var psType = _ueAsm.GetType("UnityEngine.UIElements.PanelSettings");
            var docType = _ueAsm.GetType("UnityEngine.UIElements.UIDocument");
            var smType = _ueAsm.GetType("UnityEngine.UIElements.PanelScaleMode");

            var il2cppPsType = Il2CppInterop.Runtime.Il2CppType.From(psType);
            var psRaw = UnityEngine.ScriptableObject.CreateInstance(il2cppPsType);
            var psWrap = Activator.CreateInstance(psType, new object[] { psRaw.Pointer });
            _psPtr = psRaw.Pointer;

            psType.GetProperty("scaleMode").SetValue(psWrap, Enum.Parse(smType, "ConstantPixelSize"));
            psType.GetProperty("scale").SetValue(psWrap, 1.0f);
            psType.GetProperty("sortingOrder").SetValue(psWrap, 9999);

            _go = new GameObject("CMS_UIToolkitConsoleRenderer");
            UnityEngine.Object.DontDestroyOnLoad(_go);

            var docRaw = _go.AddComponent(Il2CppInterop.Runtime.Il2CppType.From(docType));
            var docWrap = Activator.CreateInstance(docType, new object[] { ((Component)docRaw).Pointer });
            docType.GetProperty("panelSettings").SetValue(docWrap, psWrap);

            var root = docType.GetProperty("rootVisualElement").GetValue(docWrap);
            BuildPanel(root);
        }

        private void BuildPanel(object root)
        {
            _rootPtr = Ptr(root);
            var panel = VE();
            var s = Style(panel);
            SPosition(s, "Absolute");
            SLeft(s, _panelX); STop(s, _panelY);
            SWidth(s, PanelW); SHeight(s, PanelH);

            float bgAlpha = float.TryParse(ConsolePlugin.Config?.GetString("console_opacity", "0.93"), out float _opa)? Mathf.Clamp(_opa, 0.10f, 1f) : 0.93f;
            SBg(s, new Color(0.08f, 0.08f, 0.1f, bgAlpha));
            SOverflow(s, "Hidden");
            AddChild(root, panel);
            _panelPtr = Ptr(panel);

            BuildTitleBar(panel);
            BuildLogArea(panel);
            BuildConfigPanel(panel);
            BuildHeartPanel(panel);
            BuildInputRow(panel);
            BuildButtonRow(panel);
            BuildSignature(panel);

            ApplyDisplay(_panelPtr, _visible);
        }

        public void ReapplyTopmost()
        {
            if (!_initialized || _psPtr == IntPtr.Zero) return;
            var psType = _ueAsm.GetType("UnityEngine.UIElements.PanelSettings");
            var smType = _ueAsm.GetType("UnityEngine.UIElements.PanelScaleMode");
            var psWrap = Activator.CreateInstance(psType, new object[] { _psPtr });
            psType.GetProperty("sortingOrder").SetValue(psWrap, 9999);
            psType.GetProperty("scaleMode").SetValue(psWrap, Enum.Parse(smType, "ConstantPixelSize"));
            psType.GetProperty("scale").SetValue(psWrap, 1.0f);
        }

        private void BuildTitleBar(object panel)
        {
            var lbl = Activator.CreateInstance(_lblType);
            var s = Style(lbl);
            SPosition(s, "Absolute");
            SLeft(s, 0f); STop(s, 0f);
            SWidth(s, PanelW); SHeight(s, TitleH);
            SBg(s, new Color(0.15f, 0.15f, 0.22f, 1f));
            SColor(s, Color.white);
            SFont(s);
            _lblType.GetProperty("text").SetValue(lbl,
                "  CMS2026 Simple Console");
            AddChild(panel, lbl);
        }

        private void BuildLogArea(object panel)
        {
            float vpTop = TitleH + Pad;

            var viewport = VE();
            var vs = Style(viewport);
            SPosition(vs, "Absolute");
            SLeft(vs, Pad); STop(vs, vpTop);
            SWidth(vs, PanelW - Pad * 2); SHeight(vs, LogViewH);
            SBg(vs, new Color(0f, 0f, 0f, 0.6f));
            SOverflow(vs, "Hidden");
            AddChild(panel, viewport);
            _logViewportPtr = Ptr(viewport);

            var content = VE();
            var cs = Style(content);
            SPosition(cs, "Absolute");
            SLeft(cs, 0f); STop(cs, 0f);
            SWidth(cs, PanelW - Pad * 2);
            AddChild(viewport, content);
            _contentPtr = Ptr(content);
        }

        private void BuildInputRow(object panel)
        {
            float rowTop = TitleH + Pad + LogViewH + Pad;

            var tf = Activator.CreateInstance(_tfType);
            var s = Style(tf);
            SPosition(s, "Absolute");
            SLeft(s, Pad); STop(s, rowTop);
            SWidth(s, PanelW - 110f); SHeight(s, InputH);
            SBg(s, new Color(0.04f, 0.04f, 0.07f, 1f));
            SColor(s, new Color(0.85f, 1f, 0.85f, 1f));
            SFont(s);
            AddChild(panel, tf);
            _textFieldPtr = Ptr(tf);
            RegisterSubmitCallback(_textFieldPtr);

            MakeButton(panel, "Submit",
                PanelW - 102f, rowTop, 98f, InputH,
                new Color(0.15f, 0.5f, 0.15f, 1f),
                () => SubmitTextField());
        }

        private void RegisterSubmitCallback(IntPtr ptr)
        {
            var trickleType = _ueAsm.GetType("UnityEngine.UIElements.TrickleDown");
            var regMethod = _veType.GetMethods()
                .First(m => m.Name == "RegisterCallback"
                         && m.IsGenericMethod
                         && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(UnityEngine.UIElements.KeyDownEvent));

            var tf = Activator.CreateInstance(_tfType, new object[] { ptr });

            System.Action<UnityEngine.UIElements.KeyDownEvent> handler = (evt) =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    SubmitTextField();
            };

            var il2cb = Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<UnityEngine.UIElements.EventCallback<UnityEngine.UIElements.KeyDownEvent>>(handler);

            regMethod.Invoke(tf, new object[] { il2cb, Enum.Parse(trickleType, "TrickleDown") });
        }

        private void SubmitTextField()
        {
            if (_textFieldPtr == IntPtr.Zero) return;
            var tfFresh = Activator.CreateInstance(_tfType, new object[] { _textFieldPtr });
            string val = (string)_tfType.GetProperty("value").GetValue(tfFresh);
            if (!string.IsNullOrEmpty(val))
            {
                _tfType.GetProperty("value").SetValue(tfFresh, "");
                _commandInput = "";
                OnCommandSubmitted?.Invoke(val);
            }
        }

        private void BuildButtonRow(object panel)
        {
            float rowTop = TitleH + Pad + LogViewH + Pad + InputH + Pad;

            MakeButton(panel, "Clear",
                Pad, rowTop, 80f, BtnBarH,
                new Color(0.45f, 0.12f, 0.12f, 1f),
                () => OnCommandSubmitted?.Invoke("__clear"));

            MakeButton(panel, "Help",
                Pad + 84f, rowTop, 80f, BtnBarH,
                new Color(0.18f, 0.28f, 0.5f, 1f),
                () => OnCommandSubmitted?.Invoke("help"));

            MakeButton(panel, "Copy log",
                Pad + 168f, rowTop, 90f, BtnBarH,
                new Color(0.18f, 0.28f, 0.5f, 1f),
                () => OnCommandSubmitted?.Invoke("__copylog"));

            // → IMGUI moved to config panel. "⚙ Config" takes its old slot.
            var cfgBtn = MakeButtonWithPtr(panel, "🔧  Config",
                Pad + 262f, rowTop, 92f, BtnBarH,
                new Color(0.15f, 0.38f, 0.28f, 1f),
                ToggleConfig);
            _configBtnPtr = Ptr(cfgBtn);
        }

        private void BuildSignature(object panel)
        {
            float rowTop = TitleH + Pad + LogViewH + Pad + InputH + Pad;

            const float HeartBtnW = 72f;
            const float Gap = 4f;
            // Kończymy tuż przed podpisem: PanelW - 148f - Gap - HeartBtnW
            float heartBtnX = PanelW - 148f - Gap - HeartBtnW;

            var heartBtn = MakeButtonWithPtr(panel, "♥ About",
                heartBtnX, rowTop, HeartBtnW, BtnBarH,
                new Color(0f, 0f, 0f, 0f),
                ToggleHeart);
            SColor(Style(heartBtn), new Color(1f, 0.55f, 0.70f, 1f));
            _heartBtnPtr = Ptr(heartBtn);

            string sigLabel = $"SC {ConsolePlugin.Version} by Blaster";
            MakeButtonLink(Wrap(_panelPtr), sigLabel,
                PanelW - 148f, rowTop, 144f, BtnBarH,
                () => Application.OpenURL("https://github.com/iBl4St3R/CMS2026-Simple-Console"));
        }

        // ── Config panel ──────────────────────────────────────────────────────────
        private void BuildConfigPanel(object panel)
        {
            float vpTop = TitleH + Pad;

            // Outer animated viewport
            var cfg = VE();
            var s = Style(cfg);
            SPosition(s, "Absolute");
            SLeft(s, Pad); STop(s, vpTop);
            SWidth(s, PanelW - Pad * 2); SHeight(s, LogViewH);
            SBg(s, new Color(0.04f, 0.06f, 0.10f, 1f));
            SOverflow(s, "Hidden");
            SOpacity(s, 0f);
            AddChild(panel, cfg);
            _configPanelPtr = Ptr(cfg);
            ApplyDisplay(_configPanelPtr, false);

            // Inner scrollable content container
            var content = VE();
            var cs = Style(content);
            SPosition(cs, "Absolute");
            SLeft(cs, 0f); STop(cs, 0f);
            SWidth(cs, PanelW - Pad * 2);
            AddChild(cfg, content);
            _configContentPtr = Ptr(content);

            // ── Build config items ───────────────────────────────────────────────
            float y = 12f;
            _cfgToggleBtns.Clear();

            // Header
            CfgLabel(content, "🔧   Configuration",
                Pad, y, PanelW - Pad * 4, 26f,
                new Color(0.55f, 0.80f, 1.00f, 1f), fontSize: 13);
            y += 30f;

            CfgDivider(content, y, new Color(0.30f, 0.45f, 0.75f, 0.8f));
            y += 10f;

            // Mod Folder button
            MakeButton(content, "📁 Mod Folder",
                Pad, y, 100f, 24f,
                new Color(0.18f, 0.28f, 0.48f, 1f),
                () => OnCommandSubmitted?.Invoke("__openfolder"));
            y += 36f;

            CfgDivider(content, y, new Color(0.20f, 0.30f, 0.55f, 0.5f));
            y += 14f;

            // ── RENDERER ────────────────────────────────────────────────────────
            CfgSectionLabel(content, "RENDERER", y);
            y += 22f;

            CfgToggleRow(content, "UIToolkit priority  (restart required)", "uitoolkit_priority", ref y);

            // Switch to IMGUI
            MakeButton(content, "Switch to IMGUI",
                PanelW - Pad * 2 - 130f, y, 124f, 24f,
                new Color(0.3f, 0.2f, 0.45f, 1f),
                () =>
                {
                    // close config first, then switch
                    _animTarget = 0f;
                    RefreshConfigButtonLabel();
                    OnCommandSubmitted?.Invoke("__switchrenderer");
                });
            CfgLabel(content, "Switch renderer to IMGUI",
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 34f;

            CfgDivider(content, y, new Color(0.20f, 0.30f, 0.55f, 0.5f));
            y += 14f;

            // ── CONSOLE ─────────────────────────────────────────────────────────
            CfgSectionLabel(content, "CONSOLE", y);
            y += 22f;

            CfgToggleRow(content, "Show timestamps in log", "show_timestamps", ref y);
            CfgToggleRow(content, "Lock game input when open", "lock_input_when_open", ref y);
            CfgToggleRow(content, "Show console at startup", "show_at_startup", ref y);
            CfgToggleRow(content, "Capture Unity logs", "capture_unity_logs", ref y);

            // Max log lines — placeholder
            CfgMaxLogRow(content, ref y);

            // Panel size (UIToolkit only)
            CfgPanelSizeRow(content, "Panel width", "panel_width", 560f, 1400f, ref y);
            CfgPanelSizeRow(content, "Panel height", "panel_height", 300f, 1000f, ref y);

            //opacity panel
            CfgOpacityRow(content, ref y);

            CfgDivider(content, y, new Color(0.20f, 0.30f, 0.55f, 0.5f));
            y += 14f;

            // ── KEYBINDS ────────────────────────────────────────────────────────
            CfgSectionLabel(content, "KEYBINDS", y);
            y += 22f;

            // Default console key
            CfgKeybindRow(content, "Toggle console key", "toggle_console_key", ref y);

            // Lock game input standalone
            CfgKeybindRow(content, "Lock input (standalone)", "standalone_lock_key", ref y);

            CfgDivider(content, y, new Color(0.20f, 0.30f, 0.55f, 0.5f));
            y += 14f;

            // Restore Defaults
            MakeButton(content, "Restore Defaults",
                Pad, y, 130f, 26f,
                new Color(0.45f, 0.25f, 0.10f, 1f),
                () =>
                {
                    ConsolePlugin.Config?.RestoreDefaults();
                    RebuildForResize();   // rebuild czyta świeże wartości z configu
                });
            y += 40f;

            _configContentH = y;
        }

        // ── Config toggle helpers ─────────────────────────────────────────────────

        private void CfgToggleRow(object parent, string desc, string configKey, ref float y)
        {
            bool current = ConsolePlugin.Config?.GetBool(configKey, true) ?? true;

            var btn = MakeButtonWithPtr(parent,
                current ? "ON" : "OFF",
                PanelW - Pad * 2 - 130f, y, 124f, 24f,
                current ? new Color(0.20f, 0.70f, 0.35f, 1f) : new Color(0.50f, 0.15f, 0.15f, 1f),
                () => ToggleConfigKey(configKey));

            if (!_cfgToggleBtns.ContainsKey(configKey))
                _cfgToggleBtns[configKey] = Ptr(btn);

            CfgLabel(parent, desc,
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 34f;
        }

        private void CfgPlaceholderRow(object parent, string desc, string badgeText, ref float y)
        {
            MakeButton(parent, badgeText,
                PanelW - Pad * 2 - 130f, y, 124f, 24f,
                new Color(0.28f, 0.28f, 0.33f, 1f),
                () => { }); // TODO

            CfgLabel(parent, desc,
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.55f, 0.58f, 0.65f, 1f));
            y += 34f;
        }

        private void CfgKeybindRow(object parent, string desc, string configKey, ref float y)
        {
            string cur = ConsolePlugin.Config?.GetString(configKey, "None") ?? "None";

            var btn = MakeButtonWithPtr(parent, cur,
                PanelW - Pad * 2 - 130f, y, 96f, 24f,
                new Color(0.18f, 0.28f, 0.48f, 1f),
                () => OnCommandSubmitted?.Invoke("__startbind:" + configKey));

            MakeButton(parent, "✎",
                PanelW - Pad * 2 - 30f, y, 24f, 24f,
                new Color(0.12f, 0.22f, 0.40f, 1f),
                () => OnCommandSubmitted?.Invoke("__startbind:" + configKey));

            _keybindLabelPtrs[configKey] = Ptr(btn);

            CfgLabel(parent, desc,
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 34f;
        }

        private void CfgMaxLogRow(object parent, ref float y)
        {
            string cur = ConsolePlugin.Config?.GetString("max_log_lines", "2000") ?? "2000";

            var lbl = MakeButtonWithPtr(parent, cur,
                PanelW - Pad * 2 - 130f, y, 62f, 24f,
                new Color(0.10f, 0.10f, 0.16f, 1f),
                () => { });
            _maxLogValuePtr = Ptr(lbl);

            MakeButton(parent, "−",
                PanelW - Pad * 2 - 64f, y, 28f, 24f,
                new Color(0.38f, 0.18f, 0.18f, 1f),
                () => StepMaxLogLines(-500));

            MakeButton(parent, "+",
                PanelW - Pad * 2 - 32f, y, 28f, 24f,
                new Color(0.18f, 0.38f, 0.18f, 1f),
                () => StepMaxLogLines(+500));

            CfgLabel(parent, "Max log lines",
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 34f;
        }


        //C# nie pozwala na ref do właściwości warunkowej w lambdzie w ten sposób
        //private void CfgPanelSizeRow(object parent, string label,
        //    string configKey, float min, float max, ref float y)
        //{
        //    string cur = ConsolePlugin.Config?.GetString(configKey,
        //        configKey == "panel_width" ? "660" : "500") ?? "660";

        //    var lbl = MakeButtonWithPtr(parent, cur,
        //        PanelW - Pad * 2 - 130f, y, 62f, 24f,
        //        new Color(0.10f, 0.10f, 0.16f, 1f),
        //        () => { });

        //    if (configKey == "panel_width") _panelWidthValuePtr = Ptr(lbl);
        //    else _panelHeightValuePtr = Ptr(lbl);

        //    MakeButton(parent, "−",
        //        PanelW - Pad * 2 - 64f, y, 28f, 24f,
        //        new Color(0.38f, 0.18f, 0.18f, 1f),
        //        () =>
        //        {
        //            StepPanelSize(configKey, -50f, min, max,
        //                ref configKey == "panel_width"
        //                    ? ref _panelWidthValuePtr
        //                    : ref _panelHeightValuePtr);
        //        });

        //    MakeButton(parent, "+",
        //        PanelW - Pad * 2 - 32f, y, 28f, 24f,
        //        new Color(0.18f, 0.38f, 0.18f, 1f),
        //        () =>
        //        {
        //            StepPanelSize(configKey, +50f, min, max,
        //                ref configKey == "panel_width"
        //                    ? ref _panelWidthValuePtr
        //                    : ref _panelHeightValuePtr);
        //        });

        //    CfgLabel(parent, label,
        //        Pad * 2, y + 3f, PanelW - 180f, 20f,
        //        new Color(0.82f, 0.85f, 0.92f, 1f));
        //    y += 34f;
        //}

        //workaround
        private void CfgPanelSizeRow(object parent, string label,
           string configKey, float min, float max, ref float y)
        {
            float def = configKey == "panel_width" ? 660f : 500f;
            float cur = float.TryParse(
                ConsolePlugin.Config?.GetString(configKey, def.ToString()),
                out float cv) ? Mathf.Clamp(cv, min, max) : def;  // <-- clamp z pliku cfg

            if (configKey == "panel_width") _pendingPanelWidth = cur;
            else _pendingHeight = cur;

            var lbl = MakeButtonWithPtr(parent, ((int)cur).ToString(),
                PanelW - Pad * 2 - 130f, y, 62f, 24f,
                new Color(0.10f, 0.10f, 0.16f, 1f),
                () => { });

            if (configKey == "panel_width") _pendingWidthLabelPtr = Ptr(lbl);
            else _pendingHeightLabelPtr = Ptr(lbl);

            MakeButton(parent, "−",
                PanelW - Pad * 2 - 64f, y, 28f, 24f,
                new Color(0.38f, 0.18f, 0.18f, 1f),
                () => StepPanelSize(configKey, -50f, min, max));

            MakeButton(parent, "+",
                PanelW - Pad * 2 - 32f, y, 28f, 24f,
                new Color(0.18f, 0.38f, 0.18f, 1f),
                () => StepPanelSize(configKey, +50f, min, max));

            // Set button — dopiero tu zapisuje i przebudowuje
            MakeButton(parent, "Set",
                PanelW - Pad * 2 - 130f, y + 28f, 94f, 22f,
                new Color(0.20f, 0.45f, 0.20f, 1f),
                () =>
                {
                    float pending = configKey == "panel_width" ? _pendingPanelWidth : _pendingHeight;
                    ConsolePlugin.Config?.Set(configKey, ((int)pending).ToString());
                    RebuildForResize();
                });

            CfgLabel(parent, label,
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 56f; // więcej miejsca bo Set jest pod spodem
        }



        private void StepMaxLogLines(int delta)
        {
            if (ConsolePlugin.Config == null) return;
            int cur = int.TryParse(
                ConsolePlugin.Config.GetString("max_log_lines", "2000"), out int v) ? v : 2000;
            int next = Mathf.Clamp(cur + delta, 100, 10000);
            ConsolePlugin.Config.Set("max_log_lines", next.ToString());
            if (_maxLogValuePtr != IntPtr.Zero)
            {
                var b = Activator.CreateInstance(_btnType, new object[] { _maxLogValuePtr });
                _btnType.GetProperty("text").SetValue(b, next.ToString());
            }
            OnCommandSubmitted?.Invoke("__applyconfig");
        }


        //c# nie pozwala na ref do właściwości warunkowej w lambdzi
        //private void StepPanelSize(string key, float delta, float min, float max,
        //                           ref IntPtr labelPtr)
        //{
        //    if (ConsolePlugin.Config == null) return;
        //    float cur = float.TryParse(
        //        ConsolePlugin.Config.GetString(key, key == "panel_width" ? "660" : "500"),
        //        out float v) ? v : (key == "panel_width" ? 660f : 500f);
        //    float next = Mathf.Clamp(cur + delta, min, max);
        //    ConsolePlugin.Config.Set(key, ((int)next).ToString());
        //    RebuildForResize();
        //}

        //workaround
        private void StepPanelSize(string key, float delta, float min, float max)
        {
            if (ConsolePlugin.Config == null) return;

            if (key == "panel_width")
            {
                _pendingPanelWidth = Mathf.Clamp(_pendingPanelWidth + delta, min, max);
                if (_pendingWidthLabelPtr != IntPtr.Zero)
                {
                    var b = Activator.CreateInstance(_btnType, new object[] { _pendingWidthLabelPtr });
                    _btnType.GetProperty("text").SetValue(b, ((int)_pendingPanelWidth).ToString());
                }
            }
            else
            {
                _pendingHeight = Mathf.Clamp(_pendingHeight + delta, min, max);
                if (_pendingHeightLabelPtr != IntPtr.Zero)
                {
                    var b = Activator.CreateInstance(_btnType, new object[] { _pendingHeightLabelPtr });
                    _btnType.GetProperty("text").SetValue(b, ((int)_pendingHeight).ToString());
                }
            }
        }

        private void CfgOpacityRow(object parent, ref float y)
        {
            float def = 0.93f;
            _pendingOpacity = float.TryParse(
                ConsolePlugin.Config?.GetString("console_opacity", "0.93"), out float cv)
                ? Mathf.Clamp(cv, 0.10f, 1.00f) : def;

            var lbl = MakeButtonWithPtr(parent,
                Mathf.RoundToInt(_pendingOpacity * 100f) + "%",
                PanelW - Pad * 2 - 130f, y, 62f, 24f,
                new Color(0.10f, 0.10f, 0.16f, 1f),
                () => { });
            _pendingOpacityLabelPtr = Ptr(lbl);

            MakeButton(parent, "−",
                PanelW - Pad * 2 - 64f, y, 28f, 24f,
                new Color(0.38f, 0.18f, 0.18f, 1f),
                () => StepOpacity(-0.05f));

            MakeButton(parent, "+",
                PanelW - Pad * 2 - 32f, y, 28f, 24f,
                new Color(0.18f, 0.38f, 0.18f, 1f),
                () => StepOpacity(+0.05f));

            MakeButton(parent, "Set",
                PanelW - Pad * 2 - 130f, y + 28f, 94f, 22f,
                new Color(0.20f, 0.45f, 0.20f, 1f),
                () =>
                {
                    ConsolePlugin.Config?.Set("console_opacity",
                        _pendingOpacity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    RebuildForResize();
                });

            CfgLabel(parent, "Window opacity  (UIToolkit only)",
                Pad * 2, y + 3f, PanelW - 180f, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 56f;
        }

        private void StepOpacity(float delta)
        {
            _pendingOpacity = Mathf.Round(
                Mathf.Clamp(_pendingOpacity + delta, 0.10f, 1.00f) * 100f) / 100f;
            if (_pendingOpacityLabelPtr == IntPtr.Zero) return;
            var b = Activator.CreateInstance(_btnType, new object[] { _pendingOpacityLabelPtr });
            _btnType.GetProperty("text").SetValue(b,
                Mathf.RoundToInt(_pendingOpacity * 100f) + "%");
        }

        public void RefreshKeybindLabels()
        {
            if (ConsolePlugin.Config == null) return;
            foreach (var kv in _keybindLabelPtrs)
            {
                if (kv.Value == IntPtr.Zero) continue;
                string val = ConsolePlugin.Config.GetString(kv.Key, "None");
                var btn = Activator.CreateInstance(_btnType, new object[] { kv.Value });
                _btnType.GetProperty("text").SetValue(btn, val);
            }
        }

        private void RefreshMaxLogLabel()
        {
            if (_maxLogValuePtr == IntPtr.Zero || ConsolePlugin.Config == null) return;
            var b = Activator.CreateInstance(_btnType, new object[] { _maxLogValuePtr });
            _btnType.GetProperty("text").SetValue(b,
                ConsolePlugin.Config.GetString("max_log_lines", "2000"));
        }



        private void ToggleConfigKey(string key)
        {
            if (ConsolePlugin.Config == null) return;
            bool newVal = !ConsolePlugin.Config.GetBool(key, true);
            ConsolePlugin.Config.SetBool(key, newVal);
            UpdateToggleBtn(key, newVal);
            OnCommandSubmitted?.Invoke("__applyconfig");
        }

        private void UpdateToggleBtn(string key, bool val)
        {
            if (!_cfgToggleBtns.TryGetValue(key, out IntPtr ptr) || ptr == IntPtr.Zero) return;
            var btn = Activator.CreateInstance(_btnType, new object[] { ptr });
            _btnType.GetProperty("text").SetValue(btn, val ? "ON" : "OFF");
            var s = Style(btn);
            SBg(s, val ? new Color(0.20f, 0.70f, 0.35f, 1f) : new Color(0.50f, 0.15f, 0.15f, 1f));
        }

        private void RefreshAllConfigToggles()
        {
            if (ConsolePlugin.Config == null) return;
            foreach (var kv in _cfgToggleBtns)
                UpdateToggleBtn(kv.Key, ConsolePlugin.Config.GetBool(kv.Key, true));

            RefreshKeybindLabels();
            RefreshMaxLogLabel();
        }

        private void BuildHeartPanel(object panel)
        {
            float vpTop = TitleH + Pad;

            var heart = VE();
            var s = Style(heart);
            SPosition(s, "Absolute");
            SLeft(s, Pad); STop(s, vpTop);
            SWidth(s, PanelW - Pad * 2); SHeight(s, LogViewH);
            SBg(s, new Color(0.07f, 0.04f, 0.10f, 1f));
            SOverflow(s, "Hidden");
            SOpacity(s, 0f);
            AddChild(panel, heart);
            _heartPanelPtr = Ptr(heart);
            ApplyDisplay(_heartPanelPtr, false);

            var content = VE();
            var cs = Style(content);
            SPosition(cs, "Absolute");
            SLeft(cs, 0f); STop(cs, 0f);
            SWidth(cs, PanelW - Pad * 2);
            AddChild(heart, content);
            _heartContentPtr = Ptr(content);

            float y = 12f;


            // ── Notice ───────────────────────────────────────────────────────────────────
            CfgLabel(content, "⚠  NOTICE",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.95f, 0.80f, 0.30f, 1f));
            y += 24f;

            CfgLabel(content,
                "Once the full game is available for purchase, this tool will no longer",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.82f, 0.82f, 0.75f, 1f));
            y += 18f;

            CfgLabel(content,
                "support the demo in any capacity.",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.82f, 0.82f, 0.75f, 1f));
            y += 26f;

            CfgLabel(content,
                "This tool exists for educational purposes — to explore how the game works",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.82f, 0.82f, 0.75f, 1f));
            y += 18f;

            CfgLabel(content,
                "and to give the community a welcoming entry point into modding.",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.82f, 0.82f, 0.75f, 1f));
            y += 26f;

            CfgLabel(content,
                "The author does not condone piracy in any form.",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.90f, 0.60f, 0.60f, 1f));
            y += 18f;

            CfgLabel(content,
                "Hard work deserves respect — please support the developers.",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.90f, 0.60f, 0.60f, 1f));
            y += 34f;

            // ── Header ───────────────────────────────────────────────────────────────
            CfgLabel(content, "♥   About & Credits",
                Pad, y, PanelW - Pad * 4, 26f,
                new Color(1f, 0.55f, 0.70f, 1f), fontSize: 13);
            y += 30f;

            CfgDivider(content, y, new Color(0.75f, 0.30f, 0.45f, 0.8f));
            y += 14f;

            // ── Mod name ─────────────────────────────────────────────────────────────
            CfgLabel(content, $"CMS2026 Simple Console  v{ConsolePlugin.Version}",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.92f, 0.92f, 1.00f, 1f));
            y += 26f;

            // ── Author ───────────────────────────────────────────────────────────────
            CfgLabel(content, "Created by  Blaster",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.82f, 0.85f, 0.92f, 1f));
            y += 30f;

            // ── GitHub + Nexus  ─────────────────────────────────
            float btnW = 110f;
            float btnGap = 8f;
            float btnsX = Pad * 2;

            MakeButton(content, "GitHub →",
                btnsX, y, btnW, 24f,
                new Color(0.13f, 0.13f, 0.26f, 1f),
                () => Application.OpenURL("https://github.com/iBl4St3R/CMS2026-Simple-Console"));

            MakeButton(content, "Nexus Mods →",
                btnsX + btnW + btnGap, y, btnW + 10f, 24f,
                new Color(0.22f, 0.13f, 0.05f, 1f),
                () => Application.OpenURL(
                    "https://www.nexusmods.com/carmechanicsimulator2026/mods/2?tab=description"));
            y += 38f;

            CfgDivider(content, y, new Color(0.55f, 0.22f, 0.35f, 0.5f));
            y += 14f;

            // ── Thanks ───────────────────────────────────────────────────────────────
            CfgSectionLabel(content, "SPECIAL THANKS", y);
            y += 22f;

            CfgLabel(content,
                "Thanks to the MelonLoader team for making Unity modding accessible.",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.78f, 0.82f, 0.90f, 1f));
            y += 24f;

            CfgLabel(content,
                "Thank you Red Dot Games for releasing the demo and giving the community",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.78f, 0.82f, 0.90f, 1f));
            y += 20f;

            CfgLabel(content,
                "a chance to explore Car Mechanic Simulator 2026 early.  ♥",
                Pad * 2, y, PanelW - Pad * 6, 20f,
                new Color(0.78f, 0.82f, 0.90f, 1f));
            y += 34f;

            CfgDivider(content, y, new Color(0.55f, 0.22f, 0.35f, 0.5f));
            y += 14f;

            

            // ── Image (horse.png, 480×480) ──────────────────────────────
            string imgPath = System.IO.Path.Combine(
                ConsolePlugin.ModDir, "CMS2026SimpleConsole", "horse.png");

            Texture2D horseTex = LoadTextureFromFile(imgPath);

            float imgW = 480f;
            float imgH = 480f;
            float imgX = (PanelW - Pad * 2 - imgW) * 0.5f;   // wyśrodkowanie w content

            var imgEl = VE();
            var imgS = Style(imgEl);
            SPosition(imgS, "Absolute");
            SLeft(imgS, imgX);
            STop(imgS, y);
            SWidth(imgS, imgW);
            SHeight(imgS, imgH);

            if (horseTex != null)
            {
                SBg(imgS, new Color(0f, 0f, 0f, 0f));   // przezroczyste tło pod teksturą
                SetBackgroundImage(imgEl, horseTex);
                AddChild(content, imgEl);
                y += imgH + 12f;
            }
            else
            {
                // Placeholder gdy brak pliku
                SBg(imgS, new Color(0.18f, 0.10f, 0.22f, 1f));
                AddChild(content, imgEl);

                CfgLabel(content, "[horse.png not found]",
                    imgX, y + imgH * 0.45f, imgW, 20f,
                    new Color(0.55f, 0.40f, 0.60f, 1f));
                y += imgH + 12f;
            }

            _heartContentH = y;
        }

        private void ToggleHeart()
        {
            _heartAnimTarget = (_heartAnimTarget > 0.5f) ? 0f : 1f;
            RefreshHeartButtonLabel();

            // Wzajemne wykluczanie — gasim config jeśli jest otwarty
            if (_heartAnimTarget > 0.5f && _animTarget > 0.5f)
            {
                _animTarget = 0f;
                RefreshConfigButtonLabel();
            }
        }

        private void RefreshHeartButtonLabel()
        {
            if (_heartBtnPtr == IntPtr.Zero) return;
            bool showing = _heartAnimTarget > 0.5f;
            var btn = Activator.CreateInstance(_btnType, new object[] { _heartBtnPtr });
            _btnType.GetProperty("text").SetValue(btn, showing ? "✕  Close" : "♥ About");

            SColor(Style(btn), showing
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(1f, 0.55f, 0.70f, 1f));
            SBg(Style(btn), showing
                ? new Color(0.45f, 0.12f, 0.12f, 0.6f)
                : new Color(0f, 0f, 0f, 0f));

            // Szerokość zostaje 72f — nie ruszamy jej tutaj
        }

        // ── Public API ────────────────────────────────────────────────────────────
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_initialized) ApplyDisplay(_panelPtr, visible);
        }

        public void AddLine(string line)
        {
            if (!_initialized) return;

            var content = Wrap(_contentPtr);
            int childCount = (int)_veType.GetProperty("childCount").GetValue(content);

            if (childCount >= MaxLabels)
            {
                RebuildAllLines();
                return;
            }

            AppendLabel(line);
            ScrollToBottom();
        }

        public void ClearLines()
        {
            if (!_initialized) return;
            var content = Wrap(_contentPtr);
            _veType.GetMethod("Clear").Invoke(content, null);
            _currentY = 0f;
            _scrollY = 0f;
        }

        public void OnUpdate()
        {
            if (!_initialized || !_visible) return;
            HandleScroll();
            HandleDrag();
            UpdateConfigAnimation();
            UpdateHeartAnimation();
            UpdateSharedLogViewport();
            ProcessFlashQueue();
        }

        private void ProcessFlashQueue()
        {
            while (_flashQueue.Count > 0 && _flashQueue.Peek().expireAt <= Time.realtimeSinceStartup)
            {
                var (ptr, _) = _flashQueue.Dequeue();
                if (ptr == IntPtr.Zero) continue;
                var lbl = Activator.CreateInstance(_lblType, new object[] { ptr });
                SBg(Style(lbl), new Color(0f, 0f, 0f, 0f));   // przywróć przezroczystość
            }
        }

        public void OnGUI() { }

        public void Destroy()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }

        public void FocusInput()
        {
            if (_textFieldPtr == IntPtr.Zero) return;
            var tf = Activator.CreateInstance(_tfType, new object[] { _textFieldPtr });
            _tfType.GetMethod("Focus")?.Invoke(tf, null);
        }

        // ── Log internals ─────────────────────────────────────────────────────────
        private void AppendLabel(string text)
        {
            float usableW = PanelW - Pad * 4;
            const float CharW = 7.5f;
            int charsPerLine = Mathf.Max(1, Mathf.FloorToInt(usableW / CharW));
            int linesNeeded = Mathf.Max(1, Mathf.CeilToInt((float)text.Length / charsPerLine));
            float labelH = linesNeeded * LineH;

            var lbl = Activator.CreateInstance(_lblType);
            var s = Style(lbl);
            SPosition(s, "Absolute");
            SLeft(s, Pad); STop(s, _currentY);
            SWidth(s, usableW); SHeight(s, labelH);
            SFont(s);
            SColor(s, Color.white);
            _lblType.GetProperty("text").SetValue(lbl, text);

            // ── Strip rich text tags for clipboard ───────────────────────────────────
            string plainText = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
            IntPtr lblPtr = Ptr(lbl);

            // ── Click to copy — przez PointerDownEvent ────────────────────────────────
            try
            {
                var trickleType = _ueAsm.GetType("UnityEngine.UIElements.TrickleDown");
                var pointerDownType = _ueAsm.GetType("UnityEngine.UIElements.PointerDownEvent");

                var regMethod = _veType.GetMethods()
                    .First(m => m.Name == "RegisterCallback"
                             && m.IsGenericMethod
                             && m.GetParameters().Length == 2)
                    .MakeGenericMethod(pointerDownType);

                var callbackType = _ueAsm.GetType("UnityEngine.UIElements.EventCallback`1")
                    .MakeGenericType(pointerDownType);

                Action<UnityEngine.UIElements.PointerDownEvent> handler = _ =>
                {
                    GUIUtility.systemCopyBuffer = plainText;
                    FlashLabel(lblPtr);
                };

                var il2cb = Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityEngine.UIElements.EventCallback<UnityEngine.UIElements.PointerDownEvent>>(handler);

                regMethod.Invoke(lbl, new object[]
                {
            il2cb,
            Enum.Parse(trickleType, "TrickleDown")
                });
            }
            catch (Exception ex) { _log($"[ClickCopy] {ex.Message}"); }

            var content = Wrap(_contentPtr);
            AddChild(content, lbl);
            _currentY += labelH;
        }

        private void FlashLabel(IntPtr lblPtr)
        {
            if (lblPtr == IntPtr.Zero) return;

            // Zapal zielone tło
            var lbl = Activator.CreateInstance(_lblType, new object[] { lblPtr });
            SBg(Style(lbl), new Color(0.15f, 0.45f, 0.15f, 0.7f));

            // Zgaś po 0.4s — przez coroutine-like delayed call
            _flashQueue.Enqueue((lblPtr, Time.realtimeSinceStartup + 0.4f));
        }

        private void RebuildAllLines()
        {
            ClearLines();
            int start = Mathf.Max(0, _logLines.Count - MaxLabels);
            for (int i = start; i < _logLines.Count; i++)
                AppendLabel(_logLines[i]);
            ScrollToBottom();
        }

        private void RebuildForResize()
        {
            if (_rootPtr == IntPtr.Zero) return;
            var root = Activator.CreateInstance(_veType, new object[] { _rootPtr });
            if (_panelPtr != IntPtr.Zero)
            {
                var oldPanel = Activator.CreateInstance(_veType, new object[] { _panelPtr });
                _veType.GetMethod("Remove", new Type[] { _veType })?.Invoke(root, new object[] { oldPanel });
            }
            _currentY = 0f; _scrollY = 0f; _configScrollY = 0f;
            _animProgress = 0f; _animTarget = 0f;
            _cfgToggleBtns.Clear();
            _keybindLabelPtrs.Clear();
            _maxLogValuePtr = IntPtr.Zero;

            _pendingWidthLabelPtr = IntPtr.Zero;   
            _pendingHeightLabelPtr = IntPtr.Zero;

            _pendingOpacityLabelPtr = IntPtr.Zero;

            _panelWidthValuePtr = IntPtr.Zero;
            _panelHeightValuePtr = IntPtr.Zero;
            _configBtnPtr = IntPtr.Zero;
            _lockBtnPtr = IntPtr.Zero;
            _logViewportPtr = IntPtr.Zero;
            _configPanelPtr = IntPtr.Zero;
            _configContentPtr = IntPtr.Zero;
            _textFieldPtr = IntPtr.Zero;

            _heartPanelPtr = IntPtr.Zero;
            _heartContentPtr = IntPtr.Zero;
            _heartBtnPtr = IntPtr.Zero;
            _heartAnimProgress = 0f;
            _heartAnimTarget = 0f;
            _heartScrollY = 0f;

            BuildPanel(root);
            RebuildAllLines();
        }

        private void ScrollToBottom()
        {
            _scrollY = Mathf.Max(0f, _currentY - LogViewH);
            ApplyLogScroll();
        }

        private void ApplyLogScroll()
        {
            var content = Wrap(_contentPtr);
            STop(Style(content), -_scrollY);
        }

        private void ApplyConfigScroll()
        {
            if (_configContentPtr == IntPtr.Zero) return;
            var content = Wrap(_configContentPtr);
            STop(Style(content), -_configScrollY);
        }

        private void HandleScroll()
        {
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f) return;

            float mx = Input.mousePosition.x;
            float muitY = Screen.height - Input.mousePosition.y;
            float vpTop = _panelY + TitleH + Pad;
            float vpLeft = _panelX + Pad;
            float vpRight = vpLeft + PanelW - Pad * 2;
            float vpBottom = vpTop + LogViewH;

            bool inArea = mx >= vpLeft && mx <= vpRight && muitY >= vpTop && muitY <= vpBottom;
            if (!inArea) return;

            if (_heartAnimProgress >= 0.99f)
            {
                float maxScroll = Mathf.Max(0f, _heartContentH - LogViewH);
                _heartScrollY = Mathf.Clamp(_heartScrollY - delta * 40f, 0f, maxScroll);
                ApplyHeartScroll();
            }
            else if (_animProgress >= 0.99f)
            {
                float maxScroll = Mathf.Max(0f, _configContentH - LogViewH);
                _configScrollY = Mathf.Clamp(_configScrollY - delta * 40f, 0f, maxScroll);
                ApplyConfigScroll();
            }
            else
            {
                float maxScroll = Mathf.Max(0f, _currentY - LogViewH);
                _scrollY = Mathf.Clamp(_scrollY - delta * 40f, 0f, maxScroll);
                ApplyLogScroll();
            }
        }

        private void ApplyHeartScroll()
        {
            if (_heartContentPtr == IntPtr.Zero) return;
            STop(Style(Wrap(_heartContentPtr)), -_heartScrollY);
        }

        private Texture2D LoadTextureFromFile(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                _log($"[Heart] File not found: {path}");
                return null;
            }
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                _log($"[Heart] Loaded {bytes.Length} bytes from {System.IO.Path.GetFileName(path)}");

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                // Il2Cpp wymaga Il2CppStructArray<byte> zamiast zwykłego byte[]
                var il2Bytes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(bytes.Length);
                for (int i = 0; i < bytes.Length; i++)
                    il2Bytes[i] = bytes[i];

                var icType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return Array.Empty<Type>(); }
                    })
                    .FirstOrDefault(t => t.FullName == "UnityEngine.ImageConversion");

                if (icType == null)
                {
                    _log("[Heart] ImageConversion type not found.");
                    return null;
                }

                _log($"[Heart] ImageConversion found in: {icType.Assembly.GetName().Name}");

                // Szukamy przeciążenia które przyjmuje Il2CppStructArray<byte>
                var loadImg = icType.GetMethods()
                    .FirstOrDefault(m => m.Name == "LoadImage" && m.GetParameters().Length == 2);

                if (loadImg == null)
                {
                    _log("[Heart] LoadImage method not found.");
                    return null;
                }

                bool ok = (bool)loadImg.Invoke(null, new object[] { tex, il2Bytes });
                _log($"[Heart] LoadImage result: {ok}");
                return ok ? tex : null;
            }
            catch (Exception ex)
            {
                _log($"[Heart] Texture load error: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    _log($"[Heart] Inner: {ex.InnerException.Message}");
                return null;
            }
        }

        private void SetBackgroundImage(object visualElement, Texture2D tex)
        {
            if (tex == null) return;
            try
            {
                // StyleBackground
                var bgType = _ueAsm.GetType("UnityEngine.UIElements.Background");
                var sbgType = _ueAsm.GetType("UnityEngine.UIElements.StyleBackground");

                // Background.FromTexture2D(tex)
                var il2Tex = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(0); // dummy — potrzebujemy wskaźnika
                                                                                                       // Używamy statycznej metody Background.FromTexture2D
                var fromTex = bgType.GetMethod("FromTexture2D",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                // Musimy opakować Texture2D w il2cpp wrapper
                var texType = typeof(Texture2D);
                var il2TexObj = Activator.CreateInstance(texType, new object[] { tex.Pointer });

                var bgValue = fromTex.Invoke(null, new object[] { il2TexObj });

                // StyleBackground ctor(Background)
                var sbgCtor = sbgType.GetConstructor(new Type[] { bgType });
                var sbgValue = sbgCtor.Invoke(new object[] { bgValue });

                var s = Style(visualElement);
                _sType.GetProperty("backgroundImage").SetValue(s, sbgValue);

                // backgroundSize — cover-like: stretch
                var bszType = _ueAsm.GetType("UnityEngine.UIElements.BackgroundSize");
                var sbszType = _ueAsm.GetType("UnityEngine.UIElements.StyleBackgroundSize");
                var bszKind = _ueAsm.GetType("UnityEngine.UIElements.BackgroundSizeType");
                if (bszType != null && sbszType != null && bszKind != null)
                {
                    var contain = Enum.Parse(bszKind, "Contain");
                    var bszCtor = bszType.GetConstructor(new Type[] { bszKind });
                    var bszVal = bszCtor?.Invoke(new object[] { contain });
                    var sbszCtor = sbszType.GetConstructor(new Type[] { bszType });
                    var sbszVal = sbszCtor?.Invoke(new object[] { bszVal });
                    if (sbszVal != null)
                        _sType.GetProperty("backgroundSize")?.SetValue(s, sbszVal);
                }
            }
            catch (Exception ex)
            {
                _log($"[Heart] SetBackgroundImage error: {ex.Message}");
            }
        }



        private void HandleDrag()
        {
            Vector2 mp = Input.mousePosition;
            float uitY = Screen.height - mp.y;

            bool inTitle = mp.x >= _panelX && mp.x <= _panelX + PanelW
                        && uitY >= _panelY && uitY <= _panelY + TitleH;

            if (Input.GetMouseButtonDown(0) && inTitle)
            {
                _dragging = true;
                _dragOffset = new Vector2(mp.x - _panelX, uitY - _panelY);
            }
            if (Input.GetMouseButtonUp(0)) _dragging = false;

            if (_dragging && Input.GetMouseButton(0))
            {
                float uitYNow = Screen.height - Input.mousePosition.y;
                _panelX = Mathf.Clamp(Input.mousePosition.x - _dragOffset.x, 0f, Screen.width - PanelW);
                _panelY = Mathf.Clamp(uitYNow - _dragOffset.y, 0f, Screen.height - PanelH);
                var s = Style(Wrap(_panelPtr));
                SLeft(s, _panelX);
                STop(s, _panelY);
            }
        }

        private void ApplyDisplay(IntPtr ptr, bool show)
        {
            var ve = Wrap(ptr);
            var s = Style(ve);
            var val = Enum.Parse(_displayType, show ? "Flex" : "None");
            _sType.GetProperty("display").SetValue(s, _sdCtor.Invoke(new object[] { val }));
        }

        // ── Config animation ─────────────────────────────────────────────────────
        private void ToggleConfig()
        {
            _animTarget = (_animTarget > 0.5f) ? 0f : 1f;
            RefreshConfigButtonLabel();

            // Wzajemne wykluczanie — gasim heart jeśli jest otwarty
            if (_animTarget > 0.5f && _heartAnimTarget > 0.5f)
            {
                _heartAnimTarget = 0f;
                RefreshHeartButtonLabel();
            }
        }

        private void RefreshConfigButtonLabel()
        {
            if (_configBtnPtr == IntPtr.Zero) return;
            bool showingConfig = _animTarget > 0.5f;
            var btn = Activator.CreateInstance(_btnType, new object[] { _configBtnPtr });
            _btnType.GetProperty("text").SetValue(btn, showingConfig ? "✕  Close" : "🔧  Config");
            var s = Style(btn);
            SBg(s, showingConfig
                ? new Color(0.45f, 0.12f, 0.12f, 1f)
                : new Color(0.15f, 0.38f, 0.28f, 1f));
        }

        private void UpdateConfigAnimation()
        {
            if (Mathf.Abs(_animProgress - _animTarget) < 0.001f) return;

            _animProgress = Mathf.MoveTowards(
                _animProgress, _animTarget, AnimSpeed * Time.deltaTime);

            float t = SmoothStep(_animProgress);

            if (_configPanelPtr != IntPtr.Zero)
            {
                if (_animProgress <= 0.01f)
                {
                    ApplyDisplay(_configPanelPtr, false);
                    _configScrollY = 0f;
                    ApplyConfigScroll();
                }
                else
                {
                    ApplyDisplay(_configPanelPtr, true);
                    if (_animProgress > 0f && _animTarget > 0.5f)
                        RefreshAllConfigToggles();

                    var cv = Wrap(_configPanelPtr);
                    SOpacity(Style(cv), t);
                    STop(Style(cv), (TitleH + Pad) + (1f - t) * 22f);
                }
            }
        }

        private void UpdateHeartAnimation()
        {
            if (Mathf.Abs(_heartAnimProgress - _heartAnimTarget) < 0.001f) return;

            _heartAnimProgress = Mathf.MoveTowards(
                _heartAnimProgress, _heartAnimTarget, AnimSpeed * Time.deltaTime);

            float t = SmoothStep(_heartAnimProgress);

            if (_heartPanelPtr != IntPtr.Zero)
            {
                if (_heartAnimProgress <= 0.01f)
                {
                    ApplyDisplay(_heartPanelPtr, false);
                    _heartScrollY = 0f;
                    ApplyHeartScroll();
                }
                else
                {
                    ApplyDisplay(_heartPanelPtr, true);
                    var hv = Wrap(_heartPanelPtr);
                    SOpacity(Style(hv), t);
                    STop(Style(hv), (TitleH + Pad) + (1f - t) * 22f);
                }
            }
        }


        private void UpdateSharedLogViewport()
        {
            if (_logViewportPtr == IntPtr.Zero) return;

            // Łączony progress — bierzemy ten który bardziej "zakrywa" logi
            float combined = Mathf.Max(_animProgress, _heartAnimProgress);
            float t = SmoothStep(combined);

            if (combined >= 0.99f)
            {
                ApplyDisplay(_logViewportPtr, false);
            }
            else
            {
                ApplyDisplay(_logViewportPtr, true);
                var lv = Wrap(_logViewportPtr);
                SOpacity(Style(lv), 1f - t);
                STop(Style(lv), (TitleH + Pad) - t * 22f);
            }
        }

        private static float SmoothStep(float t) => t * t * (3f - 2f * t);

        // ── Config UI helpers ─────────────────────────────────────────────────────
        private void CfgLabel(object parent, string text,
            float x, float y, float w, float h, Color col, int fontSize = 11)
        {
            var lbl = Activator.CreateInstance(_lblType);
            var s = Style(lbl);
            SPosition(s, "Absolute");
            SLeft(s, x); STop(s, y);
            SWidth(s, w); SHeight(s, h);
            SFont(s);
            SColor(s, col);
            _lblType.GetProperty("text").SetValue(lbl, text);
            AddChild(parent, lbl);
        }

        private void CfgSectionLabel(object parent, string text, float y)
        {
            CfgLabel(parent, text,
                Pad, y, PanelW - Pad * 4, 18f,
                new Color(0.40f, 0.55f, 0.75f, 1f));
        }

        private void CfgDivider(object parent, float y, Color col)
        {
            var div = VE();
            var s = Style(div);
            SPosition(s, "Absolute");
            SLeft(s, Pad); STop(s, y);
            SWidth(s, PanelW - Pad * 6); SHeight(s, 1f);
            SBg(s, col);
            AddChild(parent, div);
        }

        // ── Button helpers ────────────────────────────────────────────────────────
        private void MakeButton(object parent, string label,
            float x, float y, float w, float h, Color bg, Action onClick)
            => MakeButtonWithPtr(parent, label, x, y, w, h, bg, onClick);

        private object MakeButtonWithPtr(object parent, string label,
            float x, float y, float w, float h, Color bg, Action onClick)
        {
            var btn = Activator.CreateInstance(_btnType);
            var s = Style(btn);
            SPosition(s, "Absolute");
            SLeft(s, x); STop(s, y);
            SWidth(s, w); SHeight(s, h);
            SBg(s, bg);
            SColor(s, Color.white);
            SFont(s);

            _sType.GetProperty("unityTextAlign").SetValue(s,
                _staCtor.Invoke(new object[] { UnityEngine.TextAnchor.MiddleCenter }));
            _sType.GetProperty("paddingLeft").SetValue(s, _slCtor.Invoke(new object[] { 0f }));
            _sType.GetProperty("paddingRight").SetValue(s, _slCtor.Invoke(new object[] { 0f }));
            _sType.GetProperty("paddingTop").SetValue(s, _slCtor.Invoke(new object[] { 0f }));
            _sType.GetProperty("paddingBottom").SetValue(s, _slCtor.Invoke(new object[] { 0f }));

            _btnType.GetProperty("text").SetValue(btn, label);

            var clickable = _btnType.GetProperty("clickable").GetValue(btn);
            var il2cppAction = Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<Il2CppSystem.Action>(onClick);
            _clickableType.GetMethod("add_clicked").Invoke(clickable, new object[] { il2cppAction });

            AddChild(parent, btn);
            return btn;
        }

        private void MakeButtonLink(object parent, string label,
            float x, float y, float w, float h, Action onClick)
        {
            var btn = MakeButtonWithPtr(parent, label, x, y, w, h, new Color(0f, 0f, 0f, 0f), onClick);
            SColor(Style(btn), new Color(0.4f, 0.7f, 1f, 1f));
        }

        private void UpdateLockButtonLabel()
        {
            if (_lockBtnPtr == IntPtr.Zero) return;
            var btn = Activator.CreateInstance(_btnType, new object[] { _lockBtnPtr });
            _btnType.GetProperty("text").SetValue(btn,
                _inputLocked ? "Unlock Input" : "Lock Input");
            var s = Style(btn);
            SBg(s, _inputLocked
                ? new Color(0.7f, 0.15f, 0.15f, 1f)
                : new Color(0.15f, 0.45f, 0.15f, 1f));
        }

        // ── Reflection micro-helpers ──────────────────────────────────────────────
        private object VE() => Activator.CreateInstance(_veType);
        private object Wrap(IntPtr p) => Activator.CreateInstance(_veType, new object[] { p });
        private object Style(object ve) => _veType.GetProperty("style").GetValue(ve);
        private IntPtr Ptr(object ve) => ((Il2CppSystem.Object)ve).Pointer;
        private void AddChild(object parent, object child) =>
            _veType.GetMethod("Add", new Type[] { _veType })
                   .Invoke(parent, new object[] { child });

        private void SPosition(object s, string v) =>
            _sType.GetProperty("position").SetValue(s,
                _spCtor.Invoke(new object[] { Enum.Parse(_posType, v) }));
        private void SOverflow(object s, string v) =>
            _sType.GetProperty("overflow").SetValue(s,
                _soCtor.Invoke(new object[] { Enum.Parse(_ofType, v) }));
        private void SLeft(object s, float v) => _sType.GetProperty("left").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void STop(object s, float v) => _sType.GetProperty("top").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void SWidth(object s, float v) => _sType.GetProperty("width").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void SHeight(object s, float v) => _sType.GetProperty("height").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void SBg(object s, Color c) => _sType.GetProperty("backgroundColor").SetValue(s, _scCtor.Invoke(new object[] { c }));
        private void SColor(object s, Color c) => _sType.GetProperty("color").SetValue(s, _scCtor.Invoke(new object[] { c }));
        private void SFont(object s) => _sType.GetProperty("unityFontDefinition").SetValue(s, _sfdCtor.Invoke(new object[] { _fontDef }));
        private void SOpacity(object s, float v) => _sType.GetProperty("opacity").SetValue(s, _sfCtor.Invoke(new object[] { v }));
    }
}