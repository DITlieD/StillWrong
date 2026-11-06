using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Editor.Reduction;
using StillWrong.Oracle;
using StillWrong.Replay;
using StillWrong.Recording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace StillWrong.Editor.Export
{
    [Serializable]
    public sealed class ProjectComplexitySummaryDto
    {
        public int object_count;
        public int component_count;
        public long asset_byte_count;
        public int input_step_count;
        public string summary_provenance = "";
    }

    [Serializable]
    public sealed class SessionQualifiedIdDto
    {
        public string asset_guid;
        public long local_file_id;
        public string prefab_asset_path;
        public string hierarchy_path;
        public string component_type;
        public int component_index;
        public string grounding_provenance = "not-recorded";
    }

    [Serializable]
    public sealed class ExportGroundingCandidateDto
    {
        public string hierarchy_path = string.Empty;
        public float mask_coverage;
        public float element_coverage;
    }
    [Serializable]
    public sealed class ResetMethodDto
    {
        public string type;
        public string action;
    }

    [Serializable]
    public sealed class ViewportDto
    {
        public int width;
        public int height;
        public string aspect_ratio;
        public float scale;
        public int capture_width;
        public int capture_height;
    }

    [Serializable]
    public sealed class InputEventPositionDto
    {
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class InputEventRecordDto
    {
        public int frame;
        public string action;
        public InputEventPositionDto position;
        public int button;
        public string key_code;
        public bool is_down;
        public int slot_index;
        public string text;
        public float target_height_reduction;
        public int wait_frames;
    }

    [Serializable]
    public sealed class ReplayRecipeDto
    {
        public ResetMethodDto reset_method;
        public int seed;
        public ViewportDto viewport;
        public List<InputEventRecordDto> input_events = new List<InputEventRecordDto>();
        public string recipe_provenance = "not-recorded";
    }

    [Serializable]
    public sealed class ReductionAuditEntryDto
    {
        public int trial_index;
        public string candidate_group_id;
        public List<string> candidate_objects = new List<string>();
        public string verdict;
        public string decision;
        public string reason;
        public int remaining_objects_count;
        public long trial_duration_ms;
    }

    [Serializable]
    public sealed class FreshProcessVerificationDto
    {
        public string command;
        public int exit_status;
        public string oracle_verdict;
        public string healthy_control_verdict;
        public string invalid_scenario_verdict;
        public string log_path;
        public string verification_timestamp;
    }

    [Serializable]
    public sealed class ReproManifestDto
    {
        public string bundle_version = "0.1.0";
        public string case_id;
        public string unity_version;
        public string render_pipeline;
        public ProjectComplexitySummaryDto source_project_summary;
        public ProjectComplexitySummaryDto reduced_project_summary;
        public SessionQualifiedIdDto target_identity;
        public ReplayRecipeDto replay_recipe;
        public List<ReductionAuditEntryDto> reduction_audit_log = new List<ReductionAuditEntryDto>();
        public FreshProcessVerificationDto fresh_process_verification;
        public string bundle_scene_path = "";
        public string oracle_spec_json = "";
        public string exporter_provenance = "measured-only";
        public string runtime_bundle_provenance = "";
        public List<ExportGroundingCandidateDto> grounding_candidates = new List<ExportGroundingCandidateDto>();
    }
    public sealed class ExportGroundingReport
    {
        public float BestMaskCoverage = -1f;
        public float BestElementCoverage = -1f;
        public string Provenance = "not-recorded";
        public List<ExportGroundingCandidate> Candidates = new List<ExportGroundingCandidate>();
    }
    public sealed class ExportGroundingCandidate
    {
        public string ElementPath = string.Empty;
        public float MaskCoverage = -1f;
        public float ElementCoverage = -1f;
    }

    public static class UnityReproBundleExporter
    {
        public static string ExportBundle(
            string reducedScenePath,
            string caseId,
            OracleSpec spec,
            ReductionResult reductionResult,
            string bundleOutputDirectory,
            ProjectComplexitySummaryDto sourceSummary = null,
            ExportGroundingReport grounding = null,
            string sessionDir = null,
            List<C1TrialLogEntry> trialLog = null)
        {
            if (string.IsNullOrEmpty(reducedScenePath) || !File.Exists(reducedScenePath))
            {
                throw new FileNotFoundException("Reduced scene file not found: " + reducedScenePath);
            }

            if (!Directory.Exists(bundleOutputDirectory))
            {
                Directory.CreateDirectory(bundleOutputDirectory);
            }

            string assetsDir = Path.Combine(bundleOutputDirectory, "Assets");
            string bundleScenesDir = Path.Combine(assetsDir, "Scenes");
            Directory.CreateDirectory(bundleScenesDir);

            string exportedSceneName = Path.GetFileName(reducedScenePath);
            string exportedScenePath = Path.Combine(bundleScenesDir, exportedSceneName);
            CopySingleFile(reducedScenePath, exportedScenePath);
            string[] dependencies = AssetDatabase.GetDependencies(reducedScenePath, true);
            CopyDependencyClosure(dependencies, bundleOutputDirectory);
            CopyProjectSettings(bundleOutputDirectory);
            CopyPackagesManifest(bundleOutputDirectory);
            CopySessionFolder(sessionDir, bundleOutputDirectory);
            string runtimeProvenance = CopyStillWrongRuntime(bundleOutputDirectory);

            long totalAssetBytes = 0;
            for (int i = 0; i < dependencies.Length; i++)
            {
                string depPath = dependencies[i];
                if (File.Exists(depPath))
                {
                    var fileInfo = new FileInfo(depPath);
                    totalAssetBytes += fileInfo.Length;
                }
            }

            Scene openedScene = EditorSceneManager.OpenScene(reducedScenePath, OpenSceneMode.Single);
            var sceneObjects = UnityEngine.Object.FindObjectsByType<GameObject>();
            var sceneComponents = UnityEngine.Object.FindObjectsByType<Component>();
            int auditCount = reductionResult != null && reductionResult.AuditLog != null ? reductionResult.AuditLog.Count : 0;
            List<InputEventRecordDto> earlyInputs = BuildReplayInputs(sessionDir);
            int earlySessionCount = string.IsNullOrEmpty(sessionDir) ? -1 : earlyInputs.Count;
            int stepCount = earlySessionCount >= 0 ? earlySessionCount : (auditCount > 0 ? auditCount : -1);
            string stepSource = earlySessionCount >= 0 ? "session" : (auditCount > 0 ? "audit" : "not-recorded");

            var reducedSummary = new ProjectComplexitySummaryDto
            {
                object_count = sceneObjects.Length,
                component_count = sceneComponents.Length,
                asset_byte_count = totalAssetBytes,
                input_step_count = stepCount,
                summary_provenance = "measured:reduced-scene;input-steps=" + stepSource
            };

            if (sourceSummary == null)
            {
                sourceSummary = new ProjectComplexitySummaryDto
                {
                    object_count = reductionResult != null ? reductionResult.InitialCandidateCount + (reductionResult.KeptNodeIds != null ? reductionResult.KeptNodeIds.Count : 0) : -1,
                    component_count = -1,
                    asset_byte_count = -1,
                    input_step_count = stepCount,
                    summary_provenance = (reductionResult != null ? "measured:reduction-initial-count;components-and-bytes-not-recorded" : "not-recorded:source-scene-unavailable") + ";input-steps=" + stepSource
                };
            }

            string renderPipelineName = GraphicsSettings.currentRenderPipeline != null ?
                GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in Render Pipeline";

            string targetGuid = AssetDatabase.AssetPathToGUID(reducedScenePath);
            long localId = 0;
            string specTargetPath = spec != null && spec.Targets.Count > 0 ? spec.Targets[0].ElementId : "";
            string targetComponentType = "";
            GameObject targetGo = GameObject.Find(specTargetPath);
            if (targetGo != null)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(targetGo, out string _, out long id))
                {
                    localId = id;
                }
                targetComponentType = DescribeTargetComponent(targetGo);
            }
            int ringW = -1;
            int ringH = -1;
            bool haveRing = TryGetSessionCaptureSize(sessionDir, out ringW, out ringH);
            int markedW = -1;
            int markedH = -1;
            bool haveMarked = TryGetMarkedSize(sessionDir, out markedW, out markedH);
            List<InputEventRecordDto> replayInputs = BuildReplayInputs(sessionDir);
            int sessionInputCount = string.IsNullOrEmpty(sessionDir) ? -1 : replayInputs.Count;


            var manifest = new ReproManifestDto
            {
                bundle_version = "0.1.0",
                case_id = !string.IsNullOrEmpty(caseId) ? caseId : (spec != null ? spec.SpecId : "C1-ui-clip"),
                unity_version = Application.unityVersion,
                render_pipeline = renderPipelineName,
                source_project_summary = sourceSummary,
                reduced_project_summary = reducedSummary,
                target_identity = new SessionQualifiedIdDto
                {
                    asset_guid = targetGuid,
                    local_file_id = localId,
                    prefab_asset_path = "",
                    hierarchy_path = specTargetPath,
                    component_type = targetComponentType,
                    component_index = 0,
                    grounding_provenance = grounding != null ? grounding.Provenance : "not-recorded"
                },
                replay_recipe = new ReplayRecipeDto
                {
                    reset_method = new ResetMethodDto
                    {
                        type = "script_and_scene_reset",
                        action = "Reload fixture scene or invoke reset"
                    },
                    seed = 0,
                    viewport = new ViewportDto
                    {
                        width = markedW,
                        height = markedH,
                        aspect_ratio = haveMarked && markedH > 0 ? (markedW / (float)markedH).ToString("F2") : "",
                        scale = 1.0f,
                        capture_width = ringW,
                        capture_height = ringH
                    },
                    input_events = replayInputs,
                    recipe_provenance = haveMarked && haveRing && sessionInputCount >= 0 ? "measured:marked-frame-and-ring-capture-and-session-input-events;seed-not-recorded" : "not-recorded"
                },
                fresh_process_verification = new FreshProcessVerificationDto
                {
                    command = "unity -batchmode -projectPath <bundle> -executeMethod StillWrong.Batch.Verify.Run -manifest <manifest>",
                    exit_status = -1,
                    oracle_verdict = "",
                    healthy_control_verdict = "",
                    invalid_scenario_verdict = "",
                    log_path = "",
                    verification_timestamp = DateTime.UtcNow.ToString("o")
                },
                bundle_scene_path = "Assets/Scenes/" + exportedSceneName,
                oracle_spec_json = WriteSpecJson(spec),
                grounding_candidates = BuildGroundingCandidates(grounding),
                exporter_provenance = "measured-only",
                runtime_bundle_provenance = runtimeProvenance,
            };

            if (reductionResult != null && reductionResult.AuditLog != null)
            {
                for (int i = 0; i < reductionResult.AuditLog.Count; i++)
                {
                    var entry = reductionResult.AuditLog[i];
                    manifest.reduction_audit_log.Add(new ReductionAuditEntryDto
                    {
                        trial_index = entry.TrialIndex,
                        candidate_group_id = "group_" + entry.TrialIndex,
                        candidate_objects = new List<string>(entry.TestedRemovalIds),
                        verdict = entry.Verdict.ToString(),
                        decision = entry.WasRetained ? "RetainedRemoval" : "Reverted",
                        reason = entry.Note,
                        remaining_objects_count = entry.RemainingCandidateCount,
                        trial_duration_ms = MatchTrialDurationMs(entry.TrialIndex, trialLog)
                    });
                }
            }

            string manifestJson = JsonUtility.ToJson(manifest, true);
            string manifestPath = Path.Combine(bundleOutputDirectory, "repro-manifest.json");
            File.WriteAllText(manifestPath, manifestJson);

            return manifestPath;
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string DescribeTargetComponent(GameObject targetGo)
        {
            if (targetGo == null)
            {
                return "";
            }
            Graphic graphic = targetGo.GetComponent<Graphic>();
            if (graphic != null)
            {
                return graphic.GetType().FullName;
            }
            Renderer renderer = targetGo.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.GetType().FullName;
            }
            UnityEngine.UIElements.UIDocument document = targetGo.GetComponent<UnityEngine.UIElements.UIDocument>();
            if (document != null)
            {
                return document.GetType().FullName;
            }
            return "";
        }

        private static string WriteSpecJson(OracleSpec spec)
        {
            if (spec == null)
            {
                return "";
            }
            try
            {
                return new OracleSerializer().Write(spec);
            }
            catch
            {
                return "";
            }
        }

        private static bool TryGetSessionCaptureSize(string sessionDir, out int frameW, out int frameH)
        {
            frameW = -1;
            frameH = -1;
            if (string.IsNullOrEmpty(sessionDir) || !Directory.Exists(sessionDir))
            {
                return false;
            }
            try
            {
                SessionBundle bundle = SessionBundle.Load(sessionDir);
                if (bundle == null || bundle.Manifest == null)
                {
                    return false;
                }
                if (bundle.Manifest.FrameWidth <= 0 || bundle.Manifest.FrameHeight <= 0)
                {
                    return false;
                }
                frameW = bundle.Manifest.FrameWidth;
                frameH = bundle.Manifest.FrameHeight;
                return true;
            }
            catch
            {
                return false;
            }
        }
        private static bool TryGetMarkedSize(string sessionDir, out int markedW, out int markedH)
        {
            markedW = -1;
            markedH = -1;
            if (string.IsNullOrEmpty(sessionDir))
            {
                return false;
            }
            try
            {
                string path = Path.Combine(sessionDir, "marked.png");
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                {
                    return false;
                }
                Texture2D tex = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return false;
                }
                markedW = tex.width;
                markedH = tex.height;
                UnityEngine.Object.DestroyImmediate(tex);
                return markedW > 0 && markedH > 0;
            }
            catch
            {
                return false;
            }
        }
        private static List<ExportGroundingCandidateDto> BuildGroundingCandidates(ExportGroundingReport grounding)
        {
            List<ExportGroundingCandidateDto> dtos = new List<ExportGroundingCandidateDto>();
            if (grounding == null || grounding.Candidates == null)
            {
                return dtos;
            }
            for (int i = 0; i < grounding.Candidates.Count; i++)
            {
                ExportGroundingCandidate candidate = grounding.Candidates[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.ElementPath))
                {
                    continue;
                }
                ExportGroundingCandidateDto dto = new ExportGroundingCandidateDto();
                dto.hierarchy_path = candidate.ElementPath;
                dto.mask_coverage = candidate.MaskCoverage;
                dto.element_coverage = candidate.ElementCoverage;
                dtos.Add(dto);
            }
            return dtos;
        }
        private static List<InputEventRecordDto> BuildReplayInputs(string sessionDir)
        {
            List<InputEventRecordDto> inputs = new List<InputEventRecordDto>();
            if (string.IsNullOrEmpty(sessionDir) || !Directory.Exists(sessionDir))
            {
                return inputs;
            }
            SessionBundle bundle = null;
            try
            {
                bundle = SessionBundle.Load(sessionDir);
            }
            catch
            {
                return inputs;
            }
            if (bundle == null || bundle.Inputs == null || bundle.Inputs.Events == null)
            {
                return inputs;
            }
            int cap = Math.Min(bundle.Inputs.Events.Count, 512);
            for (int i = 0; i < cap; i++)
            {
                InputEventRecord rec = bundle.Inputs.Events[i];
                if (rec == null)
                {
                    continue;
                }
                InputEventRecordDto dto = new InputEventRecordDto();
                dto.frame = rec.Frame;
                dto.action = string.IsNullOrEmpty(rec.Path) ? "" : rec.Path;
                dto.position = new InputEventPositionDto();
                dto.position.x = rec.PointerX;
                dto.position.y = rec.PointerY;
                dto.button = rec.ButtonDownMask | rec.ButtonUpMask;
                dto.is_down = rec.ButtonDownMask != 0;
                inputs.Add(dto);
            }
            return inputs;
        }

        private static long MatchTrialDurationMs(int trialIndex, List<C1TrialLogEntry> trialLog)
        {
            if (trialLog == null)
            {
                return -1;
            }
            for (int i = 0; i < trialLog.Count; i++)
            {
                if (trialLog[i] != null && trialLog[i].TrialIndex == trialIndex)
                {
                    return trialLog[i].DurationMs;
                }
            }
            return -1;
        }

        private static string Relativize(string root, string fullPath)
        {
            string rooted = fullPath;
            if (!Path.IsPathRooted(rooted))
            {
                rooted = Path.GetFullPath(Path.Combine(root, rooted));
            }
            else
            {
                rooted = Path.GetFullPath(rooted);
            }
            string normRoot = Path.GetFullPath(root);
            if (rooted.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
            {
                string rel = rooted.Substring(normRoot.Length);
                return rel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return Path.GetFileName(rooted);
        }

        private static string FindEnclosingAsmdefDir(string startDir, string projectRoot)
        {
            string dir = startDir;
            string normRoot = Path.GetFullPath(projectRoot);
            while (!string.IsNullOrEmpty(dir))
            {
                string full = Path.GetFullPath(dir);
                try
                {
                    string[] asmdefs = Directory.GetFiles(full, "*.asmdef", SearchOption.TopDirectoryOnly);
                    if (asmdefs != null && asmdefs.Length > 0)
                    {
                        return full;
                    }
                }
                catch
                {
                    return null;
                }
                if (string.Equals(full, normRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                if (full.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                {
                    dir = Path.GetDirectoryName(full);
                    continue;
                }
                return null;
            }
            return null;
        }

        private static void CopySingleFile(string sourceFile, string destFile)
        {
            try
            {
                string dir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.Copy(sourceFile, destFile, true);
                string meta = sourceFile + ".meta";
                if (File.Exists(meta))
                {
                    File.Copy(meta, destFile + ".meta", true);
                }
            }
            catch
            {
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            try
            {
                Directory.CreateDirectory(destDir);
                string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string rel = Relativize(sourceDir, files[i]);
                    CopySingleFile(files[i], Path.Combine(destDir, rel));
                }
            }
            catch
            {
            }
        }

        private static string BundleAssetsPath(string bundleOutputDirectory, string projectRoot, string fullPath)
        {
            string rel = Relativize(projectRoot, fullPath);
            string assetsPrefix = "Assets" + Path.DirectorySeparatorChar;
            if (rel.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                rel = rel.Substring(assetsPrefix.Length);
            }
            return Path.Combine(bundleOutputDirectory, "Assets", rel);
        }
        private static void CopyDependencyClosure(string[] dependencies, string bundleOutputDirectory)
        {
            if (dependencies == null)
            {
                return;
            }
            string root = ProjectRoot();
            HashSet<string> copiedAsmdefDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dependencies.Length; i++)
            {
                string depPath = dependencies[i];
                if (string.IsNullOrEmpty(depPath))
                {
                    continue;
                }
                string rel = depPath.Replace('/', Path.DirectorySeparatorChar);
                if (!rel.StartsWith("Assets" + Path.DirectorySeparatorChar) && !string.Equals(rel, "Assets", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string fullDep = Path.GetFullPath(Path.Combine(root, rel));
                if (!File.Exists(fullDep))
                {
                    continue;
                }
                if (fullDep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    string asmdefDir = FindEnclosingAsmdefDir(Path.GetDirectoryName(fullDep), root);
                    if (!string.IsNullOrEmpty(asmdefDir) && !copiedAsmdefDirs.Contains(asmdefDir))
                    {
                        copiedAsmdefDirs.Add(asmdefDir);
                        CopyDirectory(asmdefDir, BundleAssetsPath(bundleOutputDirectory, root, asmdefDir));
                    }
                    else if (string.IsNullOrEmpty(asmdefDir))
                    {
                        CopySingleFile(fullDep, BundleAssetsPath(bundleOutputDirectory, root, fullDep));
                    }
                    continue;
                }
                CopySingleFile(fullDep, BundleAssetsPath(bundleOutputDirectory, root, fullDep));
            }
            CopyReferencedAsmdefDirs(copiedAsmdefDirs, root, bundleOutputDirectory);
            CopyCompileSources(root, bundleOutputDirectory);
        }

        private static readonly string[] CompileSourceExtensions = { ".cs", ".asmdef", ".asmref", ".dll", ".rsp" };

        public static void CopyCompileSources(string root, string bundleOutputDirectory)
        {
            string assets = Path.Combine(root, "Assets");
            foreach (string file in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (Array.IndexOf(CompileSourceExtensions, extension.ToLowerInvariant()) < 0)
                {
                    continue;
                }
                CopySingleFile(file, BundleAssetsPath(bundleOutputDirectory, root, file));
            }
        }

        [Serializable]
        private sealed class AsmdefReferences
        {
            public string name = string.Empty;
            public string[] references = new string[0];
        }

        private static void CopyReferencedAsmdefDirs(HashSet<string> copiedAsmdefDirs, string root, string bundleOutputDirectory)
        {
            Dictionary<string, string> dirByName = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, AsmdefReferences> parsedByDir = new Dictionary<string, AsmdefReferences>(StringComparer.OrdinalIgnoreCase);
            foreach (string asmdef in Directory.GetFiles(Path.Combine(root, "Assets"), "*.asmdef", SearchOption.AllDirectories))
            {
                AsmdefReferences parsed = JsonUtility.FromJson<AsmdefReferences>(File.ReadAllText(asmdef));
                string dir = Path.GetDirectoryName(Path.GetFullPath(asmdef));
                parsedByDir[dir] = parsed;
                if (parsed != null && !string.IsNullOrEmpty(parsed.name) && !dirByName.ContainsKey(parsed.name))
                {
                    dirByName.Add(parsed.name, dir);
                }
            }
            Queue<string> pending = new Queue<string>(copiedAsmdefDirs);
            while (pending.Count > 0)
            {
                AsmdefReferences parsed;
                if (!parsedByDir.TryGetValue(Path.GetFullPath(pending.Dequeue()), out parsed) || parsed == null || parsed.references == null)
                {
                    continue;
                }
                for (int i = 0; i < parsed.references.Length; i++)
                {
                    string dir = ResolveAsmdefReference(parsed.references[i], dirByName, root);
                    if (string.IsNullOrEmpty(dir) || copiedAsmdefDirs.Contains(dir))
                    {
                        continue;
                    }
                    copiedAsmdefDirs.Add(dir);
                    CopyDirectory(dir, BundleAssetsPath(bundleOutputDirectory, root, dir));
                    pending.Enqueue(dir);
                }
            }
        }

        private static string ResolveAsmdefReference(string reference, Dictionary<string, string> dirByName, string root)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return string.Empty;
            }
            if (reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(reference.Substring(5));
                if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                return Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, assetPath)));
            }
            string dir;
            return dirByName.TryGetValue(reference, out dir) ? dir : string.Empty;
        }

        private static string CopyStillWrongRuntime(string bundleOutputDirectory)
        {
            string root = ProjectRoot();
            string packagesDir = Path.Combine(root, "Packages");
            string declared = "";
            try
            {
                declared = ReadManifestDependency(File.ReadAllText(Path.Combine(packagesDir, "manifest.json")), "com.stillwrong.reducer");
            }
            catch
            {
                declared = "";
            }
            string sourceDir = "";
            if (declared.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string rel = declared.Substring(5);
                string combined = rel;
                if (!Path.IsPathRooted(combined))
                {
                    combined = Path.GetFullPath(Path.Combine(packagesDir, rel));
                }
                if (Directory.Exists(combined))
                {
                    sourceDir = combined;
                }
            }
            if (string.IsNullOrEmpty(sourceDir))
            {
                string embedded = Path.Combine(packagesDir, "com.stillwrong.reducer");
                if (Directory.Exists(embedded))
                {
                    sourceDir = embedded;
                }
            }
            if (string.IsNullOrEmpty(sourceDir))
            {
                return "package-not-found";
            }
            CopyDirectory(sourceDir, Path.Combine(bundleOutputDirectory, "Packages", "com.stillwrong.reducer"));
            RewriteBundledManifest(bundleOutputDirectory);
            return "package-copied-from:" + Relativize(root, sourceDir);
        }
        private static string ReadManifestDependency(string manifestText, string packageName)
        {
            string key = "\"" + packageName + "\"";
            int at = manifestText.IndexOf(key, StringComparison.Ordinal);
            if (at < 0)
            {
                return "";
            }
            int colon = manifestText.IndexOf(':', at + key.Length);
            if (colon < 0)
            {
                return "";
            }
            int open = manifestText.IndexOf('"', colon + 1);
            if (open < 0)
            {
                return "";
            }
            int close = manifestText.IndexOf('"', open + 1);
            if (close < 0)
            {
                return "";
            }
            return manifestText.Substring(open + 1, close - open - 1);
        }
        private static void RewriteBundledManifest(string bundleOutputDirectory)
        {
            try
            {
                string path = Path.Combine(bundleOutputDirectory, "Packages", "manifest.json");
                if (!File.Exists(path))
                {
                    return;
                }
                string text = File.ReadAllText(path);
                string key = "\"com.stillwrong.reducer\"";
                int at = text.IndexOf(key, StringComparison.Ordinal);
                if (at < 0)
                {
                    return;
                }
                int colon = text.IndexOf(':', at + key.Length);
                if (colon < 0)
                {
                    return;
                }
                int open = text.IndexOf('"', colon + 1);
                if (open < 0)
                {
                    return;
                }
                int close = text.IndexOf('"', open + 1);
                if (close < 0)
                {
                    return;
                }
                string updated = text.Substring(0, open + 1) + "file:com.stillwrong.reducer" + text.Substring(close);
                File.WriteAllText(path, updated);
            }
            catch
            {
            }
        }
        private static void CopyProjectSettings(string bundleOutputDirectory)
        {
            try
            {
                string source = Path.Combine(ProjectRoot(), "ProjectSettings");
                if (!Directory.Exists(source))
                {
                    return;
                }
                CopyDirectory(source, Path.Combine(bundleOutputDirectory, "ProjectSettings"));
            }
            catch
            {
            }
        }

        private static void CopyPackagesManifest(string bundleOutputDirectory)
        {
            try
            {
                string source = Path.Combine(ProjectRoot(), "Packages", "manifest.json");
                if (!File.Exists(source))
                {
                    return;
                }
                string destDir = Path.Combine(bundleOutputDirectory, "Packages");
                Directory.CreateDirectory(destDir);
                File.Copy(source, Path.Combine(destDir, "manifest.json"), true);
                string packagesDir = Path.Combine(ProjectRoot(), "Packages");
                string[] embedded = Directory.GetDirectories(packagesDir);
                for (int i = 0; i < embedded.Length; i++)
                {
                    if (File.Exists(Path.Combine(embedded[i], "package.json")))
                    {
                        CopyDirectory(embedded[i], Path.Combine(destDir, Path.GetFileName(embedded[i])));
                    }
                }
            }
            catch
            {
            }
        }

        private static void CopySessionFolder(string sessionDir, string bundleOutputDirectory)
        {
            if (string.IsNullOrEmpty(sessionDir) || !Directory.Exists(sessionDir))
            {
                return;
            }
            try
            {
                string trimmed = sessionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dest = Path.Combine(bundleOutputDirectory, "Session", Path.GetFileName(trimmed));
                CopyDirectory(sessionDir, dest);
            }
            catch
            {
            }
        }

    }
    public static class StillWrongBatchVerifier
    {
        public static void RunFreshProcessCheck()
        {
            StillWrong.Batch.Verify.Run();
        }
    }
}

