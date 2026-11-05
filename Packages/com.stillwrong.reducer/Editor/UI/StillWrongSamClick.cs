using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using StillWrong.Meta;
using StillWrong.Oracle;
using UnityEngine;

namespace StillWrong.Editor.UI
{
    public static class StillWrongSamClick
    {
        public static string LastNote = string.Empty;
        public static string LastDirectory = string.Empty;

        public static void Attach(StillWrongGroundingRequest request, Texture2D frame, bool hasBox, float x1, float y1, float x2, float y2, bool pointNegative)
        {
            LastNote = string.Empty;
            if (request == null || frame == null)
            {
                LastNote = "SAM skipped: no frame.";
                return;
            }
            string key = Environment.GetEnvironmentVariable("MODEL_API_KEY");
            if (string.IsNullOrEmpty(key))
            {
                LastNote = "SAM skipped: no MODEL_API_KEY. Using the click.";
                return;
            }
            try
            {
                int width = frame.width;
                int height = frame.height;
                List<SamPoint> points = new List<SamPoint>();
                SamBox box = null;
                if (hasBox)
                {
                    int left = ClampEdge(Mathf.Min(x1, x2), width);
                    int top = ClampEdge(Mathf.Min(y1, y2), height);
                    int right = ClampEdge(Mathf.Max(x1, x2), width);
                    int bottom = ClampEdge(Mathf.Max(y1, y2), height);
                    if (right <= left)
                    {
                        right = Math.Min(width, left + 1);
                    }
                    if (bottom <= top)
                    {
                        bottom = Math.Min(height, top + 1);
                    }
                    box = new SamBox(left, top, right, bottom);
                }
                else
                {
                    int px = Mathf.Clamp(Mathf.RoundToInt(x1), 0, Math.Max(0, width - 1));
                    int py = Mathf.Clamp(Mathf.RoundToInt(y1), 0, Math.Max(0, height - 1));
                    points.Add(new SamPoint(px, py, pointNegative));
                }
                string note;
                request.SamMask = Segment(frame, box, points, out note);
                request.SamMaskWidth = width;
                request.SamMaskHeight = height;
                if (request.SamMask == null)
                {
                    LastNote = note;
                    return;
                }
                LastNote = note;
            }
            catch (Exception ex)
            {
                request.SamMask = null;
                LastNote = "SAM failed: " + ex.GetType().Name + ". Using the click.";
            }
        }

        public static bool HasKey()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MODEL_API_KEY"));
        }

        // One SAM 3.1 call on the frame. Points and box are in frame pixels with the origin at the top left.
        // Returns the mask in frame pixels with row 0 at the bottom, or null with a note saying why.
        public static bool[,] Segment(Texture2D frame, SamBox box, List<SamPoint> points, out string note)
        {
            note = string.Empty;
            if (frame == null)
            {
                note = "SAM skipped: no frame.";
                return null;
            }
            if (!HasKey())
            {
                note = "SAM skipped: no MODEL_API_KEY. Using the click.";
                return null;
            }
            try
            {
                int width = frame.width;
                int height = frame.height;
                byte[] jpeg = ImageConversion.EncodeToJPG(frame, 90);
                if (jpeg == null || jpeg.Length == 0)
                {
                    note = "SAM skipped: frame encode failed. Using the click.";
                    return null;
                }
                string dataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
                List<SamFramePrompt> prompts = new List<SamFramePrompt>();
                prompts.Add(new SamFramePrompt(0, box, points ?? new List<SamPoint>()));
                List<SamPromptObject> objects = new List<SamPromptObject>();
                objects.Add(new SamPromptObject("1", prompts));
                string prompt = SamPromptBuilder.BuildObjectPrompt(width, height, objects);
                SamClient client = new SamClient((HttpClient)null, null, null, null);
                SamParseResult parsed;
                using (CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    parsed = client.SegmentImageByPromptAsync(prompt, dataUrl, cancel.Token).GetAwaiter().GetResult();
                }
                SamFrameRecord frameRecord = FirstRecord(parsed);
                SamMaskRecord maskRecord = frameRecord == null ? null : frameRecord.Mask;
                if (maskRecord == null || string.IsNullOrEmpty(maskRecord.Payload))
                {
                    note = "SAM returned no mask. Using the click.";
                    return null;
                }
                int maskW = maskRecord.Width > 0 ? maskRecord.Width : width;
                int maskH = maskRecord.Height > 0 ? maskRecord.Height : height;
                byte[] raster = MaskCodec.DecodeMask(maskRecord.Payload, maskW, maskH, maskRecord.Encoding);
                bool[,] mask = PlaceInFrame(raster, maskW, maskH, frameRecord.Box, width, height);
                note = "SAM mask " + Count(mask).ToString(CultureInfo.InvariantCulture) + " pixels.";
                return mask;
            }
            catch (Exception ex)
            {
                note = "SAM failed: " + ex.GetType().Name + ". Using the click.";
                return null;
            }
        }

