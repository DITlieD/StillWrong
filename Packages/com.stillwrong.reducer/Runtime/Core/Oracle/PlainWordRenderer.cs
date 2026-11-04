using System;
using System.Collections.Generic;
using System.Text;

namespace StillWrong.Core.Oracle
{
    public static class PlainWordRenderer
    {
        public static string Render(OracleSpec spec)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Spec ");
            builder.Append(spec.SpecId);
            if (spec.DeveloperSentence != null && spec.DeveloperSentence.Length > 0)
            {
                builder.Append(": ");
                builder.Append(spec.DeveloperSentence);
            }
            if (spec.WindowStart.HasValue && spec.WindowEnd.HasValue)
            {
                builder.Append(" Window frames ");
                builder.Append(spec.WindowStart.Value);
                builder.Append(" to ");
                builder.Append(spec.WindowEnd.Value);
                builder.Append(".");
            }
            builder.Append(" Fail when ");
            builder.Append(RenderPredicate(spec.Failure));
            builder.Append(".");
            if (spec.Invariants != null)
            {
                for (int i = 0; i < spec.Invariants.Count; i++)
                {
                    builder.Append(" Expect ");
                    builder.Append(RenderPredicate(spec.Invariants[i]));
                    builder.Append(".");
                }
            }
            return builder.ToString();
        }

        public static string RenderPredicate(PredicateNode node)
        {
            switch (node.Kind)
            {
                case PredicateKind.Comparison:
                    return RenderComparison(node);
                case PredicateKind.And:
                    return RenderJoined(node.Children, " and ");
                case PredicateKind.Or:
                    return RenderJoined(node.Children, " or ");
                case PredicateKind.Not:
                    return "not (" + RenderPredicate(node.Children[0]) + ")";
                case PredicateKind.Always:
                    return "always (" + RenderPredicate(node.Children[0]) + ") from frame " + node.WindowStart.Value + " to frame " + node.WindowEnd.Value;
                case PredicateKind.Ever:
                    return "ever (" + RenderPredicate(node.Children[0]) + ") from frame " + node.WindowStart.Value + " to frame " + node.WindowEnd.Value;
                case PredicateKind.DropsThenRecovers:
                    return RenderProbe(node.Probe) + " drops below " + FormatValue(node.Threshold.Value) + " then recovers from frame " + node.WindowStart.Value + " to frame " + node.WindowEnd.Value;
                case PredicateKind.Changed:
                    return RenderProbe(node.Probe) + " changes from frame " + node.WindowStart.Value + " to frame " + node.WindowEnd.Value;
                default:
                    return "unknown";
            }
        }

        private static string RenderJoined(List<PredicateNode> children, string joiner)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < children.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(joiner);
                }
                builder.Append("(");
                builder.Append(RenderPredicate(children[i]));
                builder.Append(")");
            }
            return builder.ToString();
        }

        private static string RenderComparison(PredicateNode node)
        {
            string probe = RenderProbe(node.Probe);
            if (node.NumberValue.HasValue)
            {
                return probe + " " + OpWords(node.Op) + " " + FormatValue(node.NumberValue.Value);
            }
            if (node.BoolValue.HasValue)
            {
                string expected = node.BoolValue.Value ? "true" : "false";
                if (node.Op == ComparisonOp.Equal)
                {
                    return probe + " is " + expected;
                }
                return probe + " is not " + expected;
            }
            if (node.TextValue != null)
            {
                if (node.Op == ComparisonOp.Equal)
                {
                    return probe + " equals " + node.TextValue;
                }
                return probe + " is not " + node.TextValue;
            }
            return probe + " compares";
        }

        private static string OpWords(ComparisonOp op)
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

        private static string FormatValue(double value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string RenderProbe(ProbeCall probe)
        {
            string target = probe.Target;
            if (target == null)
            {
                target = string.Empty;
            }
            switch (probe.Probe)
            {
                case ProbeKind.VisiblePixels:
                    return "visible pixels of " + target;
                case ProbeKind.VisibleFraction:
                    return "visible fraction of " + target;
                case ProbeKind.RectMinX:
                    return "left edge of " + target;
                case ProbeKind.RectMinY:
                    return "bottom edge of " + target;
                case ProbeKind.RectMaxX:
                    return "right edge of " + target;
                case ProbeKind.RectMaxY:
                    return "top edge of " + target;
                case ProbeKind.OverflowPx:
                    return "overflow of " + target + " beyond " + probe.SecondTarget;
                case ProbeKind.Inside:
                    return target + " inside " + probe.SecondTarget;
                case ProbeKind.Alpha:
                    return "alpha of " + target;
                case ProbeKind.Active:
                    return target + " is active";
                case ProbeKind.TopmostAt:
                    return "topmost element at (" + FormatPoint(probe) + ")";
                case ProbeKind.RaycastTargetAt:
                    return "raycast target at (" + FormatPoint(probe) + ")";
                case ProbeKind.ScreenArea:
                    return "screen area of " + target;
                case ProbeKind.TextOverflowing:
                    return "text of " + target + " overflows";
                case ProbeKind.Culled:
                    return target + " culled by " + probe.SecondTarget;
                case ProbeKind.Layer:
                    return "layer of " + target;
                case ProbeKind.SortOrder:
                    return "sort order of " + target;
                case ProbeKind.MeanColorRed:
                    return "mean red of " + target;
                case ProbeKind.MeanColorGreen:
                    return "mean green of " + target;
                case ProbeKind.MeanColorBlue:
                    return "mean blue of " + target;
                case ProbeKind.ConsoleErrors:
                    return "console errors";
                default:
                    return "probe of " + target;
            }
        }

        private static string FormatPoint(ProbeCall probe)
        {
            string x = probe.PointX.HasValue ? FormatValue(probe.PointX.Value) : "?";
            string y = probe.PointY.HasValue ? FormatValue(probe.PointY.Value) : "?";
            return x + ", " + y;
        }
    }
}
