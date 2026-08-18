# `IUCPScript` — gap analysis and long-term direction

*Audit date: 2026-08-18. Scope: the `ucp exec` extension point — what it is good for today, where it
breaks down, and which of the available fixes are worth building.*

---

## 1. What it is

`IUCPScript` is UCP's escape hatch. When no built-in RPC covers a task, you implement the interface
in an Editor assembly and call it by name:

```csharp
public interface IUCPScript
{
    string Name { get; }
    string Description { get; }
    object Execute(string paramsJson);   // main thread; return value is serialised to JSON
}
```

```console
$ ucp exec list
$ ucp exec run validate-scene --params '{"strict":true}'
```

It is the right shape for **durable, project-specific automation** — a validation pass, a build
pre-step, a bespoke asset migration — that lives in version control next to the project it serves.

## 2. Where it breaks down

### 2.1 The iteration loop is a domain reload

This is the defining limitation. Every change to a script — including a typo in a log line — costs a
recompile and a domain reload: single-digit seconds on a small project, tens of seconds on a real
one. Worse, the reload invalidates every `instanceId` the agent is holding, so the surrounding
workflow has to re-snapshot afterwards.

The practical effect is that agents do not reach for `exec` when they hit a gap. They reach for
whatever they *can* do in one round trip — often hand-editing `.unity`/`.prefab` YAML, which is
exactly the failure mode the tool exists to prevent.

Unity's `com.unity.pipeline` answers this with `eval` / `eval_file`: Roslyn-compiled C# executed in
the live Editor with **no recompile and no domain reload**. That is the single biggest capability
difference between the two tools today.

### 2.2 No parameter schema

`Execute(string paramsJson)` is an opaque string. `exec/list` returns `{name, description}` and
nothing else, so a caller cannot discover that `validate-scene` accepts `strict`, or that it is a
boolean, or that it is required. Compare `unity list`, which reports every command's full parameter
schema, or UCP's own typed CLI surface, where `--help` is authoritative.

In practice the description string becomes an informal schema, which is the worst of both worlds:
unparseable by tools and unenforced at the call site.

### 2.3 No result contract

`Execute` returns `object`. Whatever comes back is reflected into JSON. The caller cannot know the
shape in advance, and there is no distinction between "succeeded with this payload" and "failed with
this reason" — a script signals failure by throwing, which surfaces as a transport-level RPC error
rather than a structured result.

This is also how the 0.6.1 serializer crash reached users: `Execute` returning `new { pos = Vector3.zero }`
was enough to take the Editor down. The serializer is hardened now, but the underlying looseness —
"return literally anything and hope" — remains.

### 2.4 Discovery instantiated every script (fixed in 0.6.1)

`exec/run` used to build the full list of scripts, constructing **every** implementation in the
domain, then linearly search it for the one requested. Any constructor side effect ran on every
invocation of any script.

Fixed in this release: the type scan is cached for the app domain's lifetime and restricted to
assemblies that actually reference the bridge assembly (checked via cheap reference metadata rather
than materialising every type in every framework assembly), and name resolution stops at the first
match instead of constructing the rest. Both are strict improvements, but neither addresses §2.1–2.3.

### 2.5 Everything is synchronous

`Execute` runs on the main thread and the caller blocks until it returns. A script that imports a
few hundred assets, or bakes, or waits on a build, freezes the Editor and holds the CLI open. There
is no handle to poll, no way to detach, and no way to inspect a partial result.

## 3. Options

### A. `ucp eval` — Roslyn-compiled C# in the live Editor · **recommended, M**

Compile a snippet against the loaded assemblies into a collectible `AssemblyLoadContext`, invoke it
on the main thread, serialise the return value, unload.

```console
$ ucp eval "return Selection.activeGameObject?.name;"
$ ucp eval --file migration.cs
```

- Removes the domain-reload tax entirely; iteration becomes a single round trip.
- Turns every missing RPC into a one-liner, which is how agents recover from surface gaps today —
  except correctly, through Unity's APIs, instead of by editing YAML.
- Reframes `IUCPScript` as the *persistence* path (promote a snippet that proved useful into a
  committed script) rather than the only extension path. The two are complementary.
- Prerequisite, now satisfied: the 0.6.1 `MiniJson` hardening. An `eval` returning a `Vector3` was
  precisely the payload that crashed the Editor.
- **Security posture:** this is arbitrary local code execution. It is no more privileged than the
  Editor already is, and the bridge is localhost-only behind a per-session token — but it should be
  documented plainly, and worth considering a `--allow-eval` opt-in in the bridge settings for
  users who want the surface off entirely.

### B. Attribute-declared metadata and schema · **recommended, S**

```csharp
[UCPScript("validate-scene", "Check the active scene against quality rules")]
public static class ValidateScene
{
    [UCPParam("strict", "Fail on warnings too", Required = false)]
    public static bool Strict;

    public static ValidationReport Execute() { ... }
}
```

Three wins for one change:

1. **Name and description become readable without constructing the type**, which removes the last
   reason `exec/run` must instantiate candidates at all (§2.4).
2. `exec/list` can publish a real parameter schema (§2.2), so an agent discovers arguments instead of
   guessing them from prose.
3. Parameters can be bound and validated before `Execute` runs, so a typo fails with
   "unknown parameter `strct`" instead of being silently ignored inside the script.

Keep `IUCPScript` working unchanged — this is an additive discovery path, not a migration.

### C. Structured results · **S**

Let a script optionally return `UCPScriptResult { bool Success; object Data; string[] Errors; }`
(with bare returns still treated as `Data`). Gives callers a success flag that is not "did an
exception escape", and gives the CLI something to derive an exit code from — which lines up with the
exit-code taxonomy proposed in
[unity-cli-competitive-analysis.md](unity-cli-competitive-analysis.md) §P1.1.

### D. Job handles for long-running work · **M**

The general answer to §2.5, and to the same problem in `build start`, `profiler capture`, and large
`references index` runs:

```console
$ ucp exec run bake-lighting --detach      # -> {"job":"j_7f3a"}
$ ucp job status j_7f3a                    # -> running, 42%, last message
$ ucp job result j_7f3a                    # -> the payload, once complete
$ ucp job cancel j_7f3a
```

Bridge-side this is a job table plus a cooperative `IUCPProgress` handed to the script. It is the
piece that makes UCP usable for work measured in minutes rather than milliseconds, and it should be
one mechanism shared across every long-running command rather than per-command flags.

### E. Rejected: file-watching hot reload

Watching `Assets/` and reloading only the script assembly sounds like it avoids the domain reload,
but Unity owns assembly loading and a partial reload desynchronises serialized state. Option A gets
the same benefit inside a boundary Unity actually supports.

## 4. Recommendation

1. **A + B together.** `eval` covers the throwaway case, attributes cover the durable case, and
   together they remove both the iteration tax and the discovery blindness. B is small and makes A's
   "promote a snippet into a committed script" story land properly.
2. **C** alongside B — same release, same authoring surface, trivial once attributes exist.
3. **D** when long-running work becomes a stated goal; scope it across all long commands at once,
   not just `exec`.

Until then, `IUCPScript` remains correct for committed automation and unattractive for exploration —
and agents will keep routing around it. That is the gap worth closing.
