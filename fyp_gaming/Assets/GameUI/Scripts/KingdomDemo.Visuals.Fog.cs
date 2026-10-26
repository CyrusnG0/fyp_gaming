// KingdomDemo (partial) — world-space fog overlays and the selection/fog consistency pass.
//
// The overlay colour is calculated at runtime. The serialized fogPhotoTint is still the scene's
// starting point, but a white/opaque photo is dimmed and greyed here so unseen terrain stays legible
// while still reading as "outside our sight".

using System.IO;
using UnityEngine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        void InitializeFogOverlays(TBTK.Node[,] nodes, int width, int height)
        {
#if UNITY_EDITOR
            if (fogPhoto == null)
                fogPhoto = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameUI/Art/fog.png");
            if (fogPhoto == null)
            {
                string fogPath = Path.Combine(Application.dataPath, "GameUI/Art/fog.png");
                if (File.Exists(fogPath))
                {
                    fogPhoto = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    fogPhoto.name = "fog_runtime";
                    if (!fogPhoto.LoadImage(File.ReadAllBytes(fogPath), false))
                        fogPhoto = null;
                }
            }
#endif
            if (fogPhoto == null)
            {
                Debug.LogWarning("[KingdomDemo] Fog photo is not assigned; grid fog overlays are disabled.");
                return;
            }

            var root = new GameObject("FogOfWarOverlays");
            fogOverlayRoot = root.transform;
            var fogSprite = Sprite.Create(fogPhoto, new Rect(0f, 0f, fogPhoto.width, fogPhoto.height),
                                          new Vector2(0.5f, 0.5f), 100f);

            float nodeSize = TBTK.GridManager.GetNodeSize();
            float spriteSize = Mathf.Max(fogSprite.bounds.size.x, fogSprite.bounds.size.y);
            float overlayScale = spriteSize > 0f ? nodeSize * 1.08f / spriteSize : 1f;
            Color overlayColor = RuntimeFogColor();

            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var node = nodes[x, z];
                    if (node == null) continue;

                    var overlay = new GameObject("Fog_" + x + "_" + z);
                    overlay.transform.SetParent(fogOverlayRoot, true);
                    overlay.transform.position = node.GetPos() + Vector3.up * fogPhotoHeight;
                    overlay.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    overlay.transform.localScale = Vector3.one * overlayScale;

                    var renderer = overlay.AddComponent<SpriteRenderer>();
                    renderer.sprite = fogSprite;
                    renderer.color = overlayColor;
                    renderer.sortingOrder = 100;
                    renderer.enabled = false;
                    fogOverlays.Add(renderer);
                }

            Debug.Log("[KingdomDemo] Created " + fogOverlays.Count + " dimmed fog overlays from " + fogPhoto.name + ".");
        }

        /// <summary>
        /// White clouds used to cover whole provinces. Keep the photo's alpha and hue intent, but mix in
        /// a cool grey and halve opacity so terrain, city silhouettes and destination highlights remain
        /// readable through the fog.
        /// </summary>
        Color RuntimeFogColor()
        {
            Color color = Color.Lerp(fogPhotoTint, FogOverlayGrey, FogOverlayGreyMix);
            color.a = fogPhotoTint.a * FogOverlayAlpha;
            return color;
        }

        void RefreshFogOverlays()
        {
            if (fogOverlayRoot == null || engine == null || currentSeat == null) return;

            int index = 0;
            int width = engine.State.Map.Width;
            int height = engine.State.Map.Height;
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var node = TBTK.GridManager.GetNode(x, z);
                    if (node == null) continue;

                    fogOverlays[index].enabled = !engine.InSight(currentSeat, x, z);
                    index++;
                }
        }

        /// <summary>
        /// Fog on the map (AGENTS.md #3): foreign armies outside the seat's observation are hidden outright,
        /// watchtowers remain visible, a city marker outside it keeps only its existence (see <see cref="TintCity"/>),
        /// and a selection that drops out of sight is dropped with it.
        /// </summary>
        void ApplyFog()
        {
            foreach (var kv in armyObjs)
            {
                bool visible = obs != null && obs.FindArmy(kv.Key) != null;
                if (kv.Value != null && kv.Value.activeSelf != visible) kv.Value.SetActive(visible);
            }
            foreach (var kv in cityObjs) TintCity(kv.Key);      // owner colour only while the city is seen
            foreach (var kv in watchTowerObjs) TintWatchTower(kv.Key);
            if (selCity != null && (obs == null || obs.FindCity(selCity.Id) == null)) selCity = null;
            if (selArmy != null && (obs == null || obs.FindArmy(selArmy.Id) == null)) selArmy = null;
        }
    }
}
