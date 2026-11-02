using System;
using LuanShi.Engine;

internal static partial class Harness
{
    // ---- LS-017: one transform for drawing, layout and pointer input --------------
    //
    // The UI draws through GUI.matrix = Scale(fit x uiScale) and anchors its panels to the visible
    // design viewport (UiLayout). These assertions exercise the same pure arithmetic the adapter uses,
    // at 80/100/120% on 16:9 and 4:3 windows. They do not (and cannot) prove that Unity reports
    // Event.mousePosition in GUI space or that a real wheel/click lands where it is drawn: that stays a
    // manual editor check. What they do prove is that the drawing transform, the layout and the
    // Input.mousePosition inverse agree, and that the right HUD, modals and dock stay on screen.

    private static void RunUiScale()
    {
        RunUiScaleReferenceLayout();
        RunUiScaleTransformValues();
        RunUiScaleLayoutAnchors();
        RunUiScalePointerAgreement();
        RunUiScaleModalFit();
        RunUiScaleSelectionPopup();
        RunUiScaleBattleModal();
    }

    // ---- 1: 100% on 16:9 must be the unchanged reference layout -------------------

    private static void RunUiScaleReferenceLayout()
    {
        Section("[UI-SCALE] 100% 16:9 is the unchanged 1600x900 reference layout");

        UiViewport viewport = UiPointerMapping.Viewport(1600, 900, 1f);
        UiLayoutMetrics layout = UiLayout.Compute(viewport);
        Check(Near(viewport.Scale, 1f) && Near(viewport.Width, 1600f) && Near(viewport.Height, 900f),
            "[UI-SCALE] a 1600x900 window at 100% is the identity transform");

        Check(Near(layout.TabColumnX, 1400f) && Near(layout.ContentRight, 1390f)
              && Near(layout.TopBarWidth, 1380f) && Near(layout.StatusBarWidth, 1380f)
              && Near(layout.StatusBarY, 846f) && Near(layout.SidePanelX, 960f)
              && Near(layout.ChatBottomY, 838f),
            "[UI-SCALE] HUD, status strip, right column and dock keep their design positions");

        Check(Near(layout.TabPanelDockedWidth, 800f) && Near(layout.TabPanelFreeWidth, 800f)
              && Near(layout.TabPanelMaxHeight, 480f),
            "[UI-SCALE] the tab body keeps its 800 wide, 480 tall design size");

        DesignRect pause = UiLayout.CenteredModal(viewport, 620f, 660f);
        Check(Near(pause.X, 490f) && Near(pause.Y, 120f) && Near(pause.Width, 620f)
              && Near(pause.Height, 660f),
            "[UI-SCALE] pause/settings modal keeps its 490,120 620x660 placement");

        Check(Near(UiLayout.ReportPanelX(layout), 400f),
            "[UI-SCALE] the court report keeps its x=400 column at 100%");
    }

    // ---- 2: scale and viewport values across 80/100/120% -------------------------

