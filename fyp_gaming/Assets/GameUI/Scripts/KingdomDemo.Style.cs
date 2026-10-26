// KingdomDemo (partial) — shared parchment-and-ink theme, layout constants and UI helpers.
//
// Everything visual is reversible from the constants at the top of this file. Textures are created at
// runtime (1x1 fills and an 8x8 nine-slice button plate), so no new art assets are required.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ---------------------------------------------------------------- palette
        private static readonly Color ParchmentPanel = new Color(0.93f, 0.88f, 0.76f, 1f);
        private static readonly Color Ink = new Color(0.15f, 0.11f, 0.07f, 1f);
        private static readonly Color MutedInk = new Color(0.34f, 0.27f, 0.18f, 1f);
        private static readonly Color MutedRed = new Color(0.62f, 0.20f, 0.16f, 1f);
        private static readonly Color AccentGold = new Color(0.72f, 0.55f, 0.22f, 1f);
        private static readonly Color PanelEdge = new Color(0.37f, 0.28f, 0.16f, 1f);
        private static readonly Color ButtonFace = new Color(0.84f, 0.77f, 0.62f, 1f);
        private static readonly Color ButtonHover = new Color(0.96f, 0.90f, 0.75f, 1f);
        private static readonly Color ButtonPressed = new Color(0.72f, 0.64f, 0.48f, 1f);
        private static readonly Color ButtonDisabled = new Color(0.79f, 0.75f, 0.67f, 1f);
        private static readonly Color TabSelected = new Color(0.82f, 0.69f, 0.43f, 1f);
        private static readonly Color InkScreen = new Color(0.10f, 0.08f, 0.06f, 1f);

        // Fog overlays are dimmed at runtime; the scene/serialized values stay untouched.
        private static readonly Color FogOverlayGrey = new Color(0.46f, 0.50f, 0.54f, 1f);
        private const float FogOverlayAlpha = 0.50f;
        private const float FogOverlayGreyMix = 0.60f;

        // ---------------------------------------------------------------- layout (1600x900 design space)
        private const float DesignWidth = 1600f;
        private const float TopBarX = 10f;
        private const float TopBarY = 8f;
        private const float TopBarWidth = 1380f;
        private const float TopBarHeight = 56f;
        private const float EndTurnX = 1400f;
        private const float EndTurnY = 8f;
        private const float EndTurnWidth = 190f;
        private const float EndTurnHeight = 44f;
        private const float TabColumnX = 1400f;
        private const float TabColumnWidth = 190f;
        private const float TabTop = 70f;
        private const float TabStep = 46f;
        private const float TabHeight = 40f;
        private const float PanelTop = 70f;
        private const float LeftPanelX = 10f;
        private const float ContentRight = 1390f;
        private const float CityPanelWidth = 420f;
        private const float ArmyPanelWidth = 430f;
        private const float PanelBelowAgentY = 268f;
        private const float StatusBarX = 10f;
        private const float StatusBarY = 792f;
        private const float StatusBarWidth = 1380f;
        private const float StatusBarHeight = 100f;

        // Worker D's controller/agent panel. It sits below the top bar, centred between the selection
        // panels and the tab column; D owns the contents.
        private const float AgentPanelX = 655f;
        private const float AgentPanelY = 70f;
        private const float AgentPanelWidth = 520f;
        private const float AgentPanelHeight = 190f;

        private const float PanelAccentWidth = 5f;
        private const float PanelBorderWidth = 1.5f;

        // Selected-marker ring.
        private const float SelectionRingHeight = 0.03f;
        private const float SelectionRingScale = 1.45f;
        private const int SelectionRingSortingOrder = 150;
        private const int SelectionRingTextureSize = 128;

        // ---------------------------------------------------------------- input lock (worker D)
        // D sets these while a model or scripted seat is taking its turn. When true every order path,
        // End-turn and tab control is inert; selection itself is also disabled by Input.cs.
        bool uiInputLocked;
        string uiLockReason;

        // D implements this in KingdomDemo.Agent.cs. The declaration is intentionally empty here.
        partial void DrawAgentPanel(Rect area);

        private Vector2 selectionScroll;   // only used when a tall selection panel must scroll

        private const string ControlsHint =
            "Click a city/army to select - Wheel over panels scrolls - End turn on the right";

        // ---------------------------------------------------------------- theme resources
        private Texture2D buttonFaceTexture;
        private Texture2D buttonHoverTexture;
        private Texture2D buttonPressedTexture;
        private Texture2D buttonDisabledTexture;
        private Texture2D tabSelectedTexture;
        private GUIStyle uiTitle;
        private GUIStyle uiPanelTitle;
        private GUIStyle uiBody;
        private GUIStyle uiSmall;
        private GUIStyle uiMono;
        private GUIStyle uiButton;
        private GUIStyle uiTab;
        private GUIStyle uiTabActive;
        private GUIStyle uiReason;
        private GUIStyle uiReasonWrap;
        private GUIStyle uiHint;
        private GUIStyle uiField;
        private GUIStyle uiButtonDisabled;
        private GUIStyle uiButtonDisabledSmall;
        private bool uiThemeReady;

        void PrepareUiTheme()
        {
            if (uiThemeReady) return;

            buttonFaceTexture = FramedTexture(ButtonFace, PanelEdge);
            buttonHoverTexture = FramedTexture(ButtonHover, PanelEdge);
            buttonPressedTexture = FramedTexture(ButtonPressed, PanelEdge);
            buttonDisabledTexture = FramedTexture(ButtonDisabled, MutedInk);
            tabSelectedTexture = FramedTexture(TabSelected, PanelEdge);

            uiTitle = LabelStyle(21, Ink, FontStyle.Bold);
            uiPanelTitle = LabelStyle(18, Ink, FontStyle.Bold);
            uiBody = LabelStyle(16, Ink);
            uiSmall = LabelStyle(14, MutedInk);
            uiMono = LabelStyle(12, MutedInk);
            uiReason = LabelStyle(14, MutedRed);
            uiReasonWrap = new GUIStyle(uiReason) { wordWrap = true };
            uiHint = LabelStyle(14, MutedInk);
            uiField = FieldStyle(14, buttonFaceTexture);
            uiButton = ButtonStyle(16, buttonFaceTexture, buttonHoverTexture, buttonPressedTexture, Ink);
            uiTab = ButtonStyle(14, buttonFaceTexture, buttonHoverTexture, buttonPressedTexture, Ink);
            uiTabActive = ButtonStyle(14, tabSelectedTexture, buttonHoverTexture, buttonPressedTexture, Ink);

            // GUIStyle no longer exposes a disabled state, so the inert look is baked into 'normal'
            // on dedicated styles used while a control is not interactive.
            uiButtonDisabled = ButtonStyle(16, buttonDisabledTexture, buttonDisabledTexture,
                                           buttonDisabledTexture, MutedRed);
            uiButtonDisabledSmall = ButtonStyle(14, buttonDisabledTexture, buttonDisabledTexture,
                                                buttonDisabledTexture, MutedRed);

            uiThemeReady = true;
        }

        static GUIStyle LabelStyle(int size, Color color, FontStyle fontStyle = FontStyle.Normal, bool wrap = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                wordWrap = wrap,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0),
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        static GUIStyle ButtonStyle(int size, Texture2D normal, Texture2D hover, Texture2D pressed,
                                    Color text)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(6, 6, 4, 4),
            };
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = pressed;
            style.focused.background = hover;
            style.normal.textColor = text;
            style.hover.textColor = text;
            style.active.textColor = text;
            style.focused.textColor = text;
            return style;
        }

        static GUIStyle FieldStyle(int size, Texture2D background)
        {
            var style = new GUIStyle(GUI.skin.textField)
            {
                fontSize = size,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(6, 6, 4, 4),
            };
            style.normal.background = background;
            style.hover.background = background;
            style.focused.background = background;
            style.active.background = background;
            style.normal.textColor = Ink;
            style.hover.textColor = Ink;
            style.focused.textColor = Ink;
            style.active.textColor = Ink;
            return style;
        }

        static Texture2D FramedTexture(Color face, Color edge)
        {
            const int size = 8;
            var pixels = new Color32[size * size];
            Color32 fill = face;
            Color32 line = edge;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = (x == 0 || y == 0 || x == size - 1 || y == size - 1) ? line : fill;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "KingdomDemoUI_Plate",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // ---------------------------------------------------------------- shared drawing
        static void FillRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>Opaque parchment plate with a faction-coloured edge. Every panel and popup uses this.</summary>
        void DrawBackdrop(Rect rect, Color? accent = null)
        {
            FillRect(rect, ParchmentPanel);
            FillRect(new Rect(rect.x, rect.y, PanelAccentWidth, rect.height), accent ?? CurrentSeatAccent());
            FillRect(new Rect(rect.x, rect.y, rect.width, PanelBorderWidth), PanelEdge);
            FillRect(new Rect(rect.x, rect.y + rect.height - PanelBorderWidth, rect.width, PanelBorderWidth), PanelEdge);
            FillRect(new Rect(rect.x + rect.width - PanelBorderWidth, rect.y, PanelBorderWidth, rect.height), PanelEdge);
        }

        Color CurrentSeatAccent()
            => currentSeat != null && factionColors.TryGetValue(currentSeat, out var color) ? color : AccentGold;

        /// <summary>The old charcoal plate remains available as a high-contrast fallback for debug overlays.</summary>
        static Color LegacyDarkPlate() => PanelColor;

        static string ControllerLabel(string controller)
        {
            if (controller == ControllerType.Human) return "Human";
            if (controller == ControllerType.Llm) return "LLM";
            if (controller == ControllerType.ScriptedAi) return "Scripted AI";
            return string.IsNullOrEmpty(controller) ? "Unknown" : controller;
        }

        string LockedReason()
            => string.IsNullOrEmpty(uiLockReason) ? "Waiting for the current controller" : uiLockReason;

        // ---------------------------------------------------------------- panel placement
        /// <summary>The opaque top-centre panel this layout must never slide under.</summary>
        Rect ActiveOverlayPanel()
        {
            if (spectatorMode) return SpectatorPanelArea();
            if (agentHasSeat) return new Rect(AgentPanelX, AgentPanelY, AgentPanelWidth, AgentPanelHeight);
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
        /// The selected city/army panel goes on the half of the screen away from its marker, so the
        /// order controls never sit on top of the thing being ordered. If the marker cannot be resolved
        /// (no camera / off-screen behind it), the safe fallback is the left column. Panels that share
        /// the agent's horizontal band start below it and are height-capped; callers that still need
        /// their full content use a scroll view. A tall panel must never slide back up under the panel.
        /// </summary>
        Rect PlaceSelectionPanel(float width, float height)
        {
            float x = SelectedMarkerOnLeftHalf() ? ContentRight - width : LeftPanelX;
            float bottom = StatusBarY - 8f;
            float y = PanelTop;
            Rect overlay = ActiveOverlayPanel();

            bool sharesOverlayBand = overlay.width > 0f
                && x < overlay.xMax && x + width > overlay.x;
            if (sharesOverlayBand)
            {
                y = overlay.yMax + 8f;
                height = Mathf.Min(height, Mathf.Max(80f, bottom - y));
            }
            else if (y + height > bottom)
            {
                if (height <= bottom - PanelTop)
                    y = bottom - height;
                else
                    height = bottom - PanelTop;
            }
            return new Rect(x, y, width, height);
        }

        bool SelectedMarkerOnLeftHalf()
        {
            var marker = SelectedMarkerObject();
            var cam = Camera.main;
            if (marker == null || cam == null) return false;

            Vector3 screenPoint = cam.WorldToScreenPoint(marker.transform.position);
            if (screenPoint.z <= 0f) return false;
            float scale = Screen.width / DesignWidth;
            return screenPoint.x / scale < DesignWidth * 0.5f;
        }

        GameObject SelectedMarkerObject()
        {
            if (selCity != null && cityObjs.TryGetValue(selCity.Id, out var cityMarker)) return cityMarker;
            if (selArmy != null && armyObjs.TryGetValue(selArmy.Id, out var armyMarker)) return armyMarker;
            return null;
        }

        // ---------------------------------------------------------------- order labels
        /// <summary>English display name for a unit type. The engine keeps the Chinese design names.</summary>
        static string UnitTypeName(string type)
        {
            switch (UnitTypes.Normalize(type))
            {
                case UnitTypes.Archer: return "Archer";
                case UnitTypes.Cavalry: return "Cavalry";
                case UnitTypes.Siege: return "Siege";
                default: return "Infantry";
            }
        }

        /// <summary>Cost label for one action: "1 order", "2 orders" (singular for 0 and 1).</summary>
        static string OrderCost(string type)
        {
            int cost = BalanceConfig.CommandPointCost(type);
            return cost + (cost >= 2 ? " orders" : " order");
        }

        /// <summary>Parenthesized cost suffix for an order button: "(1 order)", "(2 orders)", "(0 order)".</summary>
        static string OrderCostSuffix(string type) => "(" + OrderCost(type) + ")";

        static string FirstReason(string current, string candidate) => current ?? candidate;

        int MilitaryLevel() => obs != null ? obs.ResearchLevel(ResearchLine.Military) : 0;

        // ---------------------------------------------------------------- order buttons
        /// <summary>
        /// One order button. It always submits a real ActionCommand through SubmitHuman and shows the
        /// action's order cost from BalanceConfig; the engine remains the only authority. While
        /// uiInputLocked is set the button is disabled and returns the lock reason for the panel hint.
        /// </summary>
        string OrderButton(Rect r, string type, string targetId, string label, string blockedReason,
            int paramA, int paramB, GUIStyle btn,
            Dictionary<string, string> args = null, string text = null, System.Action onSubmitted = null)
        {
            string reason = uiInputLocked ? LockedReason() : blockedReason;
            bool available = reason == null;
            string buttonText = $"{label} {OrderCostSuffix(type)}";
            float wantedWidth = Mathf.Max(r.width, btn.CalcSize(new GUIContent(buttonText)).x + 16f);
            r.width = Mathf.Min(wantedWidth, Mathf.Max(80f, ContentRight - r.x - 2f));

            GUIStyle style = available
                ? btn
                : (btn.fontSize <= 14 ? uiButtonDisabledSmall : uiButtonDisabled);
            var previous = GUI.enabled;
            GUI.enabled = available;
            if (GUI.Button(r, buttonText, style) && SubmitHuman(new ActionCommand
                {
                    Type = type, TargetId = targetId, ParamA = paramA, ParamB = paramB,
                    Args = args, Text = text, Reason = "UI: " + type,
                }))
                onSubmitted?.Invoke();
            GUI.enabled = previous;
            return available ? null : label + ": " + reason;
        }
    }
}
