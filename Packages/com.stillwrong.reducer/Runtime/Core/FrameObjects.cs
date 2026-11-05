using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StillWrong.Core
{
    // What was on screen when a recorded frame was taken. Rects are in screen pixels, origin bottom left.
    public sealed class FrameObjectsSnapshot
    {
        public int FrameNumber;
        public int ScreenWidth;
        public int ScreenHeight;
        public List<MaskElement> Elements = new List<MaskElement>();

        // The same objects in frame pixels, for a frame of the given size.
        public List<MaskElement> ScaledTo(int frameWidth, int frameHeight)
        {
            double sx = ScreenWidth > 0 ? (double)frameWidth / ScreenWidth : 1.0;
            double sy = ScreenHeight > 0 ? (double)frameHeight / ScreenHeight : 1.0;
            List<MaskElement> scaled = new List<MaskElement>(Elements.Count);
            for (int i = 0; i < Elements.Count; i++)
            {
                MaskElement e = Elements[i];
                MaskElement copy = new MaskElement();
                copy.Path = e.Path;
                copy.Rect = new Rect2D((float)(e.Rect.X * sx), (float)(e.Rect.Y * sy), (float)(e.Rect.Width * sx), (float)(e.Rect.Height * sy));
                copy.DrawOrder = e.DrawOrder;
                copy.Depth = e.Depth;
                copy.IsUi = e.IsUi;
                scaled.Add(copy);
            }
            return scaled;
        }
    }

    // One text file per recorded chunk. A line starting with '#' opens a snapshot:
    // "#<tab>frameNumber<tab>screenWidth<tab>screenHeight". Each following line is one object:
    // "path<tab>x<tab>y<tab>w<tab>h<tab>drawOrder<tab>depth<tab>ui".
    public static class FrameObjects
    {
        public const string FileName = "objects.tsv";

        public static string Format(FrameObjectsSnapshot snapshot)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('#').Append('\t').Append(snapshot.FrameNumber.ToString(CultureInfo.InvariantCulture))
              .Append('\t').Append(snapshot.ScreenWidth.ToString(CultureInfo.InvariantCulture))
              .Append('\t').Append(snapshot.ScreenHeight.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < snapshot.Elements.Count; i++)
            {
                MaskElement e = snapshot.Elements[i];
                if (e == null || string.IsNullOrEmpty(e.Path) || e.Path.IndexOf('\t') >= 0 || e.Path.IndexOf('\n') >= 0)
                {
                    continue;
                }
                sb.Append(e.Path).Append('\t')
                  .Append(F(e.Rect.X)).Append('\t').Append(F(e.Rect.Y)).Append('\t')
                  .Append(F(e.Rect.Width)).Append('\t').Append(F(e.Rect.Height)).Append('\t')
                  .Append(e.DrawOrder.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(F(e.Depth)).Append('\t').Append(e.IsUi ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }

        private static string F(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static List<FrameObjectsSnapshot> Parse(string text)
        {
            List<FrameObjectsSnapshot> list = new List<FrameObjectsSnapshot>();
            if (string.IsNullOrEmpty(text))
            {
                return list;
            }
            FrameObjectsSnapshot current = null;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split('\t');
                if (parts[0] == "#")
                {
                    current = null;
                    int frame;
                    int sw;
                    int sh;
                    if (parts.Length >= 4 && TryInt(parts[1], out frame) && TryInt(parts[2], out sw) && TryInt(parts[3], out sh))
                    {
                        current = new FrameObjectsSnapshot();
                        current.FrameNumber = frame;
                        current.ScreenWidth = sw;
                        current.ScreenHeight = sh;
                        list.Add(current);
                    }
                    continue;
                }
                if (current == null || parts.Length < 8)
                {
                    continue;
                }
                float x;
                float y;
                float w;
                float h;
                long order;
                float depth;
                if (!TryFloat(parts[1], out x) || !TryFloat(parts[2], out y) || !TryFloat(parts[3], out w) || !TryFloat(parts[4], out h)
                    || !long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out order) || !TryFloat(parts[6], out depth))
                {
                    continue;
                }
                MaskElement e = new MaskElement();
                e.Path = parts[0];
                e.Rect = new Rect2D(x, y, w, h);
                e.DrawOrder = order;
                e.Depth = depth;
                e.IsUi = parts[7].Trim() == "1";
                current.Elements.Add(e);
            }
            return list;
        }

        // The snapshot taken closest to the frame, or null when none is within maxGap frames.
        public static FrameObjectsSnapshot Nearest(List<FrameObjectsSnapshot> snapshots, int frameNumber, int maxGap)
        {
            FrameObjectsSnapshot best = null;
            int bestGap = int.MaxValue;
            if (snapshots == null)
            {
                return null;
            }
            for (int i = 0; i < snapshots.Count; i++)
            {
                int gap = Math.Abs(snapshots[i].FrameNumber - frameNumber);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = snapshots[i];
                }
            }
            return bestGap <= maxGap ? best : null;
        }

        // Reads the chunk's file for a recorded frame path such as ".../chunk-00047/frames/f-000123.jpg".
        public static FrameObjectsSnapshot ForFrameFile(string framePath, int maxGap)
        {
            try
            {
                if (string.IsNullOrEmpty(framePath))
                {
                    return null;
                }
                string framesDir = Path.GetDirectoryName(framePath);
                string chunkDir = framesDir == null ? null : Path.GetDirectoryName(framesDir);
                if (chunkDir == null)
                {
                    return null;
                }
                string file = Path.Combine(chunkDir, FileName);
                if (!File.Exists(file))
                {
                    return null;
                }
                string stem = Path.GetFileNameWithoutExtension(framePath);
                int dash = stem.LastIndexOf('-');
                int frame;
                if (dash < 0 || !TryInt(stem.Substring(dash + 1), out frame))
                {
                    return null;
                }
                return Nearest(Parse(File.ReadAllText(file)), frame, maxGap);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool TryInt(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        private static bool TryFloat(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
