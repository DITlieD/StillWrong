using System;
using System.Collections.Generic;

namespace StillWrong.Core.Oracle
{
    public enum ProbeKind
    {
        VisiblePixels,
        VisibleFraction,
        RectMinX,
        RectMinY,
        RectMaxX,
        RectMaxY,
        OverflowPx,
        Inside,
        Alpha,
        Active,
        TopmostAt,
        RaycastTargetAt,
        ScreenArea,
        TextOverflowing,
        Culled,
        Layer,
        SortOrder,
        MeanColorRed,
        MeanColorGreen,
        MeanColorBlue,
        ConsoleErrors,
        Scale
    }

    public enum PredicateKind
    {
        Comparison,
        And,
        Or,
        Not,
        Always,
        Ever,
        DropsThenRecovers,
        Changed
    }

    public enum ComparisonOp
    {
        LessThan,
        LessOrEqual,
        GreaterThan,
        GreaterOrEqual,
        Equal,
        NotEqual
    }

    public sealed class TargetRef
    {
        public string Name { get; set; }
        public string ElementId { get; set; }

        public TargetRef()
        {
            Name = string.Empty;
            ElementId = string.Empty;
        }

        public TargetRef(string name, string elementId)
        {
            Name = name;
            ElementId = elementId;
        }
    }

    public sealed class ProbeCall
    {
        public ProbeKind Probe { get; set; }
        public string Target { get; set; }
        public string SecondTarget { get; set; }
        public double? PointX { get; set; }
        public double? PointY { get; set; }
        public double? RegionX { get; set; }
        public double? RegionY { get; set; }
        public double? RegionW { get; set; }
        public double? RegionH { get; set; }

        public ProbeCall()
        {
            Target = string.Empty;
        }

