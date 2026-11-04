using StillWrong.Oracle;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
namespace StillWrong.Editor.UI
{
    public sealed class StillWrongChangePopup : EditorWindow
    {
        private string _targetName = string.Empty;
        private string _targetPath = string.Empty;
        private string _sentence = string.Empty;
        private string _statusLine = string.Empty;
        private static bool _armed;
        public static void OpenForPath(string path)
        {
            StillWrongBugSheet.ShowChange(path);
        }
        public static StillWrongChangePopup FocusPopup()
        {
            try
            {
                var window = GetWindow<StillWrongChangePopup>();
                if (window != null)
                {
                    window.Show();
                    window.Focus();
                }
                return window;
            }
            catch
            {
                return null;
            }
        }
        public string PopupTargetPath()
        {
            return _targetPath;
        }
        public string PopupTargetName()
        {
            return _targetName;
        }
        public string PopupSentence()
        {
            return _sentence;
        }
        public static bool IsArmed()
        {
            return _armed;
        }
        [Shortcut("StillWrong/Arm Point Click", KeyCode.F10)]
        private static void ArmShortcut()
        {
            try
            {
                _armed = true;
                SceneView.duringSceneGui -= OnArmedSceneGui;
                SceneView.duringSceneGui += OnArmedSceneGui;
                StillWrongChangeGameClick.Arm();
            }
            catch
            {
            }
        }
        private static void OnArmedSceneGui(SceneView view)
        {
            if (!_armed)
            {
                return;
            }
            Event evt = Event.current;
            if (evt == null || evt.type != EventType.MouseDown || evt.button != 0)
            {
                return;
            }
            GameObject picked = null;
            try
            {
                picked = HandleUtility.PickGameObject(evt.mousePosition, false);
            }
            catch
            {
                picked = null;
            }
            _armed = false;
            try
            {
                SceneView.duringSceneGui -= OnArmedSceneGui;
            }
            catch
            {
            }
            StillWrongChangeGameClick.Disarm();
            if (picked != null)
            {
                string path = string.Empty;
                try
                {
                    path = UnitySymptomOracleRunner.GetHierarchyPath(picked);
                }
                catch
                {
                }
                if (!string.IsNullOrEmpty(path))
                {
                    OpenForPath(path);
                }
            }
            evt.Use();
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_targetName) ? "No target" : _targetName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_targetPath, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("What should change?");
            _sentence = EditorGUILayout.TextArea(_sentence, GUILayout.MinHeight(60));
            if (GUILayout.Button("Send", GUILayout.Height(28)))
            {
                StillWrong.Dispatch.DispatchPacket packet = BuildPacket();
                StillWrong.Dispatch.DispatchReceipt receipt = StillWrong.Dispatch.DispatchService.SendPacket(packet, StillWrongSettings.PreferredAgent(), StillWrongSettings.BranchPrefix());
                if (receipt.Sent && receipt.Route == "muse-handoff")
                {
                    _statusLine = "Queued packet for Muse intake on " + receipt.Branch + ".";
                }
                else if (receipt.Sent)
                {
                    _statusLine = "Sent packet via " + receipt.Route + " on " + receipt.Branch + ".";
                }
                else if (receipt.Saved)
                {
                    _statusLine = "Saved packet to " + receipt.PacketFile + ". No agent connected.";
                }
                else
                {
                    _statusLine = "Send failed: " + receipt.Reason;
                }
            }
            if (!string.IsNullOrEmpty(_statusLine))
            {
                EditorGUILayout.LabelField(_statusLine);
            }
        }
        private StillWrong.Dispatch.DispatchPacket BuildPacket()
        {
            StillWrong.Dispatch.DispatchPacket packet = new StillWrong.Dispatch.DispatchPacket();
            packet.Mode = "Change";
            packet.SessionDir = StillWrong.Editor.Recording.RollingRecorderControl.LastSessionDir();
            packet.Target.Path = _targetPath;
            packet.Target.Name = _targetName;
            packet.CandidatePaths.Add(_targetPath);
            packet.Sentence = _sentence;
            packet.ScenePath = string.Empty;
            try
            {
                packet.ScenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            }
            catch
            {
            }
            return packet;
        }
        private static string LeafName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            int slash = path.LastIndexOf("/");
            if (slash >= 0 && slash + 1 < path.Length)
            {
                return path.Substring(slash + 1);
            }
            return path;
        }
    }
}
