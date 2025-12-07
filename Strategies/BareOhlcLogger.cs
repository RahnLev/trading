#region Using declarations
using System;
using System.IO;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
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

        // Log writer for detailed output (like GradientSlope)
        private StreamWriter logWriter;
        private string logFilePath;
        private bool writerInitialized = false;  // Prevent multiple initializations

        // #region Bar Index Labels (copied from GradientSlopeStrategy)
        // Toggle to draw bar index labels above each bar on the chart.
        private bool showBarIndexLabels = true;
        // #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BareOhlcLogger";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize files once when data is loaded - this runs only once
                InitializeCsvWriter();
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

            // Log every tick for now to diagnose the issue
            try
            {
                // Write header on first bar
                if (!csvHeaderWritten)
                {
                    csvWriter.WriteLine("Timestamp,Bar,Open,High,Low,Close");
                    csvHeaderWritten = true;
                    Print($"[BareOhlcLogger] Header written");
                }

                var barIndex = CurrentBar - 1; // index of the completed bar
                var ts = Times[0][1];
                var line = string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2:F2},{3:F2},{4:F2},{5:F2}",
                    ts, barIndex, Open[1], High[1], Low[1], Close[1]);

                csvWriter.WriteLine(line);
                LogOutput($"Bar {barIndex}: O={Open[1]:F2} H={High[1]:F2} L={Low[1]:F2} C={Close[1]:F2}");
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

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Show Bar Index Labels", GroupName = "Debug", Order = 0)]
        public bool ShowBarIndexLabels
        {
            get { return showBarIndexLabels; }
            set { showBarIndexLabels = value; }
        }
        #endregion
    }
}
