using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
#if STILLWRONG_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Oracle;
namespace StillWrong.Replay
{
    public static class InputFeed
    {
        public static bool HasMouse()
        {
#if STILLWRONG_INPUTSYSTEM
            return Mouse.current != null;
#else
            return false;
#endif
        }
        public static bool WarpPointer(float x, float y)
        {
#if STILLWRONG_INPUTSYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            try
            {
                mouse.WarpCursorPosition(new Vector2(x, y));
                return true;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
    [Serializable]
    public sealed class ReplayFrameEntry
    {
        public int Frame;
        public string Driver = string.Empty;
        public string Values = string.Empty;
    }
    [Serializable]
    public sealed class ReplayResultFile
    {
        public string SessionId = string.Empty;
        public int FramesStepped;
        public int LegacyDispatched;
        public int InputSystemDispatched;
        public int AxisConsumed;
        public int ConsoleErrors;
        public List<ReplayFrameEntry> Frames = new List<ReplayFrameEntry>();
        public string ShippedVerdict = string.Empty;
        public List<string> ShippedReasons = new List<string>();
        public string CandidateVerdict = string.Empty;
        public List<string> CandidateReasons = new List<string>();
        public long DurationMs;
    }
    [Serializable]
    public sealed class DriverProbeValues
    {
        public string OverflowPx = string.Empty;
        public string Inside = string.Empty;
        public string TextOverflowing = string.Empty;
        public string ActiveBody = string.Empty;
        public string ActiveMask = string.Empty;
    }
    [Serializable]
    public sealed class DriverResultFile
    {
        public string SessionDirLive = string.Empty;
        public int FramesKept;
        public long JpegTotalBytes;
        public double MeanJpegBytes;
        public double MeanEncodeMs;
        public double MeanRegistryMs;
        public long ReadbackDrops;
        public long Overwrites;
        public int InputEventCount;
        public string MarkedPngPath = string.Empty;
        public int MarkedW;
        public int MarkedH;
        public long MarkedBytes;
        public int MeasureStep = 6;
        public int MeasureSessionFrame;
        public float SlotMinX;
        public float SlotMinY;
        public float SlotMaxX;
        public float SlotMaxY;
        public string SlotRectSpace = string.Empty;
        public int SlotRectSourceFrame;
        public int RegistryCountAtFrame;
        public int ScreenW;
        public int ScreenH;
        public int ReplayFramesStepped;
        public int ReplayLegacyDispatched;
        public int ReplayInputSystemDispatched;
        public int ReplayAxisConsumed;
        public int ReplayConsoleErrors;
        public long ReplayDurationMs;
        public string ShippedVerdict = string.Empty;
        public List<string> ShippedReasons = new List<string>();
        public string CandidateVerdict = string.Empty;
        public List<string> CandidateReasons = new List<string>();
        public DriverProbeValues Probes = new DriverProbeValues();
        public List<string> Limits = new List<string>();
        public string InputNote = string.Empty;
        public string FixtureNote = string.Empty;
        public string ReseedNote = string.Empty;
    }
    public sealed class SessionBundle
    {
        public string Folder = string.Empty;
        public StillWrong.Recording.SessionManifest Manifest;
        public StillWrong.Recording.FrameIndexFile Frames;
        public StillWrong.Recording.RegistryFile Registry;
        public StillWrong.Recording.InputEventFile Inputs;
        public StillWrong.Recording.SceneEventFile Scenes;
        public static SessionBundle Load(string folder)
        {
            SessionBundle bundle = new SessionBundle();
            bundle.Folder = folder;
            bundle.Manifest = JsonUtility.FromJson<StillWrong.Recording.SessionManifest>(File.ReadAllText(Path.Combine(folder, "session.json")));
            bundle.Frames = JsonUtility.FromJson<StillWrong.Recording.FrameIndexFile>(File.ReadAllText(Path.Combine(folder, "frames.json")));
            bundle.Registry = JsonUtility.FromJson<StillWrong.Recording.RegistryFile>(File.ReadAllText(Path.Combine(folder, "registry.json")));
            bundle.Inputs = JsonUtility.FromJson<StillWrong.Recording.InputEventFile>(File.ReadAllText(Path.Combine(folder, "inputs.json")));
            bundle.Scenes = JsonUtility.FromJson<StillWrong.Recording.SceneEventFile>(File.ReadAllText(Path.Combine(folder, "scenes.json")));
            return bundle;
        }
    }
    public static class CandidateSpec
    {
        public static OracleSpec Build()
        {
            OracleSpec spec = new OracleSpec();
            spec.SpecId = "C1-ui-clip-candidate";
            spec.DeveloperSentence = "Tooltip body overflows mask and escapes it.";
            spec.Targets = new List<TargetRef>();
            spec.Targets.Add(new TargetRef("body", "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody"));
            spec.Targets.Add(new TargetRef("mask", "BattleCanvas/ContextFlyout/HotbarSlotCard"));
            spec.WindowStart = 6;
            spec.WindowEnd = 6;
            spec.MeasurementFrames = new List<int>();
            spec.MeasurementFrames.Add(6);
            spec.Failure = Predicates.And(
                Predicates.GreaterThan(Probes.OverflowPx("body", "mask"), 12.0),
                Predicates.EqualBool(Probes.Inside("body", "mask"), false));
            spec.Invariants = new List<PredicateNode>();
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("body"), true));
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("mask"), true));
            return spec;
        }
    }
    public sealed class SessionReplayRunner : MonoBehaviour
    {
        public string SessionDir = string.Empty;
        public int MeasureStep = 6;
        public string ReplayResultPath = string.Empty;
        public string DriverResultPath = string.Empty;
        public void Start()
        {
            StartCoroutine(Run());
        }
        private static string DescribeValue(MeasuredValue value)
        {
            if (value == null)
            {
                return "missing (no-source)";
            }
            return value.Describe();
        }
        private static bool TryProbe(UnityProbeSource source, ProbeCall call, out string text)
        {
            text = "missing (no-source)";
            MeasuredValue value;
            if (!source.TryGetValue(call, 0, out value))
            {
                return false;
            }
            text = DescribeValue(value);
            return true;
        }
        private static void ReadPngSize(string path, out int w, out int h)
        {
            w = 0;
            h = 0;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > 28 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71)
                {
                    w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                    h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                }
            }
            catch
            {
                w = 0;
                h = 0;
            }
        }
        private IEnumerator Run()
        {
            ReplayResultFile replay = new ReplayResultFile();
            DriverResultFile driver = new DriverResultFile();
            SessionBundle bundle = null;
            try
            {
                bundle = SessionBundle.Load(SessionDir);
            }
            catch (Exception ex)
            {
                replay.ShippedVerdict = OracleVerdict.Inconclusive.ToString();
                replay.ShippedReasons.Add("session-load-failed: " + ex.GetType().Name);
                replay.CandidateVerdict = OracleVerdict.Inconclusive.ToString();
                replay.CandidateReasons.Add("session-load-failed");
                WriteFiles(replay, driver);
                yield break;
            }
            replay.SessionId = bundle.Manifest.Id;
            float prevDelta = Time.captureDeltaTime;
            float prevScale = Time.timeScale;
            int consoleErrors = 0;
            Application.LogCallback onLog = (string condition, string trace, LogType type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    consoleErrors++;
                }
            };
            Application.logMessageReceived += onLog;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string reseedNote = "reseed-failed";
                try
                {
                    UnityEngine.Random.State state = JsonUtility.FromJson<UnityEngine.Random.State>(bundle.Manifest.StartRandomState);
                    UnityEngine.Random.state = state;
                    reseedNote = "reseeded";
                }
                catch
                {
                    reseedNote = "reseed-unavailable";
                }
                driver.ReseedNote = reseedNote;
                List<StillWrong.Recording.FrameIndexEntry> kept = bundle.Frames.Frames;
                Dictionary<int, StillWrong.Recording.InputEventRecord> legacyByFrame = new Dictionary<int, StillWrong.Recording.InputEventRecord>();
                for (int i = 0; i < bundle.Inputs.Events.Count; i++)
                {
                    StillWrong.Recording.InputEventRecord rec = bundle.Inputs.Events[i];
                    if (rec != null && string.Equals(rec.Path, "legacy", StringComparison.Ordinal) && !legacyByFrame.ContainsKey(rec.Frame))
                    {
                        legacyByFrame.Add(rec.Frame, rec);
                    }
                }
                float gridScale = 1.0f;
                if (kept.Count > 0)
                {
                    gridScale = kept[0].TimeScale;
                }
                Time.timeScale = gridScale;
                Time.captureDeltaTime = 1.0f / 60.0f;
                int measureStep = MeasureStep;
                if (kept.Count == 0)
                {
                    measureStep = -1;
                }
                else if (measureStep >= kept.Count)
                {
                    measureStep = kept.Count - 1;
                }
                driver.MeasureStep = measureStep;
                OracleSpec shipped = FixtureSpecs.C1();
                OracleSpec candidate = CandidateSpec.Build();
                UnityProbeSource source = new UnityProbeSource(shipped);
                string shippedVerdict = OracleVerdict.Inconclusive.ToString();
                List<string> shippedReasons = new List<string>();
                string candidateVerdict = OracleVerdict.Inconclusive.ToString();
                List<string> candidateReasons = new List<string>();
                DriverProbeValues probes = new DriverProbeValues();
                int legacyCount = 0;
                int systemCount = 0;
                int axisCount = 0;
                for (int s = 0; s < kept.Count; s++)
                {
                    StillWrong.Recording.FrameIndexEntry entry = kept[s];
                    ReplayFrameEntry fe = new ReplayFrameEntry();
                    fe.Frame = entry.Frame;
                    fe.Driver = "none";
                    StillWrong.Recording.InputEventRecord rec = null;
                    legacyByFrame.TryGetValue(entry.Frame, out rec);
                    bool warped = false;
                    bool edged = false;
                    if (rec != null)
                    {
                        warped = InputFeed.WarpPointer(rec.PointerX, rec.PointerY);
                        if (warped)
                        {
                            systemCount++;
                            fe.Driver = "synthesized-inputsystem";
                        }
                        if (rec.AxisValues != null)
                        {
                            axisCount += rec.AxisValues.Count;
                        }
                        if (rec.ButtonDownMask != 0 || rec.ButtonUpMask != 0)
                        {
                            edged = DispatchButtonEdges(rec);
                            if (edged)
                            {
                                legacyCount++;
                                if (warped)
                                {
                                    fe.Driver = "synthesized-inputsystem+legacy-executeevents";
                                }
                                else
                                {
                                    fe.Driver = "legacy-executeevents";
                                }
                            }
                        }
                    }
                    yield return new WaitForEndOfFrame();
                    if (s == measureStep)
                    {
                        string v;
                        TryProbe(source, Probes.OverflowPx("body", "mask"), out v);
                        probes.OverflowPx = v;
                        TryProbe(source, Probes.Inside("body", "mask"), out v);
                        probes.Inside = v;
                        TryProbe(source, Probes.TextOverflowing("body"), out v);
                        probes.TextOverflowing = v;
                        TryProbe(source, Probes.Active("body"), out v);
                        probes.ActiveBody = v;
                        TryProbe(source, Probes.Active("mask"), out v);
                        probes.ActiveMask = v;
                        EvaluationContext ctx = new EvaluationContext();
                        ctx.ConsoleErrorCount = consoleErrors;
                        OracleResult shippedResult = OracleEvaluator.Evaluate(shipped, source, ctx);
                        shippedVerdict = shippedResult.Verdict.ToString();
                        shippedReasons = shippedResult.Reasons;
                        EvaluationContext ctx2 = new EvaluationContext();
                        ctx2.ConsoleErrorCount = consoleErrors;
                        OracleResult candidateResult = OracleEvaluator.Evaluate(candidate, source, ctx2);
                        candidateVerdict = candidateResult.Verdict.ToString();
                        candidateReasons = candidateResult.Reasons;
                        fe.Values = "overflowPx=" + probes.OverflowPx + " inside=" + probes.Inside + " textOverflowing=" + probes.TextOverflowing + " activeBody=" + probes.ActiveBody + " activeMask=" + probes.ActiveMask;
                        driver.MeasureSessionFrame = entry.Frame;
                    }
                    replay.Frames.Add(fe);
                }
                replay.FramesStepped = kept.Count;
                replay.LegacyDispatched = legacyCount;
                replay.InputSystemDispatched = systemCount;
                replay.AxisConsumed = axisCount;
                replay.ConsoleErrors = consoleErrors;
                replay.ShippedVerdict = shippedVerdict;
                replay.ShippedReasons = shippedReasons;
                replay.CandidateVerdict = candidateVerdict;
                replay.CandidateReasons = candidateReasons;
                FillDriverSessionFacts(driver, bundle, kept);
                driver.ReplayFramesStepped = kept.Count;
                driver.ReplayLegacyDispatched = legacyCount;
                driver.ReplayInputSystemDispatched = systemCount;
                driver.ReplayAxisConsumed = axisCount;
                driver.ReplayConsoleErrors = consoleErrors;
                driver.ShippedVerdict = shippedVerdict;
                driver.ShippedReasons = shippedReasons;
                driver.CandidateVerdict = candidateVerdict;
                driver.CandidateReasons = candidateReasons;
                driver.Probes = probes;
                driver.Limits.Add("scene-reset=fresh-fixture-scene-load-by-caller");
                driver.Limits.Add("random=" + driver.ReseedNote);
                driver.Limits.Add("input-synthesis=warp-pointer-plus-legacy-edges-no-byte-exact-device-replay");
                driver.Limits.Add("timescale-restored-to-session-first-frame");
                driver.InputNote = "pointer-and-buttons-from-legacy-records-warped-through-InputFeed-button-edges-via-ExecuteEvents-axis-samples-consumed-only";
                driver.FixtureNote = "fixture-builds-static-failing-layout-no-presenter-logic-pointer-has-no-scene-effect";
            }
            finally
            {
                clock.Stop();
                replay.DurationMs = clock.ElapsedMilliseconds;
                driver.ReplayDurationMs = clock.ElapsedMilliseconds;
                Time.timeScale = prevScale;
                Time.captureDeltaTime = prevDelta;
                Application.logMessageReceived -= onLog;
                WriteFiles(replay, driver);
            }
        }
        private void WriteFiles(ReplayResultFile replay, DriverResultFile driver)
        {
            try
            {
                if (!string.IsNullOrEmpty(ReplayResultPath))
                {
                    File.WriteAllText(ReplayResultPath, JsonUtility.ToJson(replay, true));
                }
            }
            catch
            {
            }
            try
            {
                if (!string.IsNullOrEmpty(DriverResultPath))
                {
                    File.WriteAllText(DriverResultPath, JsonUtility.ToJson(driver, true));
                }
            }
            catch
            {
            }
        }
        private static bool DispatchButtonEdges(StillWrong.Recording.InputEventRecord rec)
        {
            try
            {
                if (EventSystem.current == null)
                {
                    return false;
                }
                bool done = false;
                PointerEventData ped = new PointerEventData(EventSystem.current);
                ped.position = new Vector2(rec.PointerX, rec.PointerY);
                List<RaycastResult> results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, results);
                if (results.Count == 0)
                {
                    return false;
                }
                GameObject target = results[0].gameObject;
                if (rec.ButtonDownMask != 0)
                {
                    ped.pointerPressRaycast = results[0];
                    ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerDownHandler);
                    done = true;
                }
                if (rec.ButtonUpMask != 0)
                {
                    ped.pointerPressRaycast = results[0];
                    ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerClickHandler);
                    done = true;
                }
                return done;
            }
            catch
            {
                return false;
            }
        }
        private static void FillDriverSessionFacts(DriverResultFile driver, SessionBundle bundle, List<StillWrong.Recording.FrameIndexEntry> kept)
        {
            driver.SessionDirLive = bundle.Folder;
            driver.FramesKept = kept.Count;
            driver.JpegTotalBytes = bundle.Manifest.JpegTotalBytes;
            driver.MeanJpegBytes = bundle.Manifest.MeanJpegBytes;
            driver.MeanEncodeMs = bundle.Manifest.MeanEncodeMs;
            driver.MeanRegistryMs = bundle.Manifest.MeanRegistryMs;
            driver.ReadbackDrops = bundle.Manifest.ReadbackDrops;
            driver.Overwrites = bundle.Manifest.Overwrites;
            driver.InputEventCount = bundle.Manifest.InputEventCount;
            string markedPath = Path.Combine(bundle.Folder, "marked.png");
            driver.MarkedPngPath = markedPath;
            try
            {
                FileInfo info = new FileInfo(markedPath);
                driver.MarkedBytes = info.Exists ? info.Length : 0;
            }
            catch
            {
                driver.MarkedBytes = 0;
            }
            int w;
            int h;
            ReadPngSize(markedPath, out w, out h);
            driver.MarkedW = w;
            driver.MarkedH = h;
            driver.ScreenW = Screen.width;
            driver.ScreenH = Screen.height;
            driver.RegistryCountAtFrame = 0;
            if (driver.MeasureStep >= 0 && driver.MeasureStep < kept.Count)
            {
                int sessionFrame = kept[driver.MeasureStep].Frame;
                driver.SlotRectSourceFrame = sessionFrame;
                if (bundle.Registry != null && bundle.Registry.Frames != null)
                {
                    for (int i = 0; i < bundle.Registry.Frames.Count; i++)
                    {
                        StillWrong.Recording.RegistryFrame rf = bundle.Registry.Frames[i];
                        if (rf != null && rf.Frame == sessionFrame)
                        {
                            driver.RegistryCountAtFrame = rf.Elements != null ? rf.Elements.Count : 0;
                            if (rf.Elements != null)
                            {
                                for (int e = 0; e < rf.Elements.Count; e++)
                                {
                                    StillWrong.Recording.RegistryEntry el = rf.Elements[e];
                                    if (el != null && !string.IsNullOrEmpty(el.Path) && el.Path.EndsWith("SlotCardBody"))
                                    {
                                        driver.SlotMinX = el.RectMinX;
                                        driver.SlotMinY = el.RectMinY;
                                        driver.SlotMaxX = el.RectMaxX;
                                        driver.SlotMaxY = el.RectMaxY;
                                        driver.SlotRectSpace = el.Space;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

