using System;
using System.Collections.Generic;

namespace StillWrong.Core.Oracle
{
    public sealed class EvaluationContext
    {
        public int ConsoleErrorCount { get; set; }
        public bool ReplayDesync { get; set; }

        public EvaluationContext()
        {
            ConsoleErrorCount = 0;
            ReplayDesync = false;
        }
    }

    public sealed class OracleResult
    {
        public OracleVerdict Verdict { get; set; }
        public List<string> Reasons { get; set; }

        public OracleResult()
        {
            Reasons = new List<string>();
        }
    }

    internal sealed class MissingDataException : Exception
    {
        public string Detail { get; }

        public MissingDataException(string detail)
            : base(detail)
        {
            Detail = detail;
        }
    }

    public static class OracleEvaluator
    {
        public static OracleResult Evaluate(OracleSpec spec, IMeasurementSource source, EvaluationContext context)
        {
            OracleResult result = new OracleResult();
            if (spec == null || source == null || context == null)
            {
                result.Verdict = OracleVerdict.Inconclusive;
                result.Reasons.Add("inconclusive: spec, source or context is missing");
                return result;
            }
            if (context.ConsoleErrorCount > 0)
            {
                result.Verdict = OracleVerdict.Inconclusive;
                result.Reasons.Add("inconclusive: console errors = " + context.ConsoleErrorCount);
                return result;
            }
            if (context.ReplayDesync)
            {
                result.Verdict = OracleVerdict.Inconclusive;
                result.Reasons.Add("inconclusive: replay desync reported");
                return result;
            }
            int anchor = 0;
            if (spec.MeasurementFrames != null && spec.MeasurementFrames.Count > 0)
            {
                anchor = spec.MeasurementFrames[0];
            }
            else if (spec.WindowStart.HasValue)
            {
                anchor = spec.WindowStart.Value;
            }
            bool failureHolds;
            List<string> failureReasons = new List<string>();
            try
            {
                failureHolds = EvaluateNode(spec.Failure, anchor, source, failureReasons);
            }
            catch (MissingDataException missing)
            {
                result.Verdict = OracleVerdict.Inconclusive;
                result.Reasons.Add("inconclusive: " + missing.Detail);
                return result;
            }
            List<string> invariantReasons = new List<string>();
            bool invariantsHold = true;
            if (spec.Invariants != null)
            {
                for (int i = 0; i < spec.Invariants.Count; i++)
                {
                    List<string> oneReasons = new List<string>();
                    bool holds;
                    try
                    {
                        holds = EvaluateNode(spec.Invariants[i], anchor, source, oneReasons);
                    }
                    catch (MissingDataException missing)
                    {
                        result.Verdict = OracleVerdict.Inconclusive;
                        result.Reasons.Add("inconclusive: " + missing.Detail);
                        return result;
                    }
                    if (!holds)
                    {
                        invariantsHold = false;
                        invariantReasons.Add("broken invariant: " + JoinReasons(oneReasons));
                    }
                    else
                    {
                        invariantReasons.Add("invariant holds: " + JoinReasons(oneReasons));
                    }
                }
            }
            if (!invariantsHold)
            {
                result.Verdict = OracleVerdict.Inconclusive;
                result.Reasons.AddRange(invariantReasons);
                result.Reasons.Add("failure was " + (failureHolds ? "true" : "false") + ": " + JoinReasons(failureReasons));
                return result;
            }
            if (failureHolds)
            {
                result.Verdict = OracleVerdict.StillWrong;
            }
            else
            {
                result.Verdict = OracleVerdict.HealthyPass;
            }
            result.Reasons.Add("failure is " + (failureHolds ? "true" : "false") + ": " + JoinReasons(failureReasons));
            result.Reasons.AddRange(invariantReasons);
            return result;
        }

        private static string JoinReasons(List<string> reasons)
        {
            if (reasons == null || reasons.Count == 0)
            {
                return "no detail";
            }
            return string.Join("; ", reasons.ToArray());
        }

        private static bool EvaluateNode(PredicateNode node, int frame, IMeasurementSource source, List<string> reasons)
        {
            if (node == null)
            {
                throw new MissingDataException("predicate node is missing");
            }
            switch (node.Kind)
            {
                case PredicateKind.Comparison:
                    return EvaluateComparison(node, frame, source, reasons);
                case PredicateKind.And:
                    return EvaluateAnd(node, frame, source, reasons);
                case PredicateKind.Or:
                    return EvaluateOr(node, frame, source, reasons);
                case PredicateKind.Not:
                    return EvaluateNot(node, frame, source, reasons);
                case PredicateKind.Always:
                    return EvaluateAlways(node, source, reasons);
                case PredicateKind.Ever:
                    return EvaluateEver(node, source, reasons);
                case PredicateKind.DropsThenRecovers:
                    return EvaluateDropsThenRecovers(node, source, reasons);
                case PredicateKind.Changed:
                    return EvaluateChanged(node, source, reasons);
                default:
                    throw new MissingDataException("unknown predicate kind");
            }
        }

