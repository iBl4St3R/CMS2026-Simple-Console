# CMS 2026 Simple Console — 1.0.0
**A powerful developer tool and C# REPL for Car Mechanic Simulator 2026.**

---

## About this mod
**CMS 2026 Developer Console** is a versatile utility tool designed for modders and power users. It provides a transparent way to look under the hood of the new Unity 6 engine implementation, helping you debug, explore, and experiment with game mechanics without "breaking" the immersion for others.

## Main Feature: C# REPL Runtime
The heart of this mod is the `run` command. It allows you to execute C# code directly during runtime, similar to the Unity Explorer REPL.

* **Live Scripting:** Modify game variables or find components instantly.
* **Unity Integration:** Full access to `UnityEngine` and `Il2CppCMS` namespaces.
* **Example Usage:**
    `run var car = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Core.Car.CarLoaderOnCar>(); if(car != null) Print("Car found: " + car.name); else Print("No car found nearby.");`

---

## Console Commands
Type these commands directly into the console (press **~** or **F7** to toggle):


* **`help`** – Compile and run the C# code.
* **`run <C# code>`** – Compile and run the C# code.
* **`find <name>`** – Search for game objects by name.
* **`inspectobj`** – Displays detailed information about the object under the crosshair.
* **`dumpobj`** – Copies the structure of the object under the crosshair directly to the clipboard.
* **`resetscene`** – Reload garage scene.
* **`scenes`** – List all available game scenes.
* **`addmoney [n]`** – Add money (default: 10000).
* **`setmoney <n>`** – Set money to exact amount.
* **`addexp [n]`** – Add EXP (default: 1000).
* **`charspeed <n>`** – Adjust player walk speed.
* **`fixcar <idx>`** – Repair all parts of a car to 100%.
* **`stealcustomercar <idx>`** – Take ownership of customer car.
* **`showgaragecars`** – List cars currently in the garage.
* **`showparkingcars`** – List cars on the parking lot.
* **`removedemowalls`** – Turn off demo walls / map restrictions.

---

## Installation
1.  Install **MelonLoader 0.6.1+**.
2.  Download the latest release.
3.  Unzip `CMS2026SimpleConsole.dll` into your game's **`Mods`** folder.
    * `Default: \SteamLibrary\steamapps\common\Car Mechanic Simulator 2026 Demo\Mods\`

---

## Known Issues
* Use `inspectobj` only when looking at physical objects; otherwise, it might return "No object detected".
* REPL requires basic knowledge of C# and game's internal namespaces.