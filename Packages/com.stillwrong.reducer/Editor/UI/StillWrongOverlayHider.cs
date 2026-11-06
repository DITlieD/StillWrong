using System;
using System.Collections.Generic;
using UnityEngine;

namespace StillWrong.Editor.UI
{
    // Hides HUD objects named in Settings > "Hide while reviewing", so they do not cover the frame
    // the user clicks on. One scan over the loaded scene objects per call.
    public static class StillWrongOverlayHider
    {
        public static void Hide(string names)
        {
            if (string.IsNullOrEmpty(names))
            {
                return;
            }
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
            string[] parts = names.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length > 0)
                {
                    wanted.Add(name);
                }
            }
            if (wanted.Count == 0)
            {
                return;
            }
            Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i] != null ? all[i].gameObject : null;
                if (go != null && go.activeSelf && wanted.Contains(go.name))
                {
                    go.SetActive(false);
                }
            }
        }
    }
}
