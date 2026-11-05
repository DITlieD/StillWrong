using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Inspection;
using UnityEngine;

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
                List<SpatialHitElement> found = UnitySpatialHitProvider.CollectAllSpatialElements();
                FrameObjectsSnapshot snapshot = new FrameObjectsSnapshot();
                snapshot.FrameNumber = frameNumber;
                snapshot.ScreenWidth = screenW;
                snapshot.ScreenHeight = screenH;
                for (int i = 0; i < found.Count && snapshot.Elements.Count < MaxObjects; i++)
                {
                    SpatialHitElement hit = found[i];
                    if (hit == null || !hit.IsActiveAndEnabled || hit.ScreenRect.Area < 4f)
                    {
                        continue;
                    }
                    Rect2D r = hit.ScreenRect;
                    if (r.XMax < 0f || r.YMax < 0f || r.XMin > screenW || r.YMin > screenH)
                    {
                        continue;
                    }
                    MaskElement element = new MaskElement();
                    element.Path = hit.HierarchyPath;
                    element.Rect = r;
                    element.DrawOrder = hit.DrawOrder;
                    element.Depth = hit.CameraDepth;
                    element.IsUi = !hit.IsRenderer3D;
                    snapshot.Elements.Add(element);
                }
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
    }
}
