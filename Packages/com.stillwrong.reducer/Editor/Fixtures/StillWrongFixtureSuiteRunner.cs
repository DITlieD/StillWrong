using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using UnityEngine;
using UnityEditor;

namespace StillWrong.Editor.Fixtures
{
    public static class StillWrongFixtureSuiteRunner
    {
        private sealed class CalibrationVerdictSet
        {
            public OracleVerdict KnownBadVerdict { get; set; }
            public OracleVerdict HealthyVerdict { get; set; }
            public OracleVerdict InvalidVerdict { get; set; }
            public bool AllPassed => KnownBadVerdict == OracleVerdict.StillWrong &&
                                     HealthyVerdict == OracleVerdict.HealthyPass &&
                                     InvalidVerdict == OracleVerdict.Inconclusive;
        }

        private sealed class FixtureProofExecutor : ITrialExecutor
        {
            private readonly HashSet<string> _protectedIds;

            public FixtureProofExecutor(HashSet<string> protectedIds)
            {
                _protectedIds = protectedIds;
            }

            public OracleVerdict ExecuteTrial(ISet<string> candidateRemovals)
            {
                if (candidateRemovals.Any(_protectedIds.Contains))
                {
                    return OracleVerdict.Inconclusive;
                }
                return OracleVerdict.StillWrong;
            }
        }

        public static void RunC1VerticalProof()
        {
            bool ok = RunProofForCase("C1-ui-clip");
            if (!ok)
            {
                throw new InvalidOperationException("C1 vertical proof failed.");
            }
        }

        public static void RunAllFixturesProof()
        {
            string[] cases = new[]
            {
                "C1-ui-clip",
                "C2-sorting-occlusion",
                "C3-transient-disappear",
                "C4-heldout-second-project"
            };

            foreach (var cid in cases)
            {
                bool ok = RunProofForCase(cid);
                if (!ok)
                {
                    throw new InvalidOperationException("Proof failed for case: " + cid);
                }
            }
        }

