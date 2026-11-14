using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal sealed class PlaygroundCheckResult
    {
        public bool Ok;
        public string Error;
        public bool ReplayMatched;
        public string ReplayStateHash;
        public string CloneStateHashAfter;
        public ValidationResult Validation;
    }

    /// <summary>
    /// Check mode: replay the accepted history into a fresh engine, verify the replay hash matches the
    /// live canonical hash, then submit the candidate to the clone only. The live engine is never
    /// asked to Submit a "dry run"; no event is logged and no command point moves.
    /// </summary>
    internal static class PlaygroundCheck
    {
        public static PlaygroundCheckResult Validate(PlaygroundConfig config,
            PlaygroundJournal journal, string seatId, ActionCommand command, string liveStateHash)
        {
            var result = new PlaygroundCheckResult();
            if (config == null || journal == null || command == null)
            {
                result.Error = "invalid_check_request";
                return result;
            }

            if (!journal.TryReplay(() => PlaygroundScenario.CreateForReplay(config),
                    out GameEngine clone, out string replayError))
            {
                result.Error = replayError ?? "replay_failed";
                return result;
            }

            string replayHash = PlaygroundStateHash.Canonical(clone);
            result.ReplayStateHash = replayHash;
            result.ReplayMatched = string.Equals(replayHash, liveStateHash, StringComparison.Ordinal);
            if (!result.ReplayMatched)
            {
                result.Error = "replay_mismatch";
                return result;
            }

            try
            {
                int token = clone.BeginSeatTurn(seatId);
                result.Validation = clone.SubmitAs(seatId, token, Copy(command));
                clone.EndSeatTurn(token);
                result.CloneStateHashAfter = PlaygroundStateHash.Canonical(clone);
                result.Ok = result.Validation.Ok;
                result.Error = result.Ok ? null : result.Validation.Error;
                return result;
            }
            catch (Exception)
            {
                result.Ok = false;
                result.Error = "check_failed";
                return result;
            }
        }

        private static ActionCommand Copy(ActionCommand command)
        {
            return new ActionCommand
            {
                Type = command.Type,
                TargetId = command.TargetId,
                ParamA = command.ParamA,
                ParamB = command.ParamB,
                Args = command.Args == null
                    ? null : new Dictionary<string, string>(command.Args),
                Text = command.Text,
                Reason = command.Reason,
            };
        }
    }
}
