# CMS2026 REPL — Kontekst dla AI

## Środowisko
- Silnik: Unity IL2CPP + MelonLoader
- Kompilator: Roslyn (runtime) — skrypt jest wstrzykiwany w klasę `__Repl`
- Skrypt uruchamiasz: `runfile MojSkrypt.cs`
- Dostępne funkcje: `Log("tekst")`, `Print("tekst")`

## Dostępne usingi (wbudowane w template — NIE pisz ich w skrypcie)
System, System.Collections, System.Collections.Generic,
System.Linq, System.Reflection, UnityEngine,
Il2Cpp, Il2CppInterop.Runtime, Il2CppInterop.Runtime.Injection,
Il2CppInterop.Runtime.InteropTypes, Il2CppSystem,
MelonLoader,
Il2CppCMS.Core.Car, Il2CppCMS.Core.Car.Containers,
Il2CppCMS.Player, Il2CppCMS.Shared, Il2CppCMS.Scenes.Loader

## ZAKAZANE wzorce (powodują błąd kompilacji)
```csharp
// ❌ Extension methods IL2CPP nie działają w REPL
val.BoxIl2CppObject()
obj.Cast<T>()
obj.GetIl2CppType()

// ❌ Ten plik DLL nie istnieje
using Il2CppInterop.Runtime.InteropTypes; // OK jako using, ale DLL nie ma osobno
```

## POPRAWNE wzorce
```csharp
// ✅ Box float/int/bool — używaj helpera z template
Box(0.5f)           // zamiast val.BoxIl2CppObject()
IlCast<T>(obj)      // zamiast obj.Cast<T>()

// ✅ Pobieranie typu IL2CPP — zawsze statycznie
var t = Il2CppType.Of<CarPart>();
var field = t.GetField("Condition");
field.SetValue(part, Box(0.95f));

// ✅ FindObjectsOfType
var loaders = UnityEngine.Object.FindObjectsOfType<CarLoader>(true);

// ✅ GetComponent
var dbg = loader.gameObject.GetComponent<CarDebug>();  // CarDebug jest w namespace Il2Cpp

// ✅ Coroutiny
MelonCoroutines.Start(MojIEnumerator());

// ✅ Reflection na metodach gry (gdy brak bezpośredniego dostępu)
var method = loader.GetType().GetMethod("UnloadCar",
    BindingFlags.Public | BindingFlags.Instance);
method?.Invoke(loader, null);

// ✅ Helpery wbudowane w template
var loaders = GetCarLoaders();
var dbg = GetCarDebug(loaders[0]);
```

## Przykład działającego skryptu — naprawa auta
```csharp
var loaders = GetCarLoaders();
foreach (var loader in loaders)
{
    if (string.IsNullOrWhiteSpace(loader.CarID)) continue;
    Log($"Naprawiam: {loader.CarID}");

    var partType = Il2CppType.Of<CarPart>();
    var field = partType.GetField("Condition");

    for (int i = 0; i < loader.carParts.Count; i++)
    {
        var part = loader.carParts[i];
        if (part == null) continue;
        try { field.SetValue(part, Box(1.0f)); } catch { }
    }
    Log("Gotowe!");
}
```

## Przykład działającego skryptu — spawn auta
```csharp
var loaders = GetCarLoaders();
var free = loaders.FirstOrDefault(cl => string.IsNullOrWhiteSpace(cl.CarID));
if (free == null) { Log("Brak wolnych slotów!"); return; }

var dbg = GetCarDebug(free);
if (dbg == null) { Log("Brak CarDebug!"); return; }

dbg.LoadCar("car_mayenm5", 1);
Log("Spawn wysłany — poczekaj chwilę.");
```

## Kluczowe klasy i ich lokalizacja
| Klasa | Namespace | Opis |
|---|---|---|
| `CarLoader` | `Il2CppCMS.Core.Car` | Główny komponent auta w scenie |
| `CarPart` | `Il2CppCMS.Core.Car.Containers` | Część auta (ma pole `Condition`) |
| `CarDebug` | `Il2Cpp` | Helper do Load/Unload aut |
| `SharedGameDataManager` | `Il2CppCMS.Shared` | Pieniądze: `.money`, `.AddMoneyRpc(n)` |
| `PlayerData` | `Il2CppCMS.Player` | EXP/level: `PlayerData.AddPlayerExp(n, true)` |
| `SceneLoader` | `Il2CppCMS.Scenes.Loader` | Ładowanie scen |