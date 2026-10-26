using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunObservation()
    {
        // ------------------------------------------------ season name + observation wiring
        Section("season name / observation (ADR-011 payload)");

        {
            var e = Build(3, 3, morale: 100, food: 5000);
            var obs = e.Observe("seat-a");
            Check(obs.SeasonName == "Autumn", $"observation carries the season name ({obs.SeasonName})");
            Check(obs.WinnerSeatId == null, "a live game reports winner_seat_id = null");
            string json = obs.ToJson();
            Check(json.Contains("\"season_name\":\"Autumn\""), "observation JSON carries season_name");
            Check(json.Contains("\"winner_seat_id\":null"), "observation JSON carries winner_seat_id");
            Check(e.Log.Records.Count > 0 && Find(e.Log, "season_start").Payload["season_name"] == "Spring",
                  "season_start event carries the season name");
        }

        {
            var e = Build(11, 1, morale: 100, food: 5000);
            e.State.Cities.Add(new CityState { Id = "city-b", Name = "City B", X = 5, Z = 0, OwnerSeatId = null, Population = 10000, Food = 500, Gold = 0, Garrison = 0 });
            var army = AddArmy(e, "seat-a", 4, 5000);
            e.Submit(new ActionCommand { Type = ActionType.March, ActorSeatId = "seat-a", TargetId = army.Id, ParamA = 5, ParamB = 0 });
            var obs = e.Observe("seat-a");
            Check(obs.WinnerSeatId == "seat-a", "observation exposes the winner for the LLM/handover path");
            Check(obs.ToJson().Contains("\"winner_seat_id\":\"seat-a\""), "observation JSON carries the winner");
        }
    }
}
