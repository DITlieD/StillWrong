using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace StillWrong.Core.Oracle
{
    public sealed class OracleSerializer
    {
        private readonly JsonSerializerSettings settings;

        public OracleSerializer()
        {
            settings = new JsonSerializerSettings();
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            settings.Formatting = Formatting.None;
            settings.NullValueHandling = NullValueHandling.Include;
        }

        public string Write(OracleSpec spec)
        {
            return JsonConvert.SerializeObject(ToDto(spec), settings);
        }

        public OracleSpec Read(string json)
        {
            SpecDto dto = JsonConvert.DeserializeObject<SpecDto>(json, settings);
            return FromDto(dto);
        }

        private static SpecDto ToDto(OracleSpec spec)
        {
            SpecDto dto = new SpecDto();
            dto.SpecId = spec.SpecId;
            dto.DeveloperSentence = spec.DeveloperSentence;
            dto.Targets = new List<TargetDto>();
            if (spec.Targets != null)
            {
                for (int i = 0; i < spec.Targets.Count; i++)
                {
                    TargetDto target = new TargetDto();
                    target.Name = spec.Targets[i].Name;
                    target.ElementId = spec.Targets[i].ElementId;
                    dto.Targets.Add(target);
                }
            }
            dto.WindowStart = spec.WindowStart;
            dto.WindowEnd = spec.WindowEnd;
            dto.MeasurementFrames = new List<int>();
            if (spec.MeasurementFrames != null)
            {
                for (int i = 0; i < spec.MeasurementFrames.Count; i++)
                {
                    dto.MeasurementFrames.Add(spec.MeasurementFrames[i]);
                }
            }
            dto.Failure = ToPredicateDto(spec.Failure);
            dto.Invariants = new List<PredicateDto>();
            if (spec.Invariants != null)
            {
                for (int i = 0; i < spec.Invariants.Count; i++)
                {
                    dto.Invariants.Add(ToPredicateDto(spec.Invariants[i]));
                }
            }
            return dto;
        }

        private static PredicateDto ToPredicateDto(PredicateNode node)
        {
            PredicateDto dto = new PredicateDto();
            dto.Kind = node.Kind.ToString();
            if (node.Probe != null)
            {
                ProbeDto probe = new ProbeDto();
                probe.Probe = node.Probe.Probe.ToString();
                probe.Target = node.Probe.Target;
                probe.SecondTarget = node.Probe.SecondTarget;
                probe.PointX = node.Probe.PointX;
                probe.PointY = node.Probe.PointY;
                probe.RegionX = node.Probe.RegionX;
                probe.RegionY = node.Probe.RegionY;
                probe.RegionW = node.Probe.RegionW;
                probe.RegionH = node.Probe.RegionH;
                dto.Probe = probe;
            }
            else
            {
                dto.Probe = null;
            }
            dto.Op = node.Op.ToString();
            dto.NumberValue = node.NumberValue;
            dto.BoolValue = node.BoolValue;
            dto.TextValue = node.TextValue;
            dto.Children = new List<PredicateDto>();
            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    dto.Children.Add(ToPredicateDto(node.Children[i]));
                }
            }
            dto.WindowStart = node.WindowStart;
            dto.WindowEnd = node.WindowEnd;
            dto.Threshold = node.Threshold;
            return dto;
        }

        private static OracleSpec FromDto(SpecDto dto)
        {
            OracleSpec spec = new OracleSpec();
            spec.SpecId = dto.SpecId;
            spec.DeveloperSentence = dto.DeveloperSentence;
            spec.Targets = new List<TargetRef>();
            if (dto.Targets != null)
            {
                for (int i = 0; i < dto.Targets.Count; i++)
                {
                    spec.Targets.Add(new TargetRef(dto.Targets[i].Name, dto.Targets[i].ElementId));
                }
            }
            spec.WindowStart = dto.WindowStart;
            spec.WindowEnd = dto.WindowEnd;
            spec.MeasurementFrames = new List<int>();
            if (dto.MeasurementFrames != null)
            {
                for (int i = 0; i < dto.MeasurementFrames.Count; i++)
                {
                    spec.MeasurementFrames.Add(dto.MeasurementFrames[i]);
                }
            }
            spec.Failure = FromPredicateDto(dto.Failure);
            spec.Invariants = new List<PredicateNode>();
            if (dto.Invariants != null)
            {
                for (int i = 0; i < dto.Invariants.Count; i++)
                {
                    spec.Invariants.Add(FromPredicateDto(dto.Invariants[i]));
                }
            }
            return spec;
        }

        private static PredicateNode FromPredicateDto(PredicateDto dto)
        {
            PredicateNode node = new PredicateNode();
            PredicateKind kind;
            if (Enum.TryParse<PredicateKind>(dto.Kind, out kind))
            {
                node.Kind = kind;
            }
            if (dto.Probe != null)
            {
                ProbeCall probe = new ProbeCall();
                ProbeKind probeKind;
                if (Enum.TryParse<ProbeKind>(dto.Probe.Probe, out probeKind))
                {
                    probe.Probe = probeKind;
                }
                probe.Target = dto.Probe.Target;
                probe.SecondTarget = dto.Probe.SecondTarget;
                probe.PointX = dto.Probe.PointX;
                probe.PointY = dto.Probe.PointY;
                probe.RegionX = dto.Probe.RegionX;
                probe.RegionY = dto.Probe.RegionY;
                probe.RegionW = dto.Probe.RegionW;
                probe.RegionH = dto.Probe.RegionH;
                node.Probe = probe;
            }
            ComparisonOp op;
            if (dto.Op != null && Enum.TryParse<ComparisonOp>(dto.Op, out op))
            {
                node.Op = op;
            }
            node.NumberValue = dto.NumberValue;
            node.BoolValue = dto.BoolValue;
            node.TextValue = dto.TextValue;
            node.Children = new List<PredicateNode>();
            if (dto.Children != null)
            {
                for (int i = 0; i < dto.Children.Count; i++)
                {
                    node.Children.Add(FromPredicateDto(dto.Children[i]));
                }
            }
            node.WindowStart = dto.WindowStart;
            node.WindowEnd = dto.WindowEnd;
            node.Threshold = dto.Threshold;
            return node;
        }

        private sealed class SpecDto
        {
            public string SpecId { get; set; }
            public string DeveloperSentence { get; set; }
            public List<TargetDto> Targets { get; set; }
            public int? WindowStart { get; set; }
            public int? WindowEnd { get; set; }
            public List<int> MeasurementFrames { get; set; }
            public PredicateDto Failure { get; set; }
            public List<PredicateDto> Invariants { get; set; }
        }

        private sealed class TargetDto
        {
            public string Name { get; set; }
            public string ElementId { get; set; }
        }

        private sealed class ProbeDto
        {
            public string Probe { get; set; }
            public string Target { get; set; }
            public string SecondTarget { get; set; }
            public double? PointX { get; set; }
            public double? PointY { get; set; }
            public double? RegionX { get; set; }
            public double? RegionY { get; set; }
            public double? RegionW { get; set; }
            public double? RegionH { get; set; }
        }

        private sealed class PredicateDto
        {
            public string Kind { get; set; }
            public ProbeDto Probe { get; set; }
            public string Op { get; set; }
            public double? NumberValue { get; set; }
            public bool? BoolValue { get; set; }
            public string TextValue { get; set; }
            public List<PredicateDto> Children { get; set; }
            public int? WindowStart { get; set; }
            public int? WindowEnd { get; set; }
            public double? Threshold { get; set; }
        }
    }
}
