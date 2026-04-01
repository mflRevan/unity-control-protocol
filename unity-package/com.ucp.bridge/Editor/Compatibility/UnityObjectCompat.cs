using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    internal static class UnityObjectCompat
    {
        public static Object ResolveByInstanceId(int instanceId)
        {
            return EditorUtility.InstanceIDToObject(instanceId);
        }

        public static T ResolveByInstanceId<T>(int instanceId) where T : Object
        {
            return ResolveByInstanceId(instanceId) as T;
        }
    }
}
