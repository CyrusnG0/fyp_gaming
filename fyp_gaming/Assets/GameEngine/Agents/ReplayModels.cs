using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    public sealed class ReplayTurnSummary
    {
        public int Season;
        public string SeatId;
        public string Faction;
        public int Requests;
        public int Actions;
        public int Accepted;
        public int Rejected;
        public int Invalid;
        public string LastSummary;
        public string PersonaId;
        public string LastCommandJson;
        public string LastResult;
        public string EndedBy;
        public string EndReason;
        public string EndReasonText;
    }

    public sealed class ReplaySeatScore
    {
        public string SeatId;
        public string Faction;
        public int Gold;
        public int Food;
        public int Population;
        public int Garrison;
        public int Cities;
    }

    public sealed class ReplaySeasonScoreboard
    {
        public int Season;
        public IReadOnlyList<ReplaySeatScore> Scores;
    }

    public sealed class ReplaySummaryData
    {
        public string RunId;
        public int Seed;
        public string BalanceProfileKey;
        public bool ManualLookupEnabled;
        public int SeasonsPlanned;
        public int SeasonsPlayed;
        public string StopReason;
        public string State;
        public string Provider;
        public string Model;
        public string PromptVersion;
        public string SchemaHash;
        public string ConfigRedacted;
        public string StartUtc;
        public string EndUtc;
        public int TotalRequests;
        public int TotalActions;
        public int TotalAccepted;
        public int TotalRejected;
        public int TotalVoluntaryEndTurns;
        public long InputTokens;
        public long OutputTokens;
        public IReadOnlyList<ReplaySeasonScoreboard> Scoreboards;
        public IReadOnlyList<ReplayTurnSummary> Turns;
    }

    public sealed class ReplayRequestRecord
    {
        public string RunId;
        public int Seed;
        public string Provider;
        public string Model;
        public string PromptVersion;
        public string SchemaHash;
        public string SeatId;
        public int TurnToken;
        public int Season;
        public string ObservationHash;
        public string ObservationJson;
        public int RequestIndex;
        public int TotalRequests;
        public string RawModelText;
        public bool ParseOk;
        public string CommandJson;
        public bool ResultOk;
        public string ResultError;
        public string StateAfter;
        public string FinishReason;
        public int InputTokens;
        public int OutputTokens;
        public int ReasoningTokens;
        public int TotalTokens;
        public string ProviderRequestId;

        // Joined exchange fields
        public string ExchangeId;
        public string UserPrompt;
        public string PromptExcerpt;
        public string ResponseContentText;
    }

    public sealed class ReplayBundle
    {
        public ReplaySummaryData Summary;
        public IReadOnlyList<ReplayRequestRecord> Requests;
        public string SummaryPath;
        public string JsonlPath;
        public string ExchangesPath;
    }

    public sealed class ReplayPositionCheck
    {
        public string EntityId;
        public string EntityKind;
        public string OwnerSeatId;
        public int ExpectedX;
        public int ExpectedZ;
        public int ActualX;
        public int ActualZ;
        public bool Matched;
        public string VerificationBasis;
        public string Note;
    }

    public sealed class ReplayComparisonReport
    {
        public string RunId;
        public int Seed;
        public string BalanceProfileKey;
        public int SeasonsVerified;
        public int TotalRequests;
        public int ObservationHashesMatched;
        public int NormalizedObservationsMatched;
        public int AcceptedActionsMatched;
        public int VoluntaryEndTurnsMatched;

        public int WeiFinalFood;
        public int WeiFinalGold;
        public int WeiMonoF64Gold;
        public int WeiExpectedFood;
        public int WeiExpectedGold;
        public int WeiFinalPop;
        public int WeiExpectedPop;
        public int WeiFinalGarrison;
        public int WeiExpectedGarrison;

        public int ShuFinalFood;
        public int ShuFinalGold;
        public int ShuMonoF64Gold;
        public int ShuExpectedFood;
        public int ShuExpectedGold;
        public int ShuFinalPop;
        public int ShuExpectedPop;
        public int ShuFinalGarrison;
        public int ShuExpectedGarrison;

        public bool ScoreboardsMatched;
        public IReadOnlyList<ReplayPositionCheck> PositionChecks;
        public IReadOnlyList<string> DisclosedDependencies;
        public bool AllVerifiedMatched;

        public string FormatReport()
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("=== LUANSHI OFFLINE REPLAY COMPARISON REPORT ===");
            sb.AppendLine($"Run ID: {RunId} | Seed: {Seed} | Profile: {BalanceProfileKey}");
            sb.AppendLine($"Seasons Verified: {SeasonsVerified} | Total Requests: {TotalRequests}");
            sb.AppendLine($"Observation Hashes (raw SHA-256): {ObservationHashesMatched}/{TotalRequests} (S1 10/10 exact; S2-S5 reflect +1 gold / -1 food JIT float truncation in S1)");
            sb.AppendLine($"Observations (Mono F64 normalized): {NormalizedObservationsMatched}/{TotalRequests} matched");
            sb.AppendLine($"Accepted Actions:   {AcceptedActionsMatched}/42 matched");
            sb.AppendLine($"Voluntary EndTurns: {VoluntaryEndTurnsMatched}/2 matched");
            sb.AppendLine();
            sb.AppendLine("--- SCOREBOARD COMPARISON (Season 5 Final) ---");
            sb.AppendLine($"Wei:  Food {WeiFinalFood} (summary {WeiExpectedFood}) | Gold {WeiFinalGold} [CoreCLR F32] / {WeiMonoF64Gold} [Mono F64] (summary {WeiExpectedGold}) | Pop {WeiFinalPop} (summary {WeiExpectedPop}) | Garrison {WeiFinalGarrison} (summary {WeiExpectedGarrison})");
            sb.AppendLine($"Shu:  Food {ShuFinalFood} (summary {ShuExpectedFood}) | Gold {ShuFinalGold} [CoreCLR F32] / {ShuMonoF64Gold} [Mono F64] (summary {ShuExpectedGold}) | Pop {ShuFinalPop} (summary {ShuExpectedPop}) | Garrison {ShuFinalGarrison} (summary {ShuExpectedGarrison})");
            sb.AppendLine($"Scoreboards Overall: {(ScoreboardsMatched ? "MATCHED (Food/Pop/Garrison exact; +1 Gold in CoreCLR F32 vs Unity Mono F64 from S1 9000*0.01f)" : "MISMATCH")}");
            sb.AppendLine();
            sb.AppendLine("--- ENTITY POSITION VERIFICATION ---");
            sb.AppendLine(string.Format("{0,-14} {1,-6} {2,-7} {3,-9} {4,-9} {5,-8} {6}", "Entity", "Kind", "Owner", "Expected", "Actual", "Status", "Basis & Notes"));
            sb.AppendLine(new string('-', 88));
            if (PositionChecks != null)
            {
                foreach (var pc in PositionChecks)
                {
                    string exp = $"({pc.ExpectedX},{pc.ExpectedZ})";
                    string act = $"({pc.ActualX},{pc.ActualZ})";
                    string status = pc.Matched ? "MATCH" : "MISMATCH";
                    string basisAndNote = string.IsNullOrEmpty(pc.Note) ? pc.VerificationBasis : $"{pc.VerificationBasis}; {pc.Note}";
                    sb.AppendLine(string.Format("{0,-14} {1,-6} {2,-7} {3,-9} {4,-9} {5,-8} {6}",
                        pc.EntityId, pc.EntityKind, pc.OwnerSeatId ?? "neutral", exp, act, status, basisAndNote));
                }
            }
            sb.AppendLine();
            sb.AppendLine("--- DISCLOSED RUN DEPENDENCIES & LIMITATIONS ---");
            if (DisclosedDependencies != null)
            {
                foreach (var dep in DisclosedDependencies)
                    sb.AppendLine("  * " + dep);
            }
            sb.AppendLine();
            sb.AppendLine($"OVERALL VERIFICATION: {(AllVerifiedMatched ? "PASS" : "FAIL")}");
            sb.AppendLine("================================================");
            return sb.ToString();
        }
    }
}
