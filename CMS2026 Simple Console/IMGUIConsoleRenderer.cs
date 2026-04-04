using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CMS2026SimpleConsole
{
    public class IMGUIConsoleRenderer : IConsoleRenderer
    {
        private readonly List<string> _logLines;

        private bool _visible = true;
        private Rect _windowRect = new Rect(20f, 20f, 640f, 500f);
        private string _commandInput = "";
        private const int WindowId = 9871;

        public bool IsVisible => _visible;
        public string CommandInput { get => _commandInput; set => _commandInput = value; }
        public event Action<string> OnCommandSubmitted;

        public IMGUIConsoleRenderer(List<string> logLines)
        {
            _logLines = logLines;
        }

        public void Initialize() { }
        public void SetVisible(bool v) => _visible = v;
        public void AddLine(string line) { }
        public void ClearLines() { }
        public void OnUpdate() { }
        public void Destroy() { }

        public void OnGUI()
        {
            if (!_visible) return;

            Color orig = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);

            _windowRect = GUI.Window(WindowId, _windowRect,
                (GUI.WindowFunction)DrawWindow,
                "CMS2026 Simple Console [IMGUI fallback]");

            GUI.backgroundColor = orig;
        }

        public void FocusInput() { }

        private void DrawWindow(int id)
        {
            float logH = _windowRect.height - 100f;
            Rect logR = new Rect(10, 20, _windowRect.width - 20, logH);

            const int show = 15;
            int start = System.Math.Max(0, _logLines.Count - show);
            var sb = new StringBuilder();
            for (int i = start; i < _logLines.Count; i++)
                sb.AppendLine(_logLines[i]);
            GUI.Label(logR, sb.ToString());

            Rect inputR = new Rect(10, _windowRect.height - 70, _windowRect.width - 100, 25);
            Rect sendR = new Rect(_windowRect.width - 85, _windowRect.height - 70, 75, 25);

            _commandInput = GUI.TextField(inputR, _commandInput);

            bool send = GUI.Button(sendR, "Submit")
                || (Event.current.isKey
                    && Event.current.keyCode == KeyCode.Return
                    && _commandInput != "");

            if (send)
            {
                string cmd = _commandInput;
                _commandInput = "";
                Event.current.Use();
                OnCommandSubmitted?.Invoke(cmd);
            }

            // ── Button row ────────────────────────────────────────────────────────
            float btnY = _windowRect.height - 35f;
            float x = 10f;

            if (GUI.Button(new Rect(x, btnY, 68f, 25f), "Clear"))
                OnCommandSubmitted?.Invoke("__clear");
            x += 72f;

            if (GUI.Button(new Rect(x, btnY, 68f, 25f), "Help"))
                OnCommandSubmitted?.Invoke("help");
            x += 72f;

            if (GUI.Button(new Rect(x, btnY, 82f, 25f), "Copy log"))
                OnCommandSubmitted?.Invoke("__copylog");
            x += 86f;

            // Config — opens the mod folder and selects the cfg file
            if (GUI.Button(new Rect(x, btnY, 64f, 25f), "Config"))
                OnCommandSubmitted?.Invoke("__openconfig");
            x += 68f;

            if (GUI.Button(new Rect(x, btnY, 95f, 25f), "→ UIToolkit"))
                OnCommandSubmitted?.Invoke("__switchrenderer");

            // ── Version + author label ────────────────────────────────────────────
            string sigText = $"SC {ConsolePlugin.Version} by Blaster";
            var sigStyle = new GUIStyle(GUI.skin.label);
            sigStyle.normal.textColor = new Color(0.55f, 0.75f, 1f, 1f);
            sigStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(_windowRect.width - 170f, btnY, 160f, 25f), sigText, sigStyle);

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}