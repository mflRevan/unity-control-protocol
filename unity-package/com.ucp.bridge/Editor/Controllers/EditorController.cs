using UnityEditor;

namespace UCP.Bridge
{
    public static class EditorController
    {
        public static void Register(CommandRouter router)
        {
            router.Register("editor/status", HandleStatus);
            router.Register("editor/state", _ => EditorStateSummary.Capture(LogsController.GetLatestId()));
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
            // Not delayCall: that waits for an inspector update an unfocused editor never does.
            // The short delay lets the response reach the caller before the process goes away.
            Deferred.Run(() => EditorApplication.Exit(0), 0.3);
            return new { status = "ok", message = "Unity editor shutdown requested" };
        }
    }
}
