using System;
using System.Collections.Generic;
using System.Linq;
using StillWrong.Core;

// Simulates a reduction on a synthetic scene and counts trials. Removing a node removes its subtree,
// like DestroyImmediate on a GameObject. The bug stays only while every "needed" node is present.
static class P
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    sealed class Scene
    {
        public List<ReductionNode> Nodes = new List<ReductionNode>();
        public Dictionary<string, string> Parent = new Dictionary<string, string>();
        public void Add(string id, string parent, IEnumerable<string> children)
        { Nodes.Add(new ReductionNode(id, parent, children)); Parent[id] = parent; }
    }

    static Scene Build(int roots, int childrenPerRoot, int grandPerChild)
    {
        var s = new Scene();
        void Root(string name, int kids, int grand)
        {
            var kidIds = Enumerable.Range(0, kids).Select(k => name + "/c" + k).ToList();
            s.Add(name, null, kidIds);
            foreach (var k in kidIds)
            {
                var g = Enumerable.Range(0, grand).Select(x => k + "/g" + x).ToList();
                s.Add(k, name, g);
                foreach (var gg in g) s.Add(gg, k, null);
            }
        }
        for (int r = 0; r < roots; r++) Root("Root" + r, childrenPerRoot, grandPerChild);
        s.Add("Main Camera", null, null);
        s.Add("EventSystem", null, null);
        s.Add("Lighting", null, new[] { "Lighting/Sun" });
        s.Add("Lighting/Sun", "Lighting", null);
        s.Add("OverworldEnemies", null, new[] { "OverworldEnemies/RouteWaspPair", "OverworldEnemies/Others" });
        s.Add("OverworldEnemies/Others", "OverworldEnemies", Enumerable.Range(0, 40).Select(i => "OverworldEnemies/Others/e" + i));
        for (int i = 0; i < 40; i++) s.Add("OverworldEnemies/Others/e" + i, "OverworldEnemies/Others", null);
        s.Add("OverworldEnemies/RouteWaspPair", "OverworldEnemies", new[] { "OverworldEnemies/RouteWaspPair/SecondWasp", "OverworldEnemies/RouteWaspPair/OverworldBody" });
        s.Add("OverworldEnemies/RouteWaspPair/SecondWasp", "OverworldEnemies/RouteWaspPair", null);
        s.Add("OverworldEnemies/RouteWaspPair/OverworldBody", "OverworldEnemies/RouteWaspPair", null);
        return s;
    }

    static bool Removed(Scene s, ISet<string> removals, string id)
    {
        for (string cur = id; cur != null; cur = s.Parent.TryGetValue(cur, out var p) ? p : null)
            if (removals.Contains(cur)) return true;
        return false;
    }

    static (ReductionResult, int) Run(Scene s, string[] needed)
    {
        var ctx = new ReductionContext(s.Nodes, "OverworldEnemies/RouteWaspPair/SecondWasp", "Main Camera", "EventSystem");
        var session = new ReductionSession(ctx);
        int trials = 0;
        while (true)
        {
            var t = session.NextTrial();
            if (t == null) break;
            trials++;
            bool ok = needed.All(n => !Removed(s, t, n));
            session.Report(ok ? OracleVerdict.StillWrong : OracleVerdict.HealthyPass);
            if (trials > 100000) throw new Exception("runaway");
        }
        return (session.FinalResult, trials);
    }

    static void Case(string name, Scene s, string[] needed, int maxTrials)
    {
        var (r, trials) = Run(s, needed);
        var removed = new HashSet<string>(r.RemovedNodeIds);
        bool bugKept = needed.All(n => !Removed(s, removed, n));
        int survivors = s.Nodes.Count(n => !Removed(s, removed, n.Id));
        Console.WriteLine($"  {name}: {s.Nodes.Count} objects, {trials} trials, {survivors} survive");
        Check(bugKept, name + ": the bug still reproduces in the result");
        Check(trials <= maxTrials, name + $": at most {maxTrials} trials (got {trials})");
    }

    static int Main(string[] args)
    {
        int budget = args.Length > 0 ? int.Parse(args[0]) : 100000;
        // Shaped like the Overworld run: about 4,500 objects, the bug needs only the target.
        Case("overworld, target only", Build(30, 30, 4), new[] { "OverworldEnemies/RouteWaspPair/SecondWasp" }, budget);
        // The bug also needs one unprotected object elsewhere (the sun light).
        Case("overworld, target + light", Build(30, 30, 4), new[] { "OverworldEnemies/RouteWaspPair/SecondWasp", "Lighting/Sun" }, budget);
        // The bug needs three scattered leaves.
        Case("overworld, scattered deps", Build(30, 30, 4), new[] { "OverworldEnemies/RouteWaspPair/SecondWasp", "Root7/c3/g1", "Root22/c19" }, budget);
        // A flat scene with no hierarchy.
        var flat = new Scene();
        for (int i = 0; i < 1500; i++) flat.Add("obj" + i, null, null);
        flat.Add("Main Camera", null, null); flat.Add("EventSystem", null, null);
        flat.Add("OverworldEnemies/RouteWaspPair/SecondWasp", null, null);
        Case("flat 1,500 objects, two deps", flat, new[] { "OverworldEnemies/RouteWaspPair/SecondWasp", "obj777" }, budget);
        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        return fails;
    }
}
