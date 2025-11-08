#region Using declarations
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.ComponentModel.DataAnnotations;

using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// NOTE: This strategy calls your local AI proxy (e.g., http://localhost:5265/ai/chat-openai?mode=live)
// Make sure your service is running and OPENAI_API_KEY is configured there.

namespace NinjaTrader.Custom.Strategies
{
    public class AiAssistedOptimizer : Strategy
    {
        // Configure your endpoint here
        private const string ServiceBase = "http://localhost:5265";
        private const string Endpoint = "/ai/chat-openai?mode=live"; // use ?mode=mock to test without OpenAI
		//private const string Endpoint = "/ai/chat-openai?mode=mock";

        private static readonly HttpClient Http = new HttpClient();
        private string lastAdvice = "(no advice yet)";
        private DateTime lastRequestTime = DateTime.MinValue;
        private bool inFlight = false;

        // Throttle AI calls
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "MinIntervalMinutes", Order = 0)]
        public int MinIntervalMinutes { get; set; } = 2;
        private TimeSpan minInterval => TimeSpan.FromMinutes(MinIntervalMinutes);

        // Optional diagnostics
        [NinjaScriptProperty]
        [Display(Name = "EnableDebugLogs", Order = 99)]
        public bool EnableDebugLogs { get; set; } = false;

        private CancellationTokenSource cts;

        // Example parameters you might optimize
        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "FastPeriod", Order = 1)]
        public int FastPeriod { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "SlowPeriod", Order = 2)]
        public int SlowPeriod { get; set; } = 50;

        static AiAssistedOptimizer()
        {
            try
            {
                Http.Timeout = TimeSpan.FromSeconds(30);
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("NT8-AI-Assisted-Optimizer/1.0");
            }
            catch { /* best effort */ }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AI-Assisted Optimizer (Advisory)";
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.Configure)
            {
                Http.Timeout = TimeSpan.FromSeconds(30);
            }
            else if (State == State.DataLoaded)
            {
                cts = new CancellationTokenSource();
            }
            else if (State == State.Terminated)
            {
                try { cts?.Cancel(); } catch { }
                try { cts?.Dispose(); } catch { }
            }
        }

        protected override void OnBarUpdate()
        {
            // Avoid AI calls during historical processing
            if (State != State.Realtime)
            {
                DrawAdvice();
                return;
            }

            if (!inFlight && DateTime.UtcNow - lastRequestTime >= minInterval)
            {
                lastRequestTime = DateTime.UtcNow;
                _ = RequestAdviceAsync(); // fire-and-forget
            }

            DrawAdvice();
        }

        private void DrawAdvice()
        {
            var font = new SimpleFont("Segoe UI", 12);
            Draw.TextFixed(
                this,
                "aiAdvice",
                string.IsNullOrWhiteSpace(lastAdvice) ? "(no advice yet)" : lastAdvice,
                TextPosition.TopLeft,
                Brushes.White,
                font,
                Brushes.Black,
                Brushes.Transparent,
                80);
        }

        private async Task RequestAdviceAsync()
        {
            if (cts == null || cts.IsCancellationRequested)
                return;

            inFlight = true;
            try
            {
                var payloadJson = BuildChatJson();
                var uri = BuildUri(ServiceBase, Endpoint);

                int maxAttempts = 2;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        using (var content = new StringContent(payloadJson, Encoding.UTF8, "application/json"))
                        using (var resp = await Http.PostAsync(uri, content, cts.Token))
                        {
                            var body = await resp.Content.ReadAsStringAsync();

                            if (!resp.IsSuccessStatusCode)
                            {
                                if (EnableDebugLogs)
                                    Print($"AI HTTP {resp.StatusCode}: {Truncate(body, 300)}");

                                if ((int)resp.StatusCode >= 500 && attempt < maxAttempts)
                                {
                                    await Task.Delay(GetBackoffDelay(attempt), cts.Token);
                                    continue;
                                }

                                UpdateAdviceSafe($"AI error {resp.StatusCode}: {Truncate(body, 300)}");
                                return;
                            }

                            var reply = TryExtractReply(body);
                            if (string.IsNullOrWhiteSpace(reply))
                                reply = body;

                            UpdateAdviceSafe(FormatAdvice(reply));
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        UpdateAdviceSafe("AI request canceled.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (EnableDebugLogs)
                            Print($"AI request attempt {attempt} failed: {ex.Message}");

                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(GetBackoffDelay(attempt), cts.Token);
                            continue;
                        }

                        UpdateAdviceSafe("AI request failed: " + ex.Message);
                        return;
                    }
                }
            }
            finally
            {
                inFlight = false;
            }
        }

        private string BuildChatJson()
        {
            // Compute stats manually to avoid missing API properties
            var trades = SystemPerformance.AllTrades;
            int count = trades.Count;
            int winners = trades.WinningTrades.Count;

            double netProfit = 0.0;
            double grossProfit = 0.0;
            double grossLoss = 0.0; // keep negative
            for (int i = 0; i < trades.Count; i++)
            {
                var t = trades[i];
                double pnl = t?.ProfitCurrency ?? 0.0;
                netProfit += pnl;
                if (pnl >= 0) grossProfit += pnl;
                else grossLoss += pnl; // negative
            }

            double winRate = count > 0 ? 100.0 * winners / count : 0.0;
            double avgTrade = count > 0 ? netProfit / count : 0.0;
            double profitFactor = (grossLoss < 0.0) ? (grossProfit / Math.Abs(grossLoss)) : 0.0;
            double maxDrawdown = ComputeMaxDrawdownFromTrades();

            string summary = $"EMA crossover style with Fast={FastPeriod}, Slow={SlowPeriod}.";
            string instr = Instrument?.FullName ?? "Unknown";
            string bars = BarsPeriod != null ? $"{BarsPeriod.Value} {BarsPeriod.BarsPeriodType}" : "Unknown bars";

            string userPrompt =
                $"Instrument={instr}, Bars={bars}. Params: Fast={FastPeriod}, Slow={SlowPeriod}. " +
                $"Recent trades={count}, WinRate={winRate:F1}%, AvgTrade={avgTrade:F2}, PF={profitFactor:F2}, MaxDD={maxDrawdown:F2}, Net={netProfit:F2}. " +
                $"Strategy summary: {summary} " +
                $"Suggest 3 concrete improvements (parameter ranges to test, risk controls) and walk-forward steps. Keep ~120 words.";

            string systemPrompt = "You help optimize NinjaTrader strategies. Prefer statistically sound, testable suggestions.";
            string model = "gpt-4o-mini";
            double temperature = 0.2;
            int maxTokens = 300;

            // Manual JSON to avoid external libraries
            string esc(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

            var json =
                "{"
                + $"\"model\":\"{esc(model)}\","
                + $"\"temperature\":{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)},"
                + $"\"max_tokens\":{maxTokens},"
                + "\"messages\":["
                + $"{{\"role\":\"system\",\"content\":\"{esc(systemPrompt)}\"}},"
                + $"{{\"role\":\"user\",\"content\":\"{esc(userPrompt)}\"}}"
                + "]"
                + "}";

            return json;
        }

        private double ComputeMaxDrawdownFromTrades()
        {
            try
            {
                var trades = SystemPerformance.AllTrades;
                double equity = 0.0;
                double peak = 0.0;
                double maxDD = 0.0;

                for (int i = 0; i < trades.Count; i++)
                {
                    var t = trades[i];
                    double pnl = t?.ProfitCurrency ?? 0.0;
                    equity += pnl;
                    if (equity > peak) peak = equity;
                    double dd = peak - equity;
                    if (dd > maxDD) maxDD = dd;
                }
                return maxDD;
            }
            catch
            {
                return 0.0;
            }
        }

        // Lightweight extraction supporting:
        // 1) { "reply": "..." }
        // 2) OpenAI-like: { "choices":[{ "message": { "content": "..." } }] }
        private static string TryExtractReply(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            var direct = ExtractJsonStringValue(body, "reply");
            if (!string.IsNullOrWhiteSpace(direct))
                return UnescapeBasic(direct);

            var content = ExtractFirstContentFromChoices(body);
            if (!string.IsNullOrWhiteSpace(content))
                return UnescapeBasic(content);

            var topContent = ExtractJsonStringValue(body, "content");
            if (!string.IsNullOrWhiteSpace(topContent))
                return UnescapeBasic(topContent);

            return null;
        }

        private static string ExtractFirstContentFromChoices(string json)
        {
            int choicesIdx = json.IndexOf("\"choices\"", StringComparison.OrdinalIgnoreCase);
            if (choicesIdx < 0) return null;

            int contentIdx = json.IndexOf("\"content\"", choicesIdx, StringComparison.OrdinalIgnoreCase);
            if (contentIdx < 0) return null;

            int startQuote = json.IndexOf('"', contentIdx + 9);
            if (startQuote < 0) return null;
            startQuote = json.IndexOf('"', startQuote + 1);
            if (startQuote < 0) return null;

            int endQuote = FindStringEnd(json, startQuote + 1);
            if (endQuote < 0) return null;

            return json.Substring(startQuote + 1, endQuote - startQuote - 1);
        }

        private static string ExtractJsonStringValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return null;

            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return null;

            int startQuote = json.IndexOf('"', colon + 1);
            if (startQuote < 0) return null;

            int endQuote = FindStringEnd(json, startQuote + 1);
            if (endQuote < 0) return null;

            return json.Substring(startQuote + 1, endQuote - startQuote - 1);
        }

        private static int FindStringEnd(string s, int startPos)
        {
            bool escape = false;
            for (int i = startPos; i < s.Length; i++)
            {
                char c = s[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') return i;
            }
            return -1;
        }

        private static string UnescapeBasic(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private void UpdateAdviceSafe(string text)
        {
            try
            {
                lastAdvice = string.IsNullOrWhiteSpace(text) ? "(empty AI reply)" : text.Trim();
                Print("AI Advice: " + lastAdvice);
            }
            catch { /* best effort */ }
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s.Substring(0, n) + "...");

        private static string FormatAdvice(string s) =>
            string.IsNullOrWhiteSpace(s) ? "(empty AI reply)" : s.Trim();

        private static Uri BuildUri(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            if (path.StartsWith("/")) path = path.Substring(1);
            return new Uri(baseUrl + path);
        }

        private static int GetBackoffDelay(int attempt)
        {
            int baseDelay = (int)Math.Pow(2, attempt) * 250; // 500ms, 1000ms, ...
            int jitter = new Random().Next(0, 250);
            return baseDelay + jitter;
        }
    }
}
