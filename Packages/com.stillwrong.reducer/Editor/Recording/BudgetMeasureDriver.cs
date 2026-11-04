using System;
using System.IO;
using StillWrong.Recording;
using StillWrong.Editor.Fixtures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class BudgetState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public long MemBeforeOn;
        public long MemAfterOn;
        public long MemBeforeOff;
        public long MemAfterOff;
        public double OnMsTotal;
        public int OnTicks;
        public double OffMsTotal;
        public int OffTicks;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class BudgetResult
    {
        public string RunId = string.Empty;
        public string CaseId = "budget-measure";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public double WindowSeconds = 15.0;
        public double OnMeanFrameMs = -1.0;
        public string OnMeanFrameMethod = string.Empty;
        public double OffMeanFrameMs = -1.0;
        public string OffMeanFrameMethod = string.Empty;
        public double CaptureEncodeMsPerFrame = -1.0;
        public string CaptureEncodeReason = string.Empty;
        public long ManagedMemoryDeltaBytes;
        public string ManagedMemoryNote = string.Empty;
        public double DiskBytesPerSecond = -1.0;
        public string DiskRateNote = string.Empty;
        public int ChunkCount;
        public double RetainedSeconds;
        public long DroppedFrames;
        public int ExpiredChunksDeleted;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class BudgetMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class BudgetMeasureDriver
    {
        private const string BudgetFolder = "Library/StillWrong/budget";
        private const double WindowSeconds = 15.0;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string BudgetDir()
        {
            return Path.Combine(ProjectRoot(), BudgetFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(BudgetDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(BudgetDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(BudgetDir());
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "budget-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            BudgetState state = new BudgetState();
            state.Phase = "enterplay-on";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            BudgetMarker marker = new BudgetMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(BudgetDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static BudgetState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<BudgetState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(BudgetState state)
        {
            try
            {
                Directory.CreateDirectory(BudgetDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(BudgetState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                BudgetMarker marker = new BudgetMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(BudgetDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            BudgetState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "enterplay-on")
                {
                    TickEnterPlayOn(state);
                }
                else if (state.Phase == "window-on")
                {
                    TickWindowOn(state);
                }
                else if (state.Phase == "exitplay-on")
                {
                    TickExitPlay(state, "enterplay-off");
                }
                else if (state.Phase == "enterplay-off")
                {
                    TickEnterPlayOff(state);
                }
                else if (state.Phase == "window-off")
                {
                    TickWindowOff(state);
                }
                else if (state.Phase == "exitplay-off")
                {
                    TickExitPlay(state, "finish");
                }
            }
            catch
            {
            }
        }
        private static long ManagedHeapBytes()
        {
            try
            {
                return System.GC.GetTotalMemory(false);
            }
            catch
            {
                return -1;
            }
        }
        private static void TickEnterPlayOn(BudgetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            GameObject host = new GameObject("StillWrong_BudgetRecorder");
            RollingCaptureRecorder recorder = host.AddComponent<RollingCaptureRecorder>();
            recorder.Begin("budget-on");
            if (!recorder.IsCapturing())
            {
                Fail(state, "recorder-would-not-capture");
                return;
            }
            state.MemBeforeOn = ManagedHeapBytes();
            state.Phase = "window-on";
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickWindowOn(BudgetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "window-on-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "window-on-timeout");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime < WindowSeconds)
            {
                state.OnMsTotal = state.OnMsTotal + Time.unscaledDeltaTime * 1000.0;
                state.OnTicks = state.OnTicks + 1;
                SaveState(state);
                return;
            }
            state.MemAfterOn = ManagedHeapBytes();
            state.Phase = "exitplay-on";
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static void TickExitPlay(BudgetState state, string next)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (next == "finish")
            {
                Finish(state);
                return;
            }
            state.Phase = next;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickEnterPlayOff(BudgetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                DemoSceneFixtureBuilder.BuildScene();
                state.MemBeforeOff = ManagedHeapBytes();
                state.OffMsTotal = 0.0;
                state.OffTicks = 0;
                state.Phase = "window-off";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
                EditorApplication.isPlaying = true;
                return;
            }
            state.Phase = "window-off";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickWindowOff(BudgetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "window-off-play-exited-early");
                return;
            }
            state.OffMsTotal = state.OffMsTotal + Time.unscaledDeltaTime * 1000.0;
            state.OffTicks = state.OffTicks + 1;
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "window-off-timeout");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime < WindowSeconds)
            {
                SaveState(state);
                return;
            }
            state.MemAfterOff = ManagedHeapBytes();
            state.Phase = "exitplay-off";
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static RollingStatusFile ReadRecorderStatus()
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
        private static void Finish(BudgetState state)
        {
            BudgetResult result = new BudgetResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.WindowSeconds = WindowSeconds;
            RollingStatusFile status = ReadRecorderStatus();
            if (state.OnTicks > 0)
            {
                result.OnMeanFrameMs = state.OnMsTotal / state.OnTicks;
                result.OnMeanFrameMethod = "driver-tick-sampled-unscaled-delta";
            }
            else
            {
                result.OnMeanFrameMethod = "not-recorded: no on-window ticks sampled";
            }
            if (status != null && status.CapturedFrames > 0)
            {
                result.CaptureEncodeMsPerFrame = (status.CaptureMsTotal + status.EncodeMsTotal) / status.CapturedFrames;
                result.CaptureEncodeReason = "stopwatch-per-timeline-frame";
            }
            else
            {
                result.CaptureEncodeReason = "not-recorded: no timeline frames captured";
            }
            if (state.MemBeforeOn >= 0 && state.MemAfterOn >= 0)
            {
                result.ManagedMemoryDeltaBytes = state.MemAfterOn - state.MemBeforeOn;
                result.ManagedMemoryNote = "managed-heap-sample; GC noise not separated";
            }
            else
            {
                result.ManagedMemoryNote = "not-recorded: heap sample unavailable";
            }
            if (status != null && status.TotalBytes >= 0 && WindowSeconds > 0.0)
            {
                result.DiskBytesPerSecond = status.TotalBytes / WindowSeconds;
                result.DiskRateNote = "present-bytes rate; deleted chunks excluded";
            }
            else
            {
                result.DiskRateNote = "not-recorded: session size unreadable";
            }
            if (status != null)
            {
                result.ChunkCount = status.ChunkCount;
                result.RetainedSeconds = status.RetainedSeconds;
                result.DroppedFrames = status.DroppedFrames;
                result.ExpiredChunksDeleted = status.ChunksDeleted;
            }
            if (state.OffTicks > 0)
            {
                result.OffMeanFrameMs = state.OffMsTotal / state.OffTicks;
                result.OffMeanFrameMethod = "driver-tick-sampled-unscaled-delta";
            }
            else
            {
                result.OffMeanFrameMethod = "not-recorded: no off-window ticks sampled";
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
                BudgetMarker marker = new BudgetMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(BudgetDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static void WriteResult(BudgetState state, BudgetResult result, string failure)
        {
            try
            {
                BudgetResult dto = result != null ? result : new BudgetResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(BudgetDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