    private static void RunUiScaleTransformValues()
    {
        Section("[UI-SCALE] fit x uiScale across 80/100/120% on 16:9 and 4:3");

        Check(Near(UiPointerMapping.Scale(1920, 1080, 1f), 1.2f)
              && Near(UiPointerMapping.Scale(1920, 1080, 0.8f), 0.96f)
              && Near(UiPointerMapping.Scale(1920, 1080, 1.2f), 1.44f)
              && Near(UiPointerMapping.Scale(1280, 960, 1f), 0.8f)
              && Near(UiPointerMapping.Scale(1280, 960, 0.8f), 0.64f)
              && Near(UiPointerMapping.Scale(1280, 960, 1.2f), 0.96f),
            "[UI-SCALE] 16:9 gives 1.2/0.96/1.44 and 4:3 gives 0.8/0.64/0.96 at 80/100/120%");

        UiViewport reference = UiPointerMapping.Viewport(1920, 1080, 1f);
        UiViewport large = UiPointerMapping.Viewport(1920, 1080, 1.2f);
        UiViewport small = UiPointerMapping.Viewport(1920, 1080, 0.8f);
        Check(Near(reference.Width, 1600f) && Near(reference.Height, 900f)
              && Near(large.Width, 1600f / 1.2f) && Near(large.Height, 750f)
              && Near(small.Width, 2000f) && Near(small.Height, 1125f),
            "[UI-SCALE] the design viewport grows at 80% (2000x1125) and shrinks at 120% (1333x750)");

        Check(Near(UiPointerMapping.ClampUiScale(0.5f), 0.8f)
              && Near(UiPointerMapping.ClampUiScale(2f), 1.2f)
              && Near(UiPointerMapping.ClampUiScale(float.NaN), 1f),
            "[UI-SCALE] the UI-scale setting clamps to 80-120% and survives a corrupt stored value");

        UiLayoutMetrics fourThree = UiLayout.Compute(UiPointerMapping.Viewport(1280, 960, 1f));
        Check(Near(fourThree.Height, 1200f) && Near(fourThree.StatusBarY, 1146f),
            "[UI-SCALE] at 4:3 the status strip hugs the real window bottom (design y 1146) "
            + "instead of floating at 846");

        UiLayoutMetrics wide = UiLayout.Compute(large);
        Check(UiLayout.ReportPanelX(wide) < 400f
              && UiLayout.ReportPanelX(wide) + UiLayout.ReportPanelWidth <= wide.ContentRight + 0.01f,
            "[UI-SCALE] at 120% the court report moves left to clear the End-turn column");
    }

    // ---- 3: anchored layout keeps every edge control on screen -------------------

    private static void RunUiScaleLayoutAnchors()
    {
        Section("[UI-SCALE] anchored layout at 80/100/120% on 16:9 and 4:3");

        int[] widths = { 1920, 1280, 1600, 2560 };
        int[] heights = { 1080, 960, 900, 1440 };
        float[] scales = { 0.8f, 1f, 1.2f };
        string[] aspect = { "16:9", "4:3", "16:9", "16:9" };

        for (int w = 0; w < widths.Length; w++)
        {
            for (int s = 0; s < scales.Length; s++)
            {
                UiLayoutMetrics m = UiLayout.Compute(UiPointerMapping.Viewport(
                    widths[w], heights[w], scales[s]));
                string name = $"[UI-SCALE] {MathfPercent(scales[s])} {aspect[w]} "
                              + $"{widths[w]}x{heights[w]} keeps every anchored control on screen";

                bool rightColumn = m.TabColumnX >= UiLayout.EdgeMargin
                                   && m.TabColumnX + UiLayout.ColumnWidth <= m.Width + 0.01f
                                   && m.ContentRight < m.TabColumnX;
                bool statusStrip = m.StatusBarY >= 0f
                                   && m.StatusBarY + UiLayout.StatusBarHeight <= m.Height + 0.01f
                                   && m.StatusBarWidth > 0f;
                bool dock = m.SidePanelX >= UiLayout.EdgeMargin
                            && m.SidePanelX + UiLayout.DockWidth <= m.ContentRight + 0.01f;
                bool tabBody = UiLayout.TabPanelDockedX + m.TabPanelDockedWidth
                                   <= m.SidePanelX - UiLayout.PanelGutter + 0.01f
                               && UiLayout.TabPanelX + m.TabPanelFreeWidth
                                   <= m.ContentRight - UiLayout.ContentGap + 0.01f
                               && UiLayout.TabPanelY + m.TabPanelMaxHeight
                                   <= m.StatusBarY + 0.01f;
                float reportX = UiLayout.ReportPanelX(m);
                bool report = reportX >= UiLayout.EdgeMargin
                              && reportX + UiLayout.ReportPanelWidth <= m.ContentRight + 0.01f;
                Check(rightColumn && statusStrip && dock && tabBody && report, name);
            }
        }
    }

    // ---- 4: pointer, projector and Event positions agree with the drawing --------

