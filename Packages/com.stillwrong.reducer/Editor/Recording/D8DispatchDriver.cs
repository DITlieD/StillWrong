using System;
using System.IO;
using StillWrong.Dispatch;
using StillWrong.Recording;
using StillWrong.Editor.Fixtures;
using StillWrong.Editor.UI;
using UnityEditor;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D8DispatchState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8DispatchResult
    {
        public string RunId = string.Empty;
        public string CaseId = "d8-dispatch";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public string PacketFile = string.Empty;
        public string Route = string.Empty;
        public string Branch = string.Empty;
        public bool BranchExists;
        public string FailureVerdict = string.Empty;
        public string FreshProcessVerdict = string.Empty;
        public int KeptCount;
        public int RemovedCount;
        public string BundlePath = string.Empty;
        public string FrameChunk = string.Empty;
        public string FrameFile = string.Empty;
        public double FrameClockSeconds;
        public string SheetProgress = string.Empty;
        public int SheetW;
        public int SheetH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8DispatchMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class D8DispatchDriver
    {
        private const string DispatchFolder = "Library/StillWrong/d8dispatch";
        private const string TargetPath = "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody";
        private const string Sentence = "gate dispatch proof";
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string DispatchDir()
        {
            return Path.Combine(ProjectRoot(), DispatchFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(DispatchDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(DispatchDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(DispatchDir());
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "d8dispatch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D8DispatchState state = new D8DispatchState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D8DispatchMarker marker = new D8DispatchMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(DispatchDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static D8DispatchState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D8DispatchState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D8DispatchState state)
        {
            try
            {
                Directory.CreateDirectory(DispatchDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D8DispatchState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                D8DispatchMarker marker = new D8DispatchMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(DispatchDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            D8DispatchState state = LoadState();
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
                else if (state.Phase == "exitplay")
                {
                    TickExitPlay(state);
                }
                else if (state.Phase == "dispatch")
                {
                    TickDispatch(state);
                }
            }
            catch
            {
            }
        }
        private static void TickEnterPlay(D8DispatchState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            RollingCaptureRecorder recorder = UnityEngine.Object.FindFirstObjectByType<RollingCaptureRecorder>();
            if (recorder == null)
            {
                GameObject host = new GameObject("StillWrong_D8DispatchRecorder");
                recorder = host.AddComponent<RollingCaptureRecorder>();
                recorder.Begin("d8dispatch", 60.0, 30.0);
                if (!recorder.IsCapturing())
                {
                    Fail(state, "record-would-not-capture");
                    return;
                }
            }
            state.Phase = "record";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRecord(D8DispatchState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "record-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "record-timeout");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime < 12.0)
            {
                return;
            }
            RollingCaptureRecorder recorder = UnityEngine.Object.FindFirstObjectByType<RollingCaptureRecorder>();
            if (recorder == null || !recorder.IsCapturing())
            {
                Fail(state, "record-lost-recorder");
                return;
            }
            recorder.FreezeCapture();
            state.SessionDir = recorder.GetSessionDir();
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
        private static void TickExitPlay(D8DispatchState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (string.IsNullOrEmpty(state.SessionDir) || !Directory.Exists(state.SessionDir))
            {
                Fail(state, "exitplay-no-session");
                return;
            }
            state.Phase = "dispatch";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickDispatch(D8DispatchState state)
        {
            if (EditorApplication.isPlaying)
            {
                Fail(state, "dispatch-entered-play");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 1500.0)
            {
                Fail(state, "dispatch-timeout");
                return;
            }
            StillWrongBugSheet.OpenWithSession(state.SessionDir, null, null, "SlotCardBody", TargetPath, "dispatch-gate", state.ScreenW, state.ScreenH);
            StillWrongBugSheet sheet = EditorWindow.GetWindow<StillWrongBugSheet>();
            if (sheet == null)
            {
                Fail(state, "dispatch-no-sheet");
                return;
            }
            string progress = sheet.DispatchSelectedBug(Sentence);
            D8DispatchResult result = new D8DispatchResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.SheetProgress = progress;
            result.SheetW = (int)sheet.position.width;
            result.SheetH = (int)sheet.position.height;
            if (progress.StartsWith("failed"))
            {
                WriteResult(state, result, progress);
                Fail(state, "dispatch-" + progress);
                return;
            }
            string packetFile;
            string route;
            string branch;
            ReadDispatchProgress(out packetFile, out route, out branch);
            result.PacketFile = packetFile;
            result.Route = route;
            result.Branch = branch;
            if (!string.IsNullOrEmpty(branch))
            {
                result.BranchExists = DispatchService.BranchExists(branch);
            }
            if (!string.IsNullOrEmpty(packetFile) && File.Exists(packetFile))
            {
                try
                {
                    DispatchPacket packet = JsonUtility.FromJson<DispatchPacket>(File.ReadAllText(packetFile));
                    if (packet != null)
                    {
                        result.FailureVerdict = packet.FailureVerdict;
                        result.FreshProcessVerdict = packet.FreshProcessVerdict;
                        result.KeptCount = packet.KeptNodeIds != null ? packet.KeptNodeIds.Count : 0;
                        result.RemovedCount = packet.RemovedNodeIds != null ? packet.RemovedNodeIds.Count : 0;
                        result.BundlePath = packet.BundlePath;
                        result.FrameChunk = packet.FrameChunk;
                        result.FrameFile = packet.FrameFile;
                        result.FrameClockSeconds = packet.FrameClockSeconds;
                    }
                }
                catch
                {
                }
            }
            if (string.IsNullOrEmpty(result.PacketFile) || result.KeptCount == 0 || result.FreshProcessVerdict != "StillWrong" || !result.BranchExists)
            {
                WriteResult(state, result, "failed: dispatch-incomplete");
                Fail(state, "dispatch-incomplete");
                return;
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
                D8DispatchMarker marker = new D8DispatchMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(DispatchDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static void ReadDispatchProgress(out string packetFile, out string route, out string branch)
        {
            packetFile = string.Empty;
            route = string.Empty;
            branch = string.Empty;
            try
            {
                string dir = DispatchService.PacketRoot();
                if (!Directory.Exists(dir))
                {
                    return;
                }
                string[] files = Directory.GetFiles(dir, "progress-*.txt");
                if (files == null || files.Length == 0)
                {
                    return;
                }
                Array.Sort(files, StringComparer.Ordinal);
                string latest = files[files.Length - 1];
                string[] lines = File.ReadAllLines(latest);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    int at = lines[i].IndexOf("dispatched route=");
                    if (at < 0)
                    {
                        continue;
                    }
                    string tail = lines[i].Substring(at + "dispatched route=".Length);
                    int b = tail.IndexOf(" branch=");
                    int f = tail.IndexOf(" file=");
                    if (b > 0)
                    {
                        route = tail.Substring(0, b);
                    }
                    if (b >= 0 && f > b)
                    {
                        branch = tail.Substring(b + " branch=".Length, f - b - " branch=".Length);
                    }
                    if (f >= 0)
                    {
                        packetFile = tail.Substring(f + " file=".Length);
                    }
                    return;
                }
            }
            catch
            {
            }
        }
        private static void WriteResult(D8DispatchState state, D8DispatchResult result, string failure)
        {
            try
            {
                D8DispatchResult dto = result != null ? result : new D8DispatchResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(DispatchDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
