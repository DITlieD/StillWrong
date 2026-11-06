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
    public sealed class C3CaptureState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public string ScenePath = string.Empty;
        public string CaseId = string.Empty;
        public int StartAbsFrame;
        public int RecordTarget = 120;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class C3CaptureMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    [Serializable]
    public sealed class C3CaptureMeta
    {
        public string RunId = string.Empty;
        public string CaseId = "C3-transient-disappear";
        public string SessionDir = string.Empty;
        public int RecordedFrames;
        public int RequestedFrames;
        public long ReadbackDrops;
        public int AttemptedFrames;
        public long UnrecordedAttempts;
        public int ScreenW;
        public int ScreenH;
        public int RecordTarget = 120;
    }
    public static class C3CaptureDriver
    {
        private const string RunFolder = "Library/StillWrong/c3run";
        private const int RecordFrames = 120;
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
            C3TransientDisappearFixtureBuilder.BuildFailingScene();
            return StartRun("C3");
        }
        public static string StartRun(string fixtureId)
        {
            string norm = (fixtureId ?? string.Empty).Trim().ToUpperInvariant();
            string scenePath;
            string caseId;
            string runPrefix;
            if (norm == "C3B")
            {
                C3bSoleOccupantFixtureBuilder.BuildFailingScene();
                scenePath = C3bSoleOccupantFixtureBuilder.ScenePath;
                caseId = "C3b-sole-occupant";
                runPrefix = "c3b-capture-";
            }
            else if (norm == "C3" || norm == string.Empty)
            {
                C3TransientDisappearFixtureBuilder.BuildFailingScene();
                scenePath = C3TransientDisappearFixtureBuilder.ScenePath;
                caseId = "C3-transient-disappear";
                runPrefix = "c3-capture-";
            }
            else
            {
                throw new ArgumentOutOfRangeException("fixtureId", "unknown fixture " + fixtureId);
            }
            Directory.CreateDirectory(RunDir());
            string runId = runPrefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            C3CaptureState state = new C3CaptureState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.ScenePath = scenePath;
            state.CaseId = caseId;
            state.RecordTarget = RecordFrames;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            C3CaptureMarker marker = new C3CaptureMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(RunDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static C3CaptureState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<C3CaptureState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(C3CaptureState state)
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
        private static void Fail(C3CaptureState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            try
            {
                C3CaptureMarker marker = new C3CaptureMarker();
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
            C3CaptureState state = LoadState();
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
            }
            catch
            {
            }
        }
        private static void TickEnterPlay(C3CaptureState state)
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
            GameObject go = new GameObject("StillWrong_C3Recorder");
            UnityTemporalRingBuffer recorder = go.AddComponent<UnityTemporalRingBuffer>();
            recorder.Configure(300, null, null);
            recorder.StartRecording();
            state.Phase = "record";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRecord(C3CaptureState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "record-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "record-timeout");
                return;
            }
            int idx = Time.frameCount - state.StartAbsFrame;
            if (idx >= state.RecordTarget)
            {
                UnityTemporalRingBuffer recorder = UnityEngine.Object.FindAnyObjectByType<UnityTemporalRingBuffer>();
                if (recorder == null)
                {
                    Fail(state, "record-recorder-missing");
                    return;
                }
                recorder.Freeze();
                state.Phase = "freeze";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
            }
        }
        private static void TickFreeze(C3CaptureState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "freeze-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "freeze-timeout");
                return;
            }
            UnityTemporalRingBuffer recorder = UnityEngine.Object.FindAnyObjectByType<UnityTemporalRingBuffer>();
            if (recorder == null)
            {
                Fail(state, "freeze-recorder-missing");
                return;
            }
            if (string.IsNullOrEmpty(recorder.LastExportPath))
            {
                return;
            }
            state.SessionDir = recorder.LastExportPath;
            state.Phase = "exitplay";
            SaveState(state);
            EditorApplication.isPlaying = false;
        }
        private static void TickExitPlay(C3CaptureState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (string.IsNullOrEmpty(state.SessionDir))
            {
                Fail(state, "exitplay-no-session");
                return;
            }
            EditorSceneManager.OpenScene(state.ScenePath, OpenSceneMode.Single);
            int recordedFrames = 0;
            long readbackDrops = 0;
            int attemptedFrames = 0;
            try
            {
                SessionBundle bundle = SessionBundle.Load(state.SessionDir);
                if (bundle != null && bundle.Frames != null && bundle.Frames.Frames != null)
                {
                    recordedFrames = bundle.Frames.Frames.Count;
                    int minFrame = int.MaxValue;
                    int maxFrame = int.MinValue;
                    for (int i = 0; i < bundle.Frames.Frames.Count; i++)
                    {
                        int frame = bundle.Frames.Frames[i].Frame;
                        if (frame < minFrame)
                        {
                            minFrame = frame;
                        }
                        if (frame > maxFrame)
                        {
                            maxFrame = frame;
                        }
                    }
                    if (maxFrame >= minFrame)
                    {
                        attemptedFrames = maxFrame - minFrame + 1;
                    }
                }
                if (bundle != null && bundle.Manifest != null)
                {
                    readbackDrops = bundle.Manifest.ReadbackDrops;
                }
            }
            catch
            {
                recordedFrames = 0;
                readbackDrops = 0;
                attemptedFrames = 0;
            }
            long unrecordedAttempts = (long)attemptedFrames - (long)recordedFrames - readbackDrops;
            if (unrecordedAttempts < 0)
            {
                unrecordedAttempts = 0;
            }
            if ((long)recordedFrames + readbackDrops + unrecordedAttempts < state.RecordTarget)
            {
                Fail(state, "exitplay-short-session");
                return;
            }
            C3CaptureMeta meta = new C3CaptureMeta();
            meta.RunId = state.RunId;
            meta.CaseId = state.CaseId;
            meta.SessionDir = state.SessionDir;
            meta.RecordedFrames = recordedFrames;
            meta.RequestedFrames = state.RecordTarget;
            meta.ReadbackDrops = readbackDrops;
            meta.AttemptedFrames = attemptedFrames;
            meta.UnrecordedAttempts = unrecordedAttempts;
            meta.ScreenW = state.ScreenW;
            meta.ScreenH = state.ScreenH;
            meta.RecordTarget = state.RecordTarget;
            try
            {
                File.WriteAllText(Path.Combine(RunDir(), "run-meta.json"), JsonUtility.ToJson(meta, true));
            }
            catch
            {
            }
            C3CaptureMarker marker = new C3CaptureMarker();
            marker.Kind = "done";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = state.RunId;
            try
            {
                File.WriteAllText(Path.Combine(RunDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
    }
}

