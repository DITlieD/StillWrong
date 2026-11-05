using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using StillWrong.Editor.Recording;
namespace StillWrong.Editor.UI
{
    public sealed class StillWrongSettings : EditorWindow
    {
        public const string RetentionMinutesKey = "StillWrong.RetentionMinutes";
        public const string TimelineFpsKey = "StillWrong.TimelineFps";
        public const string PreferredAgentKey = "StillWrong.PreferredAgent";
        public const string BranchPrefixKey = "StillWrong.BranchPrefix";
        public const string AgentCommandKey = "StillWrong.AgentCommand";
        public const string HiddenOverlaysKey = "StillWrong.HiddenOverlays";
        public const string FreezeShortcutId = "StillWrong/Freeze Capture";
        public const string PointShortcutId = "StillWrong/Arm Point Click";
        public static int RetentionMinutes()
        {
            try
            {
                return Mathf.Clamp(EditorPrefs.GetInt(RetentionMinutesKey, 3), 1, 10);
            }
            catch
            {
                return 3;
            }
        }
        public static int TimelineFps()
        {
            try
            {
                return Mathf.Clamp(EditorPrefs.GetInt(TimelineFpsKey, 30), 1, 60);
            }
            catch
            {
                return 30;
            }
        }
        public static string PreferredAgent()
        {
            try
            {
                return EditorPrefs.GetString(PreferredAgentKey, "Muse");
            }
            catch
            {
                return "Muse";
            }
        }
        public static string BranchPrefix()
        {
            try
            {
                return EditorPrefs.GetString(BranchPrefixKey, "stillwrong/");
            }
            catch
            {
                return "stillwrong/";
            }
        }
        public static string AgentCommand()
        {
            try
            {
                return EditorPrefs.GetString(AgentCommandKey, string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }
        // Comma-separated GameObject names hidden while the review is open, for HUD pieces that cover the game view.
        public static string HiddenOverlays()
        {
            try
            {
                return EditorPrefs.GetString(HiddenOverlaysKey, string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }
        [MenuItem("Tools/Still Wrong/Settings")]
        public static void ShowWindow()
        {
            var window = GetWindow<StillWrongSettings>("Still Wrong - Settings");
            window.minSize = new Vector2(360, 340);
            window.Show();
        }
        private void OnGUI()
        {
            bool auto = RollingRecorderControl.IsEnabled();
            bool next = EditorGUILayout.Toggle("Background recording", auto);
            if (next != auto)
            {
                RollingRecorderControl.SetEnabled(next);
            }
            int minutes = EditorGUILayout.IntField("Retention minutes", RetentionMinutes());
            minutes = Mathf.Clamp(minutes, 1, 10);
            try
            {
                EditorPrefs.SetInt(RetentionMinutesKey, minutes);
            }
            catch
            {
            }
            int fps = EditorGUILayout.IntField("Timeline fps", TimelineFps());
            fps = Mathf.Clamp(fps, 1, 60);
            try
            {
                EditorPrefs.SetInt(TimelineFpsKey, fps);
            }
            catch
            {
            }
            EditorGUILayout.LabelField("Freeze hotkey", CurrentBinding(FreezeShortcutId));
            EditorGUILayout.LabelField("Point hotkey", CurrentBinding(PointShortcutId));
            string agent = EditorGUILayout.TextField("Preferred agent", PreferredAgent());
            try
            {
                EditorPrefs.SetString(PreferredAgentKey, agent);
            }
            catch
            {
            }
            string prefix = EditorGUILayout.TextField("Branch prefix", BranchPrefix());
            try
            {
                EditorPrefs.SetString(BranchPrefixKey, prefix);
            }
            catch
            {
            }
            string hidden = EditorGUILayout.TextField("Hide while reviewing", HiddenOverlays());
            try
            {
                EditorPrefs.SetString(HiddenOverlaysKey, hidden);
            }
            catch
            {
            }
            string command = EditorGUILayout.TextField("Agent command", AgentCommand());
            try
            {
                EditorPrefs.SetString(AgentCommandKey, command);
            }
            catch
            {
            }
        }
        private static string CurrentBinding(string shortcutId)
        {
            try
            {
                return ShortcutManager.instance.GetShortcutBinding(shortcutId).ToString();
            }
            catch
            {
                return "not bound";
            }
        }
    }
}