    private static void RunUiScalePointerAgreement()
    {
        Section("[UI-SCALE] pointer mapping agrees with the drawing transform");

        UiPointerMapping.EventToDesignSpace(1200f, 300f, out float eventX, out float eventY);
        Check(Near(eventX, 1200f) && Near(eventY, 300f),
            "[UI-SCALE] Event.mousePosition is already design space, so the mapping is the identity");

        int[] widths = { 1920, 1280, 1600 };
        int[] heights = { 1080, 960, 900 };
        float[] scales = { 0.8f, 1f, 1.2f };
        float[] designXs = { 0f, 400f, 1000f, 1599.5f };
        float[] designYs = { 0f, 120f, 500f, 899.5f };

        for (int w = 0; w < widths.Length; w++)
        {
            for (int s = 0; s < scales.Length; s++)
            {
                UiViewport viewport = UiPointerMapping.Viewport(widths[w], heights[w], scales[s]);
                UiLayoutMetrics layout = UiLayout.Compute(viewport);
                float worst = 0f;
                foreach (float dx in designXs)
                {
                    foreach (float dy in designYs)
                    {
                        UiPointerMapping.ToScreenPoint(dx, dy, heights[w], viewport.Scale,
                            out float screenX, out float screenYBottomLeft);
                        UiPointerMapping.ToDesignSpace(screenX, screenYBottomLeft, widths[w], heights[w],
                            scales[s], out float backX, out float backY);
                        worst = Math.Max(worst, Math.Abs(backX - dx));
                        worst = Math.Max(worst, Math.Abs(backY - dy));
                    }
                }
                bool roundTrip = worst < 0.05f;

                // The right column's own design point must still land inside the window pixels.
                UiPointerMapping.ToScreenPoint(layout.TabColumnX + 4f, 30f, heights[w], viewport.Scale,
                    out float endTurnX, out float endTurnY);
                bool onScreen = endTurnX >= 0f && endTurnX <= widths[w] && endTurnY >= 0f
                                && endTurnY <= heights[w];

                Check(roundTrip && onScreen,
                    $"[UI-SCALE] {MathfPercent(scales[s])} {widths[w]}x{heights[w]}: Input.mousePosition "
                    + "round-trips and the End-turn column stays inside the window");
            }
        }

        // Regression: at 100% on a 1920 window the old code divided an Event position by
        // Screen.width/1600 (=1.2), so a click at design x=1000 was read as x=833 and a click on the
        // dock fell through to the map. The identity mapping must keep it at 1000.
        UiPointerMapping.EventToDesignSpace(1000f, 100f, out float clickX, out float clickY);
        float legacyEventX = 1000f / (1920f / 1600f);
        Check(Near(clickX, 1000f) && Near(clickY, 100f) && Math.Abs(legacyEventX - clickX) > 100f,
            "[UI-SCALE] the removed Screen.width division would have moved a 1000px click to 833px");

        // Regression: at 80% the old Input.mousePosition path divided by Screen.width/uiScale
        // (scale 1.5 on a 1920 window), which mapped a physical point on the dock back to the map.
        UiViewport small = UiPointerMapping.Viewport(1920, 1080, 0.8f);
        UiLayoutMetrics smallLayout = UiLayout.Compute(small);
        var dockRect = new DesignRect(smallLayout.SidePanelX, UiLayout.DockY,
            UiLayout.DockWidth, 260f);
        UiPointerMapping.ToScreenPoint(dockRect.X + 4f, dockRect.Y + 4f, 1080, small.Scale,
            out float dockScreenX, out float dockScreenYBottomLeft);
        UiPointerMapping.ToDesignSpace(dockScreenX, dockScreenYBottomLeft, 1920, 1080, 0.8f,
            out float mappedX, out float mappedY);
        float legacyDesignX = dockScreenX / ((1920f / 0.8f) / 1600f);
        Check(dockRect.Contains(mappedX, mappedY) && !dockRect.Contains(legacyDesignX, mappedY),
            "[UI-SCALE] at 80% a dock click stays on the dock instead of falling through to the map");
    }

