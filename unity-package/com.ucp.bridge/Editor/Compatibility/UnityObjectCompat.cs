using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    /// <summary>
    /// Object identity across the Unity 6 line. Up to 6000.4 an object is identified by a 32-bit
    /// instance id; from 6000.5 it is a 64-bit <c>EntityId</c> whose upper word is a session
    /// stamp and whose lower word is the classic id, and neither sign- nor zero-extending the
    /// lower word resolves the object again. The wire protocol therefore carries ids as 64-bit
    /// integers everywhere. On older editors the values are the same numbers they always were.
    /// </summary>
    internal static class UnityObjectCompat
    {
        public static long GetId(this Object obj)
        {
#if UNITY_6000_5_OR_NEWER
            return unchecked((long)EntityId.ToULong(obj.GetEntityId()));
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>Reads an id parameter from a parsed JSON payload (int, long, or numeric string).</summary>
        public static long ReadId(object value)
        {
            return System.Convert.ToInt64(value);
        }

        public static long GetSceneHandle(Scene scene)
        {
#if UNITY_6000_5_OR_NEWER
            return unchecked((long)scene.handle.GetRawData());
#else
            // On 6000.0–6000.4, Scene.handle is a SceneHandle with implicit int and
            // uint operators; widening straight to long is ambiguous (CS0457). Pin the
            // int conversion explicitly — handles are 32-bit on these versions.
            return (int)scene.handle;
#endif
        }

        public static Object ResolveByInstanceId(long instanceId)
        {
#if UNITY_6000_5_OR_NEWER
            return EditorUtility.EntityIdToObject(EntityId.FromULong(unchecked((ulong)instanceId)));
#else
            // 32-bit editors cannot have handed out anything wider; treat an out-of-range id as
            // "no such object" rather than truncating it into a different object's id.
            if (instanceId < int.MinValue || instanceId > int.MaxValue)
                return null;
            return EditorUtility.InstanceIDToObject((int)instanceId);
#endif
        }

        public static T ResolveByInstanceId<T>(long instanceId) where T : Object
        {
            return ResolveByInstanceId(instanceId) as T;
        }
    }
}
