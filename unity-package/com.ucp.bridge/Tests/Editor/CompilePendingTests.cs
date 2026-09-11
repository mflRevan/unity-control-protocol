using NUnit.Framework;
using UnityEditor;

namespace UCP.Bridge.Tests
{
    /// <summary>
    /// A requested compile counts as compiling before Unity starts it. Without this, a `play`
    /// request that lands in the frames between `compile --no-wait` and the actual compile is
    /// discarded by the reload that follows (Unity 6000.6 stays in edit mode).
    /// </summary>
    public sealed class CompilePendingTests
    {
        [Test]
        public void RequestedCompile_CountsAsPendingUntilUnityPicksItUp()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                Assert.Ignore("the editor is already compiling; the flag would be true regardless");

            Assert.That(BridgeServer.IsCompilePending, Is.False, "nothing was requested yet");
            BridgeServer.NoteCompileRequested();
            Assert.That(BridgeServer.IsCompilePending, Is.True, "a fresh request must read as pending");
            BridgeServer.ClearCompileRequestForTests();
            Assert.That(BridgeServer.IsCompilePending, Is.False);
        }
    }
}