    // ---- 5: modal controls stay reachable at every setting ----------------------

    private static void RunUiScaleModalFit()
    {
        Section("[UI-SCALE] pause/settings modal buttons stay reachable");

        int[] widths = { 1920, 1280, 1600 };
        int[] heights = { 1080, 960, 900 };
        float[] scales = { 0.8f, 1f, 1.2f };

        for (int w = 0; w < widths.Length; w++)
        {
            for (int s = 0; s < scales.Length; s++)
            {
                UiViewport viewport = UiPointerMapping.Viewport(widths[w], heights[w], scales[s]);
                DesignRect pause = UiLayout.CenteredModal(viewport, 620f, 660f);
                // Back sits at the panel's bottom edge (yMax - 62, height 38); the four stacked buttons
                // start 110 below the title and step 70, the last one ending 372 from the panel top.
                float backBottom = pause.YMax - 62f + 38f;
                float lastButtonBottom = pause.Y + 110f + 3f * 70f + 52f;
                Check(pause.X >= 0f && pause.XMax <= viewport.Width + 0.01f
                      && backBottom <= viewport.Height + 0.01f
                      && lastButtonBottom <= viewport.Height + 0.01f,
                    $"[UI-SCALE] {MathfPercent(scales[s])} {widths[w]}x{heights[w]}: Resume/Settings/"
                    + "Restart/Quit and Back fit inside the visible viewport");
            }
        }
    }

    private static float MathfPercent(float scale)
        => (float)Math.Round(scale * 100f);

    // ---- 6: the location-choice popup is anchored with the same transform --------

    private static void RunUiScaleSelectionPopup()
    {
        Section("[UI-SCALE] location-choice popup follows the shared frame transform");

        // DrawLocationChoice projects the marker to screen pixels and maps them back with
        // UiPointerMapping.ToDesignSpace(..., uiScale) - the same call TryProjectCity uses - then offsets
        // by (+24, -42) and clamps inside ContentRight/StatusBarY with a 220x112 popup (8 px margins).
        // A marker at a fixed design point must therefore put the popup at the same design point at every
        // UI scale, and the clamped popup must stay inside the map band.
        int[] widths = { 1920, 1280, 1600 };
        int[] heights = { 1080, 960, 900 };
        float[] scales = { 0.8f, 1f, 1.2f };
        const float popupWidth = 220f;
        const float popupHeight = 112f;

        for (int w = 0; w < widths.Length; w++)
        {
            for (int s = 0; s < scales.Length; s++)
            {
                UiViewport viewport = UiPointerMapping.Viewport(widths[w], heights[w], scales[s]);
                UiLayoutMetrics layout = UiLayout.Compute(viewport);
                float markerX = layout.ContentRight * 0.5f;
                float markerY = layout.StatusBarY * 0.5f;

                UiPointerMapping.ToScreenPoint(markerX, markerY, heights[w], viewport.Scale,
                    out float screenX, out float screenYBottomLeft);
                UiPointerMapping.ToDesignSpace(screenX, screenYBottomLeft, widths[w], heights[w],
                    scales[s], out float anchorX, out float anchorY);

                float popupX = ClampValue(anchorX + 24f, UiLayout.EdgeMargin,
                    layout.ContentRight - popupWidth - 8f);
                float popupY = ClampValue(anchorY - 42f, UiLayout.TopBarY + UiLayout.TopBarHeight + 8f,
                    layout.StatusBarY - popupHeight - 8f);

                bool anchored = Math.Abs(anchorX - markerX) < 0.05f && Math.Abs(anchorY - markerY) < 0.05f;
                bool inside = popupX >= UiLayout.EdgeMargin
                              && popupX + popupWidth <= layout.ContentRight + 0.01f
                              && popupY >= UiLayout.TopBarY + UiLayout.TopBarHeight
                              && popupY + popupHeight <= layout.StatusBarY + 0.01f;
                Check(anchored && inside,
                    $"[UI-SCALE] {MathfPercent(scales[s])} {widths[w]}x{heights[w]}: the location-choice "
                    + "popup stays beside its marker and inside the map band");
            }
        }

        // The removed Screen.width/1600f scale placed the same marker at marker * uiScale (16:9), i.e.
        // 20% off at 120% on a 1920 window; the shared transform must not reproduce that drift.
        UiViewport large = UiPointerMapping.Viewport(1920, 1080, 1.2f);
        UiLayoutMetrics largeLayout = UiLayout.Compute(large);
        float probe = largeLayout.ContentRight * 0.5f;
        UiPointerMapping.ToScreenPoint(probe, largeLayout.StatusBarY * 0.5f, 1080, large.Scale,
            out float probeScreenX, out float probeScreenY);
        float legacyAnchorX = probeScreenX / (1920f / 1600f);
        UiPointerMapping.ToDesignSpace(probeScreenX, probeScreenY, 1920, 1080, 1.2f,
            out float sharedAnchorX, out _);
        Check(Near(sharedAnchorX, probe) && Math.Abs(legacyAnchorX - probe) > 40f,
            "[UI-SCALE] at 120% the old Screen.width/1600f anchor drifted the popup off its hex");
    }

