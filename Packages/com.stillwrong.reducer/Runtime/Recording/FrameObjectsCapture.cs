using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Inspection;
using UnityEngine;
using UnityEngine.UI;

namespace StillWrong.Recording
{
    // Saves what was on screen next to a recorded frame, so a SAM mask on that frame
    // is matched to the objects that were there at that moment, not to the scene as it is now.
    public static class FrameObjectsCapture
    {
        public const int MaxObjects = 800;

        // Appends one snapshot to the chunk's objects file. Returns the bytes written.
        public static long Write(string chunkDir, int frameNumber)
        {
            try
            {
                int screenW = Screen.width;
                int screenH = Screen.height;
                if (screenW <= 1 || screenH <= 1 || string.IsNullOrEmpty(chunkDir))
                {
                    return 0;
                }
                FrameObjectsSnapshot snapshot = new FrameObjectsSnapshot();
                snapshot.FrameNumber = frameNumber;
                snapshot.ScreenWidth = screenW;
                snapshot.ScreenHeight = screenH;
                CollectOnScreen(snapshot, Camera.main, screenW, screenH);
                string text = FrameObjects.Format(snapshot);
                Directory.CreateDirectory(chunkDir);
                File.AppendAllText(Path.Combine(chunkDir, FrameObjects.FileName), text);
                return text.Length;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private static readonly Vector3[] Corners4 = new Vector3[4];

        // Cheap pass for snapshots: skip hidden objects before any string or material work,
        // and build hierarchy paths only for objects that land on screen.
        private static void CollectOnScreen(FrameObjectsSnapshot snapshot, Camera camera, int screenW, int screenH)
        {
            Graphic[] graphics = UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None);
            for (int i = 0; i < graphics.Length && snapshot.Elements.Count < MaxObjects; i++)
            {
                Graphic g = graphics[i];
                if (g == null || !g.enabled || !g.gameObject.activeInHierarchy) continue;
                Canvas canvas = g.canvas;
                if (canvas == null) continue;
                Camera eventCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : (canvas.worldCamera != null ? canvas.worldCamera : camera);
                RectTransform rt = g.rectTransform;
                rt.GetWorldCorners(Corners4);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    Vector2 sp = eventCam != null ? RectTransformUtility.WorldToScreenPoint(eventCam, Corners4[c]) : new Vector2(Corners4[c].x, Corners4[c].y);
                    minX = Mathf.Min(minX, sp.x); minY = Mathf.Min(minY, sp.y);
                    maxX = Mathf.Max(maxX, sp.x); maxY = Mathf.Max(maxY, sp.y);
                }
                Rect2D r = Rect2D.FromMinMax(minX, minY, maxX, maxY);
                if (!OnScreen(r, screenW, screenH)) continue;
                MaskElement e = new MaskElement();
                e.Path = UnitySpatialHitProvider.GetHierarchyPath(g.gameObject);
                e.Rect = r;
                e.DrawOrder = SpatialHitResolver.ComputeDrawOrder(canvas.sortingOrder, rt.GetSiblingIndex());
                e.Depth = eventCam != null ? (rt.position - eventCam.transform.position).magnitude : 0f;
                e.IsUi = true;
                snapshot.Elements.Add(e);
            }

            if (camera == null) return;
            Vector3 camPos = camera.transform.position;
            Vector3 camFwd = camera.transform.forward;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length && snapshot.Elements.Count < MaxObjects; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null || !rend.enabled || !rend.isVisible || !rend.gameObject.activeInHierarchy) continue;
                Bounds b = rend.bounds;
                Vector3 c0 = b.center, ex = b.extents;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                int valid = 0;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = new Vector3(
                        (k & 1) == 0 ? c0.x - ex.x : c0.x + ex.x,
                        (k & 2) == 0 ? c0.y - ex.y : c0.y + ex.y,
                        (k & 4) == 0 ? c0.z - ex.z : c0.z + ex.z);
                    Vector3 sp = camera.WorldToScreenPoint(corner);
                    if (sp.z <= 0f) continue;
                    valid++;
                    minX = Mathf.Min(minX, sp.x); minY = Mathf.Min(minY, sp.y);
                    maxX = Mathf.Max(maxX, sp.x); maxY = Mathf.Max(maxY, sp.y);
                }
                if (valid == 0) continue;
                Rect2D r = Rect2D.FromMinMax(minX, minY, maxX, maxY);
                if (!OnScreen(r, screenW, screenH)) continue;
                Transform t = rend.transform;
                MaskElement e = new MaskElement();
                e.Path = UnitySpatialHitProvider.GetHierarchyPath(rend.gameObject);
                e.Rect = r;
                e.DrawOrder = SpatialHitResolver.ComputeDrawOrder(rend.sortingOrder, t.GetSiblingIndex());
                e.Depth = Vector3.Dot(t.position - camPos, camFwd);
                e.IsUi = false;
                snapshot.Elements.Add(e);
            }
        }

        private static bool OnScreen(Rect2D r, int w, int h)
        {
            return r.Area >= 4f && !(r.XMax < 0f || r.YMax < 0f || r.XMin > w || r.YMin > h);
        }
    }
}
