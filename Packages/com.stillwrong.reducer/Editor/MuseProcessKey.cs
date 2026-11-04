using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace StillWrong.Editor
{
    [InitializeOnLoad]
    static class MuseProcessKey
    {
        static MuseProcessKey()
        {
            if (Application.isBatchMode)
            {
                return;
            }
            string current = Environment.GetEnvironmentVariable("MODEL_API_KEY");
            if (string.IsNullOrEmpty(current))
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home))
                {
                    string path = Path.Combine(home, ".config", "muse", "meta-api-key");
                    if (File.Exists(path))
                    {
                        try
                        {
                            string key = File.ReadAllText(path);
                            if (!string.IsNullOrEmpty(key))
                            {
                                key = key.Trim();
                                if (key.Length > 0)
                                {
                                    Environment.SetEnvironmentVariable("MODEL_API_KEY", key);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            string now = Environment.GetEnvironmentVariable("MODEL_API_KEY");
            bool present = !string.IsNullOrEmpty(now);
            int length = now == null ? 0 : now.Length;
            try
            {
                File.WriteAllText("/tmp/sw-model-route.txt", "batch=false\npresent=" + (present ? "true" : "false") + "\nlength=" + length.ToString() + "\n");
            }
            catch
            {
            }
        }
    }
}
