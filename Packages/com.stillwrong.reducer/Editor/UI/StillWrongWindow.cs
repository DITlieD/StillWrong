using System;
using System.IO;
using System.Collections.Generic;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Editor.Export;
using StillWrong.Editor.Reduction;
using StillWrong.Inspection;
using StillWrong.Oracle;
using StillWrong.Recording;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StillWrong.Editor.UI
{
    public sealed class StillWrongWindowState : ScriptableSingleton<StillWrongWindowState>
    {
        public string caseId = "C1-ui-clip";
        public string targetPath = "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody";
        public string maskPath = "BattleCanvas/ContextFlyout/HotbarSlotCard";
        public string occluderPath = "";
        public string sentence = "The tooltip body text expands the container bounds or wraps within visible limits.";
        public int markMode;
        public Vector2 markPoint = new Vector2(960f, 540f);
        public Rect markBox = new Rect(800f, 400f, 320f, 180f);
        public List<Vector2> negativePoints = new List<Vector2>();
        public int scrubFrameIndex;
        public bool showJson;
        public string specJson = "";
        public string lastConfirmedTarget = "";
        public double lastConfirmedMaskCoverage = -1.0;
        public double lastConfirmedElementCoverage = -1.0;
        public Rect lastConfirmedBox = new Rect(0f, 0f, 0f, 0f);
        public bool hasConfirmedBox;
    }
    public sealed class StillWrongWindow : EditorWindow
    {
        public enum MarkMode
        {
            Point,
            Box
        }

        private StillWrongWindowState State
        {
            get { return StillWrongWindowState.instance; }
        }

        private MarkMode _markMode = MarkMode.Point;
        private bool _draggingBox;

        private Vector2 _scrollPos;

        private string _groundingStatus = "Ready";
        private List<StillWrongGroundingCandidate> _candidates = new List<StillWrongGroundingCandidate>();
        private bool _candidatesAmbiguous;
        private string _candidatesDegraded = string.Empty;
        private bool _candidatesUsedSam;
        private bool _candidatesUsedId;
        private string _oracleStatus = "Not evaluated";
        private OracleVerdict? _lastVerdict = null;

        private string _reductionStatus = "Idle";
        private ReductionResult _lastReductionResult = null;
        private string _reducedScenePath = "";

        private string _exportDirectory = "outputs/bundles/C1-ui-clip-repro";
        private string _lastExportedManifest = "";
        private string _verifyStatus = "Not verified";
        private string _reportStatus = "";

        [MenuItem("Tools/Still Wrong/Visual Bug Reducer")]
        public static void ShowWindow()
        {
            var window = GetWindow<StillWrongWindow>("Still Wrong Reducer");
            window.minSize = new Vector2(480, 640);
            window.Show();
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(8);

            DrawRingBufferSection();
            EditorGUILayout.Space(8);

            DrawGroundingSection();
            EditorGUILayout.Space(8);

            DrawOracleSection();
            EditorGUILayout.Space(8);

            DrawReductionSection();
            EditorGUILayout.Space(8);

            DrawExportSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Still Wrong Visual Bug Reducer", EditorStyles.boldLabel);
            Scene activeScene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Active Scene: " + (string.IsNullOrEmpty(activeScene.path) ? "Untitled / Unsaved" : activeScene.path));
            State.caseId = EditorGUILayout.TextField("Case ID", State.caseId);
        }
        private void DrawRingBufferSection()
        {
            EditorGUILayout.LabelField("1. Temporal Ring Buffer (60 Hz)", EditorStyles.boldLabel);

            var buffer = UnityTemporalRingBuffer.Instance;
            if (buffer == null)
            {
                EditorGUILayout.HelpBox("UnityTemporalRingBuffer instance not found in scene. Add the component to a GameObject to record.", MessageType.Info);
                if (GUILayout.Button("Create RingBuffer In Scene"))
                {
                    var go = new GameObject("StillWrong_RingBuffer");
                    go.AddComponent<UnityTemporalRingBuffer>();
                }
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status: " + (buffer.IsFrozen ? "Frozen" : (buffer.IsRecording ? "Recording" : "Paused")));
            EditorGUILayout.LabelField("Frames: " + buffer.Count + "/" + buffer.Capacity);
            EditorGUILayout.LabelField("Dropped: " + buffer.DroppedFramesCount);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(buffer.IsFrozen ? "Unfreeze" : "Freeze Ring Buffer"))
            {
                buffer.ToggleFreeze();
            }
            if (GUILayout.Button("Clear Buffer"))
            {
                buffer.Clear();
                State.scrubFrameIndex = 0;
            }
            EditorGUILayout.EndHorizontal();

            if (buffer.Count > 0)
            {
                State.scrubFrameIndex = EditorGUILayout.IntSlider("Timeline Scrub", State.scrubFrameIndex, 0, buffer.Count - 1);
                var frame = buffer.GetFrameAt(State.scrubFrameIndex);
                if (frame != null)
                {
                    EditorGUILayout.LabelField(string.Format("Frame #{0} | Time: {1:F2}s | dt: {2:F4}s | Dropped: {3}",
                        frame.FrameIndex, frame.Timestamp, frame.DeltaTime, frame.IsDroppedFrame));
                    EditorGUILayout.LabelField(string.Format("Cam Pos: {0} | Pointer: {1}",
                        frame.CameraPosition, frame.PointerPosition));

                    if (frame.DownscaledFrame != null)
                    {
                        Rect previewRect = GUILayoutUtility.GetRect(240, 135, GUILayout.ExpandWidth(false));
                        GUI.DrawTexture(previewRect, frame.DownscaledFrame, ScaleMode.ScaleToFit);
                        HandlePreviewEvents(previewRect, frame);
                        DrawMarkOverlay(previewRect, frame);
                    }
                }
            }
        }

        private void HandlePreviewEvents(Rect previewRect, RingFrameRecord frame)
        {
            Event evt = Event.current;
            if (evt == null || frame.DownscaledFrame == null)
            {
                return;
            }
            float fw = frame.DownscaledFrame.width;
            float fh = frame.DownscaledFrame.height;
            if (fw <= 0f || fh <= 0f)
            {
                return;
            }
            if (!previewRect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.MouseUp && _draggingBox)
                {
                    _draggingBox = false;
                    evt.Use();
                }
                return;
            }
            Vector2 framePos = new Vector2((evt.mousePosition.x - previewRect.xMin) / previewRect.width * fw, (evt.mousePosition.y - previewRect.yMin) / previewRect.height * fh);
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (evt.shift)
                {
                    State.negativePoints.Add(framePos);
                    evt.Use();
                }
                else if (_markMode == MarkMode.Box)
                {
                    _draggingBox = true;
                    State.markBox = new Rect(framePos.x, framePos.y, 1f, 1f);
                    evt.Use();
                }
                else
                {
                    State.markPoint = framePos;
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && _draggingBox)
            {
                Rect box = State.markBox;
                box.xMax = framePos.x;
                box.yMax = framePos.y;
                State.markBox = NormalizeRect(box);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _draggingBox)
            {
                _draggingBox = false;
                evt.Use();
            }
        }
        private void DrawMarkOverlay(Rect previewRect, RingFrameRecord frame)
        {
            if (frame.DownscaledFrame == null)
            {
                return;
            }
            float fw = frame.DownscaledFrame.width;
            float fh = frame.DownscaledFrame.height;
            if (fw <= 0f || fh <= 0f)
            {
                return;
            }
            if (State.hasConfirmedBox)
            {
                DrawRectOutline(ToPreviewRect(State.lastConfirmedBox, previewRect, fw, fh), new Color(0f, 1f, 0f, 1f));
            }
            if (_markMode == MarkMode.Box)
            {
                DrawRectOutline(ToPreviewRect(State.markBox, previewRect, fw, fh), new Color(1f, 1f, 1f, 1f));
            }
            else
            {
                Vector2 p = ToPreviewPoint(State.markPoint, previewRect, fw, fh);
                DrawRectOutline(Rect.MinMaxRect(p.x - 4f, p.y - 4f, p.x + 4f, p.y + 4f), new Color(1f, 1f, 1f, 1f));
            }
            for (int i = 0; i < State.negativePoints.Count; i++)
            {
                Vector2 n = ToPreviewPoint(State.negativePoints[i], previewRect, fw, fh);
                DrawRectOutline(Rect.MinMaxRect(n.x - 4f, n.y - 4f, n.x + 4f, n.y + 4f), new Color(1f, 0f, 0f, 1f));
            }
        }
        private static Rect ToPreviewRect(Rect frameRect, Rect previewRect, float fw, float fh)
        {
            float xMin = previewRect.xMin + frameRect.xMin / fw * previewRect.width;
            float xMax = previewRect.xMin + frameRect.xMax / fw * previewRect.width;
            float yMin = previewRect.yMin + frameRect.yMin / fh * previewRect.height;
            float yMax = previewRect.yMin + frameRect.yMax / fh * previewRect.height;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
        private static Vector2 ToPreviewPoint(Vector2 framePoint, Rect previewRect, float fw, float fh)
        {
            return new Vector2(previewRect.xMin + framePoint.x / fw * previewRect.width, previewRect.yMin + framePoint.y / fh * previewRect.height);
        }
        private static Rect NormalizeRect(Rect box)
        {
            float xMin = Math.Min(box.xMin, box.xMax);
            float xMax = Math.Max(box.xMin, box.xMax);
            float yMin = Math.Min(box.yMin, box.yMax);
            float yMax = Math.Max(box.yMin, box.yMax);
            return Rect.MinMaxRect(xMin, yMin, Math.Max(xMax, xMin + 1f), Math.Max(yMax, yMin + 1f));
        }
        private static void DrawRectOutline(Rect rect, Color color)
        {
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = saved;
        }
        private void DrawGroundingSection()
        {
            EditorGUILayout.LabelField("2. Grounding & Region Specification", EditorStyles.boldLabel);

            _markMode = (MarkMode)EditorGUILayout.EnumPopup("Mark Mode", _markMode);
            if (_markMode == MarkMode.Point)
            {
                State.markPoint = EditorGUILayout.Vector2Field("Point Coordinate (frame px)", State.markPoint);
            }
            else
            {
                State.markBox = EditorGUILayout.RectField("Box Region (frame px)", State.markBox);
            }
            State.markMode = (int)_markMode;
            if (GUILayout.Button("Clear Negative Points"))
            {
                State.negativePoints = new List<Vector2>();
            }

            State.targetPath = EditorGUILayout.TextField("Target Hierarchy Path", State.targetPath);
            State.maskPath = EditorGUILayout.TextField("Mask Hierarchy Path", State.maskPath);
            State.occluderPath = EditorGUILayout.TextField("Occluder Path (C2)", State.occluderPath);

            EditorGUILayout.LabelField("Expected Behavior:");
            State.sentence = EditorGUILayout.TextArea(State.sentence, GUILayout.Height(48));

            if (GUILayout.Button("Ground Mark"))
            {
                RunGrounding();
            }
            EditorGUILayout.LabelField("Grounding Status: " + _groundingStatus);
            if (!StillWrongGrounding.IsCustomService)
            {
                EditorGUILayout.HelpBox("Degraded grounding: no SAM service registered. Box overlap plus ID buffer only; no mask, no semantic pick.", MessageType.Info);
            }
            if (!string.IsNullOrEmpty(_candidatesDegraded))
            {
                EditorGUILayout.LabelField("Coverage Source: " + _candidatesDegraded);
            }
            if (_candidatesAmbiguous)
            {
                EditorGUILayout.HelpBox("Ambiguous: top candidates agree within 10 percent. Confirm by hand.", MessageType.Warning);
            }
            for (int i = 0; i < _candidates.Count; i++)
            {
                StillWrongGroundingCandidate candidate = _candidates[i];
                if (candidate == null)
                {
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(candidate.ElementPath + " | mask " + (candidate.MaskCoverage * 100.0).ToString("F1") + "% | element " + (candidate.ElementCoverage * 100.0).ToString("F1") + "%" + (candidate.IsAnalytic ? " | analytic" : "") + (string.IsNullOrEmpty(candidate.Warning) ? "" : " | " + candidate.Warning));
                if (GUILayout.Button("Target", GUILayout.Width(64)))
                {
                    ConfirmCandidate(candidate, true);
                }
                if (GUILayout.Button("Mask", GUILayout.Width(64)))
                {
                    ConfirmCandidate(candidate, false);
                }
                EditorGUILayout.EndHorizontal();
            }


        }
        private void RunGrounding()
        {
            StillWrongGroundingRequest request = new StillWrongGroundingRequest();
            request.Sentence = State.sentence;
            if (_markMode == MarkMode.Box)
            {
                request.HasBox = true;
                request.Box = ToScreenBox(State.markBox);
            }
            else
            {
                request.PositivePoints.Add(ToScreenPoint(State.markPoint));
            }
            for (int i = 0; i < State.negativePoints.Count; i++)
            {
                request.NegativePoints.Add(ToScreenPoint(State.negativePoints[i]));
            }
            StillWrongGroundingResult result = StillWrongGrounding.ActiveService.Ground(request);
            _candidates = result.Candidates != null ? result.Candidates : new List<StillWrongGroundingCandidate>();
            _candidatesAmbiguous = result.IsAmbiguous;
            _candidatesDegraded = result.DegradedReason;
            _candidatesUsedSam = result.UsedSamMask;
            _candidatesUsedId = result.UsedIdBuffer;
            _groundingStatus = "Candidates: " + _candidates.Count + (result.UsedSamMask ? " (sam-mask)" : "") + (result.UsedIdBuffer ? " (id-buffer)" : " (box-overlap)");
        }
        private void ConfirmCandidate(StillWrongGroundingCandidate candidate, bool asTarget)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.ElementPath))
            {
                return;
            }
            if (asTarget)
            {
                State.targetPath = candidate.ElementPath;
            }
            else
            {
                State.maskPath = candidate.ElementPath;
            }
            State.lastConfirmedTarget = candidate.ElementPath;
            State.lastConfirmedMaskCoverage = candidate.MaskCoverage;
            State.lastConfirmedElementCoverage = candidate.ElementCoverage;
            State.lastConfirmedBox = State.markBox;
            State.hasConfirmedBox = _markMode == MarkMode.Box;
            _groundingStatus = "Confirmed " + (asTarget ? "target" : "mask") + ": " + candidate.ElementPath + " at mask " + (candidate.MaskCoverage * 100.0).ToString("F1") + "%, element " + (candidate.ElementCoverage * 100.0).ToString("F1") + "%";
        }
        private static Rect ToScreenBox(Rect frameBox)
        {
            float sx = Screen.width > 0 ? Screen.width / (float)UnityTemporalRingBuffer.FrameWidth : 1f;
            float sy = Screen.height > 0 ? Screen.height / (float)UnityTemporalRingBuffer.FrameHeight : 1f;
            return Rect.MinMaxRect(frameBox.xMin * sx, frameBox.yMin * sy, frameBox.xMax * sx, frameBox.yMax * sy);
        }
        private static Vector2 ToScreenPoint(Vector2 framePoint)
        {
            float sx = Screen.width > 0 ? Screen.width / (float)UnityTemporalRingBuffer.FrameWidth : 1f;
            float sy = Screen.height > 0 ? Screen.height / (float)UnityTemporalRingBuffer.FrameHeight : 1f;
            return new Vector2(framePoint.x * sx, framePoint.y * sy);
        }
        private void DrawOracleSection()
        {
            EditorGUILayout.LabelField("3. Tri-State Symptom Oracle", EditorStyles.boldLabel);

            if (GUILayout.Button("Evaluate Oracle Now"))
            {
                OracleSpec spec = ResolveSpec();
                OracleResult result = UnitySymptomOracleRunner.EvaluateLive(spec);
                _lastVerdict = result.Verdict;
                _oracleStatus = "Verdict: " + _lastVerdict.Value;
                if (result.Reasons.Count > 0)
                {
                    _oracleStatus += " | " + result.Reasons[0];
                }
            }

            if (_lastVerdict.HasValue)
            {
                GUIStyle verdictStyle = new GUIStyle(EditorStyles.boldLabel);
                switch (_lastVerdict.Value)
                {
                    case OracleVerdict.StillWrong:
                        verdictStyle.normal.textColor = Color.red;
                        break;
                    case OracleVerdict.HealthyPass:
                        verdictStyle.normal.textColor = Color.green;
                        break;
                    default:
                        verdictStyle.normal.textColor = Color.yellow;
                        break;
                }
                EditorGUILayout.LabelField(_oracleStatus, verdictStyle);
            }
            else
            {
                EditorGUILayout.LabelField("Oracle Status: " + _oracleStatus);
            }
            State.showJson = EditorGUILayout.Toggle("Show Spec JSON", State.showJson);
            if (State.showJson)
            {
                State.specJson = EditorGUILayout.TextArea(State.specJson, GUILayout.Height(96));
                if (GUILayout.Button("Copy Current Spec to JSON"))
                {
                    try
                    {
                        State.specJson = new OracleSerializer().Write(ResolveSpec());
                    }
                    catch (Exception ex)
                    {
                        _oracleStatus = "Spec JSON write failed: " + ex.Message;
                    }
                }
                if (GUILayout.Button("Apply Spec JSON"))
                {
                    ApplySpecJson();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(PlainWordRenderer.Render(ResolveSpec()), MessageType.None);
            }
        }

        private void ApplySpecJson()
        {
            if (string.IsNullOrEmpty(State.specJson))
            {
                _oracleStatus = "Spec JSON empty; nothing applied.";
                return;
            }
            try
            {
                OracleSpec parsed = new OracleSerializer().Read(State.specJson);
                if (parsed == null)
                {
                    _oracleStatus = "Spec JSON parsed to null; nothing applied.";
                    return;
                }
                _oracleStatus = "Spec JSON parsed: " + parsed.SpecId + ". Evaluate to check the verdict.";
            }
            catch (Exception ex)
            {
                _oracleStatus = "Spec JSON parse failed: " + ex.Message;
            }
        }
        private void DrawReductionSection()
        {
            EditorGUILayout.LabelField("4. Automated Scene Reduction (ddmin)", EditorStyles.boldLabel);

            Scene activeScene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Source: " + activeScene.path);

            if (GUILayout.Button("Run Automated Scene Reduction"))
            {
                if (string.IsNullOrEmpty(activeScene.path))
                {
                    EditorUtility.DisplayDialog("Error", "Please save the active scene before reducing.", "OK");
                    return;
                }

                _reductionStatus = "Running delta debugging reduction...";
                OracleSpec spec = ResolveSpec();

                try
                {
                    var (result, path) = UnitySceneCopyReducer.ReduceScene(
                        activeScene.path,
                        spec);

                    _lastReductionResult = result;
                    _reducedScenePath = path;
                    _reductionStatus = string.Format("Reduction complete: {0}/{1} nodes removed ({2} kept).",
                        result.RemovedCount, result.InitialCandidateCount, result.FinalCandidateCount);
                }
                catch (Exception ex)
                {
                    _reductionStatus = "Reduction failed: " + ex.Message;
                    Debug.LogException(ex);
                }
            }

            EditorGUILayout.LabelField("Reduction Status: " + _reductionStatus);
            if (!string.IsNullOrEmpty(_reducedScenePath))
            {
                EditorGUILayout.LabelField("Reduced Scene: " + _reducedScenePath);
            }
            DrawTrialList();
        }

        private void DrawTrialList()
        {
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "StillWrong", "reductions");
            if (!Directory.Exists(root))
            {
                EditorGUILayout.LabelField("Trials: no reduction runs found.");
                return;
            }
            string[] dirs = Directory.GetDirectories(root);
            if (dirs == null || dirs.Length == 0)
            {
                EditorGUILayout.LabelField("Trials: no reduction runs found.");
                return;
            }
            Array.Sort(dirs, StringComparer.Ordinal);
            string latest = dirs[dirs.Length - 1];
            string logPath = Path.Combine(latest, "trial-log.json");
            if (!File.Exists(logPath))
            {
                EditorGUILayout.LabelField("Trials: latest run has no trial log.");
                return;
            }
            TrialSummaryFile summary = null;
            try
            {
                summary = JsonUtility.FromJson<TrialSummaryFile>(File.ReadAllText(logPath));
                if (summary == null || summary.Trials == null)
                {
                    EditorGUILayout.LabelField("Trials: trial log unreadable.");
                    return;
                }
            }
            catch
            {
                EditorGUILayout.LabelField("Trials: trial log unreadable.");
                return;
            }
            EditorGUILayout.LabelField("Trials (" + Path.GetFileName(latest) + "): " + summary.Trials.Count);
            int shown = Math.Min(summary.Trials.Count, 8);
            for (int i = summary.Trials.Count - shown; i < summary.Trials.Count; i++)
            {
                C1TrialLogEntry entry = summary.Trials[i];
                if (entry == null)
                {
                    continue;
                }
                EditorGUILayout.LabelField("#" + entry.TrialIndex + " " + entry.Verdict + " " + entry.DurationMs + "ms");
            }
        }
        private void DrawExportSection()
        {
            EditorGUILayout.LabelField("5. Repro Bundle Export", EditorStyles.boldLabel);

            _exportDirectory = EditorGUILayout.TextField("Export Directory", _exportDirectory);

            if (GUILayout.Button("Export Reproduction Bundle"))
            {
                string sceneToExport = !string.IsNullOrEmpty(_reducedScenePath) ? _reducedScenePath : SceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(sceneToExport) || !File.Exists(sceneToExport))
                {
                    EditorUtility.DisplayDialog("Error", "No valid reduced scene to export. Run reduction first or save active scene.", "OK");
                    return;
                }

                OracleSpec spec = ResolveSpec();
                string manifestPath = UnityReproBundleExporter.ExportBundle(
                    sceneToExport,
                    State.caseId,
                    spec,
                    _lastReductionResult,
                    _exportDirectory);

                _lastExportedManifest = manifestPath;
                EditorUtility.DisplayDialog("Export Complete", "Reproduction bundle exported to:" + manifestPath, "OK");
            }

            if (!string.IsNullOrEmpty(_lastExportedManifest))
            {
                EditorGUILayout.LabelField("Manifest: " + _lastExportedManifest);
            }
            bool hasManifest = !string.IsNullOrEmpty(_lastExportedManifest) && File.Exists(_lastExportedManifest);
            if (hasManifest)
            {
                if (GUILayout.Button("Verify Bundle (Edit Mode)"))
                {
                    int code = StillWrong.Batch.Verify.RunInEditor(_lastExportedManifest);
                    _verifyStatus = code == 0 ? "Verify passed (edit mode; Play Mode gate runs in the live editor)." : "Verify failed with exit " + code + " (edit mode).";
                }
            }
            else
            {
                EditorGUILayout.LabelField("Verify: disabled, export a bundle first.");
            }
            EditorGUILayout.LabelField("Verify: " + _verifyStatus);
            if (GUILayout.Button("Write Report"))
            {
                _reportStatus = WriteReport();
            }
            if (!string.IsNullOrEmpty(_reportStatus))
            {
                EditorGUILayout.LabelField(_reportStatus);
            }
        }

        private string WriteReport()
        {
            OracleSpec spec = ResolveSpec();
            string dir = !string.IsNullOrEmpty(_lastExportedManifest) ? Path.GetDirectoryName(_lastExportedManifest) : _exportDirectory;
            if (string.IsNullOrEmpty(dir))
            {
                return "Report: no output directory.";
            }
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "grounding-report.txt");
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("case: " + State.caseId);
                sb.AppendLine("target: " + State.targetPath);
                sb.AppendLine("mask: " + State.maskPath);
                sb.AppendLine("sentence: " + State.sentence);
                sb.AppendLine("confirmed-target: " + State.lastConfirmedTarget);
                sb.AppendLine("confirmed-mask-coverage: " + State.lastConfirmedMaskCoverage.ToString("F4"));
                sb.AppendLine("confirmed-element-coverage: " + State.lastConfirmedElementCoverage.ToString("F4"));
                sb.AppendLine("candidates: " + _candidates.Count + " ambiguous=" + _candidatesAmbiguous + " degraded=" + _candidatesDegraded);
                for (int i = 0; i < _candidates.Count; i++)
                {
                    StillWrongGroundingCandidate c = _candidates[i];
                    if (c == null)
                    {
                        continue;
                    }
                    sb.AppendLine("candidate: " + c.ElementPath + " mask=" + c.MaskCoverage.ToString("F4") + " element=" + c.ElementCoverage.ToString("F4") + (c.IsAnalytic ? " analytic" : "") + (string.IsNullOrEmpty(c.Warning) ? "" : " " + c.Warning));
                }
                sb.AppendLine("oracle: " + _oracleStatus);
                sb.AppendLine("reduction: " + _reductionStatus + " scene=" + _reducedScenePath);
                sb.AppendLine("spec-words: " + PlainWordRenderer.Render(spec));
                File.WriteAllText(path, sb.ToString());
                return "Report written: " + path;
            }
            catch (Exception ex)
            {
                return "Report failed: " + ex.Message;
            }
        }
        private OracleSpec ResolveSpec()
        {
            OracleSpec spec = UnitySymptomOracleRunner.SpecById(State.caseId);
            if (spec != null)
            {
                return spec;
            }
            return FixtureSpecs.C1();
        }
    }
}
