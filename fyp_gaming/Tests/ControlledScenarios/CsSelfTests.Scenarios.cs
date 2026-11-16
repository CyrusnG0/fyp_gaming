using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LuanShi.ControlledScenarios
{
    // CS-34: the two strong-threat scenarios must advertise their real legal actions honestly.
    // The retreat-only scenario disables attacks and labels itself as such; the fight-or-retreat
    // scenario enables attacks so both an attack and a retreat are offered. Every reply stays a
    // labelled fake_scripted fixture: no choice and no outcome is asserted as good or bad.
    public static partial class CsSelfTests
    {
        private static void StrongThreatLegalActions(TestLog log)
        {
            CsScenarioResult retreatOnly = RunSpec(Builtin("cs-strong-threat-retreat-v1", 4244),
                "cs34-retreat-only", out string retreatRoot);
            try
            {
                StudyEvidence evidence = StudyEvidence.Read(retreatOnly.Directory);
                log.Check(evidence.FirstDispatch.ValueKind != JsonValueKind.Undefined,
                    "[CS-34] the retreat-only scenario has a first study-seat dispatch");
                log.Check(evidence.ExactPromptMatch,
                    "[CS-34] the retreat-only prompt is the exact text saved in prompts.ndjson");
                log.Check(evidence.DisplayName != null
                        && evidence.DisplayName.IndexOf("retreat-only", StringComparison.OrdinalIgnoreCase) >= 0
                        && evidence.DisplayName.IndexOf("attack disabled", StringComparison.OrdinalIgnoreCase) >= 0,
                    "[CS-34] the retreat-only title/label says retreat-only and attack disabled",
                    evidence.DisplayName ?? "missing display_name");
                log.Check(evidence.AttackCount == 0
                        && (evidence.CatalogueText ?? "").IndexOf("type=attack_army",
                            StringComparison.Ordinal) < 0,
                    "[CS-34] retreat-only legal actions contain NO attack action",
                    "attack options=" + evidence.AttackCount);
                log.Check(string.IsNullOrEmpty(evidence.AttacksJson),
                    "[CS-34] retreat-only engine exposes no attack view at all");
                log.Check(MovementHas(evidence.MovementJson, CsScenarioFactory.OwnArmyId, 7, 8),
                    "[CS-34] retreat-only engine exposes the retreat march to (7,8)");
                log.Check(ContainsSection(evidence.UserText, "CANDIDATE OPTIONS")
                        && ContainsSection(evidence.UserText, "MOVEMENT OPTIONS"),
                    "[CS-34] retreat-only prompt advertises candidate and movement options");
                log.Check(evidence.FixtureLabelled,
                    "[CS-34] retreat-only replies are fake_scripted fixtures; no good/bad outcome is asserted");
            }
            finally { Cleanup(retreatRoot); }

            CsScenarioResult fightOrRetreat = RunSpec(
                Builtin("cs-strong-threat-fight-or-retreat-v1", 4248),
                "cs34-fight-or-retreat", out string fightRoot);
            try
            {
                StudyEvidence evidence = StudyEvidence.Read(fightOrRetreat.Directory);
                log.Check(evidence.FirstDispatch.ValueKind != JsonValueKind.Undefined,
                    "[CS-34] the fight-or-retreat scenario has a first study-seat dispatch");
                log.Check(evidence.ExactPromptMatch,
                    "[CS-34] the fight-or-retreat prompt is the exact text saved in prompts.ndjson");
                log.Check(evidence.DisplayName != null
                        && evidence.DisplayName.IndexOf("fight or retreat", StringComparison.OrdinalIgnoreCase) >= 0
                        && evidence.DisplayName.IndexOf("attack enabled", StringComparison.OrdinalIgnoreCase) >= 0,
                    "[CS-34] the fight-or-retreat title/label says fight or retreat and attack enabled",
                    evidence.DisplayName ?? "missing display_name");
                log.Check(evidence.AttackCount > 0 && evidence.AttackTargetsEnemy,
                    "[CS-34] fight-or-retreat legal actions include an attack on the enemy army",
                    "attack options=" + evidence.AttackCount);
                log.Check(AttackHas(evidence.AttacksJson, CsScenarioFactory.OwnArmyId,
                        CsScenarioFactory.EnemyArmyId, 9, 8),
                    "[CS-34] the engine's attack view names the enemy army at (9,8)");
                log.Check(MovementHas(evidence.MovementJson, CsScenarioFactory.OwnArmyId, 7, 8),
                    "[CS-34] fight-or-retreat legal actions also include the retreat march to (7,8)");
                log.Check(ContainsSection(evidence.UserText, "CANDIDATE OPTIONS")
                        && ContainsSection(evidence.UserText, "MOVEMENT OPTIONS")
                        && ContainsSection(evidence.UserText, "type=attack_army"),
                    "[CS-34] fight-or-retreat prompt advertises candidate, movement and attack options");
                log.Check(evidence.FixtureLabelled,
                    "[CS-34] fight-or-retreat replies are fake_scripted fixtures; no good/bad outcome is asserted");
            }
            finally { Cleanup(fightRoot); }
        }

        private static bool ContainsSection(string text, string section)
            => !string.IsNullOrEmpty(text) && text.IndexOf(section, StringComparison.Ordinal) >= 0;

        /// <summary>Structured movement candidates exactly as the engine exposed them in evidence.</summary>
        private static bool MovementHas(string movementJson, string armyId, int x, int z)
        {
            if (string.IsNullOrEmpty(movementJson)) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(movementJson))
                {
                    JsonElement root = doc.RootElement;
                    if (!root.TryGetProperty("armies", out JsonElement armies)
                        || armies.ValueKind != JsonValueKind.Array) return false;
                    foreach (JsonElement army in armies.EnumerateArray())
                    {
                        if (CsEvidenceRecorder.Text(army, "army_id") != armyId) continue;
                        if (!army.TryGetProperty("options", out JsonElement options)
                            || options.ValueKind != JsonValueKind.Array) continue;
                        foreach (JsonElement option in options.EnumerateArray())
                            if (CsEvidenceRecorder.Int(option, "x") == x
                                && CsEvidenceRecorder.Int(option, "z") == z) return true;
                    }
                }
            }
            catch (JsonException) { }
            return false;
        }

        /// <summary>Structured attack candidates exactly as the engine exposed them in evidence.</summary>
        private static bool AttackHas(string attackJson, string armyId, string targetArmyId,
            int x, int z)
        {
            if (string.IsNullOrEmpty(attackJson)) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(attackJson))
                {
                    JsonElement root = doc.RootElement;
                    if (!root.TryGetProperty("armies", out JsonElement armies)
                        || armies.ValueKind != JsonValueKind.Array) return false;
                    foreach (JsonElement army in armies.EnumerateArray())
                    {
                        if (CsEvidenceRecorder.Text(army, "army_id") != armyId) continue;
                        if (!army.TryGetProperty("options", out JsonElement options)
                            || options.ValueKind != JsonValueKind.Array) continue;
                        foreach (JsonElement option in options.EnumerateArray())
                            if (CsEvidenceRecorder.Text(option, "kind") == "attack_army"
                                && CsEvidenceRecorder.Text(option, "target_army_id") == targetArmyId
                                && CsEvidenceRecorder.Int(option, "x") == x
                                && CsEvidenceRecorder.Int(option, "z") == z) return true;
                    }
                }
            }
            catch (JsonException) { }
            return false;
        }

        /// <summary>The first study-seat dispatch plus the structured legal actions saved with it.</summary>
        private sealed class StudyEvidence
        {
            public JsonElement FirstDispatch;
            public bool ExactPromptMatch;
            public JsonElement Catalogue;
            public string CatalogueText, MovementJson, AttacksJson, DisplayName;
            public int AttackCount;
            public bool AttackTargetsEnemy;
            public bool FixtureLabelled;
            public string UserText;

            public static StudyEvidence Read(string scenarioDirectory)
            {
                var evidence = new StudyEvidence();
                List<JsonElement> dispatches = ReadJsonLines(
                    Path.Combine(scenarioDirectory, "dispatches.ndjson"));
                foreach (JsonElement dispatch in dispatches)
                {
                    if (CsEvidenceRecorder.Text(dispatch, "seat_id") != "seat-0") continue;
                    evidence.FirstDispatch = dispatch;
                    break;
                }

                JsonElement prompt = Obj(evidence.FirstDispatch, "prompt");
                string system = CsEvidenceRecorder.Text(prompt, "system");
                evidence.UserText = CsEvidenceRecorder.Text(prompt, "user");
                JsonElement visible = Obj(prompt, "visible_options");
                evidence.Catalogue = visible.ValueKind == JsonValueKind.Object
                    && visible.TryGetProperty("catalogue", out JsonElement catalogue)
                    ? catalogue : default;
                evidence.CatalogueText = CsEvidenceRecorder.Text(visible, "catalogue_text");
                evidence.MovementJson = CsEvidenceRecorder.Text(visible, "movement_json");
                evidence.AttacksJson = CsEvidenceRecorder.Text(visible, "attacks_json");
                evidence.CountAttacks();

                foreach (JsonElement line in ReadJsonLines(
                    Path.Combine(scenarioDirectory, "prompts.ndjson")))
                {
                    if (CsEvidenceRecorder.Text(line, "seat") != "seat-0") continue;
                    evidence.ExactPromptMatch = system != null && evidence.UserText != null
                        && CsEvidenceRecorder.Text(line, "system") == system
                        && CsEvidenceRecorder.Text(line, "user") == evidence.UserText;
                    break;
                }

                evidence.DisplayName = ReadDisplayName(
                    Path.Combine(scenarioDirectory, "scenario.json"));
                evidence.FixtureLabelled = dispatches.Count > 0;
                foreach (JsonElement dispatch in dispatches)
                {
                    JsonElement reply = Obj(dispatch, "reply");
                    JsonElement provider = Obj(dispatch, "provider");
                    evidence.FixtureLabelled &= CsEvidenceRecorder.Text(reply, "source") == "fake_scripted"
                        && CsEvidenceRecorder.Bool(provider, "live") == false;
                }
                return evidence;
            }

            private void CountAttacks()
            {
                AttackCount = 0;
                AttackTargetsEnemy = false;
                if (Catalogue.ValueKind != JsonValueKind.Array) return;
                foreach (JsonElement option in Catalogue.EnumerateArray())
                {
                    if (CsEvidenceRecorder.Text(option, "Type") != "attack_army") continue;
                    AttackCount++;
                    if (CsEvidenceRecorder.Text(option, "TargetId") == CsScenarioFactory.OwnArmyId
                        && CsEvidenceRecorder.Int(option, "ParamA") == 9
                        && CsEvidenceRecorder.Int(option, "ParamB") == 8)
                        AttackTargetsEnemy = true;
                }
            }

            private static string ReadDisplayName(string path)
            {
                if (!File.Exists(path)) return null;
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                        return CsEvidenceRecorder.Text(doc.RootElement, "display_name");
                }
                catch (JsonException) { return null; }
            }
        }
    }
}