    // ---- 7: the battle-result modal fits and Continue stays reachable -------------

    private static void RunUiScaleBattleModal()
    {
        Section("[UI-SCALE] battle-result modal fits and Continue stays reachable");

        UiViewport reference = UiPointerMapping.Viewport(1600, 900, 1f);
        DesignRect historical = UiLayout.CenteredModal(reference, 920f, 640f);
        Check(Near(historical.X, 340f) && Near(historical.Y, 130f)
              && Near(historical.Width, 920f) && Near(historical.Height, 640f),
            "[UI-SCALE] 100% 16:9 keeps the battle modal at its historical 340,130 920x640");

        int[] widths = { 1920, 1280, 1600 };
        int[] heights = { 1080, 960, 900 };
        float[] scales = { 0.8f, 1f, 1.2f };

        for (int w = 0; w < widths.Length; w++)
        {
            for (int s = 0; s < scales.Length; s++)
            {
                UiViewport viewport = UiPointerMapping.Viewport(widths[w], heights[w], scales[s]);
                DesignRect panel = UiLayout.CenteredModal(viewport, 920f, 640f);
                // Continue sits at the panel's bottom-right (yMax - 62, height 38); the defender block
                // starts 340 below the panel top and its troop row ends 70 further down.
                float continueTop = panel.YMax - 62f;
                float continueBottom = continueTop + 38f;
                float defenderBlockBottom = panel.Y + 340f + 70f;
                bool fits = panel.X >= 0f && panel.XMax <= viewport.Width + 0.01f
                            && panel.Y >= 0f && panel.YMax <= viewport.Height + 0.01f
                            && continueTop >= panel.Y && continueBottom <= panel.YMax
                            && continueBottom <= viewport.Height - 24f
                            && defenderBlockBottom <= panel.YMax - 92f;
                Check(fits,
                    $"[UI-SCALE] {MathfPercent(scales[s])} {widths[w]}x{heights[w]}: battle Continue and "
                    + "the stat rows stay at least 24 design units above the window bottom");
            }
        }

        // The old fixed y=130..770 plate left only 4 design units under Continue at 120% 16:9; the
        // fitted modal must give it real clearance.
        UiViewport large = UiPointerMapping.Viewport(1600, 900, 1.2f);
        DesignRect fitted = UiLayout.CenteredModal(large, 920f, 640f);
        float clearance = large.Height - (fitted.YMax - 62f + 38f);
        float legacyClearance = large.Height - (130f + 640f - 62f + 38f);
        Check(clearance >= 60f && clearance > legacyClearance + 50f,
            "[UI-SCALE] at 120% 16:9 the fitted battle modal gives Continue far more bottom clearance");
    }

    private static float ClampValue(float value, float min, float max)
        => value < min ? min : (value > max ? max : value);
}
