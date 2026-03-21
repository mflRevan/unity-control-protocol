using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UCP.Bridge
{
    public static class PackagesController
    {
        private const int DefaultTimeoutMs = 120000;

        public static void Register(CommandRouter router)
        {
            router.Register("packages/list", HandleList);
            router.Register("packages/search", HandleSearch);
            router.Register("packages/info", HandleInfo);
            router.Register("packages/add", HandleAdd);
            router.Register("packages/remove", HandleRemove);
            router.Register("packages/dependencies", HandleDependencies);
            router.Register("packages/dependency/set", HandleSetDependency);
            router.Register("packages/dependency/remove", HandleRemoveDependency);
            router.Register("packages/registries/list", HandleListRegistries);
            router.Register("packages/registries/add", HandleAddRegistry);
            router.Register("packages/registries/remove", HandleRemoveRegistry);
        }

        private static object HandleList(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var offline = ReadOptionalBool(parameters, "offline");
            var includeIndirect = ReadOptionalBool(parameters, "includeIndirect");
            var directDependencies = ReadDirectDependencies();
            var request = WaitForRequest(Client.List(offline, includeIndirect), DefaultTimeoutMs);
            var packages = request.Result
                .Select(package => SerializePackage(package, directDependencies))
                .OrderByDescending(item => Convert.ToBoolean(item["directDependency"]))
                .ThenBy(item => item["name"].ToString(), StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();

            return new Dictionary<string, object>
            {
                ["packages"] = packages,
                ["offline"] = offline,
                ["includeIndirect"] = includeIndirect,
                ["count"] = packages.Count
            };
        }

        private static object HandleSearch(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var query = ReadOptionalString(parameters, "query");
            var offline = ReadOptionalBool(parameters, "offline");
            var maxResults = ReadOptionalInt(parameters, "maxResults", 50);
            SearchRequest request = string.IsNullOrWhiteSpace(query)
                ? WaitForRequest(Client.SearchAll(offline), DefaultTimeoutMs)
                : WaitForRequest(Client.Search(query, offline), DefaultTimeoutMs);

            var results = request.Result
                .Select(package => SerializePackage(package, null))
                .OrderBy(item => item["name"].ToString(), StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxResults))
                .Cast<object>()
                .ToList();

            return new Dictionary<string, object>
            {
                ["query"] = query,
                ["offline"] = offline,
                ["results"] = results,
                ["count"] = results.Count
            };
        }

        private static object HandleInfo(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var name = RequireString(parameters, "name");
            var offline = ReadOptionalBool(parameters, "offline");
            var directDependencies = ReadDirectDependencies();

            var installed = WaitForRequest(Client.List(offline, true), DefaultTimeoutMs)
                .Result
                .FirstOrDefault(package =>
                    package.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || package.packageId.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (installed != null)
            {
                var payload = SerializePackage(installed, directDependencies);
                payload["installed"] = true;
                return payload;
            }

            var search = WaitForRequest(Client.Search(name, offline), DefaultTimeoutMs);
            var found = search.Result.FirstOrDefault(package =>
                package.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || package.packageId.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (found == null)
                throw new ArgumentException($"Package not found: {name}");

            var result = SerializePackage(found, null);
            result["installed"] = false;
            return result;
        }

        private static object HandleAdd(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var packageId = RequireString(parameters, "packageId");
            var request = WaitForRequest(Client.Add(packageId), DefaultTimeoutMs);
            var directDependencies = ReadDirectDependencies();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["packageId"] = packageId,
                ["package"] = SerializePackage(request.Result, directDependencies)
            };
        }

        private static object HandleRemove(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var name = RequireString(parameters, "name");
            var request = WaitForRequest(Client.Remove(name), DefaultTimeoutMs);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["name"] = request.PackageIdOrName
            };
        }

        private static object HandleDependencies(string paramsJson)
        {
            var manifest = ReadManifest();
            var dependencies = ReadDirectDependencies()
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => (object)new Dictionary<string, object>
                {
                    ["name"] = entry.Key,
                    ["reference"] = entry.Value
                })
                .ToList();

            return new Dictionary<string, object>
            {
                ["dependencies"] = dependencies,
                ["count"] = dependencies.Count,
                ["manifestPath"] = ManifestPath
            };
        }

        private static object HandleSetDependency(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var name = RequireString(parameters, "name");
            var reference = RequireString(parameters, "reference");
            var manifest = ReadManifest();
            var dependencies = EnsureObject(manifest, "dependencies");
            var previous = dependencies.ContainsKey(name) ? dependencies[name]?.ToString() : null;
            dependencies[name] = reference;
            WriteManifest(manifest);
            TriggerResolve();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["name"] = name,
                ["reference"] = reference,
                ["previousReference"] = previous,
                ["changed"] = !string.Equals(previous, reference, StringComparison.Ordinal)
            };
        }

        private static object HandleRemoveDependency(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var name = RequireString(parameters, "name");
            var manifest = ReadManifest();
            var dependencies = EnsureObject(manifest, "dependencies");
            var previous = dependencies.ContainsKey(name) ? dependencies[name]?.ToString() : null;
            var removed = dependencies.Remove(name);
            if (!removed)
                throw new ArgumentException($"Dependency not found: {name}");

            WriteManifest(manifest);
            TriggerResolve();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["name"] = name,
                ["previousReference"] = previous
            };
        }

        private static object HandleListRegistries(string paramsJson)
        {
            var registries = ReadScopedRegistries()
                .OrderBy(item => item["name"].ToString(), StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();

            return new Dictionary<string, object>
            {
                ["registries"] = registries,
                ["count"] = registries.Count,
                ["manifestPath"] = ManifestPath
            };
        }

        private static object HandleAddRegistry(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var name = RequireString(parameters, "name");
            var url = RequireString(parameters, "url");
            var scopes = ReadScopes(parameters);
            if (scopes.Count == 0)
                throw new ArgumentException("At least one scope is required");

            var manifest = ReadManifest();
            var registries = EnsureArray(manifest, "scopedRegistries");
            Dictionary<string, object> previous = null;

            for (var index = registries.Count - 1; index >= 0; index--)
            {
                if (registries[index] is Dictionary<string, object> existing
                    && existing.TryGetValue("name", out var existingName)
                    && string.Equals(existingName?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    previous = existing;
                    registries.RemoveAt(index);
                }
            }

            var registry = new Dictionary<string, object>
            {
                ["name"] = name,
                ["url"] = url,
                ["scopes"] = scopes.Cast<object>().ToList()
            };

            registries.Add(registry);
            WriteManifest(manifest);
            TriggerResolve();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["registry"] = SerializeRegistry(registry),
                ["previous"] = previous != null ? SerializeRegistry(previous) : null
            };
        }

        private static object HandleRemoveRegistry(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var name = RequireString(parameters, "name");
            var manifest = ReadManifest();
            var registries = EnsureArray(manifest, "scopedRegistries");
            Dictionary<string, object> removed = null;

            for (var index = registries.Count - 1; index >= 0; index--)
            {
                if (registries[index] is Dictionary<string, object> existing
                    && existing.TryGetValue("name", out var existingName)
                    && string.Equals(existingName?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    removed = existing;
                    registries.RemoveAt(index);
                    break;
                }
            }

            if (removed == null)
                throw new ArgumentException($"Scoped registry not found: {name}");

            WriteManifest(manifest);
            TriggerResolve();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["registry"] = SerializeRegistry(removed)
            };
        }

        private static Dictionary<string, object> ParseParameters(string paramsJson)
        {
            return string.IsNullOrWhiteSpace(paramsJson)
                ? new Dictionary<string, object>()
                : MiniJson.Deserialize(paramsJson) as Dictionary<string, object>
                    ?? throw new ArgumentException("Invalid parameters");
        }

        private static string RequireString(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || value == null || string.IsNullOrWhiteSpace(value.ToString()))
                throw new ArgumentException($"Missing '{key}' parameter");

            return value.ToString();
        }

        private static string ReadOptionalString(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var value) && value != null
                ? value.ToString()
                : null;
        }

        private static bool ReadOptionalBool(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var value) && value != null && Convert.ToBoolean(value);
        }

        private static int ReadOptionalInt(Dictionary<string, object> parameters, string key, int defaultValue)
        {
            return parameters.TryGetValue(key, out var value) && value != null
                ? Convert.ToInt32(value)
                : defaultValue;
        }

        private static Dictionary<string, string> ReadDirectDependencies()
        {
            var manifest = ReadManifest();
            var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!manifest.TryGetValue("dependencies", out var dependenciesObj)
                || !(dependenciesObj is Dictionary<string, object> dependencyMap))
            {
                return dependencies;
            }

            foreach (var entry in dependencyMap)
                dependencies[entry.Key] = entry.Value?.ToString() ?? string.Empty;

            return dependencies;
        }

        private static List<Dictionary<string, object>> ReadScopedRegistries()
        {
            var manifest = ReadManifest();
            if (!manifest.TryGetValue("scopedRegistries", out var registriesObj)
                || !(registriesObj is List<object> registryList))
            {
                return new List<Dictionary<string, object>>();
            }

            var result = new List<Dictionary<string, object>>();
            foreach (var item in registryList)
            {
                if (item is Dictionary<string, object> registry)
                    result.Add(SerializeRegistry(registry));
            }

            return result;
        }

        private static Dictionary<string, object> ReadManifest()
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException($"manifest.json not found: {ManifestPath}");

            var json = File.ReadAllText(ManifestPath);
            return MiniJson.Deserialize(json) as Dictionary<string, object>
                ?? throw new ArgumentException("manifest.json is not a JSON object");
        }

        private static void WriteManifest(Dictionary<string, object> manifest)
        {
            File.WriteAllText(ManifestPath, MiniJson.Serialize(manifest));
        }

        private static Dictionary<string, object> EnsureObject(Dictionary<string, object> root, string key)
        {
            if (root.TryGetValue(key, out var existing) && existing is Dictionary<string, object> existingObject)
                return existingObject;

            var created = new Dictionary<string, object>();
            root[key] = created;
            return created;
        }

        private static List<object> EnsureArray(Dictionary<string, object> root, string key)
        {
            if (root.TryGetValue(key, out var existing) && existing is List<object> existingArray)
                return existingArray;

            var created = new List<object>();
            root[key] = created;
            return created;
        }

        private static Dictionary<string, object> SerializePackage(UpmPackageInfo package, IDictionary<string, string> directDependencies)
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = package.name,
                ["displayName"] = package.displayName,
                ["version"] = package.version,
                ["packageId"] = package.packageId,
                ["source"] = package.source.ToString(),
                ["resolvedPath"] = package.resolvedPath,
                ["description"] = package.description ?? string.Empty,
                ["directDependency"] = directDependencies != null && directDependencies.ContainsKey(package.name),
                ["dependencies"] = package.dependencies != null
                    ? package.dependencies.Select(dep => (object)new Dictionary<string, object>
                    {
                        ["name"] = dep.name,
                        ["version"] = dep.version
                    }).ToList()
                    : new List<object>()
            };

            if (!string.IsNullOrEmpty(package.category))
                result["category"] = package.category;

            if (package.versions != null)
            {
                result["versions"] = new Dictionary<string, object>
                {
                    ["recommended"] = package.versions.recommended ?? string.Empty,
                    ["latestCompatible"] = package.versions.latestCompatible ?? string.Empty
                };
            }

            if (package.registry != null)
            {
                result["registry"] = new Dictionary<string, object>
                {
                    ["name"] = package.registry.name ?? string.Empty,
                    ["url"] = package.registry.url ?? string.Empty,
                    ["isDefault"] = package.registry.isDefault
                };
            }

            return result;
        }

        private static Dictionary<string, object> SerializeRegistry(Dictionary<string, object> registry)
        {
            var scopes = registry.TryGetValue("scopes", out var scopesObj) && scopesObj is List<object> scopeList
                ? scopeList.Select(scope => (object)scope.ToString()).ToList()
                : new List<object>();

            return new Dictionary<string, object>
            {
                ["name"] = registry.TryGetValue("name", out var name) ? name?.ToString() ?? string.Empty : string.Empty,
                ["url"] = registry.TryGetValue("url", out var url) ? url?.ToString() ?? string.Empty : string.Empty,
                ["scopes"] = scopes
            };
        }

        private static List<string> ReadScopes(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("scopes", out var scopesObj) || !(scopesObj is List<object> scopes))
                return new List<string>();

            return scopes
                .Select(scope => scope?.ToString())
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static T WaitForRequest<T>(T request, int timeoutMs) where T : Request
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!request.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Package Manager request timed out");

                Thread.Sleep(50);
            }

            if (request.Status == StatusCode.Failure)
                throw new InvalidOperationException(request.Error?.message ?? "Package Manager request failed");

            return request;
        }

        private static void TriggerResolve()
        {
            Client.Resolve();
            WaitForRequest(Client.List(true, true), DefaultTimeoutMs);
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static string ManifestPath => Path.Combine(ProjectRoot, "Packages", "manifest.json");
    }
}
