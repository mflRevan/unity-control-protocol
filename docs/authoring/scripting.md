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

## Hot reload

`ucp compile` is the safe path after an edit, but it costs a full compile and domain reload:
several seconds on a small project, half a minute or more on a large one, and play mode is torn
down. `ucp hot-reload` patches edited method bodies into the running editor instead, in about a
second, in edit mode or play mode, without a domain reload.

```bash
# edit Assets/Scripts/EnemyAI.cs, then:
ucp hot-reload apply Assets/Scripts/EnemyAI.cs
ucp hot-reload apply Assets/Scripts/EnemyAI.cs Assets/Scripts/Spawner.cs   # several files at once
ucp hot-reload status                                                      # what is patched right now
ucp hot-reload revert                                                      # drop every patch
ucp compile                                                                # make the edits permanent
```

`apply` compiles the given files on their own, against the assemblies the editor already has
loaded, with Unity's bundled C# compiler. Every method in the result whose declaring type still
has the same field layout is then detoured onto the new body (a Harmony prefix that runs the new
code and skips the old). Objects keep their state, play mode keeps running, and the next
`Update` already executes the edit.

What hot reload handles:

- method bodies, instance and static, including properties, operators, lambdas, async methods,
  local functions, and iterators
- new private methods, as long as only other patched code calls them
- several files, in several script assemblies, in one call

What still needs `ucp compile` (the response lists these under `needsFullCompile`, with a reason):

- adding, removing, reordering, or retyping instance fields, or adding static fields
- new types, renamed or removed members, changed signatures, generic types and methods
- constructor and field-initializer changes
- edits in files you did not pass, and anything that other, unpatched code must see (a new
  public method called from a file you did not hand to `apply`)

While patches are live, Unity's auto refresh is held so a window focus does not trigger the
recompile that would wipe them; `status` reports `autoRefreshHeld`. `ucp compile` (or
`hot-reload revert`) releases the hold. The patches live in the managed domain, so a domain
reload drops them: when that reload is not a rebuild (entering play mode with domain reload on,
a manual reload), the bridge re-applies them from the same files and logs that it did; when the
reload came from a real compile, the edits are compiled in and the patch list is dropped.

Rules of thumb for an agent loop:

- Iterating on behaviour (tuning a value, fixing a branch, changing what a method does): `apply`.
- Changing shape (fields, types, signatures): `compile`.
- Before handing off, running tests, or building: `compile`, so nothing depends on a patch.
- A failed `apply` compile exits non-zero and prints the `CS####` lines; the editor is unchanged.

Patched code runs from a small side assembly, so stack traces name that assembly, reflection
over a patched type still sees the original members, and `typeof(X).Assembly` of patched code is
not the script assembly. Hot reload needs the Mono scripting backend the Unity editor uses today
and ships with Lib.Harmony 2.2.2 as an editor-only plugin inside the bridge package.

### `ucp script doctor`

Diagnose generated C# project files and stale script references.

```bash
# Report stale .csproj Compile entries
ucp script doctor

# Delete stale generated project files and ask Unity to regenerate them
ucp script doctor --fix
```

Use this after raw filesystem deletes of `.cs` files if Unity or the C# compiler reports `CS2001` for files that no longer exist.
