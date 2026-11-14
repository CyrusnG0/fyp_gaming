using LuanShi.Engine;

internal static partial class Harness
{
    private static void InquiryResearchDiplomacyReachesNextPrompt()
    {
        foreach (string topic in new[] { "research", "diplomacy" })
        {
            GameEngine engine = DiplomacyScenario();
            FactionState faction = engine.State.FindFaction("seat-a");
            faction.Controller = ControllerType.Llm;
            faction.ResearchPoints = 137;
            Check(engine.Submit(TreatyOrder("seat-b", "seat-a", TreatyType.Nap)).Ok,
                "[research/diplomacy] the fixture has an incoming treaty offer");
            var host = new LlmHarnessHost(engine, "seat-a");
            string before = engine.Observe("seat-a").ToJson();

            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"" + topic + "\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var runner = new LlmTurnRunner(fake, InquiryConfig(true), null);
            runner.Begin(host, host.Token, "seat-a", 99);
            LlmDrain(runner);

            string section = fake.Requests.Count == 2 ? InquirySection(fake.Requests[1].User) : null;
            JsonObject context = null;
            if (StrictJson.TryParse(section, out JsonNode json, out _) && json is JsonObject result
                && result.TryGet("payload", out JsonNode payloadNode) && payloadNode is JsonObject payload
                && payload.TryGet("current_context", out JsonNode contextNode))
                context = contextNode as JsonObject;

            Check(context != null && context.TryGet("seat_id", out JsonNode seat)
                && seat is JsonString id && id.Value == "seat-a"
                && (topic == "research" ? InquiryShowsResearchPoints(context) : InquiryShowsInboundProposal(context)),
                "[research/diplomacy] " + topic + " delivers the requesting seat's current facts to the next prompt");
            Check(runner.EndReason == EndReason.ModelEndedTurn && host.EndTurnCalls == 1
                && host.SubmitCalls == 0 && engine.Observe("seat-a").ToJson() == before,
                "[research/diplomacy] " + topic + " uses an inquiry request without submitting an order");
        }
    }

    private static bool InquiryShowsResearchPoints(JsonObject context)
        => context.TryGet("research_points", out JsonNode points) && points is JsonInt value && value.Value == 137;

    private static bool InquiryShowsInboundProposal(JsonObject context)
        => context.TryGet("proposals", out JsonNode proposals) && proposals is JsonArray items && items.Count == 1
           && items.Items[0] is JsonObject proposal && proposal.TryGet("direction", out JsonNode direction)
           && direction is JsonString value && value.Value == "inbound"
           && proposal.TryGet("counterparty_seat_id", out JsonNode counterparty)
           && counterparty is JsonString id && id.Value == "seat-b";
}
