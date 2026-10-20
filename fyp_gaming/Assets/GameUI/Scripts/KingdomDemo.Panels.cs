// KingdomDemo (partial) — the IMGUI shell and map panels (city/army/popup), plus shared button helpers.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------------ UI

        void OnGUI()
        {
            uiRects.Clear();
            float s = Screen.width / 1600f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            var body = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            var mono = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Normal };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16 };

            if (!ready)
            {
                GUI.Label(new Rect(20, 20, 900, 40), "KingdomDemo init failed: " + initError, title);
                return;
            }

            // Between human seats the whole screen is replaced by the handover blocker: the previous
            // player must not see the next player's map, panels or report (hotseat rule).
            if (handoverShowing)
            {
                DrawHandoverOverlay(title, body);
                return;
            }

            if (engine == null || obs == null || string.IsNullOrEmpty(currentSeat))
            {
                GUI.Label(new Rect(20, 20, 1100, 40),
                    "KingdomDemo is waiting for game state to finish initializing.", title);
                return;
            }

            var fac = engine.State.FindFaction(currentSeat);
            if (fac == null)
            {
                GUI.Label(new Rect(20, 20, 1100, 40),
                    "KingdomDemo cannot find the current seat: " + currentSeat, title);
                return;
            }
            int food = 0, gold = 0, pop = 0;
            // render from the seat's observation, never from canonical state
            foreach (var c in obs.CitiesOf(currentSeat)) { food += c.Food; gold += c.Gold; pop += c.Population; }

            // 一統天下 (design §13): the game is decided — the land is one, there is nothing left to order.
            if (engine.State.WinnerSeatId != null)
            {
                DrawVictoryBanner(title, body);
                return;
            }

            bool hotseat = SeatRotation.HumanSeats(engine.State).Count > 1;
            // Top bar: the header is data-driven (seat ids, larger populations), so it gets the full width
            // left of the tab / End-turn column (x = 1400) instead of a fixed 1000 px. wordWrap is off — a
            // long header must not wrap into a clipped second line — and the font steps down if that width
            // is still not enough, so the trailing values (Food/Gold) always stay on screen.
            string header =
                $"Season {engine.State.Season} ({BalanceConfig.SeasonName(engine.State.Season)}) · {fac.Name} ({(hotseat ? currentSeat + " · Player" : "Player")})" +
                $" · Orders {fac.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}" +
                $" · Prestige {fac.Prestige} · Population {pop} · Food {food} · Gold {gold}";
            const float headerWidth = 1356f;
            var headerStyle = new GUIStyle(title) { wordWrap = false };
            foreach (int size in new[] { 20, 18, 16, 14 })
            {
                headerStyle.fontSize = size;
                if (headerStyle.CalcSize(new GUIContent(header)).x <= headerWidth) break;
            }

            var top = new Rect(10, 8, 1380, 34);
            uiRects.Add(top);
            DrawBackdrop(top);
            GUI.Label(new Rect(22, 12, headerWidth, 28), header, headerStyle);

            // tab bar — Map is the live view; the others preview the full design concept
            var tabBtn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            for (int i = 0; i < Tabs.GetLength(0); i++)
            {
                var r = new Rect(1400, 60 + i * 46, 190, 40);
                uiRects.Add(r);
                var prevBg = GUI.backgroundColor;
                if (activeTab == Tabs[i, 0]) GUI.backgroundColor = new Color(0.60f, 0.78f, 1f);
                if (GUI.Button(r, Tabs[i, 1], tabBtn)) ShowTab(Tabs[i, 0]);
                GUI.backgroundColor = prevBg;
            }

            // selection panel (map view only)
            if (activeTab == "map")
            {
                if (selCity != null) DrawCityPanel(selCity, body, btn);
                else if (selArmy != null) DrawArmyPanel(selArmy, body, btn);
            }
            DrawLocationChoice(body, btn);

            // end this seat's turn (single-human mode ends the season, as before)
            var end = new Rect(1400, 8, 190, 44);
            uiRects.Add(end);
            if (GUI.Button(end, hotseat ? $"End {fac.Name}'s turn" : "End season", btn))
            {
                locationChoiceOpen = false;
                EndTurn();
            }
            if (hotseat)
                GUI.Label(new Rect(1400, 56, 190, 22), $"Turn: {fac.Name}", mono);

            // status + JSON command feed (the FYP artifact: UI clicks == LLM JSON)
            var feed = new Rect(10, 800, 1100, 92);
            uiRects.Add(feed);
            DrawBackdrop(feed);
            GUI.Label(new Rect(20, 804, 1080, 22), statusMsg, body);
            for (int i = 0; i < jsonFeed.Count; i++)
                GUI.Label(new Rect(20, 826 + i * 16, 1080, 16), jsonFeed[i], mono);

            if (activeTab != "map" && !reportShowing) DrawTabPanel(body, btn, title, mono);
            if (reportShowing) DrawReport(body, btn, title);

            HandleClicks();
        }

        void DrawLocationChoice(GUIStyle body, GUIStyle btn)
        {
            if (!locationChoiceOpen || activeTab != "map") return;

            var node = TBTK.GridManager.GetNode(locationChoiceX, locationChoiceZ);
            // drawn from the observation, like everything else: a hex whose city or army this seat cannot
            // see has nothing to offer (the click handler only opens this for a city it may inspect)
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
            x = Mathf.Clamp(x, 8f, 1600f - width - 8f);
            y = Mathf.Clamp(y, 50f, 900f - height - 8f);

            locationChoiceRect = new Rect(x, y, width, height);
            uiRects.Add(locationChoiceRect);
            DrawBackdrop(locationChoiceRect);
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 22f), "Choose a target to inspect", body);

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

        /// <summary>
        /// Opaque plate behind a panel or modal. This skin's GUI.Box renders see-through, so map sprites
        /// bled through and collided with panel text (smoke-test finding): plate the rect in solid
        /// charcoal first and keep the box for its border. Every modal and side panel goes through here.
        /// </summary>
        void DrawBackdrop(Rect r)
        {
            var prev = GUI.color;
            GUI.color = PanelColor;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(r, GUIContent.none);
        }

        /// <summary>
        /// City details for the current seat. An own city shows everything and offers orders; a foreign or
        /// neutral city shows **only what this seat actually holds** — the observation when the city is in
        /// sight, otherwise a live 偵察 report (bands or exact numbers, P8), otherwise nothing at all. It
        /// never falls back to canonical state, so the panel cannot leak an unseen city's garrison.
        /// </summary>
        void DrawCityPanel(CityState city, GUIStyle body, GUIStyle btn)
        {
            bool mine = city.OwnerSeatId == currentSeat;
            bool conversionOpen = mine && garrisonConversionOpen && garrisonConversionCityId == city.Id && city.Garrison > 0;
            var inSight = obs != null ? obs.FindCity(city.Id) : null;
            var report = obs != null ? obs.FindScoutedCity(city.Id) : null;
            var p = new Rect(10, 50, 420, mine ? (conversionOpen ? 520 : 480) : (inSight != null || report != null ? 230 : 120));
            uiRects.Add(p);
            DrawBackdrop(p);
            // the name and owner are themselves information: an unseen city with no report stays unnamed,
            // even if a debug hook selects it by id
            bool knowsCity = mine || inSight != null || report != null;
            GUI.Label(new Rect(22, 58, 400, 24),
                knowsCity ? $"{city.Name} ({OwnerName(city)})" : "Unknown city", body);

            if (mine)
            {
                string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? " (yield halved)" : "";
                GUI.Label(new Rect(22, 86, 400, 116),
                    $"Population {city.Population}\nFood {city.Food}\nGold {city.Gold}\nGarrison {city.Garrison} {UnitTypeName(city.GarrisonType)}\n" +
                    $"Morale {city.Morale}{moraleWarning}\n" +
                    $"Reclaimed {city.ReclaimPct}%  Defense {city.Defense}/{BalanceConfig.DefenseMax}  Training {city.Training}/{MyTrainingCap()}",
                    body);

                float buttonLeft = p.x + 12f;
                float buttonTop = p.y + 160f;
                float buttonGap = 12f;
                float buttonWidth = (p.width - 24f - buttonGap) * 0.5f;
                float rightButtonLeft = buttonLeft + buttonWidth + buttonGap;
                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop, buttonWidth, 34), ActionType.LevyTax, city.Id, "Tax", null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop, buttonWidth, 34), ActionType.LightenLabor, city.Id, "Lighten labor",
                    city.Gold >= BalanceConfig.LightenLaborGoldCost ? null : "Not enough gold", 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 38f, buttonWidth, 34), ActionType.Reclaim, city.Id, "Reclaim",
                    city.ReclaimPct >= BalanceConfig.ReclaimPctCap ? "Limit reached"
                    : city.ReclaimedThisSeason ? "Already reclaimed this season" : null, 0, 0, btn));
                string recruitType = UnitTypes.Normalize(recruitUnitType);
                int recruitRequired = UnitTypes.RequiredMilitaryLevel(recruitType);
                int recruitCost = UnitTypes.RecruitGoldCost(recruitType, BalanceConfig.RecruitTroopGain);
                string recruitBlocked =
                    MilitaryLevel() < recruitRequired ? $"needs Military {recruitRequired}"
                    : city.Garrison > 0 && UnitTypes.Normalize(city.GarrisonType) != recruitType
                        ? $"garrison is {UnitTypeName(city.GarrisonType)}"
                    : city.Gold < recruitCost ? $"needs {recruitCost} gold"
                    : city.Population < BalanceConfig.RecruitPopCost ? "Not enough population" : null;
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop + 38f, buttonWidth, 34), ActionType.Recruit, city.Id, "Recruit",
                    recruitBlocked, 0, 0, btn,
                    new Dictionary<string, string> { { "unit_type", recruitType } }));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 76f, buttonWidth, 34), ActionType.Train, city.Id, "Train",
                    city.Training >= MyTrainingCap() ? "Limit reached" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(rightButtonLeft, buttonTop + 76f, buttonWidth, 34), ActionType.Fortify, city.Id, "Fortify",
                    city.Defense >= BalanceConfig.DefenseMax ? "Limit reached" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + 114f, buttonWidth, 34), ActionType.Relief, city.Id, "Relief",
                    city.Food < BalanceConfig.ReliefFoodCost ? "Not enough food" : null, 0, 0, btn));
                var convertRect = new Rect(rightButtonLeft, buttonTop + 114f, buttonWidth, 34);
                var armyOnCity = engine.State.ArmyAt(city.X, city.Z);
                bool convertTypeMismatch = armyOnCity != null && armyOnCity.OwnerSeatId == currentSeat
                    && UnitTypes.Normalize(armyOnCity.UnitType) != UnitTypes.Normalize(city.GarrisonType);
                var previousEnabled = GUI.enabled;
                GUI.enabled = city.Garrison > 0 && !convertTypeMismatch;
                if (GUI.Button(convertRect, "Convert garrison " + OrderCostSuffix(ActionType.ConvertGarrison), btn))
                {
                    garrisonConversionOpen = true;
                    garrisonConversionCityId = city.Id;
                    garrisonConversionAmount = city.Garrison;
                }
                GUI.enabled = previousEnabled;
                if (city.Garrison <= 0) hint = FirstReason(hint, "Convert garrison: No garrison");
                else if (convertTypeMismatch)
                    hint = FirstReason(hint,
                        $"Convert garrison: unit types differ (army is {UnitTypeName(armyOnCity.UnitType)})");

                // 兵種 picker: four toggles, research-gated types greyed with their reason (ADR-019)
                float typeLabelTop = 364f;
                GUI.Label(new Rect(22, typeLabelTop, 400, 20),
                    $"Recruit type: {UnitTypeName(recruitType)} · {recruitCost} gold, {BalanceConfig.RecruitPopCost} pop", body);
                string gateHint = null;
                int military = MilitaryLevel();
                for (int i = 0; i < UnitTypes.All.Length; i++)
                {
                    string type = UnitTypes.All[i];
                    int required = UnitTypes.RequiredMilitaryLevel(type);
                    bool gated = military < required;
                    var typeRect = new Rect(22 + i * 100f, typeLabelTop + 22f, 94f, 26f);
                    var prevBg = GUI.backgroundColor;
                    if (recruitType == type) GUI.backgroundColor = new Color(0.60f, 0.78f, 1f);
                    var prevEnabledFlag = GUI.enabled;
                    GUI.enabled = !gated;
                    if (GUI.Button(typeRect, UnitTypeName(type), btn)) recruitUnitType = type;
                    GUI.enabled = prevEnabledFlag;
                    GUI.backgroundColor = prevBg;
                    if (gated)
                        gateHint = gateHint == null
                            ? $"{UnitTypeName(type)} needs Military {required}"
                            : gateHint + " · " + $"{UnitTypeName(type)} needs Military {required}";
                }
                if (gateHint != null) GUI.Label(new Rect(22, typeLabelTop + 50f, 400, 20), gateHint, body);

                if (conversionOpen)
                {
                    GUI.Label(new Rect(22, 440, 400, 22),
                        $"Convert {garrisonConversionAmount} of {city.Garrison} {UnitTypeName(city.GarrisonType)} garrison to army", body);
                    garrisonConversionAmount = Mathf.Clamp(garrisonConversionAmount, 1, city.Garrison);
                    garrisonConversionAmount = Mathf.RoundToInt(GUI.HorizontalSlider(
                        new Rect(22, 466, 290, 24), garrisonConversionAmount, 1, city.Garrison));
                    if (GUI.Button(new Rect(320, 460, 90, 34), "Confirm", btn))
                        SubmitHuman(new ActionCommand
                        {
                            Type = ActionType.ConvertGarrison,
                            TargetId = city.Id,
                            ParamA = garrisonConversionAmount,
                            Reason = "UI: convert_garrison",
                        });
                }

                if (hint != null) GUI.Label(new Rect(22, conversionOpen ? 506 : 440, 400, 40), hint, body);
            }
            else
            {
                if (inSight != null)
                {
                    string moraleWarning = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold ? " (yield halved)" : "";
                    GUI.Label(new Rect(22, 84, 400, 126),
                        $"{city.Name} — in sight\n" +
                        $"Garrison {city.Garrison} {UnitTypeName(city.GarrisonType)}\nMorale {city.Morale}{moraleWarning}\n" +
                        $"Food {city.Food}  Gold {city.Gold}\n" +
                        $"Population {city.Population}\n" +
                        $"Defense {city.Defense}/{BalanceConfig.DefenseMax}  Training {city.Training}",
                        body);
                }
                else if (report != null)
                {
                    string detail = report.Accuracy == "exact"
                        ? $"Garrison {report.Garrison}  Food {report.Food}\nMorale {report.Morale}  Gold {report.Gold}\nPopulation {report.Population}"
                        : $"Troops: {report.GarrisonBand}\nFood: {report.FoodBand}";
                    GUI.Label(new Rect(22, 84, 400, 126),
                        $"{(city.IsNeutral ? "Neutral city" : "Enemy city")} — scout report ({report.Accuracy})\n" +
                        $"{detail}\nReport valid to season {report.UntilSeason}",
                        body);
                }
                else
                {
                    GUI.Label(new Rect(22, 84, 400, 30), "Outside our sight — nothing is known", body);
                }
            }
        }

        /// <summary>
        /// English display name for a 兵種. The engine's <see cref="UnitTypes.Label"/> keeps the Chinese
        /// design names (engine-side vocabulary); the demo UI is English, so every screen-facing use goes
        /// through this helper instead.
        /// </summary>
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

        /// <summary>
        /// One order button. It always submits a real ActionCommand through SubmitHuman and shows the
        /// action's order cost from BalanceConfig; the engine stays the only authority, so a stale hint
        /// just means the click comes back rejected with a reason.
        /// </summary>
        string OrderButton(Rect r, string type, string targetId, string label, string blockedReason,
            int paramA, int paramB, GUIStyle btn,
            Dictionary<string, string> args = null, string text = null, System.Action onSubmitted = null)
        {
            bool available = blockedReason == null;
            string buttonText = $"{label} {OrderCostSuffix(type)}";
            r.width = Mathf.Max(r.width, btn.CalcSize(new GUIContent(buttonText)).x + 16f);
            var prev = GUI.enabled;
            GUI.enabled = available;
            if (GUI.Button(r, buttonText, btn) && SubmitHuman(new ActionCommand
                {
                    Type = type, TargetId = targetId, ParamA = paramA, ParamB = paramB,
                    Args = args, Text = text, Reason = "UI: " + type,
                }))
                onSubmitted?.Invoke();
            GUI.enabled = prev;
            return available ? null : label + ": " + blockedReason;
        }

        static string FirstReason(string current, string candidate) => current ?? candidate;

        int MilitaryLevel() => obs != null ? obs.ResearchLevel(ResearchLine.Military) : 0;

        void DrawArmyPanel(ArmyState army, GUIStyle body, GUIStyle btn)
        {
            var enemyArmies = AttackableEnemyArmies(army);
            var enemyCities = AdjacentEnemyCities(army);
            int orders = enemyArmies.Count + enemyCities.Count;
            int attackRange = UnitTypes.AttackRange(army.UnitType);
            var occupiedCity = engine.State.CityAt(army.X, army.Z);
            bool canGarrison = occupiedCity != null && occupiedCity.OwnerSeatId == army.OwnerSeatId;
            if (canGarrison && garrisonArmyId != army.Id)
            {
                garrisonArmyId = army.Id;
                garrisonArmyAmount = army.Troops;
                garrisonArmyConversionOpen = false;
            }
            if (canGarrison) garrisonArmyAmount = Mathf.Clamp(garrisonArmyAmount, 1, army.Troops);

            const float panelWidth = 440f;
            float garrisonControlsHeight = garrisonArmyConversionOpen ? 90f : 52f;
            var p = new Rect(10, 50, panelWidth, (orders > 0 ? 236 + orders * 38 : 150) + (canGarrison ? garrisonControlsHeight : 0));
            uiRects.Add(p);
            DrawBackdrop(p);
            GUI.Label(new Rect(22, 58, panelWidth - 32f, 24),
                $"{army.Name} · {UnitTypeName(army.UnitType)} · Troops {army.Troops}", body);
            int foodPerSeason = (int)(army.Troops * UnitTypes.FoodPerTroop(army.UnitType));
            int factionFood = 0;
            foreach (var city in engine.State.Cities)
                if (city.OwnerSeatId == army.OwnerSeatId) factionFood += city.Food;
            GUI.Label(new Rect(22, 86, panelWidth - 32f, 78),
                $"Position ({army.X}, {army.Z})\nMorale {army.Morale}\n" +
                $"Faction food {factionFood} (army need {foodPerSeason} / season)\n" +
                (army.MarchCountThisSeason >= UnitTypes.MarchesPerSeason(army.UnitType)
                    ? "No marches left this season"
                    : $"Select a hex to march ({BalanceConfig.MarchBudget(engine.State.Season, army.UnitType)} points)"), body);

            if (canGarrison)
            {
                if (!garrisonArmyConversionOpen)
                {
                    if (GUI.Button(new Rect(p.x + 12f, p.y + 124f, p.width - 24f, 34), "Convert troops " + OrderCostSuffix(ActionType.GarrisonArmy), btn))
                        garrisonArmyConversionOpen = true;
                }
                else
                {
                    GUI.Label(new Rect(p.x + 12f, p.y + 120f, p.width - 24f, 22),
                        $"Garrison troops: {garrisonArmyAmount}/{army.Troops}", body);
                    garrisonArmyAmount = Mathf.RoundToInt(GUI.HorizontalSlider(
                        new Rect(p.x + 12f, p.y + 142f, p.width - 24f, 20f),
                        garrisonArmyAmount, 1, army.Troops));
                    string garrisonBlocked = occupiedCity.Garrison > 0
                        && UnitTypes.Normalize(occupiedCity.GarrisonType) != UnitTypes.Normalize(army.UnitType)
                        ? $"unit types differ (garrison {UnitTypeName(occupiedCity.GarrisonType)})" : null;
                    OrderButton(new Rect(p.x + 12f, p.y + 162f, p.width - 24f, 34), ActionType.GarrisonArmy,
                        army.Id,
                        garrisonBlocked == null
                            ? $"Garrison in {occupiedCity.Name}"
                            : $"Garrison — {garrisonBlocked}",
                        garrisonBlocked, garrisonArmyAmount, 0, btn);
                }
            }

            if (orders == 0)
            {
                float noOrdersTop = canGarrison
                    ? p.y + (garrisonArmyConversionOpen ? 208f : 166f)
                    : p.y + 120f;
                GUI.Label(new Rect(22, noOrdersTop, p.width - 32f, 22), "No enemy armies or cities nearby", body);
                return;
            }

            string hint = null;
            int row = 0;
            float buttonLeft = p.x + 12f;
            float buttonTop = p.y + (canGarrison ? (garrisonArmyConversionOpen ? 212f : 174f) : 110f);
            float buttonWidth = p.width - 24f;
            foreach (var enemy in enemyArmies)
            {
                int distance = engine.State.Map.HexDistance(army.X, army.Z, enemy.X, enemy.Z, attackRange);
                string label = (distance > 1 ? "Attack (ranged) " : "Attack ") + enemy.Name + $" · Troops {enemy.Troops}";
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + row * 38f, buttonWidth, 34), ActionType.AttackArmy,
                    army.Id, label,
                    army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "Army routed and unable to fight" : null,
                    enemy.X, enemy.Z, btn));
                row++;
            }
            foreach (var city in enemyCities)
            {
                var defensiveArmy = engine.State.ArmyAt(city.X, city.Z);
                int defensiveArmyTroops = defensiveArmy != null && defensiveArmy.OwnerSeatId == city.OwnerSeatId
                    ? defensiveArmy.Troops : 0;
                int effectiveGarrison = city.Garrison + defensiveArmyTroops;
                int requiredTroops = BalanceConfig.AssaultTroopRatio * effectiveGarrison;
                string blocked = army.Morale <= BalanceConfig.CombatRoutMoraleThreshold ? "Army routed and unable to fight"
                    : UnitTypes.WaivesAssaultRatio(army.UnitType) ? null
                    : army.Troops < requiredTroops
                        ? $"Requires {BalanceConfig.AssaultTroopRatio}:1 troop ratio ({requiredTroops} troops needed)" : null;
                hint = FirstReason(hint, OrderButton(new Rect(buttonLeft, buttonTop + row * 38f, buttonWidth, 34), ActionType.AttackCity,
                    army.Id, $"Assault {city.Name} · {army.Troops}/{effectiveGarrison} troops", blocked, city.X, city.Z, btn));
                row++;
            }

            if (hint != null) GUI.Label(new Rect(22, buttonTop + row * 38f + 38f, p.width - 32f, 22), hint, body);
        }

        /// <summary>
        /// Enemy armies the seat may actually act against: candidates come from this seat's observation, so
        /// the panel can never name or count a unit the fog hides (it used to read canonical state, which
        /// also let a player attack an army they could not see). P15: 弓兵 reaches 2 hexes, every other type
        /// 1 — the same <see cref="UnitTypes.AttackRange"/> the validator uses. The engine still has the last word.
        /// </summary>
        List<ArmyState> AttackableEnemyArmies(ArmyState army)
        {
            var list = new List<ArmyState>();
            if (obs == null) return list;
            int range = UnitTypes.AttackRange(army.UnitType);
            foreach (var other in obs.Armies)
                if (other.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.HexDistance(army.X, army.Z, other.X, other.Z, range) >= 0) list.Add(other);
            return list;
        }

        /// <summary>Enemy cities within reach, from this seat's observation only (see above).</summary>
        List<CityState> AdjacentEnemyCities(ArmyState army)
        {
            var list = new List<CityState>();
            if (obs == null) return list;
            foreach (var city in obs.Cities)
                if (!city.IsNeutral && city.OwnerSeatId != army.OwnerSeatId
                    && engine.State.Map.AreAdjacent(army.X, army.Z, city.X, city.Z)) list.Add(city);
            return list;
        }

        void PlannedLabel(Rect r, string text, GUIStyle style)
        {
            bool prev = GUI.enabled;
            GUI.enabled = false;      // grayed out: designed, not yet implemented
            GUI.Label(r, text, style);
            GUI.enabled = prev;
        }

        string OwnerName(CityState city)
        {
            if (city.IsNeutral) return "Neutral";
            var f = engine.State.FindFaction(city.OwnerSeatId);
            return f != null ? f.Name : "?";
        }
    }
}
