using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunVictory()
    {
        // ------------------------------------------------ 一統天下 victory
        Section("victory — 一統天下 (design §13)");

        {
            var e = Build(11, 1, morale: 100, food: 5000);
            e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
            e.State.Cities.Add(new CityState { Id = "city-b", Name = "City B", X = 5, Z = 0, OwnerSeatId = null, Population = 10000, Food = 500, Gold = 0, Garrison = 0 });
            var army = AddArmy(e, "seat-a", 4, 5000);

            var march = e.Submit(new ActionCommand { Type = ActionType.March, ActorSeatId = "seat-a", TargetId = army.Id, ParamA = 5, ParamB = 0 });
            Check(march.Ok, $"march onto the last neutral city ({march.Error})");
            Check(e.State.FindCity("city-b").OwnerSeatId == "seat-a", "the neutral city is captured");
            Check(e.State.WinnerSeatId == "seat-a", $"every city is owned by seat-a → WinnerSeatId = {e.State.WinnerSeatId}");

            var over = Find(e.Log, "game_over");
            Check(over != null && over.Visibility == "public", "game_over is public");
            Check(over != null && over.Payload["winner_seat_id"] == "seat-a" && over.Payload["reason"] == "unification",
                  "game_over payload names the winner and the route");
            Check(e.SeesEvent("seat-b", over), "the losing seat sees game_over");

            var after = e.Submit(new ActionCommand { Type = ActionType.Recruit, ActorSeatId = "seat-a", TargetId = "city-a" });
            Check(!after.Ok && after.Error == "game over", $"Submit() rejects every later action ({after.Error})");
            var afterOther = e.Submit(new ActionCommand { Type = ActionType.LevyTax, ActorSeatId = "seat-b", TargetId = "city-b" });
            Check(!afterOther.Ok && afterOther.Error == "game over", $"the defeat seat is blocked too ({afterOther.Error})");
        }

        {
            // not yet: one seat short of every city
            var e = Build(11, 1, morale: 100, food: 5000);
            e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
            e.State.Cities.Add(new CityState { Id = "city-b", Name = "City B", X = 5, Z = 0, OwnerSeatId = "seat-b", Population = 10000, Food = 500, Gold = 0, Garrison = 0 });
            var army = AddArmy(e, "seat-a", 0, 5000);
            e.Submit(new ActionCommand { Type = ActionType.March, ActorSeatId = "seat-a", TargetId = army.Id, ParamA = 6, ParamB = 0 });
            Check(e.State.WinnerSeatId == null, "no victory while any city belongs to another seat");
            Check(CountEvents(e.Log, "game_over") == 0, "no game_over event while the game is live");
        }
    }
}
