using System;
using System.IO;
using StillWrong.Oracle;
using StillWrong.Editor.Fixtures;
using StillWrong.Editor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D8PopupState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string TargetName = string.Empty;
        public string TargetPath = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8PopupResult
    {
        public string RunId = string.Empty;
        public string CaseId = "d8-popup";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public string TargetName = string.Empty;
        public string TargetPath = string.Empty;
        public string Mode = "Change";
        public string Sentence = string.Empty;
        public string ClickKind = "scripted-point";
        public bool PopupOpened;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8PopupMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class D8PopupDriver
    {
        private const string PopupFolder = "Library/StillWrong/d8popup";
        private const int MeasureFrame = 6;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string PopupDir()
        {
            return Path.Combine(ProjectRoot(), PopupFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(PopupDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(PopupDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(PopupDir());
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "d8popup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D8PopupState state = new D8PopupState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D8PopupMarker marker = new D8PopupMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(PopupDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
            EditorApplication.isPlaying = true;
            return runId;
        }
        [InitializeOnLoadMethod]
        private static void Register()
        {
            if (_ticked)
            {
                return;
            }
            _ticked = true;
            EditorApplication.update += Tick;
        }
        private static D8PopupState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D8PopupState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D8PopupState state)
        {
            try
            {
                Directory.CreateDirectory(PopupDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D8PopupState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                D8PopupMarker marker = new D8PopupMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(PopupDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }
            }
            catch
            {
            }
        }
        private static void ArmTick()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            D8PopupState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "enterplay")
                {
                    TickEnterPlay(state);
                }
                else if (state.Phase == "measure")
                {
                    TickMeasure(state);
                }
                else if (state.Phase == "exitplay")
                {
                    TickExitPlay(state);
                }
            }
            catch
            {
            }
        }
        private static void TickEnterPlay(D8PopupState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            state.Phase = "measure";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickMeasure(D8PopupState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "measure-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "measure-timeout");
                return;
            }
            if (Time.frameCount - state.StartAbsFrame < MeasureFrame)
            {
                return;
            }
            GameObject body = UnitySymptomOracleRunner.FindGameObjectByPath(DemoSceneFixtureBuilder.TargetHierarchyPath);
            if (body == null)
            {
                Fail(state, "measure-body-missing");
                return;
            }
            state.TargetPath = UnitySymptomOracleRunner.GetHierarchyPath(body);
            state.TargetName = body.name;
            state.Phase = "exitplay";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static void TickExitPlay(D8PopupState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (string.IsNullOrEmpty(state.TargetPath))
            {
                Fail(state, "exitplay-no-target");
                return;
            }
            StillWrongChangePopup.OpenForPath(state.TargetPath);
            D8PopupResult result = new D8PopupResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.TargetName = state.TargetName;
            result.TargetPath = state.TargetPath;
            StillWrongChangePopup popup = EditorWindow.GetWindow<StillWrongChangePopup>();
            result.PopupOpened = popup != null && popup.PopupTargetPath() == state.TargetPath;
            WriteResult(state, result, string.Empty);
            try
            {
                DemoSceneFixtureBuilder.BuildScene();
            }
            catch
            {
            }
            try
            {
                D8PopupMarker marker = new D8PopupMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(PopupDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static void WriteResult(D8PopupState state, D8PopupResult result, string failure)
        {
            try
            {
                D8PopupResult dto = result != null ? result : new D8PopupResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(PopupDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
