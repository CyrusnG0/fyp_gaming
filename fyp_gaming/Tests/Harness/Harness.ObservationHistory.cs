using System;

using System.Collections.Generic;

using LuanShi.Engine;



// [LS-003] Immutable history and caller-data snapshots.

//

// Companion module to Harness.ObservationDetachment.cs: emission-time copies of caller-owned payloads

// and args, projected event detachment, destroyed/moved subjects, and the deterministic/canonical

// isolation oracle. Split so each LS-003 test module stays focused and small.



internal static partial class Harness

{

    // ---- caller-owned payload / args ----------------------------------------

    private static void CallerOwnedPayloadsAndArgs()
    {
        var e = DetachWorld(704);
        var args = new Dictionary<string, string>
        {
            { "treaty_type", TreatyType.Nap }, { "duration", "4" },
        };
        var cmd = new ActionCommand
        {
            Type = ActionType.ProposeTreaty, ActorSeatId = "seat-a", TargetId = "seat-b",
            Args = args, Text = "first text",
        };
        var r = e.Submit(cmd);
        string canonicalJson = e.Log.ToJsonLines();
        var canonical = Find(e.Log, "action");
        int argsCount = canonical.Args.Count;

        args["treaty_type"] = TreatyType.Truce;
        args["injected"] = "mutated";
        cmd.Args = new Dictionary<string, string> { { "treaty_type", "alliance" } };

        Check(r.Ok && e.Log.ToJsonLines() == canonicalJson,
              $"[LS-003] mutating the command's args after Submit leaves history unchanged ({r.Error})");
        Check(canonical.Args != null && canonical.Args.Count == argsCount
           && canonical.Args["treaty_type"] == TreatyType.Nap && !canonical.Args.ContainsKey("injected"),
              "[LS-003] the emitted record kept its own copy of the command args");

        var payload = new Dictionary<string, string> { { "secret", "one" } };
        var emitArgs = new Dictionary<string, string> { { "k", "v" } };
        var emitted = e.Log.Emit(e.State.GameId, e.State.Season, e.State.Phase,
            "seat-a", ControllerType.Human, "ls003_test", "owner_only",
            payload, "a-test", emitArgs, "note");
        string afterEmit = e.Log.ToJsonLines();

        payload["secret"] = "two";
        payload["extra"] = "x";
        emitArgs["k"] = "changed";
        emitArgs["extra"] = "y";

        Check(e.Log.ToJsonLines() == afterEmit,
              "[LS-003] emission snapshots the caller's payload and args dictionaries");
        Check(emitted.Payload["secret"] == "one" && emitted.Payload.Count == 1
           && emitted.Args["k"] == "v" && emitted.Args.Count == 1,
              "[LS-003] the emitted record is immune to later caller mutations");
    }

    // ---- projected events ---------------------------------------------------

    private static void ProjectedEventDetachment()
    {
        var e = DetachWorld(705);
        var recruitArgs = new Dictionary<string, string> { { "unit_type", UnitTypes.Infantry } };
        var r = e.Submit(new ActionCommand
        {
            Type = ActionType.Recruit, ActorSeatId = "seat-b", TargetId = "city-b", Args = recruitArgs,
            Text = "private note",
        });

        var canonical = Find(e.Log, "action");
        var bystander = ObsEvent(e.Observe("seat-a"), "action");
        var actor = ObsEvent(e.Observe("seat-b"), "action");

        Check(r.Ok && canonical != null && canonical.Args != null,
              $"[LS-003] the fixture recruits with caller command data ({r.Error})");
        Check(bystander != null && bystander.Payload.Count == 1
           && bystander.Payload["action"] == ActionType.Recruit,
              "[LS-003] a bystander's projected event keeps only the public action type");
        Check(bystander != null && bystander.Args == null && bystander.Text == null,
              "[LS-003] a bystander's projected event carries no args or free text");
        Check(actor != null && actor.Args != null && actor.Payload.ContainsKey("reason"),
              "[LS-003] the actor's projected event keeps its own details");
        Check(!ReferenceEquals(bystander.Payload, canonical.Payload)
           && !ReferenceEquals(actor.Payload, canonical.Payload),
              "[LS-003] observation events carry detached payload copies");
        Check(ObsEvent(e.Observe("seat-a"), "recruit") == null,
              "[LS-003] the hidden city's domain event is not projected to the bystander");

        bool payloadWriteThrew = Throws<NotSupportedException>(
            () => { ((IDictionary<string, string>)bystander.Payload)["injected"] = "yes"; });
        Check(payloadWriteThrew,
              "[LS-003] a projected event's payload cannot be modified");
        Check(!canonical.Payload.ContainsKey("injected") && !actor.Payload.ContainsKey("injected")
           && !ObsEvent(e.Observe("seat-a"), "action").Payload.ContainsKey("injected"),
              "[LS-003] an attempted payload write reaches neither canonical nor actor history");

        string actorJson = actor.ToJson();
        bool actorWriteThrew = Throws<NotSupportedException>(
            () => { ((IDictionary<string, string>)actor.Payload)["injected"] = "yes"; });
        Check(actorWriteThrew && actor.ToJson() == actorJson,
              "[LS-003] an attempted write cannot rewrite a prior projected event");

        bool logPayloadThrew = Throws<NotSupportedException>(
            () => { ((IDictionary<string, string>)canonical.Payload)["hack"] = "1"; });
        Check(logPayloadThrew && !Find(e.Log, "action").Payload.ContainsKey("hack"),
              "[LS-003] canonical history payloads are read-only");

        bool logListThrew = Throws<NotSupportedException>(
            () => { ((IList<EventRecord>)e.Log.Records).Clear(); });
        Check(logListThrew && e.Log.Records.Count > 0,
              "[LS-003] the returned event log cannot be truncated");
    }

