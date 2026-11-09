using System;
using System.IO;
using LuanShi.Engine;

internal static partial class DiagnosticsTests
{
    private static void RunProviderMetadataRedaction()
    {
        Section("capture: provider metadata and mixed-case credentials are redacted");
        string body = Envelope("", FakeKey, true, 1, 1, 0)
            .Replace("chatcmpl-diag", FakeKey);
        using (var stub = new StubServer(body))
        {
            var config = Config(stub.BaseUrl, 3000);
            config.MaxInvalidPerTurn = 1;
            var run = RunTurn(new OpenAiChatTransport(config), config, "metadata-redaction");
            try
            {
                string evidence = ExchangeText(run.Dir);
                foreach (string file in Directory.GetFiles(run.Dir, "*.jsonl"))
                    evidence += File.ReadAllText(file);
                Check(!evidence.Contains(FakeKey),
                    "provider id, finish reason and diagnostics do not expose the configured key");
                Check(evidence.Contains("[redacted]"), "redaction remains visible in evidence");
            }
            finally { DeleteDir(run.Dir); }
        }
        string filtered = LlmResponseDiagnostics.RedactForCapture(
            "bEaReR abc.def.ghi and SK-UPPER-SECRET", null);
        Check(!filtered.Contains("abc.def.ghi") && !filtered.Contains("UPPER-SECRET"),
            "mixed-case bearer and key markers are removed");
    }
}
