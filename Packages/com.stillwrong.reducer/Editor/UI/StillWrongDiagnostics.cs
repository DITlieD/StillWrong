using UnityEditor;
using UnityEngine;
using StillWrong.Editor.Recording;
namespace StillWrong.Editor.UI
{
    public sealed class StillWrongDiagnostics : EditorWindow
    {
        private Vector2 _scrollPos;
        [MenuItem("Tools/Still Wrong/Diagnostics")]
        public static void ShowWindow()
        {
            var window = GetWindow<StillWrongDiagnostics>("Still Wrong - Diagnostics");
            window.minSize = new Vector2(420, 300);
            window.Show();
        }
        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.LabelField("Bug sheet", EditorStyles.boldLabel);
            StillWrongBugSheet sheet = GetWindow<StillWrongBugSheet>();
            if (sheet != null)
            {
                EditorGUILayout.LabelField(sheet.SheetDiagnostics());
            }
            else
            {
                EditorGUILayout.LabelField("No bug sheet open.");
            }
            EditorGUILayout.LabelField("Recorder", EditorStyles.boldLabel);
            string state;
            double retained;
            int chunks;
            long bytes;
            long dropped;
            if (RollingRecorderControl.TryReadStatus(out state, out retained, out chunks, out bytes, out dropped))
            {
                EditorGUILayout.LabelField(state + ", " + retained.ToString("F1") + "s kept, " + chunks + " chunks, " + bytes + " bytes, " + dropped + " dropped");
            }
            else
            {
                EditorGUILayout.LabelField("No recorder status file.");
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
