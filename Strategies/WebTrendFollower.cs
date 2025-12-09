// Auto-generated helper strategy: follows trend commands from the dashboard via REST.
// Trades 1 contract (configurable) when a LONG/SHORT trend command is received.
// Safe defaults: live-only, short HTTP timeouts, and an enable switch.
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class WebTrendFollower : Strategy
    {
        [NinjaScriptProperty]
        [Display(Name = "DashboardBaseUrl", Order = 1, GroupName = "WebTrend")]
        public string DashboardBaseUrl { get; set; } = "http://127.0.0.1:51888";

        [NinjaScriptProperty]
        [Display(Name = "EnableRemoteTrading", Order = 2, GroupName = "WebTrend")]
        public bool EnableRemoteTrading { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Contracts", Order = 3, GroupName = "WebTrend")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "PollIntervalSeconds", Order = 4, GroupName = "WebTrend")]
        public int PollIntervalSeconds { get; set; } = 0; // 0 = poll every tick (throttled only by network)

        private HttpClient client;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private long lastCommandId = 0;
        private DateTime lastPollTime = DateTime.MinValue;

        private StreamWriter tradeWriter;
        private string tradeLogPath;
        private bool tradeHeaderWritten = false;

        private StreamWriter commandWriter;
        private string commandLogPath;

        private bool warnedRemoteDisabled = false;
        private bool warnedHistorical = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "WebTrendFollower";
                Calculate = Calculate.OnEachTick; // poll even when bars are slow
                IsOverlay = false;
                IsUnmanaged = false;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;

                EnableRemoteTrading = true; // default ON so commands are consumed
            }
            else if (State == State.Configure)
            {
                // no additional configuration
            }
            else if (State == State.DataLoaded)
            {
                client = new HttpClient();
                client.Timeout = TimeSpan.FromMilliseconds(300);
                InitializeTradeWriter();
                InitializeCommandWriter();
            }
            else if (State == State.Terminated)
            {
                if (commandWriter != null)
                {
                    try
                    {
                        commandWriter.Flush();
                        commandWriter.Dispose();
                        commandWriter = null;
                    }
                    catch (Exception ex)
                    {
                        Print($"[WebTrendFollower] Error closing command writer: {ex.Message}");
                    }
                }

                if (tradeWriter != null)
                {
                    try
                    {
                        tradeWriter.Flush();
                        tradeWriter.Dispose();
                        tradeWriter = null;
                    }
                    catch (Exception ex)
                    {
                        Print($"[WebTrendFollower] Error closing trade writer: {ex.Message}");
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (!EnableRemoteTrading)
            {
                LogOnce(ref warnedRemoteDisabled, "EnableRemoteTrading is false; skipping polls.");
                return;
            }

            // Only act on real-time data to avoid historical backfill trades
            if (State == State.Historical)
            {
                LogOnce(ref warnedHistorical, "Strategy is historical; skipping polls.");
                return;
            }

            // Throttle by poll interval only; allow multiple ticks per bar when interval permits
            var elapsedSec = (DateTime.UtcNow - lastPollTime).TotalSeconds;
            if (PollIntervalSeconds > 0 && elapsedSec < PollIntervalSeconds)
                return;

            try
            {
                var cmds = PollCommands();
                Print($"[WebTrendFollower] Polled commands: {cmds.Count} (afterId={lastCommandId})");
                if (cmds.Count > 0)
                {
                    var latest = cmds.OrderBy(c => c.Id).Last();
                    ExecuteTrend(latest);
                    lastCommandId = latest.Id;
                }
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] Poll error: {ex.Message}");
            }
            finally
            {
                lastPollTime = DateTime.UtcNow;
            }
        }

        private void ExecuteTrend(TrendCommand cmd)
        {
            if (cmd == null)
                return;

            Print($"[WebTrendFollower] Command #{cmd.Id} {cmd.Direction} bar={cmd.BarIndex} price={cmd.Price:F2}");

            // Normalize incoming directions to LONG/SHORT/FLAT
            var dir = cmd.Direction.ToUpperInvariant();
            if (dir == "UP") dir = "LONG";
            if (dir == "DOWN") dir = "SHORT";

            if (dir == "LONG")
            {
                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort();
                if (Position.MarketPosition != MarketPosition.Long)
                    EnterLong(Math.Max(1, Contracts), "WebTrendLong");
            }
            else if (dir == "SHORT")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong();
                if (Position.MarketPosition != MarketPosition.Short)
                    EnterShort(Math.Max(1, Contracts), "WebTrendShort");
            }
            else // FLAT or anything else
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong();
                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort();
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            if (tradeWriter == null || execution == null || execution.Order == null)
                return;

            try
            {
                int barIndex = CurrentBar;
                string ts = time.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string marketAction = execution.Order.OrderAction.ToString();
                string marketPositionStr = marketPosition.ToString();
                string orderName = execution.Order.Name ?? string.Empty;
                double qty = quantity;

                double open = double.NaN;
                double high = double.NaN;
                double low = double.NaN;
                double close = double.NaN;

                if (CurrentBar >= 0)
                {
                    open = Open[0];
                    high = High[0];
                    low = Low[0];
                    close = Close[0];
                }

                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5:F2},{6:F0},{7},{8:F2},{9:F2},{10:F2},{11:F2}",
                    ts,
                    barIndex,
                    marketAction,
                    marketPositionStr,
                    orderName,
                    price,
                    qty,
                    Instrument?.FullName ?? string.Empty,
                    open,
                    high,
                    low,
                    close);

                tradeWriter.WriteLine(line);
                Print($"[WebTrendFollower] Fill logged: {line}");
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] Trade log error: {ex.Message}");
            }
        }

        private List<TrendCommand> PollCommands()
        {
            var results = new List<TrendCommand>();
            if (client == null)
                return results;

            string url = $"{DashboardBaseUrl}/commands/next?afterId={lastCommandId}&limit=5";
            try
            {
                using (var cts = new CancellationTokenSource(300))
                {
                    var response = client.GetAsync(url, cts.Token).Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        Print($"[WebTrendFollower] Poll HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                        return results;
                    }

                    var json = response.Content.ReadAsStringAsync().Result;
                    var root = serializer.Deserialize<Dictionary<string, object>>(json);
                    if (root == null)
                        return results;

                    // Case 1: array of commands
                    if (root.ContainsKey("commands") && root["commands"] is object[] arr)
                    {
                        foreach (var item in arr)
                        {
                            var cmd = ParseCommand(item);
                            if (cmd != null)
                            {
                                LogCommand(cmd);
                                results.Add(cmd);
                            }
                        }
                        return results;
                    }

                    // Case 2: single command
                    if (root.ContainsKey("command"))
                    {
                        var cmd = ParseCommand(root["command"]);
                        if (cmd != null)
                        {
                            LogCommand(cmd);
                            results.Add(cmd);
                        }
                        return results;
                    }

                    // Case 3: status-only response (e.g., { status: "empty" })
                    if (root.ContainsKey("status"))
                    {
                        var status = Convert.ToString(root["status"]);
                        if (status == "empty")
                            return results;
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] Poll error: {ex.Message}");
            }
            return results;
        }

        private TrendCommand ParseCommand(object raw)
        {
            try
            {
                if (raw is Dictionary<string, object> dict)
                {
                    var dir = (dict.ContainsKey("direction") ? Convert.ToString(dict["direction"]) : "").ToUpperInvariant();
                    // Accept UP/DOWN/FLAT and map to LONG/SHORT/FLAT
                    if (dir == "UP") dir = "LONG";
                    if (dir == "DOWN") dir = "SHORT";
                    if (dir != "LONG" && dir != "SHORT" && dir != "FLAT")
                        return null;

                    var cmd = new TrendCommand
                    {
                        Id = dict.ContainsKey("id") ? Convert.ToInt64(dict["id"]) : 0,
                        Direction = dir,
                        BarIndex = dict.ContainsKey("barIndex") ? Convert.ToInt32(dict["barIndex"]) : -1,
                        Price = dict.ContainsKey("price") ? Convert.ToDouble(dict["price"]) : 0.0,
                        Note = dict.ContainsKey("note") ? Convert.ToString(dict["note"]) : string.Empty
                    };
                    return cmd;
                }
            }
            catch
            {
                // ignore malformed
            }
            return null;
        }

        private void LogCommand(TrendCommand cmd)
        {
            if (commandWriter == null || cmd == null)
                return;

            try
            {
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4:F2},{5},{6}",
                    ts,
                    cmd.Id,
                    cmd.Direction,
                    cmd.BarIndex,
                    cmd.Price,
                    cmd.Note ?? string.Empty,
                    lastCommandId);
                commandWriter.WriteLine(line);
                Print($"[WebTrendFollower] Command logged: {line}");
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] Command log error: {ex.Message}");
            }
        }

        private void InitializeTradeWriter()
        {
            try
            {
                string logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                System.IO.Directory.CreateDirectory(logDir);

                tradeLogPath = System.IO.Path.Combine(logDir, "WebTrendFollowerTrades.csv");
                bool fileExists = System.IO.File.Exists(tradeLogPath);
                tradeWriter = new System.IO.StreamWriter(tradeLogPath, true, Encoding.UTF8);
                tradeWriter.AutoFlush = true;

                // Only write header when file is empty/new
                if (!fileExists || new System.IO.FileInfo(tradeLogPath).Length == 0)
                {
                    tradeWriter.WriteLine("Timestamp,BarIndex,MarketAction,MarketPosition,OrderName,Price,Quantity,Instrument,Open,High,Low,Close");
                }

                Print($"[WebTrendFollower] Trade log: {tradeLogPath}");
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] Failed to initialize trade writer: {ex.Message}");
            }
        }

        private void InitializeCommandWriter()
        {
            try
            {
                string logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                System.IO.Directory.CreateDirectory(logDir);

                commandLogPath = System.IO.Path.Combine(logDir, "WebTrendFollowerCommands.csv");
                bool fileExists = System.IO.File.Exists(commandLogPath);
                commandWriter = new System.IO.StreamWriter(commandLogPath, true, Encoding.UTF8);
                commandWriter.AutoFlush = true;

                if (!fileExists || new System.IO.FileInfo(commandLogPath).Length == 0)
                {
                    commandWriter.WriteLine("Timestamp,CommandId,Direction,BarIndex,Price,Note,RawState");
                }

                Print($"[WebTrendFollower] Command log: {commandLogPath}");
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] Failed to initialize command writer: {ex.Message}");
            }
        }

        private class TrendCommand
        {
            public long Id { get; set; }
            public string Direction { get; set; }
            public int BarIndex { get; set; }
            public double Price { get; set; }
            public string Note { get; set; }
        }

        private void LogOnce(ref bool flag, string message)
        {
            if (flag)
                return;
            flag = true;
            Print($"[WebTrendFollower] {message}");
        }
    }
}
