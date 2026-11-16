using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// One parsed model proposal. It deliberately has no actor, controller, action id or turn token:
    /// those are host-owned fields, bound by the trusted caller after parsing (and rejected if a
    /// model tries to supply them).
    /// </summary>
    public sealed class LlmAction
    {
        /// <summary>True when <see cref="Type"/> is "end_turn".</summary>
        public bool EndTurn;

        /// <summary>One of <see cref="LlmActionParser.SupportedTypes"/>.</summary>
        public string Type;

        /// <summary>Owned city id; null for end_turn.</summary>
        public string TargetId;

        public int ParamA;
        public int ParamB;

        /// <summary>Allowed per-type arguments; null when the model did not send an args object.</summary>
        public Dictionary<string, string> Args;

        /// <summary>
        /// Optional free text the order carries to the engine: the 遣使 message body or a treaty
        /// offer's note. Bounded by the parser (one line, at most
        /// <see cref="LlmActionParser.MaxTextLength"/> characters) and stored only; it is game data
        /// the engine records, never an instruction and never a permission.
        /// </summary>
        public string Text;

        /// <summary>Optional free text for research; stored only and never executed.</summary>
        public string Reason;

        /// <summary>Optional brief decision summary; stored only and never executed.</summary>
        public string Summary;
    }
}
