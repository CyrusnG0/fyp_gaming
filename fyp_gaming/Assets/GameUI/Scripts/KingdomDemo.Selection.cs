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

            if (GUI.Button(new Rect(x + 12f, y + 42f, width - 24f, 28f), city.Name, btn))
            {
                locationChoiceOpen = false;
                selCity = city;
                selArmy = null;
            }
            if (GUI.Button(new Rect(x + 12f, y + 76f, width - 24f, 28f),
                $"{army.Name} ({UnitTypeName(army.UnitType)})", btn))
            {
                locationChoiceOpen = false;
                selArmy = army;
                selCity = null;
                TBTK.GridIndicator.SetSelect(node);
                ShowArmyMoveTargets(army);
                statusMsg = "Selected " + army.Name + " (" + UnitTypeName(army.UnitType) + ") — choose a destination";
            }
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
                return Color.Lerp(color, Color.white, 0.18f);
            return AccentGold;
        }

        Texture2D SelectionRingTexture()
        {
            if (selectionRingTexture != null) return selectionRingTexture;

            const int size = SelectionRingTextureSize;
            const float centre = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            var dark = new Color32(14, 10, 6, 210);
            var white = new Color32(255, 255, 255, 255);
            var innerGlow = new Color32(255, 255, 255, 70);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 color = clear;
                    if (radius >= 59f && radius <= 63f) color = dark;          // ink outline
                    else if (radius >= 55f && radius < 59f) color = white;    // faction-tinted ring
                    else if (radius >= 50f && radius < 55f) color = innerGlow; // soft inner echo
                    else if (radius < 59f) color = new Color32(14, 10, 6, 24); // faint underlay
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
