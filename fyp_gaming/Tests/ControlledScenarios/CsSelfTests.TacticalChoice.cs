using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    public static partial class CsSelfTests
    {
        private static void TacticalChoiceChecks(TestLog log)
        {
            CsScenarioSpec spec = Builtin(CsScenarios.TacticalChoiceId, 5151);
            log.Check(spec.Seasons == 2 && spec.EnableAttack && spec.EnableSpatial
                && spec.EnableManualLookup && spec.EnableDeliberation,
                "[CS-T1] staged tactical choice enables attack, spatial and inquiry");
            GameEngine attackWorld = CsWorld.Create(spec, "tactical-attack");
            bool listed = false;
            foreach (ArmyAttackPlan p in AttackPlanner.Build(attackWorld, "seat-0").Armies)
                foreach (AttackOption o in p.Options)
                    if (o.Kind == ActionType.AttackArmy && o.X == 9 && o.Z == 8) listed = true;
            ValidationResult attack = attackWorld.Submit(new ActionCommand {
                Type = ActionType.AttackArmy, ActorSeatId = "seat-0", TargetId = CsScenarioFactory.OwnArmyId,
                ParamA = 9, ParamB = 8 });
            GameEngine retreatWorld = CsWorld.Create(spec, "tactical-retreat");
            ValidationResult retreat = retreatWorld.Submit(new ActionCommand {
                Type = ActionType.March, ActorSeatId = "seat-0", TargetId = CsScenarioFactory.OwnArmyId,
                ParamA = 7, ParamB = 8 });
            log.Check(listed && attack.Ok && retreat.Ok,
                "[CS-T1] attack and retreat are both engine-legal from the starting position",
                "listed=" + listed + " attack=" + attack.Ok + " retreat=" + retreat.Ok);
            spec.MaxRequests = 30;
            CsScenarioResult result = RunSpec(spec, "cst1", out string root);
            try
            {
                log.Check(result.Status == CsStatus.Completed && result.SeasonsPlayed == 2,
                    "[CS-T1] fake rehearsal completes two seasons", "status=" + result.Status);
                List<JsonElement> prompts = ReadJsonLines(Path.Combine(result.Directory, "prompts.ndjson"));
                bool initial = false, ownerNote = false, leak = false, foreign = false;
                foreach (JsonElement p in prompts)
                {
                    string seat = p.GetProperty("seat").GetString();
                    string user = p.GetProperty("user").GetString();
                    if (seat == "seat-0")
                    {
                        if (!initial) initial = user.Contains("SPATIAL CONTEXT")
                            && user.Contains("attack_army") && user.Contains("7,8");
                        if (user.Contains("fixture: keep the army safe")) ownerNote = true;
                        if (user.Contains("cs-army-hidden") || user.Contains("cs-city-enemy")) foreign = true;
                    }
                    else if (user.Contains("fixture: keep the army safe")) leak = true;
                }
                log.Check(initial, "[CS-T1] first saved prompt offers attack, retreat and spatial context");
                log.Check(ownerNote && !leak && !foreign,
                    "[CS-T1] note stays private and hidden enemies are absent");
                var lines = new List<JsonElement>();
                foreach (string f in Directory.GetFiles(result.Directory, "run-*.jsonl"))
                    lines.AddRange(ReadJsonLines(f));
                int zero = 0;
                foreach (JsonElement x in lines)
                    if (x.GetProperty("seat_id").GetString() == "seat-0"
                        && x.GetProperty("season").GetInt32() == 1
                        && x.GetProperty("actions_this_turn").GetInt32() == 0) zero++;
                log.Check(zero >= 2, "[CS-T1] inquiry and note do not spend actions", "zero=" + zero);
            }
            finally { Cleanup(root); }
        }
    }
}
