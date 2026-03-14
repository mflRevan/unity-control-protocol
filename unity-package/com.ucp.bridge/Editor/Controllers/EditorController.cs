using UnityEditor;

namespace UCP.Bridge
{
    public static class EditorController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("editor/quit", HandleQuit);
        }

        private static object HandleQuit(string paramsJson)
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return new { status = "ok", message = "Unity editor shutdown requested" };
        }
    }
}