namespace StillWrong.Batch
{
    using StillWrong.Editor.Export;

    public static class Verify
    {
        private static bool _ticked;
        [Serializable]
        private sealed class VerifyState
        {
            public bool RunActive;
            public int Phase;
            public string ManifestPath = string.Empty;
            public string SceneProjectPath = string.Empty;
            public string TargetPath = string.Empty;
            public int MeasureFrames = 8;
            public int PlayStartFrame;
            public long PhaseStartUtc;
            public string BundleVerdict = string.Empty;
            public string NegativeVerdict = string.Empty;
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
        public static void Run()
        {
            if (!BeginRun(FindManifestPath()))
            {
                FinishWithCode(1);
                return;
            }
        }

        public static int RunInEditor(string manifestPath)
        {
            string sceneProjectPath = string.Empty;
            string targetPath = string.Empty;
            int measureFrames = 8;
            OracleSpec spec = LoadSpec(manifestPath, ref sceneProjectPath, ref targetPath, ref measureFrames);
            if (spec == null || string.IsNullOrEmpty(sceneProjectPath) || string.IsNullOrEmpty(targetPath))
            {
                return 1;
            }
            OpenBundleScene(sceneProjectPath);
            OracleResult bundleResult = EvaluateCurrent(spec);
            string bundleVerdict = bundleResult.Verdict.ToString();
            GameObject targetGo = UnitySymptomOracleRunner.FindGameObjectByPath(targetPath);
            if (targetGo != null)
            {
                targetGo.SetActive(false);
            }
            OracleResult negativeResult = EvaluateCurrent(spec);
            string negativeVerdict = negativeResult.Verdict.ToString();
            if (targetGo != null)
            {
                targetGo.SetActive(true);
            }
            int code = PassCode(bundleVerdict, negativeVerdict);
            WriteManifest(manifestPath, bundleVerdict, negativeVerdict, code, "edit-mode-check;play-mode-gate-runs-live-editor");
            return code;
        }

