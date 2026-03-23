using UnityEditor;

namespace UCP.Bridge
{
    public static class EditorController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("editor/status", HandleStatus);
            router.Register("editor/quit", HandleQuit);
        }

        private static object HandleStatus(string paramsJson)
        {
            return new
            {
                compiling = EditorApplication.isCompiling,
                updating = EditorApplication.isUpdating,
                playing = EditorApplication.isPlaying,
                willChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                timeSinceStartup = EditorApplication.timeSinceStartup
            };
        }

        private static object HandleQuit(string paramsJson)
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return new { status = "ok", message = "Unity editor shutdown requested" };
        }
    }
}
