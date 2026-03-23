using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    public static class AssetController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("asset/search", HandleSearch);
            router.Register("asset/info", HandleInfo);
            router.Register("asset/read", HandleReadScriptableObject);
            router.Register("asset/write", HandleWriteScriptableObject);
            router.Register("asset/write-batch", HandleWriteScriptableObjectBatch);
            router.Register("asset/create-so", HandleCreateScriptableObject);
            router.Register("asset/delete", HandleDeleteAsset);
        }

        private static object HandleSearch(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            string typeFilter = null;
            string nameFilter = null;
            string pathFilter = null;
            int maxResults = 100;

            if (p != null)
            {
                if (p.TryGetValue("type", out var tObj) && tObj != null)
                    typeFilter = tObj.ToString();
                if (p.TryGetValue("name", out var nObj) && nObj != null)
                    nameFilter = nObj.ToString();
                if (p.TryGetValue("path", out var pObj) && pObj != null)
                    pathFilter = pObj.ToString();
                if (p.TryGetValue("maxResults", out var mObj))
                    maxResults = Convert.ToInt32(mObj);
            }

            // Build search filter
            string filter = "";
            if (!string.IsNullOrEmpty(nameFilter))
                filter += nameFilter + " ";
            if (!string.IsNullOrEmpty(typeFilter))
                filter += $"t:{GetSearchTypeFilter(typeFilter)}";

            string[] searchFolders = null;
            if (!string.IsNullOrEmpty(pathFilter))
                searchFolders = new[] { pathFilter };

            string[] guids;
            if (searchFolders != null)
                guids = AssetDatabase.FindAssets(filter.Trim(), searchFolders);
            else
                guids = AssetDatabase.FindAssets(filter.Trim());

            var results = new List<object>();
            int totalMatches = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                foreach (var asset in GetMatchingAssets(assetPath, typeFilter, nameFilter))
                {
                    totalMatches++;
                    if (results.Count >= maxResults)
                        continue;

                    results.Add(new Dictionary<string, object>
                    {
                        ["guid"] = guids[i],
                        ["path"] = assetPath,
                        ["type"] = GetDisplayType(assetPath, asset),
                        ["name"] = asset.name,
                        ["isSubAsset"] = AssetDatabase.IsSubAsset(asset)
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["results"] = results,
                ["total"] = totalMatches,
                ["returned"] = results.Count
            };
        }

        private static object HandleInfo(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");

            string assetPath = pathObj.ToString();
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                throw new ArgumentException($"Asset not found: {assetPath}");

            var info = new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["name"] = asset.name,
                ["type"] = asset.GetType().Name,
                ["fullType"] = asset.GetType().FullName,
                ["instanceId"] = asset.GetInstanceID(),
                ["guid"] = AssetDatabase.AssetPathToGUID(assetPath)
            };

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer != null)
                info["importerType"] = importer.GetType().Name;

            // Add additional info for specific types
            if (asset is GameObject go)
            {
                info["isPrefab"] = true;
                var components = new List<string>();
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c != null)
                        components.Add(c.GetType().Name);
                }
                info["components"] = components;
            }

            if (asset is ScriptableObject)
            {
                info["isScriptableObject"] = true;
            }

            return info;
        }

        private static object HandleReadScriptableObject(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");

            string assetPath = pathObj.ToString();
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                throw new ArgumentException($"Asset not found: {assetPath}");

            // Optional: read specific field only
            string fieldName = null;
            if (p.TryGetValue("field", out var fObj) && fObj != null)
                fieldName = fObj.ToString();

            var so = new SerializedObject(asset);
            var fields = new List<object>();

            if (fieldName != null)
            {
                var prop = so.FindProperty(fieldName);
                if (prop == null)
                    throw new ArgumentException($"Field '{fieldName}' not found on {asset.GetType().Name}");

                fields.Add(new Dictionary<string, object>
                {
                    ["name"] = prop.name,
                    ["propertyPath"] = prop.propertyPath,
                    ["displayName"] = prop.displayName,
                    ["type"] = prop.propertyType.ToString(),
                    ["value"] = SerializedPropertyControllerSupport.ReadValue(prop)
                });
            }
            else
            {
                var iter = so.GetIterator();
                if (iter.NextVisible(true))
                {
                    do
                    {
                        if (iter.name == "m_Script") continue;
                        fields.Add(SerializedPropertyControllerSupport.Describe(iter));
                    }
                    while (iter.NextVisible(false));
                }
            }

            so.Dispose();

            return new Dictionary<string, object>
            {
                ["path"] = assetPath,
                ["name"] = asset.name,
                ["type"] = asset.GetType().Name,
                ["fields"] = fields
            };
        }

        private static object HandleWriteScriptableObject(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");
            if (!p.TryGetValue("field", out var fieldObj) || fieldObj == null)
                throw new ArgumentException("Missing 'field' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            string assetPath = pathObj.ToString();
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                throw new ArgumentException($"Asset not found: {assetPath}");

            string fieldName = fieldObj.ToString();
            var so = new SerializedObject(asset);
            Undo.RecordObject(asset, $"UCP Write {fieldName}");
            SerializedPropertyControllerSupport.WriteFieldValue(so, asset.GetType().Name, fieldName, p["value"]);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            so.Dispose();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["path"] = assetPath,
                ["field"] = fieldName
            };
        }

        private static object HandleWriteScriptableObjectBatch(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");
            if (!p.TryGetValue("values", out var valuesObj) || !(valuesObj is Dictionary<string, object> values))
                throw new ArgumentException("Missing 'values' parameter");

            string assetPath = pathObj.ToString();
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                throw new ArgumentException($"Asset not found: {assetPath}");

            var so = new SerializedObject(asset);
            Undo.RecordObject(asset, $"UCP Batch Write {asset.name}");

            var fields = new List<object>();
            foreach (var entry in values)
            {
                SerializedPropertyControllerSupport.WriteFieldValue(so, asset.GetType().Name, entry.Key, entry.Value);
                fields.Add(entry.Key);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            so.Dispose();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["path"] = assetPath,
                ["fields"] = fields
            };
        }

        private static object HandleCreateScriptableObject(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("type", out var typeObj) || typeObj == null)
                throw new ArgumentException("Missing 'type' parameter");
            if (!p.TryGetValue("path", out var pathObj) || pathObj == null)
                throw new ArgumentException("Missing 'path' parameter");

            string typeName = typeObj.ToString();
            string assetPath = pathObj.ToString();

            // Resolve ScriptableObject type
            Type soType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in assembly.GetTypes())
                {
                    if (!typeof(ScriptableObject).IsAssignableFrom(t)) continue;
                    if (t.Name == typeName || t.FullName == typeName)
                    {
                        soType = t;
                        break;
                    }
                }
                if (soType != null) break;
            }

            if (soType == null)
                throw new ArgumentException($"ScriptableObject type not found: {typeName}");

            var instance = ScriptableObject.CreateInstance(soType);

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                CreateFoldersRecursive(dir);
            }

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["path"] = assetPath,
                ["type"] = soType.Name,
                ["instanceId"] = instance.GetInstanceID()
            };
        }

        private static object HandleDeleteAsset(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj) || pathObj == null)
                throw new ArgumentException("Missing 'path' parameter");

            string assetPath = pathObj.ToString();
            var normalizedPath = assetPath.Replace("\\", "/");
            if (!AssetDatabase.AssetPathExists(normalizedPath))
                throw new ArgumentException($"Asset not found: {normalizedPath}");

            if (!AssetDatabase.DeleteAsset(normalizedPath))
                throw new InvalidOperationException($"Failed to delete asset: {normalizedPath}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["path"] = normalizedPath
            };
        }

        private static void CreateFoldersRecursive(string path)
        {
            var parts = path.Replace("\\", "/").Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static IEnumerable<UnityEngine.Object> GetMatchingAssets(string assetPath, string typeFilter, string nameFilter)
        {
            UnityEngine.Object[] candidates;
            if (string.IsNullOrEmpty(typeFilter) && string.IsNullOrEmpty(nameFilter))
            {
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                candidates = mainAsset != null ? new[] { mainAsset } : Array.Empty<UnityEngine.Object>();
            }
            else
            {
                candidates = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            }

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                if (!MatchesName(candidate, assetPath, nameFilter)) continue;
                if (!MatchesType(candidate, assetPath, typeFilter)) continue;
                yield return candidate;
            }
        }

        private static bool MatchesName(UnityEngine.Object asset, string assetPath, string nameFilter)
        {
            if (string.IsNullOrEmpty(nameFilter))
                return true;

            return asset.name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                || System.IO.Path.GetFileNameWithoutExtension(assetPath).Contains(nameFilter, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesType(UnityEngine.Object asset, string assetPath, string typeFilter)
        {
            if (string.IsNullOrEmpty(typeFilter))
                return true;

            if (typeFilter.Equals("Prefab", StringComparison.OrdinalIgnoreCase))
            {
                return asset is GameObject
                    && !AssetDatabase.IsSubAsset(asset)
                    && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            }

            var normalizedType = NormalizeTypeFilter(typeFilter);
            var assetType = asset.GetType();
            return assetType.Name.Equals(normalizedType, StringComparison.OrdinalIgnoreCase)
                || (assetType.FullName != null && assetType.FullName.Equals(normalizedType, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDisplayType(string assetPath, UnityEngine.Object asset)
        {
            if (asset is GameObject
                && !AssetDatabase.IsSubAsset(asset)
                && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return "Prefab";
            }

            return asset.GetType().Name;
        }

        private static string GetSearchTypeFilter(string typeFilter)
        {
            if (typeFilter.Equals("Prefab", StringComparison.OrdinalIgnoreCase))
                return "GameObject";

            return NormalizeTypeFilter(typeFilter);
        }

        private static string NormalizeTypeFilter(string typeFilter)
        {
            return typeFilter?.Trim() ?? string.Empty;
        }

    }
}
