// KingdomDemo (partial) — map visuals: scene markers, owner tinting, army banners and fog overlays.

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        void AddCity(string objName, string id, string displayName, string ownerSeat)
        {
            var go = FindOrCreateMarker(objName, id == "city-wei" ? 2 : id == "city-shu" ? 9 : 6,
                                        id == "city-wei" ? 2 : id == "city-shu" ? 6 : 4, ownerSeat);
            if (go == null) throw new System.Exception("scene object missing: " + objName);
            var node = TBTK.GridManager.GetNode(go.transform.position, null);
            if (node == null) throw new System.Exception(objName + " is not on a grid node");

            var city = new CityState
            {
                Id = id,
                Name = displayName,
                X = node.idxX,
                Z = node.idxZ,
                OwnerSeatId = ownerSeat,
            };
            if (ownerSeat != null)
            {
                city.Population = BalanceConfig.StartPopulation;
                city.Food = BalanceConfig.StartFood;
                city.Gold = BalanceConfig.StartGold;
            }
            else
            {
                city.Population = 3000; city.Food = 200; city.Gold = 100;   // TBD: neutral city stats
            }
            engine.State.Cities.Add(city);
            cityObjs[id] = go;
            TintCity(id);
        }

        void AddWatchTower(string objName, string id, string displayName, int x, int z)
        {
            var node = TBTK.GridManager.GetNode(x, z);
            if (node == null) throw new System.Exception("watch tower is not on a grid node");

            var go = FindSceneObject(objName);
            if (go == null)
            {
                go = new GameObject(objName);
                go.transform.position = node.GetPos();
                var renderer = go.AddComponent<SpriteRenderer>();
                if (watchTowerImage != null)
                    renderer.sprite = Sprite.Create(watchTowerImage,
                        new Rect(0f, 0f, watchTowerImage.width, watchTowerImage.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            else
            {
                var sceneNode = TBTK.GridManager.GetNode(go.transform.position, null);
                if (sceneNode == null) throw new System.Exception(objName + " is not on a grid node");
                node = sceneNode;
                go.transform.position = node.GetPos();
            }

            Transform gridTerms = null;
            foreach (var cityObject in cityObjs.Values)
            {
                if (cityObject == null) continue;
                gridTerms = cityObject.transform.parent;
                break;
            }
            go.transform.SetParent(gridTerms, true);

            var watchTowerRenderer = go.GetComponent<SpriteRenderer>();
            if (watchTowerRenderer == null) watchTowerRenderer = go.AddComponent<SpriteRenderer>();
            watchTowerRenderer.sortingOrder = 500;

            go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            go.transform.localEulerAngles = new Vector3(45f, 0f, 0f);
            var towerPosition = go.transform.position;
            towerPosition.y = 0.55f;
            go.transform.position = towerPosition;

            engine.State.WatchTowers.Add(new WatchTowerState
            {
                Id = id,
                Name = displayName,
                X = node.idxX,
                Z = node.idxZ,
                OwnerSeatId = null,
            });
            watchTowerObjs[id] = go;
            TintWatchTower(id);
        }

        void AddArmy(string objName, string id, string displayName, string ownerSeat)
        {
            var go = FindOrCreateMarker(objName, id == "army-wei-1" ? 2 : 9, id == "army-wei-1" ? 3 : 5, ownerSeat);
            if (go == null) throw new System.Exception("scene object missing: " + objName);
            if (!go.activeInHierarchy)
            {
                Debug.Log("[KingdomDemo] skipping disabled army: " + objName);
                return;
            }
            var node = TBTK.GridManager.GetNode(go.transform.position, null);
            if (node == null) throw new System.Exception(objName + " is not on a grid node");
            go.transform.position = node.GetPos();

            engine.State.Armies.Add(new ArmyState
            {
                Id = id,
                Name = displayName,
                OwnerSeatId = ownerSeat,
                Troops = BalanceConfig.StartFieldArmyTroops,
                X = node.idxX,
                Z = node.idxZ,
            });
            armyObjs[id] = go;
            TintArmy(id);
        }

        /// <summary>
        /// The scene object if the scene has one, otherwise a runtime marker: a flat, faction-coloured
        /// disc. The smoke test found plain grey cylinders where the scene has no object yet (all of Shu
        /// today), which read as debris — the marker now carries its owner's colour from the start.
        /// </summary>
        GameObject FindOrCreateMarker(string objName, int x, int z, string ownerSeat)
        {
            var go = FindSceneObject(objName);
            if (go != null) return go;

            var node = TBTK.GridManager.GetNode(x, z);
            if (node == null)
                for (int ix = 0; ix < TBTK.GridManager.DimensionX() && node == null; ix++)
                    for (int iz = 0; iz < TBTK.GridManager.DimensionZ() && node == null; iz++)
                        node = TBTK.GridManager.GetNode(ix, iz);
            if (node == null) return null;

            go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = objName;
            go.transform.position = node.GetPos();
            go.transform.localScale = FallbackMarkerScale;

            var marker = go.GetComponentInChildren<MeshRenderer>();
            if (marker != null)
            {
                var material = MarkerMaterial(OwnerColor(ownerSeat));
                if (material != null) marker.sharedMaterial = material;
            }
            return go;
        }

        GameObject FindSceneObject(string objName)
        {
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go.name == objName) return go;
            return null;
        }

        /// <summary>Faction colour, neutral grey for unowned ground (ADR-008 palette).</summary>
        Color OwnerColor(string seatId)
            => seatId != null && factionColors.TryGetValue(seatId, out var color) ? color : NeutralColor;

        /// <summary>
        /// One runtime material per colour, cached, so a fallback marker shows its owner's colour.
        /// Tries URP first (this project renders URP), then built-in unlit — no material assets added.
        /// </summary>
        Material MarkerMaterial(Color color)
        {
            if (markerMaterials.TryGetValue(color, out var cached)) return cached;

            Material material = null;
            foreach (var shaderName in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default" })
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) continue;
                material = new Material(shader);
                break;
            }
            if (material == null) return null;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);   // URP/Unlit
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);           // built-in
            markerMaterials[color] = material;
            return material;
        }

        /// <summary>
        /// A city marker is tinted with its owner's colour only while the current seat can actually see the
        /// city (its own, or one inside the fog radius). A city outside the observation keeps its marker —
        /// the map still says "a city stands here" — but is painted in the neutral colour, because the
        /// owner's banner is exactly the information the fog is supposed to withhold (hotseat privacy).
        /// </summary>
        void TintCity(string cityId)
        {
            var city = engine.State.FindCity(cityId);
            if (city == null) return;
            bool known = city.OwnerSeatId == currentSeat || (obs != null && obs.FindCity(cityId) != null);
            var color = OwnerColor(known ? city.OwnerSeatId : null);

            var go = cityObjs[cityId];
            var sprite = go.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) { sprite.color = color; return; }

            // the runtime fallback marker is a mesh, not a sprite
            var marker = go.GetComponentInChildren<MeshRenderer>();
            if (marker == null) return;
            var material = MarkerMaterial(color);
            if (material != null) marker.sharedMaterial = material;
        }

        void TintWatchTower(string towerId)
        {
            var tower = engine.State.WatchTowers.Find(t => t.Id == towerId);
            if (tower == null || !watchTowerObjs.TryGetValue(towerId, out var go) || go == null) return;

            bool known = tower.OwnerSeatId == currentSeat || (obs != null && obs.FindWatchTower(towerId) != null);
            var color = OwnerColor(known ? tower.OwnerSeatId : null);
            var sprite = go.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) sprite.color = color;
        }

        void TintArmy(string armyId)
        {
            var army = engine.State.FindArmy(armyId);
            if (army == null || !armyObjs.TryGetValue(armyId, out var go) || go == null) return;

            var color = OwnerColor(army.OwnerSeatId);
            foreach (var sprite in go.GetComponentsInChildren<SpriteRenderer>())
                sprite.color = color;

            var marker = go.GetComponentInChildren<MeshRenderer>();
            if (marker != null)
            {
                var material = MarkerMaterial(color);
                if (material != null) marker.sharedMaterial = material;
            }
        }

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
                    renderer.color = fogPhotoTint;
                    renderer.sortingOrder = 100;
                    renderer.enabled = false;
                    fogOverlays.Add(renderer);
                }

                    Debug.Log("[KingdomDemo] Created " + fogOverlays.Count + " fog overlays from " + fogPhoto.name + ".");
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

                        bool hidden = !engine.InSight(currentSeat, x, z);
                        fogOverlays[index].enabled = hidden;
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

        void SyncArmyVisual(string armyId)
        {
            if (!armyObjs.TryGetValue(armyId, out var go) || go == null) return;
            var army = engine.State.FindArmy(armyId);
            if (army == null) return;                                  // destroyed in battle
            var node = TBTK.GridManager.GetNode(army.X, army.Z);
            if (node == null) return;

            var unit = go.GetComponent<TBTK.Unit>();
            if (unit != null && unit.node != node)
            {
                if (unit.node != null && unit.node.unit == unit) unit.node.unit = null;
                if (node.unit != null && node.unit != unit) node.unit.node = null;
                unit.node = node;
                node.unit = unit;
            }

            go.transform.position = node.GetPos();
            if (go.name.StartsWith("ArmyBanner_", System.StringComparison.Ordinal))
            {
                var visualPosition = go.transform.position;
                visualPosition.y = 0.5f;
                go.transform.position = visualPosition;
                armyBannerBases[armyId] = visualPosition;   // base for the idle bob below
            }
            foreach (var city in engine.State.Cities) TintCity(city.Id);   // capture may have changed owners
        }

        void EnsureArmyVisual(ArmyState army)
        {
            if (army == null) return;
            if (!armyObjs.ContainsKey(army.Id))
                armyObjs[army.Id] = CreateArmyBannerMarker(army);
            SyncArmyVisual(army.Id);
        }

        GameObject CreateArmyBannerMarker(ArmyState army)
        {
            Texture2D bannerTexture = UnitBannerTexture(army.UnitType);
            if (bannerTexture == null)
            {
                if (armyBannerImage == null)
                {
#if UNITY_EDITOR
                    armyBannerImage = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Assets/GameUI/Art/Prop_ArmyBanner.png");
#endif
                    if (armyBannerImage == null)
                    {
                        string bannerPath = Path.Combine(Application.dataPath, "GameUI/Art/Prop_ArmyBanner.png");
                        if (File.Exists(bannerPath))
                        {
                            armyBannerImage = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (!armyBannerImage.LoadImage(File.ReadAllBytes(bannerPath), false))
                                armyBannerImage = null;
                        }
                    }
                }
                bannerTexture = armyBannerImage;
            }

            if (bannerTexture == null)
                return FindOrCreateMarker("ArmyMarker_" + army.Id, army.X, army.Z, army.OwnerSeatId);

            var node = TBTK.GridManager.GetNode(army.X, army.Z);
            if (node == null) return null;

            string sourceName = army.OwnerSeatId == SeatA ? weiArmyObj : shuArmyObj;
            var source = FindSceneObject(sourceName);
            var sourceUnit = source != null ? source.GetComponent<TBTK.Unit>() : null;
            if (source == null || sourceUnit == null)
                return FindOrCreateMarker("ArmyMarker_" + army.Id, army.X, army.Z, army.OwnerSeatId);

            var spawnPosition = node.GetPos();
            spawnPosition.y = 0.5f;
            var go = Instantiate(source, spawnPosition, Quaternion.Euler(45f, 0f, 0f));
            go.name = "ArmyBanner_" + army.Id;
            foreach (var sourceRenderer in go.GetComponentsInChildren<Renderer>())
                sourceRenderer.enabled = false;

            var unit = go.GetComponent<TBTK.Unit>();
            unit.node = node;
            node.unit = unit;
            foreach (var faction in TBTK.UnitManager.GetFactionList())
                if (faction.factionID == unit.facID && !faction.unitList.Contains(unit))
                {
                    faction.unitList.Add(unit);
                    break;
                }

            go.transform.localEulerAngles = new Vector3(45f, 0f, 0f);
            go.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(bannerTexture,
                new Rect(0f, 0f, bannerTexture.width, bannerTexture.height),
                new Vector2(0.5f, 0.5f), 100f);
            renderer.color = OwnerColor(army.OwnerSeatId);
            renderer.sortingOrder = convertedArmySortingOrder;
            return go;
        }

        /// <summary>After a battle: hide banners of destroyed armies, move the survivors, recolour
        /// captured cities, drop a selection that no longer exists.</summary>
        void SyncBattleVisuals()
        {
            foreach (var kv in armyObjs)
                if (kv.Value != null && kv.Value.activeSelf && engine.State.FindArmy(kv.Key) == null)
                    kv.Value.SetActive(false);
            foreach (var army in engine.State.Armies) SyncArmyVisual(army.Id);
            if (selArmy != null && engine.State.FindArmy(selArmy.Id) == null) selArmy = null;
            if (selCity != null && engine.State.FindCity(selCity.Id) == null) selCity = null;
        }

        // ---- army banner art & idle bob ---------------------------------------

        // 兵種 banner art (ADR-019), loaded once from Assets/GameUI/Art. Loading by path keeps a fresh
        // clone working with no scene reference or manual import step, exactly like the fog photo.
        private readonly Dictionary<string, Texture2D> unitBannerImages = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Vector3> armyBannerBases = new Dictionary<string, Vector3>();

        private const float BannerBobAmplitude = 0.04f;   // world units; the banner is 0.7 units wide
        private const float BannerBobSpeed = 1.7f;        // radians / second

        /// <summary>
        /// The texture for one unit type, or null when that PNG is missing (the caller then falls back to
        /// the shared faction banner). A missing file is retried on the next army rather than cached as null.
        /// </summary>
        Texture2D UnitBannerTexture(string unitType)
        {
            string type = UnitTypes.Normalize(unitType);
            if (unitBannerImages.TryGetValue(type, out var cached)) return cached;

            string fileName = "banner_" + type + ".png";
            Texture2D texture = null;
#if UNITY_EDITOR
            texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameUI/Art/" + fileName);
#endif
            if (texture == null)
            {
                string path = Path.Combine(Application.dataPath, "GameUI/Art/" + fileName);
                if (File.Exists(path))
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.name = "banner_" + type + "_runtime";
                    if (!texture.LoadImage(File.ReadAllBytes(path), false)) texture = null;
                }
            }

            if (texture != null) unitBannerImages[type] = texture;
            return texture;
        }

        /// <summary>
        /// Idle bob: every army banner drifts a few centimetres up and down, each at its own phase, so a
        /// stack of banners on neighbouring hexes does not pulse in lockstep. The base position is written
        /// by <see cref="SyncArmyVisual"/>; this only adds the offset, so marches still land exactly on a hex.
        /// Called from the single per-frame Update in KingdomDemo.Input.cs (a partial class can only have one).
        /// </summary>
        void BobArmyBanners()
        {
            if (armyBannerBases.Count == 0) return;
            foreach (var kv in armyBannerBases)
            {
                if (!armyObjs.TryGetValue(kv.Key, out var go) || go == null) continue;
                float phase = BannerPhase(kv.Key);
                go.transform.position = kv.Value
                    + Vector3.up * (Mathf.Sin(Time.time * BannerBobSpeed + phase) * BannerBobAmplitude);
            }
        }

        /// <summary>
        /// Stable per-army phase in [0, 2π). string.GetHashCode is randomised per process on .NET Core,
        /// so it would make the bob pattern differ between runs; a deterministic char hash does not.
        /// </summary>
        static float BannerPhase(string armyId)
        {
            int hash = 17;
            foreach (char c in armyId) hash = unchecked(hash * 31 + c);
            return (hash & 0xFFFF) / 65536f * (Mathf.PI * 2f);
        }
    }
}