        private static bool EvaluateAnd(PredicateNode node, int frame, IMeasurementSource source, List<string> reasons)
        {
            if (node.Children == null || node.Children.Count == 0)
            {
                throw new MissingDataException("and node has no children");
            }
            bool holds = true;
            for (int i = 0; i < node.Children.Count; i++)
            {
                List<string> childReasons = new List<string>();
                bool child = EvaluateNode(node.Children[i], frame, source, childReasons);
                reasons.Add("(" + JoinReasons(childReasons) + ")");
                if (!child)
                {
                    holds = false;
                }
            }
            return holds;
        }

        private static bool EvaluateOr(PredicateNode node, int frame, IMeasurementSource source, List<string> reasons)
        {
            if (node.Children == null || node.Children.Count == 0)
            {
                throw new MissingDataException("or node has no children");
            }
            bool holds = false;
            for (int i = 0; i < node.Children.Count; i++)
            {
                List<string> childReasons = new List<string>();
                bool child = EvaluateNode(node.Children[i], frame, source, childReasons);
                reasons.Add("(" + JoinReasons(childReasons) + ")");
                if (child)
                {
                    holds = true;
                }
            }
            return holds;
        }

        private static bool EvaluateNot(PredicateNode node, int frame, IMeasurementSource source, List<string> reasons)
        {
            if (node.Children == null || node.Children.Count != 1)
            {
                throw new MissingDataException("not node must have exactly one child");
            }
            List<string> childReasons = new List<string>();
            bool child = EvaluateNode(node.Children[0], frame, source, childReasons);
            reasons.Add("not (" + JoinReasons(childReasons) + ")");
            return !child;
        }

        private static bool EvaluateAlways(PredicateNode node, IMeasurementSource source, List<string> reasons)
        {
            if (node.Children == null || node.Children.Count != 1)
            {
                throw new MissingDataException("always node must have exactly one child");
            }
            if (!node.WindowStart.HasValue || !node.WindowEnd.HasValue)
            {
                throw new MissingDataException("always node is missing its window");
            }
            int start = node.WindowStart.Value;
            int end = node.WindowEnd.Value;
            if (end < start)
            {
                throw new MissingDataException("always node window is reversed");
            }
            for (int f = start; f <= end; f++)
            {
                List<string> childReasons = new List<string>();
                bool child = EvaluateNode(node.Children[0], f, source, childReasons);
                if (!child)
                {
                    reasons.Add("always fails at frame " + f + ": " + JoinReasons(childReasons));
                    return false;
                }
            }
            reasons.Add("always holds from frame " + start + " to frame " + end);
            return true;
        }

        private static bool EvaluateEver(PredicateNode node, IMeasurementSource source, List<string> reasons)
        {
            if (node.Children == null || node.Children.Count != 1)
            {
                throw new MissingDataException("ever node must have exactly one child");
            }
            if (!node.WindowStart.HasValue || !node.WindowEnd.HasValue)
            {
                throw new MissingDataException("ever node is missing its window");
            }
            int start = node.WindowStart.Value;
            int end = node.WindowEnd.Value;
            if (end < start)
            {
                throw new MissingDataException("ever node window is reversed");
            }
            for (int f = start; f <= end; f++)
            {
                List<string> childReasons = new List<string>();
                bool child = EvaluateNode(node.Children[0], f, source, childReasons);
                if (child)
                {
                    reasons.Add("ever holds at frame " + f + ": " + JoinReasons(childReasons));
                    return true;
                }
            }
            reasons.Add("ever fails from frame " + start + " to frame " + end);
            return false;
        }

        private static bool EvaluateDropsThenRecovers(PredicateNode node, IMeasurementSource source, List<string> reasons)
        {
            if (node.Probe == null)
            {
                throw new MissingDataException("drops-then-recovers node is missing its probe");
            }
            if (!node.Threshold.HasValue)
            {
                throw new MissingDataException("drops-then-recovers node is missing its threshold");
            }
            if (!node.WindowStart.HasValue || !node.WindowEnd.HasValue)
            {
                throw new MissingDataException("drops-then-recovers node is missing its window");
            }
            int start = node.WindowStart.Value;
            int end = node.WindowEnd.Value;
            if (end <= start)
            {
                reasons.Add("drops-then-recovers window from frame " + start + " to frame " + end + " is too short");
                return false;
            }
            double threshold = node.Threshold.Value;
            double first = ReadNumber(node.Probe, start, source);
            double last = ReadNumber(node.Probe, end, source);
            bool fell = false;
            int fellFrame = -1;
            for (int f = start; f <= end; f++)
            {
                double value = ReadNumber(node.Probe, f, source);
                if (value < threshold)
                {
                    fell = true;
                    fellFrame = f;
                    break;
                }
            }
            if (first >= threshold && fell && last >= threshold)
            {
                reasons.Add(DescribeProbe(node.Probe) + " starts at " + FormatNumber(first) + " at frame " + start + ", falls below " + FormatNumber(threshold) + " at frame " + fellFrame + ", recovers to " + FormatNumber(last) + " at frame " + end);
                return true;
            }
            reasons.Add(DescribeProbe(node.Probe) + " from frame " + start + " (" + FormatNumber(first) + ") to frame " + end + " (" + FormatNumber(last) + ") with threshold " + FormatNumber(threshold) + (fell ? " fell at frame " + fellFrame : " never fell"));
            return false;
        }

