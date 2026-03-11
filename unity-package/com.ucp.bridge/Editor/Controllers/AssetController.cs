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
            router.Register("asset/create-so", HandleCreateScriptableObject);
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
                filter += $"t:{typeFilter}";

            string[] searchFolders = null;
            if (!string.IsNullOrEmpty(pathFilter))
                searchFolders = new[] { pathFilter };

            string[] guids;
            if (searchFolders != null)
                guids = AssetDatabase.FindAssets(filter.Trim(), searchFolders);
            else
                guids = AssetDatabase.FindAssets(filter.Trim());

            var results = new List<object>();
            int count = Math.Min(guids.Length, maxResults);
            for (int i = 0; i < count; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);

                results.Add(new Dictionary<string, object>
                {
                    ["guid"] = guids[i],
                    ["path"] = assetPath,
                    ["type"] = assetType?.Name ?? "Unknown",
                    ["name"] = System.IO.Path.GetFileNameWithoutExtension(assetPath)
                });
            }

            return new Dictionary<string, object>
            {
                ["results"] = results,
                ["total"] = guids.Length,
                ["returned"] = count
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
                    ["displayName"] = prop.displayName,
                    ["type"] = prop.propertyType.ToString(),
                    ["value"] = ReadSerializedValue(prop)
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
                        fields.Add(new Dictionary<string, object>
                        {
                            ["name"] = iter.name,
                            ["displayName"] = iter.displayName,
                            ["type"] = iter.propertyType.ToString(),
                            ["value"] = ReadSerializedValue(iter)
                        });
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
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                so.Dispose();
                throw new ArgumentException($"Field '{fieldName}' not found on {asset.GetType().Name}");
            }

            Undo.RecordObject(asset, $"UCP Write {fieldName}");
            WriteSerializedValue(prop, p["value"]);
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

        // ---- Serialized property value reading/writing ----

        private static object ReadSerializedValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return (double)prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                        return new Dictionary<string, object>
                        {
                            ["instanceId"] = prop.objectReferenceValue.GetInstanceID(),
                            ["name"] = prop.objectReferenceValue.name,
                            ["type"] = prop.objectReferenceValue.GetType().Name
                        };
                    return null;
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    return new List<object> { (double)v2.x, (double)v2.y };
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    return new List<object> { (double)v3.x, (double)v3.y, (double)v3.z };
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return new List<object> { (double)v4.x, (double)v4.y, (double)v4.z, (double)v4.w };
                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue;
                    return new List<object> { (double)q.x, (double)q.y, (double)q.z, (double)q.w };
                case SerializedPropertyType.Rect:
                    var r = prop.rectValue;
                    return new Dictionary<string, object>
                    {
                        ["x"] = (double)r.x,
                        ["y"] = (double)r.y,
                        ["width"] = (double)r.width,
                        ["height"] = (double)r.height
                    };
                case SerializedPropertyType.Bounds:
                    var b = prop.boundsValue;
                    return new Dictionary<string, object>
                    {
                        ["center"] = new List<object> { (double)b.center.x, (double)b.center.y, (double)b.center.z },
                        ["size"] = new List<object> { (double)b.size.x, (double)b.size.y, (double)b.size.z }
                    };
                case SerializedPropertyType.ArraySize:
                    return prop.intValue;
                case SerializedPropertyType.LayerMask:
                    return prop.intValue;
                default:
                    return $"<{prop.propertyType}>";
            }
        }

        private static void WriteSerializedValue(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value?.ToString() ?? "";
                    break;
                case SerializedPropertyType.Color:
                    if (value is List<object> cArr && cArr.Count >= 3)
                        prop.colorValue = new Color(
                            Convert.ToSingle(cArr[0]),
                            Convert.ToSingle(cArr[1]),
                            Convert.ToSingle(cArr[2]),
                            cArr.Count >= 4 ? Convert.ToSingle(cArr[3]) : 1f);
                    break;
                case SerializedPropertyType.Enum:
                    if (value is string enumStr)
                    {
                        int idx = Array.IndexOf(prop.enumDisplayNames, enumStr);
                        if (idx >= 0) prop.enumValueIndex = idx;
                        else if (int.TryParse(enumStr, out int enumIdx)) prop.enumValueIndex = enumIdx;
                    }
                    else
                    {
                        prop.enumValueIndex = Convert.ToInt32(value);
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    if (value is List<object> v2 && v2.Count >= 2)
                        prop.vector2Value = new Vector2(Convert.ToSingle(v2[0]), Convert.ToSingle(v2[1]));
                    break;
                case SerializedPropertyType.Vector3:
                    if (value is List<object> v3 && v3.Count >= 3)
                        prop.vector3Value = new Vector3(Convert.ToSingle(v3[0]), Convert.ToSingle(v3[1]), Convert.ToSingle(v3[2]));
                    break;
                case SerializedPropertyType.Vector4:
                    if (value is List<object> v4 && v4.Count >= 4)
                        prop.vector4Value = new Vector4(Convert.ToSingle(v4[0]), Convert.ToSingle(v4[1]), Convert.ToSingle(v4[2]), Convert.ToSingle(v4[3]));
                    break;
                case SerializedPropertyType.Quaternion:
                    if (value is List<object> qArr && qArr.Count >= 4)
                        prop.quaternionValue = new Quaternion(Convert.ToSingle(qArr[0]), Convert.ToSingle(qArr[1]), Convert.ToSingle(qArr[2]), Convert.ToSingle(qArr[3]));
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (value == null)
                        prop.objectReferenceValue = null;
                    else if (value is Dictionary<string, object> refDict && refDict.TryGetValue("instanceId", out var refId))
                        prop.objectReferenceValue = EditorUtility.InstanceIDToObject(Convert.ToInt32(refId));
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                default:
                    throw new ArgumentException($"Cannot write property of type {prop.propertyType}");
            }
        }
    }
}