        public static void SetReportMask(bool[,] mask)
        {
            LastMask = mask;
        }

        public static void WriteReport(Texture2D frame, StillWrongGroundingResult withMask, StillWrongGroundingResult withoutMask)
        {
            try
            {
                string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "StillWrong", "grounding");
                string dir = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(dir);
                LastDirectory = dir;
                if (frame != null)
                {
                    byte[] jpeg = ImageConversion.EncodeToJPG(frame, 90);
                    if (jpeg != null)
                    {
                        File.WriteAllBytes(Path.Combine(dir, "frame.jpg"), jpeg);
                    }
                }
                WriteImages(dir, frame);
                StringBuilder json = new StringBuilder();
                AppendReport(json, withMask, withoutMask);
                File.WriteAllText(Path.Combine(dir, "candidates.json"), json.ToString());
                if (!string.IsNullOrEmpty(LastNote))
                {
                    LastNote = LastNote + " Debug: " + dir;
                }
            }
            catch (Exception ex)
            {
                LastNote = LastNote + " Debug write failed: " + ex.GetType().Name + ".";
            }
        }

        private static void WriteImages(string dir, Texture2D frame)
        {
            bool[,] mask = LastMask;
            if (mask == null)
            {
                return;
            }
            int height = mask.GetLength(0);
            int width = mask.GetLength(1);
            Texture2D maskTex = new Texture2D(width, height, TextureFormat.RGB24, false);
            Texture2D overlay = frame != null ? new Texture2D(frame.width, frame.height, TextureFormat.RGB24, false) : null;
            Color32[] overlayPixels = null;
            if (overlay != null && frame.width == width && frame.height == height)
            {
                overlayPixels = frame.GetPixels32();
            }
            Color32[] maskPixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool on = mask[y, x];
                    maskPixels[y * width + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                    if (overlayPixels != null && on)
                    {
                        Color32 src = overlayPixels[y * width + x];
                        overlayPixels[y * width + x] = new Color32((byte)(src.r / 2), (byte)Math.Min(255, src.g + 80), (byte)(src.b / 2), 255);
                    }
                }
            }
            maskTex.SetPixels32(maskPixels);
            maskTex.Apply(false);
            File.WriteAllBytes(Path.Combine(dir, "mask.png"), maskTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(maskTex);
            if (overlay != null && overlayPixels != null)
            {
                overlay.SetPixels32(overlayPixels);
                overlay.Apply(false);
                File.WriteAllBytes(Path.Combine(dir, "overlay.png"), overlay.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(overlay);
            }
            StillWrongIdMap map = StillWrongIdBuffer.LastIdMap;
            if (map == null || map.Ids == null || map.Width != width || map.Height != height || map.Ids.Length < width * height)
            {
                return;
            }
            Texture2D idTex = new Texture2D(width, height, TextureFormat.RGB24, false);
            Color32[] idPixels = new Color32[width * height];
            for (int i = 0; i < idPixels.Length; i++)
            {
                int id = map.Ids[i];
                byte r = (byte)((id * 47) & 255);
                byte g = (byte)((id * 91) & 255);
                byte b = (byte)((id * 17) & 255);
                if (id == 0)
                {
                    r = 0;
                    g = 0;
                    b = 0;
                }
                idPixels[i] = new Color32(r, g, b, 255);
            }
            idTex.SetPixels32(idPixels);
            idTex.Apply(false);
            File.WriteAllBytes(Path.Combine(dir, "id.png"), idTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(idTex);
        }

        private static bool[,] LastMask;

        private static SamFrameRecord FirstRecord(SamParseResult parsed)
        {
            if (parsed == null || parsed.Records == null)
            {
                return null;
            }
            for (int i = 0; i < parsed.Records.Count; i++)
            {
                SamFrameRecord record = parsed.Records[i];
                if (record != null && record.Mask != null && !string.IsNullOrEmpty(record.Mask.Payload))
                {
                    return record;
                }
            }
            return null;
        }

        private static bool[,] PlaceInFrame(byte[] raster, int maskW, int maskH, SamBoxRecord box, int width, int height)
        {
            bool cropped = box != null && (maskW != width || maskH != height);
            int offsetX = cropped ? box.X1 : 0;
            int offsetY = cropped ? box.Y1 : 0;
            bool[,] mask = new bool[height, width];
            if (raster != null)
            {
                for (int row = 0; row < maskH; row++)
                {
                    for (int col = 0; col < maskW; col++)
                    {
                        int index = row * maskW + col;
                        if (index >= raster.Length || raster[index] == 0)
                        {
                            continue;
                        }
                        int x = offsetX + col;
                        int top = offsetY + row;
                        if (x < 0 || x >= width || top < 0 || top >= height)
                        {
                            continue;
                        }
                        mask[height - 1 - top, x] = true;
                    }
                }
            }
            LastMask = mask;
            return mask;
        }

        private static int Count(bool[,] mask)
        {
            int count = 0;
            int height = mask.GetLength(0);
            int width = mask.GetLength(1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[y, x])
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static int ClampEdge(float value, int limit)
        {
            int rounded = Mathf.RoundToInt(value);
            if (rounded < 0)
            {
                return 0;
            }
            if (rounded > limit)
            {
                return limit;
            }
            return rounded;
        }

        private static void AppendReport(StringBuilder json, StillWrongGroundingResult withMask, StillWrongGroundingResult withoutMask)
        {
            json.Append((char)123);
            json.Append((char)10);
            json.Append("  ");
            Quote(json, "note");
            json.Append(": ");
            Quote(json, Escape(LastNote));
            json.Append((char)44);
            json.Append((char)10);
            json.Append("  ");
            Quote(json, "usedSamMask");
            json.Append(": ");
            json.Append(withMask != null && withMask.UsedSamMask ? "true" : "false");
            json.Append((char)44);
            json.Append((char)10);
            json.Append("  ");
            Quote(json, "degraded");
            json.Append(": ");
            Quote(json, Escape(withMask == null ? string.Empty : withMask.DegradedReason));
            json.Append((char)44);
            json.Append((char)10);
            json.Append("  ");
            Quote(json, "withSam");
            json.Append(": ");
            AppendCandidates(json, withMask);
            json.Append((char)44);
            json.Append((char)10);
            json.Append("  ");
            Quote(json, "withoutSam");
            json.Append(": ");
            AppendCandidates(json, withoutMask);
            json.Append((char)10);
            json.Append((char)125);
            json.Append((char)10);
        }
        private static void Quote(StringBuilder json, string value)
        {
            json.Append((char)34);
            json.Append(value ?? string.Empty);
            json.Append((char)34);
        }
        private static void AppendCandidates(StringBuilder json, StillWrongGroundingResult result)
        {
            json.Append((char)91);
            json.Append((char)10);
            if (result == null || result.Candidates == null)
            {
                json.Append("  ]");
                return;
            }
            List<StillWrongGroundingCandidate> ranked = new List<StillWrongGroundingCandidate>(result.Candidates);
            if (result.UsedSamMask)
            {
                ranked.Sort(StillWrongGroundingRanking.Compare);
            }
            else
            {
                ranked.Sort(CompareCoverage);
            }
            for (int i = 0; i < ranked.Count; i++)
            {
                StillWrongGroundingCandidate candidate = ranked[i];
                if (i > 0)
                {
                    json.Append((char)44);
                    json.Append((char)10);
                }
                json.Append("    {");
                Quote(json, "rank");
                json.Append(": ");
                json.Append((i + 1).ToString(CultureInfo.InvariantCulture));
                json.Append(", ");
                Quote(json, "path");
                json.Append(": ");
                Quote(json, Escape(candidate == null ? string.Empty : candidate.ElementPath));
                json.Append(", ");
                Quote(json, "coverage");
                json.Append(": ");
                double coverage = StillWrongGroundingRanking.Score(candidate, result.UsedSamMask);
                json.Append(coverage.ToString("0.######", CultureInfo.InvariantCulture));
                json.Append(", ");
                Quote(json, "maskCoverage");
                json.Append(": ");
                json.Append((candidate == null ? 0d : candidate.MaskCoverage).ToString("0.######", CultureInfo.InvariantCulture));
                json.Append(", ");
                Quote(json, "elementCoverage");
                json.Append(": ");
                json.Append((candidate == null ? 0d : candidate.ElementCoverage).ToString("0.######", CultureInfo.InvariantCulture));
                json.Append((char)125);
            }
            json.Append((char)10);
            json.Append("  ]");
        }
        private static int CompareCoverage(StillWrongGroundingCandidate a, StillWrongGroundingCandidate b)
        {
            double left = a == null ? 0d : a.MaskCoverage;
            double right = b == null ? 0d : b.MaskCoverage;
            return right.CompareTo(left);
        }
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.Replace(new string((char)92, 1), new string((char)92, 2)).Replace(new string((char)34, 1), new string(new char[] { (char)92, (char)34 }));
        }
    }
}
