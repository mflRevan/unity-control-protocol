using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UCP.Bridge
{
    internal static class UnityObjectCompat
    {
        public static int GetId(this Object obj)
        {
#if UNITY_6000_5_OR_NEWER
            return unchecked((int)EntityId.ToULong(obj.GetEntityId()));
#else
            return obj.GetInstanceID();
#endif
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

        public static Object ResolveByInstanceId(int instanceId)
        {
#if UNITY_6000_5_OR_NEWER
            return EditorUtility.EntityIdToObject(EntityId.FromULong(unchecked((ulong)instanceId)));
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        public static T ResolveByInstanceId<T>(int instanceId) where T : Object
        {
            return ResolveByInstanceId(instanceId) as T;
        }
    }
}
