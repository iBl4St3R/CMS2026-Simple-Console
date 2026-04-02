# CMS 2026 Simple Console — v1.0.3
**Developer console and C# REPL for Car Mechanic Simulator 2026**

---

## 📖 Overview
**CMS 2026 Simple Console** is a developer-oriented tool created for modders and advanced users.  
It provides a powerful and transparent way to inspect, debug, and interact with the game built on the Unity 6 engine.

This tool allows you to explore internal systems, experiment with gameplay mechanics, and accelerate development workflows — all without modifying the base game files.

---

## 🚀 Core Feature — C# REPL Runtime
The main feature of this mod is the `run` command, which enables real-time execution of C# code during gameplay.

### Features
- **Live scripting** – Modify game state instantly
- **Full Unity access** – Use `UnityEngine` and `Il2CppCMS` namespaces
- **Runtime inspection** – Quickly locate and analyze objects

### Example
```csharp
run var car = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Core.Car.CarLoaderOnCar>();
if (car != null)
    Print("Car found: " + car.name);
else
    Print("No car found nearby.");
```
## 🖥️ Console Commands
Press **F7** to toggle the console and enter commands:

* `run <C# code>` – Execute C# code at runtime
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

## 📦 Installation
1. Install **MelonLoader 0.7.2**
2. Download the latest release
3. Extract `CMS2026SimpleConsole.dll` into your game's **Mods** folder

**Default path:**
```text
SteamLibrary\steamapps\common\Car Mechanic Simulator 2026 Demo\Mods\
```
---

## ⚠️ Known Issues
* None at the moment

## 📌 Notes
* This tool is intended for development and debugging purposes
* Use responsibly when modifying gameplay