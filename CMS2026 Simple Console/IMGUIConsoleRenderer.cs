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
                "CMS2026 Simple Console  [F7=hide]  [IMGUI fallback]");

            GUI.backgroundColor = orig;
        }

        public void FocusInput()
        {

        }

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

            float btnY = _windowRect.height - 35;

            if (GUI.Button(new Rect(10, btnY, 80, 25), "Clear"))
                OnCommandSubmitted?.Invoke("__clear");

            if (GUI.Button(new Rect(100, btnY, 80, 25), "Help"))
                OnCommandSubmitted?.Invoke("help");

            if (GUI.Button(new Rect(190, btnY, 100, 25), "Copy log"))
                OnCommandSubmitted?.Invoke("__copylog");

            if (GUI.Button(new Rect(300, btnY, 100, 25), "→ UIToolkit"))
                OnCommandSubmitted?.Invoke("__switchrenderer");


            var sigStyle = new GUIStyle(GUI.skin.label);
            sigStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            GUI.Label(new Rect(_windowRect.width - 80, btnY, 110, 25), "by Blaster", sigStyle);

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}