using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Oracle;
using StillWrong.Recording;
using StillWrong.Replay;
using StillWrong.Editor.Fixtures;
namespace StillWrong.Editor.Reduction
{
    [Serializable]
    public sealed class C1TrialHistoryEntry
    {
        public List<string> RemovalIds = new List<string>();
        public string Verdict = string.Empty;
    }
    [Serializable]
    public sealed class C1TrialProbeBlock
    {
        public string OverflowPx = string.Empty;
        public string Inside = string.Empty;
        public string TextOverflowing = string.Empty;
        public string ActiveBody = string.Empty;
        public string ActiveMask = string.Empty;
    }
    [Serializable]
    public sealed class C1TrialLogEntry
    {
        public int TrialIndex;
        public List<string> RemovalIds = new List<string>();
        public string Verdict = string.Empty;
        public List<string> Reasons = new List<string>();
        public C1TrialProbeBlock Probes = new C1TrialProbeBlock();
        public long DurationMs;
        public string Note = string.Empty;
        public int EditDestroyed;
        public int PlayDestroyed;
    }
    [Serializable]
    public sealed class C1ControlBlock
    {
        public string SceneKind = string.Empty;
        public string Verdict = string.Empty;
        public List<string> Reasons = new List<string>();
        public C1TrialProbeBlock Probes = new C1TrialProbeBlock();
    }
    [Serializable]
    public sealed class C1ReductionState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public List<string> PendingRemovalIds = new List<string>();
        public string PendingKind = string.Empty;
        public string PendingScene = string.Empty;
        public int PendingStep;
        public int PendingEditDestroyed;
        public int PendingPlayDestroyed;
        public List<C1TrialHistoryEntry> History = new List<C1TrialHistoryEntry>();
        public List<C1TrialLogEntry> TrialLog = new List<C1TrialLogEntry>();
        public C1ControlBlock Baseline = new C1ControlBlock();
        public C1ControlBlock Healthy = new C1ControlBlock();
        public C1ControlBlock Invalid = new C1ControlBlock();
        public double PhaseStartTime;
        public double WallStartTime;
        public long StartAbsFrame;
        public string Failure = string.Empty;
        public bool EnterPlayModeOptionsEnabled;
        public int EnterPlayModeOptions;
    }
    public static class C1ReductionDriver
    {
        private const string ReductionsFolder = "Library/StillWrong/reductions";
        private const int MeasureStep = 6;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string ReductionsDir()
        {
            return Path.Combine(ProjectRoot(), ReductionsFolder);
        }
        public static string RunDir(string runId)
        {
            return Path.Combine(ReductionsDir(), runId);
        }
        private static string StatePath(string runId)
        {
            return Path.Combine(RunDir(runId), "trial-state.json");
        }
        public static string StartRun(string sessionDir)
        {
            string runId = "c1-reduction-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(RunDir(runId));
            C1ReductionState state = new C1ReductionState();
            state.Phase = "control-healthy-build";
            state.RunId = runId;
            state.SessionDir = sessionDir;
            state.WallStartTime = EditorApplication.timeSinceStartup;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            state.EnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            state.EnterPlayModeOptions = (int)EditorSettings.enterPlayModeOptions;
            SaveState(state);
            ArmTick();
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
        private static List<string> ActiveRuns()
        {
            List<string> runs = new List<string>();
            try
            {
                string root = ReductionsDir();
                if (!Directory.Exists(root))
                {
                    return runs;
                }
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string sp = Path.Combine(dirs[i], "trial-state.json");
                    if (File.Exists(sp))
                    {
                        try
                        {
                            C1ReductionState s = JsonUtility.FromJson<C1ReductionState>(File.ReadAllText(sp));
                            if (s != null && !string.IsNullOrEmpty(s.Phase) && s.Phase != "done" && s.Phase != "stopped")
                            {
                                runs.Add(Path.GetFileName(dirs[i]));
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            return runs;
        }
        private static C1ReductionState LoadState(string runId)
        {
            try
            {
                return JsonUtility.FromJson<C1ReductionState>(File.ReadAllText(StatePath(runId)));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(C1ReductionState state)
        {
            try
            {
                Directory.CreateDirectory(RunDir(state.RunId));
                File.WriteAllText(StatePath(state.RunId), JsonUtility.ToJson(state, true));
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
            List<string> runs = ActiveRuns();
            if (runs.Count == 0)
            {
                EditorApplication.update -= Tick;
                return;
            }
            for (int i = 0; i < runs.Count; i++)
            {
                try
                {
                    TickRun(runs[i]);
                }
                catch
                {
                }
            }
        }
        private static void TickRun(string runId)
        {
            C1ReductionState state = LoadState(runId);
            if (state == null)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup - state.WallStartTime > 3600.0)
            {
                state.Failure = "run-timeout";
                state.Phase = "stopped";
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
                return;
            }
            if (state.Phase == "control-healthy-build")
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                C1UiClipFixtureBuilder.BuildHealthyScene();
                state.PendingScene = "healthy";
                state.PendingKind = "control";
                state.PendingRemovalIds = new List<string>();
                state.Phase = "enterplay";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
                EditorApplication.isPlaying = true;
            }
            else if (state.Phase == "control-invalid-build")
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                C1UiClipFixtureBuilder.BuildInvalidScene();
                state.PendingScene = "invalid";
                state.PendingKind = "control";
                state.PendingRemovalIds = new List<string>();
                state.Phase = "enterplay";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
                EditorApplication.isPlaying = true;
            }
            else if (state.Phase == "control-failing-build")
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                C1UiClipFixtureBuilder.BuildFailingScene();
                state.PendingScene = "failing-baseline";
                state.PendingKind = "baseline";
                state.PendingRemovalIds = new List<string>();
                state.Phase = "enterplay";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
                EditorApplication.isPlaying = true;
            }
            else if (state.Phase == "enterplay")
            {
                if (!EditorApplication.isPlaying)
                {
                    return;
                }
                state.Phase = "replay";
                state.PendingStep = 0;
                state.StartAbsFrame = Time.frameCount;
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
            }
            else if (state.Phase == "mutate-trial")
            {
                TickMutateTrial(state);
            }
            else if (state.Phase == "enterplay-trial")
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = true;
                    return;
                }
                state.Phase = "replay";
                state.PendingStep = 0;
                state.StartAbsFrame = Time.frameCount;
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
            }
            else if (state.Phase == "replay")
            {
                TickReplay(state);
            }
            else if (state.Phase == "exitplay")
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                if (state.PendingKind == "trial")
                {
                    DiscardTrialEdits();
                }
                AdvanceAfterPlay(state);
            }
        }
        private static ReductionContext BuildContext()
        {
            List<ReductionNode> nodes = C1UiClipFixtureBuilder.CreateReductionNodes();
            OracleSpec spec = FixtureSpecs.C1();
            List<string> targets = new List<string>();
            if (spec.Targets != null)
            {
                for (int i = 0; i < spec.Targets.Count; i++)
                {
                    if (!string.IsNullOrEmpty(spec.Targets[i].ElementId))
                    {
                        targets.Add(spec.Targets[i].ElementId);
                    }
                }
            }
            string targetId = targets.Count > 0 ? targets[0] : string.Empty;
            return new ReductionContext(nodes, targetId, "MainCamera", "EventSystem", targets, null);
        }
        private static ReductionSession Rehydrate(C1ReductionState state)
        {
            ReductionSession session = new ReductionSession(BuildContext());
            for (int i = 0; i < state.History.Count; i++)
            {
                C1TrialHistoryEntry h = state.History[i];
                ISet<string> next = session.NextTrial();
                if (next == null)
                {
                    throw new InvalidOperationException("rehydrate-exhausted");
                }
                HashSet<string> a = new HashSet<string>(next, StringComparer.Ordinal);
                HashSet<string> b = new HashSet<string>(h.RemovalIds, StringComparer.Ordinal);
                if (!a.SetEquals(b))
                {
                    throw new InvalidOperationException("rehydrate-mismatch");
                }
                session.Report((OracleVerdict)Enum.Parse(typeof(OracleVerdict), h.Verdict));
            }
            return session;
        }
        private static bool MatchesNode(string goPath, string nodeId)
        {
            if (string.Equals(goPath, nodeId, StringComparison.Ordinal))
            {
                return true;
            }
            return goPath.EndsWith("/" + nodeId, StringComparison.Ordinal);
        }
        private static int DestroyCandidates(ISet<string> removalIds, HashSet<string> protectedIds, bool immediate)
        {
            int destroyed = 0;
            try
            {
                GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>();
                List<GameObject> targets = new List<GameObject>();
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i];
                    if (go == null)
                    {
                        continue;
                    }
                    string path = UnitySymptomOracleRunner.GetHierarchyPath(go);
                    for (int r = 0; r < removalIds.Count; r++)
                    {
                        string id = new List<string>(removalIds)[r];
                        if (protectedIds.Contains(id))
                        {
                            continue;
                        }
                        if (MatchesNode(path, id))
                        {
                            targets.Add(go);
                            break;
                        }
                    }
                }
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] != null)
                    {
                        if (immediate)
                        {
                            UnityEngine.Object.DestroyImmediate(targets[i]);
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(targets[i]);
                        }
                        destroyed++;
                    }
                }
            }
            catch
            {
            }
            return destroyed;
        }
        private static void TickMutateTrial(C1ReductionState state)
        {
            if (EditorApplication.isPlaying)
            {
                state.Failure = "mutate-entered-while-playing";
                state.Phase = "stopped";
                SaveState(state);
                try
                {
                    EditorApplication.isPlaying = false;
                }
                catch
                {
                }
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(C1UiClipFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            HashSet<string> protectedIds = BuildProtectedIds();
            HashSet<string> removal = new HashSet<string>(state.PendingRemovalIds, StringComparer.Ordinal);
            removal.ExceptWith(protectedIds);
            state.PendingEditDestroyed = DestroyCandidates(removal, protectedIds, true);
            state.PendingPlayDestroyed = 0;
            state.Phase = "enterplay-trial";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
            EditorApplication.isPlaying = true;
        }
        private static void DiscardTrialEdits()
        {
            try
            {
                EditorSceneManager.OpenScene(C1UiClipFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
        }
        private static HashSet<string> BuildProtectedIds()
        {
            HashSet<string> protectedIds = new HashSet<string>(StringComparer.Ordinal);
            OracleSpec spec0 = FixtureSpecs.C1();
            if (spec0.Targets != null)
            {
                for (int i = 0; i < spec0.Targets.Count; i++)
                {
                    protectedIds.Add(spec0.Targets[i].ElementId);
                }
            }
            protectedIds.Add("MainCamera");
            protectedIds.Add("EventSystem");
            return protectedIds;
        }
        private static string DescribeValue(MeasuredValue value)
        {
            if (value == null)
            {
                return "missing (no-source)";
            }
            return value.Describe();
        }
        private static void ReadProbes(UnityProbeSource source, C1TrialProbeBlock probes)
        {
            MeasuredValue value;
            if (source.TryGetValue(Probes.OverflowPx("body", "mask"), 0, out value))
            {
                probes.OverflowPx = DescribeValue(value);
            }
            else
            {
                probes.OverflowPx = "missing (no-source)";
            }
            if (source.TryGetValue(Probes.Inside("body", "mask"), 0, out value))
            {
                probes.Inside = DescribeValue(value);
            }
            else
            {
                probes.Inside = "missing (no-source)";
            }
            if (source.TryGetValue(Probes.TextOverflowing("body"), 0, out value))
            {
                probes.TextOverflowing = DescribeValue(value);
            }
            else
            {
                probes.TextOverflowing = "missing (no-source)";
            }
            if (source.TryGetValue(Probes.Active("body"), 0, out value))
            {
                probes.ActiveBody = DescribeValue(value);
            }
            else
            {
                probes.ActiveBody = "missing (no-source)";
            }
            if (source.TryGetValue(Probes.Active("mask"), 0, out value))
            {
                probes.ActiveMask = DescribeValue(value);
            }
            else
            {
                probes.ActiveMask = "missing (no-source)";
            }
        }
        private static SessionBundle LoadBundle(string sessionDir)
        {
            return SessionBundle.Load(sessionDir);
        }
        private static void TickReplay(C1ReductionState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                state.Failure = "replay-timeout";
                state.Phase = "stopped";
                SaveState(state);
                try
                {
                    EditorApplication.isPlaying = false;
                }
                catch
                {
                }
                return;
            }
            SessionBundle bundle;
            try
            {
                bundle = LoadBundle(state.SessionDir);
            }
            catch
            {
                state.Failure = "bundle-load-failed";
                state.Phase = "stopped";
                SaveState(state);
                try
                {
                    EditorApplication.isPlaying = false;
                }
                catch
                {
                }
                return;
            }
            Dictionary<int, StillWrong.Recording.InputEventRecord> legacyByFrame = new Dictionary<int, StillWrong.Recording.InputEventRecord>();
            for (int i = 0; i < bundle.Inputs.Events.Count; i++)
            {
                StillWrong.Recording.InputEventRecord rec = bundle.Inputs.Events[i];
                if (rec != null && rec.Path == "legacy" && !legacyByFrame.ContainsKey(rec.Frame))
                {
                    legacyByFrame.Add(rec.Frame, rec);
                }
            }
            List<StillWrong.Recording.FrameIndexEntry> kept = bundle.Frames.Frames;
            int step = Time.frameCount - (int)state.StartAbsFrame;
            if (step < 0)
            {
                step = 0;
            }
            if (step == 0)
            {
                HashSet<string> protectedIds = BuildProtectedIds();
                HashSet<string> removal = new HashSet<string>(state.PendingRemovalIds, StringComparer.Ordinal);
                removal.ExceptWith(protectedIds);
                state.PendingPlayDestroyed = DestroyCandidates(removal, protectedIds, false);
                try
                {
                    UnityEngine.Random.State st = JsonUtility.FromJson<UnityEngine.Random.State>(bundle.Manifest.StartRandomState);
                    UnityEngine.Random.state = st;
                }
                catch
                {
                }
                if (kept.Count > 0)
                {
                    Time.timeScale = kept[0].TimeScale;
                }
                Time.captureDeltaTime = 1.0f / 60.0f;
            }
            if (step < kept.Count)
            {
                StillWrong.Recording.FrameIndexEntry entry = kept[step];
                StillWrong.Recording.InputEventRecord rec = null;
                legacyByFrame.TryGetValue(entry.Frame, out rec);
                if (rec != null)
                {
                    InputFeed.WarpPointer(rec.PointerX, rec.PointerY);
                    if (rec.ButtonDownMask != 0 || rec.ButtonUpMask != 0)
                    {
                        DispatchEdges(rec);
                    }
                }
            }
            if (step < MeasureStep)
            {
                return;
            }
            int consoleErrors = 0;
            Application.LogCallback handler = (string condition, string trace, LogType type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    consoleErrors++;
                }
            };
            Application.logMessageReceived += handler;
            OracleSpec spec = FixtureSpecs.C1();
            UnityProbeSource source = new UnityProbeSource(spec);
            C1TrialProbeBlock probes = new C1TrialProbeBlock();
            ReadProbes(source, probes);
            EvaluationContext ctx = new EvaluationContext();
            ctx.ConsoleErrorCount = consoleErrors;
            StillWrongIdBuffer.SetAmbientConsoleErrors(consoleErrors);
            OracleResult result = OracleEvaluator.Evaluate(spec, source, ctx);
            Application.logMessageReceived -= handler;
            C1TrialLogEntry log = new C1TrialLogEntry();
            log.TrialIndex = state.TrialLog.Count;
            log.RemovalIds = new List<string>(state.PendingRemovalIds);
            log.Verdict = result.Verdict.ToString();
            log.Reasons = result.Reasons;
            log.Probes = probes;
            log.DurationMs = (long)((EditorApplication.timeSinceStartup - state.PhaseStartTime) * 1000.0);
            log.EditDestroyed = state.PendingEditDestroyed;
            log.PlayDestroyed = state.PendingPlayDestroyed;
            state.TrialLog.Add(log);
            C1TrialHistoryEntry h = new C1TrialHistoryEntry();
            h.RemovalIds = new List<string>(state.PendingRemovalIds);
            h.Verdict = result.Verdict.ToString();
                    if (state.PendingKind == "trial")
                    {
                        state.History.Add(h);
                    }
            state.Phase = "exitplay";
            SaveState(state);
            try
            {
                Time.timeScale = 1.0f;
                Time.captureDeltaTime = 0.0f;
            }
            catch
            {
            }
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static void DispatchEdges(StillWrong.Recording.InputEventRecord rec)
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
        private static void AdvanceAfterPlay(C1ReductionState state)
        {
            ReductionSession session;
            try
            {
                session = Rehydrate(state);
            }
            catch
            {
                state.Failure = "rehydrate-failed";
                state.Phase = "stopped";
                SaveState(state);
                return;
            }
            if (state.PendingKind == "control" || state.PendingKind == "baseline")
            {
                C1TrialLogEntry last = state.TrialLog[state.TrialLog.Count - 1];
                C1ControlBlock block = new C1ControlBlock();
                block.SceneKind = state.PendingScene;
                block.Verdict = last.Verdict;
                block.Reasons = last.Reasons;
                block.Probes = last.Probes;
                if (state.PendingScene == "healthy")
                {
                    state.Healthy = block;
                    state.Phase = "control-invalid-build";
                    SaveState(state);
                }
                else if (state.PendingScene == "invalid")
                {
                    state.Invalid = block;
                    state.Phase = "control-failing-build";
                    SaveState(state);
                }
                else
                {
                    state.Baseline = block;
                    if (last.Verdict != OracleVerdict.StillWrong.ToString())
                    {
                        state.Failure = "baseline-not-stillwrong";
                        state.Phase = "stopped";
                        SaveState(state);
                        return;
                    }
                    ISet<string> first = session.NextTrial();
                    if (first == null)
                    {
                        FinishRun(state, session);
                        return;
                    }
                    state.PendingRemovalIds = new List<string>(first);
                    state.PendingKind = "trial";
                    state.PendingScene = "failing";
                    state.Phase = "mutate-trial";
                    SaveState(state);
                }
                return;
            }
            if (state.PendingKind == "trial")
            {
                ISet<string> next = session.NextTrial();
                if (next == null)
                {
                    FinishRun(state, session);
                    return;
                }
                state.PendingRemovalIds = new List<string>(next);
                state.PendingScene = "failing";
                state.Phase = "mutate-trial";
                SaveState(state);
                return;
            }
            state.Failure = "unknown-pending-kind";
            state.Phase = "stopped";
            SaveState(state);
        }
        private static void FinishRun(C1ReductionState state, ReductionSession session)
        {
            try
            {
                if (!session.IsFinished)
                {
                    state.Failure = "session-not-finished";
                    state.Phase = "stopped";
                    SaveState(state);
                    return;
                }
                ReductionResult result = session.FinalResult;
                TrialSummaryFile summary = new TrialSummaryFile();
                summary.RunId = state.RunId;
                summary.SessionDir = state.SessionDir;
                summary.InitialCandidateCount = result.InitialCandidateCount;
                summary.FinalCandidateCount = result.FinalCandidateCount;
                summary.RemovedCount = result.RemovedCount;
                summary.KeptNodeIds = new List<string>(result.KeptNodeIds);
                summary.RemovedNodeIds = new List<string>(result.RemovedNodeIds);
                summary.IsMinimized = result.IsMinimized;
                summary.Trials = state.TrialLog;
                summary.Baseline = state.Baseline;
                summary.Healthy = state.Healthy;
                summary.Invalid = state.Invalid;
                summary.EnterPlayModeOptionsEnabledBefore = state.EnterPlayModeOptionsEnabled;
                summary.EnterPlayModeOptionsBefore = state.EnterPlayModeOptions;
                long wallMs = (long)((EditorApplication.timeSinceStartup - state.WallStartTime) * 1000.0);
                summary.WallMs = wallMs;
                summary.MeanTrialMs = state.TrialLog.Count > 0 ? wallMs / state.TrialLog.Count : 0;
                File.WriteAllText(Path.Combine(RunDir(state.RunId), "trial-log.json"), JsonUtility.ToJson(summary, true));
                state.Phase = "done";
                SaveState(state);
            }
            catch
            {
                state.Failure = "finish-failed";
                state.Phase = "stopped";
                SaveState(state);
            }
        }
    }
    [Serializable]
    public sealed class TrialSummaryFile
    {
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public int InitialCandidateCount;
        public int FinalCandidateCount;
        public int RemovedCount;
        public List<string> KeptNodeIds = new List<string>();
        public List<string> RemovedNodeIds = new List<string>();
        public bool IsMinimized;
        public List<C1TrialLogEntry> Trials = new List<C1TrialLogEntry>();
        public C1ControlBlock Baseline = new C1ControlBlock();
        public C1ControlBlock Healthy = new C1ControlBlock();
        public C1ControlBlock Invalid = new C1ControlBlock();
        public bool EnterPlayModeOptionsEnabledBefore;
        public int EnterPlayModeOptionsBefore;
        public long WallMs;
        public long MeanTrialMs;
    }
}

