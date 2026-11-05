using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StillWrong.Editor.Recording;
using StillWrong.Recording;
namespace StillWrong.Editor.UI
{
    // Drawing the recorded frame, zoom, the SAM mask overlay, and turning a click into a pick.
    public sealed partial class StillWrongBugSheet
    {
        private void DrawFrameArea()
        {
            float width = EditorGUIUtility.currentViewWidth - 24f;
            float height = _frameCap > 1f ? _frameCap : 160f;
            Texture2D shown = SelectedFullFrame();
            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(rect, new Color(0.05f, 0.06f, 0.08f, 1f));
            if (shown == null)
            {
                GUI.Label(rect, "Frozen frame", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            shown.filterMode = FilterMode.Bilinear;
            shown.wrapMode = TextureWrapMode.Clamp;
            HandleFrameZoom(rect);
            DrawZoomedFrame(rect, shown);
            HandleFramePointer(rect, shown);
            EditorGUILayout.LabelField(FrameCaption() + "   scroll to zoom, right-drag to move", EditorStyles.miniLabel);
        }
        private void DrawZoomedFrame(Rect view, Texture2D tex)
        {
            Rect fit = AspectFit(view, tex);
            if (fit.width <= 1f || fit.height <= 1f)
            {
                return;
            }
            Texture2D overlay = MaskOverlayTexture();
            if (_zoom <= 1.001f)
            {
                GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit, false);
                if (overlay != null)
                {
                    GUI.DrawTexture(fit, overlay, ScaleMode.StretchToFill, true);
                }
                return;
            }
            float uvW = 1f / _zoom;
            float uvH = 1f / _zoom;
            float u = 0.5f - uvW * 0.5f - _pan.x / fit.width;
            float vTop = 0.5f - uvH * 0.5f - _pan.y / fit.height;
            u = Mathf.Clamp(u, 0f, 1f - uvW);
            vTop = Mathf.Clamp(vTop, 0f, 1f - uvH);
            Rect coords = new Rect(u, 1f - (vTop + uvH), uvW, uvH);
            GUI.DrawTextureWithTexCoords(fit, tex, coords, false);
            if (overlay != null)
            {
                GUI.DrawTextureWithTexCoords(fit, overlay, coords, true);
            }
        }
        // The SAM mask of the last click, tinted cyan, only while the same frame is shown.
        private Texture2D MaskOverlayTexture()
        {
            string framePath = (_thumbFiles != null && _selectedThumb >= 0 && _selectedThumb < _thumbFiles.Count) ? _thumbFiles[_selectedThumb] : string.Empty;
            if (_maskOverlay == null || _maskOverlayPath != framePath)
            {
                return null;
            }
            if (_maskOverlayTex != null)
            {
                return _maskOverlayTex;
            }
            int h = _maskOverlay.GetLength(0);
            int w = _maskOverlay.GetLength(1);
            Color32[] pixels = new Color32[w * h];
            Color32 on = new Color32(25, 227, 255, 110);
            Color32 off = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = _maskOverlay[y, x] ? on : off;
                }
            }
            _maskOverlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _maskOverlayTex.filterMode = FilterMode.Point;
            _maskOverlayTex.SetPixels32(pixels);
            _maskOverlayTex.Apply(false);
            return _maskOverlayTex;
        }
        private void SetMaskOverlay(bool[,] mask, string framePath)
        {
            if (_maskOverlayTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_maskOverlayTex);
                _maskOverlayTex = null;
            }
            _maskOverlay = mask;
            _maskOverlayPath = framePath ?? string.Empty;
        }
        private static Rect AspectFit(Rect view, Texture2D tex)
        {
            float aspect = tex.height > 0 ? (float)tex.width / (float)tex.height : 16f / 9f;
            float fitW = view.width;
            float fitH = fitW / aspect;
            if (fitH > view.height)
            {
                fitH = view.height;
                fitW = fitH * aspect;
            }
            float x = view.x + (view.width - fitW) * 0.5f;
            float y = view.y + (view.height - fitH) * 0.5f;
            return new Rect(x, y, fitW, fitH);
        }
        private void HandleFrameZoom(Rect rect)
        {
            Event evt = Event.current;
            if (evt == null || !rect.Contains(evt.mousePosition))
            {
                return;
            }
            if (evt.type == EventType.ScrollWheel)
            {
                float step = evt.delta.y < 0f ? 1.15f : 0.87f;
                _zoom = Mathf.Clamp(_zoom * step, 1f, 8f);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 1)
            {
                _pan += evt.delta;
                evt.Use();
                Repaint();
            }
        }
        private Rect FrameDrawRect(Rect view, Texture2D tex)
        {
            float aspect = tex.height > 0 ? (float)tex.width / (float)tex.height : 16f / 9f;
            float fitW = view.width;
            float fitH = fitW / aspect;
            if (fitH > view.height)
            {
                fitH = view.height;
                fitW = fitH * aspect;
            }
            float x = view.x + (view.width - fitW) * 0.5f;
            float y = view.y + (view.height - fitH) * 0.5f;
            return new Rect(x, y, fitW, fitH);
        }
        private void HandleFramePointer(Rect rect, Texture2D tex)
        {
            Event evt = Event.current;
            if (evt == null || tex == null)
            {
                return;
            }
            if (evt.button == 1)
            {
                return;
            }
            if (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag && evt.type != EventType.MouseUp)
            {
                return;
            }
            if (!rect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.MouseUp && _dragging)
                {
                    _dragging = false;
                    evt.Use();
                }
                return;
            }
            float fx;
            float fy;
            if (!TexturePoint(rect, evt.mousePosition, tex, out fx, out fy))
            {
                if (evt.type == EventType.MouseUp)
                {
                    _dragging = false;
                }
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                _dragStart = new Vector2(fx, fy);
                _dragging = true;
                if (evt.shift)
                {
                    ResolvePoint(fx, fy, true, tex);
                    _dragging = false;
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                float dx = fx - _dragStart.x;
                float dy = fy - _dragStart.y;
                if (dx * dx + dy * dy > 16.0f)
                {
                    ResolveBox(_dragStart.x, _dragStart.y, fx, fy, tex);
                }
                else
                {
                    ResolvePoint(fx, fy, false, tex);
                }
                evt.Use();
            }
        }
        private void ToScreenPoint(float fx, float fy, Texture2D tex, out float sx, out float sy)
        {
            sx = fx;
            sy = tex.height - fy;
        }
        private bool TexturePoint(Rect view, Vector2 mouse, Texture2D tex, out float fx, out float fy)
        {
            fx = 0f;
            fy = 0f;
            if (tex == null || tex.width < 1 || tex.height < 1)
            {
                return false;
            }
            Rect local = FrameDrawRect(new Rect(0f, 0f, view.width, view.height), tex);
            Rect drawn = new Rect(view.x + local.x, view.y + local.y, local.width, local.height);
            Rect visible = Rect.MinMaxRect(
                Mathf.Max(drawn.xMin, view.xMin),
                Mathf.Max(drawn.yMin, view.yMin),
                Mathf.Min(drawn.xMax, view.xMax),
                Mathf.Min(drawn.yMax, view.yMax));
            if (visible.width < 1f || visible.height < 1f || !visible.Contains(mouse))
            {
                return false;
            }
            fx = (mouse.x - drawn.x) / drawn.width * tex.width;
            fy = (mouse.y - drawn.y) / drawn.height * tex.height;
            if (fx < 0f || fy < 0f || fx > tex.width || fy > tex.height)
            {
                return false;
            }
            return true;
        }
        private void ResolvePoint(float fx, float fy, bool negative, Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            float sx;
            float sy;
            ToScreenPoint(fx, fy, tex, out sx, out sy);
            StillWrongGroundingRequest request = new StillWrongGroundingRequest();
            request.Sentence = _sentence;
            if (negative)
            {
                request.NegativePoints.Add(new Vector2(sx, sy));
            }
            else
            {
                request.PositivePoints.Add(new Vector2(sx, sy));
            }
            FinishGround(request, tex, false, fx, fy, fx, fy, negative);
        }
        private void ResolveBox(float x1, float y1, float x2, float y2, Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            float ax;
            float ay;
            ToScreenPoint(x1, y1, tex, out ax, out ay);
            float bx;
            float by;
            ToScreenPoint(x2, y2, tex, out bx, out by);
            StillWrongGroundingRequest request = new StillWrongGroundingRequest();
            request.Sentence = _sentence;
            request.HasBox = true;
            request.Box = Rect.MinMaxRect(Mathf.Min(ax, bx), Mathf.Min(ay, by), Mathf.Max(ax, bx), Mathf.Max(ay, by));
            FinishGround(request, tex, true, x1, y1, x2, y2, false);
        }
        private void FinishGround(StillWrongGroundingRequest request, Texture2D tex, bool hasBox, float x1, float y1, float x2, float y2, bool pointNegative)
        {
            string framePath = (_thumbFiles != null && _selectedThumb >= 0 && _selectedThumb < _thumbFiles.Count) ? _thumbFiles[_selectedThumb] : string.Empty;
            StillWrong.Core.FrameObjectsSnapshot snapshot = StillWrong.Core.FrameObjects.ForFrameFile(framePath, 15);
            if (snapshot != null && snapshot.Elements.Count > 0 && !pointNegative)
            {
                StillWrongFrameGroundingOutcome outcome = StillWrongFrameGrounding.Run(tex, snapshot, hasBox, x1, y1, x2, y2);
                _groundNote = outcome.Note;
                SetMaskOverlay(outcome.Mask, framePath);
                if (outcome.Candidates.Count > 0)
                {
                    _candidates = outcome.Candidates;
                    _candidateIndex = 0;
                }
                StillWrongSamClick.SetReportMask(outcome.Mask);
                StillWrongSamClick.WriteReport(tex, outcome.AsResult(), outcome.ClickOnly);
                return;
            }
            StillWrongSamClick.Attach(request, tex, hasBox, x1, y1, x2, y2, pointNegative);
            bool[,] mask = request.SamMask;
            request.SamMask = null;
            StillWrongGroundingResult without = StillWrongGrounding.ActiveService.Ground(request);
            request.SamMask = mask;
            StillWrongGroundingResult result = mask == null ? without : StillWrongGrounding.ActiveService.Ground(request);
            StillWrongSamClick.WriteReport(tex, result, without);
            _groundNote = StillWrongSamClick.LastNote + (snapshot == null ? " No object snapshot for this frame, ranked against the live scene." : string.Empty);
            SetMaskOverlay(mask, framePath);
            if (result != null && result.UsedSamMask && result.Candidates != null)
            {
                result.Candidates.Sort(CompareCandidateCoverage);
            }
            if (result != null && result.Candidates != null && result.Candidates.Count > 0)
            {
                _candidates = result.Candidates;
                _candidateIndex = 0;
            }
        }
        private static int CompareCandidateCoverage(StillWrongGroundingCandidate a, StillWrongGroundingCandidate b)
        {
            double left = a == null ? 0d : a.MaskCoverage;
            double right = b == null ? 0d : b.MaskCoverage;
            return right.CompareTo(left);
        }
    }
}
