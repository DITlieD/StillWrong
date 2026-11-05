using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace StillWrong.Recording
{
    // Encodes recorded frames to JPEG and writes them to disk on a background thread, so the
    // game frame only pays for copying the pixels. Two threads keep up with 30 fps at 1080p.
    // At most Capacity frames wait; a full queue drops the frame.
    public sealed class FrameEncodeWorker : IDisposable
    {
        public const int Capacity = 4;
        public const int Threads = 2;

        private sealed class Job
        {
            public byte[] Pixels;
            public GraphicsFormat Format;
            public int Width;
            public int Height;
            public int Quality;
            public string Path;
        }

        private readonly BlockingCollection<Job> _queue = new BlockingCollection<Job>(Capacity);
        private readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private readonly Thread[] _threads = new Thread[Threads];
        private int _inFlight;
        private long _bytes;
        private long _written;
        private long _failed;
        private long _encodeTicks;

        public FrameEncodeWorker()
        {
            for (int i = 0; i < Threads; i++)
            {
                _threads[i] = new Thread(Run);
                _threads[i].IsBackground = true;
                _threads[i].Name = "StillWrong frame encoder " + i;
                _threads[i].Start();
            }
        }

        public long BytesWritten { get { return Interlocked.Read(ref _bytes); } }
        public long FramesWritten { get { return Interlocked.Read(ref _written); } }
        public long FramesFailed { get { return Interlocked.Read(ref _failed); } }
        public double EncodeMsTotal { get { return Interlocked.Read(ref _encodeTicks) * 1000.0 / Stopwatch.Frequency; } }

        // Copies the frame's pixels and queues them. The texture can be reused or destroyed right after.
        public bool TryQueue(Texture2D frame, int quality, string path)
        {
            if (frame == null || _queue.IsAddingCompleted)
            {
                return false;
            }
            NativeArray<byte> data = frame.GetRawTextureData<byte>();
            if (data.Length == 0)
            {
                return false;
            }
            byte[] buffer = Rent(data.Length);
            NativeArray<byte>.Copy(data, buffer, data.Length);
            Job job = new Job();
            job.Pixels = buffer;
            job.Format = frame.graphicsFormat;
            job.Width = frame.width;
            job.Height = frame.height;
            job.Quality = quality;
            job.Path = path;
            Interlocked.Increment(ref _inFlight);
            if (!_queue.TryAdd(job))
            {
                Interlocked.Decrement(ref _inFlight);
                Return(buffer);
                return false;
            }
            return true;
        }

        // Waits until every queued frame is on disk, up to the timeout. Returns false on timeout.
        public bool Flush(int timeoutMs)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (Volatile.Read(ref _inFlight) > 0 && clock.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(5);
            }
            return Volatile.Read(ref _inFlight) == 0;
        }

        public void Dispose()
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.CompleteAdding();
            }
            for (int i = 0; i < Threads; i++)
            {
                _threads[i].Join(3000);
            }
        }

        private void Run()
        {
            foreach (Job job in _queue.GetConsumingEnumerable())
            {
                try
                {
                    long start = Stopwatch.GetTimestamp();
                    byte[] jpeg = ImageConversion.EncodeArrayToJPG(job.Pixels, job.Format, (uint)job.Width, (uint)job.Height, 0, job.Quality);
                    Interlocked.Add(ref _encodeTicks, Stopwatch.GetTimestamp() - start);
                    if (jpeg == null || jpeg.Length == 0)
                    {
                        Interlocked.Increment(ref _failed);
                        continue;
                    }
                    string dir = Path.GetDirectoryName(job.Path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllBytes(job.Path, jpeg);
                    Interlocked.Add(ref _bytes, jpeg.Length);
                    Interlocked.Increment(ref _written);
                }
                catch (Exception)
                {
                    // A failed frame must never stop the encoder thread; it is counted and skipped.
                    Interlocked.Increment(ref _failed);
                }
                finally
                {
                    Return(job.Pixels);
                    Interlocked.Decrement(ref _inFlight);
                }
            }
        }

        private byte[] Rent(int length)
        {
            byte[] buffer;
            while (_pool.TryTake(out buffer))
            {
                if (buffer.Length == length)
                {
                    return buffer;
                }
            }
            return new byte[length];
        }

        private void Return(byte[] buffer)
        {
            if (buffer != null && _pool.Count < Capacity + 2)
            {
                _pool.Add(buffer);
            }
        }
    }
}
