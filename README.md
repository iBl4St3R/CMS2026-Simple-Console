# CMS2026-Simple-Console
CMS 2026 Simple Console is a versatile utility tool designed for modders and power users of the Car Mechanic Simulator 2026. It provides a transparent way to look under the hood of the new Unity 6 engine implementation, helping you debug, explore, and experiment with game mechanics.

Main Feature: C# REPL Runtime
The heart of this mod is the run command. It allows you to execute C# code directly during runtime, similar to the Unity Explorer REPL.
Live Scripting: Modify game variables, spawn objects, or find specific components without restarting the game.
Unity Integration: Full access to UnityEngine and game-specific namespaces like Il2CppCMS.
Instant Feedback: Results or errors are printed directly to the console.

Example:
run var car = UnityEngine.Object.FindObjectOfType<Il2CppCMS.Core.Car.CarLoaderOnCar>(); if(car != null) Print("Car found: " + car.name); else Print("No car found nearby.");

Console Commands
The mod includes several built-in commands to streamline your workflow:

run <C# code> – Compile and run the C# code.
resetscene – reload garage scene.
charspeed <n> – player walk speed.
addmoney [n] – add money (default: 10000).
setmoney <n> – set money to exact amount.
addexp [n] – add EXP (default: 1000).
stealcustomercar <idx> – take ownership of customer car.
fixcar <idx> – repair all parts of a car to 100%.
removedemowalls – turn off demo walls.
find <name> – search for game objects by name.
inspectobj – Displays detailed information about the object under the crosshair.
dumpobj – Copies the structure of the object under the crosshair directly to the clipboard.
scenes – scene List.
showgaragecars – list cars in garage.
showparkingcars – list cars on parking.

