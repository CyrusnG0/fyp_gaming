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

    }
}
