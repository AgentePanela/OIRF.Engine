# Localization

ORIF includes a first-class localization system powered by [Project Fluent](https://projectfluent.org/) (via the `Linguini` library). All user-facing strings should go through the localization manager rather than being hard-coded.

---

## ILocalizationManager

`ILocalizationManager` is available via dependency injection:

```csharp
[Dependency] private readonly ILocalizationManager _loc = default!;
```

Or via `IoCManager`:

```csharp
var loc = IoCManager.Resolve<ILocalizationManager>();
```

---

## Getting Strings

```csharp
// Simple string lookup by key
string text = _loc.GetString("player-health-label");

// String with variables
string text = _loc.GetString("enemy-killed",
    ("name", "Goblin"),
    ("score", 150)
);
```

If the key is not found in the current culture, the fallback culture is tried. If still not found, the **key itself** is returned.

---

## File Structure

Locale files use the `.ftl` (Fluent) format. Place them in `Resources/Locale/<culture-code>/`:

```
Resources/
  Locale/
    en-US/
      game.ftl
      ui.ftl
    pt-BR/
      game.ftl
      ui.ftl
```

The culture code must match a valid `CultureInfo` name (e.g. `en-US`, `pt-BR`, `fr-FR`).

---

## Fluent (.ftl) Format

Fluent is a localization system designed to handle grammatical complexity naturally.

### Simple Messages

```fluent
# game.ftl
player-health-label = Health
main-menu-title = My Awesome Game
```

### Messages with Variables

```fluent
enemy-killed = { $name } was defeated! Score: { $score }
welcome-player = Welcome, { $playerName }!
```

### Selectors (Pluralization, etc.)

```fluent
enemies-remaining =
    { $count ->
        [0]    No enemies left!
        [one]  { $count } enemy remaining.
       *[other] { $count } enemies remaining.
    }
```

---

## Setting Up Cultures

Before loading locale files, register the cultures your game supports and set the active one:

```csharp
// Register available cultures
_loc.AddCulture(new CultureInfo("en-US"));
_loc.AddCulture(new CultureInfo("pt-BR"));

// Set the active culture
_loc.SetCulture(new CultureInfo("en-US"));

// Set a fallback culture (used when a key is missing in the active culture)
_loc.SetFallbackCulture(new CultureInfo("en-US"));

// Load all .ftl files from Resources/Locale/
_loc.LoadCulture();
```

> **Note:** Call `LoadCulture()` after the engine's loading phase completes.

---

## Querying Available Cultures

```csharp
List<CultureInfo> available = _loc.GetAvailableCultures();
```

The list is populated automatically from the subfolder names found in `Resources/Locale/`.

---

## Changing Culture at Runtime

```csharp
_loc.SetCulture(new CultureInfo("pt-BR"));
_loc.ReloadCulture();   // rebuilds the Fluent bundles
```

Subscribe to the reload event to update any cached strings in your UI:

```csharp
_loc.OnReloadCulture += () =>
{
    // Refresh displayed text
    myLabel.Text = _loc.GetString("player-health-label");
};
```

---

## Custom Functions

You can register custom Fluent functions to extend message formatting.

```csharp
// Register a global function (available in all cultures)
_loc.AddFunction("UPPERCASE", (args, _) =>
{
    var str = args.FirstOrDefault()?.ToString() ?? string.Empty;
    return (FluentString)str.ToUpperInvariant();
});

// Register a per-culture function
_loc.AddFunction(new CultureInfo("pt-BR"), "PLURAL-BR", MyPluralFunction);
```

Use in `.ftl`:

```fluent
item-name = { UPPERCASE($name) }
```

---

## Supported Variable Types

The engine automatically converts the following C# types to Fluent values:

| C# type | Fluent type |
|---------|-------------|
| `string` | `FluentString` |
| `bool` | `FluentString` (`"true"` / `"false"`) |
| `Enum` | `FluentString` (lowercase enum name) |
| `int`, `float`, `double`, etc. | `FluentNumber` |
| `EntityUid` | `FluentNumber` (the raw ID) |
| `ProtoId` | `FluentString` (the ID string) |
| Other | `FluentString` (calls `.ToString()`) |
