using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    public static class PropertyController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("object/get-fields", HandleGetFields);
            router.Register("object/get-property", HandleGetProperty);
            router.Register("object/set-property", HandleSetProperty);
            router.Register("object/set-active", HandleSetActive);
            router.Register("object/set-name", HandleSetName);
        }

        private static object HandleGetFields(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");

            var componentType = p.TryGetValue("component", out var cObj) ? cObj?.ToString() : null;
            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);

            if (componentType != null)
            {
                var comp = FindComponent(go, componentType);
                return new Dictionary<string, object>
                {
                    ["instanceId"] = instanceId,
                    ["name"] = go.name,
                    ["component"] = componentType,
                    ["fields"] = SerializeComponentFields(comp)
                };
            }

            // Return fields for all components
            var allComponents = new List<object>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                allComponents.Add(new Dictionary<string, object>
                {
                    ["type"] = c.GetType().Name,
                    ["fields"] = SerializeComponentFields(c)
                });
            }

            return new Dictionary<string, object>
            {
                ["instanceId"] = instanceId,
                ["name"] = go.name,
                ["components"] = allComponents
            };
        }

        private static object HandleGetProperty(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("component", out var cObj) || cObj == null)
                throw new ArgumentException("Missing 'component' parameter");
            if (!p.TryGetValue("property", out var propObj) || propObj == null)
                throw new ArgumentException("Missing 'property' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);
            var comp = FindComponent(go, cObj.ToString());
            string propName = propObj.ToString();

            var value = GetPropertyValue(comp, propName);
            return new Dictionary<string, object>
            {
                ["instanceId"] = instanceId,
                ["component"] = cObj.ToString(),
                ["property"] = propName,
                ["value"] = ConvertToJson(value),
                ["type"] = value != null ? value.GetType().Name : "null"
            };
        }

        private static object HandleSetProperty(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("component", out var cObj) || cObj == null)
                throw new ArgumentException("Missing 'component' parameter");
            if (!p.TryGetValue("property", out var propObj) || propObj == null)
                throw new ArgumentException("Missing 'property' parameter");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("Missing 'value' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);
            var comp = FindComponent(go, cObj.ToString());
            string propName = propObj.ToString();

            Undo.RecordObject(comp, $"UCP Set {propName}");
            SetPropertyValue(comp, propName, p["value"]);
            EditorUtility.SetDirty(comp);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, comp.GetType().Name);

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["component"] = cObj.ToString(),
                ["property"] = propName
            };
        }

        private static object HandleSetActive(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("active", out var activeObj))
                throw new ArgumentException("Missing 'active' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);

            Undo.RecordObject(go, "UCP Set Active");
            go.SetActive(Convert.ToBoolean(activeObj));
            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "GameObject");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["active"] = go.activeSelf
            };
        }

        private static object HandleSetName(string paramsJson)
        {
            var p = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>;
            if (p == null || !p.TryGetValue("instanceId", out var idObj))
                throw new ArgumentException("Missing 'instanceId' parameter");
            if (!p.TryGetValue("name", out var nameObj) || nameObj == null)
                throw new ArgumentException("Missing 'name' parameter");

            int instanceId = Convert.ToInt32(idObj);
            var go = FindGameObject(instanceId);

            Undo.RecordObject(go, "UCP Rename");
            go.name = nameObj.ToString();
            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneChangeTracker.RecordGameObjectChange(go, "GameObject");

            return new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["instanceId"] = instanceId,
                ["name"] = go.name
            };
        }

        // ---- Reflection helpers ----

        private static List<object> SerializeComponentFields(Component comp)
        {
            var fields = new List<object>();
            var type = comp.GetType();

            // Use SerializedObject for reliable Unity property enumeration
            var so = new SerializedObject(comp);
            try
            {
                so.Update();
                var prop = so.GetIterator();
                if (prop.NextVisible(true))
                {
                    do
                    {
                        var fieldDict = new Dictionary<string, object>
                        {
                            ["name"] = prop.name,
                            ["displayName"] = prop.displayName,
                            ["type"] = prop.propertyType.ToString(),
                            ["editable"] = !prop.isReadOnly(true)
                        };

                        try
                        {
                            fieldDict["value"] = SerializedPropertyToValue(prop);
                        }
                        catch
                        {
                            fieldDict["value"] = "<unreadable>";
                        }

                        fields.Add(fieldDict);
                    }
                    while (prop.NextVisible(false));
                }
            }
            finally
            {
                so.Dispose();
            }

            return fields;
        }

        private static object SerializedPropertyToValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return (double)prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
                case SerializedPropertyType.ObjectReference:
                    return ObjectReferenceResolver.Serialize(prop.objectReferenceValue);
                case SerializedPropertyType.LayerMask:
                    return prop.intValue;
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
                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue;
                    return new List<object> { (double)q.x, (double)q.y, (double)q.z, (double)q.w };
                case SerializedPropertyType.AnimationCurve:
                    return "<AnimationCurve>";
                case SerializedPropertyType.Gradient:
                    return "<Gradient>";
                default:
                    return $"<{prop.propertyType}>";
            }
        }

        private static object GetPropertyValue(Component comp, string propertyName)
        {
            var so = new SerializedObject(comp);
            try
            {
                so.Update();
                var prop = so.FindProperty(propertyName);
                if (prop != null)
                    return SerializedPropertyToValue(prop);
            }
            finally
            {
                so.Dispose();
            }

            // Fallback to reflection
            var type = comp.GetType();
            var fi = type.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null)
                return fi.GetValue(comp);

            var pi = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanRead)
                return pi.GetValue(comp);

            throw new ArgumentException($"Property '{propertyName}' not found on {type.Name}");
        }

        private static void SetPropertyValue(Component comp, string propertyName, object jsonValue)
        {
            var so = new SerializedObject(comp);
            try
            {
                so.Update();
                var prop = so.FindProperty(propertyName);
                if (prop != null)
                {
                    SetSerializedPropertyValue(prop, jsonValue);
                    so.ApplyModifiedProperties();
                    return;
                }
            }
            finally
            {
                so.Dispose();
            }

            // Fallback to reflection
            var type = comp.GetType();
            var fi = type.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null)
            {
                fi.SetValue(comp, ConvertFromJson(jsonValue, fi.FieldType));
                return;
            }

            var pi = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite)
            {
                pi.SetValue(comp, ConvertFromJson(jsonValue, pi.PropertyType));
                return;
            }

            throw new ArgumentException($"Property '{propertyName}' not found or not writable on {type.Name}");
        }

        private static void SetSerializedPropertyValue(SerializedProperty prop, object value)
        {
            if (prop.isArray && prop.propertyType == SerializedPropertyType.Generic)
            {
                SetSerializedArrayValue(prop, value);
                return;
            }

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
                    var cArr = value as List<object>;
                    if (cArr != null && cArr.Count >= 3)
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
                        if (idx >= 0)
                            prop.enumValueIndex = idx;
                        else if (int.TryParse(enumStr, out int enumIdx))
                            prop.enumValueIndex = enumIdx;
                        else
                            throw new ArgumentException($"Invalid enum value: {enumStr}");
                    }
                    else
                    {
                        prop.enumValueIndex = Convert.ToInt32(value);
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    var v2Arr = value as List<object>;
                    if (v2Arr != null && v2Arr.Count >= 2)
                        prop.vector2Value = new Vector2(
                            Convert.ToSingle(v2Arr[0]),
                            Convert.ToSingle(v2Arr[1]));
                    break;
                case SerializedPropertyType.Vector3:
                    var v3Arr = value as List<object>;
                    if (v3Arr != null && v3Arr.Count >= 3)
                        prop.vector3Value = new Vector3(
                            Convert.ToSingle(v3Arr[0]),
                            Convert.ToSingle(v3Arr[1]),
                            Convert.ToSingle(v3Arr[2]));
                    break;
                case SerializedPropertyType.Vector4:
                    var v4Arr = value as List<object>;
                    if (v4Arr != null && v4Arr.Count >= 4)
                        prop.vector4Value = new Vector4(
                            Convert.ToSingle(v4Arr[0]),
                            Convert.ToSingle(v4Arr[1]),
                            Convert.ToSingle(v4Arr[2]),
                            Convert.ToSingle(v4Arr[3]));
                    break;
                case SerializedPropertyType.Quaternion:
                    var qArr = value as List<object>;
                    if (qArr != null && qArr.Count >= 4)
                        prop.quaternionValue = new Quaternion(
                            Convert.ToSingle(qArr[0]),
                            Convert.ToSingle(qArr[1]),
                            Convert.ToSingle(qArr[2]),
                            Convert.ToSingle(qArr[3]));
                    break;
                case SerializedPropertyType.Rect:
                    var rDict = value as Dictionary<string, object>;
                    if (rDict != null)
                        prop.rectValue = new Rect(
                            Convert.ToSingle(rDict["x"]),
                            Convert.ToSingle(rDict["y"]),
                            Convert.ToSingle(rDict["width"]),
                            Convert.ToSingle(rDict["height"]));
                    break;
                case SerializedPropertyType.Bounds:
                    var bDict = value as Dictionary<string, object>;
                    if (bDict != null)
                    {
                        var center = bDict["center"] as List<object>;
                        var size = bDict["size"] as List<object>;
                        if (center != null && size != null)
                            prop.boundsValue = new Bounds(
                                new Vector3(Convert.ToSingle(center[0]), Convert.ToSingle(center[1]), Convert.ToSingle(center[2])),
                                new Vector3(Convert.ToSingle(size[0]), Convert.ToSingle(size[1]), Convert.ToSingle(size[2])));
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = ObjectReferenceResolver.Resolve(value, prop.displayName);
                    if (value != null && prop.objectReferenceValue == null)
                        throw new ArgumentException($"Unable to assign object reference to '{prop.displayName}'");
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                default:
                    throw new ArgumentException($"Cannot set property of type {prop.propertyType}");
            }
        }

        private static void SetSerializedArrayValue(SerializedProperty prop, object value)
        {
            if (!(value is IList items))
                throw new ArgumentException($"Property '{prop.displayName}' expects an array value");

            prop.arraySize = items.Count;
            for (var index = 0; index < items.Count; index++)
            {
                var element = prop.GetArrayElementAtIndex(index);
                SetSerializedPropertyValue(element, items[index]);
            }
        }

        private static object ConvertToJson(object value)
        {
            if (value == null) return null;
            if (value is int || value is long || value is float || value is double ||
                value is bool || value is string)
                return value;
            if (value is Vector2 v2)
                return new List<object> { (double)v2.x, (double)v2.y };
            if (value is Vector3 v3)
                return new List<object> { (double)v3.x, (double)v3.y, (double)v3.z };
            if (value is Vector4 v4)
                return new List<object> { (double)v4.x, (double)v4.y, (double)v4.z, (double)v4.w };
            if (value is Quaternion q)
                return new List<object> { (double)q.x, (double)q.y, (double)q.z, (double)q.w };
            if (value is Color c)
                return new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
            if (value is UnityEngine.Object uObj)
                return ObjectReferenceResolver.Serialize(uObj);
            return value.ToString();
        }

        private static object ConvertFromJson(object jsonValue, Type targetType)
        {
            if (jsonValue == null) return null;
            if (targetType == typeof(int)) return Convert.ToInt32(jsonValue);
            if (targetType == typeof(float)) return Convert.ToSingle(jsonValue);
            if (targetType == typeof(double)) return Convert.ToDouble(jsonValue);
            if (targetType == typeof(bool)) return Convert.ToBoolean(jsonValue);
            if (targetType == typeof(string)) return jsonValue.ToString();
            if (targetType == typeof(long)) return Convert.ToInt64(jsonValue);

            if (targetType == typeof(Vector2) && jsonValue is List<object> v2)
                return new Vector2(Convert.ToSingle(v2[0]), Convert.ToSingle(v2[1]));
            if (targetType == typeof(Vector3) && jsonValue is List<object> v3)
                return new Vector3(Convert.ToSingle(v3[0]), Convert.ToSingle(v3[1]), Convert.ToSingle(v3[2]));
            if (targetType == typeof(Vector4) && jsonValue is List<object> v4)
                return new Vector4(Convert.ToSingle(v4[0]), Convert.ToSingle(v4[1]), Convert.ToSingle(v4[2]), Convert.ToSingle(v4[3]));
            if (targetType == typeof(Quaternion) && jsonValue is List<object> qv)
                return new Quaternion(Convert.ToSingle(qv[0]), Convert.ToSingle(qv[1]), Convert.ToSingle(qv[2]), Convert.ToSingle(qv[3]));
            if (targetType == typeof(Color) && jsonValue is List<object> cv)
                return new Color(Convert.ToSingle(cv[0]), Convert.ToSingle(cv[1]), Convert.ToSingle(cv[2]),
                    cv.Count >= 4 ? Convert.ToSingle(cv[3]) : 1f);

            return Convert.ChangeType(jsonValue, targetType);
        }

        private static GameObject FindGameObject(int instanceId)
        {
            var obj = UnityObjectCompat.ResolveByInstanceId<GameObject>(instanceId);
            if (obj != null) return obj;

            // Search in loaded scenes
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchy(root, instanceId);
                    if (found != null) return found;
                }
            }

            throw new ArgumentException($"GameObject not found: {instanceId}");
        }

        private static GameObject FindInHierarchy(GameObject go, int instanceId)
        {
            if (go.GetInstanceID() == instanceId) return go;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var found = FindInHierarchy(go.transform.GetChild(i).gameObject, instanceId);
                if (found != null) return found;
            }
            return null;
        }

        private static Component FindComponent(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName || c.GetType().FullName == typeName)
                    return c;
            }
            throw new ArgumentException($"Component '{typeName}' not found on '{go.name}'");
        }

        // Extension-like helper for SerializedProperty.isReadOnly
        // which doesn't exist as a public API in older Unity
    }

    internal static class SerializedPropertyExtensions
    {
        internal static bool isReadOnly(this SerializedProperty prop, bool includeChildren)
        {
            // Check if the property path suggests it's a built-in non-editable field
            string path = prop.propertyPath;
            if (path == "m_Script") return true;
            if (path == "m_ObjectHideFlags") return true;
            return false;
        }
    }
}



