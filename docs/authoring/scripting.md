# Scripting

Execute custom C# scripts in the Unity Editor remotely. UCP provides a Playwright-like scripting system where you define `IUCPScript` classes in your project and run them from the CLI with parameters.

## Commands

### `ucp exec list`

List all available UCP scripts in the project.

```bash
ucp exec list
```

The list includes each script's `Name` and `Description`, so agents can discover callable editor automation without grepping the project for `IUCPScript`.

### `ucp exec run <name>`

Execute a named script with optional JSON parameters.

```bash
# Run a script
ucp exec run SetupScene

# Run with parameters
ucp exec run CreatePrefabs --params '{"count": 10, "prefix": "Enemy"}'
```

| Flag              | Description                           |
| ----------------- | ------------------------------------- |
| `--params <json>` | JSON parameters to pass to the script |

## Writing Scripts

Create a C# class implementing `IUCPScript` in your project:

```csharp
using UCP.Bridge;
using UnityEditor;
using UnityEngine;

public class SetupScene : IUCPScript
{
    public string Name => "SetupScene";
    public string Description => "Create a simple starter scene";

    public object Execute(string paramsJson)
    {
        // Create a ground plane
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10, 1, 10);

        // Create a light
        var lightObj = new GameObject("Main Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;

        return new { objectsCreated = 2 };
    }
}
```

Scripts are discovered automatically and can be executed remotely by name. `ucp compile` now performs a synchronous asset refresh before requesting compilation, which covers the common raw-file workflow: write a new `.cs` file, run `ucp compile`, then immediately call it with `ucp exec run`.

### `ucp script doctor`

Diagnose generated C# project files and stale script references.

```bash
# Report stale .csproj Compile entries
ucp script doctor

# Delete stale generated project files and ask Unity to regenerate them
ucp script doctor --fix
```

Use this after raw filesystem deletes of `.cs` files if Unity or the C# compiler reports `CS2001` for files that no longer exist.
