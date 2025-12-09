#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Net.Http;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Minimal strategy that logs OHLC to CSV at the first tick of each new bar.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class BareOhlcLogger : Strategy
    {
        private StreamWriter csvWriter;
        private string csvFilePath;
        private bool csvHeaderWritten = false;
        private HashSet<int> loggedBars = new HashSet<int>(); // Track which bars have been logged

        // Log writer for detailed output (like GradientSlope)
        private StreamWriter logWriter;
        private string logFilePath;
        private bool writerInitialized = false;  // Prevent multiple initializations
        private static HttpClient sharedClient;
        private static readonly object clientLock = new object();
        private DateTime lastCommandPollUtc = DateTime.MinValue;
        private readonly TimeSpan commandPollInterval = TimeSpan.FromSeconds(1);
        private int lastCommandIdHandled = -1;
        private DateTime lastEmptyCommandLogUtc = DateTime.MinValue;
        private readonly TimeSpan emptyLogInterval = TimeSpan.FromSeconds(30);

        #region Bar Index Labels (copied from GradientSlopeStrategy)
        // Toggle to draw bar index labels above each bar on the chart.
        private bool showBarIndexLabels = true;
        #endregion

        #region Helper Methods
        
        /// <summary>
        /// Helper method to classify a candle based on its open/close relationship
        /// Called on first tick of bar to analyze the previously completed bar
        /// </summary>
        /// <param name="open">Open price of the completed bar</param>
        /// <param name="close">Close price of the completed bar</param>
        /// <returns>"good" for green/bullish, "bad" for red/bearish, "good and bad" for doji/neutral</returns>
        private string ClassifyCandle(double open, double close)
        {
            const double dojiThreshold = 0.0001; // Very small threshold for doji detection
            double difference = Math.Abs(close - open);
            
            if (difference <= dojiThreshold)
            {
                return "good and bad"; // Doji or very small body
            }
            else if (close > open)
            {
                return "good"; // Green/Bullish candle
            }
            else
            {
                return "bad"; // Red/Bearish candle
            }
        }
        
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BareOhlcLogger";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                EnableDashboardStreaming = false;
                DashboardBaseUrl = "http://127.0.0.1:51888";
                EnableCommandPolling = false;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize files once when data is loaded - this runs only once
                InitializeCsvWriter();
                EnsureHttpClient();
            }
            else if (State == State.Terminated)
            {
                // Close log writer first
                if (logWriter != null)
                {
                    try
                    {
                        LogOutput("Strategy terminated.");
                        logWriter.Flush();
                        logWriter.Dispose();
                        logWriter = null;
                    }
                    catch (Exception ex)
                    {
                        Print($"[BareOhlcLogger] Error closing log writer: {ex.Message}");
                    }
                }

                // Close CSV writer - explicitly flush and dispose to release file lock
                if (csvWriter != null)
                {
                    try
                    {
                        csvWriter.Flush();
                        csvWriter.Dispose();  // Dispose to ensure file is released
                        csvWriter = null;
                        Print($"[BareOhlcLogger] CSV writer closed and disposed.");
                    }
                    catch (Exception ex)
                    {
                        Print($"[BareOhlcLogger] Error closing CSV writer: {ex.Message}");
                    }
                }
                
                // Reset initialization flag so next run can create new files
                writerInitialized = false;
            }
        }

        private void InitializeCsvWriter()
        {
            // Only initialize once - prevent multiple file creations
            if (writerInitialized || csvWriter != null)
            {
                Print($"[BareOhlcLogger] InitializeCsvWriter called but already initialized. writerInitialized={writerInitialized}, csvWriter={csvWriter != null}");
                return;
            }
            
            Print($"[BareOhlcLogger] InitializeCsvWriter starting...");
            writerInitialized = true;

            // Use the same path pattern as GradientSlopeStrategy
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "NinjaTrader 8", "bin", "Custom", "strategy_logs");
            
            try
            {
                Directory.CreateDirectory(logDir);
            }
            catch (Exception ex)
            {
                Print($"[BareOhlcLogger] Failed to create directory {logDir}: {ex.Message}");
                return;
            }

            // Create filename with timestamp including milliseconds to avoid conflicts
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            csvFilePath = Path.Combine(logDir, $"BareOhlcLogger_{Instrument.FullName}_{timestamp}.csv");
            
            try
            {
                csvWriter = new StreamWriter(csvFilePath, false);
                csvWriter.AutoFlush = true;  // Auto-flush for real-time file updates
                csvHeaderWritten = false;
                Print($"[BareOhlcLogger] CSV Log: {csvFilePath}");
            }
            catch (Exception ex)
            {
                Print($"[BareOhlcLogger] Failed to initialize CSV writer: {ex.Message}");
            }

            // Initialize dedicated log output file (like GradientSlope)
            logFilePath = Path.Combine(logDir, $"BareOhlcLogger_{Instrument.FullName}_{timestamp}.log");
            try
            {
                logWriter = new StreamWriter(logFilePath, false);
                logWriter.AutoFlush = true;  // Auto-flush for real-time viewing
                LogOutput("=".PadRight(80, '='));
                LogOutput($"BareOhlcLogger Strategy Log Started: {DateTime.Now}");
                LogOutput($"Instrument: {Instrument.FullName}");
                LogOutput($"Calculate: {Calculate}");
                LogOutput("=".PadRight(80, '='));
                Print($"[BareOhlcLogger] Log File: {logFilePath}");
            }
            catch (Exception ex)
            {
                Print($"[BareOhlcLogger] Failed to initialize log writer: {ex.Message}");
            }
        }

        protected override void OnBarUpdate()
        {
            TryPollDashboardCommand();

            // Safety check - writer should be initialized
            if (csvWriter == null)
            {
                LogOutput($"OnBarUpdate called but csvWriter is null. CurrentBar={CurrentBar}");
                return;
            }

            // Need a completed prior bar to log
            if (CurrentBar < 1)
            {
                return;
            }

            // Only log each completed bar once
            try
            {
                // Write header on first bar
                if (!csvHeaderWritten)
                {
                    csvWriter.WriteLine("Timestamp,Bar,Open,High,Low,Close,Volume,PNL,CandleType,BodyPct,UpperWick,LowerWick,Bid,Ask,Spread,Instrument,TickSize");
                    csvHeaderWritten = true;
                    Print($"[BareOhlcLogger] Header written");
                }

                var barIndex = CurrentBar - 1; // index of the completed bar
                
                // Check if this bar has already been logged
                if (loggedBars.Contains(barIndex))
                {
                    return; // Skip logging - already processed this bar
                }
                
                // Mark this bar as logged
                loggedBars.Add(barIndex);
                
                var ts = Times[0][1];
                
                // Calculate PNL (Close - Open) and classify candle
                var pnl = Close[1] - Open[1];
                var candleType = ClassifyCandle(Open[1], Close[1]);

                double range = High[1] - Low[1];
                double bodyPct = range > 0 ? (Close[1] - Open[1]) / range : 0;
                double upperWick = High[1] - Math.Max(Open[1], Close[1]);
                double lowerWick = Math.Min(Open[1], Close[1]) - Low[1];

                // Current bid/ask (may be NaN on some data feeds)
                double bid = GetCurrentBid();
                double ask = GetCurrentAsk();
                double spread = (double.IsNaN(bid) || double.IsNaN(ask)) ? double.NaN : ask - bid;

                var line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss},{1},{2:F5},{3:F5},{4:F5},{5:F5},{6},{7:F5},{8},{9:F6},{10:F5},{11:F5},{12:F5},{13:F5},{14:F5},{15},{16:F5}",
                    ts,
                    barIndex,
                    Open[1],
                    High[1],
                    Low[1],
                    Close[1],
                    Volume[1],
                    pnl,
                    candleType,
                    bodyPct,
                    upperWick,
                    lowerWick,
                    bid,
                    ask,
                    spread,
                    Instrument?.FullName ?? "",
                    TickSize);

                csvWriter.WriteLine(line);
                LogOutput($"Bar {barIndex}: O={Open[1]:F2} H={High[1]:F2} L={Low[1]:F2} C={Close[1]:F2} PNL={pnl:F2} Type={candleType} [LOGGED ONCE]");

                // Optional: stream bar to dashboard for live candles.html
                if (EnableDashboardStreaming && sharedClient != null)
                {
                    SendBarToDashboard(barIndex, ts, Open[1], High[1], Low[1], Close[1], Volume[1]);
                }
            }
            catch (Exception ex)
            {
                // Log error but keep strategy running
                Print($"[BareOhlcLogger] Error writing to CSV: {ex.Message}");
            }

            // Draw bar index labels on the chart
            if (showBarIndexLabels && CurrentBar >= 0)
            {
                // Draw the label for the current bar
                string tag = "BarLabel_" + CurrentBar;
                double yPosition = High[0] + (6 * TickSize);
                Draw.Text(this, tag, CurrentBar.ToString(), 0, yPosition, Brushes.Black);
            }
        }

        private void LogOutput(string message)
        {
            if (logWriter != null)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    logWriter.WriteLine($"[{timestamp}] {message}");
                }
                catch (Exception ex)
                {
                    Print($"[BareOhlcLogger] Log Error: {ex.Message}");
                }
            }
        }

        private void EnsureHttpClient()
        {
            if (sharedClient == null)
            {
                lock (clientLock)
                {
                    if (sharedClient == null)
                    {
                        sharedClient = new HttpClient();
                        sharedClient.Timeout = TimeSpan.FromMilliseconds(300);
                        sharedClient.DefaultRequestHeaders.ConnectionClose = false;
                    }
                }
            }
        }

        private void SendBarToDashboard(int barIndex, DateTime ts, double open, double high, double low, double close, double volume)
        {
            try
            {
                // Prepare minimal JSON payload for the /diag endpoint
                var json = new StringBuilder();
                json.Append("{");
                json.Append("\"barIndex\":").Append(barIndex).Append(',');
                json.Append("\"time\":\"").Append(ts.ToString("o")).Append("\",");
                json.Append("\"open\":").Append(open.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                json.Append("\"high\":").Append(high.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                json.Append("\"low\":").Append(low.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                json.Append("\"close\":").Append(close.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                json.Append("\"volume\":").Append(volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
                json.Append("}");

                var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
                var cts = new CancellationTokenSource(300);
                var url = DashboardBaseUrl.TrimEnd('/') + "/diag";
                sharedClient.PostAsync(url, content, cts.Token).ContinueWith(t =>
                {
                    // silent on success/failure; dashboard is optional
                });
            }
            catch
            {
                // Ignore send errors to avoid impacting strategy
            }
        }

        private void TryPollDashboardCommand()
        {
            if (!EnableCommandPolling || sharedClient == null)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (nowUtc - lastCommandPollUtc < commandPollInterval)
            {
                return; // Throttle polling to avoid hammering the server
            }

            lastCommandPollUtc = nowUtc;

            try
            {
                using (var cts = new CancellationTokenSource(300))
                {
                    var url = DashboardBaseUrl.TrimEnd('/') + "/commands/next";
                    var response = sharedClient.GetAsync(url, cts.Token).GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        // Log occasionally if polling is enabled but failing
                        var now = DateTime.UtcNow;
                        if (now - lastEmptyCommandLogUtc > emptyLogInterval)
                        {
                            lastEmptyCommandLogUtc = now;
                            Print($"[BareOhlcLogger] Command poll HTTP {(int)response.StatusCode}");
                        }
                        return;
                    }

                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    ProcessCommandJson(json);
                }
            }
            catch (Exception ex)
            {
                Print($"[BareOhlcLogger] Command poll error: {ex.Message}");
            }
        }

        private void ProcessCommandJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null)
                {
                    return;
                }

                if (!root.TryGetValue("status", out var statusObj) || !(statusObj is string status))
                {
                    return;
                }

                if (status == "empty")
                {
                    var now = DateTime.UtcNow;
                    if (now - lastEmptyCommandLogUtc > emptyLogInterval)
                    {
                        lastEmptyCommandLogUtc = now;
                        Print("[BareOhlcLogger] Command queue empty (polling active)");
                    }
                    return;
                }

                if (status != "ok")
                {
                    return;
                }

                if (!root.TryGetValue("command", out var cmdObj))
                {
                    return;
                }

                var cmd = cmdObj as Dictionary<string, object>;
                if (cmd == null)
                {
                    return;
                }

                var type = GetString(cmd, "type");
                var cmdId = ToNullableInt(cmd, "id");

                if (cmdId.HasValue && cmdId.Value == lastCommandIdHandled)
                {
                    return; // Already handled
                }

                if (string.Equals(type, "trend", StringComparison.OrdinalIgnoreCase))
                {
                    var direction = GetString(cmd, "direction");
                    var price = ToNullableDouble(cmd, "price");
                    var barIndex = ToNullableInt(cmd, "barIndex");
                    var source = GetString(cmd, "source") ?? "dashboard";
                    ExecuteTrendCommand(cmdId ?? -1, direction, price, barIndex, source);
                }

                if (cmdId.HasValue)
                {
                    lastCommandIdHandled = cmdId.Value;
                }
            }
            catch (Exception ex)
            {
                Print($"[BareOhlcLogger] Command parse error: {ex.Message}");
            }
        }

        private void ExecuteTrendCommand(int cmdId, string direction, double? price, int? barIndex, string source)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return;
            }

            var dir = direction.Trim().ToUpperInvariant();
            // Treat anything other than UP/DOWN as FLAT for safety
            if (dir != "UP" && dir != "DOWN" && dir != "FLAT")
            {
                dir = "FLAT";
            }

            var msg = $"[CMD] #{cmdId} trend {dir} price={price?.ToString("F2") ?? ""} barIndex={barIndex?.ToString() ?? ""} source={source} posBefore={Position.MarketPosition}";
            LogOutput(msg);
            Print($"[BareOhlcLogger] {msg}");

            if (dir == "UP")
            {
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort();
                }
                if (Position.MarketPosition != MarketPosition.Long)
                {
                    EnterLong("CmdTrendLong");
                }
            }
            else if (dir == "DOWN")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong();
                }
                if (Position.MarketPosition != MarketPosition.Short)
                {
                    EnterShort("CmdTrendShort");
                }
            }
            else if (dir == "FLAT")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ExitLong();
                }
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ExitShort();
                }
            }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var obj) || obj == null)
            {
                return null;
            }
            return obj.ToString();
        }

        private static double? ToNullableDouble(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var obj) || obj == null)
            {
                return null;
            }

            try
            {
                return Convert.ToDouble(obj, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static int? ToNullableInt(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var obj) || obj == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(obj, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Show Bar Index Labels", GroupName = "Debug", Order = 0)]
        public bool ShowBarIndexLabels
        {
            get { return showBarIndexLabels; }
            set { showBarIndexLabels = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Enable Dashboard Streaming", GroupName = "Dashboard", Order = 1)]
        public bool EnableDashboardStreaming { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dashboard Base Url", GroupName = "Dashboard", Order = 2)]
        public string DashboardBaseUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Command Polling", GroupName = "Dashboard", Order = 3)]
        public bool EnableCommandPolling { get; set; }
        #endregion
    }
}
