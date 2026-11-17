using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LuanShi.Engine;

// [new] Deterministic RichContextInputs builder for the prompt-size probe. Fixed seedless data:
// no clocks, no network, no global RNG. The same case always renders the same prompt.
internal static partial class Harness
{
    private sealed class PromptSizeProbeCase
    {
        public int HistoryLines = 40;
        public int HistoryLineChars = 40;
        public int EventCount = 120;
        public int MemoryCount = 30;
        public int OwnCityCount = 4;
        public int ArmyCount = 5;
        public int OptionsPerArmy = 20;
        public int InquiryChars;
        public int PriorResultCount = 6;
        public int PriorResultChars = 1200;
        public int VisibleRowCount = 17;
        public int ExtraObservationCities;
        public int SystemChars = PromptSizeProbeSystemChars;
        public int ReservedChars;
        public string Sentinel;   // opt-in unique marker used only by the counts-only scan
        public bool UsePersona;
    }

    private static RichContextResult ProbeAssemble(PromptSizeProbeCase c)
        => ProbeAssembleWithInputs(ProbeInputs(c));

    private static RichContextResult ProbeAssembleWithInputs(RichContextInputs input,
        int maxPromptChars = 100000)
    {
        LlmConfig config = LlmConfig.RichDeliberation();
        // The provider limit in the input is normally the binding 40000; a huge max renders
        // the same input at level 0 for the byte-equality comparison.
        config.MaxPromptChars = maxPromptChars;
        return RichContextAssembler.Assemble(input, config);
    }

    private static RichContextInputs ProbeInputs(PromptSizeProbeCase c)
    {
        var obs = new Observation
        {
            GameId = "probe-game",
            SeatId = "seat-1",
            Season = 60,
            SeasonName = "Winter",
            Phase = "Planning",
            CommandPoints = 6,
            Prestige = 140,
            ResearchPoints = 90,
        };
        foreach (string line in ResearchLine.All) obs.Research[line] = 2;

        for (int i = 0; i < c.OwnCityCount; i++)
        {
            obs.Cities.Add(new CityView
            {
                Id = "city-own-" + i.ToString(CultureInfo.InvariantCulture),
                Name = "Probe City " + i.ToString(CultureInfo.InvariantCulture),
                X = 8 + i * 6,
                Z = 10,
                OwnerSeatId = "seat-1",
                Population = 20000,
                Food = 6000,
                Gold = 2500,
                Garrison = 1500,
                Morale = 80,
                Defense = 5,
                Training = 5,
                ReclaimPct = 15,
            });
        }
        for (int i = 0; i < c.ExtraObservationCities; i++)
        {
            obs.Cities.Add(new CityView
            {
                Id = "city-extra-" + i.ToString(CultureInfo.InvariantCulture),
                Name = "Far Province City " + i.ToString(CultureInfo.InvariantCulture)
                    + " " + ProbeFill(24, 400 + i),
                X = (i * 7) % 80,
                Z = (i * 5) % 52,
                OwnerSeatId = i % 3 == 0 ? null : "seat-0",
                Population = 12000 + i,
                Food = 3000 + i,
                Gold = 1500 + i,
                Garrison = 900 + i,
                Morale = 60 + (i % 20),
                Defense = 3,
                Training = 3,
                ReclaimPct = 10,
            });
        }

        if (!string.IsNullOrEmpty(c.Sentinel))
        {
            obs.Cities.Add(new CityView
            {
                Id = "city-sentinel", Name = c.Sentinel + "_CITY", X = 1, Z = 1,
                OwnerSeatId = "seat-1", Population = 1000, Food = 100, Gold = 100,
                Garrison = 10, Morale = 50,
            });
        }

        for (int i = 0; i < c.ArmyCount; i++)
        {
            obs.Armies.Add(new ArmyView
            {
                Id = "army-own-" + i.ToString(CultureInfo.InvariantCulture),
                Name = "Probe Host " + i.ToString(CultureInfo.InvariantCulture),
                OwnerSeatId = "seat-1",
                Troops = 2200 + i * 100,
                UnitType = UnitTypes.Cavalry,
                X = 8 + i * 7,
                Z = 8 + (i % 3) * 5,
                Morale = 75,
            });
        }

        obs.Factions.Add(new FactionView
        {
            SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm, Prestige = 140,
        });
        obs.Factions.Add(new FactionView
        {
            SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm, Prestige = 150,
        });
        obs.Relations.Add(new RelationView
        {
            SeatId = "seat-0", MyTrust = 40, TheirTrust = 30, AtWar = false,
        });

        for (int i = 0; i < c.EventCount; i++)
            obs.Events.Add(ProbeEvent(i, i == c.EventCount - 1 ? c.Sentinel : null));

        var history = new List<string>(c.HistoryLines);
        for (int i = 0; i < c.HistoryLines; i++)
        {
            history.Add("S" + (59 - i).ToString(CultureInfo.InvariantCulture)
                + " order accepted: " + ProbeFill(Math.Max(0, c.HistoryLineChars - 20), 200 + i));
        }
        if (!string.IsNullOrEmpty(c.Sentinel) && history.Count > 0)
            history[history.Count - 1] += " " + c.Sentinel + "_HISTORY";

        var memories = new List<LlmMemory>(c.MemoryCount);
        for (int i = 0; i < c.MemoryCount; i++)
        {
            memories.Add(new LlmMemory
            {
                Id = "mem-" + i.ToString("00", CultureInfo.InvariantCulture),
                GameId = "probe-game",
                SeatId = "seat-1",
                Type = LlmMemoryType.Fact,
                Content = "fact " + i.ToString(CultureInfo.InvariantCulture) + " " + ProbeFill(170, 300 + i),
                Source = "probe",
                Season = 58 - (i % 6),
                Importance = 5 - (i % 3),
                Confidence = 4,
            });
        }
        if (!string.IsNullOrEmpty(c.Sentinel))
            foreach (LlmMemory memory in memories)
                memory.Content += " " + c.Sentinel + "_MEMORY";

        var priors = new List<RichInquiryEntry>(c.PriorResultCount);
        for (int i = 0; i < c.PriorResultCount; i++)
        {
            priors.Add(new RichInquiryEntry
            {
                Season = 40 + i,
                Tool = "manual_lookup",
                Json = "prior " + i.ToString(CultureInfo.InvariantCulture) + " "
                    + ProbeFill(c.PriorResultChars, 500 + i),
            });
        }

        var allowances = new PromptBuilder.RichAllowances
        {
            RequestsLeftTurn = 39,
            RequestsLeftSession = 399,
            InquiriesLeftTurn = 29,
            InquiriesLeftSession = 299,
            ActionsLeftTurn = 11,
            ActionsLeftSession = 119,
            SecondsLeftTurn = 590,
            SecondsLeftSession = 5300,
            InputTokensLeftSession = 11800000,
            OutputTokensLeftSession = 1900000,
            OutputTokensPerReply = 16000,
            NoteMaxChars = 2000,
        };

        return new RichContextInputs
        {
            Observation = obs,
            Options = ActionCatalogue.Build(obs),
            Movement = ProbeMovement(c),
            History = history,
            HistoryDropped = 0,
            Memories = memories,
            PriorResults = priors,
            PriorDropped = 0,
            LatestInquiryResult = c.InquiryChars > 0
                ? (string.IsNullOrEmpty(c.Sentinel)
                    ? ProbeInquiry(c.InquiryChars)
                    : c.Sentinel + "_INQUIRY " + ProbeInquiry(c.InquiryChars))
                : null,
            Note = "Hold the western line and keep a food reserve for winter.",
            Persona = c.UsePersona ? LlmPersona.ShuAggressive : null,
            AttackEnabled = false,
            Attacks = null,
            Version = PromptBuilder.CombinedVersionFor(true, false, null, false, true, true, true, true),
            SystemChars = c.SystemChars,
            RequestsLeft = 39,
            ActionsLeft = 11,
            Allowances = allowances,
            SpatialOptions = new SpatialContextOptions
            {
                Level = SpatialDetailLevel.Rich,
                ProviderCharLimit = PromptSizeProbeLimit,
            },
            ReservedChars = c.ReservedChars,
        };
    }

