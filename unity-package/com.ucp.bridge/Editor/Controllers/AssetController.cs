using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UCP.Bridge
{
    public static class AssetController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("asset/search", HandleSearch);
            router.Register("asset/info", HandleInfo);
            router.Register("asset/inspect", HandleInspect);
            router.Register("asset/read", HandleReadScriptableObject);
            router.Register("asset/write", HandleWriteScriptableObject);
            router.Register("asset/write-batch", HandleWriteScriptableObjectBatch);
            router.Register("asset/create-so", HandleCreateScriptableObject);
            router.Register("asset/delete", HandleDeleteAsset);
            router.Register("asset/move", HandleMoveAsset);
            router.Register("asset/bulk-move", HandleBulkMoveAssets);
        }

        private static object HandleSearch(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            string typeFilter = null;
            string nameFilter = null;
            string pathFilter = null;
            bool useRegex = false;
            int maxResults = 100;

            if (p != null)
            {
                if (p.TryGetValue("type", out var tObj) && tObj != null)
                    typeFilter = tObj.ToString();
                if (p.TryGetValue("name", out var nObj) && nObj != null)
                    nameFilter = nObj.ToString();
                if (p.TryGetValue("path", out var pObj) && pObj != null)
                    pathFilter = pObj.ToString();
                if (p.TryGetValue("regex", out var regexObj) && regexObj != null)
                    useRegex = Convert.ToBoolean(regexObj);
                if (p.TryGetValue("maxResults", out var mObj))
                    maxResults = Convert.ToInt32(mObj);
            }

            Regex nameRegex = null;
            if (useRegex && !string.IsNullOrEmpty(nameFilter))
            {
                try
                {
                    nameRegex = new Regex(nameFilter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException($"Invalid regex for name filter: {ex.Message}");
                }
            }

            // Build search filter
            string filter = "";
            if (!useRegex && !string.IsNullOrEmpty(nameFilter))
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
                foreach (var asset in GetMatchingAssets(assetPath, typeFilter, nameFilter, nameRegex))
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

        private static object HandleInspect(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("path", out var pathObj))
                throw new ArgumentException("Missing 'path' parameter");

            string assetPath = pathObj.ToString();
            int maxFields = 80;
            if (p.TryGetValue("maxFields", out var maxObj) && maxObj != null)
                maxFields = Math.Max(1, Convert.ToInt32(maxObj));

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                throw new ArgumentException($"Asset not found: {assetPath}");

            var result = HandleInfo(paramsJson) as Dictionary<string, object>;
            result["inspectedAtUtc"] = DateTime.UtcNow.ToString("o");

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer != null)
                result["importer"] = InspectSerializedObject(importer, maxFields);

            if (asset is Material material)
            {
                result["shader"] = material.shader != null ? material.shader.name : string.Empty;
                result["shaderPath"] = material.shader != null ? AssetDatabase.GetAssetPath(material.shader) : string.Empty;
                result["keywords"] = InspectMaterialKeywords(material);
                result["properties"] = InspectMaterialProperties(material, maxFields);
            }
            else if (asset is GameObject gameObject)
            {
                result["renderers"] = InspectPrefabRenderers(gameObject);
            }
            else
            {
                result["fields"] = InspectSerializedObject(asset, maxFields);
            }

            return result;
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

        private static List<object> InspectSerializedObject(UnityEngine.Object target, int maxFields)
        {
            var serializedObject = new SerializedObject(target);
            var fields = new List<object>();
            try
            {
                var iterator = serializedObject.GetIterator();
                if (iterator.NextVisible(true))
                {
                    do
                    {
                        if (iterator.name == "m_Script") continue;
                        fields.Add(SerializedPropertyControllerSupport.Describe(iterator));
                    }
                    while (fields.Count < maxFields && iterator.NextVisible(false));
                }
            }
            finally
            {
                serializedObject.Dispose();
            }

            return fields;
        }

        private static List<object> InspectMaterialKeywords(Material material)
        {
            var keywords = new List<object>();
            foreach (var keyword in material.enabledKeywords)
                keywords.Add(keyword.name);
            return keywords;
        }

        private static List<object> InspectMaterialProperties(Material material, int maxFields)
        {
            var properties = new List<object>();
            var shader = material.shader;
            if (shader == null)
                return properties;

            var count = Math.Min(shader.GetPropertyCount(), maxFields);
            for (int i = 0; i < count; i++)
            {
                var name = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                properties.Add(new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["description"] = shader.GetPropertyDescription(i),
                    ["type"] = type.ToString(),
                    ["value"] = ReadMaterialValue(material, name, type)
                });
            }

            return properties;
        }

        private static object ReadMaterialValue(Material material, string name, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color:
                    var color = material.GetColor(name);
                    return new List<object> { color.r, color.g, color.b, color.a };
                case ShaderPropertyType.Vector:
                    var vector = material.GetVector(name);
                    return new List<object> { vector.x, vector.y, vector.z, vector.w };
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return material.GetFloat(name);
                case ShaderPropertyType.Texture:
                    var texture = material.GetTexture(name);
                    return texture != null
                        ? new Dictionary<string, object>
                        {
                            ["name"] = texture.name,
                            ["path"] = AssetDatabase.GetAssetPath(texture),
                            ["type"] = texture.GetType().Name
                        }
                        : null;
                default:
                    return null;
            }
        }

        private static List<object> InspectPrefabRenderers(GameObject gameObject)
        {
            var renderers = new List<object>();
            foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new List<object>();
                foreach (var material in renderer.sharedMaterials)
                {
                    materials.Add(material != null
                        ? new Dictionary<string, object>
                        {
                            ["name"] = material.name,
                            ["path"] = AssetDatabase.GetAssetPath(material),
                            ["shader"] = material.shader != null ? material.shader.name : string.Empty
                        }
                        : null);
                }

                renderers.Add(new Dictionary<string, object>
                {
                    ["path"] = GetTransformPath(renderer.transform),
                    ["type"] = renderer.GetType().Name,
                    ["enabled"] = renderer.enabled,
                    ["materials"] = materials
                });
            }

            return renderers;
        }

        private static string GetTransformPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", names);
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

        private static object HandleMoveAsset(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            var sourcePath = RequirePathParameter(parameters, "path");
            var destination = RequirePathParameter(parameters, "destination");

            var result = MoveAssetInternal(sourcePath, destination, true);
            return result;
        }

        private static object HandleBulkMoveAssets(string paramsJson)
        {
            var parameters = ParseParameters(paramsJson);
            if (!parameters.TryGetValue("moves", out var movesObject) || movesObject == null)
                throw new ArgumentException("Missing 'moves' parameter");

            var continueOnError = TryGetOptionalBool(parameters, "continueOnError");
            var dryRun = TryGetOptionalBool(parameters, "dryRun");
            var requests = ParseMoveRequests(movesObject);
            var results = new List<object>();
            var errors = new List<object>();
            var movedCount = 0;
            var stopped = false;
            var anyChanged = false;

            if (dryRun)
            {
                for (var index = 0; index < requests.Count; index++)
                {
                    var request = requests[index];
                    try
                    {
                        var moveResult = PreviewMoveInternal(request.SourcePath, request.Destination);
                        results.Add(AddMoveIndex(moveResult, index));
                        if (GetBool(moveResult, "changed"))
                        {
                            movedCount++;
                            anyChanged = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new Dictionary<string, object>
                        {
                            ["index"] = index,
                            ["sourcePath"] = request.SourcePath,
                            ["destinationPath"] = request.Destination,
                            ["message"] = ex.Message
                        });

                        if (!continueOnError)
                        {
                            stopped = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                var preparedMoves = new List<PreparedMove>();
                for (var index = 0; index < requests.Count; index++)
                {
                    var request = requests[index];
                    try
                    {
                        var preparedMove = PrepareMove(request.SourcePath, request.Destination, true);
                        preparedMoves.Add(new PreparedMove(index, request.SourcePath, preparedMove));
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new Dictionary<string, object>
                        {
                            ["index"] = index,
                            ["sourcePath"] = request.SourcePath,
                            ["destinationPath"] = request.Destination,
                            ["message"] = ex.Message
                        });

                        if (!continueOnError)
                        {
                            stopped = true;
                            break;
                        }
                    }
                }

                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var preparedMove in preparedMoves)
                    {
                        try
                        {
                            var moveResult = MovePreparedAsset(preparedMove.SourcePath, preparedMove.Move, false);
                            results.Add(AddMoveIndex(moveResult, preparedMove.Index));
                            if (GetBool(moveResult, "changed"))
                            {
                                movedCount++;
                                anyChanged = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add(new Dictionary<string, object>
                            {
                                ["index"] = preparedMove.Index,
                                ["sourcePath"] = preparedMove.SourcePath,
                                ["destinationPath"] = preparedMove.Move.ResolvedDestination,
                                ["message"] = ex.Message
                            });

                            if (!continueOnError)
                            {
                                stopped = true;
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            if (!dryRun && anyChanged)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["requested"] = requests.Count,
                ["moved"] = movedCount,
                ["failed"] = errors.Count,
                ["dryRun"] = dryRun,
                ["stopped"] = stopped,
                ["results"] = results,
                ["errors"] = errors
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

        private static IEnumerable<UnityEngine.Object> GetMatchingAssets(
            string assetPath,
            string typeFilter,
            string nameFilter,
            Regex nameRegex)
        {
            UnityEngine.Object[] candidates;
            if (string.IsNullOrEmpty(typeFilter) && string.IsNullOrEmpty(nameFilter) && nameRegex == null)
            {
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                candidates = mainAsset != null ? new[] { mainAsset } : Array.Empty<UnityEngine.Object>();
            }
            else if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
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
                if (!MatchesName(candidate, assetPath, nameFilter, nameRegex)) continue;
                if (!MatchesType(candidate, assetPath, typeFilter)) continue;
                yield return candidate;
            }
        }

        private static bool MatchesName(UnityEngine.Object asset, string assetPath, string nameFilter, Regex nameRegex)
        {
            if (string.IsNullOrEmpty(nameFilter) && nameRegex == null)
                return true;

            if (nameRegex != null)
            {
                var fileStem = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                return nameRegex.IsMatch(asset.name)
                    || (!string.IsNullOrEmpty(fileStem) && nameRegex.IsMatch(fileStem));
            }

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

        private static Dictionary<string, object> MoveAssetInternal(string sourcePath, string destination, bool finalize)
        {
            var move = PrepareMove(sourcePath, destination, true);
            return MovePreparedAsset(NormalizeMovePath(sourcePath), move, finalize);
        }

        private static Dictionary<string, object> PreviewMoveInternal(string sourcePath, string destination)
        {
            var normalizedSource = NormalizeMovePath(sourcePath);
            var move = PrepareMove(normalizedSource, destination, false);
            return DescribeMovedAsset(normalizedSource, move.ResolvedDestination, move.Changed, move.Identity);
        }

        private static ValidatedMove PrepareMove(string sourcePath, string destination, bool ensureParentFolders)
        {
            var normalizedSource = NormalizeMovePath(sourcePath);
            var move = ValidateMoveRequest(normalizedSource, destination);
            if (ensureParentFolders)
                EnsureParentFoldersExist(move.ResolvedDestination);
            return move;
        }

        private static Dictionary<string, object> MovePreparedAsset(string normalizedSource, ValidatedMove move, bool finalize)
        {
            var moveError = AssetDatabase.MoveAsset(normalizedSource, move.ResolvedDestination);
            if (!string.IsNullOrEmpty(moveError))
                throw new InvalidOperationException(moveError);

            if (finalize)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            return DescribeMovedAsset(normalizedSource, move.ResolvedDestination, true, move.Identity);
        }

        private static Dictionary<string, object> DescribeMovedAsset(
            string sourcePath,
            string destinationPath,
            bool changed,
            AssetIdentity identity)
        {
            var payload = new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["sourcePath"] = sourcePath,
                ["destinationPath"] = destinationPath,
                ["changed"] = changed,
                ["guid"] = identity.Guid,
                ["isFolder"] = identity.IsFolder
            };

            if (!string.IsNullOrEmpty(identity.Name))
            {
                payload["name"] = identity.Name;
            }

            if (!string.IsNullOrEmpty(identity.Type))
            {
                payload["type"] = identity.Type;
            }

            return payload;
        }

        private static AssetIdentity DescribeExistingAsset(string assetPath, bool isFolder)
        {
            var guid = GetGuidForAssetPath(assetPath);
            var name = isFolder ? System.IO.Path.GetFileName(assetPath.TrimEnd('/')) : null;
            var type = isFolder ? "Folder" : null;

            if (!isFolder)
            {
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset != null)
                {
                    name = mainAsset.name;
                    type = mainAsset.GetType().Name;
                }
            }

            return new AssetIdentity(guid, name, type, isFolder);
        }

        private static string GetGuidForAssetPath(string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.IsNullOrEmpty(guid))
                return guid;

            var metaPath = ResolveProjectRelativePath(assetPath + ".meta");
            if (!File.Exists(metaPath))
                return string.Empty;

            foreach (var line in File.ReadLines(metaPath))
            {
                if (!line.TrimStart().StartsWith("guid:", StringComparison.Ordinal))
                    continue;

                var parts = line.Split(new[] { ':' }, 2);
                return parts.Length == 2 ? parts[1].Trim() : string.Empty;
            }

            return string.Empty;
        }

        private static string ResolveProjectRelativePath(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolveDestinationPath(string sourcePath, string destination)
        {
            var normalizedDestination = NormalizeMovePath(destination);
            if (string.IsNullOrEmpty(normalizedDestination))
                throw new ArgumentException("Missing 'destination' parameter");

            if (AssetDatabase.IsValidFolder(normalizedDestination))
                return normalizedDestination + "/" + System.IO.Path.GetFileName(sourcePath);

            if (normalizedDestination.EndsWith("/", StringComparison.Ordinal))
                return normalizedDestination.TrimEnd('/') + "/" + System.IO.Path.GetFileName(sourcePath);

            return normalizedDestination;
        }

        private static void EnsureParentFoldersExist(string assetPath)
        {
            var parent = System.IO.Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;

            CreateFoldersRecursive(parent);
        }

        private static string NormalizeMovePath(string path)
        {
            return path?.Trim().Replace('\\', '/');
        }

        private static ValidatedMove ValidateMoveRequest(string normalizedSource, string destination)
        {
            var isFolder = AssetDatabase.IsValidFolder(normalizedSource);
            if (!isFolder && !AssetDatabase.AssetPathExists(normalizedSource))
                throw new ArgumentException(BuildAssetNotFoundMessage(normalizedSource));

            if (!IsMovableAssetPath(normalizedSource))
                throw new ArgumentException($"Asset moves are only supported under Assets/: {normalizedSource}");

            var identity = DescribeExistingAsset(normalizedSource, isFolder);
            var resolvedDestination = ResolveDestinationPath(normalizedSource, destination);
            if (!IsMovableAssetPath(resolvedDestination))
                throw new ArgumentException($"Destination must be under Assets/: {resolvedDestination}");

            if (string.Equals(normalizedSource, resolvedDestination, StringComparison.OrdinalIgnoreCase))
                return new ValidatedMove(identity, resolvedDestination, false);

            if (AssetDatabase.AssetPathExists(resolvedDestination) || AssetDatabase.IsValidFolder(resolvedDestination))
                throw new ArgumentException($"Destination already exists: {resolvedDestination}");

            return new ValidatedMove(identity, resolvedDestination, true);
        }

        private static bool IsMovableAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && (path.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildAssetNotFoundMessage(string normalizedSource)
        {
            var message = $"Asset not found: {normalizedSource}.";
            var suggestions = FindSimilarAssetPaths(normalizedSource, 3);
            if (suggestions.Count > 0)
                message += " Did you mean: " + string.Join(", ", suggestions) + "?";
            message += " If the asset was created or renamed outside Unity, refresh or reimport so AssetDatabase can pick up the latest path.";
            return message;
        }

        private static List<string> FindSimilarAssetPaths(string normalizedSource, int maxSuggestions)
        {
            var targetName = Path.GetFileNameWithoutExtension(normalizedSource) ?? normalizedSource;
            var targetFileName = Path.GetFileName(normalizedSource) ?? normalizedSource;
            var targetExtension = Path.GetExtension(normalizedSource) ?? string.Empty;
            var matches = new List<(string path, int score)>();

            foreach (var candidate in AssetDatabase.GetAllAssetPaths())
            {
                if (!candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var score = 0;
                if (candidate.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 3;
                if (string.Equals(Path.GetFileName(candidate), targetFileName, StringComparison.OrdinalIgnoreCase))
                    score += 4;
                if (string.Equals(Path.GetExtension(candidate), targetExtension, StringComparison.OrdinalIgnoreCase))
                    score += 1;
                if (score > 0)
                    matches.Add((candidate, score));
            }

            matches.Sort((left, right) =>
            {
                var scoreCompare = right.score.CompareTo(left.score);
                return scoreCompare != 0
                    ? scoreCompare
                    : string.Compare(left.path, right.path, StringComparison.OrdinalIgnoreCase);
            });

            var suggestions = new List<string>();
            foreach (var match in matches)
            {
                if (suggestions.Contains(match.path))
                    continue;
                suggestions.Add(match.path);
                if (suggestions.Count >= maxSuggestions)
                    break;
            }

            return suggestions;
        }

        private static Dictionary<string, object> ParseParameters(string paramsJson)
        {
            return MiniJson.Deserialize(paramsJson) as Dictionary<string, object>
                ?? throw new ArgumentException("Invalid parameters");
        }

        private static string RequirePathParameter(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var valueObject) || valueObject == null)
                throw new ArgumentException($"Missing '{key}' parameter");

            var value = valueObject.ToString();
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Missing '{key}' parameter");

            return value;
        }

        private static bool TryGetOptionalBool(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var valueObject)
                && valueObject != null
                && Convert.ToBoolean(valueObject);
        }

        private static List<MoveRequest> ParseMoveRequests(object movesObject)
        {
            var requests = new List<MoveRequest>();

            if (movesObject is List<object> list)
            {
                foreach (var item in list)
                {
                    if (!(item is Dictionary<string, object> entry))
                        throw new ArgumentException("Each bulk move entry must be an object");

                    requests.Add(new MoveRequest(
                        RequireMoveEntry(entry, "from"),
                        RequireMoveEntry(entry, "to")));
                }
            }
            else if (movesObject is Dictionary<string, object> map)
            {
                foreach (var entry in map)
                {
                    if (entry.Value == null)
                        throw new ArgumentException($"Missing destination for bulk move source '{entry.Key}'");
                    requests.Add(new MoveRequest(entry.Key, entry.Value.ToString()));
                }
            }
            else
            {
                throw new ArgumentException("'moves' must be a JSON array or object map");
            }

            return requests;
        }

        private static string RequireMoveEntry(Dictionary<string, object> entry, string key)
        {
            if (!entry.TryGetValue(key, out var valueObject) || valueObject == null)
                throw new ArgumentException($"Bulk move entry missing '{key}'");

            var value = valueObject.ToString();
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Bulk move entry missing '{key}'");

            return value;
        }

        private static Dictionary<string, object> AddMoveIndex(Dictionary<string, object> payload, int index)
        {
            payload["index"] = index;
            return payload;
        }

        private static bool GetBool(Dictionary<string, object> payload, string key)
        {
            return payload.TryGetValue(key, out var valueObject)
                && valueObject != null
                && Convert.ToBoolean(valueObject);
        }

        private readonly struct MoveRequest
        {
            public MoveRequest(string sourcePath, string destination)
            {
                SourcePath = sourcePath;
                Destination = destination;
            }

            public string SourcePath { get; }
            public string Destination { get; }
        }

        private readonly struct PreparedMove
        {
            public PreparedMove(int index, string sourcePath, ValidatedMove move)
            {
                Index = index;
                SourcePath = sourcePath;
                Move = move;
            }

            public int Index { get; }
            public string SourcePath { get; }
            public ValidatedMove Move { get; }
        }

        private readonly struct AssetIdentity
        {
            public AssetIdentity(string guid, string name, string type, bool isFolder)
            {
                Guid = guid ?? string.Empty;
                Name = name;
                Type = type;
                IsFolder = isFolder;
            }

            public string Guid { get; }
            public string Name { get; }
            public string Type { get; }
            public bool IsFolder { get; }
        }

        private readonly struct ValidatedMove
        {
            public ValidatedMove(AssetIdentity identity, string resolvedDestination, bool changed)
            {
                Identity = identity;
                ResolvedDestination = resolvedDestination;
                Changed = changed;
            }

            public AssetIdentity Identity { get; }
            public string ResolvedDestination { get; }
            public bool Changed { get; }
        }

    }
}
