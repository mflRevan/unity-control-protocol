using System;
using System.Collections.Generic;
using UnityEditor;

namespace UCP.Bridge
{
    /// <summary>
    /// Runs an action on a later editor tick, in a way that also works while the editor is
    /// unfocused and idle. <c>EditorApplication.delayCall</c> does not: it fires after the next
    /// inspector update, which an unfocused editor with no pending events never performs, so a
    /// quit or a recording queued that way waits until someone moves the mouse over the window.
    /// Since ucp stopped dragging the editor to the foreground, that is the common case.
    /// </summary>
    public static class Deferred
    {
        private struct Entry
        {
            public Action Action;
            public double RunAt;
        }

        private static readonly List<Entry> s_entries = new List<Entry>();
        private static bool s_subscribed;

        /// <summary>Run <paramref name="action"/> on the first editor update at least <paramref name="delaySeconds"/> from now.</summary>
        public static void Run(Action action, double delaySeconds = 0)
        {
            if (action == null)
                return;
            lock (s_entries)
            {
                s_entries.Add(new Entry { Action = action, RunAt = EditorApplication.timeSinceStartup + delaySeconds });
                if (!s_subscribed)
                {
                    EditorApplication.update += Tick;
                    s_subscribed = true;
                }
            }
            // An unfocused editor throttles its loop; a queued player-loop update makes the next
            // tick happen promptly instead of whenever the window next gets an event.
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void Tick()
        {
            List<Action> due = null;
            lock (s_entries)
            {
                var now = EditorApplication.timeSinceStartup;
                for (var i = s_entries.Count - 1; i >= 0; i--)
                {
                    if (s_entries[i].RunAt > now)
                        continue;
                    (due ??= new List<Action>()).Add(s_entries[i].Action);
                    s_entries.RemoveAt(i);
                }
                if (s_entries.Count == 0)
                {
                    EditorApplication.update -= Tick;
                    s_subscribed = false;
                }
                else
                    EditorApplication.QueuePlayerLoopUpdate();
            }
            if (due == null)
                return;
            for (var i = due.Count - 1; i >= 0; i--)
            {
                try { due[i](); }
                catch (Exception e) { UnityEngine.Debug.LogError($"[UCP] Deferred action failed: {e}"); }
            }
        }
    }
}
