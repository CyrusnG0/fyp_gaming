using System;
using System.Collections.Generic;

namespace LuanShi.Playground
{
    internal sealed class PlaygroundAssertion
    {
        public string Name;
        public bool Ok;
        public string Detail;

        public PlaygroundAssertion() { }
        public PlaygroundAssertion(string name, bool ok, string detail = null)
        {
            Name = name; Ok = ok; Detail = detail;
        }
    }

    internal sealed class PlaygroundInquiryRecord
    {
        public int RequestIndex;
        public string SeatId;
        public int Season;
        public string Tool;
        public string RequestJson;
        public string ResultJson;
        public int ResultChars;
        public bool Ok;
        public string ErrorCode;
        public bool Truncated;
        public long ElapsedMs;
        public int CpBefore;
        public int CpAfter;
        public long ActionAdmissionsBefore;
        public long ActionAdmissionsAfter;
        public int RetainedIndex = -1;
    }

    internal sealed class PlaygroundNoteRecordView
    {
        public int RequestIndex;
        public string SeatId;
        public int Season;
        public string Event;
        public string Text;
        public int Chars;
    }

    internal sealed class PlaygroundActionRecord
    {
        public int RequestIndex;
        public string SeatId;
        public int Season;
        public string Type;
        public string CommandJson;
        public bool CheckRequested;
        public bool CheckRan;
        public bool CheckOk;
        public string CheckError;
        public bool ReplayMatched;
        public string ReplayStateHash;
        public string LiveStateHashBeforeCheck;
        public string LiveStateHashAfterCheck;
        public string LiveStateHashBeforeExecute;
        public string LiveStateHashAfterExecute;
        public bool Executed;
        public bool Accepted;
        public string EngineError;
        public string FeedbackShown;
        public int CpBefore;
        public int CpAfter;
        public long ActionAdmissionsBefore;
        public long ActionAdmissionsAfter;
    }

    internal sealed class PlaygroundTurnRecord
    {
        public string SeatId;
        public int Season;
        public int TurnToken;
        public string StateHashBefore;
        public string StateHashAfter;
        public string NoteShown;
        public string NoteSaved;
        public string EndReason;
        public int RequestStartIndex;
        public int RequestCount;
        public int CpBefore;
        public int CpAfter;
        public int InquiryCount;
        public int ActionCount;
    }

    /// <summary>One request's exact model-visible context plus the pre-rendered prompt components.</summary>
    internal sealed class PlaygroundRequestRecord
    {
        public int Index;
        public string SeatId;
        public int Season;
        public int TurnToken;
        public string PromptVersion;
        public string PromptBaseVersion;
        public string PromptAddendumVersion;
        public string SchemaHash;
        public int Seed;
        public string System;
        public string User;
        public string ObservationJson;
        public string OfferedActions;
        public int OptionCount;
        public List<string> MemoryLines = new List<string>();
        public List<string> PriorInquiryResults = new List<string>();
        public string NoteShown;
        public string RemainingJson;
        public string ConfigJson;
        public string Admission;
        public string ResponseKind;
        public string ResponseText;
        public long? InputTokens;
        public long? OutputTokens;
        public int LatencyMs;
        public long ElapsedMs;
        public string Outcome;
        public string Detail;
    }

    /// <summary>Everything a run exposes: readable trace source plus machine-readable comparison data.</summary>
    internal sealed class PlaygroundRunRecord
    {
        public const string SchemaVersion = "playground-v1";

        public string Kind = "fake-session";
        public string RunId;
        public string Provider;
        public bool Fake;
        public int Seed;
        public string GameId;
        public string ScenarioId;
        public string PromptBaseVersion;
        public string PromptAddendumVersion;
        public string ConfigJson;
        public string StartStateHash;
        public string EndStateHash;
        public string BudgetJson;
        public List<PlaygroundTurnRecord> Turns = new List<PlaygroundTurnRecord>();
        public List<PlaygroundRequestRecord> Requests = new List<PlaygroundRequestRecord>();
        public List<PlaygroundInquiryRecord> Inquiries = new List<PlaygroundInquiryRecord>();
        public List<PlaygroundActionRecord> Actions = new List<PlaygroundActionRecord>();
        public List<PlaygroundNoteRecordView> Notes = new List<PlaygroundNoteRecordView>();
        public List<PlaygroundActionRecord> Checks = new List<PlaygroundActionRecord>();
        public List<PlaygroundAssertion> Assertions = new List<PlaygroundAssertion>();
        public List<string> Errors = new List<string>();

        public int AcceptedActions
        {
            get { int n = 0; foreach (PlaygroundActionRecord a in Actions) if (a.Accepted) n++; return n; }
        }

        public int RejectedActions
        {
            get { int n = 0; foreach (PlaygroundActionRecord a in Actions) if (a.Executed && !a.Accepted) n++; return n; }
        }

        public PlaygroundRequestRecord FindRequest(int index)
        {
            foreach (PlaygroundRequestRecord request in Requests)
                if (request.Index == index) return request;
            return null;
        }

        public PlaygroundTurnRecord FindTurn(string seatId, int season)
        {
            foreach (PlaygroundTurnRecord turn in Turns)
                if (turn.SeatId == seatId && turn.Season == season) return turn;
            return null;
        }

        public PlaygroundAssertion Check(bool ok, string name, string detail = null)
        {
            var assertion = new PlaygroundAssertion(name, ok, detail);
            Assertions.Add(assertion);
            return assertion;
        }
    }
}
