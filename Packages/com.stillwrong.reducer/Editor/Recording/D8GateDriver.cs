using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Dispatch;
using StillWrong.Recording;
using StillWrong.Core.Oracle;
using StillWrong.Editor.Fixtures;
using StillWrong.Editor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D8GateState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string PacketFile = string.Empty;
        public string Branch = string.Empty;
        public string Route = string.Empty;
        public string LiveVerdict = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8GateResult
    {
        public string RunId = string.Empty;
        public string CaseId = "d8-gate";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public string LiveVerdict = string.Empty;
        public string SheetProgress = string.Empty;
        public string PacketFile = string.Empty;
        public string Branch = string.Empty;
        public string Route = string.Empty;
        public bool SheetOpened;
        public int SheetW;
        public int SheetH;
        public int PopupW;
        public int PopupH;
        public int RetainedChunks;
        public double RetainedSeconds;
        public int ExpiredDeleted;
        public bool SentinelIntact;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8GateMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class D8GateDriver
    {
        private const string GateFolder = "Library/StillWrong/d8gate";
        private const int MeasureFrame = 6;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string GateDir()
        {
            return Path.Combine(ProjectRoot(), GateFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(GateDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(GateDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(GateDir());
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "d8gate-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D8GateState state = new D8GateState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D8GateMarker marker = new D8GateMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(GateDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static D8GateState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D8GateState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D8GateState state)
        {
            try
            {
                Directory.CreateDirectory(GateDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D8GateState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                D8GateMarker marker = new D8GateMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(GateDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            D8GateState state = LoadState();
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
                else if (state.Phase == "retentionrun")
                {
                    TickRetentionRun(state);
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
        private static void TickEnterPlay(D8GateState state)
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
        private static void TickMeasure(D8GateState state)
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
            string sceneFound = string.Empty;
            string sceneFailure = DispatchService.FindSceneForPath(DemoSceneFixtureBuilder.TargetHierarchyPath, out sceneFound);
            if (!string.IsNullOrEmpty(sceneFailure))
            {
                Fail(state, "measure-" + sceneFailure);
                return;
            }
            OracleSpec spec = FixtureSpecs.C1();
            OracleResult live = DispatchService.CheckFailureLive(spec);
            state.LiveVerdict = live != null ? live.Verdict.ToString() : "Inconclusive";
            if (state.LiveVerdict != "StillWrong")
            {
                Fail(state, "measure-live-not-stillwrong");
                return;
            }
            DispatchPacket packet = new DispatchPacket();
            packet.Mode = "Bug";
            packet.SessionDir = RollingRecorderControl.LastSessionDir();
            packet.ScenePath = sceneFound;
            packet.Target.Path = DemoSceneFixtureBuilder.TargetHierarchyPath;
            packet.Target.Name = "SlotCardBody";
            packet.CandidatePaths.Add(packet.Target.Path);
            packet.Sentence = "gate bug proof";
            packet.FailureVerdict = state.LiveVerdict;
            if (live.Reasons != null)
            {
                packet.FailureReasons = new List<string>(live.Reasons);
            }
            DispatchReceipt receipt = DispatchService.SendPacket(packet, StillWrongSettings.PreferredAgent(), StillWrongSettings.BranchPrefix());
            if (!receipt.Sent)
            {
                Fail(state, "measure-" + receipt.Reason);
                return;
            }
            state.PacketFile = receipt.PacketFile;
            state.Branch = receipt.Branch;
            state.Route = receipt.Route;
            state.Phase = "retentionrun";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRetentionRun(D8GateState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "retention-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "retention-timeout");
                return;
            }
            RollingCaptureRecorder recorder = UnityEngine.Object.FindFirstObjectByType<RollingCaptureRecorder>();
            if (recorder == null)
            {
                GameObject host = new GameObject("StillWrong_D8GateRecorder");
                recorder = host.AddComponent<RollingCaptureRecorder>();
                recorder.Begin("d8gate-retention", 60.0, 30.0);
                if (!recorder.IsCapturing())
                {
                    Fail(state, "retention-would-not-capture");
                    return;
                }
                SaveState(state);
                return;
            }
            RollingStatusFile status = ReadStatus();
            if (status == null || status.ChunksDeleted < 1)
            {
                return;
            }
            state.Phase = "exitplay";
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static RollingStatusFile ReadStatus()
        {
            try
            {
                string path = Path.Combine(RollingCaptureRecorder.RecorderDir(), "status.json");
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<RollingStatusFile>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void TickExitPlay(D8GateState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            D8GateResult result = new D8GateResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.LiveVerdict = state.LiveVerdict;
            result.PacketFile = state.PacketFile;
            result.Branch = state.Branch;
            result.Route = state.Route;
            StillWrongBugSheet sheet = EditorWindow.GetWindow<StillWrongBugSheet>();
            if (sheet != null)
            {
                result.SheetOpened = true;
                result.SheetW = (int)sheet.position.width;
                result.SheetH = (int)sheet.position.height;
                result.SheetProgress = sheet.SheetProgress();
            }
            StillWrongChangePopup popup = EditorWindow.GetWindow<StillWrongChangePopup>();
            if (popup != null)
            {
                result.PopupW = (int)popup.position.width;
                result.PopupH = (int)popup.position.height;
            }
            RollingStatusFile status = ReadStatus();
            if (status != null)
            {
                result.RetainedChunks = status.ChunkCount;
                result.RetainedSeconds = status.RetainedSeconds;
                result.ExpiredDeleted = status.ChunksDeleted;
            }
            try
            {
                string sentinel = Path.Combine(RollingCaptureRecorder.RecorderDir(), "sentinel.txt");
                result.SentinelIntact = File.Exists(sentinel);
            }
            catch
            {
            }
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
                D8GateMarker marker = new D8GateMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(GateDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static void WriteResult(D8GateState state, D8GateResult result, string failure)
        {
            try
            {
                D8GateResult dto = result != null ? result : new D8GateResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(GateDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
