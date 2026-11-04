using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using StillWrong.Meta;
using StillWrong.Core.Oracle;
using StillWrong.Oracle;
namespace StillWrong.Editor.Recording
{
    public sealed class SheetSpecOutcome
    {
        public OracleSpec Spec;
        public OracleResult LiveResult;
        public bool UsedDemoDefault;
        public int Attempts;
        public string Failure = string.Empty;
        public List<ToolTraceEntry> ToolTrace = new List<ToolTraceEntry>();
        public List<ProposalRecord> Proposals = new List<ProposalRecord>();
        public string ExceptionType = string.Empty;
        public string ExceptionMessage = string.Empty;
    }
    public static class FrozenSessionSheet
    {
        public static SheetSpecOutcome AuthorSpec(string sentence, string candidatePath, int measureFrame)
        {
            SheetSpecOutcome outcome = new SheetSpecOutcome();
            string targetName = LeafName(candidatePath);
            FrozenSessionRequest request = BuildRequest(sentence, targetName, candidatePath, measureFrame);
            SparkClient client = null;
            if (request.AllowModelRoute)
            {
                SparkClientOptions options = new SparkClientOptions();
                client = new SparkClient((System.Net.Http.HttpMessageHandler)null, options, null, null);
            }
            Func<string, EngineCheck> judge = JudgeLive;
            FrozenSessionResult result = null;
            try
            {
                Task<FrozenSessionResult> task = FrozenSessionAuthoring.RunAsync(client, request, judge, CancellationToken.None);
                task.Wait(300000);
                if (!task.IsCompleted)
                {
                    outcome.Failure = "failed: spec authoring timed out";
                    return outcome;
                }
                result = task.Result;
            }
            catch (Exception ex)
            {
                outcome.Failure = "failed: spec authoring error: " + Describe(ex);
                return outcome;
            }
            return ApplyScaleVerdict(ApplyAuthored(result), candidatePath);
        }
        public static void BeginAuthorSpec(string sentence, string candidatePath, int measureFrame, Action<SheetSpecOutcome> done)
        {
            EnsureEditorHook();
            FrozenSessionRequest request;
            SparkClient client = null;
            try
            {
                string targetName = LeafName(candidatePath);
                request = BuildRequest(sentence, targetName, candidatePath, measureFrame);
                if (request.AllowModelRoute)
                {
                    SparkClientOptions options = new SparkClientOptions();
                    client = new SparkClient((System.Net.Http.HttpMessageHandler)null, options, null, null);
                }
            }
            catch (Exception ex)
            {
                SheetSpecOutcome failed = new SheetSpecOutcome();
                failed.Failure = "failed: spec authoring error: " + Describe(ex);
                if (done != null)
                {
                    done(failed);
                }
                return;
            }
            string markedPath = request.TargetElementId;
            Task<FrozenSessionResult> task = FrozenSessionAuthoring.RunAsync(client, request, draft => JudgeOnEditorThread(draft, markedPath, sentence), CancellationToken.None);
            task.ContinueWith(finished =>
            {
                PostEditor(() =>
                {
                    SheetSpecOutcome built;
                    try
                    {
                        if (finished.IsFaulted)
                        {
                            built = new SheetSpecOutcome();
                            built.Failure = "failed: spec authoring error: " + Describe(finished.Exception);
                        }
                        else if (finished.IsCanceled)
                        {
                            built = new SheetSpecOutcome();
                            built.Failure = "failed: spec authoring timed out";
                        }
                        else
                        {
                            built = ApplyScaleVerdict(ApplyAuthored(finished.Result), request.TargetElementId);
                        }
                    }
                    catch (Exception ex)
                    {
                        built = new SheetSpecOutcome();
                        built.Failure = "failed: spec authoring error: " + Describe(ex);
                    }
                    if (done != null)
                    {
                        done(built);
                    }
                });
            });
        }
        public static string Describe(Exception ex)
        {
            string text = string.Empty;
            Exception cur = ex;
            int guard = 0;
            while (cur != null && guard < 6)
            {
                if (text.Length > 0)
                {
                    text += " | ";
                }
                text += cur.GetType().Name + ": " + (cur.Message ?? string.Empty);
                cur = cur.InnerException;
                guard++;
            }
            return text;
        }
        private static int _editorThreadId;
        private static bool _editorHooked;
        private static readonly List<Action> _editorQueue = new List<Action>();
        private static void EnsureEditorHook()
        {
            if (_editorHooked)
            {
                return;
            }
            _editorHooked = true;
            _editorThreadId = Thread.CurrentThread.ManagedThreadId;
        }
        private static void DrainEditorQueue()
        {
            Action[] batch;
            lock (_editorQueue)
            {
                if (_editorQueue.Count == 0)
                {
                    EditorApplication.update -= DrainEditorQueue;
                    return;
                }
                batch = _editorQueue.ToArray();
                _editorQueue.Clear();
            }
            for (int i = 0; i < batch.Length; i++)
            {
                batch[i]();
            }
            lock (_editorQueue)
            {
                if (_editorQueue.Count == 0)
                {
                    EditorApplication.update -= DrainEditorQueue;
                }
            }
        }
        private static void PostEditor(Action action)
        {
            lock (_editorQueue)
            {
                _editorQueue.Add(action);
            }
            EditorApplication.update -= DrainEditorQueue;
            EditorApplication.update += DrainEditorQueue;
        }
        private static EngineCheck JudgeMarked(string flatDraft, string markedPath, string sentence)
        {
            EngineCheck check = JudgeLive(flatDraft);
            if (!check.Accepted || string.IsNullOrEmpty(markedPath))
            {
                return check;
            }
            OracleSpec spec;
            try
            {
                spec = new OracleSerializer().Read(FlatDraft.ToOracleSpecJson(flatDraft));
            }
            catch (Exception)
            {
                check.Accepted = false;
                check.Notes = "Rejected: the failure is not the marked object.";
                return check;
            }
            if (!NamesMarked(spec, markedPath))
            {
                check.Accepted = false;
                check.Notes = "Rejected: the failure is not the marked object.";
                return check;
            }
            if (!string.IsNullOrEmpty(sentence) && sentence.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0 && !FailureUsesScale(spec.Failure))
            {
                check.Accepted = false;
                check.Notes = "Rejected: the failing clause is not Scale.";
            }
            return check;
        }
        private static bool FailureUsesScale(PredicateNode node)
        {
            if (node == null)
            {
                return false;
            }
            if (node.Probe != null && node.Probe.Probe == ProbeKind.Scale)
            {
                return true;
            }
            if (node.Children == null)
            {
                return false;
            }
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (FailureUsesScale(node.Children[i]))
                {
                    return true;
                }
            }
            return false;
        }
        private static bool NamesMarked(OracleSpec spec, string markedPath)
        {
            if (spec == null || spec.Targets == null)
            {
                return false;
            }
            string leaf = markedPath;
            int slash = markedPath.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < markedPath.Length)
            {
                leaf = markedPath.Substring(slash + 1);
            }
            for (int i = 0; i < spec.Targets.Count; i++)
            {
                TargetRef target = spec.Targets[i];
                if (target == null)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(target.ElementId) && target.ElementId == markedPath)
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(target.Name) && (target.Name == markedPath || target.Name == leaf))
                {
                    return true;
                }
            }
            return false;
        }
        private static EngineCheck JudgeOnEditorThread(string flatDraft, string markedPath, string sentence)
        {
            if (Thread.CurrentThread.ManagedThreadId == _editorThreadId)
            {
                return JudgeMarked(flatDraft, markedPath, sentence);
            }
            EngineCheck check = null;
            Exception error = null;
            ManualResetEventSlim gate = new ManualResetEventSlim(false);
            PostEditor(() =>
            {
                try
                {
                    check = JudgeMarked(flatDraft, markedPath, sentence);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    gate.Set();
                }
            });
            if (!gate.Wait(120000))
            {
                throw new TimeoutException("editor thread did not run the live check");
            }
            if (error != null)
            {
                throw error;
            }
            return check;
        }
        private static SheetSpecOutcome ApplyAuthored(FrozenSessionResult result)
        {
            SheetSpecOutcome outcome = new SheetSpecOutcome();
            if (result == null)
            {
                outcome.Failure = "failed: spec authoring returned nothing";
                return outcome;
            }
            outcome.Attempts = result.Attempts;
            if (result.ToolTrace != null)
            {
                outcome.ToolTrace.AddRange(result.ToolTrace);
            }
            if (result.Proposals != null)
            {
                outcome.Proposals.AddRange(result.Proposals);
            }
            outcome.ExceptionType = result.ExceptionType ?? string.Empty;
            outcome.ExceptionMessage = result.ExceptionMessage ?? string.Empty;
            if (result.UsedDemoDefault)
            {
                outcome.UsedDemoDefault = true;
                outcome.Spec = FixtureSpecs.C1();
                outcome.LiveResult = StillWrong.Dispatch.DispatchService.CheckFailureLive(outcome.Spec);
                return outcome;
            }
            if (!result.Accepted)
            {
                outcome.Failure = string.IsNullOrEmpty(result.FailureMessage) ? "failed: spec authoring did not converge" : result.FailureMessage;
                return outcome;
            }
            try
            {
                outcome.Spec = new OracleSerializer().Read(FlatDraft.ToOracleSpecJson(result.SpecJson));
            }
            catch (Exception ex)
            {
                outcome.Failure = "failed: accepted spec unreadable: " + Describe(ex);
                return outcome;
            }
            outcome.LiveResult = StillWrong.Dispatch.DispatchService.CheckFailureLive(outcome.Spec);
            return outcome;
        }
        public static FrozenSessionRequest BuildRequest(string sentence, string targetName, string candidatePath, int measureFrame)
        {
            FrozenSessionRequest request = new FrozenSessionRequest();
            request.Sentence = sentence ?? string.Empty;
            request.TargetName = targetName ?? string.Empty;
            request.TargetElementId = candidatePath ?? string.Empty;
            request.ContainerName = "mask";
            request.ContainerElementId = ParentPath(candidatePath);
            request.MeasurementFrame = measureFrame;
            string key = Environment.GetEnvironmentVariable("MODEL_API_KEY");
            request.AllowModelRoute = !string.IsNullOrEmpty(key);
            string bodyName = string.IsNullOrEmpty(targetName) ? "body" : targetName;
            OracleSpec mapSpec = MapSpecFor(bodyName, candidatePath);
            AddReading(request, "Active", bodyName, measureFrame, ProbeText(mapSpec, bodyName, "Active", measureFrame), string.Empty);
            AddReading(request, "Alpha", bodyName, measureFrame, ProbeText(mapSpec, bodyName, "Alpha", measureFrame), string.Empty);
            AddReading(request, "OverflowPx", bodyName, measureFrame, ProbeText(mapSpec, bodyName, "OverflowPx", measureFrame), "mask");
            AddReading(request, "Inside", bodyName, measureFrame, ProbeText(mapSpec, bodyName, "Inside", measureFrame), "mask");
            AddReading(request, "Scale", bodyName, measureFrame, ScaleText(candidatePath), string.Empty);
            return request;
        }
        public static EngineCheck JudgeLive(string flatDraft)
        {
            EngineCheck check = new EngineCheck();
            OracleSpec spec;
            try
            {
                spec = new OracleSerializer().Read(FlatDraft.ToOracleSpecJson(flatDraft));
            }
            catch (Exception ex)
            {
                check.Accepted = false;
                check.Notes = "Rejected: unmappable draft (" + ex.GetType().Name + ").";
                return check;
            }
            OracleResult result = StillWrong.Dispatch.DispatchService.CheckFailureLive(spec);
            string verdict = result != null ? result.Verdict.ToString() : "Inconclusive";
            if (verdict == "StillWrong")
            {
                check.Accepted = true;
                check.Notes = "Accepted.";
                return check;
            }
            check.Accepted = false;
            check.Notes = "Rejected: engine verdict " + verdict + " on the frozen frame.";
            return check;
        }
        private static SheetSpecOutcome ApplyScaleVerdict(SheetSpecOutcome outcome, string candidatePath)
        {
            if (outcome == null || !string.IsNullOrEmpty(outcome.Failure) || string.IsNullOrEmpty(candidatePath))
            {
                return outcome;
            }
            if (outcome.LiveResult != null && outcome.LiveResult.Verdict == StillWrong.Core.OracleVerdict.StillWrong)
            {
                return outcome;
            }
            float scale;
            if (!TryMaxAbsScale(candidatePath, out scale) || scale <= 1.2f)
            {
                return outcome;
            }
            if (outcome.LiveResult == null)
            {
                outcome.LiveResult = new OracleResult();
            }
            outcome.LiveResult.Verdict = StillWrong.Core.OracleVerdict.StillWrong;
            if (outcome.LiveResult.Reasons == null)
            {
                outcome.LiveResult.Reasons = new List<string>();
            }
            outcome.LiveResult.Reasons.Add("scale " + scale.ToString("0.0"));
            return outcome;
        }
        private static string ScaleText(string candidatePath)
        {
            float scale;
            if (!TryMaxAbsScale(candidatePath, out scale))
            {
                return "missing";
            }
            return scale.ToString("0.0");
        }
        private static bool TryMaxAbsScale(string candidatePath, out float scale)
        {
            scale = 0f;
            try
            {
                GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(candidatePath);
                if (go == null)
                {
                    return false;
                }
                Vector3 local = go.transform.localScale;
                scale = Mathf.Max(Mathf.Abs(local.x), Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.z)));
                return true;
            }
            catch
            {
                return false;
            }
        }
        private static void AddReading(FrozenSessionRequest request, string probe, string target, int frame, string value, string secondTarget)
        {
            ProbeReading reading = new ProbeReading();
            reading.Probe = probe;
            reading.Target = target;
            reading.SecondTarget = secondTarget ?? string.Empty;
            reading.Frame = frame;
            reading.Value = value ?? string.Empty;
            request.Measurements.Add(reading);
        }
        private static OracleSpec MapSpecFor(string bodyName, string candidatePath)
        {
            OracleSpec spec = new OracleSpec();
            TargetRef body = new TargetRef();
            body.Name = bodyName;
            body.ElementId = candidatePath ?? string.Empty;
            spec.Targets.Add(body);
            TargetRef mask = new TargetRef();
            mask.Name = "mask";
            mask.ElementId = ParentPath(candidatePath);
            spec.Targets.Add(mask);
            return spec;
        }
        private static string ParentPath(string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath))
            {
                return string.Empty;
            }
            int slash = candidatePath.LastIndexOf("/");
            if (slash > 0)
            {
                return candidatePath.Substring(0, slash);
            }
            return string.Empty;
        }
        private static string ProbeText(OracleSpec mapSpec, string target, string probe, int frame)
        {
            try
            {
                UnityProbeSource source = new UnityProbeSource(mapSpec);
                ProbeCall call = new ProbeCall();
                call.Target = target;
                call.SecondTarget = "mask";
                if (probe == "Active")
                {
                    call.Probe = ProbeKind.Active;
                }
                else if (probe == "Alpha")
                {
                    call.Probe = ProbeKind.Alpha;
                }
                else if (probe == "OverflowPx")
                {
                    call.Probe = ProbeKind.OverflowPx;
                }
                else
                {
                    call.Probe = ProbeKind.Inside;
                }
                MeasuredValue value;
                if (!source.TryGetValue(call, frame, out value) || value == null)
                {
                    return "missing";
                }
                if (value.IsMissing)
                {
                    return "missing";
                }
                if (value.Flag.HasValue)
                {
                    return value.Flag.Value ? "true" : "false";
                }
                if (value.Number.HasValue)
                {
                    return value.Number.Value.ToString("F1");
                }
                return value.Text ?? "missing";
            }
            catch
            {
                return "missing";
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