        private static void ArmTick()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            VerifyState state = null;
            try
            {
                state = LoadState();
            }
            catch
            {
                state = null;
            }
            if (state == null || !state.RunActive)
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                TickInner(state);
            }
            catch
            {
                FinishWithCode(1);
            }
        }

        private static void TickInner(VerifyState state)
        {
            if (state.Phase == 0)
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                OpenBundleScene(state.SceneProjectPath);
                state.Phase = 1;
                state.PlayStartFrame = Time.frameCount;
                state.PhaseStartUtc = DateTime.UtcNow.Ticks;
                SaveState(state);
                EditorApplication.isPlaying = true;
            }
            else if (state.Phase == 1)
            {
                if (!EditorApplication.isPlaying)
                {
                    return;
                }
                if (UtcSecondsSince(state.PhaseStartUtc) > 120.0)
                {
                    FinishWithCode(1);
                    return;
                }
                if (Time.frameCount - state.PlayStartFrame < state.MeasureFrames)
                {
                    return;
                }
                OracleSpec spec = LoadSpec(state.ManifestPath);
                state.BundleVerdict = EvaluateCurrent(spec).Verdict.ToString();
                state.Phase = 2;
                SaveState(state);
                EditorApplication.isPlaying = false;
            }
            else if (state.Phase == 2)
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                OpenBundleScene(state.SceneProjectPath);
                GameObject targetGo = UnitySymptomOracleRunner.FindGameObjectByPath(state.TargetPath);
                if (targetGo != null)
                {
                    targetGo.SetActive(false);
                }
                state.Phase = 3;
                state.PlayStartFrame = Time.frameCount;
                state.PhaseStartUtc = DateTime.UtcNow.Ticks;
                SaveState(state);
                EditorApplication.isPlaying = true;
            }
            else if (state.Phase == 3)
            {
                if (!EditorApplication.isPlaying)
                {
                    return;
                }
                if (UtcSecondsSince(state.PhaseStartUtc) > 120.0)
                {
                    FinishWithCode(1);
                    return;
                }
                if (Time.frameCount - state.PlayStartFrame < state.MeasureFrames)
                {
                    return;
                }
                OracleSpec spec = LoadSpec(state.ManifestPath);
                state.NegativeVerdict = EvaluateCurrent(spec).Verdict.ToString();
                state.Phase = 4;
                SaveState(state);
                EditorApplication.isPlaying = false;
            }
            else
            {
                if (EditorApplication.isPlaying)
                {
                    return;
                }
                int code = PassCode(state.BundleVerdict, state.NegativeVerdict);
                WriteManifest(state.ManifestPath, state.BundleVerdict, state.NegativeVerdict, code, "play-mode-batch");
                state.RunActive = false;
                SaveState(state);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(code);
                }
            }
        }

        private static int PassCode(string bundleVerdict, string negativeVerdict)
        {
            if (bundleVerdict == OracleVerdict.StillWrong.ToString() && negativeVerdict == OracleVerdict.Inconclusive.ToString())
            {
                return 0;
            }
            return 1;
        }

        private static void OpenBundleScene(string sceneProjectPath)
        {
            if (string.IsNullOrEmpty(sceneProjectPath))
            {
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(sceneProjectPath, OpenSceneMode.Single);
            }
            catch
            {
            }
        }

        private static OracleResult EvaluateCurrent(OracleSpec spec)
        {
            OracleSpec use = spec != null ? spec : FixtureSpecs.C1();
            EvaluationContext ctx = new EvaluationContext();
            return OracleEvaluator.Evaluate(use, new UnityProbeSource(use), ctx);
        }

        private static string StatePath()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "verify-state.json");
        }
        private static void SaveState(VerifyState state)
        {
            try
            {
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static VerifyState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<VerifyState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static bool BeginRun(string manifestPath)
        {
            string sceneProjectPath = string.Empty;
            string targetPath = string.Empty;
            int measureFrames = 8;
            OracleSpec spec = LoadSpec(manifestPath, ref sceneProjectPath, ref targetPath, ref measureFrames);
            if (spec == null || string.IsNullOrEmpty(sceneProjectPath) || string.IsNullOrEmpty(targetPath))
            {
                return false;
            }
            VerifyState state = new VerifyState();
            state.RunActive = true;
            state.Phase = 0;
            state.ManifestPath = manifestPath;
            state.SceneProjectPath = sceneProjectPath;
            state.TargetPath = targetPath;
            state.MeasureFrames = measureFrames;
            SaveState(state);
            ArmTick();
            return true;
        }
        private static OracleSpec LoadSpec(string manifestPath)
        {
            string sceneProjectPath = string.Empty;
            string targetPath = string.Empty;
            int measureFrames = 8;
            return LoadSpec(manifestPath, ref sceneProjectPath, ref targetPath, ref measureFrames);
        }
        private static OracleSpec LoadSpec(string manifestPath, ref string sceneProjectPath, ref string targetPath, ref int measureFrames)
        {
            sceneProjectPath = string.Empty;
            targetPath = string.Empty;
            measureFrames = 8;
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                return null;
            }
            ReproManifestDto manifest = null;
            try
            {
                manifest = JsonUtility.FromJson<ReproManifestDto>(File.ReadAllText(manifestPath));
            }
            catch
            {
                return null;
            }
            if (manifest == null)
            {
                return null;
            }
            OracleSpec spec = null;
            if (!string.IsNullOrEmpty(manifest.oracle_spec_json))
            {
                try
                {
                    spec = new OracleSerializer().Read(manifest.oracle_spec_json);
                }
                catch
                {
                    spec = null;
                }
            }
            if (spec == null)
            {
                spec = FixtureSpecs.C1();
            }
            sceneProjectPath = manifest.bundle_scene_path != null ? manifest.bundle_scene_path : "";
            if (manifest.target_identity != null && !string.IsNullOrEmpty(manifest.target_identity.hierarchy_path))
            {
                targetPath = manifest.target_identity.hierarchy_path;
            }
            if (spec != null && spec.MeasurementFrames != null && spec.MeasurementFrames.Count > 0)
            {
                measureFrames = Math.Max(4, Math.Min(30, spec.MeasurementFrames[0] + 2));
            }
            return spec;
        }
        private static double UtcSecondsSince(long startTicks)
        {
            try
            {
                return (DateTime.UtcNow.Ticks - startTicks) / 10000000.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static string FindManifestPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-manifest" || args[i] == "-manifestPath")
                {
                    return args[i + 1];
                }
            }
            try
            {
                string[] found = Directory.GetFiles(Application.dataPath, "repro-manifest.json", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    return found[0];
                }
            }
            catch
            {
            }
            return "";
        }

        private static void WriteManifest(string manifestPath, string bundleVerdict, string negativeVerdict, int code, string mode)
        {
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                return;
            }
            try
            {
                ReproManifestDto manifest = JsonUtility.FromJson<ReproManifestDto>(File.ReadAllText(manifestPath));
                if (manifest == null)
                {
                    return;
                }
                manifest.fresh_process_verification = new FreshProcessVerificationDto
                {
                    command = string.Join(" ", Environment.GetCommandLineArgs()),
                    exit_status = code,
                    oracle_verdict = bundleVerdict,
                    healthy_control_verdict = "not-supplied",
                    invalid_scenario_verdict = negativeVerdict,
                    log_path = mode,
                    verification_timestamp = DateTime.UtcNow.ToString("o")
                };
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            }
            catch
            {
            }
        }

        private static void FinishWithCode(int code)
        {
            try
            {
                VerifyState state = LoadState();
                if (state != null)
                {
                    state.RunActive = false;
                    SaveState(state);
                }
            }
            catch
            {
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
