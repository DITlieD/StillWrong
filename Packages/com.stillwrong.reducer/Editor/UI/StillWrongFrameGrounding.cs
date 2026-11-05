using System;
using System.Collections.Generic;
using System.Globalization;
using StillWrong.Core;
using StillWrong.Meta;
using UnityEngine;

namespace StillWrong.Editor.UI
{
    public sealed class StillWrongFrameGroundingOutcome
    {
        public List<StillWrongGroundingCandidate> Candidates = new List<StillWrongGroundingCandidate>();
        public StillWrongGroundingResult ClickOnly = new StillWrongGroundingResult();
        public bool[,] Mask;
        public bool Refined;
        public bool Ambiguous;
        public string Note = string.Empty;

        public StillWrongGroundingResult AsResult()
        {
            StillWrongGroundingResult result = new StillWrongGroundingResult();
            result.Candidates = Candidates;
            result.UsedSamMask = Mask != null;
            result.IsAmbiguous = Ambiguous;
            result.MaskAreaPx = MaskRanker.Area(Mask);
            result.DegradedReason = Mask != null ? (Refined ? "sam-mask-refined-on-recorded-objects" : "sam-mask-on-recorded-objects") : "click-on-recorded-objects";
            return result;
        }
    }

    // Grounds a click on a recorded frame against the objects saved with that frame.
    // SAM outlines what was clicked; the outline is matched to the saved object rects.
    // When the outline also swallows a smaller object (two sprites drawn over each other),
    // SAM is asked again with a negative point on that object so it leaves it out.
    public static class StillWrongFrameGrounding
    {
        public const int MaxNegativePoints = 3;

        public static StillWrongFrameGroundingOutcome Run(Texture2D frame, FrameObjectsSnapshot snapshot, bool hasBox, float x1, float y1, float x2, float y2)
        {
            StillWrongFrameGroundingOutcome outcome = new StillWrongFrameGroundingOutcome();
            if (frame == null || snapshot == null)
            {
                outcome.Note = "No recorded objects for this frame.";
                return outcome;
            }
            int width = frame.width;
            int height = frame.height;
            List<MaskElement> elements = snapshot.ScaledTo(width, height);
            // Mask and object rects use row 0 at the bottom; the click comes in with row 0 at the top.
            Vec2 click = hasBox ? new Vec2((x1 + x2) * 0.5f, height - (y1 + y2) * 0.5f) : new Vec2(x1, height - y1);
            List<MaskRank> clickRanks = MaskRanker.RankByClick(elements, click, width, height);
            outcome.ClickOnly.Candidates = ToCandidates(clickRanks, false);
            outcome.ClickOnly.DegradedReason = "click-on-recorded-objects";

            SamBox box = hasBox ? ToBox(x1, y1, x2, y2, width, height) : null;
            List<SamPoint> points = new List<SamPoint>();
            if (!hasBox)
            {
                points.Add(new SamPoint(Clamp(x1, width), Clamp(y1, height), false));
            }
            string note;
            bool[,] mask = StillWrongSamClick.Segment(frame, box, points, out note);
            if (mask == null)
            {
                outcome.Candidates = outcome.ClickOnly.Candidates;
                outcome.Ambiguous = clickRanks.Count > 1;
                outcome.Note = note + " Picked from " + clickRanks.Count.ToString(CultureInfo.InvariantCulture) + " recorded objects under the click.";
                return outcome;
            }
            List<MaskRank> ranks = MaskRanker.Rank(mask, elements, click, true);
            string refineNote = string.Empty;
            if (MaskRanker.NeedsRefine(ranks))
            {
                List<Vec2> negatives = MaskRanker.NegativePoints(ranks, click, true, MaxNegativePoints);
                if (negatives.Count > 0)
                {
                    List<SamPoint> refinePoints = new List<SamPoint>(points);
                    for (int i = 0; i < negatives.Count; i++)
                    {
                        refinePoints.Add(new SamPoint(Clamp(negatives[i].X, width), Clamp(height - negatives[i].Y, height), true));
                    }
                    string refinedNote;
                    bool[,] refined = StillWrongSamClick.Segment(frame, box, refinePoints, out refinedNote);
                    List<MaskRank> refinedRanks = refined == null ? null : MaskRanker.Rank(refined, elements, click, true);
                    if (refinedRanks != null && refinedRanks.Count > 0 && (ranks.Count == 0 || refinedRanks[0].Score >= ranks[0].Score))
                    {
                        mask = refined;
                        ranks = refinedRanks;
                        outcome.Refined = true;
                        refineNote = " Asked SAM again without " + negatives.Count.ToString(CultureInfo.InvariantCulture) + " overlapping object(s).";
                    }
                }
            }
            outcome.Mask = mask;
            outcome.Ambiguous = MaskRanker.IsAmbiguous(ranks);
            outcome.Candidates = ToCandidates(ranks, true);
            if (outcome.Candidates.Count == 0)
            {
                outcome.Candidates = outcome.ClickOnly.Candidates;
            }
            string top = ranks.Count > 0 ? " Best match " + LeafName(ranks[0].Element.Path) + " at " + (ranks[0].Iou * 100.0).ToString("F0", CultureInfo.InvariantCulture) + "% overlap." : " No recorded object matches the mask.";
            outcome.Note = note + refineNote + top + (outcome.Ambiguous ? " Another object is close, check the pick." : string.Empty);
            return outcome;
        }

        private static List<StillWrongGroundingCandidate> ToCandidates(List<MaskRank> ranks, bool fromMask)
        {
            List<StillWrongGroundingCandidate> list = new List<StillWrongGroundingCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ranks.Count; i++)
            {
                MaskRank rank = ranks[i];
                if (rank.Element == null || string.IsNullOrEmpty(rank.Element.Path) || !seen.Add(rank.Element.Path))
                {
                    continue;
                }
                StillWrongGroundingCandidate candidate = new StillWrongGroundingCandidate();
                candidate.ElementPath = rank.Element.Path;
                candidate.MaskCoverage = fromMask ? rank.MaskShare : 0.0;
                candidate.ElementCoverage = fromMask ? rank.Fill : 0.0;
                candidate.BoxAreaInclusive = rank.RectPixels;
                candidate.IsAnalytic = true;
                candidate.Warning = fromMask ? "iou " + rank.Iou.ToString("0.00", CultureInfo.InvariantCulture) : "under-click";
                list.Add(candidate);
            }
            return list;
        }

        private static SamBox ToBox(float x1, float y1, float x2, float y2, int width, int height)
        {
            int left = Clamp(Mathf.Min(x1, x2), width);
            int top = Clamp(Mathf.Min(y1, y2), height);
            int right = Math.Max(left + 1, Math.Min(width, Mathf.RoundToInt(Mathf.Max(x1, x2))));
            int bottom = Math.Max(top + 1, Math.Min(height, Mathf.RoundToInt(Mathf.Max(y1, y2))));
            return new SamBox(left, top, right, bottom);
        }

        private static int Clamp(float value, int limit)
        {
            return Mathf.Clamp(Mathf.RoundToInt(value), 0, Math.Max(0, limit - 1));
        }

        private static string LeafName(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash >= 0 && slash + 1 < path.Length ? path.Substring(slash + 1) : path;
        }
    }
}
