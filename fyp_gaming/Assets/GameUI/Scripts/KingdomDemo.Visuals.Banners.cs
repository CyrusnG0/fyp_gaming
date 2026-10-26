// KingdomDemo (partial) — army banner visuals: spawn, scale, stagger and idle bob.
//
// Presentation-only: this reads the same ArmyState the rest of the map visuals use. Staggering uses only
// armies visible to the display seat's Observation; it never changes a unit's logical hex.

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
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

            go.transform.position = node.GetPos() + ArmyBannerStaggerOffset(army);
            if (go.name.StartsWith("ArmyBanner_", System.StringComparison.Ordinal))
            {
                var visualPosition = go.transform.position;
                visualPosition.y = 0.5f;
                go.transform.position = visualPosition;
                // One shot-wide scale for every runtime banner (all seats), so banners read at a
                // consistent size next to the city sprites. Scene-authored armies keep their art/scale.
                go.transform.localScale = Vector3.one * ArmyBannerWorldScale;
                armyBannerBases[armyId] = visualPosition;   // base for the idle bob below
            }
            foreach (var city in engine.State.Cities) TintCity(city.Id);   // capture may have changed owners
        }

        /// <summary>
        /// Fans out nearby own banners so four armies on neighbouring hexes do not project as one
        /// vertical column. It only consults the display seat's own Observation armies; the logical
        /// army coordinates, node occupancy and click targets are untouched.
        /// </summary>
        Vector3 ArmyBannerStaggerOffset(ArmyState army)
        {
            if (obs == null || string.IsNullOrEmpty(currentSeat) || army.OwnerSeatId != currentSeat)
                return Vector3.zero;

            var own = obs.ArmiesOf(currentSeat);
            if (own.Count < 2) return Vector3.zero;
            var group = new List<ArmyView>();
            foreach (var other in own)
                if (engine.State.Map.HexDistance(army.X, army.Z, other.X, other.Z,
                                                ArmyBannerStaggerHexSteps) >= 0)
                    group.Add(other);
            if (group.Count < 2) return Vector3.zero;

            group.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            int index = group.FindIndex(a => a.Id == army.Id);
            if (index < 0) return Vector3.zero;

            float steps = Mathf.Clamp(index - (group.Count - 1) * 0.5f,
                                      -ArmyBannerStaggerMaxSteps, ArmyBannerStaggerMaxSteps);
            return new Vector3(steps * ArmyBannerStaggerSpacing, 0f, 0f);
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
            go.transform.localScale = Vector3.one * ArmyBannerWorldScale;

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
