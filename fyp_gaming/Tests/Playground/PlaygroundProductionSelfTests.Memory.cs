using System;
using System.IO;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Disk-backed memory lifecycle through the production runner and fake transport.</summary>
    internal static partial class PlaygroundSelfTests
    {
        private static void ProductionMemoryPersistsAndRecallsForOneSeat(TestLog log)
        {
            string directory = Path.Combine(Path.GetTempPath(), "luanshi-memory-lifecycle-"
                + Guid.NewGuid().ToString("N"));
            try
            {
                GameEngine engine = ProductionEngine();
                var firstStore = new LocalLlmMemoryStore(directory);
                var firstFake = new FakeLlmTransport();
                firstFake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-a\",\"summary\":\"secure bridge\"}", 0, 100, 10);
                firstFake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
                var firstRunner = new LlmTurnRunner(firstFake, RichConfig(), null, firstStore);
                var firstHost = new ProductionHost(engine, "seat-a");
                firstRunner.Begin(firstHost, firstHost.Token, "seat-a", ProdSeed);
                DrainProduction(firstRunner);

                bool actionAccepted = firstRunner.AcceptedActionsThisTurn == 1;
                bool actionMemoryPersisted = false;
                foreach (LlmMemory memory in firstStore.Load(ProdGame, "seat-a"))
                    if (memory.Source == "engine_action" && memory.Content == "fortify city-a was accepted")
                        actionMemoryPersisted = true;

                // Plant matching records under a different seat and game. Neither may enter the
                // fresh runner's prompt or the result returned by its recall_memory tool.
                firstStore.Append(LlmMemoryExtractor.FromSummary(ProdGame, "seat-b", 1,
                    "fortify city-a FOREIGN-SEAT-MEMORY"));
                firstStore.Append(LlmMemoryExtractor.FromSummary(ProdGame + "-other", "seat-a", 1,
                    "fortify city-a FOREIGN-GAME-MEMORY"));

                // Advance to a later season and create both a new store and runner, exercising disk reload.
                engine.BeginSeason();
                var freshStore = new LocalLlmMemoryStore(directory);
                var secondFake = new FakeLlmTransport();
                secondFake.EnqueueSuccess("{\"tool\":\"recall_memory\",\"query\":\"fortify city-a\",\"limit\":5}", 0, 100, 10);
                secondFake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
                var secondRunner = new LlmTurnRunner(secondFake, RichConfig(), null, freshStore);
                var secondHost = new ProductionHost(engine, "seat-a");
                secondRunner.Begin(secondHost, secondHost.Token, "seat-a", ProdSeed);
                DrainProduction(secondRunner);

                string firstPrompt = secondFake.Requests.Count > 0 ? secondFake.Requests[0].User : "";
                string recallPrompt = secondFake.Requests.Count > 1 ? secondFake.Requests[1].User : "";
                string recallPayload = DelimitedSection(recallPrompt,
                    PromptBuilder.InquiryResultBeginMarker, PromptBuilder.InquiryResultEndMarker);
                bool ownMemoryReloaded = false;
                foreach (LlmMemory memory in secondRunner.Memories)
                    if (memory.Source == "engine_action" && memory.Content == "fortify city-a was accepted")
                        ownMemoryReloaded = true;

                log.Check(actionAccepted && actionMemoryPersisted
                          && firstRunner.EndReason == EndReason.ModelEndedTurn && firstFake.CallCount == 2,
                    "production memory: an accepted engine action is written to the local store");
                log.Check(ownMemoryReloaded && !firstPrompt.Contains("FOREIGN-SEAT-MEMORY")
                          && !firstPrompt.Contains("FOREIGN-GAME-MEMORY"),
                    "production memory: a fresh runner reloads only this game's seat memories");
                log.Check(secondRunner.EndReason == EndReason.ModelEndedTurn && secondFake.CallCount == 2
                          && secondRunner.InquiriesThisTurn == 1
                          && HasExpectedRecallHit(recallPayload)
                          && !recallPayload.Contains("FOREIGN-SEAT-MEMORY")
                          && !recallPayload.Contains("FOREIGN-GAME-MEMORY"),
                    "production memory: the recall_memory response payload contains this seat's persisted hit only",
                    "state=" + secondRunner.State + " reason=" + secondRunner.EndReason + "/" + secondRunner.EndReasonText
                        + " inquiries=" + secondRunner.InquiriesThisTurn + " requests=" + secondFake.CallCount
                        + " payload=" + recallPayload);
                string wrongPayload = recallPayload.Replace(
                    "\"content\":\"fortify city-a was accepted\"",
                    "\"content\":\"fortify city-a was rejected\"");
                log.Check(!HasExpectedRecallHit("") && !HasExpectedRecallHit(wrongPayload),
                    "production memory: the recall assertion rejects an absent or incorrect tool result");
            }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (Exception) { }
            }
        }

        private static string DelimitedSection(string text, string beginMarker, string endMarker)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(beginMarker) || string.IsNullOrEmpty(endMarker))
                return "";
            int begin = text.IndexOf(beginMarker, StringComparison.Ordinal);
            if (begin < 0) return "";
            begin += beginMarker.Length;
            int end = text.IndexOf(endMarker, begin, StringComparison.Ordinal);
            if (end < 0) return "";
            return text.Substring(begin, end - begin).Trim();
        }

        private static bool HasExpectedRecallHit(string payload)
            => !string.IsNullOrEmpty(payload)
                && payload.Contains("\"tool\":\"recall_memory\"")
                && payload.Contains("\"source\":\"engine_action\"")
                && payload.Contains("\"content\":\"fortify city-a was accepted\"");
    }
}
