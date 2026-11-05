using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using StillWrong.Oracle;
using UnityEngine;

namespace StillWrong.Editor.UI
{
    public static class StillWrongFlatIdRender
    {
        private const string ProxyMarker = "Occlusion Shape Proxy";
        private const string ShaderName = "Hidden/StillWrong/FlatId";

        public static string Capture(Camera camera, int width, int height)
        {
            if (camera == null || width < 1 || height < 1)
            {
                return "ID render skipped: no camera.";
            }
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                return "ID render skipped: shader missing.";
            }
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            string[] owners = new string[renderers.Length];
            List<string> paths = new List<string>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }
                owners[i] = OwnerPath(r.gameObject);
                paths.Add(owners[i]);
            }
            Dictionary<string, int> pathToId = StillWrongIdBuffer.AssignIds(paths);
            Material[][] saved = new Material[renderers.Length][];
            Dictionary<string, Material> created = new Dictionary<string, Material>(StringComparer.Ordinal);
            CameraState state = CameraState.Save(camera);
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D read = null;
            StillWrongIdMap map = null;
            try
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (owners[i] == null)
                    {
                        continue;
                    }
                    Renderer r = renderers[i];
                    int id = pathToId[owners[i]];
                    Material[] shared = r.sharedMaterials;
                    saved[i] = shared;
                    bool sprite = r is SpriteRenderer;
                    int count = Math.Max(1, shared != null ? shared.Length : 0);
                    Material[] swap = new Material[count];
                    for (int k = 0; k < count; k++)
                    {
                        Material original = shared != null && k < shared.Length ? shared[k] : null;
                        swap[k] = MaterialFor(shader, id, sprite ? null : CutoutTexture(original), sprite, created);
                    }
                    r.sharedMaterials = swap;
                }
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                target.antiAliasing = 1;
                target.Create();
                camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                state.DisablePipelineExtras(camera);
                camera.Render();
                RenderTexture.active = target;
                read = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                read.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                read.Apply(false);
                map = StillWrongIdBuffer.DecodeFrame(read);
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (saved[i] != null && renderers[i] != null)
                    {
                        renderers[i].sharedMaterials = saved[i];
                    }
                }
                state.Restore(camera);
                RenderTexture.active = previous;
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (read != null)
                {
                    UnityEngine.Object.DestroyImmediate(read);
                }
                foreach (Material m in created.Values)
                {
                    UnityEngine.Object.DestroyImmediate(m);
                }
            }
            if (map == null)
            {
                return "ID render failed.";
            }
            int stray = 0;
            foreach (KeyValuePair<int, int> kv in map.Counts)
            {
                if (kv.Key != 0 && kv.Key > pathToId.Count)
                {
                    stray += kv.Value;
                }
            }
            StillWrongIdBuffer.SetIdMap(map);
            StillWrongIdBuffer.SetRegistryMap(pathToId);
            return "ID map " + pathToId.Count.ToString(CultureInfo.InvariantCulture) + " ids, " + map.Counts.Count.ToString(CultureInfo.InvariantCulture) + " colours, " + stray.ToString(CultureInfo.InvariantCulture) + " stray px.";
        }

        public static string OwnerPath(GameObject go)
        {
            Transform owner = go.transform;
            if (go.name.IndexOf(ProxyMarker, StringComparison.Ordinal) >= 0 && owner.parent != null)
            {
                owner = owner.parent;
            }
            return UnitySymptomOracleRunner.GetHierarchyPath(owner.gameObject);
        }

        private static Texture CutoutTexture(Material material)
        {
            if (material == null || material.renderQueue < 2450 || material.renderQueue >= 3000)
            {
                return null;
            }
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
            {
                return material.GetTexture("_BaseMap");
            }
            if (material.HasProperty("_MainTex"))
            {
                return material.GetTexture("_MainTex");
            }
            return null;
        }

        private static Material MaterialFor(Shader shader, int id, Texture texture, bool sprite, Dictionary<string, Material> created)
        {
            string key = id.ToString(CultureInfo.InvariantCulture) + "/" + (texture != null ? texture.GetEntityId().GetHashCode().ToString(CultureInfo.InvariantCulture) : "0") + (sprite ? "/s" : "/m");
            Material material;
            if (created.TryGetValue(key, out material))
            {
                return material;
            }
            material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.renderQueue = 2000;
            Color c = StillWrongIdBuffer.EncodeIdColor(id);
            material.SetVector("_IdColor", new Vector4(c.r, c.g, c.b, 1f));
            material.SetFloat("_VertexAlpha", sprite ? 1f : 0f);
            if (texture != null)
            {
                material.SetTexture("_MainTex", texture);
            }
            created.Add(key, material);
            return material;
        }

        private sealed class CameraState
        {
            private RenderTexture _target;
            private CameraClearFlags _clear;
            private Color _background;
            private bool _hdr;
            private bool _msaa;
            private Component _pipelineData;
            private readonly Dictionary<string, object> _pipelineValues = new Dictionary<string, object>(StringComparer.Ordinal);
            private static readonly string[] PipelineProperties = { "renderPostProcessing", "antialiasing", "dithering" };

            public static CameraState Save(Camera camera)
            {
                CameraState state = new CameraState();
                state._target = camera.targetTexture;
                state._clear = camera.clearFlags;
                state._background = camera.backgroundColor;
                state._hdr = camera.allowHDR;
                state._msaa = camera.allowMSAA;
                state._pipelineData = camera.GetComponent("UniversalAdditionalCameraData");
                if (state._pipelineData != null)
                {
                    for (int i = 0; i < PipelineProperties.Length; i++)
                    {
                        PropertyInfo property = state._pipelineData.GetType().GetProperty(PipelineProperties[i]);
                        if (property != null && property.CanRead && property.CanWrite)
                        {
                            state._pipelineValues[PipelineProperties[i]] = property.GetValue(state._pipelineData);
                        }
                    }
                }
                return state;
            }

            public void DisablePipelineExtras(Camera camera)
            {
                if (_pipelineData == null)
                {
                    return;
                }
                foreach (string name in _pipelineValues.Keys)
                {
                    PropertyInfo property = _pipelineData.GetType().GetProperty(name);
                    object off = property.PropertyType == typeof(bool) ? (object)false : Enum.ToObject(property.PropertyType, 0);
                    property.SetValue(_pipelineData, off);
                }
            }

            public void Restore(Camera camera)
            {
                camera.targetTexture = _target;
                camera.clearFlags = _clear;
                camera.backgroundColor = _background;
                camera.allowHDR = _hdr;
                camera.allowMSAA = _msaa;
                if (_pipelineData == null)
                {
                    return;
                }
                foreach (KeyValuePair<string, object> kv in _pipelineValues)
                {
                    _pipelineData.GetType().GetProperty(kv.Key).SetValue(_pipelineData, kv.Value);
                }
            }
        }
    }
}
