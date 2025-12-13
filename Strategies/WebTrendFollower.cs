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
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;

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

        [NinjaScriptProperty]
        [Display(Name = "FixedStopPoints", Order = 5, GroupName = "WebTrend")]
        public double FixedStopPoints { get; set; } = 10; // default 10 points fixed stop

        [NinjaScriptProperty]
        [Display(Name = "EnableTrailingStops", Order = 6, GroupName = "WebTrend")]
        public bool EnableTrailingStops { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "TrailTriggerPoints", Order = 7, GroupName = "WebTrend")]
        public double TrailTriggerPoints { get; set; } = 10; // how far price must move in favor before trail starts

        [NinjaScriptProperty]
        [Display(Name = "TrailStepPoints", Order = 8, GroupName = "WebTrend")]
        public double TrailStepPoints { get; set; } = 5; // tighten stop to this distance off the best excursion

        [NinjaScriptProperty]
        [Display(Name = "LockProfitPercent", Order = 9, GroupName = "WebTrend")]
        public double LockProfitPercent { get; set; } = 0.66; // lock 66% of MFE

        [NinjaScriptProperty]
        [Display(Name = "EnableLocalTrendMirror", Order = 10, GroupName = "WebTrend")]
        public bool EnableLocalTrendMirror { get; set; } = false; // mirror candles.html detection locally (no orders)

        [NinjaScriptProperty]
        [Display(Name = "MirrorScanBars", Order = 11, GroupName = "WebTrend")]
        public int MirrorScanBars { get; set; } = 300; // how many recent bars to scan for mirrored logic

        [NinjaScriptProperty]
        [Display(Name = "ShowChartMarkers", Order = 12, GroupName = "WebTrend")]
        public bool ShowChartMarkers { get; set; } = true; // draw simple entry/exit markers

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

        private double entryPrice = double.NaN;
        private MarketPosition entrySide = MarketPosition.Flat;
        private double highestHighSinceEntry = double.NaN;
        private double lowestLowSinceEntry = double.NaN;
        private double initialStopPrice = double.NaN;
        private double currentStopPrice = double.NaN;
        private bool entryOrderWorking = false;
        private string activeEntrySignal = null;


        private class MirrorTrend
        {
            public string Direction { get; set; }
            public int StartBar { get; set; }
            public int EndBar { get; set; }
            public double Change { get; set; }
            public double SlopePerBar { get; set; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "WebTrendFollower";
                Calculate = Calculate.OnEachTick; // poll even when bars are slow
                IsOverlay = true; // draw markers on the price panel
                IsUnmanaged = false;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries; // avoid stacking entries
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

            // Manage trailing stops intrabar so we can ratchet without waiting for a new command
            ManageTrailingStops();

            // Optionally mirror the lightweight trend detection logic from candles.html (analytics only)
            RunLocalTrendMirror();
        }

        private void RunLocalTrendMirror()
        {
            if (!EnableLocalTrendMirror)
                return;

            // Need enough bars to analyze
            int maxBars = Math.Max(2, MirrorScanBars);
            int available = CurrentBar + 1;
            int take = Math.Min(available, maxBars);
            if (take < 2)
                return;

            var closes = new List<double>(take);
            // Oldest -> newest
            for (int offset = take - 1; offset >= 0; offset--)
                closes.Add(Close[offset]);

            var candidates = AnalyzeCandidateTrendsMirror(closes);
            if (candidates.Count == 0)
                return;

            var last = candidates[candidates.Count - 1];
            Print($"[WebTrendFollower] Mirror trend: dir={last.Direction} start={last.StartBar} end={last.EndBar} change={last.Change:P4} slopePB={last.SlopePerBar:E4}");
        }

        private List<MirrorTrend> AnalyzeCandidateTrendsMirror(List<double> closes)
        {
            var result = new List<MirrorTrend>();
            if (closes == null || closes.Count < 2)
                return result;

            int minRun = 2;
            double minChange = 0.0005; // 0.05%
            double minSlopePerBar = 0.00001;

            int n = closes.Count;
            int startBarBase = CurrentBar - (n - 1);

            int i = 0;
            while (i + minRun <= n)
            {
                var baseSeg = closes.GetRange(i, minRun);
                var baseReg = CalcLinearRegression(baseSeg);
                var baseDir = baseReg.Slope > 0 ? "UP" : baseReg.Slope < 0 ? "DOWN" : null;
                double baseChange = (baseSeg[minRun - 1] - baseSeg[0]) / baseSeg[0];
                double slopePerBar = Math.Abs(baseReg.Slope) / Math.Max(1e-12, baseSeg[0]);

                if (baseDir == null || Math.Abs(baseChange) < minChange || slopePerBar < minSlopePerBar)
                {
                    i++;
                    continue;
                }

                int end = i + minRun;
                while (end < n)
                {
                    var extSeg = closes.GetRange(i, end - i + 1);
                    var extReg = CalcLinearRegression(extSeg);
                    var extDir = extReg.Slope > 0 ? "UP" : extReg.Slope < 0 ? "DOWN" : null;
                    double extChange = (extSeg[extSeg.Count - 1] - extSeg[0]) / extSeg[0];
                    double extSlopePB = Math.Abs(extReg.Slope) / Math.Max(1e-12, extSeg[0]);

                    if (extDir != baseDir || Math.Abs(extChange) < minChange || extSlopePB < minSlopePerBar * 0.5)
                        break;
                    end++;
                }

                int finalEnd = end - 1;
                var finalSeg = closes.GetRange(i, finalEnd - i + 1);
                var finalReg = CalcLinearRegression(finalSeg);
                double finalChange = (finalSeg[finalSeg.Count - 1] - finalSeg[0]) / finalSeg[0];
                double finalSlopePB = Math.Abs(finalReg.Slope) / Math.Max(1e-12, finalSeg[0]);

                result.Add(new MirrorTrend
                {
                    Direction = baseDir,
                    StartBar = startBarBase + i,
                    EndBar = startBarBase + finalEnd,
                    Change = finalChange,
                    SlopePerBar = finalSlopePB
                });

                i = end;
            }

            // Fallback pass if nothing found (more lenient)
            if (result.Count == 0)
            {
                double fbMinChange = 0.0001;
                double fbMinSlopePB = 0.0;
                int j = 0;
                while (j + minRun <= n)
                {
                    var seg = closes.GetRange(j, minRun);
                    var reg = CalcLinearRegression(seg);
                    var dir = reg.Slope > 0 ? "UP" : reg.Slope < 0 ? "DOWN" : null;
                    double chg = (seg[minRun - 1] - seg[0]) / seg[0];
                    double slopePB = Math.Abs(reg.Slope) / Math.Max(1e-12, seg[0]);

                    if (dir == null || Math.Abs(chg) < fbMinChange || slopePB < fbMinSlopePB)
                    {
                        j++;
                        continue;
                    }

                    int k = j + minRun;
                    while (k < n)
                    {
                        var ext = closes.GetRange(j, k - j + 1);
                        var extReg = CalcLinearRegression(ext);
                        var extDir = extReg.Slope > 0 ? "UP" : extReg.Slope < 0 ? "DOWN" : null;
                        double extChange = (ext[ext.Count - 1] - ext[0]) / ext[0];
                        double extSlopePB = Math.Abs(extReg.Slope) / Math.Max(1e-12, ext[0]);
                        if (extDir != dir || Math.Abs(extChange) < fbMinChange || extSlopePB < fbMinSlopePB)
                            break;
                        k++;
                    }

                    result.Add(new MirrorTrend
                    {
                        Direction = dir,
                        StartBar = startBarBase + j,
                        EndBar = startBarBase + (k - 1),
                        Change = (closes[k - 1] - closes[j]) / closes[j],
                        SlopePerBar = Math.Abs(reg.Slope) / Math.Max(1e-12, seg[0])
                    });

                    j = k;
                }
            }

            return result;
        }

        private (double Slope, double Intercept) CalcLinearRegression(List<double> values)
        {
            int n = values.Count;
            if (n == 0)
                return (0.0, 0.0);

            double sumX = 0.0, sumY = 0.0, sumXY = 0.0, sumX2 = 0.0;
            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = values[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-12)
                return (0.0, values[0]);

            double slope = (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;
            return (slope, intercept);
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

            if (entryOrderWorking)
            {
                var sig = string.IsNullOrEmpty(activeEntrySignal) ? "unknown" : activeEntrySignal;
                Print($"[WebTrendFollower] Entry already working ({sig}); skipping new command until prior entry resolves.");
                return;
            }

            // FLAT means flatten-only: never chain a new entry in the same poll
            if (dir == "FLAT")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong();
                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort();
                CancelStops();
                ResetStopState();
                return;
            }

            // If already on the requested side, do nothing (avoid adds)
            if (dir == "LONG" && Position.MarketPosition == MarketPosition.Long)
                return;
            if (dir == "SHORT" && Position.MarketPosition == MarketPosition.Short)
                return;

            if (dir == "LONG")
            {
                // Reversal is two-step: flatten now, enter on a later poll
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort();
                    CancelStops();
                    ResetStopState();
                    return;
                }
                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort();
                if (Position.MarketPosition != MarketPosition.Long)
                {
                    entryOrderWorking = true;
                    activeEntrySignal = "WebTrendLong";
                    EnterLong(Math.Max(1, Contracts), "WebTrendLong");
                }
            }
            else if (dir == "SHORT")
            {
                // Reversal is two-step: flatten now, enter on a later poll
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong();
                    CancelStops();
                    ResetStopState();
                    return;
                }
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong();
                if (Position.MarketPosition != MarketPosition.Short)
                {
                    entryOrderWorking = true;
                    activeEntrySignal = "WebTrendShort";
                    EnterShort(Math.Max(1, Contracts), "WebTrendShort");
                }
            }
        }

        protected override void OnOrderUpdate(Cbi.Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, Cbi.OrderState orderState, DateTime time, Cbi.ErrorCode error, string nativeError)
        {
            base.OnOrderUpdate(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, error, nativeError);

            if (order == null)
                return;

            var name = order.Name ?? string.Empty;
            var action = order.OrderAction;
            bool isEntry = (name == "WebTrendLong" && action == Cbi.OrderAction.Buy) || (name == "WebTrendShort" && action == Cbi.OrderAction.SellShort);

            if (!isEntry)
                return;

            if (orderState == Cbi.OrderState.Cancelled || orderState == Cbi.OrderState.Rejected)
            {
                entryOrderWorking = false;
                activeEntrySignal = null;
                Print("[WebTrendFollower] Entry order cleared (" + orderState + ")");
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            if (tradeWriter == null || execution == null || execution.Order == null)
                return;

            TrackStopsOnFill(execution, price, marketPosition);

            if (ShowChartMarkers)
            {
                DrawExecutionMarker(execution, marketPosition, price);
            }

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

        private void DrawExecutionMarker(Cbi.Execution execution, MarketPosition mp, double price)
        {
            try
            {
                string tagBase = "WTF_" + execution.Order?.OrderId + "_" + CurrentBar;
                if (execution.Order == null)
                    return;

                var action = execution.Order.OrderAction;
                bool isEntry = action == Cbi.OrderAction.Buy || action == Cbi.OrderAction.SellShort;
                bool isExit = action == Cbi.OrderAction.Sell || action == Cbi.OrderAction.BuyToCover;

                if (isEntry)
                {
                    var brush = action == Cbi.OrderAction.Buy ? Brushes.LimeGreen : Brushes.OrangeRed;
                    Draw.ArrowUp(this, tagBase + "_entry", false, 0, price, brush);
                }
                else if (isExit)
                {
                    var brush = Brushes.LightGray;
                    Draw.ArrowDown(this, tagBase + "_exit", false, 0, price, brush);
                }
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] DrawExecutionMarker error: {ex.Message}");
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

        private void TrackStopsOnFill(Cbi.Execution execution, double fillPrice, MarketPosition marketPosition)
        {
            // Reset when we flatten from any side
            if (marketPosition == MarketPosition.Flat)
            {
                ResetStopState();
                return;
            }

            if (execution.Order == null)
                return;

            var name = execution.Order.Name ?? string.Empty;
            var action = execution.Order.OrderAction;

            // Entry fill clears the in-flight flag so we don't stack entries
            entryOrderWorking = false;
            activeEntrySignal = null;

            // Only react to our managed entry orders
            if (name == "WebTrendLong" && action == Cbi.OrderAction.Buy)
            {
                ApplyInitialStop(true, fillPrice);
            }
            else if (name == "WebTrendShort" && action == Cbi.OrderAction.SellShort)
            {
                ApplyInitialStop(false, fillPrice);
            }
        }

        private void ApplyInitialStop(bool isLong, double fillPrice)
        {
            var dist = Math.Max(0.0001, FixedStopPoints);
            entrySide = isLong ? MarketPosition.Long : MarketPosition.Short;
            entryPrice = fillPrice;
            highestHighSinceEntry = isLong ? High[0] : double.NaN;
            lowestLowSinceEntry = isLong ? double.NaN : Low[0];
            var stop = isLong ? fillPrice - dist : fillPrice + dist;
            initialStopPrice = stop;
            currentStopPrice = stop;

            var signal = isLong ? "WebTrendLong" : "WebTrendShort";
            SetStopLoss(signal, CalculationMode.Price, stop, false);
            Print($"[WebTrendFollower] Initial stop {signal} at {stop:F2} (dist {dist:F2})");
        }

        private void ManageTrailingStops()
        {
            if (Position == null || entrySide == MarketPosition.Flat)
                return;

            if (double.IsNaN(entryPrice))
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ResetStopState();
                return;
            }

            if (!EnableTrailingStops)
                return;

            var trigger = Math.Max(0.0001, TrailTriggerPoints); // minimum favorable move before trailing
            var lockPct = Math.Max(0.0, Math.Min(1.0, LockProfitPercent));
            var epsilon = TickSize > 0 ? TickSize * 0.1 : 1e-6;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                highestHighSinceEntry = double.IsNaN(highestHighSinceEntry) ? High[0] : Math.Max(highestHighSinceEntry, High[0]);
                lowestLowSinceEntry = double.IsNaN(lowestLowSinceEntry) ? Low[0] : Math.Min(lowestLowSinceEntry, Low[0]);
                var move = highestHighSinceEntry - entryPrice; // MFE for long
                if (move >= trigger && move > 0)
                {
                    var candidate = entryPrice + move * lockPct;
                    var floor = initialStopPrice; // never loosen below initial protective stop
                    var newStop = Math.Max(candidate, floor);
                    if (double.IsNaN(currentStopPrice) || newStop > currentStopPrice + epsilon)
                    {
                        currentStopPrice = newStop;
                        SetStopLoss("WebTrendLong", CalculationMode.Price, newStop, false);
                        Print($"[WebTrendFollower] Trailing stop LONG -> {newStop:F2} (MFE {move:F2}, lock {lockPct:P0})");
                    }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                highestHighSinceEntry = double.IsNaN(highestHighSinceEntry) ? High[0] : Math.Max(highestHighSinceEntry, High[0]);
                lowestLowSinceEntry = double.IsNaN(lowestLowSinceEntry) ? Low[0] : Math.Min(lowestLowSinceEntry, Low[0]);
                var move = entryPrice - lowestLowSinceEntry; // MFE for short
                if (move >= trigger && move > 0)
                {
                    var candidate = entryPrice - move * lockPct;
                    var ceiling = initialStopPrice; // initial stop is above entry for shorts
                    var newStop = double.IsNaN(currentStopPrice) ? candidate : Math.Min(candidate, currentStopPrice);
                    newStop = Math.Min(newStop, ceiling); // never loosen higher than initial stop
                    if (double.IsNaN(currentStopPrice) || newStop < currentStopPrice - epsilon)
                    {
                        currentStopPrice = newStop;
                        SetStopLoss("WebTrendShort", CalculationMode.Price, newStop, false);
                        Print($"[WebTrendFollower] Trailing stop SHORT -> {newStop:F2} (MFE {move:F2}, lock {lockPct:P0})");
                    }
                }
            }
        }

        private void ResetStopState()
        {
            entryPrice = double.NaN;
            entrySide = MarketPosition.Flat;
            highestHighSinceEntry = double.NaN;
            lowestLowSinceEntry = double.NaN;
            initialStopPrice = double.NaN;
            currentStopPrice = double.NaN;
            entryOrderWorking = false;
            activeEntrySignal = null;
        }

        private void CancelStops()
        {
            try
            {
                // Use very wide values to effectively remove any working SetStopLoss orders
                SetStopLoss("WebTrendLong", CalculationMode.Price, double.MaxValue, false);
                SetStopLoss("WebTrendShort", CalculationMode.Price, double.MinValue, false);
                Print("[WebTrendFollower] Cancelled working stops (FLAT/reversal)");
            }
            catch (Exception ex)
            {
                Print($"[WebTrendFollower] CancelStops error: {ex.Message}");
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