        private static bool EvaluateChanged(PredicateNode node, IMeasurementSource source, List<string> reasons)
        {
            if (node.Probe == null)
            {
                throw new MissingDataException("changed node is missing its probe");
            }
            if (!node.WindowStart.HasValue || !node.WindowEnd.HasValue)
            {
                throw new MissingDataException("changed node is missing its window");
            }
            int start = node.WindowStart.Value;
            int end = node.WindowEnd.Value;
            if (end < start)
            {
                throw new MissingDataException("changed node window is reversed");
            }
            string first = ReadNormalized(node.Probe, start, source);
            for (int f = start + 1; f <= end; f++)
            {
                string current = ReadNormalized(node.Probe, f, source);
                if (!current.Equals(first, StringComparison.Ordinal))
                {
                    reasons.Add(DescribeProbe(node.Probe) + " changes from frame " + start + " to frame " + end);
                    return true;
                }
            }
            reasons.Add(DescribeProbe(node.Probe) + " is unchanged from frame " + start + " to frame " + end);
            return false;
        }

        private static bool EvaluateComparison(PredicateNode node, int frame, IMeasurementSource source, List<string> reasons)
        {
            if (node.Probe == null)
            {
                throw new MissingDataException("comparison is missing its probe");
            }
            MeasuredValue value = ReadValue(node.Probe, frame, source);
            string probeName = DescribeProbe(node.Probe);
            string atFrame = " at frame " + frame;
            if (node.NumberValue.HasValue)
            {
                if (!value.Number.HasValue)
                {
                    throw new MissingDataException("measurement " + probeName + atFrame + " is not a number, got " + value.Describe());
                }
                double actual = value.Number.Value;
                double expected = node.NumberValue.Value;
                bool holds = CompareNumbers(actual, node.Op, expected);
                reasons.Add(probeName + atFrame + " = " + FormatNumber(actual) + " " + OpPhrase(node.Op) + " " + FormatNumber(expected));
                return holds;
            }
            if (node.BoolValue.HasValue)
            {
                if (!value.Flag.HasValue)
                {
                    throw new MissingDataException("measurement " + probeName + atFrame + " is not a boolean, got " + value.Describe());
                }
                bool actual = value.Flag.Value;
                bool expected = node.BoolValue.Value;
                bool holds;
                if (node.Op == ComparisonOp.Equal)
                {
                    holds = actual == expected;
                }
                else if (node.Op == ComparisonOp.NotEqual)
                {
                    holds = actual != expected;
                }
                else
                {
                    throw new MissingDataException("boolean comparison uses an unsupported operator");
                }
                reasons.Add(probeName + atFrame + " = " + (actual ? "true" : "false") + " " + OpPhrase(node.Op) + " " + (expected ? "true" : "false"));
                return holds;
            }
            if (node.TextValue != null)
            {
                if (value.Text == null)
                {
                    throw new MissingDataException("measurement " + probeName + atFrame + " is not text, got " + value.Describe());
                }
                string actual = value.Text;
                string expected = node.TextValue;
                bool holds;
                if (node.Op == ComparisonOp.Equal)
                {
                    holds = actual.Equals(expected, StringComparison.Ordinal);
                }
                else if (node.Op == ComparisonOp.NotEqual)
                {
                    holds = !actual.Equals(expected, StringComparison.Ordinal);
                }
                else
                {
                    throw new MissingDataException("text comparison uses an unsupported operator");
                }
                reasons.Add(probeName + atFrame + " = " + actual + " " + OpPhrase(node.Op) + " " + expected);
                return holds;
            }
            throw new MissingDataException("comparison has no constant value");
        }

