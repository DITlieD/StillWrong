using System;
using System.Collections.Generic;

namespace StillWrong.Recording
{
    [Serializable]
    public sealed class SessionManifest
    {
        public string Id;
        public string UnityVersion;
        public string SceneName;
        public string ScenePath;
        public int FrameWidth;
        public int FrameHeight;
        public int GridRate;
        public string StartRandomState;
        public int StartFrame;
        public int FrameCount;
        public long JpegTotalBytes;
        public double MeanJpegBytes;
        public double MeanEncodeMs;
        public double MeanRegistryMs;
        public double MeanCaptureMs;
        public double MeanReadbackMs;
        public long ReadbackDrops;
        public long Overwrites;
        public int InputEventCount;
        public int RegistryElementCount;
        public long MarkedPngBytes;
        public long FolderBytes;
    }

    [Serializable]
    public sealed class FrameIndexEntry
    {
        public int Frame;
        public double Timestamp;
        public double UnscaledTime;
        public float TimeScale;
        public bool Dropped;
        public string JpegFile;
        public double EncodeMs;
        public double RegistryMs;
        public int RegistryCount;
    }

    [Serializable]
    public sealed class FrameIndexFile
    {
        public List<FrameIndexEntry> Frames = new List<FrameIndexEntry>();
    }

    [Serializable]
    public sealed class RegistryEntry
    {
        public string Path;
        public string GoId;
        public int InstanceId;
        public string Type;
        public string Space;
        public float RectMinX;
        public float RectMinY;
        public float RectMaxX;
        public float RectMaxY;
        public float Alpha;
        public int SortOrder;
        public int SiblingIndex;
        public int Layer;
        public bool Active;
    }

    [Serializable]
    public sealed class RegistryFrame
    {
        public int Frame;
        public List<RegistryEntry> Elements = new List<RegistryEntry>();
    }

    [Serializable]
    public sealed class RegistryFile
    {
        public List<RegistryFrame> Frames = new List<RegistryFrame>();
    }

    [Serializable]
    public sealed class InputEventRecord
    {
        public int Frame;
        public string Path;
        public float PointerX;
        public float PointerY;
        public int ButtonDownMask;
        public int ButtonUpMask;
        public float ScrollX;
        public float ScrollY;
        public List<string> AxisNames = new List<string>();
        public List<float> AxisValues = new List<float>();
        public int DeviceId;
        public string Layout;
        public string EventType;
        public string StateBase64;
        public int StateSize;
    }

    [Serializable]
    public sealed class InputEventFile
    {
        public List<InputEventRecord> Events = new List<InputEventRecord>();
    }

    [Serializable]
    public sealed class SceneEventEntry
    {
        public int Frame;
        public string Kind;
        public string SceneName;
        public string ScenePath;
    }

    [Serializable]
    public sealed class SceneEventFile
    {
        public List<SceneEventEntry> Events = new List<SceneEventEntry>();
    }
}
