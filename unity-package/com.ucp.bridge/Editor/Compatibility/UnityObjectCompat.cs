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
            return scene.handle;
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
