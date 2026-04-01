using System;
using System.Collections.Generic;

namespace CMS2026SimpleConsole
{
    public interface IConsoleRenderer
    {
        bool IsVisible { get; }

        void Initialize();
        void SetVisible(bool visible);

        void AddLine(string line);
        void ClearLines();

        void OnUpdate();
        void OnGUI();

        string CommandInput { get; set; }
        event Action<string> OnCommandSubmitted;

        void Destroy();
    }
}