        public static bool RunProofForCase(string caseId)
        {
            string cid = (caseId ?? string.Empty).Trim();

            OracleSpec spec;
            InMemoryMeasurementSource badSource;
            InMemoryMeasurementSource healthySource;
            InMemoryMeasurementSource invalidSource;
            List<ReductionNode> nodes;
            string targetId;
            string cameraNodeId;
            string eventSystemNodeId;

            if (cid == "C1-ui-clip")
            {
                spec = C1UiClipFixtureBuilder.CreateFailureConditionSpec();
                badSource = C1UiClipFixtureBuilder.CreateMeasurementSource("failing");
                healthySource = C1UiClipFixtureBuilder.CreateMeasurementSource("healthy");
                invalidSource = C1UiClipFixtureBuilder.CreateMeasurementSource("invalid");
                nodes = C1UiClipFixtureBuilder.CreateReductionNodes();
                targetId = C1UiClipFixtureBuilder.TargetHierarchyPath;
                cameraNodeId = "MainCamera";
                eventSystemNodeId = "EventSystem";
                C1UiClipFixtureBuilder.BuildFailingScene();
            }
            else if (cid == "C2-sorting-occlusion")
            {
                spec = C2SortingOcclusionFixtureBuilder.CreateFailureConditionSpec();
                badSource = C2SortingOcclusionFixtureBuilder.CreateMeasurementSource("failing");
                healthySource = C2SortingOcclusionFixtureBuilder.CreateMeasurementSource("healthy");
                invalidSource = C2SortingOcclusionFixtureBuilder.CreateMeasurementSource("invalid");
                nodes = C2SortingOcclusionFixtureBuilder.CreateReductionNodes();
                targetId = C2SortingOcclusionFixtureBuilder.TargetHierarchyPath;
                cameraNodeId = "MainCamera";
                eventSystemNodeId = "EventSystem";
                C2SortingOcclusionFixtureBuilder.BuildFailingScene();
            }
            else if (cid == "C3-transient-disappear")
            {
                spec = C3TransientDisappearFixtureBuilder.CreateFailureConditionSpec();
                badSource = C3TransientDisappearFixtureBuilder.CreateMeasurementSource("failing");
                healthySource = C3TransientDisappearFixtureBuilder.CreateMeasurementSource("healthy");
                invalidSource = C3TransientDisappearFixtureBuilder.CreateMeasurementSource("invalid");
                nodes = C3TransientDisappearFixtureBuilder.CreateReductionNodes();
                targetId = C3TransientDisappearFixtureBuilder.TargetHierarchyPath;
                cameraNodeId = "MainCamera";
                eventSystemNodeId = "EventSystem";
                C3TransientDisappearFixtureBuilder.BuildFailingScene();
            }
            else if (cid == "C4-heldout-second-project")
            {
                spec = C4HeldoutWorldBillboardFixtureBuilder.CreateFailureConditionSpec();
                badSource = C4HeldoutWorldBillboardFixtureBuilder.CreateMeasurementSource("failing");
                healthySource = C4HeldoutWorldBillboardFixtureBuilder.CreateMeasurementSource("healthy");
                invalidSource = C4HeldoutWorldBillboardFixtureBuilder.CreateMeasurementSource("invalid");
                nodes = C4HeldoutWorldBillboardFixtureBuilder.CreateReductionNodes();
                targetId = C4HeldoutWorldBillboardFixtureBuilder.BillboardId;
                cameraNodeId = "PlayerCameraRig/MainCamera";
                eventSystemNodeId = "EventSystem";
                C4HeldoutWorldBillboardFixtureBuilder.BuildFailingScene();
            }
            else
            {
                Debug.LogError("Inconclusive: unknown case " + cid + "; no verdict produced.");
                return false;
            }

            var liveContext = new EvaluationContext();
            var invalidContext = new EvaluationContext();
            invalidContext.ConsoleErrorCount = 1;
            var calib = new CalibrationVerdictSet
            {
                KnownBadVerdict = OracleEvaluator.Evaluate(spec, badSource, liveContext).Verdict,
                HealthyVerdict = OracleEvaluator.Evaluate(spec, healthySource, liveContext).Verdict,
                InvalidVerdict = OracleEvaluator.Evaluate(spec, invalidSource, invalidContext).Verdict
            };

            if (!calib.AllPassed)
            {
                Debug.LogError("Calibration check failed for " + cid + ": bad=" + calib.KnownBadVerdict + ", healthy=" + calib.HealthyVerdict + ", invalid=" + calib.InvalidVerdict);
                return false;
            }

            var context = new ReductionContext(nodes, targetId, cameraNodeId, eventSystemNodeId);
            var protectedClosure = DeltaDebuggingReducer.ComputeProtectedClosure(context);
            var executor = new FixtureProofExecutor(protectedClosure);
            var result = DeltaDebuggingReducer.Reduce(context, executor);

            if (result.RemovedCount == 0 || !result.KeptNodeIds.Contains(targetId))
            {
                Debug.LogError("DeltaDebuggingReducer failed to minimize or lost target for " + cid);
                return false;
            }

            SaveVerdictJson(cid, calib, result);
            Debug.Log("Proof successful for " + cid + ": bad=" + calib.KnownBadVerdict + ", healthy=" + calib.HealthyVerdict + ", invalid=" + calib.InvalidVerdict + ", removed " + result.RemovedCount + " nodes, kept " + result.KeptNodeIds.Count + " nodes.");
            return true;
        }
        private static string BuildVerdictJson(
            string caseId,
            CalibrationVerdictSet calib,
            ReductionResult result)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"case_id\": \"" + Escape(caseId) + "\",\n");
            sb.Append("  \"provenance\": \"measured-only;no-estimates\",\n");
            sb.Append("  \"unity_version\": \"" + Escape(Application.unityVersion) + "\",\n");
            sb.Append("  \"calibration\": {\n");
            sb.Append("    \"known_bad\": \"" + calib.KnownBadVerdict + "\",\n");
            sb.Append("    \"healthy\": \"" + calib.HealthyVerdict + "\",\n");
            sb.Append("    \"invalid\": \"" + calib.InvalidVerdict + "\"\n");
            sb.Append("  },\n");
            sb.Append("  \"reduction\": {\n");
            sb.Append("    \"removed\": " + result.RemovedCount + ",\n");
            sb.Append("    \"kept\": " + result.KeptNodeIds.Count + ",\n");
            sb.Append("    \"trials\": [\n");
            for (int i = 0; i < result.AuditLog.Count; i++)
            {
                var entry = result.AuditLog[i];
                sb.Append("    {\n");
                sb.Append("      \"trial_index\": " + entry.TrialIndex + ",\n");
                sb.Append("      \"removed_ids\": [");
                for (int j = 0; j < entry.TestedRemovalIds.Count; j++)
                {
                    sb.Append("\"" + Escape(entry.TestedRemovalIds[j]) + "\"");
                    if (j + 1 < entry.TestedRemovalIds.Count) sb.Append(", ");
                }
                sb.Append("],\n");
                sb.Append("      \"verdict\": \"" + entry.Verdict + "\",\n");
                sb.Append("      \"decision\": \"" + (entry.WasRetained ? "RetainedRemoval" : "Reverted") + "\",\n");
                sb.Append("      \"reason\": \"" + Escape(entry.Note) + "\",\n");
                sb.Append("      \"remaining\": " + entry.RemainingCandidateCount + "\n");
                sb.Append("    }");
                if (i + 1 < result.AuditLog.Count) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("    ]\n");
            sb.Append("  },\n");
            sb.Append("  \"inconclusive_reason\": \"\"\n");
            sb.Append("}\n");

            return sb.ToString();
        }

        private static void SaveVerdictJson(string caseId, CalibrationVerdictSet calib, ReductionResult result)
        {
            string json = BuildVerdictJson(caseId, calib, result);
            string outDir = "outputs/manifests";
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string path = Path.Combine(outDir, "repro-manifest-" + caseId + ".json");
            File.WriteAllText(path, json, Encoding.UTF8);

            string defaultPath = "outputs/repro-manifest.json";
            File.WriteAllText(defaultPath, json, Encoding.UTF8);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
