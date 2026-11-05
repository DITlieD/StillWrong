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



        // 8. Live case from the real 1920x1080 frame: the big wasp's rect is 182x167 but SAM's head-only
        //    refine mask is 81x121. The wasp is kept (not background), and the head-only refine is rejected.
        int FW = 1920, FH = 1080;
        MaskElement fullGround = E("Terrain/Ground", 0, 0, FW, FH, 0);
        MaskElement wide = E("Enemies/BigWasp", 520, 708, 182, 167, 1);
        MaskElement swarm = E("Enemies/SmallWasp", 492, 740, 46, 55, 2);
        var real = new List<MaskElement> { fullGround, wide, swarm };
        bool[,] headOnly = Mask(FW, FH, new Rect2D(562, 734, 81, 121));
        var rHead = MaskRanker.Rank(headOnly, real, new Vec2(608, 818), true);
        Check(rHead.Count > 0 && rHead[0].Element == wide, "wide sprite with a small mask is kept, ground is dropped");
        bool[,] firstPass = Mask(FW, FH, new Rect2D(520, 716, 182, 140), new Rect2D(494, 742, 42, 50));
        var rFirst = MaskRanker.Rank(firstPass, real, new Vec2(608, 818), true);
        Check(rFirst.Count > 0 && rFirst[0].Element == wide, "first pass on the real frame names the big wasp");
        Check(!MaskRanker.RefineIsBetter(rFirst, rHead), "refine that cuts the object to one part is rejected");
        // Live SAM results: box + click + negative gave 9,133 px all inside the big wasp; click only gave 14,659 px
        // with 3,232 px outside it. The clean one must win even though its rect overlap is a little lower.
        // Sizes match the live run: 11,480 px inside the big wasp plus 3,200 px over the small wasp outside it.
        bool[,] liveMerged = Mask(FW, FH, new Rect2D(540, 730, 140, 82), new Rect2D(470, 745, 50, 64));
        var rLive = MaskRanker.Rank(liveMerged, real, new Vec2(608, 770), true);
        Check(rLive.Count > 0 && rLive[0].Element == wide && MaskRanker.NeedsRefine(rLive), "live-sized merged mask names the big wasp and asks for a refine");
        bool[,] boxed = Mask(FW, FH, new Rect2D(545, 735, 130, 70));
        var rBoxed = MaskRanker.Rank(boxed, real, new Vec2(608, 770), true);
        Check(rBoxed.Count > 0 && rBoxed[0].Element == wide && rBoxed[0].MaskShare > 0.99, "boxed refine stays inside the big wasp");
        Check(MaskRanker.RefineIsBetter(rLive, rBoxed), "refine that removes the merge without shrinking the object is kept");
        bool[,] liveHead = Mask(FW, FH, new Rect2D(562, 734, 58, 77));
        Check(!MaskRanker.RefineIsBetter(rLive, MaskRanker.Rank(liveHead, real, new Vec2(608, 770), true)), "live-sized head-only refine is still rejected");
        int bl, bt, br, bb;
        MaskRanker.RefineBox(rFirst[0], FW, FH, out bl, out bt, out br, out bb);
        Check(bl < 520 && br > 702 && bt < 1080 - 875 && bb > 1080 - 708, "refine box covers the big wasp in top-left frame pixels (" + bl + "," + bt + "," + br + "," + bb + ")");
        // Close camera on the Overworld: the wasp billboard's bounds rect is 485x562 on a 1920x1080 frame (13%),
        // its mask about 9000 px. It must stay a candidate.
        MaskElement closeWasp = E("OverworldEnemies/RouteWasp/WaspBillboard", 570, 515, 486, 562, 0);
        MaskElement closeGround = E("Terrain/Chunk_0_0", 0, 0, 1920, 1080, 0);
        var rClose = MaskRanker.Rank(Mask(1920, 1080, new Rect2D(760, 760, 110, 85)), new List<MaskElement> { closeGround, closeWasp }, new Vec2(812, 800), true);
        Check(rClose.Count > 0 && rClose[0].Element == closeWasp && rClose.TrueForAll(r => r.Element != closeGround), "close-up sprite kept, full-frame ground dropped");
        var rTight = MaskRanker.Rank(Mask(FW, FH, new Rect2D(522, 710, 178, 163)), real, new Vec2(608, 818), true);
        Check(MaskRanker.RefineIsBetter(rFirst, rTight), "refine that fits the object better is kept");

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
