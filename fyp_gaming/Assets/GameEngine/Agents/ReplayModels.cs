using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    public enum ReplayPositionVerificationKind
    {
        DirectObservation,
        AcceptedCommandInference,
        UnverifiedSceneSetup
    }

    public enum VisibleMarchRuleProvenance
    {
        LegacyUnenforced,
        SharedEngineEstablished,
        LlmHostOnlyHistorical,
        Unestablished
    }

    public enum AssaultTroopRatioRuleProvenance
    {
        Established,
        HistoricalOldRulePreserved,
        Unestablished
    }

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
        public IReadOnlyList<string> MissingFields;
    }

    public sealed class ReplaySeasonScoreboard
    {
        public int Season;
        public IReadOnlyList<ReplaySeatScore> Scores;
    }

    public sealed class ReplaySummaryData
    {
        public string RunId;
        public string ScenarioId;
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
        public int TotalInvalid;
        public bool? AttackEnabled;
        public bool? ResearchEnabled;
        public bool? GarrisonConversionEnabled;
        public bool? DiplomacyEnabled;
        public bool? VisibleMarchRoutesEnforced;
        public bool? AssaultTroopRatioEnforced;
        public string SpatialContextLevel;
        public IReadOnlyList<ReplaySeasonScoreboard> Scoreboards;
        public IReadOnlyList<ReplayTurnSummary> Turns;
    }

    public sealed class ReplayRequestRecord
    {
        public string RunId;
        public string ScenarioId;
        public int Seed;
        public string Provider;
        public string Model;
        public string PromptVersion;
        public string SchemaHash;
        public string SeatId;
        public string ControllerType;
        public int TurnToken;
        public int Season;
        public string ObservationHash;
        public string ObservationJson;
        public int RequestIndex;
        public int LatencyMs;
        public int InputTokens;
        public int OutputTokens;
        public string RawModelText;
        public bool ParseOk;
        public string CommandJson;
        public bool ResultOk;
        /// <summary>True only when the record itself carried result_ok; ResultOk is otherwise derived.</summary>
        public bool ResultRecorded;
        public string ResultError;
        /// <summary>Recorded inquiry_tool (empty when the record is not an inquiry or the field is absent).</summary>
        public string InquiryTool;
        /// <summary>Recorded bounded inquiry result size; present when the runner wrote an inquiry result.</summary>
        public int? InquiryResultChars;
        /// <summary>Recorded observation events included in the prompt (from context_report or completeness section).</summary>
        public int? RecordedObservationEventsIncluded;
        /// <summary>Recorded observation events available before truncation (from context_report or completeness section).</summary>
        public int? RecordedObservationEventsAvailable;
        public string StateAfter;
        public string ErrorKind;
        public string ErrorDetail;
        public string ProviderRequestId;
        public string FinishReason;
        public int ReasoningTokens;
        /// <summary>
        /// Provider-reported reasoning-token count exactly as recorded; null when the provider reported
        /// none (ReasoningTokens reads 0 in that case, which is not a measurement).
        /// </summary>
        public int? ReasoningTokensReported;
        /// <summary>Recorded length of the provider's separate reasoning stream; null when absent.</summary>
        public int? ReasoningContentChars;
        /// <summary>
        /// Debug only. The provider's own reasoning stream, when the exchange record carried it in its raw
        /// body and it parsed. It is not the model's explanation, is unverified, and may contain seat-private
        /// context, so no default view shows it. Null when not recorded.
        /// </summary>
        public string ProviderReasoningText;
        public int TotalTokens;
        public int HttpStatus;
        public int TotalRequests;

        public string ExchangeId;
        public string UserPrompt;
        public string PromptExcerpt;
        public string ResponseContentText;
        public string RecordedExplanation;

        public bool IsProviderFailure
            => !string.IsNullOrEmpty(ErrorKind) && !string.Equals(ErrorKind, "None", StringComparison.Ordinal);

        public bool IsEmptyResponse
            => !IsProviderFailure && !ParseOk && string.IsNullOrWhiteSpace(RawModelText);
    }

    public sealed class ReplayFeedEvent
    {
        public int Sequence;
        public string RunId;
        public string SeatId;
        public string Faction;
        public int Season;
        public int RequestIndex;
        public string ActionType;
        public string ActionSummary;
        public string CommandJson;
        public bool ResultOk;
        public string ResultText;
        /// <summary>The model's own written explanation: a statement, not verified private reasoning.</summary>
        public string Explanation;
        /// <summary>Provider-reported reasoning-token count for the request; null when none was reported.</summary>
        public int? ReasoningTokens;
        public string ObservationHash;

        /// <summary>
        /// The one-line display of the model's statement and the provider's reasoning-token count, always
        /// labelled as a statement. It never presents either as verified private reasoning.
        /// </summary>
        public string StatementText()
        {
            string tokens = ReasoningTokens.HasValue
                ? "reasoning tokens: " + ReasoningTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "reasoning tokens: not reported";
            return string.IsNullOrEmpty(Explanation) || Explanation == "unknown"
                ? "model statement: none recorded [" + tokens + "]"
                : "model statement: \"" + Explanation + "\" [" + tokens + "]";
        }
    }

    public sealed class ReplayBundle
    {
        public ReplaySummaryData Summary;
        public IReadOnlyList<ReplayRequestRecord> Requests;
        public ReplayBootstrapData Bootstrap;
        public string SetupSource;
        public string MapSource;
        public string SummaryPath;
        public string JsonlPath;
        public string ExchangesPath;
        public string BootstrapPath;
        public bool? BootstrapVisibleMarchRoutes;
        public bool? BootstrapStateRequireVisibleMarchRoutes;
        public bool? BootstrapAssaultTroopRatio;
        public bool? BootstrapStateRequireAssaultTroopRatio;
    }

    public sealed class ReplayPositionCheck
    {
        public string EntityId;
        public string EntityKind;
        public string OwnerSeatId;
        public int? ExpectedX;
        public int? ExpectedZ;
        public int ActualX;
        public int ActualZ;
        public bool Matched;
        public bool VerifiedByLogs;
        public ReplayPositionVerificationKind VerificationKind;
        public string VerificationBasis;
        public string Note;
    }

    public sealed class ReplayComparisonReport
    {
        public string RunId;
        public string ScenarioId;
        public int Seed;
        public string BalanceProfileKey;
        public string SetupSource;
        public string MapSource;
        public int SeasonsVerified;
        public int TotalRequests;

        public int ObservationHashesMatched;
        public int ObservationHashMismatches;
        public IReadOnlyList<string> ObservationHashDiscrepancies;

        public int ExpectedAcceptedActions;
        public int AcceptedActionsReplayed;
        public int ExpectedRejectedActions;
        public int RejectedActionsReplayed;
        public int ExpectedEngineRejections;
        public int EngineRejectionsReplayed;
        public int ExpectedInvalidResponses;
        public int InvalidResponsesReplayed;
        public int ExpectedProviderFailures;
        public int ProviderFailuresReplayed;
        public int UnexpectedRejections;
        public int UnexpectedAcceptances;
        public int RejectionReasonMismatches;
        public int ActionOutcomeMismatches;
        public IReadOnlyList<string> ActionDiscrepancies;

        public int ExpectedVoluntaryEndTurns;
        public int VoluntaryEndTurnsReplayed;

        public int WeiFinalFood;
        public int WeiFinalGold;
        public int WeiExpectedFood;
        public int WeiExpectedGold;
        public int WeiFinalPop;
        public int WeiExpectedPop;
        public int WeiFinalGarrison;
        public int WeiExpectedGarrison;

        public int ShuFinalFood;
        public int ShuFinalGold;
        public int ShuExpectedFood;
        public int ShuExpectedGold;
        public int ShuFinalPop;
        public int ShuExpectedPop;
        public int ShuFinalGarrison;
        public int ShuExpectedGarrison;

        public bool FinalScoreboardExactMatch;
        public bool AllSeasonsScoreboardExactMatch;
        public IReadOnlyList<string> RawScoreboardMismatches;

        public int DirectObservationCount;
        public int DirectObservationMatched;
        public int CommandInferredCount;
        public int CommandInferredMatched;
        public int UnverifiedSceneSetupCount;
        public IReadOnlyList<ReplayPositionCheck> PositionChecks;

        public IReadOnlyList<string> ProposedFloatingPointNotes;
        public IReadOnlyList<string> DisclosedDependencies;
        public bool IsIncompleteRun;
        public string StopReason;
        public int FinalRecordedSeason;
        public string FinalRecordedSeatId;
        public VisibleMarchRuleProvenance VisibleMarchRuleProvenance;
        public bool VisibleMarchRuleEstablished = true;
        public string VisibleMarchRuleNote;
        public AssaultTroopRatioRuleProvenance AssaultTroopRatioProvenance;
        public bool AssaultTroopRatioRuleEstablished = true;
        public bool? EstablishedAssaultTroopRatio;
        public string AssaultTroopRatioRuleNote;
        public bool RawExactMatch;
        public bool ExactVerificationPassed => RawExactMatch;

        public ReplaySeatScore WeiExpectedScore;
        public ReplaySeatScore ShuExpectedScore;
        public ReplaySeatScore WeiFinalScore;
        public ReplaySeatScore ShuFinalScore;

        public string FormatReport()
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("=== LUANSHI OFFLINE REPLAY COMPARISON REPORT ===");
            string sSeed = Seed != 0 ? Seed.ToString() : "unknown";
            sb.AppendLine($"Run ID: {RunId} | Seed: {sSeed} | Profile: {BalanceProfileKey}");
            if (!string.IsNullOrEmpty(SetupSource) || !string.IsNullOrEmpty(MapSource))
                sb.AppendLine($"Setup Source: {SetupSource ?? "unknown"} | Map Source: {MapSource ?? "unknown"}");
            if (IsIncompleteRun)
                sb.AppendLine($"Incomplete / Paused Run: stopped at Season {FinalRecordedSeason} ({FinalRecordedSeatId ?? "unknown"}), reason: {StopReason ?? "unknown"}");
            sb.AppendLine($"Seasons Verified: {SeasonsVerified} | Total Requests: {TotalRequests}");
            sb.AppendLine($"Observation Hashes (raw SHA-256): {ObservationHashesMatched}/{TotalRequests} matched ({ObservationHashMismatches} mismatches)");
            sb.AppendLine($"Accepted Actions:   {AcceptedActionsReplayed}/{ExpectedAcceptedActions} matched (replayed rejected: {RejectedActionsReplayed}, unexpected rejections: {UnexpectedRejections}, unexpected acceptances: {UnexpectedAcceptances}, reason mismatches: {RejectionReasonMismatches})");
            sb.AppendLine($"Non-Accepted Breakdown: engine rejected {EngineRejectionsReplayed}/{ExpectedEngineRejections} | invalid/empty {InvalidResponsesReplayed}/{ExpectedInvalidResponses} | provider failure {ProviderFailuresReplayed}/{ExpectedProviderFailures}");
            sb.AppendLine($"Voluntary EndTurns: {VoluntaryEndTurnsReplayed}/{ExpectedVoluntaryEndTurns} matched");
            sb.AppendLine($"Action Outcome Mismatches: {ActionOutcomeMismatches}");
            sb.AppendLine();
            sb.AppendLine($"--- SCOREBOARD COMPARISON (Season {SeasonsVerified} Final) ---");
            string weiF = FormatReportScoreField(WeiFinalFood, WeiExpectedFood, WeiExpectedScore, "food");
            string weiG = FormatReportScoreField(WeiFinalGold, WeiExpectedGold, WeiExpectedScore, "gold");
            string weiP = FormatReportScoreField(WeiFinalPop, WeiExpectedPop, WeiExpectedScore, "population");
            string weiGar = FormatReportScoreField(WeiFinalGarrison, WeiExpectedGarrison, WeiExpectedScore, "garrison");
            sb.AppendLine($"Wei:  Food {weiF} | Gold {weiG} | Pop {weiP} | Garrison {weiGar}");

            string shuF = FormatReportScoreField(ShuFinalFood, ShuExpectedFood, ShuExpectedScore, "food");
            string shuG = FormatReportScoreField(ShuFinalGold, ShuExpectedGold, ShuExpectedScore, "gold");
            string shuP = FormatReportScoreField(ShuFinalPop, ShuExpectedPop, ShuExpectedScore, "population");
            string shuGar = FormatReportScoreField(ShuFinalGarrison, ShuExpectedGarrison, ShuExpectedScore, "garrison");
            sb.AppendLine($"Shu:  Food {shuF} | Gold {shuG} | Pop {shuP} | Garrison {shuGar}");
            sb.AppendLine($"Final Scoreboard Match: {(FinalScoreboardExactMatch ? "MATCH (all deltas 0)" : "MISMATCH (raw deltas present)")}");
            sb.AppendLine($"All Seasons Scoreboard Match: {(AllSeasonsScoreboardExactMatch ? "MATCH" : "MISMATCH")}");
            if (RawScoreboardMismatches != null && RawScoreboardMismatches.Count > 0)
            {
                sb.AppendLine("Raw Scoreboard Deltas:");
                foreach (var m in RawScoreboardMismatches) sb.AppendLine("  * " + m);
            }
            sb.AppendLine();
            sb.AppendLine("--- ENTITY POSITION VERIFICATION ---");
            sb.AppendLine($"Summary: Direct Observations {DirectObservationMatched}/{DirectObservationCount} | Command Inferred {CommandInferredMatched}/{CommandInferredCount} | Unverified Scene Setup {UnverifiedSceneSetupCount}");
            sb.AppendLine(string.Format("{0,-14} {1,-6} {2,-7} {3,-9} {4,-9} {5,-10} {6}", "Entity", "Kind", "Owner", "Expected", "Actual", "Status", "Basis & Notes"));
            sb.AppendLine(new string('-', 90));
            if (PositionChecks != null)
            {
                foreach (var pc in PositionChecks)
                {
                    string exp = (pc.ExpectedX.HasValue && pc.ExpectedZ.HasValue) ? $"({pc.ExpectedX.Value},{pc.ExpectedZ.Value})" : "n/a (fog)";
                    string act = $"({pc.ActualX},{pc.ActualZ})";
                    string status = pc.VerificationKind == ReplayPositionVerificationKind.UnverifiedSceneSetup
                        ? "UNVERIFIED"
                        : (pc.Matched ? "MATCH" : "MISMATCH");
                    string basisAndNote = string.IsNullOrEmpty(pc.Note) ? pc.VerificationBasis : $"{pc.VerificationBasis}; {pc.Note}";
                    sb.AppendLine(string.Format("{0,-14} {1,-6} {2,-7} {3,-9} {4,-9} {5,-10} {6}",
                        pc.EntityId, pc.EntityKind, pc.OwnerSeatId ?? "neutral", exp, act, status, basisAndNote));
                }
            }
            sb.AppendLine();
            sb.AppendLine("--- PROPOSED FLOATING-POINT DIAGNOSTIC (SEPARATE FROM RAW DIFF) ---");
            if (ProposedFloatingPointNotes != null)
            {
                foreach (var note in ProposedFloatingPointNotes)
                    sb.AppendLine("  * " + note);
            }
            sb.AppendLine();
            sb.AppendLine("--- DISCLOSED RUN DEPENDENCIES & LIMITATIONS ---");
            if (DisclosedDependencies != null)
            {
                foreach (var dep in DisclosedDependencies)
                    sb.AppendLine("  * " + dep);
            }
            if (!string.IsNullOrEmpty(VisibleMarchRuleNote))
                sb.AppendLine("  * Visible March Rule: " + VisibleMarchRuleNote);
            if (!string.IsNullOrEmpty(AssaultTroopRatioRuleNote))
                sb.AppendLine("  * Assault Troop Ratio Rule: " + AssaultTroopRatioRuleNote);
            sb.AppendLine();
            sb.AppendLine($"OVERALL RAW VERIFICATION: {(RawExactMatch ? "PASS" : "FAIL (RAW MISMATCHES REPORTED HONESTLY)")}");
            sb.AppendLine("================================================");
            return sb.ToString();
        }

        private static string FormatReportScoreField(int act, int exp, ReplaySeatScore expScore, string field)
        {
            if (expScore == null) return $"{act} (exp unknown, delta unknown)";
            if (expScore.MissingFields != null)
            {
                for (int i = 0; i < expScore.MissingFields.Count; i++)
                    if (string.Equals(expScore.MissingFields[i], field, StringComparison.Ordinal))
                        return $"{act} (exp unknown, delta unknown)";
            }
            int d = act - exp;
            return $"{act} (exp {exp}, delta {d})";
        }
    }
}
