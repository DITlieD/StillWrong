using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StillWrong.Meta
{
    public sealed class SamBox
    {
        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        public SamBox(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    public sealed class SamPoint
    {
        public int X { get; }
        public int Y { get; }
        public bool Negative { get; }
        public bool HasInvalidLabel { get; }
        public string InvalidLabelValue { get; }

        public SamPoint(int x, int y, bool negative)
        {
            X = x;
            Y = y;
            Negative = negative;
            HasInvalidLabel = false;
            InvalidLabelValue = string.Empty;
        }

        public SamPoint(int x, int y, string label)
        {
            X = x;
            Y = y;
            if (label != null && label.Equals("negative", System.StringComparison.Ordinal))
            {
                Negative = true;
                HasInvalidLabel = false;
                InvalidLabelValue = string.Empty;
            }
            else if (label != null && label.Equals("positive", System.StringComparison.Ordinal))
            {
                Negative = false;
                HasInvalidLabel = false;
                InvalidLabelValue = string.Empty;
            }
            else
            {
                Negative = false;
                HasInvalidLabel = true;
                InvalidLabelValue = label;
            }
        }

        public string Label
        {
            get { return Negative ? "negative" : "positive"; }
        }
    }

    public sealed class SamFramePrompt
    {
        public int Frame { get; }
        public SamBox Box { get; }
        public IList<SamPoint> Points { get; }

        public SamFramePrompt(int frame, SamBox box, IList<SamPoint> points)
        {
            Frame = frame;
            Box = box;
            if (points != null)
            {
                Points = points;
            }
            else
            {
                Points = new List<SamPoint>();
            }
        }

        public SamFramePrompt(int frame)
            : this(frame, null, null)
        {
        }
    }

    public sealed class SamPromptObject
    {
        public string Id { get; }
        public IList<SamFramePrompt> Prompts { get; }

        public SamPromptObject(string id, IList<SamFramePrompt> prompts)
        {
            Id = id;
            if (prompts != null)
            {
                Prompts = prompts;
            }
            else
            {
                Prompts = new List<SamFramePrompt>();
            }
        }

        public SamPromptObject(int id, IList<SamFramePrompt> prompts)
            : this(id.ToString(), prompts)
        {
        }
    }

    public static class SamPromptBuilder
    {
        private const long MaxSafeInteger = 9007199254740991L;
        private static readonly Regex CanonicalDecimal = new Regex("^(0|[1-9][0-9]*)$");

        public static string BuildObjectPrompt(int width, int height, IList<SamPromptObject> objects)
        {
            if (width <= 0 || height <= 0)
            {
                throw new SamPromptException("invalid_size", "Size must be a positive width and height.");
            }
            if (objects == null || objects.Count == 0)
            {
                throw new SamPromptException("no_objects", "At least one object is required.");
            }
            string view = "w=" + width + ";h=" + height;
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            SortedDictionary<int, List<string>> segmentsByFrame = new SortedDictionary<int, List<string>>();
            for (int oi = 0; oi < objects.Count; oi++)
            {
                SamPromptObject promptObject = objects[oi];
                if (promptObject == null)
                {
                    throw new SamPromptException("invalid_object_id", "Object id is missing.");
                }
                string objectId = NormalizeObjectId(promptObject.Id);
                if (seenIds.Contains(objectId))
                {
                    throw new SamPromptException("duplicate_object_id", "Object " + objectId + " is given more than once.");
                }
                seenIds.Add(objectId);
                IList<SamFramePrompt> prompts = promptObject.Prompts;
                if (prompts == null || prompts.Count == 0)
                {
                    throw new SamPromptException("empty_object", "Object " + objectId + " has no prompts.");
                }
                HashSet<int> seenFrames = new HashSet<int>();
                for (int pi = 0; pi < prompts.Count; pi++)
                {
                    SamFramePrompt prompt = prompts[pi];
                    if (prompt == null)
                    {
                        throw new SamPromptException("invalid_frame", "Frame of object " + objectId + " is missing.");
                    }
                    int frame = prompt.Frame;
                    if (frame < 0)
                    {
                        throw new SamPromptException("invalid_frame", "Frame of object " + objectId + " is not a non-negative integer.");
                    }
                    if (seenFrames.Contains(frame))
                    {
                        throw new SamPromptException("duplicate_frame", "Object " + objectId + " has more than one prompt on frame " + frame + ".");
                    }
                    seenFrames.Add(frame);
                    SamBox box = prompt.Box;
                    IList<SamPoint> points = prompt.Points;
                    bool hasBox = box != null;
                    bool hasPoints = points != null && points.Count > 0;
                    if (!hasBox && !hasPoints)
                    {
                        throw new SamPromptException("empty_prompt", "Object " + objectId + " has neither a box nor points on frame " + frame + ".");
                    }
                    StringBuilder token = new StringBuilder();
                    if (hasBox)
                    {
                        token.Append(BoxToken(box, width, height));
                    }
                    if (hasPoints)
                    {
                        for (int qi = 0; qi < points.Count; qi++)
                        {
                            token.Append(PointToken(points[qi], width, height));
                        }
                    }
                    string segment = objectId + token.ToString();
                    List<string> list;
                    if (!segmentsByFrame.TryGetValue(frame, out list))
                    {
                        list = new List<string>();
                        segmentsByFrame[frame] = list;
                    }
                    list.Add(segment);
                }
            }
            StringBuilder output = new StringBuilder();
            foreach (KeyValuePair<int, List<string>> entry in segmentsByFrame)
            {
                output.Append("<" + entry.Key + "f>");
                for (int si = 0; si < entry.Value.Count; si++)
                {
                    if (si > 0)
                    {
                        output.Append(",");
                    }
                    output.Append(entry.Value[si]);
                }
            }
            return output.ToString();
        }

        private static string NormalizeObjectId(string id)
        {
            if (id == null)
            {
                throw new SamPromptException("invalid_object_id", "Object id is missing.");
            }
            if (!CanonicalDecimal.IsMatch(id))
            {
                throw new SamPromptException("invalid_object_id", "Object id is not a non-negative integer or its decimal string.");
            }
            string trimmed = id.Length > 1 ? id.TrimStart('0') : id;
            if (trimmed.Length == 0)
            {
                trimmed = "0";
            }
            if (!long.TryParse(trimmed, out long parsed))
            {
                throw new SamPromptException("invalid_object_id", "Object id is not a non-negative integer or its decimal string.");
            }
            if (parsed < 0 || parsed > MaxSafeInteger)
            {
                throw new SamPromptException("invalid_object_id", "Object id is not a non-negative integer or its decimal string.");
            }
            if (id.Length > 1 && id[0] == '0')
            {
                throw new SamPromptException("invalid_object_id", "Object id is not a non-negative integer or its decimal string.");
            }
            return id;
        }

        private static string BoxToken(SamBox box, int width, int height)
        {
            if (box.Left < 0 || box.Top < 0 || box.Right < 0 || box.Bottom < 0)
            {
                throw new SamPromptException("invalid_box", "Box must have non-negative integer edges.");
            }
            if (!(box.Left < box.Right && box.Right <= width && box.Top < box.Bottom && box.Bottom <= height))
            {
                throw new SamPromptException("invalid_box", "Box must have left smaller than right within width and top smaller than bottom within height.");
            }
            return "<|box;x1=" + box.Left + ";y1=" + box.Top + ";x2=" + (box.Right - 1) + ";y2=" + (box.Bottom - 1) + ";w=" + width + ";h=" + height + "|>";
        }

        private static string PointToken(SamPoint point, int width, int height)
        {
            if (point == null)
            {
                throw new SamPromptException("invalid_point", "Point is missing.");
            }
            if (point.HasInvalidLabel)
            {
                throw new SamPromptException("invalid_point_label", "Point label must be positive or negative.");
            }
            if (point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
            {
                throw new SamPromptException("invalid_point", "Point must have integer coordinates inside the media.");
            }
            string prefix = point.Negative ? "-" : string.Empty;
            return prefix + "<|point;x=" + point.X + ";y=" + point.Y + ";w=" + width + ";h=" + height + "|>";
        }
    }
}
