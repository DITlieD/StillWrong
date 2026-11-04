using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StillWrong.Meta;
using StillWrong.Core.Oracle;
using StillWrong.Editor.Fixtures;
using UnityEditor;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D8SpecState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public double PhaseStartTime;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8SpecResult
    {
        public string RunId = string.Empty;
        public string CaseId = "d8-spec";
        public string UnityVersion = string.Empty;
        public string ModelRoute = string.Empty;
        public bool Accepted;
        public bool UsedDemoDefault;
        public int Attempts;
        public string SpecId = string.Empty;
        public string LiveVerdict = string.Empty;
        public string FailureMessage = string.Empty;
        public List<string> Readings = new List<string>();
        public string Failure = string.Empty;
        public List<string> ToolTrace = new List<string>();
        public List<string> Proposals = new List<string>();
        public string ExceptionType = string.Empty;
        public string ExceptionMessage = string.Empty;
    }
    [Serializable]
    public sealed class D8SpecMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    internal sealed class D8SpecStub : HttpMessageHandler
    {
        public readonly Queue<HttpResponseMessage> Responses = new Queue<HttpResponseMessage>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Responses.Dequeue());
        }
    }
    public static class D8SpecDriver
    {
        private const string SpecFolder = "Library/StillWrong/d8spec";
        private const string TargetPath = "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody";
        private const string Sentence = "Tooltip text is clipped by its container.";
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string SpecDir()
        {
            return Path.Combine(ProjectRoot(), SpecFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(SpecDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(SpecDir(), "result.json");
        }
        public static string StartRun(string mode)
        {
            Directory.CreateDirectory(SpecDir());
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "d8spec-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D8SpecState state = new D8SpecState();
            state.Phase = string.IsNullOrEmpty(mode) ? "demo" : mode;
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D8SpecMarker marker = new D8SpecMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(SpecDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static D8SpecState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D8SpecState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D8SpecState state)
        {
            try
            {
                Directory.CreateDirectory(SpecDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D8SpecState state, string reason, D8SpecResult result)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, result, "failed: " + reason);
            try
            {
                D8SpecMarker marker = new D8SpecMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(SpecDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            D8SpecState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "demo" || state.Phase == "model" || state.Phase == "live")
                {
                    TickRun(state);
                }
            }
            catch
            {
            }
        }
        private static void TickRun(D8SpecState state)
        {
            if (EditorApplication.isPlaying)
            {
                Fail(state, "run-entered-play", null);
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 400.0)
            {
                Fail(state, "run-timeout", null);
                return;
            }
            D8SpecResult result = new D8SpecResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ModelRoute = state.Phase;
            FrozenSessionRequest request = FrozenSessionSheet.BuildRequest(Sentence, "SlotCardBody", TargetPath, 6);
            for (int i = 0; i < request.Measurements.Count; i++)
            {
                ProbeReading reading = request.Measurements[i];
                result.Readings.Add(reading.Probe + "|" + reading.Target + "|" + reading.Frame + "|" + reading.Value);
            }
            if (state.Phase == "demo")
            {
                SheetSpecOutcome outcome = FrozenSessionSheet.AuthorSpec(Sentence, TargetPath, 6);
                result.Accepted = string.IsNullOrEmpty(outcome.Failure);
                result.UsedDemoDefault = outcome.UsedDemoDefault;
                result.Attempts = outcome.Attempts;
                result.FailureMessage = outcome.Failure;
                OracleSpec spec = FixtureSpecs.C1();
                result.SpecId = spec.SpecId;
                result.LiveVerdict = outcome.LiveResult != null ? outcome.LiveResult.Verdict.ToString() : "Inconclusive";
            }
            else if (state.Phase == "live")
            {
                SheetSpecOutcome outcome = FrozenSessionSheet.AuthorSpec(Sentence, TargetPath, 6);
                result.Accepted = string.IsNullOrEmpty(outcome.Failure);
                result.UsedDemoDefault = outcome.UsedDemoDefault;
                result.Attempts = outcome.Attempts;
                result.FailureMessage = outcome.Failure;
                result.SpecId = outcome.Spec != null ? outcome.Spec.SpecId : string.Empty;
                result.LiveVerdict = outcome.LiveResult != null ? outcome.LiveResult.Verdict.ToString() : "Inconclusive";
                if (outcome.ToolTrace != null)
                {
                    for (int i = 0; i < outcome.ToolTrace.Count; i++)
                    {
                        ToolTraceEntry entry = outcome.ToolTrace[i];
                        result.ToolTrace.Add(entry.ToolName + "|" + entry.ArgumentsJson + "|" + entry.ResultText);
                    }
                }
                if (outcome.Proposals != null)
                {
                    for (int i = 0; i < outcome.Proposals.Count; i++)
                    {
                        ProposalRecord proposal = outcome.Proposals[i];
                        result.Proposals.Add(proposal.Accepted + "|" + proposal.EngineNotes + "|" + proposal.DraftJson);
                    }
                }
                result.ExceptionType = outcome.ExceptionType ?? string.Empty;
                result.ExceptionMessage = outcome.ExceptionMessage ?? string.Empty;
            }
            else
            {
                D8SpecStub stub = new D8SpecStub();
                stub.Responses.Enqueue(Final("measured"));
                stub.Responses.Enqueue(Final(C1Flat()));
                SparkClientOptions options = new SparkClientOptions();
                options.ApiKeyProvider = StubKey;
                SparkClient client = new SparkClient((HttpMessageHandler)stub, options, null, null);
                request.AllowModelRoute = true;
                Task<FrozenSessionResult> task = FrozenSessionAuthoring.RunAsync(client, request, FrozenSessionSheet.JudgeLive, CancellationToken.None);
                task.Wait(300000);
                FrozenSessionResult authored = task.Result;
                result.Accepted = authored.Accepted;
                result.UsedDemoDefault = authored.UsedDemoDefault;
                result.Attempts = authored.Attempts;
                result.FailureMessage = authored.FailureMessage;
                if (authored.Accepted)
                {
                    try
                    {
                        OracleSpec spec = new OracleSerializer().Read(FlatDraft.ToOracleSpecJson(authored.SpecJson));
                        result.SpecId = spec.SpecId;
                        OracleResult live = StillWrong.Dispatch.DispatchService.CheckFailureLive(spec);
                        result.LiveVerdict = live != null ? live.Verdict.ToString() : "Inconclusive";
                    }
                    catch (Exception ex)
                    {
                        WriteResult(state, result, "failed: accepted-unreadable: " + ex.GetType().Name);
                        Fail(state, "accepted-unreadable", result);
                        return;
                    }
                }
            }
            bool pass = state.Phase == "demo" ? (result.UsedDemoDefault && result.LiveVerdict == "StillWrong") : (state.Phase == "live" ? (result.Accepted && !result.UsedDemoDefault && result.LiveVerdict == "StillWrong" && result.SpecId != "C1-ui-clip") : (result.Accepted && !result.UsedDemoDefault && result.LiveVerdict == "StillWrong" && result.SpecId != "C1-ui-clip"));
            WriteResult(state, result, pass ? string.Empty : "failed: gate-incomplete");
            if (!pass)
            {
                Fail(state, "gate-incomplete", result);
                return;
            }
            try
            {
                D8SpecMarker marker = new D8SpecMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(SpecDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static string StubKey()
        {
            return "stub-key";
        }
        private static HttpResponseMessage Final(string content)
        {
            string quote = ((char)34).ToString();
            string back = ((char)92).ToString();
            string safe = content.Replace(back, back + back).Replace(quote, back + quote);
            string body = "{" + quote + "choices" + quote + ": [{" + quote + "finish_reason" + quote + ": " + quote + "stop" + quote + ", " + quote + "message" + quote + ": {" + quote + "content" + quote + ": " + quote + safe + quote + ", " + quote + "tool_calls" + quote + ": []}}]}";
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return response;
        }
        private static string C1Flat()
        {
            string q = ((char)34).ToString();
            string n = "null";
            string targets = "[{" + q + "name" + q + ":" + q + "body" + q + "," + q + "elementId" + q + ":" + q + "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody" + q + "},{" + q + "name" + q + ":" + q + "mask" + q + "," + q + "elementId" + q + ":" + q + "BattleCanvas/ContextFlyout/HotbarSlotCard" + q + "}]";
            string fail1 = "{" + q + "probe" + q + ":" + q + "OverflowPx" + q + "," + q + "target" + q + ":" + q + "body" + q + "," + q + "secondTarget" + q + ":" + q + "mask" + q + "," + q + "op" + q + ":" + q + "GreaterThan" + q + "," + q + "numberValue" + q + ":12.0," + q + "boolValue" + q + ":" + n + "," + q + "textValue" + q + ":" + n + "}";
            string fail2 = "{" + q + "probe" + q + ":" + q + "Inside" + q + "," + q + "target" + q + ":" + q + "body" + q + "," + q + "secondTarget" + q + ":" + q + "mask" + q + "," + q + "op" + q + ":" + q + "Equal" + q + "," + q + "numberValue" + q + ":" + n + "," + q + "boolValue" + q + ":false," + q + "textValue" + q + ":" + n + "}";
            string inv1 = "{" + q + "probe" + q + ":" + q + "Active" + q + "," + q + "target" + q + ":" + q + "body" + q + "," + q + "secondTarget" + q + ":" + n + "," + q + "op" + q + ":" + q + "Equal" + q + "," + q + "numberValue" + q + ":" + n + "," + q + "boolValue" + q + ":true," + q + "textValue" + q + ":" + n + "}";
            string inv2 = "{" + q + "probe" + q + ":" + q + "Active" + q + "," + q + "target" + q + ":" + q + "mask" + q + "," + q + "secondTarget" + q + ":" + n + "," + q + "op" + q + ":" + q + "Equal" + q + "," + q + "numberValue" + q + ":" + n + "," + q + "boolValue" + q + ":true," + q + "textValue" + q + ":" + n + "}";
            return "{" + q + "specId" + q + ":" + q + "sheet-c1-clip" + q + "," + q + "developerSentence" + q + ":" + q + "Tooltip text is clipped by its container." + q + "," + q + "targets" + q + ":" + targets + "," + q + "measurementFrames" + q + ":[6]," + q + "failureCombinator" + q + ":" + q + "all" + q + "," + q + "failureClauses" + q + ":[" + fail1 + "," + fail2 + "]," + q + "invariantClauses" + q + ":[" + inv1 + "," + inv2 + "]}";
        }
        private static void WriteResult(D8SpecState state, D8SpecResult result, string failure)
        {
            try
            {
                D8SpecResult dto = result != null ? result : new D8SpecResult();
                dto.RunId = state.RunId;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(SpecDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
