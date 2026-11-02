// KingdomDemo (partial) — the per-frame IMGUI transform and the anchored layout it implies.
//
// One transform serves drawing and pointer conversion: a pure uniform scale of the 1600x900 reference
// design space (fit x the user's UI scale), with no translation. Design units therefore equal GUI units,
// so Event.mousePosition can be read directly while Input.mousePosition and projector points go through
// UiPointerMapping. Everything that depends on a window edge is derived from the visible viewport: at
// 120% the viewport is narrower than 1600 design units, and anchoring keeps the right HUD, the End-turn
// button and the tab column inside it instead of pushing them off screen.

using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        // The demo has exactly one KingdomDemo component per scene and IMGUI geometry is a property of
        // the window, so the frame's metrics live in one static slot. Static helpers that are pure
        // geometry (SpectatorPanelArea) and the many drawing partials read them by name.
        private static UiViewport uiViewport =
            new UiViewport(1f, UiPointerMapping.DesignWidth, UiPointerMapping.DesignHeight);
        private static UiLayoutMetrics uiLayout =
            UiLayout.Compute(new UiViewport(1f, UiPointerMapping.DesignWidth, UiPointerMapping.DesignHeight));

        /// <summary>Computes this frame's transform and anchors, then installs the GUI matrix.</summary>
        void BeginUiFrame()
        {
            uiViewport = UiPointerMapping.Viewport(Screen.width, Screen.height, uiScale);
            uiLayout = UiLayout.Compute(uiViewport);
            float s = uiViewport.Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        }

        /// <summary>Visible width in design units (1600 at 100% on a 16:9 window).</summary>
        static float DesignWidth => uiViewport.Width;

        /// <summary>Visible height in design units (900 at 100% on a 16:9 window).</summary>
        static float DesignHeight => uiViewport.Height;

        // ------------------------------------------------------------ anchored HUD geometry
        static float TabColumnX => uiLayout.TabColumnX;
        static float EndTurnX => uiLayout.TabColumnX;
        static float ContentRight => uiLayout.ContentRight;
        static float TopBarWidth => uiLayout.TopBarWidth;
        static float StatusBarY => uiLayout.StatusBarY;
        static float StatusBarWidth => uiLayout.StatusBarWidth;
        static float SidePanelX => uiLayout.SidePanelX;
        static float AgentPanelX => uiLayout.SidePanelX;
        static float AgentPanelWidth => UiLayout.DockWidth;
        static float ChatBottomY => uiLayout.ChatBottomY;
        static float TabPanelX => UiLayout.TabPanelX;

        /// <summary>Tab body width: the docked body steps aside for the side dock.</summary>
        float TabPanelWidth => SideDockVisible() ? uiLayout.TabPanelDockedWidth : uiLayout.TabPanelFreeWidth;

        /// <summary>Height the tab body may use without reaching the status strip.</summary>
        float TabPanelDefaultHeight => uiLayout.TabPanelMaxHeight;

        /// <summary>
        /// Court report / victory column origin: the historical 400 at 100%, pulled left just enough to
        /// clear the right column when the viewport is narrower than the reference canvas (120%).
        /// </summary>
        static float ReportPanelX => UiLayout.ReportPanelX(uiLayout);

        /// <summary>
        /// A modal panel of the preferred size, shrunk to the viewport and centred in it. At 100% on a
        /// 16:9 window this reproduces the historical placement exactly.
        /// </summary>
        static Rect ModalPanelRect(float width, float height)
        {
            DesignRect fitted = UiLayout.CenteredModal(uiViewport, width, height);
            return new Rect(fitted.X, fitted.Y, fitted.Width, fitted.Height);
        }
    }
}
