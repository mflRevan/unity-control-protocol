using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    public static class AssetImportSupport
    {
        private static string s_lastReimportedPathForTests;

        public static string LastReimportedPathForTests => s_lastReimportedPathForTests;

        public static void ClearTestState()
        {
            s_lastReimportedPathForTests = null;
        }

        public static bool SupportsAutomaticReimport(string requestedPath)
        {
            var assetPath = GetPrimaryAssetPath(requestedPath);
            return !string.IsNullOrEmpty(assetPath) && IsProjectAssetPath(assetPath);
        }

        public static string GetPrimaryAssetPath(string requestedPath)
        {
            var normalized = NormalizePath(requestedPath);
            if (string.IsNullOrEmpty(normalized))
                return normalized;

            if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 5);

            return normalized;
        }

        public static AssetImporter ResolveImporter(string requestedPath)
        {
            var assetPath = GetPrimaryAssetPath(requestedPath);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("Missing 'path' parameter");

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                throw new ArgumentException($"No asset importer found for: {requestedPath}");

            return importer;
        }

        public static Dictionary<string, object> ReimportOrDescribe(string requestedPath, bool noReimport)
        {
            if (noReimport)
                return CreateReimportResult(requestedPath, GetPrimaryAssetPath(requestedPath), false, true, "Skipped by request");

            if (!SupportsAutomaticReimport(requestedPath))
                return CreateReimportResult(
                    requestedPath,
                    GetPrimaryAssetPath(requestedPath),
                    false,
                    true,
                    "Path is outside Unity-managed Assets/ or Packages/");

            return Reimport(requestedPath);
        }

        public static Dictionary<string, object> Reimport(string requestedPath, bool forceSynchronous = true)
        {
            var assetPath = GetPrimaryAssetPath(requestedPath);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("Missing 'path' parameter");
            if (!IsProjectAssetPath(assetPath))
                throw new ArgumentException($"Path is not under Assets/ or Packages/: {requestedPath}");

            var options = ImportAssetOptions.ForceUpdate;
            if (forceSynchronous)
                options |= ImportAssetOptions.ForceSynchronousImport;

            AssetDatabase.ImportAsset(assetPath, options);
            RecordReimport(assetPath);
            return CreateReimportResult(requestedPath, assetPath, true, false, null);
        }

        public static Dictionary<string, object> SaveImporterSettings(string requestedPath, AssetImporter importer, bool noReimport)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            var assetPath = GetPrimaryAssetPath(requestedPath);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("Missing 'path' parameter");

            EditorUtility.SetDirty(importer);

            if (noReimport)
            {
                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                return CreateReimportResult(requestedPath, assetPath, false, true, "Skipped by request");
            }

            importer.SaveAndReimport();
            RecordReimport(assetPath);
            return CreateReimportResult(requestedPath, assetPath, true, false, null);
        }

        private static bool IsProjectAssetPath(string assetPath)
        {
            return assetPath.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || assetPath.Equals("Packages", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return path?.Trim().Replace('\\', '/');
        }

        private static void RecordReimport(string assetPath)
        {
            s_lastReimportedPathForTests = assetPath;
        }

        private static Dictionary<string, object> CreateReimportResult(
            string requestedPath,
            string assetPath,
            bool reimported,
            bool skipped,
            string reason)
        {
            var importer = !string.IsNullOrEmpty(assetPath)
                ? AssetImporter.GetAtPath(assetPath)
                : null;

            var result = new Dictionary<string, object>
            {
                ["requestedPath"] = requestedPath,
                ["assetPath"] = assetPath,
                ["reimported"] = reimported,
                ["skipped"] = skipped
            };

            if (!string.IsNullOrEmpty(reason))
                result["reason"] = reason;

            if (importer != null)
                result["importerType"] = importer.GetType().Name;

            return result;
        }
    }

    internal static class SerializedPropertyControllerSupport
    {
        public static Dictionary<string, object> Describe(SerializedProperty property)
        {
            return new Dictionary<string, object>
            {
                ["name"] = property.name,
                ["propertyPath"] = property.propertyPath,
                ["displayName"] = property.displayName,
                ["type"] = property.propertyType.ToString(),
                ["value"] = ReadValue(property)
            };
        }

        public static object ReadValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return (double)property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    var color = property.colorValue;
                    return new List<object> { (double)color.r, (double)color.g, (double)color.b, (double)color.a };
                case SerializedPropertyType.ObjectReference:
                    return ObjectReferenceResolver.Serialize(property.objectReferenceValue);
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    var vector2 = property.vector2Value;
                    return new List<object> { (double)vector2.x, (double)vector2.y };
                case SerializedPropertyType.Vector3:
                    var vector3 = property.vector3Value;
                    return new List<object> { (double)vector3.x, (double)vector3.y, (double)vector3.z };
                case SerializedPropertyType.Vector4:
                    var vector4 = property.vector4Value;
                    return new List<object> { (double)vector4.x, (double)vector4.y, (double)vector4.z, (double)vector4.w };
                case SerializedPropertyType.Quaternion:
                    var quaternion = property.quaternionValue;
                    return new List<object> { (double)quaternion.x, (double)quaternion.y, (double)quaternion.z, (double)quaternion.w };
                case SerializedPropertyType.Rect:
                    var rect = property.rectValue;
                    return new Dictionary<string, object>
                    {
                        ["x"] = (double)rect.x,
                        ["y"] = (double)rect.y,
                        ["width"] = (double)rect.width,
                        ["height"] = (double)rect.height
                    };
                case SerializedPropertyType.Bounds:
                    var bounds = property.boundsValue;
                    return new Dictionary<string, object>
                    {
                        ["center"] = new List<object> { (double)bounds.center.x, (double)bounds.center.y, (double)bounds.center.z },
                        ["size"] = new List<object> { (double)bounds.size.x, (double)bounds.size.y, (double)bounds.size.z }
                    };
                case SerializedPropertyType.ArraySize:
                    return property.intValue;
                case SerializedPropertyType.LayerMask:
                    return property.intValue;
                case SerializedPropertyType.Generic:
                    if (property.isArray)
                    {
                        var items = new List<object>();
                        for (var index = 0; index < property.arraySize; index++)
                            items.Add(ReadValue(property.GetArrayElementAtIndex(index)));
                        return items;
                    }
                    return $"<{property.propertyType}>";
                default:
                    return $"<{property.propertyType}>";
            }
        }

        public static void WriteFieldValue(SerializedObject serializedObject, string ownerName, string fieldName, object value)
        {
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
                throw new ArgumentException($"Field '{fieldName}' not found on {ownerName}");

            WriteValue(property, value);
        }

        public static void WriteValue(SerializedProperty property, object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = value?.ToString() ?? string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    if (value is List<object> colorArray && colorArray.Count >= 3)
                    {
                        property.colorValue = new Color(
                            Convert.ToSingle(colorArray[0]),
                            Convert.ToSingle(colorArray[1]),
                            Convert.ToSingle(colorArray[2]),
                            colorArray.Count >= 4 ? Convert.ToSingle(colorArray[3]) : 1f);
                        break;
                    }
                    throw new ArgumentException($"Expected an array for '{property.displayName}'");
                case SerializedPropertyType.Enum:
                    if (value is string enumString)
                    {
                        var enumIndex = Array.IndexOf(property.enumDisplayNames, enumString);
                        if (enumIndex >= 0)
                        {
                            property.enumValueIndex = enumIndex;
                            break;
                        }

                        if (int.TryParse(enumString, out var parsedEnumIndex))
                        {
                            property.enumValueIndex = parsedEnumIndex;
                            break;
                        }
                    }
                    else
                    {
                        property.enumValueIndex = Convert.ToInt32(value);
                        break;
                    }

                    throw new ArgumentException($"Enum value '{value}' is not valid for '{property.displayName}'");
                case SerializedPropertyType.Vector2:
                    if (value is List<object> vector2 && vector2.Count >= 2)
                    {
                        property.vector2Value = new Vector2(
                            Convert.ToSingle(vector2[0]),
                            Convert.ToSingle(vector2[1]));
                        break;
                    }
                    throw new ArgumentException($"Expected a 2-item array for '{property.displayName}'");
                case SerializedPropertyType.Vector3:
                    if (value is List<object> vector3 && vector3.Count >= 3)
                    {
                        property.vector3Value = new Vector3(
                            Convert.ToSingle(vector3[0]),
                            Convert.ToSingle(vector3[1]),
                            Convert.ToSingle(vector3[2]));
                        break;
                    }
                    throw new ArgumentException($"Expected a 3-item array for '{property.displayName}'");
                case SerializedPropertyType.Vector4:
                    if (value is List<object> vector4 && vector4.Count >= 4)
                    {
                        property.vector4Value = new Vector4(
                            Convert.ToSingle(vector4[0]),
                            Convert.ToSingle(vector4[1]),
                            Convert.ToSingle(vector4[2]),
                            Convert.ToSingle(vector4[3]));
                        break;
                    }
                    throw new ArgumentException($"Expected a 4-item array for '{property.displayName}'");
                case SerializedPropertyType.Quaternion:
                    if (value is List<object> quaternion && quaternion.Count >= 4)
                    {
                        property.quaternionValue = new Quaternion(
                            Convert.ToSingle(quaternion[0]),
                            Convert.ToSingle(quaternion[1]),
                            Convert.ToSingle(quaternion[2]),
                            Convert.ToSingle(quaternion[3]));
                        break;
                    }
                    throw new ArgumentException($"Expected a 4-item array for '{property.displayName}'");
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = ObjectReferenceResolver.Resolve(value, property.displayName);
                    if (value != null && property.objectReferenceValue == null)
                        throw new ArgumentException($"Unable to assign object reference to '{property.displayName}'");
                    break;
                case SerializedPropertyType.LayerMask:
                    property.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Generic:
                    if (property.isArray && value is List<object> items)
                    {
                        property.arraySize = items.Count;
                        for (var index = 0; index < items.Count; index++)
                            WriteValue(property.GetArrayElementAtIndex(index), items[index]);
                        break;
                    }
                    throw new ArgumentException($"Cannot write property of type {property.propertyType}");
                default:
                    throw new ArgumentException($"Cannot write property of type {property.propertyType}");
            }
        }
    }
}
