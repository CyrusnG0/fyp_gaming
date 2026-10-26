// KingdomDemo (partial) — a runtime selection ring that stays legible over the fog.
//
// The TBTK indicator prefabs are particle/mesh effects and disappear under the demo's world-space fog
// plates. This ring is a separate SpriteRenderer drawn above the fog, so the selected city or army is
// always obvious. It is presentation-only: no game state is read or changed.

using UnityEngine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private SpriteRenderer selectionRing;
        private Texture2D selectionRingTexture;

        void RefreshSelectionRing()
        {
            bool show = ready && engine != null && activeTab == "map" && !reportShowing && !handoverShowing
                && engine.State.WinnerSeatId == null && (selCity != null || selArmy != null);
            if (!show)
            {
                if (selectionRing != null) selectionRing.enabled = false;
                return;
            }

            int x, z;
            string ownerSeat;
            if (selCity != null)
            {
                x = selCity.X;
                z = selCity.Z;
                ownerSeat = selCity.OwnerSeatId;
            }
            else
            {
                x = selArmy.X;
                z = selArmy.Z;
                ownerSeat = selArmy.OwnerSeatId;
            }

            var node = TBTK.GridManager.GetNode(x, z);
            if (node == null)
            {
                if (selectionRing != null) selectionRing.enabled = false;
                return;
            }

            EnsureSelectionRing();
            Vector3 position = node.GetPos();
            position.y = SelectionRingHeight;
            selectionRing.transform.position = position;

            float nodeSize = TBTK.GridManager.GetNodeSize();
            float spriteWidth = selectionRing.sprite != null ? selectionRing.sprite.bounds.size.x : 1f;
            float scale = spriteWidth > 0f ? nodeSize * SelectionRingScale / spriteWidth : 1f;
            selectionRing.transform.localScale = Vector3.one * scale;
            selectionRing.color = RingColor(ownerSeat);
            selectionRing.enabled = true;
        }

        /// <summary>
        /// A hex that holds both a city and an army offers a two-button choice. Drawn from the
        /// observation, like everything else, so an unseen hex has nothing to offer.
        /// </summary>
        void DrawLocationChoice(GUIStyle body, GUIStyle btn)
        {
            if (!locationChoiceOpen || activeTab != "map") return;

            var node = TBTK.GridManager.GetNode(locationChoiceX, locationChoiceZ);
            var city = obs != null ? obs.Cities.Find(c => c.X == locationChoiceX && c.Z == locationChoiceZ) : null;
            var army = obs != null ? obs.ArmiesOf(currentSeat).Find(a => a.X == locationChoiceX && a.Z == locationChoiceZ) : null;
            if (node == null || city == null || army == null)
            {
                locationChoiceOpen = false;
                return;
            }

            float scale = Screen.width / 1600f;
            Vector3 screenPoint = Camera.main != null
                ? Camera.main.WorldToScreenPoint(node.GetPos())
                : new Vector3(800f * scale, Screen.height * 0.5f, 1f);
            float x = screenPoint.x / scale + 24f;
            float y = (Screen.height - screenPoint.y) / scale - 42f;
            const float width = 220f;
            const float height = 112f;
            x = Mathf.Clamp(x, LeftPanelX, ContentRight - width - 8f);
            y = Mathf.Clamp(y, TopBarY + TopBarHeight + 8f, StatusBarY - height - 8f);

            locationChoiceRect = KeepClearOfOverlay(new Rect(x, y, width, height));
            x = locationChoiceRect.x;
            y = locationChoiceRect.y;
            uiRects.Add(locationChoiceRect);
            DrawBackdrop(locationChoiceRect);
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 22f), "Choose a target to inspect", uiPanelTitle);

            if (GUI.Button(new Rect(x + 12f, y + 42f, width - 24f, 28f),
                           EllipsizeToWidth(btn, city.Name, width - 36f), btn))
            {
                locationChoiceOpen = false;
                selCity = city;
                selArmy = null;
            }
            string armyLabel = $"{army.Name} ({UnitTypeName(army.UnitType)})";
            if (GUI.Button(new Rect(x + 12f, y + 76f, width - 24f, 28f),
                           EllipsizeToWidth(btn, armyLabel, width - 36f), btn))
            {
                locationChoiceOpen = false;
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(node);
                ShowArmyMoveTargets(army);
                statusMsg = "Selected " + army.Name + " (" + UnitTypeName(army.UnitType) + ") — choose a destination";
            }
        }

        /// <summary>
        /// The selected army's name plate, drawn only while its selection ring is on screen. The name
        /// comes from the display seat's ArmyView; this is presentation only and never a new info path.
        /// </summary>
        void DrawSelectedArmyLabel()
        {
            if (!ready || selArmy == null || activeTab != "map" || reportShowing || handoverShowing
                || engine == null || engine.State.WinnerSeatId != null) return;
            var cam = Camera.main;
            var node = TBTK.GridManager.GetNode(selArmy.X, selArmy.Z);
            if (cam == null || node == null) return;

            Vector3 screen = cam.WorldToScreenPoint(node.GetPos() + Vector3.up * CityLabelAnchorHeight);
            if (screen.z <= 0f) return;
            float scale = Screen.width / DesignWidth;
            var anchor = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);

            string text = EllipsizeToWidth(uiCityTag, selArmy.Name,
                                           SelectionArmyLabelMaxWidth - SelectionArmyLabelPadX * 2f);
            float width = Mathf.Min(SelectionArmyLabelMaxWidth,
                uiCityTag.CalcSize(new GUIContent(text)).x + SelectionArmyLabelPadX * 2f);
            float x = Mathf.Clamp(anchor.x - width * 0.5f, LeftPanelX,
                                  Mathf.Max(LeftPanelX, ContentRight - width));
            float y = anchor.y - SelectionArmyLabelOffsetY;
            if (y < TopBarY + TopBarHeight) y = anchor.y + CityLabelOffsetY;

            // This plate is drawn above the marker, so a click on it lands on a different hex than the
            // selected army's. Register it as UI for this frame (OnGUI clears uiRects at the top, and
            // DrawSelectedArmyLabel runs before HandleClicks) so the click cannot fall through to the
            // map raycast and issue a march to the hex behind the plate.
            var plate = new Rect(x, y, width, SelectionArmyLabelHeight);
            uiRects.Add(plate);

            Color owner = OwnerColor(selArmy.OwnerSeatId);
            FillRect(plate, owner);
            FillRect(new Rect(x + CityLabelBorder, y + CityLabelBorder,
                              width - CityLabelBorder * 2f, SelectionArmyLabelHeight - CityLabelBorder * 2f),
                     CityLabelPlate);

            Color previous = uiCityTag.normal.textColor;
            uiCityTag.normal.textColor = owner;
            GUI.Label(new Rect(x + SelectionArmyLabelPadX, y,
                               width - SelectionArmyLabelPadX * 2f, SelectionArmyLabelHeight),
                      text, uiCityTag);
            uiCityTag.normal.textColor = previous;
        }

        void EnsureSelectionRing()
        {
            if (selectionRing != null) return;

            var go = new GameObject("Kingdom_SelectionRing");
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(SelectionRingTexture(),
                new Rect(0f, 0f, SelectionRingTextureSize, SelectionRingTextureSize),
                new Vector2(0.5f, 0.5f), 100f);
            renderer.sortingOrder = SelectionRingSortingOrder;
            renderer.enabled = false;
            selectionRing = renderer;
        }

        Color RingColor(string ownerSeat)
        {
            if (ownerSeat != null && factionColors.TryGetValue(ownerSeat, out var color))
                return Color.Lerp(color, Color.white, SelectionRingWhiteMix);
            return AccentGold;
        }

        Texture2D SelectionRingTexture()
        {
            if (selectionRingTexture != null) return selectionRingTexture;

            const int size = SelectionRingTextureSize;
            const float centre = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            var dark = new Color32(14, 10, 6, 220);
            var white = new Color32(255, 255, 255, 255);
            var echo = new Color32(255, 255, 255, 90);
            var underlay = new Color32(14, 10, 6, 26);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 color = clear;
                    if (radius >= SelectionRingBandOuter && radius <= SelectionRingInkRadius)
                        color = dark;                                                   // ink outline
                    else if (radius >= SelectionRingBandInner && radius < SelectionRingBandOuter)
                        color = white;                                                  // faction-tinted ring
                    else if (radius >= SelectionRingEchoRadius && radius < SelectionRingBandInner)
                        color = echo;                                                   // soft inner echo
                    else if (radius < SelectionRingEchoRadius)
                        color = underlay;                                               // faint centre wash
                    pixels[y * size + x] = color;
                }

            selectionRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "KingdomDemo_SelectionRing",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            selectionRingTexture.SetPixels32(pixels);
            selectionRingTexture.Apply();
            return selectionRingTexture;
        }
    }
}
