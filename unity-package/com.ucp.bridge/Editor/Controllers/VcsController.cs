using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Version control operations via UnityEditor.VersionControl (Unity VCS / Plastic SCM).
    /// All commands go through the Editor's VC provider — the active provider must be configured
    /// in Unity's Project Settings > Version Control.
    /// </summary>
    public static class VcsController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("vcs/info", HandleInfo);
            router.Register("vcs/status", HandleStatus);
            router.Register("vcs/checkout", HandleCheckout);
            router.Register("vcs/revert", HandleRevert);
            router.Register("vcs/commit", HandleCommit);
            router.Register("vcs/diff", HandleDiff);
            router.Register("vcs/incoming", HandleIncoming);
            router.Register("vcs/update", HandleUpdate);
            router.Register("vcs/branches", HandleBranches);
            router.Register("vcs/lock", HandleLock);
            router.Register("vcs/unlock", HandleUnlock);
            router.Register("vcs/history", HandleHistory);
            router.Register("vcs/resolve", HandleResolve);
        }

        // ── Helpers ──────────────────────────────────────────────

        private static void EnsureVcsActive()
        {
            if (!Provider.enabled || !Provider.isActive)
                throw new InvalidOperationException(
                    "Version control is not active. Enable it in Project Settings > Version Control.");
        }

        private static bool WaitForTask(UnityEditor.VersionControl.Task task, int timeoutMs = 30000)
        {
            if (task == null) return false;
            task.Wait();
            return task.success;
        }

        private static List<string> ParsePaths(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null) return new List<string>();

            if (p.TryGetValue("paths", out var pathsObj) && pathsObj is List<object> list)
                return list.Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToList();

            if (p.TryGetValue("path", out var pathObj) && pathObj != null)
            {
                var s = pathObj.ToString();
                if (!string.IsNullOrEmpty(s)) return new List<string> { s };
            }

            return new List<string>();
        }

        private static AssetList ToAssetList(IEnumerable<string> paths)
        {
            var list = new AssetList();
            foreach (var path in paths)
            {
                var asset = Provider.GetAssetByPath(path);
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static Dictionary<string, object> AssetToDict(Asset asset)
        {
            return new Dictionary<string, object>
            {
                ["path"] = asset.path ?? "",
                ["state"] = asset.state.ToString(),
                ["isFolder"] = asset.isFolder,
                ["name"] = asset.name ?? "",
                ["locked"] = asset.IsState(Asset.States.LockedLocal) || asset.IsState(Asset.States.LockedRemote),
                ["lockedLocal"] = asset.IsState(Asset.States.LockedLocal),
                ["lockedRemote"] = asset.IsState(Asset.States.LockedRemote),
            };
        }

        // ── Handlers ─────────────────────────────────────────────

        private static object HandleInfo(string paramsJson)
        {
            EnsureVcsActive();

            // Return plugin and workspace info
            var plugin = Provider.GetActivePlugin();

            return new Dictionary<string, object>
            {
                ["enabled"] = Provider.enabled,
                ["active"] = Provider.isActive,
                ["onlineState"] = Provider.onlineState.ToString(),
                ["offlineReason"] = Provider.offlineReason,
                ["plugin"] = plugin?.name ?? "unknown",
            };
        }

        private static object HandleStatus(string paramsJson)
        {
            EnsureVcsActive();

            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var paths = ParsePaths(paramsJson);
            bool all = paths.Count == 0;

            // If specific paths, query those; otherwise query all changes under Assets
            UnityEditor.VersionControl.Task statusTask;
            if (all)
                statusTask = Provider.Status("Assets", true);
            else
                statusTask = Provider.Status(ToAssetList(paths), true);

            WaitForTask(statusTask);

            var items = new List<object>();
            if (statusTask.assetList != null)
            {
                foreach (var asset in statusTask.assetList)
                {
                    // Skip clean assets (no pending state) when showing full status
                    if (all && !HasPendingState(asset)) continue;
                    items.Add(AssetToDict(asset));
                }
            }

            return new Dictionary<string, object>
            {
                ["count"] = items.Count,
                ["assets"] = items,
            };
        }

        private static bool HasPendingState(Asset a)
        {
            var states = a.state;
            return (states & (Asset.States.CheckedOutLocal | Asset.States.CheckedOutRemote
                    | Asset.States.AddedLocal | Asset.States.AddedRemote
                    | Asset.States.DeletedLocal | Asset.States.DeletedRemote
                    | Asset.States.LockedLocal | Asset.States.LockedRemote
                    | Asset.States.Conflicted
                    | Asset.States.MovedLocal
                    | Asset.States.MovedRemote | Asset.States.Updating
                    | Asset.States.OutOfSync
                    )) != 0;
        }

        private static object HandleCheckout(string paramsJson)
        {
            EnsureVcsActive();

            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var paths = ParsePaths(paramsJson);
            bool all = p?.ContainsKey("all") == true && Convert.ToBoolean(p["all"]);

            AssetList assets;
            if (all)
            {
                // Checkout everything that's modified / added under Assets
                var statusTask = Provider.Status("Assets", true);
                WaitForTask(statusTask);
                assets = new AssetList();
                if (statusTask.assetList != null)
                {
                    foreach (var a in statusTask.assetList)
                    {
                        if (HasPendingState(a) || a.IsState(Asset.States.ReadOnly))
                            assets.Add(a);
                    }
                }
            }
            else
            {
                if (paths.Count == 0)
                    throw new ArgumentException("Provide 'path', 'paths', or 'all: true'");
                assets = ToAssetList(paths);
            }

            if (assets.Count == 0)
                return new Dictionary<string, object> { ["count"] = 0, ["message"] = "Nothing to checkout" };

            var task = Provider.Checkout(assets, CheckoutMode.Both);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = assets.Count,
                ["paths"] = assets.Select(a => a.path).ToList(),
            };
        }

        private static object HandleRevert(string paramsJson)
        {
            EnsureVcsActive();

            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var paths = ParsePaths(paramsJson);
            bool all = p?.ContainsKey("all") == true && Convert.ToBoolean(p["all"]);

            AssetList assets;
            if (all)
            {
                var statusTask = Provider.Status("Assets", true);
                WaitForTask(statusTask);
                assets = new AssetList();
                if (statusTask.assetList != null)
                {
                    foreach (var a in statusTask.assetList)
                    {
                        if (HasPendingState(a))
                            assets.Add(a);
                    }
                }
            }
            else
            {
                if (paths.Count == 0)
                    throw new ArgumentException("Provide 'path', 'paths', or 'all: true'");
                assets = ToAssetList(paths);
            }

            if (assets.Count == 0)
                return new Dictionary<string, object> { ["count"] = 0, ["message"] = "Nothing to revert" };

            var revertMode = RevertMode.Normal;
            if (p?.ContainsKey("keepLocal") == true && Convert.ToBoolean(p["keepLocal"]))
                revertMode = RevertMode.KeepModifications;

            var task = Provider.Revert(assets, revertMode);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = assets.Count,
                ["paths"] = assets.Select(a => a.path).ToList(),
            };
        }

        private static object HandleCommit(string paramsJson)
        {
            EnsureVcsActive();

            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("message", out var msgObj) || string.IsNullOrEmpty(msgObj?.ToString()))
                throw new ArgumentException("Missing 'message' parameter");

            var message = msgObj.ToString();
            var paths = ParsePaths(paramsJson);
            bool all = paths.Count == 0;

            // Build changeset
            AssetList assets;
            if (all)
            {
                var statusTask = Provider.Status("Assets", true);
                WaitForTask(statusTask);
                assets = new AssetList();
                if (statusTask.assetList != null)
                {
                    foreach (var a in statusTask.assetList)
                    {
                        if (HasPendingState(a))
                            assets.Add(a);
                    }
                }
            }
            else
            {
                assets = ToAssetList(paths);
            }

            if (assets.Count == 0)
                return new Dictionary<string, object> { ["success"] = false, ["message"] = "No pending changes to commit" };

            var changeset = new ChangeSet("", "");
            var task = Provider.Submit(changeset, assets, message, true);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = assets.Count,
                ["commitMessage"] = message,
                ["paths"] = assets.Select(a => a.path).ToList(),
            };
        }

        private static object HandleDiff(string paramsJson)
        {
            EnsureVcsActive();

            var paths = ParsePaths(paramsJson);

            if (paths.Count == 0)
            {
                // Return summary of changes (no specific diff)
                var statusTask = Provider.Status("Assets", true);
                WaitForTask(statusTask);

                var summary = new Dictionary<string, int>
                {
                    ["modified"] = 0,
                    ["added"] = 0,
                    ["deleted"] = 0,
                    ["moved"] = 0,
                    ["conflicted"] = 0,
                    ["outOfSync"] = 0
                };
                var items = new List<object>();
                if (statusTask.assetList != null)
                {
                    foreach (var a in statusTask.assetList)
                    {
                        if (!HasPendingState(a)) continue;
                        items.Add(AssetToDict(a));

                        if (a.IsState(Asset.States.CheckedOutLocal))
                            summary["modified"]++;
                        if (a.IsState(Asset.States.AddedLocal)) summary["added"]++;
                        if (a.IsState(Asset.States.DeletedLocal)) summary["deleted"]++;
                        if (a.IsState(Asset.States.MovedLocal)) summary["moved"]++;
                        if (a.IsState(Asset.States.Conflicted))
                            summary["conflicted"]++;
                        if (a.IsState(Asset.States.OutOfSync)) summary["outOfSync"]++;
                    }
                }

                return new Dictionary<string, object>
                {
                    ["summary"] = summary,
                    ["count"] = items.Count,
                    ["assets"] = items,
                };
            }
            else
            {
                // Diff specific files — launch diff tool if running interactively
                // For CLI, return status info for the requested paths
                var statusTask = Provider.Status(ToAssetList(paths), true);
                WaitForTask(statusTask);

                var items = new List<object>();
                if (statusTask.assetList != null)
                {
                    foreach (var a in statusTask.assetList)
                        items.Add(AssetToDict(a));
                }

                return new Dictionary<string, object>
                {
                    ["count"] = items.Count,
                    ["assets"] = items,
                };
            }
        }

        private static object HandleIncoming(string paramsJson)
        {
            EnsureVcsActive();

            var task = Provider.Incoming();
            WaitForTask(task);

            var items = new List<object>();
            if (task.assetList != null)
            {
                foreach (var a in task.assetList)
                    items.Add(AssetToDict(a));
            }

            return new Dictionary<string, object>
            {
                ["count"] = items.Count,
                ["assets"] = items,
            };
        }

        private static object HandleUpdate(string paramsJson)
        {
            EnsureVcsActive();

            // Get incoming changes and apply them
            var incomingTask = Provider.Incoming();
            WaitForTask(incomingTask);

            if (incomingTask.assetList == null || incomingTask.assetList.Count == 0)
                return new Dictionary<string, object> { ["success"] = true, ["count"] = 0, ["message"] = "Already up to date" };

            var task = Provider.GetLatest(incomingTask.assetList);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = incomingTask.assetList.Count,
                ["assets"] = incomingTask.assetList.Select(a => a.path).ToList(),
            };
        }

        private static object HandleBranches(string paramsJson)
        {
            EnsureVcsActive();

            // Use the cm CLI tool to list branches — Provider API doesn't expose branches directly
            // Fall back to reporting what we can from the provider
            var result = new Dictionary<string, object>
            {
                ["onlineState"] = Provider.onlineState.ToString(),
                ["plugin"] = Provider.GetActivePlugin()?.name ?? "unknown",
            };

            // Try running "cm find branches" via process if Plastic SCM is available
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cm",
                    Arguments = "find branches --format={name} --nototal",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(10000);

                var branches = output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim())
                    .Where(b => !string.IsNullOrEmpty(b))
                    .ToList();

                result["branches"] = branches;
                result["count"] = branches.Count;
            }
            catch
            {
                result["branches"] = new List<string>();
                result["count"] = 0;
                result["note"] = "Could not list branches. Ensure 'cm' CLI is available in PATH.";
            }

            // Try getting current branch
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cm",
                    Arguments = "status --header --machinereadable",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(10000);

                // First line usually contains the current changeset/branch info
                result["currentInfo"] = output.Trim().Split('\n').FirstOrDefault() ?? "";
            }
            catch { /* cm not available */ }

            return result;
        }

        private static object HandleLock(string paramsJson)
        {
            EnsureVcsActive();

            var paths = ParsePaths(paramsJson);
            if (paths.Count == 0)
                throw new ArgumentException("Provide 'path' or 'paths' to lock");

            var assets = ToAssetList(paths);
            var task = Provider.Checkout(assets, CheckoutMode.Exact);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = assets.Count,
                ["paths"] = assets.Select(a => a.path).ToList(),
            };
        }

        private static object HandleUnlock(string paramsJson)
        {
            EnsureVcsActive();

            var paths = ParsePaths(paramsJson);
            if (paths.Count == 0)
                throw new ArgumentException("Provide 'path' or 'paths' to unlock");

            var assets = ToAssetList(paths);
            var task = Provider.Revert(assets, RevertMode.Normal);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = assets.Count,
                ["paths"] = assets.Select(a => a.path).ToList(),
            };
        }

        private static object HandleHistory(string paramsJson)
        {
            EnsureVcsActive();

            var paths = ParsePaths(paramsJson);
            if (paths.Count == 0)
                paths = new List<string> { "Assets" };

            // Try cm log for richer history
            try
            {
                var cmPaths = string.Join(" ", paths.Select(p => $"\"{p}\""));
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cm",
                    Arguments = $"log --csformat=\"{{changesetid}}|{{date}}|{{owner}}|{{comment}}\" -n 20",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(10000);

                var entries = new List<object>();
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        entries.Add(new Dictionary<string, object>
                        {
                            ["changeset"] = parts[0].Trim(),
                            ["date"] = parts[1].Trim(),
                            ["author"] = parts[2].Trim(),
                            ["comment"] = parts[3].Trim(),
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["count"] = entries.Count,
                    ["entries"] = entries,
                };
            }
            catch
            {
                // Fallback: no cm CLI available, try Provider API
                throw new InvalidOperationException(
                    "History requires the 'cm' CLI. Ensure Plastic SCM / Unity VCS client is installed.");
            }
        }

        private static object HandleResolve(string paramsJson)
        {
            EnsureVcsActive();

            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            var paths = ParsePaths(paramsJson);

            if (paths.Count == 0)
                throw new ArgumentException("Provide 'path' or 'paths' to resolve");

            var resolveMethod = ResolveMethod.UseMerged;
            if (p?.TryGetValue("method", out var methodObj) == true)
            {
                var m = methodObj?.ToString()?.ToLower();
                resolveMethod = m switch
                {
                    "mine" => ResolveMethod.UseMine,
                    "theirs" => ResolveMethod.UseTheirs,
                    _ => ResolveMethod.UseMerged,
                };
            }

            var assets = ToAssetList(paths);
            var task = Provider.Resolve(assets, resolveMethod);
            bool ok = WaitForTask(task);

            return new Dictionary<string, object>
            {
                ["success"] = ok,
                ["count"] = assets.Count,
                ["paths"] = assets.Select(a => a.path).ToList(),
            };
        }
    }
}
