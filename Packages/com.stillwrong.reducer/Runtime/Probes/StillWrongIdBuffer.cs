using System;
using System.Collections.Generic;
using StillWrong.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace StillWrong.Oracle
{
    public sealed class StillWrongIdMap
    {
        public int Width;
        public int Height;
        public int[] Ids = new int[0];
        public Dictionary<int, int> Counts = new Dictionary<int, int>();
        public bool IsAnalytic;
    }

    public sealed class StillWrongGraphicOverride
    {
        public Graphic Graphic;
        public Material SavedMaterial;
        public Color SavedColor;
    }

    public sealed class StillWrongRendererOverride
    {
        public Renderer Renderer;
        public Material[] SavedMaterials;
    }

    public static class StillWrongIdBuffer
    {
        private static Material _graphicIdMaterial;
        private static Dictionary<int, Material> _rendererIdMaterials = new Dictionary<int, Material>();
        private static Dictionary<string, int> _lastPathToId = new Dictionary<string, int>(StringComparer.Ordinal);
        private static StillWrongIdMap _lastIdMap;
        private static Color32[] _lastColorPixels = new Color32[0];
        private static int _lastColorWidth;
        private static int _lastColorHeight;
        private static int _ambientConsoleErrors;

        public static Dictionary<string, int> AssignIds(IList<string> paths)
        {
            List<string> unique = new List<string>();
            if (paths != null)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (!string.IsNullOrEmpty(paths[i]) && !unique.Contains(paths[i]))
                    {
                        unique.Add(paths[i]);
                    }
                }
            }
            unique.Sort(StringComparer.Ordinal);
            Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < unique.Count; i++)
            {
                ids[unique[i]] = i + 1;
            }
            return ids;
        }

        public static Color EncodeIdColor(int id)
        {
            int clamped = Math.Max(0, id) & 16777215;
            return new Color((clamped & 255) / 255f, ((clamped >> 8) & 255) / 255f, ((clamped >> 16) & 255) / 255f, 1f);
        }

        public static int DecodeIdColor(Color32 c)
        {
            return c.r | (c.g << 8) | (c.b << 16);
        }

        private static Material GraphicIdMaterial()
        {
            if (_graphicIdMaterial == null)
            {
                Shader shader = Shader.Find("UI/Default");
                _graphicIdMaterial = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            }
            return _graphicIdMaterial;
        }

        private static Material RendererIdMaterialFor(int id)
        {
            Material cached = null;
            if (_rendererIdMaterials.TryGetValue(id, out cached) && cached != null)
            {
                return cached;
            }
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("UI/Default");
            }
            Material created = new Material(shader);
            created.color = EncodeIdColor(id);
            _rendererIdMaterials[id] = created;
            return created;
        }

        public static Dictionary<Graphic, int> BuildGraphicAssignments(IReadOnlyDictionary<string, int> pathToId)
        {
            Dictionary<Graphic, int> assignments = new Dictionary<Graphic, int>();
            if (pathToId == null)
            {
                return assignments;
            }
            foreach (KeyValuePair<string, int> kv in pathToId)
            {
                GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(kv.Key);
                if (go == null || !go.activeInHierarchy)
                {
                    continue;
                }
                Graphic[] graphics = go.GetComponents<Graphic>();
                for (int i = 0; i < graphics.Length; i++)
                {
                    if (graphics[i] != null && graphics[i].enabled && !assignments.ContainsKey(graphics[i]))
                    {
                        assignments.Add(graphics[i], kv.Value);
                    }
                }
            }
            return assignments;
        }

        public static Dictionary<Renderer, int> BuildRendererAssignments(IReadOnlyDictionary<string, int> pathToId)
        {
            Dictionary<Renderer, int> assignments = new Dictionary<Renderer, int>();
            if (pathToId == null)
            {
                return assignments;
            }
            foreach (KeyValuePair<string, int> kv in pathToId)
            {
                GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(kv.Key);
                if (go == null || !go.activeInHierarchy)
                {
                    continue;
                }
                Renderer[] renderers = go.GetComponents<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled && !assignments.ContainsKey(renderers[i]))
                    {
                        assignments.Add(renderers[i], kv.Value);
                    }
                }
            }
            return assignments;
        }

        public static List<StillWrongGraphicOverride> BeginGraphicIds(Dictionary<Graphic, int> assignments)
        {
            List<StillWrongGraphicOverride> saved = new List<StillWrongGraphicOverride>();
            if (assignments == null)
            {
                return saved;
            }
            Material idMaterial = GraphicIdMaterial();
            foreach (KeyValuePair<Graphic, int> kv in assignments)
            {
                Graphic graphic = kv.Key;
                if (graphic == null)
                {
                    continue;
                }
                StillWrongGraphicOverride ov = new StillWrongGraphicOverride();
                ov.Graphic = graphic;
                ov.SavedMaterial = graphic.material;
                ov.SavedColor = graphic.color;
                saved.Add(ov);
                graphic.material = idMaterial;
                graphic.canvasRenderer.SetColor(EncodeIdColor(kv.Value));
            }
            return saved;
        }

        public static void EndGraphicIds(List<StillWrongGraphicOverride> saved)
        {
            if (saved == null)
            {
                return;
            }
            for (int i = 0; i < saved.Count; i++)
            {
                StillWrongGraphicOverride ov = saved[i];
                if (ov == null || ov.Graphic == null)
                {
                    continue;
                }
                ov.Graphic.material = ov.SavedMaterial;
                ov.Graphic.canvasRenderer.SetColor(ov.SavedColor);
            }
        }

        public static List<StillWrongRendererOverride> BeginRendererIds(Dictionary<Renderer, int> assignments)
        {
            List<StillWrongRendererOverride> saved = new List<StillWrongRendererOverride>();
            if (assignments == null)
            {
                return saved;
            }
            foreach (KeyValuePair<Renderer, int> kv in assignments)
            {
                Renderer renderer = kv.Key;
                if (renderer == null)
                {
                    continue;
                }
                StillWrongRendererOverride ov = new StillWrongRendererOverride();
                ov.Renderer = renderer;
                ov.SavedMaterials = renderer.sharedMaterials;
                saved.Add(ov);
                Material[] shared = renderer.sharedMaterials;
                Material[] ids = new Material[shared != null ? shared.Length : 1];
                for (int i = 0; i < ids.Length; i++)
                {
                    ids[i] = RendererIdMaterialFor(kv.Value);
                }
                renderer.sharedMaterials = ids;
            }
            return saved;
        }

        public static void EndRendererIds(List<StillWrongRendererOverride> saved)
        {
            if (saved == null)
            {
                return;
            }
            for (int i = 0; i < saved.Count; i++)
            {
                StillWrongRendererOverride ov = saved[i];
                if (ov == null || ov.Renderer == null)
                {
                    continue;
                }
                ov.Renderer.sharedMaterials = ov.SavedMaterials;
            }
        }

        public static Dictionary<string, Rect2D> CollectUidocumentRects(Camera camera)
        {
            Dictionary<string, Rect2D> rects = new Dictionary<string, Rect2D>(StringComparer.Ordinal);
            UnityEngine.Object[] found = new UnityEngine.Object[0];
            try
            {
                found = UnityEngine.Object.FindObjectsByType<UIDocument>();
            }
            catch
            {
                return rects;
            }
            for (int i = 0; i < found.Length; i++)
            {
                UIDocument doc = found[i] as UIDocument;
                if (doc == null)
                {
                    continue;
                }
                GameObject go = doc.gameObject;
                if (go == null || !go.activeInHierarchy || !doc.enabled)
                {
                    continue;
                }
                string path = UnitySymptomOracleRunner.GetHierarchyPath(go);
                if (!rects.ContainsKey(path))
                {
                    rects.Add(path, UnitySymptomOracleRunner.ComputeScreenRect(go, camera != null ? camera : Camera.main));
                }
            }
            return rects;
        }

        public static StillWrongIdMap DecodeFrame(Texture2D frame)
        {
            if (frame == null)
            {
                return null;
            }
            Color32[] pixels = null;
            try
            {
                pixels = frame.GetPixels32();
            }
            catch
            {
                return null;
            }
            if (pixels == null || pixels.Length == 0)
            {
                return null;
            }
            StillWrongIdMap map = new StillWrongIdMap();
            map.Width = frame.width;
            map.Height = frame.height;
            map.Ids = new int[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                int id = DecodeIdColor(pixels[i]);
                map.Ids[i] = id;
                int count = 0;
                map.Counts.TryGetValue(id, out count);
                map.Counts[id] = count + 1;
            }
            return map;
        }

        public static bool TestCulledByCamera(Renderer renderer, Camera camera)
        {
            if (renderer == null || camera == null)
            {
                return false;
            }
            if (!renderer.gameObject.activeInHierarchy || !renderer.enabled)
            {
                return false;
            }
            if (((1 << renderer.gameObject.layer) & camera.cullingMask) == 0)
            {
                return true;
            }
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return !GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }

        public static void SetRegistryMap(Dictionary<string, int> pathToId)
        {
            _lastPathToId = pathToId != null ? pathToId : new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public static IReadOnlyDictionary<string, int> RegistryMap
        {
            get { return _lastPathToId; }
        }

        public static bool TryGetIdForPath(string path, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return _lastPathToId.TryGetValue(path, out id);
        }

        public static void SetIdMap(StillWrongIdMap map)
        {
            _lastIdMap = map;
        }

        public static StillWrongIdMap LastIdMap
        {
            get { return _lastIdMap; }
        }

        public static void SetColorFrame(Texture2D frame)
        {
            if (frame == null)
            {
                _lastColorPixels = new Color32[0];
                _lastColorWidth = 0;
                _lastColorHeight = 0;
                return;
            }
            try
            {
                _lastColorPixels = frame.GetPixels32();
                _lastColorWidth = frame.width;
                _lastColorHeight = frame.height;
            }
            catch
            {
                _lastColorPixels = new Color32[0];
                _lastColorWidth = 0;
                _lastColorHeight = 0;
            }
        }

        public static bool TryGetIdCount(int id, out int count)
        {
            count = 0;
            if (_lastIdMap == null || _lastIdMap.Counts == null)
            {
                return false;
            }
            _lastIdMap.Counts.TryGetValue(id, out count);
            return true;
        }

        public static bool TryGetIdFraction(int id, double expectedAreaPx, out double fraction)
        {
            fraction = 0.0;
            if (_lastIdMap == null || _lastIdMap.Ids == null || _lastIdMap.Ids.Length == 0)
            {
                return false;
            }
            if (expectedAreaPx <= 0.0)
            {
                return false;
            }
            int count = 0;
            _lastIdMap.Counts.TryGetValue(id, out count);
            fraction = Math.Min(1.0, count / expectedAreaPx);
            return true;
        }

        public static bool TryMeanColor(Rect2D regionPx, out Color color)
        {
            color = new Color(0f, 0f, 0f, 1f);
            if (_lastColorPixels == null || _lastColorPixels.Length == 0)
            {
                return false;
            }
            int x0 = Math.Max(0, Mathf.FloorToInt(regionPx.XMin));
            int y0 = Math.Max(0, Mathf.FloorToInt(regionPx.YMin));
            int x1 = Math.Min(_lastColorWidth - 1, Mathf.CeilToInt(regionPx.XMax) - 1);
            int y1 = Math.Min(_lastColorHeight - 1, Mathf.CeilToInt(regionPx.YMax) - 1);
            if (x1 < x0 || y1 < y0)
            {
                return false;
            }
            double r = 0.0;
            double g = 0.0;
            double b = 0.0;
            long n = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Color32 px = _lastColorPixels[y * _lastColorWidth + x];
                    r += px.r / 255.0;
                    g += px.g / 255.0;
                    b += px.b / 255.0;
                    n++;
                }
            }
            if (n == 0)
            {
                return false;
            }
            color = new Color((float)(r / n), (float)(g / n), (float)(b / n), 1f);
            return true;
        }

        public static void SetAmbientConsoleErrors(int count)
        {
            _ambientConsoleErrors = count;
        }

        public static int AmbientConsoleErrors
        {
            get { return _ambientConsoleErrors; }
        }
    }
}