    // ---- moved / destroyed subjects ----------------------------------------

    private static void MovedAndDestroyedSubjects()
    {
        var e = DetachWorld(706);
        e.State.Diplomacy.DeclareWar("seat-a", "seat-b");
        var before = e.Observe("seat-a");
        string beforeJson = before.ToJson();
        Check(before.FindArmy("army-b") != null && before.FindArmy("army-b").Troops == 40,
              "[LS-003] the enemy army is visible before the battle");

        var r = e.Submit(new ActionCommand
        {
            Type = ActionType.AttackArmy, ActorSeatId = "seat-a", TargetId = "army-a", ParamA = 5, ParamB = 0,
        });
        var after = e.Observe("seat-a");

        Check(r.Ok, $"[LS-003] the battle resolves ({r.Error})");
        Check(e.State.FindArmy("army-b") == null,
              "[LS-003] the defeated army leaves canonical state");
        Check(before.FindArmy("army-b") != null && before.FindArmy("army-b").Troops == 40,
              "[LS-003] an old snapshot still holds the destroyed army's last view");
        Check(before.ToJson() == beforeJson,
              "[LS-003] destruction does not rewrite an old snapshot");
        Check(after.FindArmy("army-b") == null,
              "[LS-003] the new snapshot no longer lists the destroyed army");

        before.FindArmy("army-b").Troops = 999999;
        Check(e.State.FindArmy("army-b") == null && e.Observe("seat-a").FindArmy("army-b") == null,
              "[LS-003] mutating the stale view cannot resurrect the army");
    }

    // ---- deterministic JSON and canonical isolation -------------------------

    private static void DeterministicAndCanonicalIsolation()
    {
        var e1 = DetachWorld(708);
        var e2 = DetachWorld(708);
        string baseline = e1.Observe("seat-a").ToJson();
        Check(baseline == e2.Observe("seat-a").ToJson(),
              "[LS-003] two identical worlds produce byte-identical observations");

        var obs = e1.Observe("seat-a");
        var kept = e1.Observe("seat-a");
        string keptJson = kept.ToJson();
        var firstEvent = obs.Events.Count > 0 ? obs.Events[0] : null;
        string fingerprint = CanonicalFingerprint(e1);

        obs.FindCity("city-a").Gold = 0;
        obs.FindArmy("army-a").Morale = 0;
        obs.Cities.Add(CityView.From(e1.State.FindCity("city-b")));
        obs.Research[ResearchLine.Military] = 5;
        obs.Relation("seat-b").AtWar = true;
        obs.Events.Clear();
        if (firstEvent != null) obs.Events.Add(firstEvent);

        Check(CanonicalFingerprint(e1) == fingerprint,
              "[LS-003] a full battery of view mutations leaves canonical state and history identical");
        Check(kept.ToJson() == keptJson,
              "[LS-003] mutating one retained snapshot leaves a second snapshot unchanged");
        Check(e1.Observe("seat-a").ToJson() == baseline,
              "[LS-003] a fresh observation after the mutation battery is byte-identical to the baseline");
        Check(e1.Observe("seat-a").ToJson() == e2.Observe("seat-a").ToJson(),
              "[LS-003] detached views preserve deterministic JSON across worlds");
    }
}

