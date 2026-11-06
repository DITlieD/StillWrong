using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using StillWrong.Recording;
using StillWrong.Editor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    public static class RollingRecorderControl
    {
        public const string EnableKey = "StillWrong.BackgroundRecordingEnabled";
        public static bool IsEnabled()
        {
            try
            {
                return EditorPrefs.GetBool(EnableKey, false);
            }
            catch
            {
                return false;
            }
        }
        public static void SetEnabled(bool enabled)
        {
            try
            {
                EditorPrefs.SetBool(EnableKey, enabled);
            }
            catch
            {
            }
        }
        public static string StatusFilePath()
        {
            return Path.Combine(RollingCaptureRecorder.RecorderDir(), "status.json");
        }
        public static bool TryReadStatus(out string state, out double retainedSeconds, out int chunkCount, out long totalBytes, out long droppedFrames)
        {
            state = string.Empty;
            retainedSeconds = 0.0;
            chunkCount = 0;
            totalBytes = 0;
            droppedFrames = 0;
            try
            {
                string path = StatusFilePath();
                if (!File.Exists(path))
                {
                    return false;
                }
                RollingStatusFile status = JsonUtility.FromJson<RollingStatusFile>(File.ReadAllText(path));
                if (status == null || string.IsNullOrEmpty(status.State))
                {
                    return false;
                }
                state = status.State;
                retainedSeconds = status.RetainedSeconds;
                chunkCount = status.ChunkCount;
                totalBytes = status.TotalBytes;
                droppedFrames = status.DroppedFrames;
                return true;
            }
            catch
            {
                return false;
            }
        }
        [MenuItem("Tools/Still Wrong/Background recording")]
        private static void ToggleBackgroundRecording()
        {
            SetEnabled(!IsEnabled());
        }
        [MenuItem("Tools/Still Wrong/Background recording", true)]
        private static bool ToggleBackgroundRecordingValidate()
        {
            Menu.SetChecked("Tools/Still Wrong/Background recording", IsEnabled());
            return true;
        }
        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            try
            {
                if (change == PlayModeStateChange.ExitingPlayMode)
                {
                    SetEnabled(false);
                    return;
                }
                if (change != PlayModeStateChange.EnteredPlayMode)
                {
                    return;
                }
                if (!IsEnabled())
                {
                    return;
                }
                StartRollingRecorder();
                StillWrongBugSheet.EnsureChip();
            }
            catch
            {
            }
        }
        [Shortcut("StillWrong/Freeze Capture", KeyCode.F9)]
        private static void FreezeShortcut()
        {
            FreezeAndOpenSheet();
        }
        public static void FreezeAndOpenSheet()
        {
            try
            {
                RollingCaptureRecorder recorder = UnityEngine.Object.FindAnyObjectByType<RollingCaptureRecorder>();
                if (recorder == null)
                {
                    StillWrongBugSheet.OpenWithSession(LastSessionDir(), null, null, string.Empty, string.Empty, "freeze", Screen.width, Screen.height);
                    return;
                }
                if (!recorder.IsFrozen())
                {
                    recorder.FreezeCapture();
                }
                StillWrongBugSheet.OpenWithSession(recorder.GetSessionDir(), null, null, string.Empty, string.Empty, "freeze", Screen.width, Screen.height);
            }
            catch
            {
            }
            EditorApplication.delayCall += PausePlay;
        }
        private static void PausePlay()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPaused = true;
            }
        }
        public static void ToggleRecord()
        {
            try
            {
                RollingCaptureRecorder recorder = UnityEngine.Object.FindAnyObjectByType<RollingCaptureRecorder>();
                if (recorder == null)
                {
                    if (!EditorApplication.isPlaying)
                    {
                        SetEnabled(!IsEnabled());
                        StillWrongBugSheet.RepaintAll();
                        return;
                    }
                    SetEnabled(true);
                    StartRollingRecorder();
                    StillWrongBugSheet.ReloadOpenReview();
                    StillWrongBugSheet.RepaintAll();
                    return;
                }
                if (recorder.IsFrozen())
                {
                    SetEnabled(true);
                    recorder.UnfreezeCapture();
                    EditorApplication.isPaused = false;
                }
                else
                {
                    recorder.FreezeCapture();
                    SetEnabled(false);
                }
                StillWrongBugSheet.ReloadOpenReview();
                StillWrongBugSheet.RepaintAll();
            }
            catch
            {
            }
        }
        public static bool CircleIsHot()
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    return IsEnabled();
                }
                RollingCaptureRecorder recorder = UnityEngine.Object.FindAnyObjectByType<RollingCaptureRecorder>();
                if (recorder == null)
                {
                    return false;
                }
                return recorder.IsCapturing() && !recorder.IsFrozen();
            }
            catch
            {
                return false;
            }
        }
        public static long CapturedFrameCount()
        {
            try
            {
                string path = StatusFilePath();
                if (!File.Exists(path))
                {
                    return 0;
                }
                RollingStatusFile status = JsonUtility.FromJson<RollingStatusFile>(File.ReadAllText(path));
                if (status == null)
                {
                    return 0;
                }
                return status.CapturedFrames;
            }
            catch
            {
                return 0;
            }
        }
        // True when a recorder is capturing. Recorders that stopped capturing (left over from
        // Edit Mode or a scene reload) are removed so a new one can start.
        private static bool FindLiveRecorder()
        {
            RollingCaptureRecorder[] recorders = UnityEngine.Object.FindObjectsByType<RollingCaptureRecorder>(FindObjectsInactive.Include);
            bool live = false;
            for (int i = 0; i < recorders.Length; i++)
            {
                RollingCaptureRecorder r = recorders[i];
                if (r == null)
                {
                    continue;
                }
                if (r.IsCapturing() && !live)
                {
                    live = true;
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(r.gameObject);
            }
            return live;
        }
        public static void StartRollingRecorder()
        {
            if (FindLiveRecorder())
            {
                return;
            }
            GameObject host = new GameObject("StillWrong_RollingRecorder");
            // Never saved into the scene; a copy rebuilt by a scene reload would sit there without capturing.
            host.hideFlags = HideFlags.DontSave;
            RollingCaptureRecorder recorder = host.AddComponent<RollingCaptureRecorder>();
            recorder.Begin(null, StillWrongSettings.RetentionMinutes() * 60.0, StillWrongSettings.TimelineFps());
        }
        public static void ResumeRecording()
        {
            try
            {
                RollingCaptureRecorder recorder = UnityEngine.Object.FindAnyObjectByType<RollingCaptureRecorder>();
                if (recorder != null && recorder.IsFrozen())
                {
                    recorder.UnfreezeCapture();
                }
                EditorApplication.isPaused = false;
            }
            catch
            {
            }
        }
        public static string LastSessionDir()
        {
            try
            {
                string path = StatusFilePath();
                if (!File.Exists(path))
                {
                    return string.Empty;
                }
                RollingStatusFile status = JsonUtility.FromJson<RollingStatusFile>(File.ReadAllText(path));
                if (status == null || string.IsNullOrEmpty(status.SessionId))
                {
                    return string.Empty;
                }
                string dir = Path.Combine(RollingCaptureRecorder.SessionsRoot(), status.SessionId);
                return Directory.Exists(dir) ? dir : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        public const string StoppedKey = "StillWrong.BackgroundRecordingStopped";
        public static bool WasStopped()
        {
            try
            {
                return EditorPrefs.GetBool(StoppedKey, false);
            }
            catch
            {
                return false;
            }
        }
        public static void SetStopped(bool stopped)
        {
            try
            {
                EditorPrefs.SetBool(StoppedKey, stopped);
            }
            catch
            {
            }
        }
        public static string RunBugDispatch(string sessionDir, string frameChunk, string frameFile, double frameClock, string candidatePath, List<string> candidatePaths, string sentence, SheetSpecOutcome authored, out string progress)
        {
            progress = string.Empty;
            if (authored == null)
            {
                return "failed: spec authoring returned nothing";
            }
            bool userMark = !string.IsNullOrEmpty(candidatePath);
            bool specMiss = userMark && authored.Failure != null && authored.Failure.IndexOf("did not converge", StringComparison.Ordinal) >= 0;
            if (!string.IsNullOrEmpty(authored.Failure) && !specMiss)
            {
                return authored.Failure;
            }
            if (EditorApplication.isPlaying)
            {
                return "failed: exit Play Mode before dispatch, reduction edits scenes";
            }
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string dispatchDir = StillWrong.Dispatch.DispatchService.PacketRoot();
            string progressFile = Path.Combine(dispatchDir, "progress-" + stamp + ".txt");
            StillWrong.Core.Oracle.OracleSpec spec = authored.Spec;
            StillWrong.Core.Oracle.OracleResult live = authored.LiveResult;
            string verdict = live != null ? live.Verdict.ToString() : "Inconclusive";
            bool probeMiss = verdict != "StillWrong";
            string scaleReason = ScaleReason(live);
            bool scaleHit = !string.IsNullOrEmpty(scaleReason);
            if (probeMiss && !userMark)
            {
                return "failed: live check is " + verdict + ", not StillWrong";
            }
            string specNote = specMiss ? "spec did not converge; user mark is the bug" : (probeMiss ? "probe " + verdict + "; user mark is the bug" : (authored.UsedDemoDefault ? "spec StillWrong via no-key demo default" : "spec StillWrong via frozen authoring attempts=" + authored.Attempts));
            WriteDispatchProgress(progressFile, specNote);
            string scenePath = string.Empty;
            string mapNote;
            candidatePath = MapToScenePath(candidatePath, ref candidatePaths, out mapNote);
            string sceneFailure = FindSourceScene(candidatePath, out scenePath);
            if (!string.IsNullOrEmpty(sceneFailure))
            {
                return "failed: " + sceneFailure;
            }
            if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
            {
                return "failed: scene file not on disk for " + candidatePath;
            }
            string reductionOut = "Assets/StillWrongDispatch/" + stamp;
            string reducedScenePath = string.Empty;
            StillWrong.Core.ReductionResult dispatchResult = null;
            List<string> kept = new List<string>();
            List<string> removed = new List<string>();
            try
            {
                System.Func<StillWrong.Core.Oracle.OracleSpec, StillWrong.Core.OracleVerdict> markOracle = null;
                List<string> protect = null;
                if (specMiss || probeMiss || scaleHit)
                {
                    markOracle = ignored => StillWrong.Core.OracleVerdict.StillWrong;
                    protect = new List<string>();
                    protect.Add(candidatePath);
                }
                var reduced = StillWrong.Editor.Reduction.UnitySceneCopyReducer.ReduceScene(scenePath, spec, reductionOut, null, markOracle, protect);
                reducedScenePath = reduced.ReducedScenePath;
                dispatchResult = reduced.Result;
                if (reduced.Result != null)
                {
                    if (reduced.Result.KeptNodeIds != null)
                    {
                        kept = new List<string>(reduced.Result.KeptNodeIds);
                    }
                    if (reduced.Result.RemovedNodeIds != null)
                    {
                        removed = new List<string>(reduced.Result.RemovedNodeIds);
                    }
                }
            }
            catch (Exception ex)
            {
                return "failed: reduction-failed: " + ex.GetType().Name + " " + ex.Message;
            }
            WriteDispatchProgress(progressFile, "reduced kept=" + kept.Count + " removed=" + removed.Count + " scene=" + reducedScenePath);
            RememberSourceScene(reducedScenePath, scenePath);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string bundleDir = Path.Combine(projectRoot, "Library", "StillWrong", "dispatch-bundle", stamp);
            string manifestPath = string.Empty;
            try
            {
                manifestPath = StillWrong.Editor.Export.UnityReproBundleExporter.ExportBundle(reducedScenePath, "d8-bug", spec, dispatchResult, bundleDir, null, null, sessionDir, null);
            }
            catch (Exception ex)
            {
                return "failed: export-failed: " + ex.GetType().Name + " " + ex.Message;
            }
            WriteDispatchProgress(progressFile, "exported manifest=" + manifestPath);
            string freshVerdict = string.Empty;
            int verifyCode = -1;
            long verifyMs = 0;
            if (specMiss)
            {
                freshVerdict = "spec did not converge";
                WriteDispatchProgress(progressFile, "spec did not converge; user mark is the bug");
            }
            else if (!probeMiss && !scaleHit)
            {
                string verifyFailure = RunFreshProcessVerify(EditorApplication.applicationPath, bundleDir, manifestPath, 1200000, out freshVerdict, out verifyCode, out verifyMs);
                if (!string.IsNullOrEmpty(verifyFailure))
                {
                    return "failed: " + verifyFailure + " (" + verifyMs + "ms)";
                }
                if (freshVerdict != "StillWrong")
                {
                    return "failed: fresh-process verdict is " + freshVerdict + ", not StillWrong";
                }
                WriteDispatchProgress(progressFile, "verified " + freshVerdict + " exit=" + verifyCode + " ms=" + verifyMs);
            }
            else if (scaleHit)
            {
                freshVerdict = scaleReason;
                WriteDispatchProgress(progressFile, "scale reading " + scaleReason);
            }
            else
            {
                freshVerdict = verdict;
                WriteDispatchProgress(progressFile, "user mark kept; probe " + verdict);
            }
            List<string> reasons = new List<string>();
            if (live != null && live.Reasons != null)
            {
                reasons = new List<string>(live.Reasons);
            }
            if (specMiss)
            {
                reasons.Add("spec did not converge; user mark is the bug");
            }
            else if (probeMiss)
            {
                reasons.Add("probe verdict " + verdict);
                reasons.Add("user mark is the bug");
            }
            StillWrong.Dispatch.DispatchPacket packet = BuildBugPacket(sessionDir, frameChunk, frameFile, frameClock, candidatePath, candidatePaths, sentence, reducedScenePath, verdict, reasons);
            packet.SourceScenePath = scenePath;
            packet.KeptNodeIds = kept;
            packet.RemovedNodeIds = removed;
            packet.BundlePath = bundleDir;
            packet.FreshProcessVerdict = freshVerdict;
            StillWrong.Dispatch.DispatchReceipt receipt = StillWrong.Dispatch.DispatchService.SendPacket(packet, StillWrong.Editor.UI.StillWrongSettings.PreferredAgent(), StillWrong.Editor.UI.StillWrongSettings.BranchPrefix());
            if (!receipt.Sent && !receipt.Saved)
            {
                return "failed: " + receipt.Reason;
            }
            WriteDispatchProgress(progressFile, "dispatched route=" + receipt.Route + " branch=" + receipt.Branch + " file=" + receipt.PacketFile);
            progress = DispatchProgressLine(receipt, false);
            return string.Empty;
        }
        public static string RunBugDispatchUnverified(string sessionDir, string frameChunk, string frameFile, double frameClock, string candidatePath, List<string> candidatePaths, string sentence, out string progress)
        {
            progress = string.Empty;
            SheetSpecOutcome authored = FrozenSessionSheet.AuthorSpec(sentence, candidatePath, 6);
            if (!string.IsNullOrEmpty(authored.Failure))
            {
                return authored.Failure;
            }
            StillWrong.Core.Oracle.OracleResult live = authored.LiveResult;
            string verdict = live != null ? live.Verdict.ToString() : "Inconclusive";
            string scenePath = string.Empty;
            string mapNote;
            candidatePath = MapToScenePath(candidatePath, ref candidatePaths, out mapNote);
            string sceneFailure = FindSourceScene(candidatePath, out scenePath);
            if (!string.IsNullOrEmpty(sceneFailure))
            {
                return "failed: " + sceneFailure;
            }
            List<string> reasons = new List<string>();
            if (live != null && live.Reasons != null)
            {
                reasons = new List<string>(live.Reasons);
            }
            StillWrong.Dispatch.DispatchPacket packet = BuildBugPacket(sessionDir, frameChunk, frameFile, frameClock, candidatePath, candidatePaths, sentence, scenePath, verdict, reasons);
            if (!IsDerivedScene(scenePath))
            {
                packet.SourceScenePath = scenePath;
            }
            packet.FreshProcessVerdict = "Unverified";
            StillWrong.Dispatch.DispatchReceipt receipt = StillWrong.Dispatch.DispatchService.SendPacket(packet, StillWrong.Editor.UI.StillWrongSettings.PreferredAgent(), StillWrong.Editor.UI.StillWrongSettings.BranchPrefix());
            if (!receipt.Sent && !receipt.Saved)
            {
                return "failed: " + receipt.Reason;
            }
            progress = DispatchProgressLine(receipt, true);
            return string.Empty;
        }
        public static void BeginBugDispatchUnverified(string sessionDir, string frameChunk, string frameFile, double frameClock, string candidatePath, List<string> candidatePaths, string sentence, Action<string, string> done)
        {
            FrozenSessionSheet.BeginAuthorSpec(sentence, candidatePath, 6, authored =>
            {
                string progress = string.Empty;
                string failure;
                try
                {
                    failure = FinishUnverifiedDispatch(sessionDir, frameChunk, frameFile, frameClock, candidatePath, candidatePaths, sentence, authored, out progress);
                }
                catch (Exception ex)
                {
                    failure = "failed: " + ex.GetType().Name + ": " + ex.Message;
                    progress = string.Empty;
                }
                if (done != null)
                {
                    done(failure, progress);
                }
            });
        }
        private static string FinishUnverifiedDispatch(string sessionDir, string frameChunk, string frameFile, double frameClock, string candidatePath, List<string> candidatePaths, string sentence, SheetSpecOutcome authored, out string progress)
        {
            progress = string.Empty;
            if (authored == null)
            {
                return "failed: spec authoring returned nothing";
            }
            if (!string.IsNullOrEmpty(authored.Failure))
            {
                return authored.Failure;
            }
            StillWrong.Core.Oracle.OracleResult live = authored.LiveResult;
            string verdict = live != null ? live.Verdict.ToString() : "Inconclusive";
            string scenePath = string.Empty;
            string mapNote;
            candidatePath = MapToScenePath(candidatePath, ref candidatePaths, out mapNote);
            string sceneFailure = FindSourceScene(candidatePath, out scenePath);
            if (!string.IsNullOrEmpty(sceneFailure))
            {
                return "failed: " + sceneFailure;
            }
            List<string> reasons = new List<string>();
            if (live != null && live.Reasons != null)
            {
                reasons = new List<string>(live.Reasons);
            }
            StillWrong.Dispatch.DispatchPacket packet = BuildBugPacket(sessionDir, frameChunk, frameFile, frameClock, candidatePath, candidatePaths, sentence, scenePath, verdict, reasons);
            if (!IsDerivedScene(scenePath))
            {
                packet.SourceScenePath = scenePath;
            }
            packet.FreshProcessVerdict = "Unverified";
            StillWrong.Dispatch.DispatchReceipt receipt = StillWrong.Dispatch.DispatchService.SendPacket(packet, StillWrong.Editor.UI.StillWrongSettings.PreferredAgent(), StillWrong.Editor.UI.StillWrongSettings.BranchPrefix());
            if (!receipt.Sent && !receipt.Saved)
            {
                return "failed: " + receipt.Reason;
            }
            progress = DispatchProgressLine(receipt, true);
            return string.Empty;
        }
        private static StillWrong.Dispatch.DispatchPacket BuildBugPacket(string sessionDir, string frameChunk, string frameFile, double frameClock, string candidatePath, List<string> candidatePaths, string sentence, string scenePath, string verdict, List<string> reasons)
        {
            StillWrong.Dispatch.DispatchPacket packet = new StillWrong.Dispatch.DispatchPacket();
            packet.Mode = "Bug";
            packet.SessionDir = sessionDir;
            packet.FrameChunk = frameChunk;
            packet.FrameFile = frameFile;
            packet.FrameClockSeconds = frameClock;
            packet.ScenePath = scenePath;
            packet.Target.Path = candidatePath;
            packet.Target.Name = LeafName(candidatePath);
            if (candidatePaths != null)
            {
                packet.CandidatePaths = new List<string>(candidatePaths);
            }
            packet.Sentence = sentence;
            packet.FailureVerdict = verdict;
            if (reasons != null)
            {
                packet.FailureReasons = new List<string>(reasons);
            }
            packet.TargetBlocks = StillWrong.Editor.UI.StillWrongBugSheet.CaptureTargetBlocks(scenePath, packet.Target.Name);
            return packet;
        }
        public const string LastWorktreeKey = "StillWrong.LastWorktree";
        public static void RememberWorktree(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                EditorPrefs.SetString(LastWorktreeKey, path);
            }
            catch
            {
            }
        }
        public static string LastWorktree()
        {
            try
            {
                return EditorPrefs.GetString(LastWorktreeKey, string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }
        public static string FindCheckWorktree()
        {
            string stored = LastWorktree();
            if (!string.IsNullOrEmpty(stored) && Directory.Exists(stored))
            {
                return stored;
            }
            string parent = StillWrong.Dispatch.DispatchService.WorktreeParent();
            if (!Directory.Exists(parent))
            {
                return string.Empty;
            }
            string best = string.Empty;
            string[] dirs = Directory.GetDirectories(parent);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string name = Path.GetFileName(dir);
                if (name == "agent-smoke")
                {
                    continue;
                }
                string[] packets = Directory.GetFiles(dir, "packet-*.json");
                if (packets == null || packets.Length == 0)
                {
                    continue;
                }
                if (string.CompareOrdinal(dir, best) > 0)
                {
                    best = dir;
                }
            }
            return best;
        }

        private static string DispatchProgressLine(StillWrong.Dispatch.DispatchReceipt receipt, bool unverified)
        {
            if (receipt != null)
            {
                RememberWorktree(receipt.WorktreePath);
            }
            string tag = unverified ? "unverified " : string.Empty;
            if (receipt.Sent && receipt.Route == "muse-handoff")
            {
                return MuseProgressLine(receipt, tag);
            }
            if (receipt.Sent)
            {
                return "Sent " + tag + "packet via " + receipt.Route + " on " + receipt.Branch + ".";
            }
            return "Saved " + tag + "packet to " + receipt.PacketFile + ". No agent connected.";
        }
        private static string MuseProgressLine(StillWrong.Dispatch.DispatchReceipt receipt, string tag)
        {
            string path = receipt.WorktreePath;
            string command = StillWrongSettings.AgentCommand();
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(path))
            {
                return "Saved " + tag + "packet in " + path + ". No agent was started.";
            }
            string packet = System.IO.Path.Combine(path, System.IO.Path.GetFileName(receipt.PacketFile));
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo();
                info.FileName = command;
                info.Arguments = QuoteArg(path) + " " + QuoteArg(packet);
                info.WorkingDirectory = path;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                System.Diagnostics.Process proc = System.Diagnostics.Process.Start(info);
                if (proc == null)
                {
                    return "Saved " + tag + "packet in " + path + ". Agent command failed: process-not-started.";
                }
                return "Saved " + tag + "packet in " + path + ". Agent command started.";
            }
            catch (System.Exception ex)
            {
                return "Saved " + tag + "packet in " + path + ". Agent command failed: " + ex.GetType().Name + ".";
            }
        }
        private static string QuoteArg(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string ScaleReason(StillWrong.Core.Oracle.OracleResult live)
        {
            if (live == null || live.Reasons == null)
            {
                return string.Empty;
            }
            for (int i = 0; i < live.Reasons.Count; i++)
            {
                string reason = live.Reasons[i];
                if (!string.IsNullOrEmpty(reason) && reason.StartsWith("scale "))
                {
                    return reason;
                }
            }
            return string.Empty;
        }
        // Objects spawned in Play Mode (a body a component builds at runtime) are gone once the
        // Editor leaves Play Mode, so the saved scene has no such path. Use the nearest ancestor
        // that exists in the scene as the target and keep the runtime path as a candidate.
        public static string MapToScenePath(string candidatePath, ref List<string> candidatePaths, out string note)
        {
            note = string.Empty;
            if (string.IsNullOrEmpty(candidatePath) || StillWrong.Oracle.UnitySymptomOracleRunner.FindGameObjectByPath(candidatePath) != null)
            {
                return candidatePath;
            }
            string path = candidatePath;
            while (true)
            {
                int slash = path.LastIndexOf('/');
                if (slash <= 0)
                {
                    return candidatePath;
                }
                path = path.Substring(0, slash);
                if (StillWrong.Oracle.UnitySymptomOracleRunner.FindGameObjectByPath(path) != null)
                {
                    break;
                }
            }
            List<string> paths = candidatePaths != null ? new List<string>(candidatePaths) : new List<string>();
            if (!paths.Contains(candidatePath))
            {
                paths.Insert(0, candidatePath);
            }
            candidatePaths = paths;
            note = candidatePath + " is spawned at runtime; using " + path + " from the saved scene";
            return path;
        }
        // The reduced copy is left open after a Send. Remember which scene it came from, so a
        // second Send from the copy goes back to that scene instead of guessing by object name.
        private static string SourceSceneKey(string derivedPath)
        {
            return "StillWrong.SourceScene." + (derivedPath ?? string.Empty).Replace('\\', '/');
        }
        private static void RememberSourceScene(string reducedScenePath, string sourceScenePath)
        {
            if (string.IsNullOrEmpty(reducedScenePath) || string.IsNullOrEmpty(sourceScenePath))
            {
                return;
            }
            EditorPrefs.SetString(SourceSceneKey(reducedScenePath), sourceScenePath);
            string baseline = reducedScenePath.Replace("_Reduced.unity", "_Baseline.unity");
            EditorPrefs.SetString(SourceSceneKey(baseline), sourceScenePath);
        }
        private static string FindSourceScene(string candidatePath, out string scenePath)
        {
            string failure = StillWrong.Dispatch.DispatchService.FindSceneForPath(candidatePath, out scenePath);
            if (!string.IsNullOrEmpty(failure))
            {
                return failure;
            }
            if (!IsDerivedScene(scenePath))
            {
                return string.Empty;
            }
            string remembered = EditorPrefs.GetString(SourceSceneKey(scenePath), string.Empty);
            if (!string.IsNullOrEmpty(remembered) && File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, remembered)))
            {
                scenePath = remembered;
                return string.Empty;
            }
            string source = FindOriginalScene(scenePath, candidatePath);
            if (string.IsNullOrEmpty(source))
            {
                return "original-scene-not-found for " + candidatePath;
            }
            scenePath = source;
            return string.Empty;
        }
        private static bool IsDerivedScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return false;
            }
            string norm = scenePath.Replace('\\', '/');
            if (norm == "Assets/StillWrongDispatch" || norm.StartsWith("Assets/StillWrongDispatch/"))
            {
                return true;
            }
            string file = Path.GetFileNameWithoutExtension(norm);
            return file.EndsWith("_Reduced");
        }
        private static string FindOriginalScene(string derivedPath, string hierarchyPath)
        {
            string baseName = Path.GetFileNameWithoutExtension(derivedPath.Replace('\\', '/'));
            while (baseName.EndsWith("_Reduced"))
            {
                baseName = baseName.Substring(0, baseName.Length - "_Reduced".Length);
            }
            string leaf = hierarchyPath ?? string.Empty;
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0)
            {
                leaf = leaf.Substring(slash + 1);
            }
            string needle = "m_Name: " + leaf;
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            List<string> named = new List<string>();
            List<string> others = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (IsDerivedScene(path))
                {
                    continue;
                }
                if (Path.GetFileNameWithoutExtension(path) == baseName)
                {
                    named.Add(path);
                }
                else
                {
                    others.Add(path);
                }
            }
            string namedHit = PickSceneContaining(named, needle);
            if (!string.IsNullOrEmpty(namedHit))
            {
                return namedHit;
            }
            if (named.Count == 1)
            {
                return named[0];
            }
            return PickSceneContaining(others, needle);
        }
        private static string PickSceneContaining(List<string> paths, string needle)
        {
            string hit = string.Empty;
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string abs = path;
                if (path.StartsWith("Assets/"))
                {
                    abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
                }
                if (!File.Exists(abs))
                {
                    continue;
                }
                string body = File.ReadAllText(abs);
                if (body.IndexOf(needle, StringComparison.Ordinal) < 0)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(hit))
                {
                    return string.Empty;
                }
                hit = path;
            }
            return hit;
        }
        private static string RunFreshProcessVerify(string editorBinary, string bundleDir, string manifestPath, int timeoutMs, out string oracleVerdict, out int exitCode, out long durationMs)
        {
            oracleVerdict = string.Empty;
            exitCode = -1;
            durationMs = 0;
            if (string.IsNullOrEmpty(editorBinary) || !File.Exists(editorBinary))
            {
                return "verify-no-editor-binary";
            }
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                return "verify-no-manifest";
            }
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = editorBinary;
                info.Arguments = "-batchmode -projectPath \"" + bundleDir + "\" -executeMethod StillWrong.Batch.Verify.Run -manifest \"" + manifestPath + "\"";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                bool grouped = File.Exists("/usr/bin/setsid");
                if (grouped)
                {
                    info.Arguments = QuoteArg(editorBinary) + " " + info.Arguments;
                    info.FileName = "/usr/bin/setsid";
                }
                Process proc = Process.Start(info);
                if (proc == null)
                {
                    watch.Stop();
                    durationMs = watch.ElapsedMilliseconds;
                    return "verify-process-not-started";
                }
                System.Text.StringBuilder drainedOut = new System.Text.StringBuilder();
                System.Text.StringBuilder drainedErr = new System.Text.StringBuilder();
                proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        drainedOut.AppendLine(e.Data);
                    }
                };
                proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        drainedErr.AppendLine(e.Data);
                    }
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                bool exited = proc.WaitForExit(timeoutMs);
                if (!exited)
                {
                    KillVerifyProcess(proc, grouped);
                    try
                    {
                        proc.WaitForExit(5000);
                    }
                    catch
                    {
                    }
                    watch.Stop();
                    durationMs = watch.ElapsedMilliseconds;
                    return "verify-timeout";
                }
                proc.WaitForExit();
                watch.Stop();
                durationMs = watch.ElapsedMilliseconds;
                exitCode = proc.ExitCode;
            }
            catch (Exception ex)
            {
                watch.Stop();
                durationMs = watch.ElapsedMilliseconds;
                return "verify-launch-failed: " + ex.GetType().Name;
            }
            try
            {
                StillWrong.Editor.Export.ReproManifestDto manifest = JsonUtility.FromJson<StillWrong.Editor.Export.ReproManifestDto>(File.ReadAllText(manifestPath));
                if (manifest == null || manifest.fresh_process_verification == null)
                {
                    return "verify-no-verdict-in-manifest";
                }
                oracleVerdict = manifest.fresh_process_verification.oracle_verdict ?? string.Empty;
                if (exitCode != 0 && manifest.fresh_process_verification.exit_status == 0)
                {
                    exitCode = 0;
                }
            }
            catch (Exception ex)
            {
                return "verify-manifest-unreadable: " + ex.GetType().Name;
            }
            if (exitCode != 0)
            {
                return "verify-exit-" + exitCode + ": " + oracleVerdict;
            }
            return string.Empty;
        }
        private static void KillVerifyProcess(Process proc, bool grouped)
        {
            if (grouped)
            {
                try
                {
                    ProcessStartInfo kill = new ProcessStartInfo();
                    kill.FileName = "/bin/kill";
                    kill.Arguments = "-KILL -" + proc.Id;
                    kill.UseShellExecute = false;
                    kill.CreateNoWindow = true;
                    Process killer = Process.Start(kill);
                    if (killer != null)
                    {
                        killer.WaitForExit(2000);
                    }
                }
                catch
                {
                }
            }
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch
            {
            }
        }
        private static void WriteDispatchProgress(string progressFile, string line)
        {
            try
            {
                string dir = Path.GetDirectoryName(progressFile);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(progressFile, DateTime.UtcNow.ToString("o") + " " + line + "\n");
            }
            catch
            {
            }
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
