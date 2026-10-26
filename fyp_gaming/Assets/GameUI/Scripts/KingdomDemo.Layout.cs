// KingdomDemo (partial) — presentation-only tunables.
//
// Every number/colour added by the bounded V1 visual pass lives here (or in Style.cs for the theme
// itself). Gameplay, provider/controller contracts and observation permissions are untouched.

using UnityEngine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        private const float DesignHeight = 900f;

        // ---------------------------------------------------------------- HUD rows
        // Two compact HUD lines. The top bar never exceeds 56 design px; the status strip never
        // exceeds 60 and no longer repeats the full controls legend.
        private const float TopBarTitleRowY = 2f;
        private const float TopBarTitleRowHeight = 26f;
        private const float TopBarTotalsRowY = 28f;
        private const float TopBarTotalsRowHeight = 18f;
        private const float StatusMessageRowY = 3f;
        private const float StatusMessageRowHeight = 19f;
        private const float StatusFeedRowY = 23f;
        private const float StatusFeedLineHeight = 11f;
        private const int StatusFeedLines = 2;
        private const float StatusHintWidth = 380f;

        // ---------------------------------------------------------------- side dock / tab body
        // The agent/spectator details are docked on the right, in the gap between the map and the tab
        // column. They never cover the tab column, and the selection panel/centre tab body step aside.
        private const float PanelPadX = 12f;
        private const float PanelHeaderPadX = 14f;
        private const float SidePanelX = 960f;
        private const float SidePanelY = 68f;
        private const float SidePanelWidth = 420f;
        private const float AgentPanelX = SidePanelX;
        private const float AgentPanelY = SidePanelY;
        private const float AgentPanelWidth = SidePanelWidth;
        private const float AgentPanelIdleHeight = 124f;
        private const float AgentPanelDetailHeight = 260f;
        private const float SpectatorPanelHeight = 400f;
        private const float TabPanelX = 400f;
        private const float TabPanelDockedX = 148f;   // 800-wide body left of the 420 side dock
        private const float TabPanelY = 190f;
        private const float TabPanelWidth = 800f;
        private const float TabPanelDefaultHeight = 480f;

        // ---------------------------------------------------------------- selection panel placement
        // The panel is capped at 440 design px and anchored beside the selected marker. It never parks
        // itself in the screen centre: if either side of the marker has room, that side wins.
        private const float SelectionPanelMaxWidth = 440f;
        private const float SelectionPanelMarkerGap = 28f;
        private const float SelectionArmyLabelOffsetY = 30f;
        private const float SelectionArmyLabelHeight = 20f;
        private const float SelectionArmyLabelMaxWidth = 220f;
        private const float SelectionArmyLabelPadX = 6f;

        // ---------------------------------------------------------------- fog shroud
        // One flat, soft-edged hex per fogged cell. No cloud texture, no per-cell pattern. TBTK uses
        // flat-top hexes arranged in columns, so the texture is wider than it is tall (2 : sqrt 3).
        private const int FogShroudTextureWidth = 128;
        private const int FogShroudTextureHeight = 111;          // 2 / sqrt(3) aspect
        private const float FogShroudSoftEdgePixels = 4f;        // softness of the outer rim only
        private const float FogShroudOverlap = 1.02f;            // closes antialiasing seams
        private const int FogShroudSortingOrder = 100;
        private const float FogShroudBaseAlpha = 0.48f;          // requested 0.45-0.55
        private const float FogShroudBoundaryAlpha = 0.54f;      // a touch darker at the sight edge
        private static readonly Color FogShroudBaseColor = new Color(0.44f, 0.50f, 0.57f, 1f);
        private static readonly Color FogShroudBoundaryColor = new Color(0.35f, 0.41f, 0.49f, 1f);

        // ---------------------------------------------------------------- terrain seams
        // A dark mat just below the node meshes. Cracks between tiles read as quiet seams instead of
        // a bright green lattice. No node transform, collider or material asset is touched.
        private static readonly bool GridSeamUnderlayEnabled = true;
        private const float GridSeamUnderlayY = -0.03f;
        private const float GridSeamUnderlayPadding = 1.02f;
        private static readonly Color GridSeamColor = new Color(0.23f, 0.28f, 0.18f, 1f);

        // ---------------------------------------------------------------- initial camera framing
        // Applied once in InitGame, before TBTK CameraControl.Start caches its private zoom. The user
        // can pan/zoom normally afterwards; this never runs again. -22.5 shows about 85% of the map
        // width at the scene's 30-degree FOV; it is clamped to the CameraControl's serialized limits.
        private const float InitialCameraPivotX = 0f;
        private const float InitialCameraPivotZ = 0f;
        private const float InitialCameraDistance = 22.5f;

        // ---------------------------------------------------------------- city labels
        private const float CityLabelAnchorHeight = 0.15f;       // world-space anchor above the node
        private const float CityLabelOffsetY = 34f;              // design px below the projected anchor
        private const float CityLabelHeight = 20f;
        private const float CityLabelPadX = 6f;
        private const float CityLabelMaxWidth = 168f;
        private const float CityLabelBorder = 1f;
        private const int CityLabelFontSize = 12;
        private const int CityLabelSmallFontSize = 11;
        private static readonly Color CityLabelPlate = new Color(0.93f, 0.88f, 0.76f, 0.94f);
        private static readonly Color CityLabelScoutedPlate = new Color(0.82f, 0.80f, 0.74f, 0.92f);
        private static readonly Color CityLabelScoutedText = new Color(0.40f, 0.35f, 0.28f, 1f);

        // The viewed capital gets a small gold "viewed" chip above its name tag.
        private const float ViewedMarkerHeight = 13f;
        private const float ViewedMarkerWidth = 52f;
        private const float ViewedMarkerGap = 2f;
        private const int ViewedMarkerFontSize = 9;
        private static readonly Color ViewedMarkerColor = new Color(0.72f, 0.55f, 0.22f, 0.96f);

        // ---------------------------------------------------------------- army banner scale
        // Runtime banner clones (CreateArmyBannerMarker) use this shot-wide scale, applied in
        // SyncArmyVisual as well. Scene-authored armies keep their art and root scale; no prefab or
        // scene asset is modified.
        private const float ArmyBannerWorldScale = 0.22f;
        // Own armies on the same or adjacent hex (within ~1.5 hex) fan out laterally instead of
        // drawing four banners in one column. The offset is presentation-only and deterministic.
        private const int ArmyBannerStaggerHexSteps = 1;
        private const float ArmyBannerStaggerSpacing = 0.16f;
        private const float ArmyBannerStaggerMaxSteps = 2f;

        // ---------------------------------------------------------------- court report
        private const float ReportPanelX = 400f;
        private const float ReportPanelY = 150f;
        private const float ReportPanelWidth = 800f;
        private const float ReportPanelBaseHeight = 116f;
        private const float ReportBodyWidth = 736f;
        private const float ReportTitleWidth = 760f;
        private const float ReportContinueWidth = 140f;
        private const float ReportContinueHeight = 40f;
        private const float ReportTextMaxHeight = 300f;

        // ---------------------------------------------------------------- agent side panel rows
        private const float AgentTitleRowY = 10f;
        private const float AgentTitleHeight = 24f;
        private const float AgentCancelWidth = 96f;
        private const float AgentCancelHeight = 24f;
        private const float AgentProviderRowY = 36f;
        private const float AgentStateRowY = 54f;
        private const float AgentRequestsRowY = 72f;
        private const float AgentDecisionRowY = 94f;
        private const float AgentCommandRowY = 132f;
        private const float AgentResultRowY = 164f;
        private const float AgentTokensRowY = 182f;
        private const float AgentCostRowY = 200f;
        private const float AgentRecorderRowY = 218f;
        private const float AgentFailureRowY = 236f;
        private const float AgentRowHeight = 16f;
        private const float AgentMonoRowHeight = 14f;
        private const int AgentDecisionLines = 2;
        private const int AgentCommandLines = 2;
        private const float AgentLineHeight = 16f;
        private const float AgentMonoLineHeight = 14f;

        // ---------------------------------------------------------------- spectator side panel rows
        private const float SpectatorTitleRowY = 10f;
        private const float SpectatorProviderRowY = 30f;
        private const float SpectatorStateRowY = 46f;
        private const float SpectatorContextRowY = 62f;
        private const float SpectatorPipsRowY = 68f;
        private const float SpectatorColumnsRowY = 86f;
        private const float SpectatorScoreRowY = 106f;
        private const float SpectatorScoreRowGap = 16f;
        private const float SpectatorRowHeight = 16f;
        private const float SpectatorLastRowY = 156f;
        private const float SpectatorResultRowY = 174f;
        private const float SpectatorDecisionRowY = 192f;
        private const float SpectatorRecentHeaderRowY = 230f;
        private const float SpectatorRecentFirstRowY = 244f;
        private const int SpectatorRecentLines = 5;
        private const float SpectatorRecentLineHeight = 14f;
        private const float SpectatorRequestsRowY = 316f;
        private const float SpectatorCostRowY = 332f;
        private const float SpectatorControlsRowY = 348f;
        private const float SpectatorSummaryRowY = 380f;
        private const float SpectatorButtonHeight = 28f;
        private const float SpectatorButtonGap = 8f;
        private const float SpectatorColumnGap = 10f;
        private const int SpectatorDecisionLines = 2;
        private const float SpectatorDecisionLineHeight = 16f;
        private const float SpectatorPipWidth = 22f;
        private const float SpectatorPipHeight = 6f;

        // ---------------------------------------------------------------- tooltip/truncation
        private const float TooltipMaxWidth = 440f;
        private const float TooltipPadding = 8f;
        private const float TooltipOffset = 12f;
    }
}
