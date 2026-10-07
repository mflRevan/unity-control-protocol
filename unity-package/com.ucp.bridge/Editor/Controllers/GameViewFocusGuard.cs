using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// Keeps a ucp-initiated play session from activating the editor's OS window.
    ///
    /// The Game view's default "Play Focused" behaviour focuses the view when play mode starts,
    /// and focusing a docked view activates the main editor window, which on Windows takes the
    /// foreground away from whatever the person was using. Unity 2022.2+ has "Play Unfocused"
    /// for exactly this (the same setting the Game view toolbar offers); the guard switches
    /// every open Game view to it for the session and puts the previous value back when play
    /// mode ends. The switch survives the play-mode domain reload through SessionState.
    /// </summary>
    internal static class GameViewFocusGuard
    {
        private const string PreviousKey = "ucp.play.gameViewBehavior.previous";
        private static Type s_gameViewType;
        private static PropertyInfo s_behaviorProperty;
        private static bool s_lookedUp;

        /// <summary>Called right before play mode is entered. Returns false when Unity has no such setting.</summary>
        public static bool SuppressForPlay()
        {
            if (!Resolve())
                return false;
            string previous = null;
            foreach (var view in OpenGameViews())
            {
                try
                {
                    var current = s_behaviorProperty.GetValue(view);
                    previous ??= current?.ToString();
                    s_behaviorProperty.SetValue(view, Enum.Parse(s_behaviorProperty.PropertyType, "PlayUnfocused"));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UCP] Could not switch a Game view to Play Unfocused: {e.Message}");
                }
            }
            if (previous != null && previous != "PlayUnfocused")
                SessionState.SetString(PreviousKey, previous);
            return true;
        }

        /// <summary>Called when play mode has ended; restores the Game views' previous behaviour.</summary>
        public static void RestoreAfterPlay()
        {
            var previous = SessionState.GetString(PreviousKey, "");
            if (string.IsNullOrEmpty(previous))
                return;
            SessionState.EraseString(PreviousKey);
            if (!Resolve())
                return;
            foreach (var view in OpenGameViews())
            {
                try { s_behaviorProperty.SetValue(view, Enum.Parse(s_behaviorProperty.PropertyType, previous)); }
                catch (Exception) { /* enum renamed between versions; leave the view as it is */ }
            }
        }

        private static bool Resolve()
        {
            if (s_lookedUp)
                return s_behaviorProperty != null;
            s_lookedUp = true;
            s_gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            s_behaviorProperty = s_gameViewType?.GetProperty("enterPlayModeBehavior",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (s_behaviorProperty == null || !s_behaviorProperty.CanWrite || !s_behaviorProperty.PropertyType.IsEnum
                || !Enum.GetNames(s_behaviorProperty.PropertyType).Contains("PlayUnfocused"))
                s_behaviorProperty = null;
            return s_behaviorProperty != null;
        }

        private static IEnumerable<EditorWindow> OpenGameViews()
        {
            return Resources.FindObjectsOfTypeAll(s_gameViewType).OfType<EditorWindow>();
        }
    }
}