    private static EventView ProbeEvent(int seq, string sentinel = null)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "army_id", "army-own-" + (seq % 5).ToString(CultureInfo.InvariantCulture) },
            { "from", (seq % 26).ToString(CultureInfo.InvariantCulture) + ","
                + (seq % 17).ToString(CultureInfo.InvariantCulture) },
            { "to", ((seq + 3) % 26).ToString(CultureInfo.InvariantCulture) + ","
                + ((seq + 5) % 17).ToString(CultureInfo.InvariantCulture) },
            { "result", "accepted" },
        };
        var rec = new EventRecord
        {
            Seq = seq,
            GameId = "probe-game",
            Season = 60 - (seq / 10),
            Phase = "Resolution",
            ActorSeatId = "seat-1",
            ControllerType = ControllerType.Llm,
            EventType = "army_moved",
            Visibility = "owner_and_observers",
            Payload = payload,
            SourceActionId = "act-" + seq.ToString("000", CultureInfo.InvariantCulture),
            Args = null,
            Text = ProbeFill(60, seq)
                + (string.IsNullOrEmpty(sentinel) ? "" : " " + sentinel + "_EVENT"),
        };
        return new EventView(rec, new Dictionary<string, string>(payload), null, rec.Text);
    }

    private static string ProbeInquiry(int chars)
    {
        if (chars <= 0) return null;
        const string prefix = "INQ ";
        return prefix + ProbeFill(Math.Max(0, chars - prefix.Length), 900);
    }

    private static string ProbeFill(int length, int seed)
    {
        if (length <= 0) return "";
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789 ";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(alphabet[(seed * 7 + i * 13) % alphabet.Length]);
        return sb.ToString();
    }

    private static string PromptSizeProbeHash(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
        var sb = new StringBuilder(64);
        for (int i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