        private static MeasuredValue ReadValue(ProbeCall probe, int frame, IMeasurementSource source)
        {
            string target = probe.Target;
            if (target != null && target.Length > 0)
            {
                if (!source.TargetExists(target, frame))
                {
                    throw new MissingDataException("target " + target + " is missing at frame " + frame);
                }
            }
            string second = probe.SecondTarget;
            if (second != null && second.Length > 0)
            {
                if (!source.TargetExists(second, frame))
                {
                    throw new MissingDataException("target " + second + " is missing at frame " + frame);
                }
            }
            MeasuredValue value;
            if (!source.TryGetValue(probe, frame, out value) || value == null)
            {
                throw new MissingDataException("measurement " + DescribeProbe(probe) + " at frame " + frame + " is missing");
            }
            if (value.IsMissing)
            {
                throw new MissingDataException("measurement " + DescribeProbe(probe) + " at frame " + frame + " is missing: " + value.MissingReason);
            }
            return value;
        }

        private static double ReadNumber(ProbeCall probe, int frame, IMeasurementSource source)
        {
            MeasuredValue value = ReadValue(probe, frame, source);
            if (!value.Number.HasValue)
            {
                throw new MissingDataException("measurement " + DescribeProbe(probe) + " at frame " + frame + " is not a number, got " + value.Describe());
            }
            return value.Number.Value;
        }

        private static string ReadNormalized(ProbeCall probe, int frame, IMeasurementSource source)
        {
            MeasuredValue value = ReadValue(probe, frame, source);
            return value.NormalizedKey();
        }

        private static bool CompareNumbers(double actual, ComparisonOp op, double expected)
        {
            switch (op)
            {
                case ComparisonOp.LessThan:
                    return actual < expected;
                case ComparisonOp.LessOrEqual:
                    return actual <= expected;
                case ComparisonOp.GreaterThan:
                    return actual > expected;
                case ComparisonOp.GreaterOrEqual:
                    return actual >= expected;
                case ComparisonOp.Equal:
                    return actual == expected;
                case ComparisonOp.NotEqual:
                    return actual != expected;
                default:
                    return false;
            }
        }

        private static string OpPhrase(ComparisonOp op)
        {
            switch (op)
            {
                case ComparisonOp.LessThan:
                    return "is less than";
                case ComparisonOp.LessOrEqual:
                    return "is at most";
                case ComparisonOp.GreaterThan:
                    return "is greater than";
                case ComparisonOp.GreaterOrEqual:
                    return "is at least";
                case ComparisonOp.Equal:
                    return "equals";
                case ComparisonOp.NotEqual:
                    return "is not";
                default:
                    return "compares to";
            }
        }

        private static string FormatNumber(double value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string DescribeProbe(ProbeCall probe)
        {
            string target = probe.Target;
            if (target == null)
            {
                target = string.Empty;
            }
            switch (probe.Probe)
            {
                case ProbeKind.VisiblePixels:
                    return "visiblePixels(" + target + ")";
                case ProbeKind.VisibleFraction:
                    return "visibleFraction(" + target + ")";
                case ProbeKind.RectMinX:
                    return "rectMinX(" + target + ")";
                case ProbeKind.RectMinY:
                    return "rectMinY(" + target + ")";
                case ProbeKind.RectMaxX:
                    return "rectMaxX(" + target + ")";
                case ProbeKind.RectMaxY:
                    return "rectMaxY(" + target + ")";
                case ProbeKind.OverflowPx:
                    return "overflowPx(" + target + "," + probe.SecondTarget + ")";
                case ProbeKind.Inside:
                    return "inside(" + target + "," + probe.SecondTarget + ")";
                case ProbeKind.Alpha:
                    return "alpha(" + target + ")";
                case ProbeKind.Active:
                    return "active(" + target + ")";
                case ProbeKind.TopmostAt:
                    return "topmostAt(" + FormatPoint(probe) + ")";
                case ProbeKind.RaycastTargetAt:
                    return "raycastTargetAt(" + FormatPoint(probe) + ")";
                case ProbeKind.ScreenArea:
                    return "screenArea(" + target + ")";
                case ProbeKind.TextOverflowing:
                    return "textOverflowing(" + target + ")";
                case ProbeKind.Culled:
                    return "culled(" + target + "," + probe.SecondTarget + ")";
                case ProbeKind.Layer:
                    return "layer(" + target + ")";
                case ProbeKind.SortOrder:
                    return "sortOrder(" + target + ")";
                case ProbeKind.MeanColorRed:
                    return "meanColorRed(" + target + ")";
                case ProbeKind.MeanColorGreen:
                    return "meanColorGreen(" + target + ")";
                case ProbeKind.MeanColorBlue:
                    return "meanColorBlue(" + target + ")";
                case ProbeKind.ConsoleErrors:
                    return "consoleErrors()";
                default:
                    return "probe(" + target + ")";
            }
        }

        private static string FormatPoint(ProbeCall probe)
        {
            string x = probe.PointX.HasValue ? FormatNumber(probe.PointX.Value) : "?";
            string y = probe.PointY.HasValue ? FormatNumber(probe.PointY.Value) : "?";
            return x + "," + y;
        }
    }
}
