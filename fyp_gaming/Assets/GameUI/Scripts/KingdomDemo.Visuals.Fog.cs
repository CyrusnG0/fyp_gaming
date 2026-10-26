// KingdomDemo (partial) — world-space fog shroud and the selection/fog consistency pass.
//
// V1 presentation change: the per-cell cloud photo was replaced by one cached, procedurally generated
// flat-hex sprite. The visible/hidden cell set is still exactly engine.InSight(currentSeat, x, z), so
// nothing about what the fog conceals changes. Only the drawing changed: a cool grey-blue uniform
// wash, with cells on the sight boundary a touch darker, and soft edges instead of a repeated pattern.

using UnityEngine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private Sprite fogShroudSprite;
        private Texture2D fogShroudTexture;

        void InitializeFogOverlays(TBTK.Node[,] nodes, int width, int height)
        {
            if (fogShroudSprite == null) fogShroudSprite = CreateFogShroudSprite();
            if (fogShroudSprite == null)
            {
                Debug.LogWarning("[KingdomDemo] Fog shroud sprite could not be created; fog overlays are disabled.");
                return;
            }

            var root = new GameObject("FogOfWarOverlays");
            fogOverlayRoot = root.transform;

            // TBTK hexes are flat-top and arranged in columns: a cell is GetNodeSize() wide and
            // spaceZHex * GetNodeSize() deep, with even columns offset by half a cell in Z. Matching
            // that footprint (plus a small overlap) tiles the shroud without a repeat pattern.
            float nodeSize = TBTK.GridManager.GetNodeSize();
            float cellWidth = nodeSize * FogShroudOverlap;
            float cellDepth = TBTK.GridGenerator.spaceZHex * nodeSize * FogShroudOverlap;
            Vector3 spriteSize = fogShroudSprite.bounds.size;
            float scaleX = spriteSize.x > 0f ? cellWidth / spriteSize.x : 1f;
            float scaleY = spriteSize.y > 0f ? cellDepth / spriteSize.y : 1f;

            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var node = nodes[x, z];
                    if (node == null) continue;

                    var overlay = new GameObject("Fog_" + x + "_" + z);
                    overlay.transform.SetParent(fogOverlayRoot, true);
                    overlay.transform.position = node.GetPos() + Vector3.up * fogPhotoHeight;
                    overlay.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    overlay.transform.localScale = new Vector3(scaleX, scaleY, 1f);

                    var renderer = overlay.AddComponent<SpriteRenderer>();
                    renderer.sprite = fogShroudSprite;
                    renderer.color = RuntimeFogColor();
                    renderer.sortingOrder = FogShroudSortingOrder;
                    renderer.enabled = false;
                    fogOverlays.Add(renderer);
                }

            Debug.Log("[KingdomDemo] Created " + fogOverlays.Count + " flat procedural fog cells.");
        }

        /// <summary>
        /// A flat, white alpha mask for one flat-top hex (pointy left/right, flat top/bottom). The
        /// interior is fully opaque (the renderer tint supplies the cool grey-blue and the requested
        /// ~0.50 alpha); only a few rim pixels fade. There is deliberately no noise or cloud pattern.
        /// </summary>
        Sprite CreateFogShroudSprite()
        {
            if (fogShroudTexture != null) return fogShroudSprite;

            int width = FogShroudTextureWidth;
            int height = FogShroudTextureHeight;
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float slant = Mathf.Sqrt(3f);                 // slope of the left/right hex edges
            float slantTerm = slant * halfW * 0.5f;       // == halfH for an exact hex
            var pixels = new Color32[width * height];
            var clear = new Color32(255, 255, 255, 0);
            var solid = new Color32(255, 255, 255, 255);

            for (int y = 0; y < height; y++)
            {
                float py = Mathf.Abs(y + 0.5f - halfH);
                for (int x = 0; x < width; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - halfW);
                    // Distance (in pixels) to the nearest edge. The flat top/bottom edge applies in
                    // the central half of the texture; the slanted edges close the side points.
                    float margin = px <= halfW * 0.5f
                        ? Mathf.Min(halfH - py, (halfH + slantTerm - slant * px - py) * 0.5f)
                        : (halfH + slantTerm - slant * px - py) * 0.5f;
                    float alpha = Mathf.Clamp01(margin / FogShroudSoftEdgePixels);
                    pixels[y * width + x] = alpha >= 0.999f ? solid
                        : new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            fogShroudTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "KingdomDemo_FogShroud",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            fogShroudTexture.SetPixels32(pixels);
            fogShroudTexture.Apply();
            return Sprite.Create(fogShroudTexture, new Rect(0f, 0f, width, height),
                                 new Vector2(0.5f, 0.5f), 100f);
        }

        Color RuntimeFogColor()
        {
            var color = FogShroudBaseColor;
            color.a = FogShroudBaseAlpha;
            return color;
        }

        Color RuntimeFogBoundaryColor()
        {
            var color = FogShroudBoundaryColor;
            color.a = FogShroudBoundaryAlpha;
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
                    if (index >= fogOverlays.Count) return;

                    bool hidden = !engine.InSight(currentSeat, x, z);
                    var overlay = fogOverlays[index++];
                    overlay.enabled = hidden;
                    if (!hidden) continue;

                    // Boundary presentation only: the same InSight rule decides every neighbour.
                    bool onSightEdge = false;
                    var neighbors = engine.State.Map.Neighbors;
                    int mapIndex = engine.State.Map.Index(x, z);
                    if (neighbors != null && mapIndex >= 0 && mapIndex < neighbors.Length)
                    {
                        var adjacent = neighbors[mapIndex];
                        if (adjacent != null)
                            foreach (int n in adjacent)
                            {
                                int nx = n % width;
                                int nz = n / width;
                                if (engine.InSight(currentSeat, nx, nz)) { onSightEdge = true; break; }
                            }
                    }
                    overlay.color = onSightEdge ? RuntimeFogBoundaryColor() : RuntimeFogColor();
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
