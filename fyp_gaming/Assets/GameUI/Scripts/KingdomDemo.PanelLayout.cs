// KingdomDemo (partial) — side-dock and selection-panel placement.
//
// Presentation-only geometry: the selected object's marker is projected to design space and the panel
// is anchored beside it. The right dock (spectator/agent) and the top/bottom HUD stay untouched.

using UnityEngine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        bool SideDockVisible() => spectatorMode || agentHasSeat;

        /// <summary>The opaque side panel this layout must never slide under.</summary>
        Rect ActiveOverlayPanel()
        {
            if (spectatorMode) return SpectatorPanelArea();
            if (agentHasSeat) return AgentPanelArea();
            return new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>
        /// Moves a small popup out of the agent/spectator rect: below first, then above, then to the
        /// side. Popups are small enough that one of those always fits.
        /// </summary>
        Rect KeepClearOfOverlay(Rect rect)
        {
            Rect overlay = ActiveOverlayPanel();
            if (overlay.width <= 0f || !rect.Overlaps(overlay)) return rect;

            float top = TopBarY + TopBarHeight + 8f;
            float bottom = StatusBarY - 8f;

            if (overlay.yMax + 8f + rect.height <= bottom)
            {
                rect.y = overlay.yMax + 8f;
                return rect;
            }
            if (overlay.y - 8f - rect.height >= top)
            {
                rect.y = overlay.y - 8f - rect.height;
                return rect;
            }
            if (overlay.x - 8f - rect.width >= LeftPanelX)
            {
                rect.x = overlay.x - 8f - rect.width;
                return rect;
            }
            if (overlay.xMax + 8f + rect.width <= ContentRight)
            {
                rect.x = overlay.xMax + 8f;
                return rect;
            }

            // Last resort: keep the popup inside the safe band, accepting a clipped height.
            rect.height = Mathf.Min(rect.height, bottom - top);
            rect.y = Mathf.Clamp(rect.y, top, Mathf.Max(top, bottom - rect.height));
            rect.x = Mathf.Clamp(rect.x, LeftPanelX, Mathf.Max(LeftPanelX, ContentRight - rect.width));
            return rect;
        }

        /// <summary>
        /// Anchors the selected city/army panel beside its marker. The preferred side is the one away
        /// from the nearest screen edge; if it has no room, the other side is used. The panel is capped
        /// to the left of the right dock and above the status strip, so it never covers the agent dock,
        /// top bar, tab column or status bar. Callers scroll a body when the height cap bites.
        /// </summary>
        Rect PlaceSelectionPanel(float width, float height)
        {
            width = Mathf.Min(width, SelectionPanelMaxWidth);
            float top = PanelTop;
            float bottom = StatusBarY - PanelGutter;
            height = Mathf.Min(height, Mathf.Max(1f, bottom - top));

            Rect overlay = ActiveOverlayPanel();
            float rightLimit = overlay.width > 0f ? overlay.x - PanelGutter : ContentRight;
            rightLimit = Mathf.Min(rightLimit, ContentRight);

            float x = LeftPanelX;
            if (TrySelectedMarkerDesignPoint(out var marker))
            {
                float rightX = marker.x + SelectionPanelMarkerGap;
                float leftX = marker.x - SelectionPanelMarkerGap - width;
                bool rightFits = rightX >= LeftPanelX && rightX + width <= rightLimit;
                bool leftFits = leftX >= LeftPanelX && leftX + width <= rightLimit;
                bool preferRight = marker.x < DesignWidth * 0.5f;

                if (preferRight ? rightFits : leftFits)
                    x = preferRight ? rightX : leftX;
                else if (preferRight ? leftFits : rightFits)
                    x = preferRight ? leftX : rightX;
                else
                {
                    // Neither side has the full width free (a marker very close to the dock). Use the
                    // side whose anchor needs the smaller clamp; the dock limit below still wins.
                    float maxX = Mathf.Max(LeftPanelX, rightLimit - width);
                    float clampedRight = Mathf.Clamp(rightX, LeftPanelX, maxX);
                    float clampedLeft = Mathf.Clamp(leftX, LeftPanelX, maxX);
                    x = Mathf.Abs(clampedRight - rightX) <= Mathf.Abs(clampedLeft - leftX)
                        ? clampedRight : clampedLeft;
                }
            }

            x = Mathf.Clamp(x, LeftPanelX, Mathf.Max(LeftPanelX, rightLimit - width));
            float y = top;
            if (y + height > bottom) y = Mathf.Max(top, bottom - height);
            return new Rect(x, y, width, height);
        }

        bool TrySelectedMarkerDesignPoint(out Vector2 designPoint)
        {
            designPoint = Vector2.zero;
            var marker = SelectedMarkerObject();
            var cam = Camera.main;
            if (marker == null || cam == null || Screen.width <= 0) return false;

            Vector3 screenPoint = cam.WorldToScreenPoint(marker.transform.position);
            if (screenPoint.z <= 0f) return false;
            float scale = Screen.width / DesignWidth;
            designPoint = new Vector2(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
            return true;
        }

        GameObject SelectedMarkerObject()
        {
            if (selCity != null && cityObjs.TryGetValue(selCity.Id, out var cityMarker)) return cityMarker;
            if (selArmy != null && armyObjs.TryGetValue(selArmy.Id, out var armyMarker)) return armyMarker;
            return null;
        }
    }
}
