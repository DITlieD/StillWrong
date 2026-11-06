using StillWrong.Recording;
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StillWrong.Editor
{
    [InitializeOnLoad]
    static class GameViewFrameGrab
    {
        static GameViewFrameGrab()
        {
            RollingCaptureRecorder.EditorFrameGrab = Grab;
            RollingCaptureRecorder.EditorFrameGrabReusesTexture = true;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.update -= KeepGameViewFullPixels;
                EditorApplication.update += KeepGameViewFullPixels;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= KeepGameViewFullPixels;
            }
        }

        static double _nextSizePass;

        static void KeepGameViewFullPixels()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextSizePass)
            {
                return;
            }
            _nextSizePass = now + 0.25;
            RollingCaptureRecorder recorder = UnityEngine.Object.FindAnyObjectByType<RollingCaptureRecorder>();
            if (recorder != null)
            {
                recorder.EditorPump();
            }
            try
            {
                EditorWindow view = FindGameView();
                if (view == null)
                {
                    return;
                }
                FieldInfo low = view.GetType().GetField("m_LowResolutionForAspectRatios", BindingFlags.Instance | BindingFlags.NonPublic);
                if (low == null)
                {
                    low = view.GetType().BaseType.GetField("m_LowResolutionForAspectRatios", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                if (low != null && low.GetValue(view) is bool on && on)
                {
                    low.SetValue(view, false);
                    view.Repaint();
                }
            }
            catch
            {
            }
        }

        static Texture2D _grab;

        static Texture2D Grab()
        {
            return ReadGameViewTarget();
        }

        static Texture2D ReadGameViewTarget()
        {
            try
            {
                EditorWindow view = FindGameView();
                if (view == null)
                {
                    return null;
                }
                RenderTexture rt = FindTarget(view);
                if (rt == null || rt.width < 2 || rt.height < 2)
                {
                    return null;
                }
                // One texture reused for every grab. ReadPixels fills its CPU copy; no GPU upload is needed
                // because the recorder only reads the pixels back.
                if (_grab == null || _grab.width != rt.width || _grab.height != rt.height)
                {
                    if (_grab != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_grab);
                    }
                    _grab = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                }
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                _grab.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0, false);
                RenderTexture.active = previous;
                return _grab;
            }
            catch
            {
                return null;
            }
        }

        static RenderTexture FindTarget(EditorWindow view)
        {
            Type type = view.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField("m_TargetTexture", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null && field.GetValue(view) is RenderTexture rt)
                {
                    return rt;
                }
                type = type.BaseType;
            }
            return null;
        }

        static EditorWindow FindGameView()
        {
            Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gvType == null)
            {
                return null;
            }
            UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(gvType);
            if (views == null || views.Length == 0)
            {
                return null;
            }
            return views[0] as EditorWindow;
        }
    }
}
