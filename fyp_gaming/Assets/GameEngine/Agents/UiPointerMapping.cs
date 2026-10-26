using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>An axis-aligned rectangle in the 1600x900 design space (top-left origin).</summary>
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
    /// Kept free of UnityEngine so the 16:9 / 4:3 corner cases can be unit-tested headlessly.
    ///
    /// <para><c>Input.mousePosition</c> has a BOTTOM-LEFT origin; every rectangle in
    /// <c>KingdomDemo.uiRects</c> is top-left design space. <c>Event.mousePosition</c> is already
    /// top-left screen space. Both are scaled by the same uniform factor OnGUI applies:
    /// <c>GUI.matrix = Scale(Screen.width / 1600)</c>. The design canvas is therefore anchored at the
    /// window's top-left, not stretched to its height: a 4:3 window shows design y 900 at screen
    /// y = 900 * scale and leaves the area below it unused, exactly as OnGUI draws it.</para>
    /// </summary>
    public static class UiPointerMapping
    {
        public const float DesignWidth = 1600f;
        public const float DesignHeight = 900f;

        /// <summary>The uniform scale of <c>GUI.matrix</c> for the current window.</summary>
        public static float Scale(int screenWidth)
            => screenWidth > 0 ? screenWidth / DesignWidth : 1f;

        /// <summary>Convert <c>Input.mousePosition</c> (bottom-left origin) to design space.</summary>
        public static void ToDesignSpace(float mouseX, float mouseYBottomLeft, int screenWidth,
            int screenHeight, out float designX, out float designY)
        {
            float scale = Scale(screenWidth);
            designX = mouseX / scale;
            designY = (screenHeight - mouseYBottomLeft) / scale;
        }

        /// <summary>Convert <c>Event.mousePosition</c> (top-left origin) to design space.</summary>
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
