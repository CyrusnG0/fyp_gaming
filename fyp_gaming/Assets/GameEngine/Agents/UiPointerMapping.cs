using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>An axis-aligned rectangle in design space (top-left origin).</summary>
    public readonly struct DesignRect
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public DesignRect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public float XMax => X + Width;
        public float YMax => Y + Height;

        /// <summary>Same half-open rule as <see cref="UnityEngine.Rect.Contains(Vector2)"/>.</summary>
        public bool Contains(float x, float y) => x >= X && x < XMax && y >= Y && y < YMax;
    }

    /// <summary>
    /// Converts a physical pointer position into the design space the IMGUI panels are laid out in.
    /// Kept free of UnityEngine so the 16:9 / 4:3 and 80/100/120% cases can be unit-tested headlessly.
    ///
    /// <para>OnGUI installs ONE uniform transform, <c>GUI.matrix = Scale(fit x uiScale)</c>, and draws
    /// every panel in design units underneath it. The design canvas is fitted to the window (the
    /// smaller axis wins), then the user's UI scale is applied, so the visible design viewport is
    /// <c>Screen.size / scale</c>: larger at 80%, smaller at 120%. Layout anchors to that viewport
    /// (<see cref="UiLayout"/>) rather than to a fixed 1600x900 box.</para>
    ///
    /// <para><c>Input.mousePosition</c> has a BOTTOM-LEFT origin and is untouched by the GUI matrix, so
    /// it must be converted here (flip, then divide by the same scale). <c>Event.mousePosition</c> is
    /// already in GUI space - IMGUI applies the matrix to event coordinates, which is why the built-in
    /// controls hit-test it against un-transformed <c>Rect</c>s - so it must NOT be divided again; see
    /// <see cref="EventToDesignSpace"/>.</para>
    /// </summary>
    public static class UiPointerMapping
    {
        public const float DesignWidth = 1600f;
        public const float DesignHeight = 900f;

        /// <summary>The UI-scale setting's supported range (see the pause menu).</summary>
        public const float MinUiScale = 0.8f;
        public const float MaxUiScale = 1.2f;

        public static float ClampUiScale(float uiScale)
            => float.IsNaN(uiScale) ? 1f : Math.Min(MaxUiScale, Math.Max(MinUiScale, uiScale));

        /// <summary>Fit of the 1600x900 reference canvas into the window; the smaller axis wins.</summary>
        public static float FitScale(int screenWidth, int screenHeight)
            => screenWidth > 0 && screenHeight > 0
                ? Math.Min(screenWidth / DesignWidth, screenHeight / DesignHeight)
                : 1f;

        /// <summary>The uniform scale OnGUI draws with: fit scale times the clamped UI scale.</summary>
        public static float Scale(int screenWidth, int screenHeight, float uiScale)
            => FitScale(screenWidth, screenHeight) * ClampUiScale(uiScale);

        /// <summary>
        /// The frame transform and the design viewport it exposes. Everything that converts a physical
        /// pointer or projector position must use this instead of <c>Screen.width / 1600</c>.
        /// </summary>
        public static UiViewport Viewport(int screenWidth, int screenHeight, float uiScale)
        {
            float scale = Scale(screenWidth, screenHeight, uiScale);
            if (scale <= 0f) return new UiViewport(1f, DesignWidth, DesignHeight);
            return new UiViewport(scale, screenWidth / scale, screenHeight / scale);
        }

        /// <summary>
        /// Width-ratio scale of the reference canvas. Superseded by
        /// <see cref="Scale(int, int, float)"/> for anything that must agree with the frame transform.
        /// </summary>
        public static float Scale(int screenWidth)
            => screenWidth > 0 ? screenWidth / DesignWidth : 1f;

        /// <summary>
        /// Width-ratio conversion kept for the original 1600x900 contract test. It ignores the window
        /// height and the UI scale, so the frame transform overload above is the one to use in the UI.
        /// </summary>
        public static void ToDesignSpace(float mouseX, float mouseYBottomLeft, int screenWidth,
            int screenHeight, out float designX, out float designY)
        {
            float scale = Scale(screenWidth);
            designX = mouseX / scale;
            designY = (screenHeight - mouseYBottomLeft) / scale;
        }

        /// <summary>
        /// Convert <c>Input.mousePosition</c> (bottom-left origin) with the frame transform, so the
        /// result is in the same space OnGUI draws in at the current UI scale.
        /// </summary>
        public static void ToDesignSpace(float mouseX, float mouseYBottomLeft, int screenWidth,
            int screenHeight, float uiScale, out float designX, out float designY)
        {
            UiViewport viewport = Viewport(screenWidth, screenHeight, uiScale);
            designX = mouseX / viewport.Scale;
            designY = (screenHeight - mouseYBottomLeft) / viewport.Scale;
        }

        /// <summary>The inverse used for drawing: a design point back to physical screen pixels.</summary>
        public static void ToScreenPoint(float designX, float designY, int screenHeight, float scale,
            out float screenX, out float screenYBottomLeft)
        {
            screenX = designX * scale;
            screenYBottomLeft = screenHeight - designY * scale;
        }

        /// <summary>
        /// Identity by design. Under OnGUI's <c>GUI.matrix</c>, <c>Event.mousePosition</c> is already
        /// expressed in GUI space - the same space the panels are laid out in - because IMGUI transforms
        /// event coordinates by the matrix before controls hit-test them against their design-space
        /// rects. Dividing it by the scale (or by <c>Screen.width / uiScale</c>) double-scales the point.
        /// </summary>
        public static void EventToDesignSpace(float eventX, float eventY, out float designX,
            out float designY)
        {
            designX = eventX;
            designY = eventY;
        }

        /// <summary>
        /// Screen-space point (top-left origin) to design space with the full frame transform.
        /// </summary>
        public static void ToDesignSpaceFromTopLeft(float screenX, float screenYTopLeft, int screenWidth,
            int screenHeight, float uiScale, out float designX, out float designY)
        {
            UiViewport viewport = Viewport(screenWidth, screenHeight, uiScale);
            designX = screenX / viewport.Scale;
            designY = screenYTopLeft / viewport.Scale;
        }

        /// <summary>
        /// Width-ratio variant kept for the original 1600x900 contract test; ignores the UI scale.
        /// Note that <c>Event.mousePosition</c> must not be passed here (see <see cref="EventToDesignSpace"/>).
        /// </summary>
        public static void ToDesignSpaceFromTopLeft(float screenX, float screenYTopLeft, int screenWidth,
            out float designX, out float designY)
        {
            float scale = Scale(screenWidth);
            designX = screenX / scale;
            designY = screenYTopLeft / scale;
        }

        public static bool Contains(float designX, float designY, DesignRect rect)
            => rect.Contains(designX, designY);

        public static bool Contains(float designX, float designY, IReadOnlyList<DesignRect> rects)
        {
            if (rects == null) return false;
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].Contains(designX, designY)) return true;
            return false;
        }
    }
}
