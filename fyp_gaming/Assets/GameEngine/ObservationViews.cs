using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    // Detached observation DTOs (LS-003).
    //
    // Every entity an Observation carries is one of these copies: the engine builds them from the
    // canonical state when Observe() runs and never hands a canonical CityState/ArmyState/... out.
    // A controller may therefore mutate what it received (they are ordinary mutable DTOs, like
    // ScoutedCityView and FactionView) without touching the game, another seat's snapshot, or an older
    // snapshot. JSON shapes are identical to the canonical classes so observation payloads are unchanged.

    /// <summary>One city as a seat's observation sees it. A detached copy of <see cref="CityState"/>.</summary>
    public sealed class CityView
    {
        public string Id;
        public string Name;
        public int X, Z;
        public string OwnerSeatId;
        public int Population;
        public int Food;
        public int Gold;
        public int Garrison;
        public string GarrisonType = UnitTypes.Infantry;
        public bool ReclaimedThisSeason;
        public int Morale;
        public int Defense;
        public int Training;
        public int ReclaimPct;

        public bool IsNeutral => string.IsNullOrEmpty(OwnerSeatId);

        public static CityView From(CityState city)
        {
            if (city == null) return null;
            return new CityView
            {
                Id = city.Id, Name = city.Name, X = city.X, Z = city.Z,
                OwnerSeatId = city.OwnerSeatId, Population = city.Population, Food = city.Food,
                Gold = city.Gold, Garrison = city.Garrison, GarrisonType = city.GarrisonType,
                ReclaimedThisSeason = city.ReclaimedThisSeason, Morale = city.Morale,
                Defense = city.Defense, Training = city.Training, ReclaimPct = city.ReclaimPct,
            };
        }

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"population\":").Append(MiniJson.Num(Population));
            sb.Append(",\"food\":").Append(MiniJson.Num(Food));
            sb.Append(",\"gold\":").Append(MiniJson.Num(Gold));
            sb.Append(",\"garrison\":").Append(MiniJson.Num(Garrison));
            sb.Append(",\"garrison_type\":").Append(MiniJson.Str(GarrisonType));
            sb.Append(",\"morale\":").Append(MiniJson.Num(Morale));
            sb.Append(",\"defense\":").Append(MiniJson.Num(Defense));
            sb.Append(",\"training\":").Append(MiniJson.Num(Training));
            sb.Append(",\"reclaim_pct\":").Append(MiniJson.Num(ReclaimPct));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One army as a seat's observation sees it. A detached copy of <see cref="ArmyState"/>.</summary>
    public sealed class ArmyView
    {
        public string Id;
        public string Name;
        public string OwnerSeatId;
        public int Troops;
        public string UnitType = UnitTypes.Infantry;
        public int X, Z;
        public bool MarchedThisSeason;
        public int MarchCountThisSeason;
        public int Morale;

        public static ArmyView From(ArmyState army)
        {
            if (army == null) return null;
            return new ArmyView
            {
                Id = army.Id, Name = army.Name, OwnerSeatId = army.OwnerSeatId, Troops = army.Troops,
                UnitType = army.UnitType, X = army.X, Z = army.Z,
                MarchedThisSeason = army.MarchedThisSeason,
                MarchCountThisSeason = army.MarchCountThisSeason, Morale = army.Morale,
            };
        }

        public string ToJson()
        {
            var sb = new StringBuilder(160);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"troops\":").Append(MiniJson.Num(Troops));
            sb.Append(",\"unit_type\":").Append(MiniJson.Str(UnitType));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"morale\":").Append(MiniJson.Num(Morale));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One watchtower as a seat's observation sees it. A detached copy of <see cref="WatchTowerState"/>.</summary>
    public sealed class WatchTowerView
    {
        public string Id;
        public string Name;
        public int X, Z;
        public string OwnerSeatId;

        public bool IsNeutral => string.IsNullOrEmpty(OwnerSeatId);

        public static WatchTowerView From(WatchTowerState tower)
        {
            if (tower == null) return null;
            return new WatchTowerView
            {
                Id = tower.Id, Name = tower.Name, X = tower.X, Z = tower.Z, OwnerSeatId = tower.OwnerSeatId,
            };
        }

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One treaty in force as the seat sees it. A detached copy of <see cref="TreatyRecord"/>.</summary>
    public sealed class TreatyView
    {
        public string Type;
        public string FactionA;
        public string FactionB;
        public int ExpirySeason;

        public bool Covers(string seatA, string seatB)
            => (FactionA == seatA && FactionB == seatB) || (FactionA == seatB && FactionB == seatA);

        public static TreatyView From(TreatyRecord treaty)
        {
            if (treaty == null) return null;
            return new TreatyView
            {
                Type = treaty.Type, FactionA = treaty.FactionA, FactionB = treaty.FactionB,
                ExpirySeason = treaty.ExpirySeason,
            };
        }

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"faction_a\":").Append(MiniJson.Str(FactionA));
            sb.Append(",\"faction_b\":").Append(MiniJson.Str(FactionB));
            sb.Append(",\"expiry_season\":").Append(MiniJson.Num(ExpirySeason));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>One pending treaty offer as the seat sees it. A detached copy of <see cref="TreatyProposal"/>.</summary>
    public sealed class ProposalView
    {
        public string ProposalId;
        public string FromSeatId;
        public string ToSeatId;
        public string Type;
        public int DurationSeasons;
        public int ProposedSeason;
        public bool Accepted;
        public string Text;

        public static ProposalView From(TreatyProposal proposal)
        {
            if (proposal == null) return null;
            return new ProposalView
            {
                ProposalId = proposal.ProposalId, FromSeatId = proposal.FromSeatId,
                ToSeatId = proposal.ToSeatId, Type = proposal.Type,
                DurationSeasons = proposal.DurationSeasons, ProposedSeason = proposal.ProposedSeason,
                Accepted = proposal.Accepted, Text = proposal.Text,
            };
        }

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"proposal_id\":").Append(MiniJson.Str(ProposalId));
            sb.Append(",\"from_seat_id\":").Append(MiniJson.Str(FromSeatId));
            sb.Append(",\"to_seat_id\":").Append(MiniJson.Str(ToSeatId));
            sb.Append(",\"treaty_type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"duration_seasons\":").Append(MiniJson.Num(DurationSeasons));
            sb.Append(",\"proposed_season\":").Append(MiniJson.Num(ProposedSeason));
            sb.Append(",\"accepted\":").Append(Accepted ? "true" : "false");
            sb.Append(",\"text\":").Append(MiniJson.Str(Text));
            sb.Append('}');
            return sb.ToString();
        }
    }
}
