using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// One frame of the IMGUI transform: the physical window mapped onto the 1600x900 reference design
    /// space by a single uniform <see cref="Scale"/>. Because the transform is a pure scale (no offset),
    /// design units and GUI units are the same coordinate space, which is why
    /// <c>Event.mousePosition</c> needs no conversion while <c>Input.mousePosition</c> does.
    /// </summary>
    public readonly struct UiViewport
    {
        /// <summary>Design pixels to screen pixels (the uniform <c>GUI.matrix</c> scale).</summary>
        public readonly float Scale;

        /// <summary>Visible width in design units: <c>Screen.width / Scale</c>.</summary>
        public readonly float Width;

        /// <summary>Visible height in design units: <c>Screen.height / Scale</c>.</summary>
        public readonly float Height;

        public UiViewport(float scale, float width, float height)
        {
            Scale = scale;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Anchored panel geometry for one frame, in visible design units. The 1600x900 layout is fixed at
    /// 100%; at other UI scales the viewport is larger (80%) or smaller (120%), so the right column, the
    /// status strip and the right dock are anchored to the viewport instead of the reference canvas.
    /// </summary>
    public readonly struct UiLayoutMetrics
    {
        public readonly float Width;
        public readonly float Height;
        public readonly float TabColumnX;
        public readonly float ContentRight;
        public readonly float TopBarWidth;
        public readonly float StatusBarY;
        public readonly float StatusBarWidth;
        public readonly float SidePanelX;
        public readonly float ChatBottomY;
        public readonly float TabPanelDockedWidth;
        public readonly float TabPanelFreeWidth;
        public readonly float TabPanelMaxHeight;

        public UiLayoutMetrics(float width, float height, float tabColumnX, float contentRight,
            float topBarWidth, float statusBarY, float statusBarWidth, float sidePanelX,
            float chatBottomY, float tabPanelDockedWidth, float tabPanelFreeWidth,
            float tabPanelMaxHeight)
        {
            Width = width;
            Height = height;
            TabColumnX = tabColumnX;
            ContentRight = contentRight;
            TopBarWidth = topBarWidth;
            StatusBarY = statusBarY;
            StatusBarWidth = statusBarWidth;
            SidePanelX = sidePanelX;
            ChatBottomY = chatBottomY;
            TabPanelDockedWidth = tabPanelDockedWidth;
            TabPanelFreeWidth = tabPanelFreeWidth;
            TabPanelMaxHeight = tabPanelMaxHeight;
        }
    }

    /// <summary>
    /// Pure layout arithmetic shared by the UI. The reference arrangement (1600x900) is reproduced
    /// exactly; every value that depends on the window edge is derived from the visible viewport so a
    /// larger UI scale shrinks the viewport instead of pushing controls off it.
    /// </summary>
    public static class UiLayout
    {
        public const float EdgeMargin = 10f;        // left/right/top/bottom margin of the HUD
        public const float ContentGap = 10f;        // gap between the content area and the right column
        public const float TopBarY = 8f;
        public const float TopBarHeight = 52f;
        public const float ColumnWidth = 190f;      // End-turn button and tab column share this width
        public const float StatusBarHeight = 46f;
        public const float BottomMargin = 8f;
        public const float DockY = 68f;
        public const float DockWidth = 420f;
        public const float PanelGutter = 12f;       // clearance a docked tab body keeps from the dock
        public const float ChatGap = 8f;
        public const float TabPanelX = 400f;        // free-standing tab body (no side dock)
        public const float TabPanelDockedX = 148f;  // tab body left of the side dock
        public const float TabPanelY = 190f;
        public const float TabPanelBaseWidth = 800f;
        public const float TabPanelDefaultHeight = 480f;
        public const float TabPanelBottomGap = 16f;
        public const float MinTabPanelWidth = 320f; // only reachable far outside 80-120%
        public const float ReportPanelWidth = 800f; // court report / victory column
        public const float ReportPanelPreferredX = 400f;

        public static UiLayoutMetrics Compute(UiViewport viewport)
        {
            float width = Math.Max(1f, viewport.Width);
            float height = Math.Max(1f, viewport.Height);

            float tabColumnX = width - EdgeMargin - ColumnWidth;
            float contentRight = tabColumnX - ContentGap;
            float statusBarY = height - BottomMargin - StatusBarHeight;
            float sidePanelX = contentRight - ContentGap - DockWidth;

            float docked = sidePanelX - PanelGutter - TabPanelDockedX;
            float free = contentRight - ContentGap - TabPanelX;
            float tabMaxHeight = statusBarY - TabPanelY - TabPanelBottomGap;

            return new UiLayoutMetrics(
                width, height,
                tabColumnX, contentRight,
                contentRight - EdgeMargin,
                statusBarY, contentRight - EdgeMargin,
                sidePanelX,
                statusBarY - ChatGap,
                ClampPanelWidth(docked),
                ClampPanelWidth(free),
                Math.Min(TabPanelDefaultHeight, Math.Max(120f, tabMaxHeight)));
        }

        static float ClampPanelWidth(float available)
            => Math.Max(MinTabPanelWidth, Math.Min(TabPanelBaseWidth, available));

        /// <summary>
        /// Court report origin: the historical 400 at 100%, pulled left just enough to clear the right
        /// column when the visible viewport is narrower than the 1600 design canvas (120%).
        /// </summary>
        public static float ReportPanelX(UiLayoutMetrics layout)
            => Math.Min(ReportPanelPreferredX, layout.ContentRight - ReportPanelWidth - ContentGap);

        /// <summary>
        /// A modal panel of the preferred size, shrunk to the viewport and centred in it. At 100% on a
        /// 16:9 window this reproduces the historical placement exactly (620x660 pause panel at 490,120).
        /// </summary>
        public static DesignRect CenteredModal(UiViewport viewport, float width, float height)
        {
            float w = Math.Min(width, Math.Max(1f, viewport.Width - 40f));
            float h = Math.Min(height, Math.Max(1f, viewport.Height - 40f));
            return new DesignRect((viewport.Width - w) * 0.5f, (viewport.Height - h) * 0.5f, w, h);
        }
    }
}
