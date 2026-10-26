// KingdomDemo (partial) — presentation-only helpers added by the bounded V1 visual pass.
//
// City labels are drawn exclusively from the current display seat's Observation (obs.Cities and
// obs.ScoutedCities); no canonical city/army/ownership data is read here. The fog visibility rule is
// untouched (see Visuals.Fog.cs); this file only changes how the map and labels are presented.

using UnityEngine;
using UnityEngine.Rendering;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo
    {
        // ------------------------------------------------------------------ text fitting
        /// <summary>Single-line text shortened with an ellipsis so it cannot cross the rect.</summary>
        static string EllipsizeToWidth(GUIStyle style, string value, float maxWidth)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            string one = value.Replace('\r', ' ').Replace('\n', ' ');
            if (maxWidth <= 0f) return "";
            if (style.CalcSize(new GUIContent(one)).x <= maxWidth) return one;

            const string ellipsis = "...";
            int low = 0, high = one.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = one.Substring(0, mid).TrimEnd() + ellipsis;
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) low = mid;
                else high = mid - 1;
            }
            return one.Substring(0, low).TrimEnd() + ellipsis;
        }

        /// <summary>Word-wrapped text clamped to a fixed number of lines (ellipsis on overflow).</summary>
        static string ClampWrappedText(GUIStyle style, string value, float width, int maxLines)
        {
            if (string.IsNullOrEmpty(value) || maxLines <= 0) return value ?? "";
            string one = value.Replace('\r', ' ').Replace('\n', ' ');
            float maxHeight = maxLines * TextLineHeight(style);
            if (style.CalcHeight(new GUIContent(one), width) <= maxHeight + 0.5f) return one;

            const string ellipsis = "...";
            int low = 0, high = one.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = one.Substring(0, mid).TrimEnd() + ellipsis;
                if (style.CalcHeight(new GUIContent(candidate), width) <= maxHeight) low = mid;
                else high = mid - 1;
            }
            return one.Substring(0, low).TrimEnd() + ellipsis;
        }

        static float TextLineHeight(GUIStyle style)
            => style.lineHeight > 0f ? style.lineHeight : style.fontSize + 3f;

        static bool DesignPointerIn(Rect rect)
        {
            var e = Event.current;
            if (e == null || Screen.width <= 0) return false;
            float scale = Screen.width / DesignWidth;
            var mouse = new Vector2(e.mousePosition.x / scale, e.mousePosition.y / scale);
            return rect.Contains(mouse);
        }

        static void DrawFittedLabel(Rect rect, string text, GUIStyle style, string tooltip = null)
        {
            GUI.Label(rect, new GUIContent(text, tooltip), style);
            if (!string.IsNullOrEmpty(tooltip) && DesignPointerIn(rect)) GUI.tooltip = tooltip;
        }

        static void DrawWrappedClampedLabel(Rect rect, string text, GUIStyle wrapStyle, int maxLines,
                                            string tooltip = null)
        {
            string clamped = ClampWrappedText(wrapStyle, text, rect.width, maxLines);
            GUI.Label(rect, new GUIContent(clamped, tooltip), wrapStyle);
            if (!string.IsNullOrEmpty(tooltip) && DesignPointerIn(rect)) GUI.tooltip = tooltip;
        }

        /// <summary>Draws GUI.tooltip (used only for the run-record path and full truncated text).</summary>
        void DrawTooltip()
        {
            if (string.IsNullOrEmpty(GUI.tooltip)) return;
            var e = Event.current;
            if (e == null) return;
            float scale = Screen.width / DesignWidth;
            var mouse = new Vector2(e.mousePosition.x / scale, e.mousePosition.y / scale);
            float width = Mathf.Min(TooltipMaxWidth,
                Mathf.Max(120f, uiSmall.CalcSize(new GUIContent(GUI.tooltip)).x + TooltipPadding * 2f));
            float height = uiSmallWrap.CalcHeight(new GUIContent(GUI.tooltip), width - TooltipPadding * 2f)
                           + TooltipPadding * 2f;
            var rect = new Rect(mouse.x + TooltipOffset, mouse.y + TooltipOffset, width, height);
            if (rect.xMax > DesignWidth - 8f) rect.x = mouse.x - TooltipOffset - width;
            if (rect.yMax > DesignHeight - 8f) rect.y = mouse.y - TooltipOffset - height;
            rect.x = Mathf.Clamp(rect.x, 8f, Mathf.Max(8f, DesignWidth - rect.width - 8f));
            rect.y = Mathf.Clamp(rect.y, 8f, Mathf.Max(8f, DesignHeight - rect.height - 8f));

            FillRect(rect, PanelEdge);
            FillRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), ParchmentPanel);
            GUI.Label(new Rect(rect.x + TooltipPadding, rect.y + TooltipPadding,
                               rect.width - TooltipPadding * 2f, rect.height - TooltipPadding * 2f),
                      GUI.tooltip, uiSmallWrap);
        }

        // ------------------------------------------------------------------ city labels
        /// <summary>
        /// Observation-authorized city name tags. Only CityViews and ScoutedCityViews from the current
        /// seat are considered; a city absent from the observation keeps no label at all.
        /// </summary>
        void DrawCityLabels()
        {
            if (!ready || obs == null || Camera.main == null || activeTab != "map"
                || reportShowing || handoverShowing) return;

            string viewedCapitalId = ViewedCapitalCityId();
            foreach (var city in obs.Cities)
                DrawCityTag(city, viewedCapitalId == city.Id);

            foreach (var scouted in obs.ScoutedCities)
            {
                if (obs.FindCity(scouted.CityId) != null) continue;   // a visible tag already exists
                DrawScoutedCityTag(scouted);
            }
        }

        string ViewedCapitalCityId()
        {
            if (obs == null || string.IsNullOrEmpty(currentSeat)) return null;

            // The observation carries no "capital" flag, so the viewed seat's capital is represented
            // by its first own city in id order (the lowest id): deterministic, view-authorized, and
            // never another seat's city. This is the only place the chip's target is decided.
            string capitalId = null;
            for (int i = 0; i < obs.Cities.Count; i++)
            {
                var city = obs.Cities[i];
                if (city.OwnerSeatId != currentSeat) continue;
                if (capitalId == null || string.CompareOrdinal(city.Id, capitalId) < 0)
                    capitalId = city.Id;
            }
            return capitalId;
        }

        bool TryProjectCity(int x, int z, out Vector2 designPoint)
        {
            designPoint = Vector2.zero;
            var node = TBTK.GridManager.GetNode(x, z);
            var cam = Camera.main;
            if (node == null || cam == null) return false;
            var screen = cam.WorldToScreenPoint(node.GetPos() + Vector3.up * CityLabelAnchorHeight);
            if (screen.z <= 0f) return false;
            float scale = Screen.width / DesignWidth;
            designPoint = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            return true;
        }

        void DrawCityTag(CityView city, bool viewedCapital)
        {
            if (!TryProjectCity(city.X, city.Z, out var anchor)) return;
            string text = EllipsizeToWidth(uiCityTag, city.Name,
                                           CityLabelMaxWidth - CityLabelPadX * 2f);
            float width = Mathf.Min(CityLabelMaxWidth,
                uiCityTag.CalcSize(new GUIContent(text)).x + CityLabelPadX * 2f);
            float x = Mathf.Clamp(anchor.x - width * 0.5f, LeftPanelX,
                                  Mathf.Max(LeftPanelX, ContentRight - width));
            float y = anchor.y + CityLabelOffsetY;
            if (y + CityLabelHeight > DesignHeight || y < TopBarY + TopBarHeight) return;

            Color owner = OwnerColor(city.OwnerSeatId);
            Color border = city.OwnerSeatId == null ? MutedInk : owner;
            Color textColor = city.OwnerSeatId == null ? MutedInk : owner;

            FillRect(new Rect(x, y, width, CityLabelHeight), border);
            FillRect(new Rect(x + CityLabelBorder, y + CityLabelBorder,
                              width - CityLabelBorder * 2f, CityLabelHeight - CityLabelBorder * 2f),
                     CityLabelPlate);

            Color previous = uiCityTag.normal.textColor;
            uiCityTag.normal.textColor = textColor;
            DrawFittedLabel(new Rect(x + CityLabelPadX, y, width - CityLabelPadX * 2f, CityLabelHeight),
                            text, uiCityTag, city.Name);
            uiCityTag.normal.textColor = previous;

            if (!viewedCapital) return;
            float chipWidth = Mathf.Min(ViewedMarkerWidth, width);
            var chip = new Rect(x + (width - chipWidth) * 0.5f,
                                y - ViewedMarkerGap - ViewedMarkerHeight, chipWidth, ViewedMarkerHeight);
            FillRect(chip, ViewedMarkerColor);
            GUI.Label(new Rect(chip.x + 1f, chip.y, chip.width - 2f, chip.height), "viewed", uiViewedTag);
        }

        void DrawScoutedCityTag(ScoutedCityView scouted)
        {
            if (!TryProjectCity(scouted.X, scouted.Z, out var anchor)) return;
            string full = scouted.Name + " - scouted";
            string text = EllipsizeToWidth(uiCityTagSmall, full, CityLabelMaxWidth - CityLabelPadX * 2f);
            float width = Mathf.Min(CityLabelMaxWidth,
                uiCityTagSmall.CalcSize(new GUIContent(text)).x + CityLabelPadX * 2f);
            float x = Mathf.Clamp(anchor.x - width * 0.5f, LeftPanelX,
                                  Mathf.Max(LeftPanelX, ContentRight - width));
            float y = anchor.y + CityLabelOffsetY;
            if (y + CityLabelHeight > DesignHeight || y < TopBarY + TopBarHeight) return;

            FillRect(new Rect(x, y, width, CityLabelHeight), MutedInk);
            FillRect(new Rect(x + CityLabelBorder, y + CityLabelBorder,
                              width - CityLabelBorder * 2f, CityLabelHeight - CityLabelBorder * 2f),
                     CityLabelScoutedPlate);
            DrawFittedLabel(new Rect(x + CityLabelPadX, y, width - CityLabelPadX * 2f, CityLabelHeight),
                            text, uiCityTagSmall, full);
        }

        // ------------------------------------------------------------------ terrain seams
        /// <summary>
        /// A dark quad just under the node meshes: the bright cracks between cells become quiet seams.
        /// This does not scale node transforms (and therefore cannot move a collider or a click target).
        /// </summary>
        void BuildGridSeamUnderlay()
        {
            if (!GridSeamUnderlayEnabled) return;
            var grid = GameObject.Find("Grid");
            if (grid == null) return;
            var renderers = grid.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            bool any = false;
            Bounds bounds = default;
            foreach (var nodeRenderer in renderers)
            {
                if (nodeRenderer == null) continue;
                if (!any) { bounds = nodeRenderer.bounds; any = true; }
                else bounds.Encapsulate(nodeRenderer.bounds);
            }
            if (!any) return;

            var go = new GameObject("Kingdom_GridSeamUnderlay");
            go.transform.position = new Vector3(bounds.center.x, GridSeamUnderlayY, bounds.center.z);

            float halfWidth = bounds.extents.x * GridSeamUnderlayPadding;
            float halfDepth = bounds.extents.z * GridSeamUnderlayPadding;
            var mesh = new Mesh
            {
                name = "Kingdom_GridSeamUnderlay",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-halfWidth, 0f, -halfDepth),
                    new Vector3(-halfWidth, 0f, halfDepth),
                    new Vector3(halfWidth, 0f, halfDepth),
                    new Vector3(halfWidth, 0f, -halfDepth),
                },
                uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();

            var material = GridSeamMaterial();
            if (material == null)
            {
                Destroy(go);
                Destroy(mesh);
                return;
            }

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.material = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Material GridSeamMaterial()
        {
            Shader shader = null;
            foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default" })
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = "Kingdom_GridSeam",
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", GridSeamColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", GridSeamColor);
            return material;
        }

        // ------------------------------------------------------------------ framing
        /// <summary>
        /// One-shot framing at InitGame end: centre the pivot and choose a zoom that shows both capitals
        /// with margin. KingdomDemo's -100 execution order means this happens before CameraControl.Start
        /// caches its private zoom, so TBTK does not immediately lerp back and later user input wins.
        /// </summary>
        void ApplyInitialCameraFraming()
        {
            var control = FindAnyObjectByType<TBTK.CameraControl>();
            float distance = InitialCameraDistance;
            if (control != null)
            {
                var pivot = control.transform;
                pivot.position = new Vector3(InitialCameraPivotX, pivot.position.y, InitialCameraPivotZ);
                distance = Mathf.Clamp(InitialCameraDistance,
                                       control.minZoomDistance, control.maxZoomDistance);
            }

            var cam = Camera.main;
            if (cam == null) return;
            var local = cam.transform.localPosition;
            cam.transform.localPosition = new Vector3(local.x, local.y, -distance);
        }

        string ViewedFactionNameFromObservation()
        {
            if (obs != null)
            {
                string seat = !string.IsNullOrEmpty(currentSeat) ? currentSeat : spectatorLastActiveSeat;
                if (!string.IsNullOrEmpty(seat))
                {
                    var faction = obs.Faction(seat);
                    if (faction != null && !string.IsNullOrEmpty(faction.Name)) return faction.Name;
                    return seat;
                }
                if (obs.Factions.Count > 0 && !string.IsNullOrEmpty(obs.Factions[0].Name))
                    return obs.Factions[0].Name;
            }
            return string.IsNullOrEmpty(currentSeat) ? "-" : currentSeat;
        }
    }
}
