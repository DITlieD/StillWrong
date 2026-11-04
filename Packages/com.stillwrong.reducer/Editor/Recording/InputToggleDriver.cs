using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Fixtures;
using StillWrong.Oracle;
using StillWrong.Recording;
using StillWrong.Replay;
using StillWrong.Editor.Fixtures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class InputToggleState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public float ButtonCenterX;
        public float ButtonCenterY;
        public int ClickFrame;
        public int ReplayStep;
        public int BeforeCount;
        public bool BeforeActive;
        public int EdgesDispatched;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class InputToggleBefore
    {
        public int Count;
        public bool TargetActive;
    }
    [Serializable]
    public sealed class InputToggleAfter
    {
        public int Count;
        public bool TargetActive;
        public int FramesReplayed;
        public int EdgesDispatched;
    }
    [Serializable]
    public sealed class InputToggleInjected
    {
        public int Frame;
        public float PointerX;
        public float PointerY;
        public string EventType = string.Empty;
    }
    [Serializable]
    public sealed class InputToggleResult
    {
        public string RunId = string.Empty;
        public string CaseId = "input-toggle-proof";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public string SessionDir = string.Empty;
        public string ButtonPath = string.Empty;
        public string TargetPath = string.Empty;
        public int ClickFrame;
        public float ButtonCenterX;
        public float ButtonCenterY;
        public InputToggleBefore Before = new InputToggleBefore();
        public InputToggleAfter After = new InputToggleAfter();
        public InputToggleInjected Injected = new InputToggleInjected();
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class InputToggleMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class InputToggleDriver
    {
        private const string RunFolder = "Library/StillWrong/inputtoggle";
        private const int RecordFrames = 40;
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
        public static string ResultPath()
        {
            return Path.Combine(RunDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(RunDir());
            InputToggleFixtureBuilder.BuildScene();
            string runId = "inputtoggle-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            InputToggleState state = new InputToggleState();
            state.Phase = "enterplay-record";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            InputToggleMarker marker = new InputToggleMarker();
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
        private static InputToggleState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<InputToggleState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(InputToggleState state)
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
        private static void Fail(InputToggleState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                InputToggleMarker marker = new InputToggleMarker();
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
            InputToggleState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "enterplay-record")
                {
                    TickEnterRecord(state);
                }
                else if (state.Phase == "record")
                {
                    TickRecord(state);
                }
                else if (state.Phase == "freeze")
                {
                    TickFreeze(state);
                }
                else if (state.Phase == "exitplay-record")
                {
                    TickExitRecord(state);
                }
                else if (state.Phase == "replay-build")
                {
                    TickReplayBuild(state);
                }
                else if (state.Phase == "enterplay-replay")
                {
                    TickEnterReplay(state);
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
        private static void TickEnterRecord(InputToggleState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            UnityTemporalRingBuffer existing = UnityEngine.Object.FindFirstObjectByType<UnityTemporalRingBuffer>();
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }
            GameObject go = new GameObject("StillWrong_InputToggleRecorder");
            UnityTemporalRingBuffer recorder = go.AddComponent<UnityTemporalRingBuffer>();
            recorder.Configure(300, null, null);
            recorder.StartRecording();
            GameObject button = UnitySymptomOracleRunner.FindGameObjectByPath(InputToggleFixtureBuilder.ButtonPath);
            if (button == null)
            {
                Fail(state, "record-button-missing");
                return;
            }
            Rect2D rect = UnitySymptomOracleRunner.ComputeScreenRect(button, Camera.main);
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                Fail(state, "record-button-rect-empty");
                return;
            }
            state.ButtonCenterX = rect.X + rect.Width * 0.5f;
            state.ButtonCenterY = rect.Y + rect.Height * 0.5f;
            state.Phase = "record";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRecord(InputToggleState state)
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
            if (Time.frameCount - state.StartAbsFrame < RecordFrames)
            {
                return;
            }
            UnityTemporalRingBuffer recorder = UnityEngine.Object.FindFirstObjectByType<UnityTemporalRingBuffer>();
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
        private static void TickFreeze(InputToggleState state)
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
            UnityTemporalRingBuffer recorder = UnityEngine.Object.FindFirstObjectByType<UnityTemporalRingBuffer>();
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
            state.Phase = "exitplay-record";
            SaveState(state);
            EditorApplication.isPlaying = false;
        }
        private static void TickExitRecord(InputToggleState state)
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
            SessionBundle bundle = null;
            try
            {
                bundle = SessionBundle.Load(state.SessionDir);
            }
            catch
            {
                bundle = null;
            }
            if (bundle == null || bundle.Frames == null || bundle.Frames.Frames == null || bundle.Frames.Frames.Count == 0)
            {
                Fail(state, "exitplay-no-frames");
                return;
            }
            int mid = bundle.Frames.Frames.Count / 2;
            state.ClickFrame = bundle.Frames.Frames[mid].Frame;
            InputEventRecord click = new InputEventRecord();
            click.Frame = state.ClickFrame;
            click.Path = "legacy";
            click.PointerX = state.ButtonCenterX;
            click.PointerY = state.ButtonCenterY;
            click.ButtonDownMask = 1;
            click.ButtonUpMask = 1;
            click.EventType = "injected-proof-click";
            try
            {
                InputEventFile inputs = JsonUtility.FromJson<InputEventFile>(File.ReadAllText(Path.Combine(state.SessionDir, "inputs.json")));
                if (inputs == null || inputs.Events == null)
                {
                    Fail(state, "exitplay-inputs-unreadable");
                    return;
                }
                inputs.Events.Add(click);
                File.WriteAllText(Path.Combine(state.SessionDir, "inputs.json"), JsonUtility.ToJson(inputs, true));
                SessionManifest manifest = JsonUtility.FromJson<SessionManifest>(File.ReadAllText(Path.Combine(state.SessionDir, "session.json")));
                if (manifest != null)
                {
                    manifest.InputEventCount = inputs.Events.Count;
                    File.WriteAllText(Path.Combine(state.SessionDir, "session.json"), JsonUtility.ToJson(manifest, true));
                }
            }
            catch
            {
                Fail(state, "exitplay-inject-failed");
                return;
            }
            state.Phase = "replay-build";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickReplayBuild(InputToggleState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            InputToggleFixtureBuilder.BuildScene();
            state.Phase = "enterplay-replay";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
            EditorApplication.isPlaying = true;
        }
        private static void TickEnterReplay(InputToggleState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            ReadBefore(state);
            SessionBundle bundle = null;
            try
            {
                bundle = SessionBundle.Load(state.SessionDir);
            }
            catch
            {
                bundle = null;
            }
            if (bundle == null || bundle.Manifest == null)
            {
                Fail(state, "replay-bundle-unreadable");
                return;
            }
            try
            {
                UnityEngine.Random.State st = JsonUtility.FromJson<UnityEngine.Random.State>(bundle.Manifest.StartRandomState);
                UnityEngine.Random.state = st;
            }
            catch
            {
            }
            state.Phase = "replay";
            state.ReplayStep = 0;
            state.EdgesDispatched = 0;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void ReadBefore(InputToggleState state)
        {
            GameObject button = UnitySymptomOracleRunner.FindGameObjectByPath(InputToggleFixtureBuilder.ButtonPath);
            GameObject target = UnitySymptomOracleRunner.FindGameObjectByPath(InputToggleFixtureBuilder.TargetPath);
            if (button != null)
            {
                ClickToggle toggle = button.GetComponent<ClickToggle>();
                if (toggle != null)
                {
                    state.BeforeCount = toggle.Count;
                }
            }
            if (target != null)
            {
                state.BeforeActive = target.activeSelf;
            }
        }
        private static void TickReplay(InputToggleState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "replay-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "replay-timeout");
                return;
            }
            SessionBundle bundle = null;
            try
            {
                bundle = SessionBundle.Load(state.SessionDir);
            }
            catch
            {
                bundle = null;
            }
            if (bundle == null || bundle.Frames == null || bundle.Frames.Frames == null)
            {
                Fail(state, "replay-bundle-unreadable");
                return;
            }
            List<FrameIndexEntry> kept = bundle.Frames.Frames;
            Dictionary<int, InputEventRecord> legacyByFrame = new Dictionary<int, InputEventRecord>();
            for (int i = 0; i < bundle.Inputs.Events.Count; i++)
            {
                InputEventRecord rec = bundle.Inputs.Events[i];
                if (rec != null && rec.Path == "legacy" && (!legacyByFrame.ContainsKey(rec.Frame) || rec.ButtonDownMask != 0 || rec.ButtonUpMask != 0))
                {
                    legacyByFrame[rec.Frame] = rec;
                }
            }
            if (state.ReplayStep < kept.Count)
            {
                FrameIndexEntry entry = kept[state.ReplayStep];
                InputEventRecord rec = null;
                legacyByFrame.TryGetValue(entry.Frame, out rec);
                if (rec != null)
                {
                    InputFeed.WarpPointer(rec.PointerX, rec.PointerY);
                    if (rec.ButtonDownMask != 0 || rec.ButtonUpMask != 0)
                    {
                        DispatchEdges(rec);
                        state.EdgesDispatched = state.EdgesDispatched + 1;
                    }
                }
                state.ReplayStep = state.ReplayStep + 1;
                SaveState(state);
                return;
            }
            InputToggleResult result = new InputToggleResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.SessionDir = state.SessionDir;
            result.ButtonPath = InputToggleFixtureBuilder.ButtonPath;
            result.TargetPath = InputToggleFixtureBuilder.TargetPath;
            result.ClickFrame = state.ClickFrame;
            result.ButtonCenterX = state.ButtonCenterX;
            result.ButtonCenterY = state.ButtonCenterY;
            result.Before.Count = state.BeforeCount;
            result.Before.TargetActive = state.BeforeActive;
            GameObject afterButton = UnitySymptomOracleRunner.FindGameObjectByPath(InputToggleFixtureBuilder.ButtonPath);
            GameObject afterTarget = UnitySymptomOracleRunner.FindGameObjectByPath(InputToggleFixtureBuilder.TargetPath);
            int afterCount = state.BeforeCount;
            bool afterActive = state.BeforeActive;
            if (afterButton != null)
            {
                ClickToggle toggle = afterButton.GetComponent<ClickToggle>();
                if (toggle != null)
                {
                    afterCount = toggle.Count;
                }
            }
            if (afterTarget != null)
            {
                afterActive = afterTarget.activeSelf;
            }
            result.After.Count = afterCount;
            result.After.TargetActive = afterActive;
            result.After.FramesReplayed = kept.Count;
            result.After.EdgesDispatched = state.EdgesDispatched;
            result.Injected.Frame = state.ClickFrame;
            result.Injected.PointerX = state.ButtonCenterX;
            result.Injected.PointerY = state.ButtonCenterY;
            result.Injected.EventType = "injected-proof-click";
            WriteResult(state, result, string.Empty);
            try
            {
                InputToggleFixtureBuilder.BuildScene();
            }
            catch
            {
            }
            try
            {
                InputToggleMarker marker = new InputToggleMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(RunDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static void DispatchEdges(InputEventRecord rec)
        {
            try
            {
                if (EventSystem.current == null)
                {
                    return;
                }
                PointerEventData ped = new PointerEventData(EventSystem.current);
                ped.position = new Vector2(rec.PointerX, rec.PointerY);
                List<RaycastResult> results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, results);
                if (results.Count == 0)
                {
                    return;
                }
                GameObject target = results[0].gameObject;
                if (rec.ButtonDownMask != 0)
                {
                    ped.pointerPressRaycast = results[0];
                    ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerDownHandler);
                }
                if (rec.ButtonUpMask != 0)
                {
                    ped.pointerPressRaycast = results[0];
                    ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerClickHandler);
                }
            }
            catch
            {
            }
        }
        private static void WriteResult(InputToggleState state, InputToggleResult result, string failure)
        {
            try
            {
                InputToggleResult dto = result != null ? result : new InputToggleResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(RunDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
