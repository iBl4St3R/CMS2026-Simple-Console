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
        private Type _veType;
        private Type _lblType;
        private Type _btnType;
        private Type _clickableType;
        private Type _sType;
        private Type _slType;
        private Type _scType;
        private Type _posType;
        private Type _spType;
        private Type _ofType;
        private Type _soType;
        private Type _fontDefType;
        private Type _sfdType;
        private Type _displayType;
        private Type _sdType;

        private Type _alignType;
        private Type _saType;
        private Type _justifyType;
        private Type _sjType;

        private Type _taType;   // UnityEngine.TextAnchor
        private Type _staType;  // StyleEnum<TextAnchor>
        private ConstructorInfo _staCtor;

        private Type _tfType;
        private IntPtr _textFieldPtr;

        // ── Constructors ────────────────────────────────────────────────────────
        private ConstructorInfo _slCtor;
        private ConstructorInfo _scCtor;
        private ConstructorInfo _spCtor;
        private ConstructorInfo _soCtor;
        private ConstructorInfo _sfdCtor;
        private ConstructorInfo _sdCtor;

        private ConstructorInfo _saCtor;
        private ConstructorInfo _sjCtor;

        // ── Font ────────────────────────────────────────────────────────────────
        private object _fontDef;

        // ── Layout ──────────────────────────────────────────────────────────────
        private const float PanelW = 660f;
        private const float PanelH = 500f;
        private const float TitleH = 24f;
        private const float LogViewH = 360f;
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
        private IntPtr _inputLblPtr;
        private GameObject _go;

        // ── State ───────────────────────────────────────────────────────────────
        private bool _initialized;
        private bool _initFailed;       // trwały błąd — nie próbuj więcej
        private bool _visible = true;
        private string _commandInput = "";
        private float _scrollY;
        private float _currentY;
        private bool _dragging;
        private Vector2 _dragOffset;

        private IntPtr _psPtr;

        public bool InitFailed => _initFailed;

        // ── Interface ────────────────────────────────────────────────────────────
        public bool IsVisible => _visible;
        public string CommandInput { get => _commandInput; set => _commandInput = value; }
        public event Action<string> OnCommandSubmitted;

        private bool _inputLocked = false;
        private IntPtr _lockBtnPtr;

        // ────────────────────────────────────────────────────────────────────────
        public UIToolkitConsoleRenderer(Action<string> log, List<string> logLines)
        {
            _log = log;
            _logLines = logLines;
        }

        // ── Initialize ───────────────────────────────────────────────────────────
        public void Initialize()
        {
            //_ueAsm = AppDomain.CurrentDomain.GetAssemblies()
            //    .First(a => a.GetName().Name == "UnityEngine.UIElementsModule");
            //_trAsm = AppDomain.CurrentDomain.GetAssemblies()
            //    .First(a => a.GetName().Name == "UnityEngine.TextRenderingModule");

            //ResolveTypes();
            //ResolveCtors();
            //SetupFont();
            //BuildUI();

            //_initialized = true;
            //_log("[UIToolkit] Renderer zainicjalizowany OK");
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
            _spType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1")
                             .MakeGenericType(_posType);

            _ofType = _ueAsm.GetType("UnityEngine.UIElements.Overflow");
            _soType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1")
                             .MakeGenericType(_ofType);

            _displayType = _ueAsm.GetType("UnityEngine.UIElements.DisplayStyle");
            _sdType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1")
                                 .MakeGenericType(_displayType);

            _alignType = _ueAsm.GetType("UnityEngine.UIElements.Align");
            _saType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_alignType);
            _justifyType = _ueAsm.GetType("UnityEngine.UIElements.Justify");
            _sjType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1").MakeGenericType(_justifyType);

            // FIX: TextAnchor dla unityTextAlign
            _taType = typeof(UnityEngine.TextAnchor);   // z Il2Cpp interop
            _staType = _ueAsm.GetType("UnityEngine.UIElements.StyleEnum`1")
                             .MakeGenericType(_taType);
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

            // FIX
            _staCtor = _staType.GetConstructor(new Type[] { _taType });
        }

        // ── Font ─────────────────────────────────────────────────────────────────
        private void SetupFont()
        {
            var fontType = _trAsm.GetType("UnityEngine.Font");
            var builtinFont = Resources.GetBuiltinResource(
                Il2CppInterop.Runtime.Il2CppType.From(fontType), "LegacyRuntime.ttf");
            var fontWrapped = Activator.CreateInstance(fontType,
                new object[] { builtinFont.Pointer });
            _fontDef = _fontDefType.GetMethod("FromFont")
                .Invoke(null, new object[] { fontWrapped });
        }

        // ── Build UI ─────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            var psType = _ueAsm.GetType("UnityEngine.UIElements.PanelSettings");
            var docType = _ueAsm.GetType("UnityEngine.UIElements.UIDocument");
            var smType = _ueAsm.GetType("UnityEngine.UIElements.PanelScaleMode");

            // NOWE — własny PanelSettings zamiast kraść od gry
            var il2cppPsType = Il2CppInterop.Runtime.Il2CppType.From(psType);
            var psRaw = UnityEngine.ScriptableObject.CreateInstance(il2cppPsType);
            var psWrap = Activator.CreateInstance(psType, new object[] { psRaw.Pointer });
            _psPtr = psRaw.Pointer;

            psType.GetProperty("scaleMode").SetValue(psWrap,
                Enum.Parse(smType, "ConstantPixelSize"));
            psType.GetProperty("scale").SetValue(psWrap, 1.0f);
            psType.GetProperty("sortingOrder").SetValue(psWrap, 9999);

            _go = new GameObject("CMS_UIToolkitConsoleRenderer");
            UnityEngine.Object.DontDestroyOnLoad(_go);

            var docRaw = _go.AddComponent(Il2CppInterop.Runtime.Il2CppType.From(docType));
            var docWrap = Activator.CreateInstance(docType,
                new object[] { ((Component)docRaw).Pointer });
            docType.GetProperty("panelSettings").SetValue(docWrap, psWrap);

            var root = docType.GetProperty("rootVisualElement").GetValue(docWrap);
            BuildPanel(root);
        }

        private void BuildPanel(object root)
        {
            var panel = VE();
            var panelStyle = Style(panel);
            SPosition(panelStyle, "Absolute");
            SLeft(panelStyle, _panelX);
            STop(panelStyle, _panelY);
            SWidth(panelStyle, PanelW);
            SHeight(panelStyle, PanelH);
            SBg(panelStyle, new Color(0.08f, 0.08f, 0.1f, 0.93f));
            SOverflow(panelStyle, "Hidden");
            AddChild(root, panel);
            _panelPtr = Ptr(panel);

            BuildTitleBar(panel);
            BuildLogArea(panel);
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
                "  CMS2026 Simple Console  [F7=hide]  |  drag: tytul");
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
            SLeft(s, Pad);
            STop(s, rowTop);
            SWidth(s, PanelW - 110f);
            SHeight(s, InputH);
            SBg(s, new Color(0.04f, 0.04f, 0.07f, 1f));
            SColor(s, new Color(0.85f, 1f, 0.85f, 1f));
            SFont(s);

            AddChild(panel, tf);
            _textFieldPtr = Ptr(tf);

            RegisterSubmitCallback(_textFieldPtr);

            MakeButton(panel, "Wyślij",
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

            MakeButton(panel, "→ IMGUI",
                Pad + 262f, rowTop, 90f, BtnBarH,
                new Color(0.3f, 0.2f, 0.45f, 1f),
                () => OnCommandSubmitted?.Invoke("__switchrenderer"));

            var lockBtn = MakeButtonWithPtr(panel, "Lock Input",
    Pad + 356f, rowTop, 100f, BtnBarH,
    new Color(0.15f, 0.45f, 0.15f, 1f),
    () =>
    {
        _inputLocked = !_inputLocked;
        UpdateLockButtonLabel();
        OnCommandSubmitted?.Invoke("__lockinput");
    });
            _lockBtnPtr = Ptr(lockBtn);
        }

        private void BuildSignature(object panel)
        {
            float rowTop = TitleH + Pad + LogViewH + Pad + InputH + Pad;

            var sig = Activator.CreateInstance(_lblType);
            var s = Style(sig);
            SPosition(s, "Absolute");
            SLeft(s, PanelW - 82f);
            STop(s, rowTop);
            SWidth(s, 78f);
            SHeight(s, BtnBarH);
            SColor(s, new Color(0.6f, 0.6f, 0.6f, 1f));
            SFont(s);
            _lblType.GetProperty("text").SetValue(sig, "by Blaster");
            AddChild(Wrap(_panelPtr), sig);
        }


        private void MakeButton(object parent,
    string label, float x, float y, float w, float h,
    Color bg, Action onClick)
        {
            MakeButtonWithPtr(parent, label, x, y, w, h, bg, onClick);
        }

        // ── Public API ───────────────────────────────────────────────────────────
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_initialized) ApplyDisplay(_panelPtr, visible);
        }

        public void AddLine(string line)
        {
            if (!_initialized) return;

            // Zamiast _lineCount używamy liczby dzieci w content
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
            _currentY = 0f;   // BYŁO: _lineCount = 0
            _scrollY = 0f;
        }

        public void OnUpdate()
        {
            if (!_initialized || !_visible) return;
            HandleScroll();
            HandleKeyboard();
            HandleDrag();
        }

        public void OnGUI() { }

        public void Destroy()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }

        public bool TryInit()
        {
            try
            {
                // Krok 1 — szukaj assembly
                var allAsm = AppDomain.CurrentDomain.GetAssemblies();
                _log($"[UIToolkit] Załadowane assemblies: {allAsm.Length}");

                _ueAsm = allAsm.FirstOrDefault(a => a.GetName().Name == "UnityEngine.UIElementsModule");
                _trAsm = allAsm.FirstOrDefault(a => a.GetName().Name == "UnityEngine.TextRenderingModule");

                if (_ueAsm == null) { _log("[UIToolkit] BRAK: UnityEngine.UIElementsModule"); return false; }
                if (_trAsm == null) { _log("[UIToolkit] BRAK: UnityEngine.TextRenderingModule"); return false; }
                _log($"[UIToolkit] ASM OK: {_ueAsm.GetName().Name}, {_trAsm.GetName().Name}");

                // Krok 2 — pobierz typ PanelSettings
                var psType = _ueAsm.GetType("UnityEngine.UIElements.PanelSettings");
                if (psType == null) { _log("[UIToolkit] BRAK typu: PanelSettings"); return false; }
                _log($"[UIToolkit] psType OK: {psType.FullName}");

                // Krok 3 — konwersja na Il2CppType
                var il2cppPsType = (Il2CppSystem.Type)null;
                try
                {
                    il2cppPsType = Il2CppInterop.Runtime.Il2CppType.From(psType);
                    _log($"[UIToolkit] Il2CppType OK: {il2cppPsType}");
                }
                catch (Exception ex)
                {
                    _log($"[UIToolkit] Il2CppType.From failed: {ex.Message}");
                    return false;
                }

                // Krok 4 — FindObjectsOfTypeAll
                var allPS = Resources.FindObjectsOfTypeAll(il2cppPsType);
                _log($"[UIToolkit] PanelSettings znalezione: {allPS.Length}");

                // Krok 4 — już nie potrzebujemy FindObjectsOfTypeAll
                // przejdź od razu do init
                // Krok 5 — reszta init
                ResolveTypes();
                ResolveCtors();
                SetupFont();
                BuildUI();

                _initialized = true;
                RebuildAllLines();
                _log("[UIToolkit] Renderer zainicjalizowany OK");
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

        // ── Internals ────────────────────────────────────────────────────────────
        private void AppendLabel(string text)
        {
            // Ile pikseli szerokości mamy na tekst
            float usableW = PanelW - Pad * 4;

            // Przybliżona szerokość znaku dla LegacyRuntime.ttf przy domyślnym rozmiarze
            // ~7.5px — możesz to skalibrować eval-em jeśli trzeba
            const float CharW = 7.5f;
            int charsPerLine = Mathf.Max(1, Mathf.FloorToInt(usableW / CharW));

            int linesNeeded = Mathf.Max(1, Mathf.CeilToInt((float)text.Length / charsPerLine));
            float labelH = linesNeeded * LineH;

            var lbl = Activator.CreateInstance(_lblType);
            var s = Style(lbl);
            SPosition(s, "Absolute");
            SLeft(s, Pad);
            STop(s, _currentY);
            SWidth(s, usableW);
            SHeight(s, labelH);
            SFont(s);
            SColor(s, Color.white);
            _lblType.GetProperty("text").SetValue(lbl, text);

            var content = Wrap(_contentPtr);
            AddChild(content, lbl);

            _currentY += labelH;
        }

        private void RebuildAllLines()
        {
            ClearLines();
            int start = Mathf.Max(0, _logLines.Count - MaxLabels);
            for (int i = start; i < _logLines.Count; i++)
                AppendLabel(_logLines[i]);   // bez indeksu
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            _scrollY = Mathf.Max(0f, _currentY - LogViewH);
            ApplyScroll();
        }

        private void ApplyScroll()
        {
            var content = Wrap(_contentPtr);
            var s = Style(content);
            STop(s, -_scrollY);
        }

        private void HandleScroll()
        {
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f) return;
            float maxScroll = Mathf.Max(0f, _currentY - LogViewH);
            _scrollY = Mathf.Clamp(_scrollY - delta * 40f, 0f, maxScroll);
            ApplyScroll();
        }

        private void HandleKeyboard()
        {


        }


        private void RefreshInputLabel()
        {

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
                _panelX = Mathf.Clamp(Input.mousePosition.x - _dragOffset.x,
                    0f, Screen.width - PanelW);
                _panelY = Mathf.Clamp(uitYNow - _dragOffset.y,
                    0f, Screen.height - PanelH);
                var panel = Wrap(_panelPtr);
                var s = Style(panel);
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

        private object MakeButtonWithPtr(object parent,string label, float x, float y, float w, float h,Color bg, Action onClick)
        {
            var btn = Activator.CreateInstance(_btnType);
            var s = Style(btn);
            SPosition(s, "Absolute");
            SLeft(s, x); STop(s, y);
            SWidth(s, w); SHeight(s, h);
            SBg(s, bg);
            SColor(s, Color.white);
            SFont(s);
            // ── centrowanie tekstu ──────────────────────────────────────────────────

            _sType.GetProperty("unityTextAlign").SetValue(s,_staCtor.Invoke(new object[] { UnityEngine.TextAnchor.MiddleCenter }));

            // Padding zero 
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

        // ── Reflection micro-helpers ─────────────────────────────────────────────
        private object VE() => Activator.CreateInstance(_veType);
        private object Wrap(IntPtr p) =>
            Activator.CreateInstance(_veType, new object[] { p });
        private object Style(object ve) =>
            _veType.GetProperty("style").GetValue(ve);
        private IntPtr Ptr(object ve) =>
            ((Il2CppSystem.Object)ve).Pointer;
        private void AddChild(object parent, object child) =>
            _veType.GetMethod("Add", new Type[] { _veType })
                   .Invoke(parent, new object[] { child });

        private void SPosition(object s, string v) =>
            _sType.GetProperty("position").SetValue(s,
                _spCtor.Invoke(new object[] { Enum.Parse(_posType, v) }));
        private void SOverflow(object s, string v) =>
            _sType.GetProperty("overflow").SetValue(s,
                _soCtor.Invoke(new object[] { Enum.Parse(_ofType, v) }));
        private void SLeft(object s, float v) =>
            _sType.GetProperty("left").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void STop(object s, float v) =>
            _sType.GetProperty("top").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void SWidth(object s, float v) =>
            _sType.GetProperty("width").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void SHeight(object s, float v) =>
            _sType.GetProperty("height").SetValue(s, _slCtor.Invoke(new object[] { v }));
        private void SBg(object s, Color c) =>
            _sType.GetProperty("backgroundColor").SetValue(s,
                _scCtor.Invoke(new object[] { c }));
        private void SColor(object s, Color c) =>
            _sType.GetProperty("color").SetValue(s,
                _scCtor.Invoke(new object[] { c }));
        private void SFont(object s) =>
            _sType.GetProperty("unityFontDefinition").SetValue(s,
                _sfdCtor.Invoke(new object[] { _fontDef }));
    }
}