        public string StableKey()
        {
            string second = SecondTarget;
            if (second == null)
            {
                second = string.Empty;
            }
            string point = string.Empty;
            if (PointX.HasValue && PointY.HasValue)
            {
                point = PointX.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + PointY.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            string region = string.Empty;
            if (RegionX.HasValue && RegionY.HasValue && RegionW.HasValue && RegionH.HasValue)
            {
                region = RegionX.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + RegionY.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + RegionW.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + RegionH.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return Probe.ToString() + "|" + Target + "|" + second + "|" + point + "|" + region;
        }
    }

    public sealed class PredicateNode
    {
        public PredicateKind Kind { get; set; }
        public ProbeCall Probe { get; set; }
        public ComparisonOp Op { get; set; }
        public double? NumberValue { get; set; }
        public bool? BoolValue { get; set; }
        public string TextValue { get; set; }
        public List<PredicateNode> Children { get; set; }
        public int? WindowStart { get; set; }
        public int? WindowEnd { get; set; }
        public double? Threshold { get; set; }

        public PredicateNode()
        {
            Children = new List<PredicateNode>();
        }
    }

    public sealed class OracleSpec
    {
        public string SpecId { get; set; }
        public string DeveloperSentence { get; set; }
        public List<TargetRef> Targets { get; set; }
        public int? WindowStart { get; set; }
        public int? WindowEnd { get; set; }
        public List<int> MeasurementFrames { get; set; }
        public PredicateNode Failure { get; set; }
        public List<PredicateNode> Invariants { get; set; }

        public OracleSpec()
        {
            SpecId = string.Empty;
            DeveloperSentence = string.Empty;
            Targets = new List<TargetRef>();
            MeasurementFrames = new List<int>();
            Invariants = new List<PredicateNode>();
        }
    }

    public sealed class MeasuredValue
    {
        public bool IsMissing { get; set; }
        public string MissingReason { get; set; }
        public double? Number { get; set; }
        public bool? Flag { get; set; }
        public string Text { get; set; }

        public MeasuredValue()
        {
            MissingReason = string.Empty;
        }

        public static MeasuredValue FromNumber(double value)
        {
            MeasuredValue result = new MeasuredValue();
            result.Number = value;
            return result;
        }

        public static MeasuredValue FromBool(bool value)
        {
            MeasuredValue result = new MeasuredValue();
            result.Flag = value;
            return result;
        }

        public static MeasuredValue FromText(string value)
        {
            MeasuredValue result = new MeasuredValue();
            result.Text = value;
            return result;
        }

        public static MeasuredValue Missing(string reason)
        {
            MeasuredValue result = new MeasuredValue();
            result.IsMissing = true;
            result.MissingReason = reason;
            return result;
        }

        public string Describe()
        {
            if (IsMissing)
            {
                return "missing (" + MissingReason + ")";
            }
            if (Number.HasValue)
            {
                return Number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (Flag.HasValue)
            {
                return Flag.Value ? "true" : "false";
            }
            if (Text != null)
            {
                return Text;
            }
            return "missing (empty)";
        }

        public string NormalizedKey()
        {
            if (IsMissing)
            {
                return "missing";
            }
            if (Number.HasValue)
            {
                return "n:" + Number.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (Flag.HasValue)
            {
                return Flag.Value ? "b:true" : "b:false";
            }
            return "t:" + Text;
        }
    }

    public static class Probes
    {
        private static ProbeCall Build(ProbeKind kind, string target)
        {
            ProbeCall call = new ProbeCall();
            call.Probe = kind;
            call.Target = target;
            return call;
        }

        public static ProbeCall VisiblePixels(string target)
        {
            return Build(ProbeKind.VisiblePixels, target);
        }

        public static ProbeCall VisibleFraction(string target)
        {
            return Build(ProbeKind.VisibleFraction, target);
        }

        public static ProbeCall RectMinX(string target)
        {
            return Build(ProbeKind.RectMinX, target);
        }

        public static ProbeCall RectMinY(string target)
        {
            return Build(ProbeKind.RectMinY, target);
        }

        public static ProbeCall RectMaxX(string target)
        {
            return Build(ProbeKind.RectMaxX, target);
        }

        public static ProbeCall RectMaxY(string target)
        {
            return Build(ProbeKind.RectMaxY, target);
        }

        public static ProbeCall OverflowPx(string target, string container)
        {
            ProbeCall call = Build(ProbeKind.OverflowPx, target);
            call.SecondTarget = container;
            return call;
        }

        public static ProbeCall Inside(string target, string container)
        {
            ProbeCall call = Build(ProbeKind.Inside, target);
            call.SecondTarget = container;
            return call;
        }

        public static ProbeCall Alpha(string target)
        {
            return Build(ProbeKind.Alpha, target);
        }

        public static ProbeCall Active(string target)
        {
            return Build(ProbeKind.Active, target);
        }

        public static ProbeCall TopmostAt(double x, double y)
        {
            ProbeCall call = new ProbeCall();
            call.Probe = ProbeKind.TopmostAt;
            call.Target = string.Empty;
            call.PointX = x;
            call.PointY = y;
            return call;
        }

        public static ProbeCall RaycastTargetAt(double x, double y)
        {
            ProbeCall call = new ProbeCall();
            call.Probe = ProbeKind.RaycastTargetAt;
            call.Target = string.Empty;
            call.PointX = x;
            call.PointY = y;
            return call;
        }

        public static ProbeCall ScreenArea(string target)
        {
            return Build(ProbeKind.ScreenArea, target);
        }

        public static ProbeCall TextOverflowing(string target)
        {
            return Build(ProbeKind.TextOverflowing, target);
        }

        public static ProbeCall Culled(string target, string camera)
        {
            ProbeCall call = Build(ProbeKind.Culled, target);
            call.SecondTarget = camera;
            return call;
        }

        public static ProbeCall Layer(string target)
        {
            return Build(ProbeKind.Layer, target);
        }

        public static ProbeCall SortOrder(string target)
        {
            return Build(ProbeKind.SortOrder, target);
        }

        public static ProbeCall MeanColorRed(string target)
        {
            return Build(ProbeKind.MeanColorRed, target);
        }

        public static ProbeCall MeanColorGreen(string target)
        {
            return Build(ProbeKind.MeanColorGreen, target);
        }

        public static ProbeCall MeanColorBlue(string target)
        {
            return Build(ProbeKind.MeanColorBlue, target);
        }

        public static ProbeCall ConsoleErrors()
        {
            ProbeCall call = new ProbeCall();
            call.Probe = ProbeKind.ConsoleErrors;
            call.Target = string.Empty;
            return call;
        }
    }

    public static class Predicates
    {
        public static PredicateNode LessThan(ProbeCall probe, double value)
        {
            return Comparison(probe, ComparisonOp.LessThan, value);
        }

        public static PredicateNode LessOrEqual(ProbeCall probe, double value)
        {
            return Comparison(probe, ComparisonOp.LessOrEqual, value);
        }

        public static PredicateNode GreaterThan(ProbeCall probe, double value)
        {
            return Comparison(probe, ComparisonOp.GreaterThan, value);
        }

        public static PredicateNode GreaterOrEqual(ProbeCall probe, double value)
        {
            return Comparison(probe, ComparisonOp.GreaterOrEqual, value);
        }

        public static PredicateNode EqualNumber(ProbeCall probe, double value)
        {
            return Comparison(probe, ComparisonOp.Equal, value);
        }

        public static PredicateNode EqualBool(ProbeCall probe, bool value)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Comparison;
            node.Probe = probe;
            node.Op = ComparisonOp.Equal;
            node.BoolValue = value;
            return node;
        }

        public static PredicateNode EqualText(ProbeCall probe, string value)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Comparison;
            node.Probe = probe;
            node.Op = ComparisonOp.Equal;
            node.TextValue = value;
            return node;
        }

        public static PredicateNode NotEqualText(ProbeCall probe, string value)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Comparison;
            node.Probe = probe;
            node.Op = ComparisonOp.NotEqual;
            node.TextValue = value;
            return node;
        }

        public static PredicateNode NotEqualNumber(ProbeCall probe, double value)
        {
            return Comparison(probe, ComparisonOp.NotEqual, value);
        }

        private static PredicateNode Comparison(ProbeCall probe, ComparisonOp op, double value)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Comparison;
            node.Probe = probe;
            node.Op = op;
            node.NumberValue = value;
            return node;
        }

