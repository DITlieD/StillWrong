using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using StillWrong.Recording;
using StillWrong.Replay;
using StillWrong.Editor.Fixtures;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class C1RunState
    {
        public string Phase = string.Empty;
        public string SessionDir = string.Empty;
        public int StartAbsFrame;
        public int RecordTarget = 120;
        public double PhaseStartTime;
        public float ScaleX = 1.0f;
        public float ScaleY = 1.0f;
        public float AppliedX;
        public float AppliedY;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class C1Marker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    [Serializable]
    public sealed class C1RunMeta
    {
        public float CaseX = 640.0f;
        public float CaseY = 180.0f;
        public float AppliedX;
        public float AppliedY;
        public float ScaleX = 1.0f;
        public float ScaleY = 1.0f;
        public int ScreenW;
        public int ScreenH;
        public int RecordTarget = 120;
        public string SessionDir = string.Empty;
        public string Mapping = string.Empty;
    }
    public static class C1SliceDriver
    {
        private const string RunFolder = "Library/StillWrong/c1run";
        private const int RecordFrames = 120;
        private const float CaseX = 640.0f;
        private const float CaseY = 180.0f;
        private const float CaseW = 1920.0f;
        private const float CaseH = 1080.0f;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string RunDir()
        {
            return Path.Combine(ProjectRoot(), RunFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(RunDir(), "driver-state.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(RunDir());
            C1UiClipFixtureBuilder.BuildFailingScene();
            C1RunState state = new C1RunState();
            state.Phase = "enterplay";
            state.RecordTarget = RecordFrames;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            C1Marker marker = new C1Marker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = "C1-ui-clip";
            File.WriteAllText(Path.Combine(RunDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
            EditorApplication.isPlaying = true;
            return RunDir();
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
        private static C1RunState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<C1RunState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(C1RunState state)
        {
            try
            {
                Directory.CreateDirectory(RunDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(string reason)
        {
            try
            {
                C1Marker marker = new C1Marker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(RunDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            C1RunState state = LoadState();
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
                else if (state.Phase == "record")
                {
                    TickRecord(state);
                }
                else if (state.Phase == "freeze")
                {
                    TickFreeze(state);
                }
                else if (state.Phase == "exitplay")
                {
                    TickExitPlay(state);
                }
                else if (state.Phase == "replays etup")
                {
                    TickReplaySetup(state);
                }
                else if (state.Phase == "replay")
                {
                    TickReplay(state);
                }
            }
            catch
            {
            }
        }
        private static void TickEnterPlay(C1RunState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            UnityTemporalRingBuffer existing = UnityEngine.Object.FindAnyObjectByType<UnityTemporalRingBuffer>();
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }
            GameObject go = new GameObject("StillWrong_C1Recorder");
            UnityTemporalRingBuffer recorder = go.AddComponent<UnityTemporalRingBuffer>();
            recorder.Configure(300, null, null);
            recorder.StartRecording();
            state.Phase = "record";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.ScaleX = Screen.width / CaseW;
            state.ScaleY = Screen.height / CaseH;
            state.AppliedX = CaseX * state.ScaleX;
            state.AppliedY = CaseY * state.ScaleY;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRecord(C1RunState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail("record-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail("record-timeout");
                return;
            }
            int idx = Time.frameCount - state.StartAbsFrame;
            if (idx >= 0 && idx <= 4)
            {
                InputFeed.WarpPointer(state.AppliedX, state.AppliedY);
            }
            if (idx >= state.RecordTarget)
            {
                UnityTemporalRingBuffer recorder = UnityEngine.Object.FindAnyObjectByType<UnityTemporalRingBuffer>();
                if (recorder == null)
                {
                    Fail("record-recorder-missing");
                    return;
                }
                recorder.Freeze();
                state.Phase = "freeze";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
            }
        }
        private static void TickFreeze(C1RunState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail("freeze-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail("freeze-timeout");
                return;
            }
        UnityTemporalRingBuffer recorder = UnityEngine.Object.FindAnyObjectByType<UnityTemporalRingBuffer>();
            if (recorder == null)
            {
                Fail("freeze-recorder-missing");
                return;
            }
            if (!string.IsNullOrEmpty(recorder.LastExportPath))
            {
                state.SessionDir = recorder.LastExportPath;
                state.Phase = "exitplay";
                SaveState(state);
                EditorApplication.isPlaying = false;
            }
        }
        private static void TickExitPlay(C1RunState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (string.IsNullOrEmpty(state.SessionDir))
            {
                Fail("exitplay-no-session");
                return;
            }
            SceneAssetCheck();
            EditorSceneManager.OpenScene(C1UiClipFixtureBuilder.ScenePath, OpenSceneMode.Single);
            C1RunState next = state;
            next.Phase = "replays etup";
            next.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(next);
            EditorApplication.isPlaying = true;
        }
        private static void SceneAssetCheck()
        {
        }
        private static void TickReplaySetup(C1RunState state)
        {
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
                {
                    Fail("replaysetup-never-entered-play");
                }
                return;
            }
            GameObject go = new GameObject("StillWrong_C1Replay");
            SessionReplayRunner runner = go.AddComponent<SessionReplayRunner>();
            runner.SessionDir = state.SessionDir;
            runner.MeasureStep = 6;
            runner.ReplayResultPath = Path.Combine(RunDir(), "replay-result.json");
            runner.DriverResultPath = Path.Combine(RunDir(), "driver-result.json");
            state.Phase = "replay";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickReplay(C1RunState state)
        {
            string driverResult = Path.Combine(RunDir(), "driver-result.json");
            if (!File.Exists(driverResult))
            {
                if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
                {
                    Fail("replay-timeout");
                }
                return;
            }
            C1RunMeta meta = new C1RunMeta();
            meta.AppliedX = state.AppliedX;
            meta.AppliedY = state.AppliedY;
            meta.ScaleX = state.ScaleX;
            meta.ScaleY = state.ScaleY;
            meta.ScreenW = state.ScreenW;
            meta.ScreenH = state.ScreenH;
            meta.RecordTarget = state.RecordTarget;
            meta.SessionDir = state.SessionDir;
            meta.Mapping = "case-1920x1080-to-gameview-scale-direct-as-screen-coords";
            try
            {
                File.WriteAllText(Path.Combine(RunDir(), "run-meta.json"), JsonUtility.ToJson(meta, true));
            }
            catch
            {
            }
            C1Marker marker = new C1Marker();
            marker.Kind = "done";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = "C1-ui-clip";
            try
            {
                File.WriteAllText(Path.Combine(RunDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
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
    }
}

