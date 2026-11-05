using System;
using System.Collections.Generic;
using StillWrong.Core;

static class P
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    static bool[,] Mask(int w, int h, params Rect2D[] parts)
    {
        bool[,] m = new bool[h, w];
        foreach (Rect2D r in parts)
            for (int y = (int)r.YMin; y < (int)r.YMax; y++)
                for (int x = (int)r.XMin; x < (int)r.XMax; x++)
                    if (x >= 0 && y >= 0 && x < w && y < h) m[y, x] = true;
        return m;
    }
    static MaskElement E(string path, float x, float y, float w, float h, long order = 0, bool ui = false)
    { return new MaskElement { Path = path, Rect = new Rect2D(x, y, w, h), DrawOrder = order, IsUi = ui }; }

    static int Main()
    {
        int W = 400, H = 300;
        MaskElement big = E("Enemies/RouteWaspPair/SecondWasp", 100, 80, 160, 140, 1);
        MaskElement small = E("Enemies/RouteWaspPair/OverworldBody/WaspSwarm", 210, 170, 60, 50, 2);
        MaskElement ground = E("Terrain/Ground", 0, 0, 400, 300, 0);
        MaskElement label = E("UI/SecondWaspLabel", 100, 230, 120, 20, 0, true);
        var scene = new List<MaskElement> { ground, big, small, label };

        // 1. SAM merged both wasps, click on the big one.
        bool[,] merged = Mask(W, H, new Rect2D(105, 85, 150, 130), new Rect2D(212, 172, 56, 46));
        var r1 = MaskRanker.Rank(merged, scene, new Vec2(150, 120), true);
        Check(r1.Count > 0 && r1[0].Element == big, "merged mask, click on big wasp -> big wasp first (got " + (r1.Count > 0 ? r1[0].Element.Path : "none") + ")");
        Check(MaskRanker.NeedsRefine(r1), "merged mask flags a refine pass");
        var neg = MaskRanker.NegativePoints(r1, new Vec2(150, 120), true, 3);
        Check(neg.Count == 1 && small.Rect.Center.X == neg[0].X, "negative point placed on the small wasp");
        Check(r1.TrueForAll(r => r.Element != ground), "full-screen ground is filtered out");

        // 2. Clean mask on the small wasp, click on it -> small wasp first even though big rect contains the click.
        bool[,] smallOnly = Mask(W, H, new Rect2D(212, 172, 56, 46));
        var r2 = MaskRanker.Rank(smallOnly, scene, new Vec2(240, 195), true);
        Check(r2.Count > 0 && r2[0].Element == small, "small-wasp mask -> small wasp first (got " + (r2.Count > 0 ? r2[0].Element.Path : "none") + ")");
        Check(!MaskRanker.NeedsRefine(r2), "clean small mask needs no refine");

        // 3. Clean big mask -> big wasp, small wasp not swallowed.
        bool[,] bigOnly = Mask(W, H, new Rect2D(105, 85, 150, 130));
        var r3 = MaskRanker.Rank(bigOnly, scene, new Vec2(150, 120), true);
        Check(r3.Count > 0 && r3[0].Element == big, "big-wasp mask -> big wasp first");

        // 4. Click without mask -> UI drawn in front wins inside its rect, otherwise front-most smallest.
        var r4 = MaskRanker.Rank(new bool[H, W], scene, new Vec2(240, 195), true);
        Check(r4.Count > 0 && r4[0].Element == small, "no mask, click on small wasp -> small wasp (front, smaller)");

        // 5. Parent and child with the same outline -> parent named first.
        MaskElement parent = E("Enemies/Wasp", 100, 80, 160, 140, 1);
        MaskElement child = E("Enemies/Wasp/Sprite", 100, 80, 160, 140, 1);
        var r5 = MaskRanker.Rank(bigOnly, new List<MaskElement> { child, parent }, new Vec2(150, 120), true);
        Check(r5.Count == 2 && r5[0].Element == parent, "identical outlines -> parent first");

        // 6. Ambiguity: click is inside the small wasp but mask merged -> ambiguous.
        var r6 = MaskRanker.Rank(merged, scene, new Vec2(240, 195), true);
        Check(MaskRanker.IsAmbiguous(r6), "merged mask with click on small wasp is ambiguous");


        // 7. Snapshot file round trip, nearest lookup and scaling to frame size.
        var snap = new FrameObjectsSnapshot { FrameNumber = 30, ScreenWidth = 800, ScreenHeight = 600, Elements = scene };
        string text = FrameObjects.Format(snap) + FrameObjects.Format(new FrameObjectsSnapshot { FrameNumber = 45, ScreenWidth = 800, ScreenHeight = 600 });
        var parsed = FrameObjects.Parse(text);
        Check(parsed.Count == 2 && parsed[0].Elements.Count == 4 && parsed[0].Elements[1].Path == big.Path && parsed[0].Elements[3].IsUi, "snapshot file round trip");
        Check(FrameObjects.Nearest(parsed, 33, 15).FrameNumber == 30 && FrameObjects.Nearest(parsed, 200, 15) == null, "nearest snapshot within gap");
        var scaled = parsed[0].ScaledTo(400, 300);
        Check(Math.Abs(scaled[1].Rect.X - 50) < 0.01 && Math.Abs(scaled[1].Rect.Width - 80) < 0.01, "snapshot scales from screen to frame pixels");

        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        return fails;
    }
}
