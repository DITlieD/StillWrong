using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using UnityEngine;
using UnityEngine.Rendering;
namespace StillWrong.Recording
{
    [Serializable]
    public sealed class RollingChunkManifest
    {
        public int ChunkIndex;
        public double StartTimeSeconds;
        public double EndTimeSeconds;
        public int Frames;
        public int Dropped;
        public long Bytes;
    }
    [Serializable]
    public sealed class RollingInputRecord
    {
        public int Frame;
        public int FrameNumber;
        public float PointerX;
        public float PointerY;
        public int ButtonMask;
        public double ClockSeconds;
    }
    [Serializable]
    public sealed class RollingInputsFile
    {
        public List<RollingInputRecord> Events = new List<RollingInputRecord>();
    }
    [Serializable]
    public sealed class RollingStatusFile
    {
        public string State = string.Empty;
        public string Reason = string.Empty;
        public string SessionId = string.Empty;
        public double RetainedSeconds;
        public int ChunkCount;
        public long TotalBytes;
        public long DroppedFrames;
        public int CapturedFrames;
        public double CaptureMsTotal;
        public double EncodeMsTotal;
        public double FrameMsTotal;
        public int FramesTimed;
        public int ChunksDeleted;
        public string LastRetentionRefusal = string.Empty;
        public string FrozenSession = string.Empty;
    }
    public sealed class RollingCaptureRecorder : MonoBehaviour
    {
        public static int TimelineWidth = 1920;
        public static int TimelineHeight = 1080;
        public const int JpegQuality = 90;
        public static System.Func<Texture2D> EditorFrameGrab;
        public const double FramesPerSecond = 30.0;
        public const double ChunkSeconds = 10.0;
        public const double RetentionTickSeconds = 5.0;
        public const int ObjectSnapshotEvery = 5;
        public string SessionId = string.Empty;
        private bool _capturing;
        private bool _frozen;
        private double _windowSeconds = 180.0;
        private double _timelineFps = 30.0;
        private string _sessionDir = string.Empty;
        private string _assertedSessionDir = string.Empty;
        private double _clock;
        private double _lastCaptureReal;
        private double _lastTickReal;
        private double _lastThrottleReal;
        private double _loopBeat;
        private double _chunkStart;
        private int _chunkIndex;
        private int _chunkFrames;
        private int _chunkDropped;
        private long _chunkBytes;
        private List<RollingInputRecord> _chunkInputs = new List<RollingInputRecord>();
        private RenderTexture _captureFull;
        private RenderTexture _captureSmall;
        private Texture2D _encodeTexture;
        private bool _readbackPending;
        private RollingFramePending _pending;
        private long _capturedFrames;
        private long _droppedFrames;
        private double _captureMsTotal;
        private double _encodeMsTotal;
        private double _frameMsTotal;
        private int _framesTimed;
        private int _chunksDeleted;
        private string _lastRefusal = string.Empty;
        private string _refusalReason = string.Empty;
        private long _bytesWritten;
        private sealed class RollingFramePending
        {
            public int FrameIndex;
            public double CaptureMs;
            public int ChunkIndex;
            public int FrameNumber;
            public float PointerX;
            public float PointerY;
            public int ButtonMask;
            public double ClockSeconds;
        }
        public static string SessionsRoot()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "StillWrong", "sessions");
        }
        public static string RecorderDir()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "StillWrong", "recorder");
        }
        public static string AssertSessionDir(string sessionsRoot, string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionsRoot) || string.IsNullOrEmpty(sessionId))
                {
                    return null;
                }
                if (sessionId.IndexOf(Path.DirectorySeparatorChar) >= 0 || sessionId.IndexOf(Path.AltDirectorySeparatorChar) >= 0 || sessionId.IndexOf("..") >= 0 || Path.IsPathRooted(sessionId))
                {
                    return null;
                }
                string rootFull = Path.GetFullPath(sessionsRoot);
                string full = Path.GetFullPath(Path.Combine(rootFull, sessionId));
                string prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return null;
                }
                return full;
            }
            catch
            {
                return null;
            }
        }
        public void Begin(string sessionId)
        {
            Begin(sessionId, 180.0, 30.0);
        }
        public string GetSessionDir()
        {
            return _sessionDir;
        }
        public void Begin(string sessionId, double windowSeconds, double timelineFps)
        {
            if (_capturing)
            {
                return;
            }
            if (windowSeconds >= 60.0 && windowSeconds <= 600.0)
            {
                _windowSeconds = windowSeconds;
            }
            if (timelineFps >= 1.0 && timelineFps <= 60.0)
            {
                _timelineFps = timelineFps;
            }
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = "s-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            }
            SessionId = sessionId;
            string root = SessionsRoot();
            _assertedSessionDir = Path.GetFullPath(Path.Combine(root, sessionId));
            _sessionDir = AssertSessionDir(root, sessionId);
            if (string.IsNullOrEmpty(_sessionDir))
            {
                _refusalReason = "bad-session-id";
                WriteStatus();
                return;
            }
            try
            {
                Directory.CreateDirectory(_sessionDir);
            }
            catch
            {
                _refusalReason = "session-dir-unwritable";
                _sessionDir = string.Empty;
                WriteStatus();
                return;
            }
            _capturing = true;
            _clock = 0.0;
            _lastCaptureReal = Time.realtimeSinceStartupAsDouble;
            _lastTickReal = Time.realtimeSinceStartupAsDouble;
            _chunkStart = 0.0;
            _chunkIndex = 0;
            _chunkFrames = 0;
            _chunkDropped = 0;
            _chunkBytes = 0;
            OpenChunkDir();
            WriteStatus();
            StartCoroutine(CaptureLoop());
        }
        public void FreezeCapture()
        {
            _frozen = true;
            WriteStatus();
        }
        public void UnfreezeCapture()
        {
            _frozen = false;
            _lastCaptureReal = Time.realtimeSinceStartupAsDouble;
            _lastTickReal = Time.realtimeSinceStartupAsDouble;
            WriteStatus();
        }
        public bool IsFrozen()
        {
            return _frozen;
        }
        public bool IsCapturing()
        {
            return _capturing && string.IsNullOrEmpty(_refusalReason);
        }
        private void OnDestroy()
        {
            try
            {
                CloseChunk();
                _capturing = false;
            }
            catch
            {
            }
            WriteStatus();
            ReleaseCaptureTarget(ref _captureFull);
            ReleaseCaptureTarget(ref _captureSmall);
        }

        public void EditorPump()
        {
            if (!_capturing || _frozen || string.IsNullOrEmpty(_sessionDir))
            {
                return;
            }
            double nowReal = Time.realtimeSinceStartupAsDouble;
            if (nowReal - _loopBeat < 0.4)
            {
                return;
            }
            _loopBeat = nowReal;
            if (nowReal - _lastThrottleReal < 1.0 / _timelineFps)
            {
                return;
            }
            _lastThrottleReal = nowReal;
            CaptureOneFrame();
            WriteStatus();
        }
        private IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return null;
                _loopBeat = Time.realtimeSinceStartupAsDouble;
                if (!_capturing || string.IsNullOrEmpty(_sessionDir))
                {
                    continue;
                }
                if (_frozen)
                {
                    continue;
                }
                double nowReal = Time.realtimeSinceStartupAsDouble;
                _clock += nowReal - _lastCaptureReal;
                _lastCaptureReal = nowReal;
                _frameMsTotal += Time.unscaledDeltaTime * 1000.0;
                _framesTimed++;
                if (_clock - _chunkStart >= ChunkSeconds)
                {
                    CloseChunk();
                    _chunkIndex++;
                    _chunkStart = _clock;
                    _chunkFrames = 0;
                    _chunkDropped = 0;
                    _chunkBytes = 0;
                    OpenChunkDir();
                }
                if (nowReal - _lastThrottleReal < 1.0 / _timelineFps)
                {
                    continue;
                }
                _lastThrottleReal = nowReal;
                CaptureOneFrame();
                if (nowReal - _lastTickReal >= RetentionTickSeconds)
                {
                    _lastTickReal = nowReal;
                    ApplyRetention();
                    WriteStatus();
                }
            }
        }
        private void CaptureOneFrame()
        {
            if (_readbackPending)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            Texture2D shot = GrabPresentedFrame();
            if (shot == null)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            RollingFramePending pending = new RollingFramePending();
            pending.FrameIndex = Time.frameCount;
            pending.ChunkIndex = _chunkIndex;
            pending.FrameNumber = _chunkFrames;
            pending.ClockSeconds = _clock;
            Vector2 pointer = CurrentPointer();
            pending.PointerX = pointer.x;
            pending.PointerY = pointer.y;
            pending.ButtonMask = CurrentButtonMask();
            System.Diagnostics.Stopwatch captureClock = System.Diagnostics.Stopwatch.StartNew();
            captureClock.Stop();
            pending.CaptureMs = captureClock.Elapsed.TotalMilliseconds;
            _captureMsTotal += pending.CaptureMs;
            if (pending.FrameNumber % ObjectSnapshotEvery == 0)
            {
                _bytesWritten += FrameObjectsCapture.Write(ChunkDir(pending.ChunkIndex), pending.FrameNumber);
            }
            StorePresentedFrame(shot, pending);
            Destroy(shot);
        }
        private Texture2D GrabPresentedFrame()
        {
            if (EditorFrameGrab != null)
            {
                try
                {
                    return EditorFrameGrab();
                }
                catch
                {
                    return null;
                }
            }
            try
            {
                return ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch
            {
                return null;
            }
        }
        private void StorePresentedFrame(Texture2D shot, RollingFramePending pending)
        {
            if (shot == null || shot.width <= 1 || shot.height <= 1)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            TimelineWidth = shot.width;
            TimelineHeight = shot.height;
            byte[] jpeg = null;
            try
            {
                jpeg = ImageConversion.EncodeToJPG(shot, JpegQuality);
            }
            catch
            {
                jpeg = null;
            }
            if (jpeg == null || jpeg.Length == 0)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            _capturedFrames++;
            _chunkFrames++;
            RollingInputRecord rec = new RollingInputRecord();
            rec.Frame = pending.FrameIndex;
            rec.FrameNumber = pending.FrameNumber;
            rec.PointerX = pending.PointerX;
            rec.PointerY = pending.PointerY;
            rec.ButtonMask = pending.ButtonMask;
            rec.ClockSeconds = pending.ClockSeconds;
            _chunkInputs.Add(rec);
            try
            {
                string dir = ChunkDir(pending.ChunkIndex);
                string file = Path.Combine(dir, "frames", "f-" + pending.FrameNumber.ToString("D6") + ".jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, jpeg);
                _chunkBytes += jpeg.Length;
                _bytesWritten += jpeg.Length;
            }
            catch
            {
            }
        }
        private static Vector2 CurrentPointer()
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                return mouse.position.ReadValue();
            }
            return Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
        private static bool CurrentButton(int button)
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            if (button == 0)
            {
                return mouse.leftButton.isPressed;
            }
            if (button == 1)
            {
                return mouse.rightButton.isPressed;
            }
            return mouse.middleButton.isPressed;
#else
            return Input.GetMouseButton(button);
#endif
        }
        private static bool CurrentButtonDown(int button)
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            if (button == 0)
            {
                return mouse.leftButton.wasPressedThisFrame;
            }
            if (button == 1)
            {
                return mouse.rightButton.wasPressedThisFrame;
            }
            return mouse.middleButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }
        private int CurrentButtonMask()
        {
            int mask = 0;
            if (CurrentButton(0))
            {
                mask = mask + 1;
            }
            if (CurrentButton(1))
            {
                mask = mask + 2;
            }
            if (CurrentButton(2))
            {
                mask = mask + 4;
            }
            return mask;
        }
        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            if (_pending == null)
            {
                return;
            }
            RollingFramePending pending = _pending;
            _pending = null;
            if (request.hasError)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            var data = request.GetData<byte>();
            int expected = TimelineWidth * TimelineHeight * 3;
            if (data.Length != expected)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            System.Diagnostics.Stopwatch encodeClock = System.Diagnostics.Stopwatch.StartNew();
            byte[] jpeg = null;
            try
            {
                if (_encodeTexture == null)
                {
                    _encodeTexture = new Texture2D(TimelineWidth, TimelineHeight, TextureFormat.RGB24, false);
                }
                byte[] buffer = new byte[expected];
                data.CopyTo(buffer);
                _encodeTexture.LoadRawTextureData(buffer);
                _encodeTexture.Apply();
                jpeg = ImageConversion.EncodeToJPG(_encodeTexture, JpegQuality);
            }
            catch (Exception)
            {
                jpeg = null;
            }
            encodeClock.Stop();
            if (jpeg == null || jpeg.Length == 0)
            {
                _droppedFrames++;
                _chunkDropped++;
                return;
            }
            _encodeMsTotal += encodeClock.Elapsed.TotalMilliseconds;
            _capturedFrames++;
            _chunkFrames++;
            RollingInputRecord rec = new RollingInputRecord();
            rec.Frame = pending.FrameIndex;
            rec.FrameNumber = pending.FrameNumber;
            rec.PointerX = pending.PointerX;
            rec.PointerY = pending.PointerY;
            rec.ButtonMask = pending.ButtonMask;
            rec.ClockSeconds = pending.ClockSeconds;
            _chunkInputs.Add(rec);
            try
            {
                string dir = ChunkDir(pending.ChunkIndex);
                string file = Path.Combine(dir, "frames", "f-" + pending.FrameNumber.ToString("D6") + ".jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, jpeg);
                _chunkBytes += jpeg.Length;
                _bytesWritten += jpeg.Length;
            }
            catch
            {
            }
        }
        private string ChunkDir(int index)
        {
            return Path.Combine(_sessionDir, "chunk-" + index.ToString("D5"));
        }
        private void OpenChunkDir()
        {
            try
            {
                string dir = ChunkDir(_chunkIndex);
                Directory.CreateDirectory(Path.Combine(dir, "frames"));
            }
            catch
            {
            }
        }
        private void CloseChunk()
        {
            if (string.IsNullOrEmpty(_sessionDir) || !_capturing)
            {
                return;
            }
            try
            {
                string dir = ChunkDir(_chunkIndex);
                RollingInputsFile inputs = new RollingInputsFile();
                inputs.Events = _chunkInputs;
                string inputsJson = JsonUtility.ToJson(inputs);
                File.WriteAllText(Path.Combine(dir, "inputs.json"), inputsJson);
                _bytesWritten += inputsJson.Length;
                RollingChunkManifest manifest = new RollingChunkManifest();
                manifest.ChunkIndex = _chunkIndex;
                manifest.StartTimeSeconds = _chunkStart;
                manifest.EndTimeSeconds = _clock;
                manifest.Frames = _chunkFrames;
                manifest.Dropped = _chunkDropped;
                manifest.Bytes = _chunkBytes;
                string manifestJson = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(dir, "manifest.json"), manifestJson);
                _bytesWritten += manifestJson.Length;
                _chunkInputs = new List<RollingInputRecord>();
            }
            catch
            {
            }
        }
        private void ApplyRetention()
        {
            if (string.IsNullOrEmpty(_sessionDir))
            {
                return;
            }
            List<RetentionChunk> infos = new List<RetentionChunk>();
            try
            {
                string[] dirs = Directory.GetDirectories(_sessionDir, "chunk-*");
                for (int i = 0; i < dirs.Length; i++)
                {
                    string manifestPath = Path.Combine(dirs[i], "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }
                    RollingChunkManifest manifest = JsonUtility.FromJson<RollingChunkManifest>(File.ReadAllText(manifestPath));
                    if (manifest == null)
                    {
                        continue;
                    }
                    RetentionChunk info = new RetentionChunk();
                    info.Name = Path.GetFileName(dirs[i]);
                    info.EndTimeSeconds = manifest.EndTimeSeconds;
                    info.Bytes = DirectorySize(dirs[i]);
                    infos.Add(info);
                }
            }
            catch
            {
                return;
            }
            RetentionPlan plan = RetentionPolicy.Plan(_sessionDir, _assertedSessionDir, infos, _clock, _windowSeconds);
            if (plan.Refused)
            {
                _lastRefusal = plan.RefusalReason;
                WriteStatus();
                return;
            }
            for (int i = 0; i < plan.DeleteNames.Count; i++)
            {
                DeleteChunk(plan.DeleteNames[i]);
            }
            WriteStatus();
        }
        private void DeleteChunk(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }
                string rootFull = Path.GetFullPath(_sessionDir);
                string prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(rootFull, name));
                if (!full.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _lastRefusal = "delete-path-escape";
                    return;
                }
                if (Directory.Exists(full))
                {
                    Directory.Delete(full, true);
                    _chunksDeleted++;
                }
            }
            catch
            {
            }
        }
        private static long DirectorySize(string dir)
        {
            long total = 0;
            try
            {
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        total += new FileInfo(files[i]).Length;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return total;
        }
        public static long SessionDirSize(string sessionDir)
        {
            return DirectorySize(sessionDir);
        }
        private void EnsureFullCaptureTarget()
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w != TimelineWidth || h != TimelineHeight)
            {
                TimelineWidth = w;
                TimelineHeight = h;
                ReleaseCaptureTarget(ref _captureSmall);
                if (_encodeTexture != null)
                {
                    Destroy(_encodeTexture);
                    _encodeTexture = null;
                }
            }
            if (w <= 0 || h <= 0)
            {
                return;
            }
            if (_captureFull == null || _captureFull.width != w || _captureFull.height != h)
            {
                ReleaseCaptureTarget(ref _captureFull);
            }
            if (_captureFull == null)
            {
                _captureFull = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            }
            if (_captureSmall == null)
            {
                _captureSmall = new RenderTexture(TimelineWidth, TimelineHeight, 0, RenderTextureFormat.ARGB32);
            }
        }
        private void ReleaseCaptureTarget(ref RenderTexture target)
        {
            if (target != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(target);
                }
                catch
                {
                }
                target = null;
            }
        }
        public double RetainedSeconds()
        {
            List<RetentionChunk> infos = new List<RetentionChunk>();
            try
            {
                if (!string.IsNullOrEmpty(_sessionDir) && Directory.Exists(_sessionDir))
                {
                    string[] dirs = Directory.GetDirectories(_sessionDir, "chunk-*");
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        string manifestPath = Path.Combine(dirs[i], "manifest.json");
                        if (!File.Exists(manifestPath))
                        {
                            continue;
                        }
                        RollingChunkManifest manifest = JsonUtility.FromJson<RollingChunkManifest>(File.ReadAllText(manifestPath));
                        if (manifest == null)
                        {
                            continue;
                        }
                        RetentionChunk info = new RetentionChunk();
                        info.Name = Path.GetFileName(dirs[i]);
                        info.StartTimeSeconds = manifest.StartTimeSeconds;
                        info.EndTimeSeconds = manifest.EndTimeSeconds;
                        info.Bytes = manifest.Bytes;
                        infos.Add(info);
                    }
                }
            }
            catch
            {
            }
            double retained = RetentionPolicy.CoveredSeconds(infos);
            if (_capturing && string.IsNullOrEmpty(_refusalReason))
            {
                double partial = _clock - _chunkStart;
                if (partial > 0.0)
                {
                    retained += partial;
                }
            }
            return retained;
        }
        public long ChunkCount()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sessionDir) && Directory.Exists(_sessionDir))
                {
                    return Directory.GetDirectories(_sessionDir, "chunk-*").Length;
                }
            }
            catch
            {
            }
            return 0;
        }
        public long TotalBytes()
        {
            if (string.IsNullOrEmpty(_sessionDir))
            {
                return 0;
            }
            return DirectorySize(_sessionDir);
        }
        private void WriteStatus()
        {
            try
            {
                RollingStatusFile status = new RollingStatusFile();
                if (!string.IsNullOrEmpty(_refusalReason))
                {
                    status.State = "Cannot record";
                    status.Reason = _refusalReason;
                }
                else if (_frozen)
                {
                    status.State = "Paused";
                    status.FrozenSession = SessionId;
                }
                else if (!_capturing)
                {
                    status.State = "Stopped";
                }
                else
                {
                    status.State = "Recording";
                }
                status.SessionId = SessionId;
                status.RetainedSeconds = RetainedSeconds();
                status.ChunkCount = (int)ChunkCount();
                status.TotalBytes = TotalBytes();
                status.DroppedFrames = _droppedFrames;
                status.CapturedFrames = (int)_capturedFrames;
                status.CaptureMsTotal = _captureMsTotal;
                status.EncodeMsTotal = _encodeMsTotal;
                status.FrameMsTotal = _frameMsTotal;
                status.FramesTimed = _framesTimed;
                status.ChunksDeleted = _chunksDeleted;
                status.LastRetentionRefusal = _lastRefusal;
                string dir = RecorderDir();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "status.json"), JsonUtility.ToJson(status, true));
            }
            catch
            {
            }
        }
    }
}
