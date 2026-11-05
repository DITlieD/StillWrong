using System;
using System.Collections.Generic;

namespace StillWrong.Core
{
    // One object that was on screen in a recorded frame. Rect is in frame pixels, origin bottom left.
    public sealed class MaskElement
    {
        public string Path = string.Empty;
        public Rect2D Rect;
        public long DrawOrder;
        public float Depth;
        public bool IsUi;
    }

    public sealed class MaskRank
    {
        public MaskElement Element;
        public int InsidePixels;
        public int RectPixels;
        // Share of the mask that falls inside this object's rect.
        public double MaskShare;
        // Share of this object's rect that the mask covers.
        public double Fill;
        // Mask against rect intersection over union. This is the main ranking score.
        public double Iou;
        public bool ContainsClick;
        public double Score;
    }

    // Ranks recorded scene objects against a SAM mask. The mask is indexed [row, column] with row 0 at the bottom.
    public static class MaskRanker
    {
        public const double TieBand = 0.05;
        public const double MinIou = 0.02;
        public const int MaxCandidates = 10;
        public const double ClickBonus = 0.05;
        public const int BackgroundRatio = 4;

        public static int Area(bool[,] mask)
        {
            if (mask == null)
            {
                return 0;
            }
            int count = 0;
            int h = mask.GetLength(0);
            int w = mask.GetLength(1);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (mask[y, x])
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public static int[] Integral(bool[,] mask)
        {
            int h = mask.GetLength(0);
            int w = mask.GetLength(1);
            int[] sums = new int[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                int row = 0;
                for (int x = 0; x < w; x++)
                {
                    if (mask[y, x])
                    {
                        row++;
                    }
                    sums[(y + 1) * (w + 1) + (x + 1)] = sums[y * (w + 1) + (x + 1)] + row;
                }
            }
            return sums;
        }

        private static int Sum(int[] sums, int w, int x0, int y0, int x1, int y1)
        {
            int stride = w + 1;
            return sums[(y1 + 1) * stride + (x1 + 1)] - sums[y0 * stride + (x1 + 1)] - sums[(y1 + 1) * stride + x0] + sums[y0 * stride + x0];
        }

        private static bool Clip(Rect2D rect, int w, int h, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Math.Max(0, (int)Math.Floor(rect.XMin));
            y0 = Math.Max(0, (int)Math.Floor(rect.YMin));
            x1 = Math.Min(w - 1, (int)Math.Ceiling(rect.XMax) - 1);
            y1 = Math.Min(h - 1, (int)Math.Ceiling(rect.YMax) - 1);
            return x1 >= x0 && y1 >= y0;
        }

        public static bool Contains(Rect2D rect, Vec2 point)
        {
            return point.X >= rect.XMin && point.X <= rect.XMax && point.Y >= rect.YMin && point.Y <= rect.YMax;
        }

        public static List<MaskRank> Rank(bool[,] mask, IList<MaskElement> elements, Vec2 click, bool hasClick)
        {
            List<MaskRank> ranks = new List<MaskRank>();
            if (mask == null || elements == null)
            {
                return ranks;
            }
            int h = mask.GetLength(0);
            int w = mask.GetLength(1);
            int maskArea = Area(mask);
            if (maskArea == 0)
            {
                return RankByClick(elements, click, w, h);
            }
            int[] sums = Integral(mask);
            for (int i = 0; i < elements.Count; i++)
            {
                MaskElement element = elements[i];
                if (element == null)
                {
                    continue;
                }
                int x0;
                int y0;
                int x1;
                int y1;
                if (!Clip(element.Rect, w, h, out x0, out y0, out x1, out y1))
                {
                    continue;
                }
                int inside = Sum(sums, w, x0, y0, x1, y1);
                int rectPixels = (x1 - x0 + 1) * (y1 - y0 + 1);
                bool containsClick = hasClick && Contains(element.Rect, click);
                if (inside == 0)
                {
                    continue;
                }
                MaskRank rank = new MaskRank();
                rank.Element = element;
                rank.InsidePixels = inside;
                rank.RectPixels = rectPixels;
                rank.MaskShare = (double)inside / maskArea;
                rank.Fill = (double)inside / rectPixels;
                rank.Iou = (double)inside / (maskArea + rectPixels - inside);
                rank.ContainsClick = containsClick;
                rank.Score = rank.Iou + (containsClick ? ClickBonus : 0.0);
                // An object four times larger than the mask (ground, sky, a full-screen panel) is not what SAM outlined.
                if (rectPixels > maskArea * BackgroundRatio)
                {
                    continue;
                }
                if (rank.Iou < MinIou && !containsClick)
                {
                    continue;
                }
                ranks.Add(rank);
            }
            SortRanks(ranks);
            if (ranks.Count > MaxCandidates)
            {
                ranks.RemoveRange(MaxCandidates, ranks.Count - MaxCandidates);
            }
            return ranks;
        }

        // Without a mask: every object under the click, front first, then smallest first.
        public static List<MaskRank> RankByClick(IList<MaskElement> elements, Vec2 click, int frameWidth, int frameHeight)
        {
            List<MaskRank> ranks = new List<MaskRank>();
            if (elements == null)
            {
                return ranks;
            }
            for (int i = 0; i < elements.Count; i++)
            {
                MaskElement element = elements[i];
                if (element == null || !Contains(element.Rect, click))
                {
                    continue;
                }
                MaskRank rank = new MaskRank();
                rank.Element = element;
                rank.ContainsClick = true;
                rank.RectPixels = (int)Math.Max(1.0, element.Rect.Area);
                rank.Score = 0.0;
                ranks.Add(rank);
            }
            ranks.Sort(CompareFrontThenSmall);
            if (ranks.Count > MaxCandidates)
            {
                ranks.RemoveRange(MaxCandidates, ranks.Count - MaxCandidates);
            }
            return ranks;
        }

        private static void SortRanks(List<MaskRank> ranks)
        {
            ranks.Sort((a, b) => b.Score.CompareTo(a.Score));
            // Near ties go to the object drawn in front; identical outlines go to the parent.
            bool swapped = true;
            int guard = 0;
            while (swapped && guard < ranks.Count * ranks.Count + 1)
            {
                swapped = false;
                guard++;
                for (int i = 0; i + 1 < ranks.Count; i++)
                {
                    MaskRank a = ranks[i];
                    MaskRank b = ranks[i + 1];
                    if (Math.Abs(a.Score - b.Score) > TieBand)
                    {
                        continue;
                    }
                    if (PreferSecond(a, b))
                    {
                        ranks[i] = b;
                        ranks[i + 1] = a;
                        swapped = true;
                    }
                }
            }
        }

        private static bool PreferSecond(MaskRank a, MaskRank b)
        {
            if (RectIou(a.Element.Rect, b.Element.Rect) > 0.95)
            {
                return b.Element.Path.Length < a.Element.Path.Length && a.Element.Path.StartsWith(b.Element.Path + "/", StringComparison.Ordinal);
            }
            if (Math.Abs(a.Score - b.Score) > 0.01)
            {
                return false;
            }
            return CompareFront(a.Element, b.Element) > 0;
        }

        // Negative when a is in front of b.
        private static int CompareFront(MaskElement a, MaskElement b)
        {
            if (a.IsUi != b.IsUi)
            {
                return a.IsUi ? -1 : 1;
            }
            if (a.DrawOrder != b.DrawOrder)
            {
                return b.DrawOrder.CompareTo(a.DrawOrder);
            }
            return a.Depth.CompareTo(b.Depth);
        }

        private static int CompareFrontThenSmall(MaskRank a, MaskRank b)
        {
            int front = CompareFront(a.Element, b.Element);
            if (front != 0)
            {
                return front;
            }
            return a.Element.Rect.Area.CompareTo(b.Element.Rect.Area);
        }

        public static double RectIou(Rect2D a, Rect2D b)
        {
            double ix = Math.Max(0.0, Math.Min(a.XMax, b.XMax) - Math.Max(a.XMin, b.XMin));
            double iy = Math.Max(0.0, Math.Min(a.YMax, b.YMax) - Math.Max(a.YMin, b.YMin));
            double inter = ix * iy;
            double union = a.Area + b.Area - inter;
            return union > 0.0 ? inter / union : 0.0;
        }

        // True when SAM likely merged the top object with something else: the mask spills out of the
        // top object's rect, or a smaller object is mostly covered by the mask too.
        public static bool NeedsRefine(List<MaskRank> ranks)
        {
            if (ranks == null || ranks.Count == 0 || ranks[0].RectPixels == 0)
            {
                return false;
            }
            MaskRank top = ranks[0];
            if (top.MaskShare < 0.85)
            {
                return true;
            }
            for (int i = 1; i < ranks.Count; i++)
            {
                if (IsSwallowed(top, ranks[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSwallowed(MaskRank top, MaskRank other)
        {
            if (other.Fill < 0.5 || other.RectPixels == 0)
            {
                return false;
            }
            if (other.RectPixels >= top.RectPixels * 0.6)
            {
                return false;
            }
            return RectIou(top.Element.Rect, other.Element.Rect) < 0.9;
        }

        // Points to tell SAM to leave out: centres of smaller objects the mask swallowed, kept away from the click.
        public static List<Vec2> NegativePoints(List<MaskRank> ranks, Vec2 click, bool hasClick, int max)
        {
            List<Vec2> points = new List<Vec2>();
            if (ranks == null || ranks.Count < 2)
            {
                return points;
            }
            MaskRank top = ranks[0];
            for (int i = 1; i < ranks.Count && points.Count < max; i++)
            {
                MaskRank other = ranks[i];
                if (!IsSwallowed(top, other))
                {
                    continue;
                }
                if (hasClick && other.ContainsClick)
                {
                    continue;
                }
                Vec2 centre = other.Element.Rect.Center;
                if (hasClick)
                {
                    double dx = centre.X - click.X;
                    double dy = centre.Y - click.Y;
                    double keepAway = Math.Max(12.0, Math.Sqrt(other.Element.Rect.Area) * 0.25);
                    if (dx * dx + dy * dy < keepAway * keepAway)
                    {
                        continue;
                    }
                }
                points.Add(centre);
            }
            return points;
        }

        // The click sits inside a smaller object that the mask also covers, so the user may mean that one.
        public static bool IsAmbiguous(List<MaskRank> ranks)
        {
            if (ranks == null || ranks.Count < 2)
            {
                return false;
            }
            if (Math.Abs(ranks[0].Score - ranks[1].Score) <= TieBand)
            {
                return true;
            }
            for (int i = 1; i < ranks.Count; i++)
            {
                if (ranks[i].ContainsClick && ranks[i].Fill >= 0.5 && ranks[i].RectPixels < ranks[0].RectPixels * 0.6)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
