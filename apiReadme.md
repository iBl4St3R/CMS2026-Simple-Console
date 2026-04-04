# CMS2026 Simple Console — Mod Integration Guide

How to register your own commands in the **CMS2026 Simple Console**.

---

## Prerequisites

- MelonLoader v0.7.2 installed
- CMS2026 Simple Console installed by the user
- Your mod targets the same game (*Car Mechanic Simulator 2026*)

---

## Option A — Reflection (recommended)

No reference to `CMS2026SimpleConsole.dll` needed. Your mod will work even if the console is not installed — it simply won't register the commands.

Add this method to your mod class:
```csharp
private void TryRegisterConsoleCommands()
{
    // Find ConsoleAPI without a hard dependency
    var apiType = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
        .FirstOrDefault(t => t.FullName == "CMS2026SimpleConsole.ConsoleAPI");

    if (apiType == null)
    {
        MelonLogger.Msg("[YourMod] CMS2026 Simple Console not found — skipping command registration.");
        return;
    }

    var register = apiType.GetMethod("RegisterCommand");

    // Register your commands here
    register.Invoke(null, new object[]
    {
        "yourmod_hello",                        // command name  (lowercase, use a mod prefix!)
        "Prints a hello message",               // description shown in 'help'
        (Action<string[]>)(args =>              // handler — args[0] = command name, args[1..] = parameters
        {
            MelonLogger.Msg("Hello from YourMod!");
        })
    });

    MelonLogger.Msg("[YourMod] Console commands registered.");
}
```

Then call it after the scene is ready:
```csharp
public override void OnSceneWasInitialized(int buildIndex, string sceneName)
{
    TryRegisterConsoleCommands();
}
```

---

## Option B — Direct reference

Use this if you want IntelliSense and compile-time checks.

**Step 1** — Add `CMS2026SimpleConsole.dll` from the game's `Mods` folder to your project references.

**Step 2** — Add the using at the top of your file:
```csharp
using CMS2026SimpleConsole;
```

**Step 3** — Register your commands:
```csharp
public override void OnInitializeMelon()
{
    ConsoleAPI.RegisterCommand(
        "yourmod_hello",
        "Prints a hello message",
        args =>
        {
            MelonLogger.Msg("Hello from YourMod!");
        });
}
```

---

## Command naming rules

| Rule | Example |
|---|---|
| Always use a mod prefix | `bananacheat_add` not `add` |
| Lowercase only, no spaces | `yourmod_dosomething` |
| Keep it short and descriptive | `yourmod_fixcar` |

---

## Handler reference
```csharp
(Action<string[]>)(args =>
{
    // args[0]  = command name  ("yourmod_hello")
    // args[1]  = first parameter
    // args[2]  = second parameter
    // etc.

    if (args.Length < 2)
    {
        MelonLogger.Msg("Usage: yourmod_hello <parameter>");
        return;
    }

    if (!int.TryParse(args[1], out int value))
    {
        MelonLogger.Msg("Invalid parameter.");
        return;
    }

    // do your thing...
})
```

---

## Tips

- **Option A is preferred** — your mod stays compatible even without the console installed.
- **Unregistering** a command is possible via `UnregisterCommand("yourmod_hello")` (Option B) or the same method name via reflection (Option A).
- Your commands appear automatically in the `help` output under the **Mod commands** section.
- If you register the same command name twice, the second call is silently ignored — always use a unique prefix.