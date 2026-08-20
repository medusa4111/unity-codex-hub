using UnityEditor;

namespace Codex.UnityBridge.Commands
{
    internal static class EditorStateTracker
    {
        private static string playModeTransition = "none";

        static EditorStateTracker()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static string PlayModeTransition
        {
            get { return playModeTransition; }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode: playModeTransition = "entering"; break;
                case PlayModeStateChange.ExitingPlayMode: playModeTransition = "exiting"; break;
                default: playModeTransition = "none"; break;
            }
        }
    }
}