        public static PredicateNode And(params PredicateNode[] children)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.And;
            node.Children = new List<PredicateNode>(children);
            return node;
        }

        public static PredicateNode Or(params PredicateNode[] children)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Or;
            node.Children = new List<PredicateNode>(children);
            return node;
        }

        public static PredicateNode Not(PredicateNode child)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Not;
            node.Children = new List<PredicateNode>();
            node.Children.Add(child);
            return node;
        }

        public static PredicateNode Always(PredicateNode child, int windowStart, int windowEnd)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Always;
            node.Children = new List<PredicateNode>();
            node.Children.Add(child);
            node.WindowStart = windowStart;
            node.WindowEnd = windowEnd;
            return node;
        }

        public static PredicateNode Ever(PredicateNode child, int windowStart, int windowEnd)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Ever;
            node.Children = new List<PredicateNode>();
            node.Children.Add(child);
            node.WindowStart = windowStart;
            node.WindowEnd = windowEnd;
            return node;
        }

        public static PredicateNode DropsThenRecovers(ProbeCall probe, double threshold, int windowStart, int windowEnd)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.DropsThenRecovers;
            node.Probe = probe;
            node.Threshold = threshold;
            node.WindowStart = windowStart;
            node.WindowEnd = windowEnd;
            return node;
        }

        public static PredicateNode Changed(ProbeCall probe, int windowStart, int windowEnd)
        {
            PredicateNode node = new PredicateNode();
            node.Kind = PredicateKind.Changed;
            node.Probe = probe;
            node.WindowStart = windowStart;
            node.WindowEnd = windowEnd;
            return node;
        }
    }
}
