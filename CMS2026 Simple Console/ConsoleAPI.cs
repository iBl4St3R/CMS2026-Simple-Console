using System;
using System.Collections.Generic;

namespace CMS2026SimpleConsole
{
    /// <summary>
    /// Public API for other mods to register custom console commands.
    /// 
    /// Option A – direct reference (add CMS2026SimpleConsole.dll to your project):
    ///     ConsoleAPI.RegisterCommand("mymod_hello", "Prints hello", args => { ... });
    ///
    /// Option B – reflection (no reference needed):
    ///     var api = AppDomain.CurrentDomain.GetAssemblies()
    ///         .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty&lt;Type&gt;(); } })
    ///         .FirstOrDefault(t => t.FullName == "CMS2026SimpleConsole.ConsoleAPI");
    ///     api?.GetMethod("RegisterCommand")
    ///         ?.Invoke(null, new object[] { "mymod_hello", "Prints hello", (Action&lt;string[]&gt;)(args => { ... }) });
    /// </summary>
    public static class ConsoleAPI
    {
        // name → (description, handler)
        // handler receives: args[0] = command name, args[1..] = parameters
        private static readonly Dictionary<string, (string Description, Action<string[]> Handler)>
            _commands = new();

        // Fired after a command is registered — ConsoleComponent subscribes to this
        internal static event Action<string, string> OnCommandRegistered;

        /// <summary>
        /// Register a new console command.
        /// </summary>
        /// <param name="name">Command name (lowercase, no spaces). Use a mod prefix, e.g. "moneycheat_add".</param>
        /// <param name="description">Short description shown in 'help'.</param>
        /// <param name="handler">
        ///     Called when the command is executed.
        ///     args[0] is the command name, args[1..] are space-split parameters.
        /// </param>
        /// <returns>True if registered, false if name was already taken.</returns>
        public static bool RegisterCommand(string name, string description, Action<string[]> handler)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null) return false;
            name = name.Trim().ToLowerInvariant();

            if (_commands.ContainsKey(name)) return false;

            _commands[name] = (description ?? "", handler);
            OnCommandRegistered?.Invoke(name, description ?? "");
            return true;
        }

        /// <summary>Unregister a previously registered command.</summary>
        public static bool UnregisterCommand(string name)
        {
            return _commands.Remove(name?.Trim().ToLowerInvariant() ?? "");
        }

        /// <summary>Returns true if a command with this name is registered.</summary>
        public static bool IsRegistered(string name)
            => _commands.ContainsKey(name?.Trim().ToLowerInvariant() ?? "");

        // ── Internal use by DevConsoleComponent ──────────────────────────────────

        internal static bool TryExecute(string name, string[] args, out string error)
        {
            error = null;
            if (!_commands.TryGetValue(name, out var entry))
                return false;

            try { entry.Handler(args); }
            catch (Exception ex) { error = ex.Message; }
            return true;
        }

        internal static IEnumerable<(string Name, string Description)> GetAll()
        {
            foreach (var kv in _commands)
                yield return (kv.Key, kv.Value.Description);
        }
    }
}