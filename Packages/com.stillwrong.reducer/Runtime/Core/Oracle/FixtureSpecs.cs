using System.Collections.Generic;

namespace StillWrong.Core.Oracle
{
    public static class FixtureSpecs
    {
        public static OracleSpec C1()
        {
            OracleSpec spec = new OracleSpec();
            spec.SpecId = "C1-ui-clip";
            spec.DeveloperSentence = "Tooltip text is clipped by its container.";
            spec.Targets = new List<TargetRef>();
            spec.Targets.Add(new TargetRef("body", "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody"));
            spec.Targets.Add(new TargetRef("mask", "BattleCanvas/ContextFlyout/HotbarSlotCard"));
            spec.WindowStart = 6;
            spec.WindowEnd = 6;
            spec.MeasurementFrames = new List<int>();
            spec.MeasurementFrames.Add(6);
            spec.Failure = Predicates.And(
                Predicates.GreaterThan(Probes.OverflowPx("body", "mask"), 12.0),
                Predicates.EqualBool(Probes.Inside("body", "mask"), false));
            spec.Invariants = new List<PredicateNode>();
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("body"), true));
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("mask"), true));
            return spec;
        }

        public static OracleSpec C2()
        {
            OracleSpec spec = new OracleSpec();
            spec.SpecId = "C2-sorting-occlusion";
            spec.DeveloperSentence = "Tavern button is covered or clicks go to the wrong element.";
            spec.Targets = new List<TargetRef>();
            spec.Targets.Add(new TargetRef("button", "TavernPromptCanvas/OpenTavernButton"));
            spec.Targets.Add(new TargetRef("dimmer", "ModalBackdropCanvas/FullscreenDimmer"));
            spec.Targets.Add(new TargetRef("blocker", "ScreenOverlayCanvas/TransparentBlocker"));
            spec.WindowStart = 3;
            spec.WindowEnd = 3;
            spec.MeasurementFrames = new List<int>();
            spec.MeasurementFrames.Add(3);
            PredicateNode visual = Predicates.And(
                Predicates.EqualBool(Probes.Inside("button", "dimmer"), true),
                Predicates.LessThan(Probes.VisibleFraction("button"), 0.10),
                Predicates.NotEqualText(Probes.TopmostAt(960.0, 420.0), "TavernPromptCanvas/OpenTavernButton"));
            PredicateNode interceptor = Predicates.And(
                Predicates.NotEqualText(Probes.RaycastTargetAt(960.0, 420.0), "TavernPromptCanvas/OpenTavernButton"),
                Predicates.GreaterThan(Probes.VisibleFraction("button"), 0.85),
                Predicates.EqualBool(Probes.Active("button"), true));
            spec.Failure = Predicates.Or(visual, interceptor);
            spec.Invariants = new List<PredicateNode>();
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("button"), true));
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("dimmer"), true));
            return spec;
        }

        public static OracleSpec C3()
        {
            OracleSpec spec = new OracleSpec();
            spec.SpecId = "C3-transient-disappear";
            spec.DeveloperSentence = "Badge icon vanishes during the collapse transition then comes back.";
            spec.Targets = new List<TargetRef>();
            spec.Targets.Add(new TargetRef("badge", "GuidanceHud/QuestTracker/ObjectiveBadgeIcon"));
            spec.Targets.Add(new TargetRef("panel", "GuidanceHud/QuestTracker"));
            spec.WindowStart = 10;
            spec.WindowEnd = 18;
            spec.MeasurementFrames = new List<int>();
            spec.MeasurementFrames.Add(18);
            spec.Failure = Predicates.And(
                Predicates.DropsThenRecovers(Probes.Alpha("badge"), 0.01, 10, 18),
                Predicates.GreaterThan(Probes.Alpha("badge"), 0.30));
            spec.Invariants = new List<PredicateNode>();
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("badge"), true));
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("panel"), true));
            return spec;
        }

        public static OracleSpec C4()
        {
            OracleSpec spec = new OracleSpec();
            spec.SpecId = "C4-heldout-second-project";
            spec.DeveloperSentence = "Status plate disappears while the dummy stays visible.";
            spec.Targets = new List<TargetRef>();
            spec.Targets.Add(new TargetRef("dummy", "ArenaCombatants/TrainingDummy/MeshRenderer"));
            spec.Targets.Add(new TargetRef("plate", "ArenaCombatants/TrainingDummy/StatusBarAnchor/UIDocument_HealthPlate"));
            spec.Targets.Add(new TargetRef("camera", "PlayerCameraRig/MainCamera"));
            spec.WindowStart = 6;
            spec.WindowEnd = 6;
            spec.MeasurementFrames = new List<int>();
            spec.MeasurementFrames.Add(6);
            PredicateNode culled = Predicates.Or(
                Predicates.EqualBool(Probes.Culled("plate", "camera"), true),
                Predicates.EqualNumber(Probes.VisiblePixels("plate"), 0.0));
            spec.Failure = Predicates.And(
                Predicates.GreaterThan(Probes.ScreenArea("dummy"), 500.0),
                culled);
            spec.Invariants = new List<PredicateNode>();
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("dummy"), true));
            spec.Invariants.Add(Predicates.EqualBool(Probes.Active("plate"), true));
            return spec;
        }

        public static List<OracleSpec> All()
        {
            List<OracleSpec> specs = new List<OracleSpec>();
            specs.Add(C1());
            specs.Add(C2());
            specs.Add(C3());
            specs.Add(C4());
            return specs;
        }
    }
}
