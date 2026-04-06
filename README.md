# CMS 2026 Simple Console
**Enhancing navigation, debugging, and mod interoperability for Car Mechanic Simulator 2026**
![version](https://img.shields.io/badge/version-1.2.0-blue)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.2-green)
---

## 📖 Overview
<img src="preview.png" align="right" width="400" alt="Console Preview">
**CMS 2026 Simple Console** is a lightweight yet powerful utility designed to bridge the gap between players, modders, and the game's internal systems. 

Built for the Unity 6 engine, it focuses on:
* **Navigation & Control:** Quickly move through scenes, manage cars, and bypass demo restrictions.
* **Mod Ecosystem:** A centralized **Mods Panel** to track installed modifications and their metadata.
* **Debugging & Inspection:** Real-time object inspection and data dumping to accelerate mod development.
* **Extensibility:** Public API allowing other mods to register custom commands and information.

---

## ✨ Key Features

### 🛠 Mod Management
The new **Mods Panel** automatically detects and lists installed mods. It pulls information like version, author, and documentation links directly from the assemblies.

### ⌨️ Advanced Input
* **Smart Autocomplete:** Press **Tab** to cycle through matching commands based on your current input.
* **Command History:** Use **Up/Down arrows** to navigate through the last 200 executed commands.


---

## 🚀 Optional Feature: C# REPL Runtime
For advanced users and developers, the console supports an optional **C# REPL** (the `run` command). This allows for real-time code execution to modify the game state on the fly.

**Note:** To keep the core mod lightweight, REPL requires manual installation of dependency libraries in the `UserLibs` folder.

---

## 🖥️ Console Commands
Press **F7** to toggle the console:

* **Tab** – Cycle through command suggestions
* **Up/Down Arrows** – Browse command history

* `run <C# code>` – Execute code (Requires Optional REPL Setup)
* `runfile script.cs` – Execute code from file (Requires Optional REPL Setup)
* `help` – Display list of available commands
* `find <name>` – Search for game objects by name
* `inspectobj` – Show detailed info about the object under the crosshair
* `dumpobj` – Copy object structure to clipboard
* `resetscene` – Reload the garage scene
* `scenes` – List all available scenes
* `addmoney [n]` – Add money (default: 10000)
* `setmoney <n>` – Set money to exact amount
* `addexp [n]` – Add EXP (default: 1000)
* `charspeed <n>` – Adjust player movement speed
* `fixcar <idx>` – Repair all car parts to 100%
* `stealcustomercar <idx>` – Take ownership of a customer car
* `showgaragecars` – List cars in the garage
* `showparkingcars` – List cars on the parking lot
* `removedemowalls` – Disable demo map restrictions


---

## 📦 Installation & Update

### 1. Standard Installation
* Install **MelonLoader v0.7.2**.
* Download `CMS2026SimpleConsole.dll` from **[Releases](https://github.com/iBl4St3R/CMS2026-Simple-Console/releases)**.
* Place the file in your game's **`Mods`** folder.

### 2. Enabling REPL Support (Optional Add-on)
* Download the **[Optional REPL Dependencies](https://github.com/iBl4St3R/CMS2026-Simple-Console/releases/tag/Optional_REPL_Dependencies)**.
* Extract all DLLs to the game's **`UserLibs`** folder.
* Enable REPL in the console's in-game settings.

### 3. Clean Update (from v1.1.x or older)
To avoid conflicts with the new modular structure, please:
1. Delete `CMS2026SimpleConsole.dll` and the `CMS2026SimpleConsole` folder from **`Mods`**.
2. **If NOT using REPL**, delete these files from **`UserLibs`**: `Microsoft.CodeAnalysis.CSharp.dll`, `Microsoft.CodeAnalysis.dll`, `System.Collections.Immutable.dll`, `System.Reflection.Metadata.dll`, `System.Runtime.CompilerServices.Unsafe.dll`.
3. Install the new version as usual.

---

## ⚠️ Known Issues
**UI Toolkit not loading:** Windows **Smart App Control** may block MelonLoader's generated files.  
**Solution:** Set Smart App Control to **Off** in Windows Security.

---

## 📄 License
For modding and educational use only. Not affiliated with Red Dot Games.


