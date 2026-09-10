using UnityEditor;

namespace UCP.Bridge
{
    /// <summary>
    /// AssetDatabase calls that changed shape inside the supported range (2021.3 and newer).
    /// </summary>
    internal static class AssetDatabaseCompat
    {
        /// <summary>
        /// Whether an asset exists at <paramref name="assetPath"/>. <c>AssetDatabase.AssetPathExists</c>
        /// only arrived in 2023.1; older editors answer the same question through the GUID lookup,
        /// restricted to assets that still exist so a just-deleted path does not read as present.
        /// </summary>
        public static bool AssetPathExists(string assetPath)
        {
#if UNITY_2023_1_OR_NEWER
            return AssetDatabase.AssetPathExists(assetPath);
#else
            return !string.IsNullOrEmpty(
                AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets));
#endif
        }
    }
}
