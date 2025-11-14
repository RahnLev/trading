using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Custom.CBASTerminal;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar;
using RTH = NinjaTrader.NinjaScript.RealtimeErrorHandling;
using System.Collections.Generic; // for Queue<DateTime> 

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CBAS_SuperTrendStrategy : Strategy
    {
        #region Inputs
		
		// Optional: scale oscillator into price units to be visible on price panel
        [NinjaScriptProperty]
        [Display(Name = "Scale Oscillator To ATR", Order = 40, GroupName = "Plots")]
        public bool ScaleOscillatorToATR { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Osc ATR Mult", Order = 41, GroupName = "Plots")]
        public double OscAtrMult { get; set; } = 0.3;
		

		 // Exit label visualization (disabled for strategy - visual only)
        [NinjaScriptProperty]
        [Display(Name = "Show Exit Labels", Order = 32, GroupName = "Filters")]
        public bool ShowExitLabels { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Exit Label ATR Offset", Order = 33, GroupName = "Filters")]
        public double ExitLabelAtrOffset { get; set; } = 0.6;
		
        [Display(Name = "Paused", GroupName = "Control", Order = 0)]
        public bool Paused { get; set; } = false; // you can omit [NinjaScriptProperty] so it's not user-editable
        public void SetPaused(bool paused)
        {
            Paused = paused;
            Print($"Strategy {(paused ? "PAUSED" : "RESUMED")}");
            // Optional: cancel working orders when pausing
            CancelAllOrders();
        }
        private string instrumentName;

        [XmlIgnore]
        [Browsable(false)]
        public string InstanceId { get; set; } = string.Empty;

        // A-Trail proximity conversion
        [NinjaScriptProperty]
        [Display(Name = "Convert A to trail on proximity", GroupName = "A-Trail", Order = 1)]
        public bool ConvertAToTrailOnProximity { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "A trail proximity (ticks to PT_A)", GroupName = "A-Trail", Order = 2)]
        public int ProximityTicksToConvertA { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "A trail ticks after proximity", GroupName = "A-Trail", Order = 3)]
        public int ProximityTrailTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "Seconds to A trail", GroupName = "A-Trail", Order = 4)]
        [Range(0, 600)]
        public int ATrailDelaySec { get; set; } = 180;

        [NinjaScriptProperty]
        [Display(Name = "A trail ticks, 0 = use B TrailTicks", GroupName = "A-Trail", Order = 5)]
        [Range(0, 1000)]
        public int ATrailTicks { get; set; } = 4; // 0 = use RunnerTrailTicks

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "A trail min seconds", GroupName = "A-Trail", Order = 6)]
        [Range(0, 600)]
        public int ATrailMinSec { get; set; } = 0;

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "A trail max seconds", GroupName = "A-Trail", Order = 7)]
        [Range(1, 1800)]
        public int ATrailMaxSec { get; set; } = 300;

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "A trail use adaptive delay", GroupName = "A-Trail", Order = 8)]
        public bool ATrailUseAdaptive { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "A trail adaptive K", GroupName = "A-Trail", Order = 9)]
        [Range(0.1, 100)]
        public double ATrailAdaptiveK { get; set; } = 8.0;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Runner trail ticks (0 = avg 7-bar range)", Order = 65, GroupName = "Runner")]
        public int RunnerTrailTicks { get; set; } = 8;

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "Runner tighten delay ms", GroupName = "Runner", Order = 66)]
        public int RunnerTightenDelayMs { get; set; } = 350; // MNQ: 300–600ms works well

        [NinjaScriptProperty]
        [Display(Name = "Trade realtime only", GroupName = "Execution", Order = 205)]
        public bool TradeRealtimeOnly { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 3600)]
        [Display(Name = "Realtime start delay (sec)", GroupName = "Execution", Order = 206)]
        public int RealtimeStartDelaySec { get; set; } = 5;

        [NinjaScriptProperty]
        [Range(0, 3600)]
        [Display(Name = "[Audit] heartbeat sec (0=off)", GroupName = "Debug")]
        public int AuditHeartbeatSec { get; set; } = 3;

        // After a stop-loss fill, block re-entry in the same direction for N seconds.
        // Opposite direction is allowed immediately.
        [NinjaScriptProperty]
        [Range(0, 3600)]
        [Display(Name = "Same-dir cooldown after SL (sec)", Order = 201, GroupName = "Execution")]
        public int StopLossSameDirCooldownSec { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Allow re-entry in same trend", GroupName = "Execution", Order = 34)]
        public bool AllowReentrySameTrend { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Allow same-bar re-entry after flat", Order = 33, GroupName = "Execution")]
        public bool AllowSameBarReentryAfterFlat { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 600000)]
        [Display(Name = "Reverse cooldown ms", Order = 72, GroupName = "Debounce")]
        public int ReverseCooldownMs { get; set; } = 2000;

        // ============ Indicator core ============

        [NinjaScriptProperty]
        [Display(Name = "Use Regime Stability", Order = 1, GroupName = "CBAS Filters")]
        public bool UseRegimeStability { get; set; } = false; // Disabled - realtime filters are the main optimization

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Regime Stability Bars", Order = 2, GroupName = "CBAS Filters")]
        public int RegimeStabilityBars { get; set; } = 10; // Matches indicator default

        [NinjaScriptProperty]
        [Display(Name = "Use Scoring Filter", Order = 3, GroupName = "CBAS Filters")]
        public bool UseScoringFilter { get; set; } = false; // Disabled - realtime filters are the main optimization

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Score Threshold", Order = 4, GroupName = "CBAS Filters")]
        public int ScoreThreshold { get; set; } = 6; // Kept at optimized value if enabled

        [NinjaScriptProperty]
        [Display(Name = "Use Smoothed VPM", Order = 5, GroupName = "CBAS Filters")]
        public bool UseSmoothedVpm { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "VPM EMA Span", Order = 6, GroupName = "CBAS Filters")]
        public int VpmEmaSpan { get; set; } = 5;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min VPM", Order = 7, GroupName = "CBAS Filters")]
        public double MinVpm { get; set; } = 200;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min ADX", Order = 8, GroupName = "CBAS Filters")]
        public int MinAdx { get; set; } = 30;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Momentum Lookback", Order = 9, GroupName = "CBAS Filters")]
        public int MomentumLookback { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Extended Logging", Order = 10, GroupName = "CBAS Logging")]
        public bool ExtendedLogging { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Compute Exit Signals", Order = 11, GroupName = "CBAS Filters")]
        public bool ComputeExitSignals { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Exit Profit ATR Mult", Order = 12, GroupName = "CBAS Filters")]
        public double ExitProfitAtrMult { get; set; } = 3.0;



        // ============ Original/visual inputs ============

        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity", Order = 1, GroupName = "Indicator")]
        public double Sensitivity { get; set; } = 3.4;

        [NinjaScriptProperty]
        [Display(Name = "EMA Energy", Order = 2, GroupName = "Indicator")]
        public bool EmaEnergy { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Keltner Length", Order = 3, GroupName = "Indicator")]
        public int KeltnerLength { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Length (unused in ST)", Order = 4, GroupName = "Indicator")]
        public int AtrLength { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Range Min Length", Order = 5, GroupName = "Indicator")]
        public int RangeMinLength { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Range Width Mult", Order = 6, GroupName = "Indicator")]
        public double RangeWidthMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Range ATR Length", Order = 7, GroupName = "Indicator")]
        public int RangeAtrLen { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name = "Bull NetFlow Min", Order = 1, GroupName = "Realtime Filters")]
        public double RealtimeBullNetflowMin { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Display(Name = "Bull Objection Max", Order = 2, GroupName = "Realtime Filters")]
        public double RealtimeBullObjectionMax { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Display(Name = "Bull EMA Color Min", Order = 3, GroupName = "Realtime Filters")]
        public double RealtimeBullEmaColorMin { get; set; } = 8.0;

        [NinjaScriptProperty]
        [Display(Name = "Use Bull Attract Filter", Order = 4, GroupName = "Realtime Filters")]
        public bool RealtimeBullUseAttract { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Bull Attract Min", Order = 5, GroupName = "Realtime Filters")]
        public double RealtimeBullAttractMin { get; set; } = 4.5;

        [NinjaScriptProperty]
        [Display(Name = "Bull Score Min (0 = ignore)", Order = 6, GroupName = "Realtime Filters")]
        public double RealtimeBullScoreMin { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Display(Name = "Bear NetFlow Max", Order = 7, GroupName = "Realtime Filters")]
        public double RealtimeBearNetflowMax { get; set; } = -0.5;

        [NinjaScriptProperty]
        [Display(Name = "Bear Objection Min", Order = 8, GroupName = "Realtime Filters")]
        public double RealtimeBearObjectionMin { get; set; } = 4.0;

        [NinjaScriptProperty]
        [Display(Name = "Bear EMA Color Max", Order = 9, GroupName = "Realtime Filters")]
        public double RealtimeBearEmaColorMax { get; set; } = 6.0;

        [NinjaScriptProperty]
        [Display(Name = "Use Bear Price/Band Filter", Order = 10, GroupName = "Realtime Filters")]
        public bool RealtimeBearUsePriceToBand { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Bear Price/Band Max", Order = 11, GroupName = "Realtime Filters")]
        public double RealtimeBearPriceToBandMax { get; set; } = 0.3;

        [NinjaScriptProperty]
        [Display(Name = "Bear Score Min (0 = ignore)", Order = 12, GroupName = "Realtime Filters")]
        public double RealtimeBearScoreMin { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Display(Name = "Flat Tolerance (points)", Order = 13, GroupName = "Realtime Filters")]
        public double RealtimeFlatTolerance { get; set; } = 0.5;

        [NinjaScriptProperty]
        [Display(Name = "Show Realtime State Plot", Order = 14, GroupName = "Realtime Filters")]
        public bool ShowRealtimeStatePlot { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Plot Realtime Signals", Order = 15, GroupName = "Realtime Filters")]
        public bool PlotRealtimeSignals { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Flip Confirmation Bars", Order = 16, GroupName = "Realtime Filters")]
        public int FlipConfirmationBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Order = 1, GroupName = "Indicator")]
        public bool EnableLogging { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Log Signals Only (bull/bear crosses)", Order = 2, GroupName = "Indicator")]
        public bool LogSignalsOnly { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Heartbeat Every N Bars (0=off)", Order = 3, GroupName = "Indicator")]
        public int HeartbeatEveryNBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Log Folder", Order = 4, GroupName = "Indicator")]
        public string LogFolder { get; set; } = @"/Users/mm/Documents/NinjaTrader 8/Indicator_logs";

        [NinjaScriptProperty]
        [Display(Name = "Log Drawn Signals", Order = 5, GroupName = "Indicator")]
        public bool LogDrawnSignals { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Color Bars By Trend", Order = 6, GroupName = "Indicator")]
        public bool ColorBarsByTrend { get; set; } = false; // Visual only, disabled for strategy

        // Risk (base/manual)
        [NinjaScriptProperty]
        [Range(1, 20000)]
        [Display(Name = "Stop (ticks) [base]", Order = 10, GroupName = "Risk")]
        public int StopTicks { get; set; } = 21;

        [NinjaScriptProperty]
        [Range(1, 20000)]
        [Display(Name = "Target A (ticks) [base]", Order = 11, GroupName = "Risk")]
        public int TargetATicks { get; set; } = 42;

        [NinjaScriptProperty]
        [Range(0, 20000)]
        [Display(Name = "Target B (ticks, 0 = no PT_B)", Order = 12, GroupName = "Risk")]
        public int TargetBTicks { get; set; } = 0;

        // Autosizing
        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "Auto size/targets from volume", Order = 90, GroupName = "AutoSizing")]
        public bool UseVolumeDrivenSizing { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Browsable(false)]
        //[Display(Name = "Volume lookback (bars)", Order = 91, GroupName = "AutoSizing")]
        public int VolumeLookback { get; set; } = 1;

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "Auto Target A from volume", Order = 92, GroupName = "AutoSizing")]
        public bool AutoTargetAFromVolume { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1.0, 1e9)]
        [Browsable(false)]
        //[Display(Name = "Target A divisor (sumVol / x)", Order = 93, GroupName = "AutoSizing")]
        public double TargetADivisor { get; set; } = 10.0;

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "Auto Stop from TargetA", Order = 94, GroupName = "AutoSizing")]
        public bool AutoStopFromVolume { get; set; } = true;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 1000)]
        //[Display(Name = "Stop divisor (TargetA / x)", Order = 95, GroupName = "AutoSizing")]
        public double StopDivisor { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Browsable(false)]
        //[Display(Name = "Auto Qty from volume", Order = 96, GroupName = "AutoSizing")]
        public bool AutoQtyFromVolume { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1.0, 1e12)]
        //[Display(Name = "Qty divisor (sumVol / x)", Order = 97, GroupName = "AutoSizing")]
        public double QtyDivisor { get; set; } = 10000.0;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100000)]
        //[Display(Name = "Min total qty", Order = 98, GroupName = "AutoSizing")]
        public int MinTotalQty { get; set; } = 2;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100000)]
        //[Display(Name = "Max total qty", Order = 99, GroupName = "AutoSizing")]
        public int MaxTotalQty { get; set; } = 2;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        //[Display(Name = "QtyA share percent", Order = 100, GroupName = "AutoSizing")]
        public int QtyASharePercent { get; set; } = 50;

        // Calculation/Display
        [NinjaScriptProperty]
        [Display(Name = "Use On Each Tick", Order = 20, GroupName = "Calculation")]
        public bool UseOnEachTick { get; set; } = true;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entries only on first tick of bar", Order = 21, GroupName = "Calculation")]
        public bool EntriesOnFirstTickOnly { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Show Indicator On Chart", Order = 22, GroupName = "Visualization")]
        public bool ShowIndicator { get; set; } = true;

        // Execution
        [NinjaScriptProperty]
        [Display(Name = "Reverse On Opposite", Order = 30, GroupName = "Execution")]
        public bool ReverseOnOpposite { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Exit runner on opposite signal", Order = 81, GroupName = "Runner")]
        public bool ExitRunnerOnOpposite { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Warmup Bars (BarsRequiredToTrade)", Order = 40, GroupName = "Execution")]
        public int WarmupBars { get; set; } = 20;

        // Scale-out
        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Qty A (targeted)", Order = 50, GroupName = "ScaleOut")]
        public int QtyA { get; set; } = 5;

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Qty B (runner)", Order = 51, GroupName = "ScaleOut")]
        public int QtyB { get; set; } = 5;

        // Runner
        [NinjaScriptProperty]//12345
        [Browsable(false)]
        [Range(0, 10000)]
        [Display(Name = "Breakeven after ticks (runner)", Order = 60, GroupName = "Runner")]
        public int BreakEvenTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "Runner BE triggers at A target/trail", Order = 64, GroupName = "Runner")]
        public bool RunnerTrailsWithA { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Entry cooldown ms (real-time only)", Order = 70, GroupName = "Debounce")]
        public int EntryCooldownMs { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name = "Flatten runner on A stop (partial)", Order = 80, GroupName = "Runner")]
        public bool FlattenRunnerOnPartialAStop { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Re-arm stop on partial fill", Order = 91, GroupName = "Risk")]
        public bool ReArmStopOnPartial { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Drop-trade tolerance (ticks)", Order = 92, GroupName = "Risk")]
        public int DropTradeToleranceTicks { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Flatten runner on A stop (full)", Order = 82, GroupName = "Runner")]
        public bool FlattenRunnerOnAStopFull { get; set; } = false;

        // Debug switch for throttled SignalCheck printing
        [NinjaScriptProperty]
        [Display(Name = "Debug: log SignalCheck", Order = 900, GroupName = "Debug")]
        public bool DebugLogSignals { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Initial stop guard ticks", GroupName = "Risk", Order = 93)]
        public int InitialStopGuardTicks { get; set; } = 8; // MNQ: start with 4–6


        #endregion
        #region Privates
        // TPM (Ticks Per Minute) tracking
        private readonly object tpmSync = new object();
        private readonly Queue<DateTime> tpmTicks = new Queue<DateTime>();

        private DateTime tpmLastPublish = DateTime.MinValue;

        // Optional toggles (used by your command handler)
        private bool tpmEnabled = true;
        private bool tpmIncludeBidAsk = false;

        // At class level
        private bool? lastLongBArmed = null;
        private bool? lastShortBArmed = null;
        private bool? lastALongTrailing = null;
        private bool? lastAShortTrailing = null;

        // Throttles for B-leg stop operations (per side)
        private DateTime nextBLongStopOpUtc = DateTime.MinValue;
        private DateTime nextBShortStopOpUtc = DateTime.MinValue;
        private const int StopOpThrottleMs = 75; // tune 50–150ms

        // Defer runner updates after A->B sync (per side)
        private DateTime runnerNextBLongUtc = DateTime.MinValue;
        private DateTime runnerNextBShortUtc = DateTime.MinValue;

        private ATR atrRunner7;
        private volatile bool flattenRequest;
        // Test-order flags (set by command, consumed in OnBarUpdate)
        private volatile int testLongQtyRequest;
        private volatile int testShortQtyRequest;
        private int gateLastBarPrinted = -1;
        private bool allowLogging = false;
        // Per-leg throttle to avoid rapid re-arm churn on partials
        private DateTime nextPartialRearmLongAUtc = DateTime.MinValue;
        private DateTime nextPartialRearmLongBUtc = DateTime.MinValue;
        private DateTime nextPartialRearmShortAUtc = DateTime.MinValue;
        private DateTime nextPartialRearmShortBUtc = DateTime.MinValue;
        private const int PartialRearmDelayMs = 150;
        private DateTime longBLastFillUtc = DateTime.MinValue;
        private DateTime shortBLastFillUtc = DateTime.MinValue;
        // A-trail override ticks once proximity triggers
        private int? aTrailTicksOverrideLongA = null;
        private int? aTrailTicksOverrideShortA = null;
        private DateTime nextEnsureALongUtc = DateTime.MinValue;
        private DateTime nextEnsureAShortUtc = DateTime.MinValue;
        private DateTime nextEnsureBLongUtc = DateTime.MinValue;
        private DateTime nextEnsureBShortUtc = DateTime.MinValue;
        private Order slALong, slAShort, slBLong, slBShort, ptA, ptB, exitA, exitB;
        private double longAStop = double.NaN;
        private double shortAStop = double.NaN;
        private int activeQtyA, activeQtyB, activeTargetATicks, activeStopTicks;

        // Validity helpers
        private static bool IsValid(double v) => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v);
        //private double T() => TickSizeSafe;
        private double Eps() => 0.5 * T();
        //private double RT(double price) => Instrument.MasterInstrument.RoundToTickSize(price);

        private double T()
        {
            if (TickSizeSafe > 0)
                return TickSizeSafe;

            var mi = Instrument?.MasterInstrument;
            if (mi != null && mi.TickSize > 0)
                return mi.TickSize;

            // Last resort to avoid divide-by-zero / NaN propagation in early states
            return 1.0;
        }
        private double RT(double price)
        {
            if (double.IsNaN(price)) return price; // preserve NaN sentinel
            var mi = Instrument?.MasterInstrument;
            return mi != null ? mi.RoundToTickSize(price) : price;
        }

        private int qtyLongAOpen = 0;
        private int qtyLongBOpen = 0;
        private int qtyShortAOpen = 0;
        private int qtyShortBOpen = 0;

        private bool longATrailMode = false;
        private bool shortATrailMode = false;
        private DateTime realtimeEnteredUtc = DateTime.MinValue;
        private DateTime realtimeTradingAllowedUtc = DateTime.MinValue;


        // A-trail timing
        private DateTime longAEntryUtc = DateTime.MinValue;
        private DateTime shortAEntryUtc = DateTime.MinValue;
        private DateTime aTrailNextUtc = DateTime.MinValue;
        private const int ATrailThrottleMs = 200; // 100–300 ms typical

        // Failsafe stop refs and prices
        private Order rescueStopLong, rescueStopShort;
        private double rescueStopLongPrice = double.NaN, rescueStopShortPrice = double.NaN;

        // Trailing throttle for failsafe
        private DateTime fsTrailNextUtc = DateTime.MinValue;
        private const int FailsafeTrailThrottleMs = 200; // adjust 100–300 ms as needed

        // Deferred actions (processed in OnBarUpdate)
        private volatile bool scheduleEnsureA, scheduleEnsureB, scheduleFailsafe;

        // Failsafe trace cache
        private MarketPosition fsLastPos = MarketPosition.Flat;
        private int fsLastQty = int.MinValue;
        private bool fsLastAOpen = false, fsLastBOpen = false, fsLastProtected = false;
        private DateTime fsLastLogUtc = DateTime.MinValue;

        private DateTime nextFailsafeAllowedUtc = DateTime.MinValue;
        private const int FailsafeCooldownMs = 300; // throttle to avoid churn

        private DateTime wdNextLongAUtc = DateTime.MinValue;
        private DateTime wdNextLongBUtc = DateTime.MinValue;
        private DateTime wdNextShortAUtc = DateTime.MinValue;
        private DateTime wdNextShortBUtc = DateTime.MinValue;
        //private const int WatchdogThrottleMs = 200; // adjust 100–300ms as needed

        // Per-leg throttle to avoid event storms in OnOrderUpdate
        private const int EnsureCooldownMs = 300;  // 250–500 ms


        private int auditLastPosQty = int.MinValue;
        private bool auditLastAOpen = false, auditLastBOpen = false;
        private bool auditLastSLA = false, auditLastSLB = false;
        private double auditLastLAStop = double.NaN, auditLastLBStop = double.NaN,
                       auditLastSAStop = double.NaN, auditLastSBStop = double.NaN;
        private DateTime auditLastLogUtc = DateTime.MinValue;
        private double TickSizeSafe => Instrument?.MasterInstrument?.TickSize ?? 1.0;
        private bool realtimeClocksSanitized = false;
        private DateTime longCooldownExpireUtc = DateTime.MinValue;
        private DateTime shortCooldownExpireUtc = DateTime.MinValue;

        private const bool EchoToOutput = true;
        private string logPath;
        private StreamWriter logWriter;
        private readonly object logLock = new object();
        private bool logInitialized;

        private enum RetryIntent { None, Long, Short }
        private RetryIntent safetyRetryIntent = RetryIntent.None;

        private int lastDynTargetA = int.MinValue;
        private int lastDynStop = int.MinValue;
        private int lastDynQtyA = int.MinValue;
        private int lastDynQtyB = int.MinValue;


        private CBASTestingIndicator3 st;
        private ATR atr;

        private int lastSignalBar = -1;
        private DateTime lastEntryAttemptUtc = DateTime.MinValue;

        private bool pendingLong = false;
        private bool pendingShort = false;

        private Order longAEntry, longBEntryOrder, shortAEntry, shortBEntryOrder;

        private double longBEntryPrice = double.NaN;
        private double shortBEntryPrice = double.NaN;

        private bool longB_BE_armed = false;
        private bool shortB_BE_armed = false;

        private double longAEntryPrice = double.NaN;
        private double shortAEntryPrice = double.NaN;
        private double longATargetPrice = double.NaN;
        private double shortATargetPrice = double.NaN;

        private double longBStop = double.NaN;
        private double shortBStop = double.NaN;



        // Per-leg open flags
        private bool isLongALegOpen = false, isLongBLegOpen = false, isShortALegOpen = false, isShortBLegOpen = false;

        // Per-leg "closing requested" flags
        private bool longA_ClosingRequested = false, shortA_ClosingRequested = false;
        private bool longB_ClosingRequested = false, shortB_ClosingRequested = false;


        // Debug throttles for SignalCheck
        private bool dbgLastBull = false, dbgLastBear = false, dbgLastFirstTick = false;
        private MarketPosition dbgLastPos = MarketPosition.Flat;
        private int dbgLastBar = -1;
        private DateTime dbgLastPrintUtc = DateTime.MinValue;

        // Added fields for robust entry/reversal handling
        private DateTime lastFlatUtc = DateTime.MinValue;
        private DateTime lastEntrySubmitUtc = DateTime.MinValue;
        private string lastEntrySignalA = null, lastEntrySignalB = null;
        private bool longAPending = false, longBPending = false, shortAPending = false, shortBPending = false;

        #endregion
        #region Terminal
        // Cancels all working orders the strategy tracks and clears refs/caches.
        // Returns the number of cancel requests sent.
        // You can choose which categories to include; defaults cancel everything.
        private int CancelAllOrders(
            string reason = "CancelAll",
            bool includeEntries = true,
            bool includeStops = true,
            bool includeTargets = true,
            bool includeExits = true,
            bool includeFailsafes = true)
        {
            int cancelled = 0;

            int CancelIfActive(ref Order o)
            {
                if (o != null && IsOrderActive(o))
                {
                    try
                    {
                        CancelOrder(o);
                        cancelled++;
                    }
                    catch (Exception ex)
                    {
                        LogPrint($"[CancelAll:{reason}] CancelOrder error for {o?.Name} From={o?.FromEntrySignal}: {ex.Message}");
                    }
                }
                o = null; // always clear our reference
                return 0;
            }

            // ENTRIES
            if (includeEntries)
            {
                CancelIfActive(ref longAEntry);
                CancelIfActive(ref longBEntryOrder);
                CancelIfActive(ref shortAEntry);
                CancelIfActive(ref shortBEntryOrder);

                // Clear pending flags so we don't submit them later
                longAPending = false;
                longBPending = false;
                shortAPending = false;
                shortBPending = false;
            }

            // TARGETS (PT_A / PT_B). PT_B_TRAIL shares slB refs, handled in STOPS.
            if (includeTargets)
            {
                CancelIfActive(ref ptA);
                CancelIfActive(ref ptB);
            }

            // EXITS (manual/forced)
            if (includeExits)
            {
                CancelIfActive(ref exitA);
                CancelIfActive(ref exitB);

                // Since we just canceled exits, clear closing-requested flags
                longA_ClosingRequested = false;
                longB_ClosingRequested = false;
                shortA_ClosingRequested = false;
                shortB_ClosingRequested = false;
            }

            // FAILSAFES
            if (includeFailsafes)
            {
                CancelIfActive(ref rescueStopLong);
                CancelIfActive(ref rescueStopShort);
                rescueStopLongPrice = double.NaN;
                rescueStopShortPrice = double.NaN;
                scheduleFailsafe = false; // don't auto-rescue immediately after mass-cancel
            }

            // STOPS (and runner trails via SL_B/PT_B_TRAIL)
            if (includeStops)
            {
                // A-leg stops
                if (slALong != null) { CancelIfActive(ref slALong); longAStop = double.NaN; }
                if (slAShort != null) { CancelIfActive(ref slAShort); shortAStop = double.NaN; }

                // B-leg stops and trailing "PT_B_TRAIL" (tracked via slB refs)
                if (slBLong != null) { CancelIfActive(ref slBLong); longBStop = double.NaN; }
                if (slBShort != null) { CancelIfActive(ref slBShort); shortBStop = double.NaN; }

                // Prevent immediate runner re-arming
                longB_BE_armed = false;
                shortB_BE_armed = false;
            }

            LogPrint($"[CancelAll:{reason}] Cancel requests sent={cancelled} (entries={includeEntries}, stops={includeStops}, targets={includeTargets}, exits={includeExits}, failsafes={includeFailsafes})");

            return cancelled;
        }

        private void PublishLog(string msg, string level = "INFO")
        {
            try
            {
                CBASTerminalBus.Publish(new CBASTerminalLogEntry
                {
                    InstanceId = InstanceId,
                    Instrument = instrumentName,
                    Message = msg,
                    Level = level,
                    Bar = CurrentBar,
                    Timestamp = Core.Globals.Now
                });
            }
            catch { }
        }
        #endregion
        #region UI thread
        private void HandleTerminalCommand(string id, string command)
        {
            if (!string.Equals(id, InstanceId, StringComparison.OrdinalIgnoreCase))
                return;

            // If ChartControl exists, you marshal to UI (your existing pattern). For simple flags,
            // it's also safe to handle immediately without Dispatcher. Below keeps your pattern.
            var dispatcher = ChartControl?.Dispatcher;
            Action handler = () =>
            {
                var (verb, args) = CBASTerminalCommands.Parse(command);
                if (verb == null) return;

                try
                {
                    switch (verb)
                    {
                        case "help":
                            PublishLog("Commands: set <Property> <value> | toggle <BoolProperty> | testlong <qty> | testshort <qty> | help");
                            break;

                        case "toggle":
                            if (args.Length != 1) { PublishLog("Usage: toggle <BoolProperty>", "WARN"); break; }
                            ToggleBoolProperty(args[0]); break;

                        case "set":
                            if (args.Length < 2) { PublishLog("Usage: set <Property> <value>", "WARN"); break; }
                            SetPropertyValue(args[0], string.Join(" ", args.Skip(1))); break;

                        case "testlong":
                            {
                                int qty = 5;
                                if (args.Length >= 1) int.TryParse(args[0], out qty); qty = Math.Max(1, qty);
                                testLongQtyRequest = qty; // flag; OnBarUpdate will submit
                                PublishLog($"Queued TEST LONG (A/B) qty={qty} per leg");
                                break;
                            }

                        case "testshort":
                            {
                                int qty = 5;
                                if (args.Length >= 1) int.TryParse(args[0], out qty); qty = Math.Max(1, qty);
                                testShortQtyRequest = qty; // flag; OnBarUpdate will submit
                                PublishLog($"Queued TEST SHORT (A/B) qty={qty} per leg");
                                break;
                            }

                        case "flat":
                        case "close":
                        case "closeall":
                        case "cancelall":
                            {
                                flattenRequest = true;
                                PublishLog("Requested FLAT: cancel all and close position");
                                break;
                            }
                        case "trailstatus":
                            {
                                try
                                {
                                    int rem = -1;
                                    bool armed = false;

                                    DateTime nowUtc = StrategyNowUtc();
                                    int baseDelaySec = ComputeATrailDelaySec();

                                    var mp = Position?.MarketPosition ?? MarketPosition.Flat;

                                    if (mp == MarketPosition.Long && isLongALegOpen && longAEntryUtc != DateTime.MinValue)
                                    {
                                        armed = longATrailMode;
                                        int delaySecLong = longATrailMode ? 0 : baseDelaySec;
                                        int elapsed = (int)Math.Max(0, (nowUtc - longAEntryUtc).TotalSeconds);
                                        rem = armed ? 0 : Math.Max(0, delaySecLong - elapsed);
                                    }
                                    else if (mp == MarketPosition.Short && isShortALegOpen && shortAEntryUtc != DateTime.MinValue)
                                    {
                                        armed = shortATrailMode;
                                        int delaySecShort = shortATrailMode ? 0 : baseDelaySec;
                                        int elapsed = (int)Math.Max(0, (nowUtc - shortAEntryUtc).TotalSeconds);
                                        rem = armed ? 0 : Math.Max(0, delaySecShort - elapsed);
                                    }
                                    else
                                    {
                                        // Fallback if MarketPosition is transient; pick whichever A leg is open
                                        if (isLongALegOpen && longAEntryUtc != DateTime.MinValue)
                                        {
                                            armed = longATrailMode;
                                            int delaySecLong = longATrailMode ? 0 : baseDelaySec;
                                            int elapsed = (int)Math.Max(0, (nowUtc - longAEntryUtc).TotalSeconds);
                                            rem = armed ? 0 : Math.Max(0, delaySecLong - elapsed);
                                        }
                                        else if (isShortALegOpen && shortAEntryUtc != DateTime.MinValue)
                                        {
                                            armed = shortATrailMode;
                                            int delaySecShort = shortATrailMode ? 0 : baseDelaySec;
                                            int elapsed = (int)Math.Max(0, (nowUtc - shortAEntryUtc).TotalSeconds);
                                            rem = armed ? 0 : Math.Max(0, delaySecShort - elapsed);
                                        }
                                        else
                                        {
                                            rem = -1;
                                            armed = false;
                                        }
                                    }

                                    PublishLog($"[TS] rem={rem} armed={armed.ToString().ToLowerInvariant()}");
                                }
                                catch (Exception ex)
                                {
                                    PublishLog($"[TS] error={ex.Message}", "WARN");
                                }
                                break;
                            }
                        case "tpm":
                            {
                                // Usage:
                                // tpm on/off
                                // tpm bidask on/off
                                if (args.Length == 0)
                                {
                                    PublishLog($"TPM: enabled={tpmEnabled} includeBidAsk={tpmIncludeBidAsk}");
                                    break;
                                }

                                if (args.Length == 1)
                                {
                                    var val = args[0].ToLowerInvariant();
                                    if (val == "on" || val == "true") { tpmEnabled = true; PublishLog("TPM enabled"); }
                                    else if (val == "off" || val == "false") { tpmEnabled = false; PublishLog("TPM disabled"); }
                                    else PublishLog("Usage: tpm on|off", "WARN");
                                    break;
                                }

                                if (args.Length == 2 && args[0].Equals("bidask", StringComparison.OrdinalIgnoreCase))
                                {
                                    var val = args[1].ToLowerInvariant();
                                    if (val == "on" || val == "true") { tpmIncludeBidAsk = true; PublishLog("TPM: include Bid/Ask ON"); }
                                    else if (val == "off" || val == "false") { tpmIncludeBidAsk = false; PublishLog("TPM: include Bid/Ask OFF"); }
                                    else PublishLog("Usage: tpm bidask on|off", "WARN");
                                    break;
                                }

                                PublishLog("Usage: tpm [on|off] | tpm bidask [on|off]", "WARN");
                                break;
                            }
                        case "trailticks":
                        case "report":
                            {
                                PublishLog($"[TT] A={ATrailTicks} B={RunnerTrailTicks}");
                                break;
                            }

                        default:
                            PublishLog($"Unknown command: {verb}", "WARN");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    PublishLog($"Command error: {ex.Message}", "ERROR");
                }

                // Visual refresh (safe to keep)
                ForceRefresh();
            };

            if (dispatcher != null)
                dispatcher.BeginInvoke(handler);
            else
                handler(); // fall back if no chart; setting flags is thread-safe
        }

        private void ToggleBoolProperty(string prop)
        {
            var pi = GetType().GetProperty(prop, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (pi == null || pi.PropertyType != typeof(bool))
            {
                PublishLog($"Bool property not found: {prop}", "WARN");
                return;
            }
            bool current = (bool)pi.GetValue(this);
            pi.SetValue(this, !current);
            PublishLog($"Toggled {pi.Name} -> {!current}");
        }

        private void SetPropertyValue(string prop, string raw)
        {
            var pi = GetType().GetProperty(prop, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (pi == null)
            {
                PublishLog($"Property not found: {prop}", "WARN");
                return;
            }
            object converted;
            if (pi.PropertyType == typeof(int) && int.TryParse(raw, out var iv)) converted = iv;
            else if (pi.PropertyType == typeof(double) && double.TryParse(raw, out var dv)) converted = dv;
            else if (pi.PropertyType == typeof(bool) && bool.TryParse(raw, out var bv)) converted = bv;
            else if (pi.PropertyType == typeof(string)) converted = raw;
            else
            {
                PublishLog($"Unsupported type for {pi.Name}: {pi.PropertyType.Name}", "WARN");
                return;
            }

            pi.SetValue(this, converted);
            PublishLog($"Set {pi.Name} -> {converted}");
        }

        private void ForceRefresh()
        {
            try
            {
                ChartControl?.InvalidateVisual();
            }
            catch { }
        }

        #endregion
        #region Event handlers

        protected override void OnBarUpdate()
        {
            // 1) BarsRequiredToTrade
            if (CurrentBar < Math.Max(BarsRequiredToTrade, 5))
            {
                pendingLong = pendingShort = false;
                return;
            }
            // 2) Pause
            if (Paused) return;
            // 3) what is it?
            bool isRealtime = State == State.Realtime;

            // 4) Update dynamic risk/qty on first tick of bar while flat
            if (IsFirstTickOfBar && Position.MarketPosition == MarketPosition.Flat)
                RefreshDynamicParameters();

            // 6) Maintain protection/stops before any decisions
            ManageRunnerStopsCompact();   // B runner trails/BE maintenance
            EnsureLegStopsCompact();      // A/B leg stop assert/tighten
            EnsureRescueProtection();     // generic failsafe if no proper stops
            ManageATrailProximity();  // flips A to trail when near PT_A and cancels PT_A
            ManageATrail();               // A leg trailing manager (existing logic)
            AuditProtection("OnBarUpdate", Time[0]);

            // 7) Realtime-only entries gate (do not block safety above)
            if (RealtimeEntriesBlocked(out double rtRem))
                return;

            // 8) First-tick-only entries gate
            if (EntriesOnFirstTickOnly && !IsFirstTickOfBar)
                return;
            // flzattenRequest
            if (flattenRequest)
            {
                flattenRequest = false;

                try
                {
                    // 1) Cancel everything the strategy is tracking (entries, stops, targets, exits, failsafes)
                    CancelAllOrders("TerminalFlat", includeEntries: true, includeStops: true, includeTargets: true, includeExits: true, includeFailsafes: true);

                    // 2) Exit any open position using your leg signals
                    // Prefer closing per-entry to keep managed associations clean.
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        // Close both A and B long legs if they exist
                        ExitLong("LongA");
                        ExitLong("LongB");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        // Close both A and B short legs if they exist
                        ExitShort("ShortA");
                        ExitShort("ShortB");
                    }

                    // Optional: if you want an emergency hard flatten as a fallback (not usually needed):
                    // if (Account != null) Account.Flatten(new[] { Instrument });

                    PublishLog("[FLAT] Cancelled all working orders and submitted exits for open legs");
                }
                catch (Exception ex)
                {
                    PublishLog($"[FLAT] Error: {ex.Message}", "ERROR");
                }

                // Prevent any immediate re-entries this bar if you want:
                // lastEntryAttemptUtc = StrategyNowUtc();
                // return; // if you want to stop processing rest of OnBarUpdate on this tick
            }


            // 9) Don't overlap submissions
            if (EntryWorking())
                return;

            // 10) Entry cooldown (realtime only)
            if (EntryCooldownMs > 0 && isRealtime && lastEntryAttemptUtc != DateTime.MinValue)
            {
                double elapsedMs = (StrategyNowUtc() - lastEntryAttemptUtc).TotalMilliseconds;
                if (elapsedMs >= 0 && elapsedMs < EntryCooldownMs)
                    return;
            }

            // 11) Safety retry handling after forced flat (if used)
            if (Position.MarketPosition == MarketPosition.Flat && safetyRetryIntent != RetryIntent.None)
            {
                if (isRealtime && lastFlatUtc != DateTime.MinValue &&
                    (DateTime.UtcNow - lastFlatUtc).TotalMilliseconds < ReverseCooldownMs)
                    return;

                if (safetyRetryIntent == RetryIntent.Long) SubmitLongAB();
                else if (safetyRetryIntent == RetryIntent.Short) SubmitShortAB();

                lastEntryAttemptUtc = lastEntrySubmitUtc;
                safetyRetryIntent = RetryIntent.None;
                return;
            }

            //testOrders
            {
                // Optional: honor your realtime delay gate
                // if (DateTime.UtcNow < realtimeTradingAllowedUtc) return;
                bool isFlat = Position.MarketPosition == MarketPosition.Flat;

                int ql = testLongQtyRequest;
                if (ql > 0)
                {
                    testLongQtyRequest = 0;
                    // Optionally gate: if (DateTime.UtcNow >= realtimeTradingAllowedUtc && isFlat && !IsSameDirCooldownActive(true))
                    if (isFlat)
                    {
                        EnterLong(ql, "LongA");
                        EnterLong(ql, "LongB");
                        LogPrint($"[TEST] Submitted LONG A/B {ql}+{ql}");
                    }
                    else
                    {
                        LogPrint("[TEST] Skipped LONG test: not flat");
                    }
                }

                int qs = testShortQtyRequest;
                if (qs > 0)
                {
                    testShortQtyRequest = 0;
                    // Optionally gate: if (DateTime.UtcNow >= realtimeTradingAllowedUtc && isFlat && !IsSameDirCooldownActive(false))
                    if (isFlat)
                    {
                        EnterShort(qs, "ShortA");
                        EnterShort(qs, "ShortB");
                        LogPrint($"[TEST] Submitted SHORT A/B {qs}+{qs}");
                    }
                    else
                    {
                        LogPrint("[TEST] Skipped SHORT test: not flat");
                    }
                }

            }

            // 12) Signals - Use indicator's optimized realtime state signals
            // When PlotRealtimeSignals=true, these use the optimized filters (netflow, objection, ema_color)
            // Otherwise, they use the standard SuperTrend cross logic
            bool bull = st.BullSignal;
            bool bear = st.BearSignal;
            
            // Also get SuperTrend line for regime checks and stop calculations
            var stLine = st.Values[0];
            bool inBullRegime = Close[0] > stLine[0];
            bool inBearRegime = Close[0] < stLine[0];

            int stamp = EntriesOnFirstTickOnly ? CurrentBar - 1 : CurrentBar;
            bool duplicate = (stamp == lastSignalBar);

            // Optional debug throttled log
            if (DebugLogSignals)
            {
                bool stateChanged = bull != dbgLastBull
                    || bear != dbgLastBear
                    || Position.MarketPosition != dbgLastPos
                    || IsFirstTickOfBar != dbgLastFirstTick
                    || (IsFirstTickOfBar && CurrentBar != dbgLastBar);
                bool throttle = (DateTime.UtcNow - dbgLastPrintUtc).TotalMilliseconds >= 250;

                if ((IsFirstTickOfBar || bull || bear || stateChanged) && throttle)
                {
                    LogPrint($"3_{Time[0]} SignalCheck bull={bull} bear={bear} pos={Position.MarketPosition} firstTick={IsFirstTickOfBar}");
                    dbgLastPrintUtc = DateTime.UtcNow;
                }

                dbgLastBull = bull;
                dbgLastBear = bear;
                dbgLastPos = Position.MarketPosition;
                dbgLastFirstTick = IsFirstTickOfBar;
                dbgLastBar = CurrentBar;
            }

            // 13) FLAT handling
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // Reverse cooldown
                if (isRealtime && lastFlatUtc != DateTime.MinValue &&
                    (DateTime.UtcNow - lastFlatUtc).TotalMilliseconds < ReverseCooldownMs)
                    return;

                // Same-direction cooldown status
                bool sameDirLongBlocked = IsSameDirCooldownActive(true);
                bool sameDirShortBlocked = IsSameDirCooldownActive(false);

                // Pending reversals take priority
                bool anyClosingRequested = longA_ClosingRequested || longB_ClosingRequested || shortA_ClosingRequested || shortB_ClosingRequested;
                if (!anyClosingRequested)
                {
                    if (pendingLong)
                    {
                        if (!sameDirLongBlocked)
                        {
                            SubmitLongAB();
                            pendingLong = false;
                            lastEntryAttemptUtc = StrategyNowUtc();
                            lastSignalBar = stamp;
                        }
                        return;
                    }
                    if (pendingShort)
                    {
                        if (!sameDirShortBlocked)
                        {
                            SubmitShortAB();
                            pendingShort = false;
                            lastEntryAttemptUtc = StrategyNowUtc();
                            lastSignalBar = stamp;
                        }
                        return;
                    }
                }

                // Fresh signals
                if (bull || bear)
                {
                    if (!duplicate || AllowSameBarReentryAfterFlat)
                    {
                        if (bull && !sameDirLongBlocked)
                        {
                            SubmitLongAB();
                            lastEntryAttemptUtc = StrategyNowUtc();
                            lastSignalBar = stamp;
                            return;
                        }
                        if (bear && !sameDirShortBlocked)
                        {
                            SubmitShortAB();
                            lastEntryAttemptUtc = StrategyNowUtc();
                            lastSignalBar = stamp;
                            return;
                        }
                    }
                }

                // Continuation re-entry (once per bar)
                if (AllowReentrySameTrend && IsFirstTickOfBar)
                {
                    if (inBearRegime && !sameDirShortBlocked)
                    {
                        SubmitShortAB();
                        lastSignalBar = stamp;
                        lastEntryAttemptUtc = StrategyNowUtc();
                        return;
                    }
                    if (inBullRegime && !sameDirLongBlocked)
                    {
                        SubmitLongAB();
                        lastSignalBar = stamp;
                        lastEntryAttemptUtc = StrategyNowUtc();
                        return;
                    }
                }
                return;
            }

            // 14) IN POSITION: update lastSignalBar only when bar moves or we'll act
            if (!duplicate)
                lastSignalBar = stamp;

            // 15) Opposite-signal handling with leg-open guards
            if (Position.MarketPosition == MarketPosition.Long && bear)
            {
                // Close A via marketable stop (keep runner unless configured otherwise)
                if (isLongALegOpen)
                {
                    double t = TickSizeSafe;
                    double bid = BidSafe();
                    double marketableStop = Instrument.MasterInstrument.RoundToTickSize(bid - t);
                    slALong = ExitLongStopMarket(0, true, 0, marketableStop, "SL_A", "LongA");
                    // keep longAStop cache as-is; OnOrderUpdate will reconcile
                }

                if (ExitRunnerOnOpposite && isLongBLegOpen)
                {
                    longB_ClosingRequested = true;
                    ExitLong("ExitB", "LongB");
                }
                if (ExitRunnerOnOpposite && ReverseOnOpposite)
                    pendingShort = true;

                return;
            }

            if (Position.MarketPosition == MarketPosition.Short && bull)
            {
                if (isShortALegOpen)
                {
                    double t = TickSizeSafe;
                    double ask = AskSafe();
                    double marketableStop = Instrument.MasterInstrument.RoundToTickSize(ask + t);
                    slAShort = ExitShortStopMarket(0, true, 0, marketableStop, "SL_A", "ShortA");
                }

                if (ExitRunnerOnOpposite && isShortBLegOpen)
                {
                    shortB_ClosingRequested = true;
                    ExitShort("ExitB", "ShortB");
                }
                if (ExitRunnerOnOpposite && ReverseOnOpposite)
                    pendingLong = true;

                return;
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            var o = execution.Order;
            if (execution == null) return;
            if (o == null) return;
            if (o.OrderState != OrderState.Filled && o.OrderState != OrderState.PartFilled) return;

            double ts = TickSizeSafe;

            bool isLongEntryA = o.Name == "LongA" && o.OrderAction == OrderAction.Buy;
            bool isShortEntryA = o.Name == "ShortA" && o.OrderAction == OrderAction.SellShort;
            bool isLongEntryB = o.Name == "LongB" && o.OrderAction == OrderAction.Buy;
            bool isShortEntryB = o.Name == "ShortB" && o.OrderAction == OrderAction.SellShort;

            // ===== BEGIN TRACK LEGS (per-execution counters) =====
            int execQty = execution.Quantity; // this execution's fill size (partial or full)

            // Entries: add to the appropriate leg on each execution
            if (isLongEntryA)
                qtyLongAOpen += execQty;
            else if (isLongEntryB)
                qtyLongBOpen += execQty;
            else if (isShortEntryA)
                qtyShortAOpen += execQty;
            else if (isShortEntryB)
                qtyShortBOpen += execQty;


            // Exits/stops/targets: subtract from the matching leg on each execution
            bool isProtectOrExit =
                 o.Name == "SL_A" || o.Name == "SL_B" || o.Name == "PT_A" || o.Name == "PT_B"
              || o.Name == "PT_B_TRAIL" || o.Name == "ExitA" || o.Name == "ExitB"
              || o.Name == "ExitB_OnAStop" || o.Name == "ForceExit_OnReject";


            if (isProtectOrExit)
            {
                if (o.FromEntrySignal == "LongA")
                    qtyLongAOpen = Math.Max(0, qtyLongAOpen - execQty);
                else if (o.FromEntrySignal == "LongB")
                    qtyLongBOpen = Math.Max(0, qtyLongBOpen - execQty);
                else if (o.FromEntrySignal == "ShortA")
                    qtyShortAOpen = Math.Max(0, qtyShortAOpen - execQty);
                else if (o.FromEntrySignal == "ShortB")
                    qtyShortBOpen = Math.Max(0, qtyShortBOpen - execQty);
            }
            ReconcileLegOpenFlags();
            // Optional: diagnostic snapshot per execution
            LogPrint($"[LegQtyExec] o={o.Name} From={o.FromEntrySignal} execQty={execQty} " +
                     $"LA={qtyLongAOpen} LB={qtyLongBOpen} SA={qtyShortAOpen} SB={qtyShortBOpen} posQty={Position.Quantity}");
            // ===== END TRACK LEGS =====


            // IMPORTANT: Avoid undersized PT/SL on partials — only act on FULL fill for entry orders.
            // BUT: do not leave partial fills unprotected. Mark leg open and let the watchdog arm a stop now.
            if ((isLongEntryA || isShortEntryA || isLongEntryB || isShortEntryB) && o.OrderState == OrderState.PartFilled)
            {
                if (isLongEntryA)
                {
                    isLongALegOpen = true;
                    if (double.IsNaN(longAEntryPrice)) longAEntryPrice = o.AverageFillPrice;
                }
                else if (isShortEntryA)
                {
                    isShortALegOpen = true;
                    if (double.IsNaN(shortAEntryPrice)) shortAEntryPrice = o.AverageFillPrice;
                }
                else if (isLongEntryB)
                {
                    isLongBLegOpen = true;
                    if (double.IsNaN(longBEntryPrice)) longBEntryPrice = o.AverageFillPrice;
                }
                else if (isShortEntryB)
                {
                    isShortBLegOpen = true;
                    if (double.IsNaN(shortBEntryPrice)) shortBEntryPrice = o.AverageFillPrice;
                }

                // Let your existing watchdog place a provisional stop immediately
                EnsureLegStopsCompact();
                AuditProtection("OnExecutionUpdate", time);
                return;
            }

            if (isLongEntryA && o.OrderState == OrderState.Filled)
            {
                longAEntryUtc = StrategyNowUtc();
                longAPending = false;
                isLongALegOpen = true;

                longAEntryPrice = o.AverageFillPrice;
                longATargetPrice = Instrument.MasterInstrument.RoundToTickSize(longAEntryPrice + activeTargetATicks * TickSizeSafe);

                double pt = longATargetPrice;
                double sl = Instrument.MasterInstrument.RoundToTickSize(longAEntryPrice - activeStopTicks * TickSizeSafe);

                // Ensure we do not keep a partial-qty stop from earlier partial fills
                if (IsOrderActive(slALong))
                {
                    CancelOrder(slALong);
                    slALong = null;
                    longAStop = double.NaN;
                }

                var now = StrategyNowUtc();
                nextEnsureALongUtc = now.AddMilliseconds(EnsureCooldownMs);

                // Always submit with qty=0 to apply to remaining qty of this entry signal
                ptA = ExitLongLimit(
                    0,           // BarsInProgress
                    true,        // isLiveUntilCancelled (not "isExitOnClose")
                    0,           // 0 -> use remaining qty tied to "LongA"
                    pt,          // limit price
                    "PT_A",
                    "LongA");

                slALong = ExitLongStopMarket(0, true, 0, sl, "SL_A", "LongA");

                longAStop = sl;

                LogPrint($"28,{time} LongA FULL fill @ {longAEntryPrice} , PT_A={pt} SL_A={sl} , dynTA={activeTargetATicks} dynSLTicks={activeStopTicks}");
            }
            else if (isShortEntryA && o.OrderState == OrderState.Filled)
            {
                shortAEntryUtc = StrategyNowUtc();
                shortAPending = false;
                isShortALegOpen = true;

                shortAEntryPrice = o.AverageFillPrice;
                shortATargetPrice = Instrument.MasterInstrument.RoundToTickSize(shortAEntryPrice - activeTargetATicks * TickSizeSafe);

                double pt = shortATargetPrice;
                double sl = Instrument.MasterInstrument.RoundToTickSize(shortAEntryPrice + activeStopTicks * TickSizeSafe);

                if (IsOrderActive(slAShort))
                {
                    CancelOrder(slAShort);
                    slAShort = null;
                    shortAStop = double.NaN;
                }
                var now = StrategyNowUtc();
                nextEnsureAShortUtc = now.AddMilliseconds(EnsureCooldownMs);

                // Correct API: ExitShortLimit for short PT
                // Use qty=0 to apply to remaining qty of "ShortA"
                ptA = ExitShortLimit(
                    0,          // BarsInProgress
                    true,       // isLiveUntilCancelled
                    0,          // 0 -> use remaining qty tied to "ShortA"
                    pt,         // limit price (below entry)
                    "PT_A",
                    "ShortA");

                slAShort = ExitShortStopMarket(
                    0, true, 0, sl, "SL_A", "ShortA");

                shortAStop = sl;

                LogPrint($"29,{time} ShortA FULL fill @ {shortAEntryPrice} , PT_A={pt} SL_A={sl} , dynTA={activeTargetATicks} dynSLTicks={activeStopTicks}");
            }
            else if (isLongEntryB && o.OrderState == OrderState.Filled)
            {
                longBLastFillUtc = StrategyNowUtc();
                longBPending = false;
                isLongBLegOpen = true;

                bool noAOpen = !isLongALegOpen && !isShortALegOpen;
                shortB_BE_armed = noAOpen || (RunnerTrailsWithA ? shortATrailMode : false);

                longBEntryPrice = o.AverageFillPrice;

                noAOpen = !isLongALegOpen && !isShortALegOpen;
                longB_BE_armed = noAOpen || (RunnerTrailsWithA ? longATrailMode : false);

                // Changed: arm only if A is not open or (if coupling is enabled) A is actually in trail mode
                longB_BE_armed = (activeQtyA <= 0) || (RunnerTrailsWithA ? longATrailMode : false);

                double bid = SafeBid();
                double desired = longBEntryPrice - activeStopTicks * ts;
                double guard = Instrument.MasterInstrument.RoundToTickSize(bid - InitialStopGuardTicks * ts);
                double sl = Instrument.MasterInstrument.RoundToTickSize(Math.Min(desired, guard));
                slBLong = ExitLongStopMarket(0, true, 0, sl, "SL_B", "LongB");
                longBStop = sl;

                if (TargetBTicks > 0)
                {
                    double ptBPrice = Instrument.MasterInstrument.RoundToTickSize(longBEntryPrice + TargetBTicks * ts);
                    ptB = ExitLongLimit(0, true, 0, ptBPrice, "PT_B", "LongB");
                }

                LogPrint($"30,{time} LongB FULL fill @ {longBEntryPrice} , SL_B init={sl} , dynSLTicks={activeStopTicks} , armed={longB_BE_armed}");
            }


            else if (isShortEntryB && o.OrderState == OrderState.Filled)
            {
                shortBLastFillUtc = StrategyNowUtc();
                shortBPending = false;
                isShortBLegOpen = true;

                shortBEntryPrice = o.AverageFillPrice;

                // Changed: arm only if A is not open or (if coupling is enabled) A is actually in trail mode
                shortB_BE_armed = (activeQtyA <= 0) || (RunnerTrailsWithA ? shortATrailMode : false);

                double ask = SafeAsk();
                double desired = shortBEntryPrice + activeStopTicks * ts;
                double guard = Instrument.MasterInstrument.RoundToTickSize(ask + InitialStopGuardTicks * ts);
                double sl = Instrument.MasterInstrument.RoundToTickSize(Math.Max(desired, guard));
                slBShort = ExitShortStopMarket(0, true, 0, sl, "SL_B", "ShortB");
                shortBStop = sl;

                if (TargetBTicks > 0)
                {
                    double ptBPrice = Instrument.MasterInstrument.RoundToTickSize(shortBEntryPrice - TargetBTicks * ts);
                    ptB = ExitShortLimit(0, true, 0, ptBPrice, "PT_B", "ShortB");
                }

                LogPrint($"31,{time} ShortB FULL fill @ {shortBEntryPrice} , SL_B init={sl} , dynSLTicks={activeStopTicks} , armed={shortB_BE_armed}");
            }

            // === Trigger A to trail immediately when B's PT_B_TRAIL fills ===
            // Reuses ATrailTicks (falls back to 1 if 0/invalid). Triggers on first fill (full or partial).
            // === Trigger A to trail immediately when B's PT_B_TRAIL fills ===
            if ((o.Name == "PT_B_TRAIL") && (o.OrderState == OrderState.Filled || o.OrderState == OrderState.PartFilled))
            {
                int ticks = ATrailTicks > 0 ? ATrailTicks : 1;

                // Long side: B runner closed by PT_B_TRAIL -> trail Long A immediately
                if (o.FromEntrySignal == "LongB" && isLongALegOpen && !longA_ClosingRequested && !longATrailMode)
                {
                    CancelPTAIfActiveFor("LongA");

                    longATrailMode = true;
                    aTrailTicksOverrideLongA = ticks;

                    // INSERT THESE LINES RIGHT AFTER flipping A into trail mode:
                    runnerNextBLongUtc = StrategyNowUtc().AddMilliseconds(RunnerTightenDelayMs); // optional defer
                    LogPrint("[A-OnBPT] LongA switched to trailing; runner long defer applied");

                    double t = ts;
                    double bid = SafeBid();
                    double desired = RT(bid - ticks * t);
                    double guard = RT(bid - 1 * t);
                    ArmOrTightenStop(true, "LongA", "SL_A", desired, guard, ref slALong, ref longAStop, "A-OnBPT");

                    LogPrint("[A-OnBPT] LongB PT_B_TRAIL fill -> LongA switched to trailing");
                }

                // Short side: B runner closed by PT_B_TRAIL -> trail Short A immediately
                if (o.FromEntrySignal == "ShortB" && isShortALegOpen && !shortA_ClosingRequested && !shortATrailMode)
                {
                    CancelPTAIfActiveFor("ShortA");

                    shortATrailMode = true;
                    aTrailTicksOverrideShortA = ticks;

                    // INSERT THESE LINES RIGHT AFTER flipping A into trail mode:
                    runnerNextBShortUtc = StrategyNowUtc().AddMilliseconds(RunnerTightenDelayMs); // optional defer
                    LogPrint("[A-OnBPT] ShortA switched to trailing; runner short defer applied");

                    double t = ts;
                    double ask = SafeAsk();
                    double desired = RT(ask + ticks * t);
                    double guard = RT(ask + 1 * t);
                    ArmOrTightenStop(false, "ShortA", "SL_A", desired, guard, ref slAShort, ref shortAStop, "A-OnBPT");

                    LogPrint("[A-OnBPT] ShortB PT_B_TRAIL fill -> ShortA switched to trailing");
                }
            }




            // Keep your trailing/stop maintenance
            EnsureLegStopsCompact();
        }

        protected override void OnOrderUpdate(
        Order order,
        double limitPrice,
        double stopPrice,
        int quantity,
        int filled,
        double averageFillPrice,
        OrderState orderState,
        DateTime time,
        ErrorCode error,
        string nativeError)
        {
            if (order == null) return;
            var nowUtc = StrategyNowUtc();

            // part 5
            // Log entries lifecycle
            if (order.Name == "LongA" || order.Name == "LongB" || order.Name == "ShortA" || order.Name == "ShortB")
            {
                LogPrint($"34,{time} ENTRY , {order.Name} , Action={order.OrderAction} , State={orderState} , Qty={quantity} Filled={filled} , Avg={averageFillPrice} , Err={error} {nativeError}");


                if (orderState == OrderState.Filled || orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
                {
                    if (order.Name == "LongA") longAPending = false;
                    if (order.Name == "LongB") longBPending = false;
                    if (order.Name == "ShortA") shortAPending = false;
                    if (order.Name == "ShortB") shortBPending = false;
                }
            }

            // Track stop/target/exit refs by matching the callback order object
            // Track stop/target/exit refs by matching the callback order object (per leg and side)
            // Track stop/target/exit refs and refresh cached stop prices if a valid stopPrice arrives
            if (order.Name == "SL_A")
            {
                if (order.FromEntrySignal == "LongA")
                {
                    slALong = order;
                    if (stopPrice > 0 && !double.IsNaN(stopPrice) && !double.IsInfinity(stopPrice))
                        longAStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                }
                else if (order.FromEntrySignal == "ShortA")
                {
                    slAShort = order;
                    if (stopPrice > 0 && !double.IsNaN(stopPrice) && !double.IsInfinity(stopPrice))
                        shortAStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                }
            }
            else if (order.Name == "SL_B" || order.Name == "PT_B_TRAIL")
            {
                if (order.FromEntrySignal == "LongB")
                {
                    slBLong = order;
                    if (stopPrice > 0 && !double.IsNaN(stopPrice) && !double.IsInfinity(stopPrice))
                        longBStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                }
                else if (order.FromEntrySignal == "ShortB")
                {
                    slBShort = order;
                    if (stopPrice > 0 && !double.IsNaN(stopPrice) && !double.IsInfinity(stopPrice))
                        shortBStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                }
            }
            else if (order.Name == "PT_A")
            {
                if (order.FromEntrySignal == "LongA" || order.FromEntrySignal == "ShortA")
                    ptA = order;
            }
            else if (order.Name == "PT_B")
            {
                if (order.FromEntrySignal == "LongB" || order.FromEntrySignal == "ShortB")
                    ptB = order;
            }
            else if (order.Name == "SL_FAILSAFE")
            {
                // Track current object and latest price
                if (order.OrderAction == OrderAction.Sell)
                {
                    rescueStopLong = order;
                    rescueStopLongPrice = stopPrice;
                }
                else if (order.OrderAction == OrderAction.BuyToCover)
                {
                    rescueStopShort = order;
                    rescueStopShortPrice = stopPrice;
                }

                if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                {
                    if (order.OrderAction == OrderAction.Sell) { rescueStopLong = null; rescueStopLongPrice = double.NaN; }
                    else if (order.OrderAction == OrderAction.BuyToCover) { rescueStopShort = null; rescueStopShortPrice = double.NaN; }
                }
            }
            else if (order.Name == "ExitA")
            {
                if (order.FromEntrySignal == "LongA" || order.FromEntrySignal == "ShortA") exitA = order;
            }
            else if (order.Name == "ExitB" || order.Name == "ExitB_OnAStop" ||
                     order.Name == "ForceExit_OnReject")
            {
                if (order.FromEntrySignal == "LongB" || order.FromEntrySignal == "ShortB") exitB = order;
            }



            // Recover from rejections
            if (error == ErrorCode.OrderRejected)
            {
                LogPrint($"35,{time} {order.Name} for {order.FromEntrySignal} REJECTED. Attempting recovery. Native='{nativeError}'");
                double t = TickSizeSafe;
                bool isLongLeg = order.FromEntrySignal == "LongA" || order.FromEntrySignal == "LongB";

                if (order.Name == "SL_A" || order.Name == "SL_B" || order.Name == "PT_B_TRAIL")
                {
                    if (isLongLeg)
                    {
                        double bid = SafeBid();
                        double newStop = Instrument.MasterInstrument.RoundToTickSize(bid - 2 * t);
                        if (order.FromEntrySignal == "LongA")
                            slALong = ExitLongStopMarket(0, true, 0, newStop, order.Name, order.FromEntrySignal);
                        else
                            slBLong = ExitLongStopMarket(0, true, 0, newStop, order.Name, order.FromEntrySignal);

                        if (order.FromEntrySignal == "LongA") longAStop = newStop; else longBStop = newStop;
                        LogPrint($"37,{time} Recovery: resubmitted {order.Name} for {order.FromEntrySignal} at {newStop} (bid={bid}).");
                    }
                    else
                    {
                        double ask = SafeAsk();
                        double newStop = Instrument.MasterInstrument.RoundToTickSize(ask + 2 * t);
                        if (order.FromEntrySignal == "ShortA")
                            slAShort = ExitShortStopMarket(0, true, 0, newStop, order.Name, order.FromEntrySignal);
                        else
                            slBShort = ExitShortStopMarket(0, true, 0, newStop, order.Name, order.FromEntrySignal);

                        if (order.FromEntrySignal == "ShortA") shortAStop = newStop; else shortBStop = newStop;
                        LogPrint($"38,{time} Recovery: resubmitted {order.Name} for {order.FromEntrySignal} at {newStop} (ask={ask}).");
                    }
                }
                else
                {
                    // Force exit the specific leg on entry/target rejection (not a stop)
                    if (order.FromEntrySignal == "LongA") exitA = ExitLong("ForceExit_OnReject", "LongA");
                    else if (order.FromEntrySignal == "LongB") exitB = ExitLong("ForceExit_OnReject", "LongB");
                    else if (order.FromEntrySignal == "ShortA") exitA = ExitShort("ForceExit_OnReject", "ShortA");
                    else if (order.FromEntrySignal == "ShortB") exitB = ExitShort("ForceExit_OnReject", "ShortB");

                    LogPrint($"39,{time} Recovery: forced exit for {order.FromEntrySignal} due to {order.Name} rejection.");
                }
            }

            // PT_A filled: arm B (if configured) and mark A leg closed
            // When A closes
            if (order.Name == "PT_A" && orderState == OrderState.Filled)
            {

                if (order.FromEntrySignal == "LongA")
                {
                    longB_BE_armed = true;
                    isLongALegOpen = false; longA_ClosingRequested = false;
                    longATrailMode = false; aTrailTicksOverrideLongA = null;
                    longAStop = double.NaN;
                }
                else if (order.FromEntrySignal == "ShortA")
                {
                    shortB_BE_armed = true;
                    isShortALegOpen = false; shortA_ClosingRequested = false;
                    shortATrailMode = false; aTrailTicksOverrideShortA = null;
                    shortAStop = double.NaN;
                }
                ptA = null;
            }
            if (order.Name == "SL_A" && orderState == OrderState.Filled)
            {
                if (order.FromEntrySignal == "LongA")
                {
                    isLongALegOpen = false;
                    longA_ClosingRequested = false;
                    longATrailMode = false;
                    aTrailTicksOverrideLongA = null;
                    longAStop = double.NaN;
                    CancelPTAIfActiveFor("LongA");
                    ptA = null;
                }
                else if (order.FromEntrySignal == "ShortA")
                {
                    isShortALegOpen = false;
                    shortA_ClosingRequested = false;
                    shortATrailMode = false;
                    aTrailTicksOverrideShortA = null;
                    shortAStop = double.NaN;
                    CancelPTAIfActiveFor("ShortA");
                    ptA = null;
                }
            }
            if (orderState == OrderState.Filled && order.Name == "ExitA")
            {
                if (order.FromEntrySignal == "LongA")
                {
                    isLongALegOpen = false; longA_ClosingRequested = false; longAStop = double.NaN;
                    longATrailMode = false; aTrailTicksOverrideLongA = null;
                    CancelPTAIfActiveFor("LongA"); ptA = null;
                }
                else if (order.FromEntrySignal == "ShortA")
                {
                    isShortALegOpen = false; shortA_ClosingRequested = false; shortAStop = double.NaN;
                    shortATrailMode = false; aTrailTicksOverrideShortA = null;
                    CancelPTAIfActiveFor("ShortA"); ptA = null;
                }
            }

            //  ----  B-leg stop filled  ----------------------------------------------
            // Do NOT decide leg-open state here; executions will do that.
            // Only clear stop refs/caches when the B stop (or B trail) reaches a terminal Filled state.
            if ((order.Name == "SL_B" || order.Name == "PT_B_TRAIL") && orderState == OrderState.Filled)
            {
                if (order.FromEntrySignal == "LongB")
                {
                    slBLong = null;                // clear ref
                    longBStop = double.NaN;        // clear cached price
                    longB_BE_armed = false;        // BE state no longer relevant
                    longB_ClosingRequested = false;
                    // do NOT set isLongBLegOpen here; OnExecutionUpdate will reconcile via qtyLongBOpen
                }
                else if (order.FromEntrySignal == "ShortB")
                {
                    slBShort = null;
                    shortBStop = double.NaN;
                    shortB_BE_armed = false;
                    shortB_ClosingRequested = false;
                    // do NOT set isShortBLegOpen here; OnExecutionUpdate will reconcile via qtyShortBOpen
                }
            }



            // Stamp same-direction cooldown only when the stop (A or B) fills at a loss >= threshold
            // Stamp same-direction cooldown when SL fills; anchor to strategy clock
            if ((order.Name == "SL_A" || order.Name == "SL_B") && orderState == OrderState.Filled)
            {
                bool isLongLeg = order.FromEntrySignal == "LongA" || order.FromEntrySignal == "LongB";
                bool isShortLeg = order.FromEntrySignal == "ShortA" || order.FromEntrySignal == "ShortB";

                // Optional: only stamp if loss >= DropTradeToleranceTicks
                bool stamp = true;
                double entry = double.NaN;
                if (order.FromEntrySignal == "LongA") entry = longAEntryPrice;
                else if (order.FromEntrySignal == "LongB") entry = longBEntryPrice;
                else if (order.FromEntrySignal == "ShortA") entry = shortAEntryPrice;
                else if (order.FromEntrySignal == "ShortB") entry = shortBEntryPrice;

                if (!double.IsNaN(entry) && entry > 0 && TickSizeSafe > 0)
                {
                    double fill = averageFillPrice;
                    double t = TickSizeSafe;

                    double lossTicks = Math.Abs(entry - fill) / t;
                    bool actuallyLost = (isLongLeg && fill < entry) || (isShortLeg && fill > entry);

                    int threshold = Math.Max(0, DropTradeToleranceTicks);
                    stamp = lossTicks >= threshold;
                    if (!stamp)
                        LogPrint($"[CooldownSkip] {time:O} {order.Name} {order.FromEntrySignal} -> loss={lossTicks:F2}t < {threshold}t");
                }

                if (stamp)
                {
                    DateTime anchorUtc = StrategyNowUtc();
                    DateTime desiredExpiry = anchorUtc.AddSeconds(Math.Max(0, StopLossSameDirCooldownSec));

                    if (isLongLeg)
                    {
                        DateTime prev = longCooldownExpireUtc;
                        longCooldownExpireUtc = (prev > desiredExpiry) ? prev : desiredExpiry;
                        LogPrint($"[CDN_STAMP_EVT] ,{time:O} dir=LONG anchorUtc={anchorUtc:O} desiredExp={desiredExpiry:O} setExp={longCooldownExpireUtc:O}");
                    }
                    else if (isShortLeg)
                    {
                        DateTime prev = shortCooldownExpireUtc;
                        shortCooldownExpireUtc = (prev > desiredExpiry) ? prev : desiredExpiry;
                        LogPrint($"[CDN_STAMP_EVT], {time:O} dir=SHORT anchorUtc={anchorUtc:O} desiredExp={desiredExpiry:O} setExp={shortCooldownExpireUtc:O}");
                    }

                    double rem = SameDirCooldownRemainingSec(isLongLeg);
                    LogPrint($"[CDN_CHECK_POST], state={State} barT={Time[0]:O} dir={(isLongLeg ? "LONG" : "SHORT")} rem={rem:F2}s nowUtc={StrategyNowUtc():O}");
                }
            }


            // ExitA/ExitB fills: mark legs closed
            if (orderState == OrderState.Filled && order.Name == "ExitA")
            {
                if (order.FromEntrySignal == "LongA") { isLongALegOpen = false; longA_ClosingRequested = false; longAStop = double.NaN; }
                else if (order.FromEntrySignal == "ShortA") { isShortALegOpen = false; shortA_ClosingRequested = false; shortAStop = double.NaN; }
            }
            if (orderState == OrderState.Filled && (order.Name == "ExitB" || order.Name == "ExitB_OnAStop" || order.Name == "ForceExit_OnReject"))
            {
                if (order.FromEntrySignal == "LongB") { isLongBLegOpen = false; longB_ClosingRequested = false; longBStop = double.NaN; longB_BE_armed = false; longBEntryPrice = double.NaN; }
                else if (order.FromEntrySignal == "ShortB") { isShortBLegOpen = false; shortB_ClosingRequested = false; shortBStop = double.NaN; shortB_BE_armed = false; shortBEntryPrice = double.NaN; }
            }

            // A-leg stop filled -> optionally flatten runner B (config)
            if (order.Name == "SL_A" && (order.FromEntrySignal == "LongA" || order.FromEntrySignal == "ShortA"))
            {
                bool isPartial = orderState == OrderState.PartFilled || (filled > 0 && filled < quantity);
                bool flattenOnPartial = FlattenRunnerOnPartialAStop && isPartial;
                bool flattenOnFull = FlattenRunnerOnAStopFull && orderState == OrderState.Filled;

                if (flattenOnPartial || flattenOnFull)
                {
                    if (order.FromEntrySignal == "LongA")
                    {
                        LogPrint($"42,{time} SL_A {(isPartial ? "partial " : "")}fill (LongA). Exiting LongB runner (config: partial={FlattenRunnerOnPartialAStop}, full={FlattenRunnerOnAStopFull}).");
                        exitB = ExitLong("ExitB_OnAStop", "LongB");
                        longB_ClosingRequested = true;
                    }
                    else
                    {
                        LogPrint($"43,{time} SL_A {(isPartial ? "partial " : "")}fill (ShortA). Exiting ShortB runner (config: partial={FlattenRunnerOnPartialAStop}, full={FlattenRunnerOnAStopFull}).");
                        exitB = ExitShort("ExitB_OnAStop", "ShortB");
                        shortB_ClosingRequested = true;
                    }
                }
                else if (isPartial || orderState == OrderState.Filled)
                {
                    LogPrint($"44,{time} SL_A {(isPartial ? "partial " : "full ")}fill; runner KEPT (partialCfg={FlattenRunnerOnPartialAStop}, fullCfg={FlattenRunnerOnAStopFull}).");
                }
            }
            // Stop/Cancel maintenance for stops, with session-close guard and closing-requested suppression
            bool isStopAorB = order.Name == "SL_A" || order.Name == "SL_B" || order.Name == "PT_B_TRAIL";
            bool isFromKnown = order.FromEntrySignal == "LongA" || order.FromEntrySignal == "ShortA" ||
                               order.FromEntrySignal == "LongB" || order.FromEntrySignal == "ShortB";



            if ((order.Name == "SL_A" || order.Name == "SL_B" || order.Name == "PT_B_TRAIL") && orderState == OrderState.Cancelled)
            {
                if (!scheduleFailsafe && Position.MarketPosition != MarketPosition.Flat && !AnyLegStopActive())
                {
                    scheduleFailsafe = true;
                    LogPrint("[FailsafeSchedule] Stop cancelled, no proper stops active; scheduling failsafe.");
                }
            }


            if (isStopAorB && isFromKnown)
            {
                bool legStillOpen =
                    (order.FromEntrySignal == "LongA" && isLongALegOpen) ||
                    (order.FromEntrySignal == "LongB" && isLongBLegOpen) ||
                    (order.FromEntrySignal == "ShortA" && isShortALegOpen) ||
                    (order.FromEntrySignal == "ShortB" && isShortBLegOpen);

                bool closingRequested =
                    (order.FromEntrySignal == "LongA" && longA_ClosingRequested) ||
                    (order.FromEntrySignal == "LongB" && longB_ClosingRequested) ||
                    (order.FromEntrySignal == "ShortA" && shortA_ClosingRequested) ||
                    (order.FromEntrySignal == "ShortB" && shortB_ClosingRequested);

                // Partial re-arm only if leg still open and not in an OCO-close flow
                if (legStillOpen && !closingRequested && ReArmStopOnPartial
                     && (orderState == OrderState.PartFilled || (filled > 0 && filled < quantity)))
                {
                    // 1) Skip if an order change is already in flight
                    if (IsChangePending(order))
                    {
                        LogPrint($"[Partial] Skip re-arm for {order.FromEntrySignal}; change pending.");
                        return;
                    }

                    // 2) Optional: small throttle per leg to avoid churn
                    if (!CanRearmNow(order.FromEntrySignal, nowUtc))  // helper shown below
                    {
                        LogPrint($"[Partial] Throttled re-arm for {order.FromEntrySignal}.");
                        return;
                    }

                    double last = double.NaN;
                    if (order.FromEntrySignal == "LongA") last = longAStop;
                    else if (order.FromEntrySignal == "LongB") last = longBStop;
                    else if (order.FromEntrySignal == "ShortA") last = shortAStop;
                    else if (order.FromEntrySignal == "ShortB") last = shortBStop;

                    double stp = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                    if (stp <= 0 || double.IsNaN(stp) || double.IsInfinity(stp))
                        stp = last; // fallback

                    bool isLongLegP = order.FromEntrySignal == "LongA" || order.FromEntrySignal == "LongB";
                    bool changed = double.IsNaN(last) || Math.Abs(stp - last) >= TickSizeSafe * 0.5;

                    if (changed)
                    {
                        if (isLongLegP)
                        {
                            if (order.FromEntrySignal == "LongA")
                                slALong = ExitLongStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                            else
                                slBLong = ExitLongStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                        }
                        else
                        {
                            if (order.FromEntrySignal == "ShortA")
                                slAShort = ExitShortStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                            else
                                slBShort = ExitShortStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                        }

                        if (order.FromEntrySignal == "LongA") longAStop = stp;
                        else if (order.FromEntrySignal == "LongB") longBStop = stp;
                        else if (order.FromEntrySignal == "ShortA") shortAStop = stp;
                        else if (order.FromEntrySignal == "ShortB") shortBStop = stp;

                        NoteRearm(order.FromEntrySignal, nowUtc);  // record throttle timestamp
                        LogPrint($"45,{time} {order.Name} partial for {order.FromEntrySignal}. Re-armed stop at {stp} for remaining qty.");
                    }
                    else
                    {
                        LogPrint($"46,{time} {order.Name} partial for {order.FromEntrySignal}. Stop unchanged at {stp}, not resubmitting.");
                    }
                }


                // Stop cancelled while leg still open -> handle near-close/out-of-session/closingRequested or resubmit
                // Stop cancelled while leg still open -> handle out-of-session/closingRequested or resubmit
                if (orderState == OrderState.Cancelled && legStillOpen)
                {
                    if (closingRequested)
                    {
                        // We requested an exit and won't resubmit the stop; clear caches
                        if (order.FromEntrySignal == "LongA") { slALong = null; longAStop = double.NaN; }
                        else if (order.FromEntrySignal == "LongB") { slBLong = null; longBStop = double.NaN; }
                        else if (order.FromEntrySignal == "ShortA") { slAShort = null; shortAStop = double.NaN; }
                        else if (order.FromEntrySignal == "ShortB") { slBShort = null; shortBStop = double.NaN; }

                        LogPrint($"48,{time} {order.Name} cancelled for {order.FromEntrySignal} due to our Exit request; not resubmitting.");
                        return;
                    }
                    else
                    {
                        // Compute a safe stop price if stopPrice is invalid
                        double stp = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                        bool isLongLegP = order.FromEntrySignal == "LongA" || order.FromEntrySignal == "LongB";

                        if (stp <= 0 || double.IsNaN(stp) || double.IsInfinity(stp))
                        {
                            double t = TickSizeSafe;
                            if (isLongLegP)
                            {
                                double bid = SafeBid();
                                stp = Instrument.MasterInstrument.RoundToTickSize(bid - 2 * t);
                            }
                            else
                            {
                                double ask = SafeAsk();
                                stp = Instrument.MasterInstrument.RoundToTickSize(ask + 2 * t);
                            }
                        }

                        if (isLongLegP)
                        {
                            if (order.FromEntrySignal == "LongA") slALong = ExitLongStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                            else slBLong = ExitLongStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                        }
                        else
                        {
                            if (order.FromEntrySignal == "ShortA") slAShort = ExitShortStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                            else slBShort = ExitShortStopMarket(0, true, 0, stp, order.Name, order.FromEntrySignal);
                        }
                        if (order.FromEntrySignal == "LongA") longAStop = stp;
                        else if (order.FromEntrySignal == "LongB") longBStop = stp;
                        else if (order.FromEntrySignal == "ShortA") shortAStop = stp;
                        else if (order.FromEntrySignal == "ShortB") shortBStop = stp;

                        LogPrint($"49,{time} {order.Name} cancelled but leg still open for {order.FromEntrySignal}. Re-submitted stop at {stp}.");
                    }
                }


                // If stop reaches a terminal state, clear the matching ref
                if (orderState == OrderState.Filled || orderState == OrderState.Rejected)
                {
                    if (order.Name == "SL_A")
                    {
                        if (order.FromEntrySignal == "LongA") slALong = null;
                        if (order.FromEntrySignal == "ShortA") slAShort = null;
                    }
                    else if (order.Name == "SL_B" || order.Name == "PT_B_TRAIL")
                    {
                        if (order.FromEntrySignal == "LongB") slBLong = null;
                        if (order.FromEntrySignal == "ShortB") slBShort = null;
                    }

                }

            }

            EnsureLegStopsCompact();


            // Audit after we've reconciled order state
            AuditProtection("OnOrderUpdate", time);

            // Diagnostics for stop/target orders
            if (order.Name == "SL_A" || order.Name == "PT_A" || order.Name == "SL_B" || order.Name == "PT_B" || order.Name == "PT_B_TRAIL")
            {
                double stpShown = SafeShownStop(order, stopPrice);
                LogPrint($"50,{time} , {order.Name} , FromEntry={order.FromEntrySignal} , State={orderState} , Lmt={limitPrice} Stp={stpShown} , Qty={quantity} Filled={filled} , Error={error} {nativeError}");
            }




            // Clear entry/target/exit refs after terminal state
            if (orderState == OrderState.Filled || orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
            {
                if (order == longAEntry) longAEntry = null;
                if (order == longBEntryOrder) longBEntryOrder = null;
                if (order == shortAEntry) shortAEntry = null;
                if (order == shortBEntryOrder) shortBEntryOrder = null;

                if (order == ptA) ptA = null;
                if (order == ptB) ptB = null;
                if (order == exitA) exitA = null;
                if (order == exitB) exitB = null;
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.Configure)
            {
                try
                {
                    string baseDir = NinjaTrader.Core.Globals.UserDataDir; // Documents\NinjaTrader 8\
                    string folder = Path.Combine(baseDir, "strategy_logs");
                    Directory.CreateDirectory(folder);

                    string fileName = string.Format(
                        "{0}_{1}_{2}_{3}.csv",
                        Name ?? "Strategy",
                        Instrument?.FullName ?? "NA",
                        Account?.Name ?? "Acct",
                        DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    if (string.IsNullOrWhiteSpace(InstanceId))
                        InstanceId = Guid.NewGuid().ToString("N");
                    BarsRequiredToTrade = WarmupBars;
                    Calculate = UseOnEachTick ? Calculate.OnEachTick : Calculate.OnBarClose;
                    RealtimeErrorHandling = RTH.IgnoreAllErrors;

                    logPath = Path.Combine(folder, fileName);
                }
                catch (Exception ex)
                {
                    // Fallback to Output
                    Print($"[LogInitError] {ex.Message}");
                }
            }
            else if (State == State.DataLoaded)
            {
                try
                {
                    atrRunner7 = ATR(7);
                    instrumentName = Instrument?.FullName ?? "N/A";
                    // Register this instance
                    CBASTerminalRegistry.Register(InstanceId, this);

                    // Subscribe to terminal commands directed to this instance
                    CBASTerminalBus.OnCommand += HandleTerminalCommand;
                    logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    logInitialized = true;
                    // CSV header
                    WriteCsvLine("ts,method,bip,state,instrument,account,msg");

                }
                catch (Exception ex)
                {
                    Print($"[LogOpenError] {ex.Message}");
                }
            }
            else if (State == State.Realtime)
            {
                if (!realtimeClocksSanitized)
                {
                    SanitizeRealtimeClocks();
                    realtimeClocksSanitized = true;
                    LogPrint($"100_[RealtimeStart] nowUtc={DateTime.UtcNow:O} CD_longRem={SameDirCooldownRemainingSec(true):F1}s CD_shortRem={SameDirCooldownRemainingSec(false):F1}s");

                    // Realtime-only gate
                    realtimeEnteredUtc = DateTime.UtcNow;
                    if (TradeRealtimeOnly && RealtimeStartDelaySec > 0)
                        realtimeTradingAllowedUtc = realtimeEnteredUtc.AddSeconds(RealtimeStartDelaySec);
                    else
                        realtimeTradingAllowedUtc = realtimeEnteredUtc;

                    // Reset TPM counters at the moment we switch to real-time
                    lock (tpmSync)
                    {
                        tpmTicks.Clear();
                        tpmLastPublish = DateTime.UtcNow;
                    }
                    PublishLog($"[TT] A={ATrailTicks} B={RunnerTrailTicks}");
                }


            }
            else if (State == State.Terminated)
            {
                try
                {
                    // Unsubscribe from the command bus
                    CBASTerminalBus.OnCommand -= HandleTerminalCommand;

                    // Unregister this instance from the registry
                    if (!string.IsNullOrWhiteSpace(InstanceId))
                        CBASTerminalRegistry.Unregister(InstanceId);
                    if (string.IsNullOrWhiteSpace(InstanceId))
                        InstanceId = Guid.NewGuid().ToString("N");
                    lock (logLock)
                    {
                        logWriter?.Flush();
                        logWriter?.Dispose();
                        logWriter = null;
                        logInitialized = false;
                    }
                    lock (tpmSync)
                    {
                        tpmTicks.Clear();
                        tpmLastPublish = DateTime.MinValue;
                    }
                }
                catch (Exception ex)
                {
                    Print($"[LogCloseError] {ex.Message}");
                }
            }
            if (State == State.SetDefaults)
            {
                Name = "CBAS_SuperTrendStrategy";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 2;
                EntryHandling = EntryHandling.UniqueEntries;
                IsUnmanaged = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                TraceOrders = true;
                RealtimeErrorHandling = RTH.IgnoreAllErrors;
                InstanceId = string.Empty;
            }
            else if (State == State.DataLoaded)
            {
st = CBASTestingIndicator3(
    Input,
    showExitLabels: ShowExitLabels,                 // bool
    exitLabelAtrOffset: ExitLabelAtrOffset,         // double
    useRegimeStability: UseRegimeStability,
    regimeStabilityBars: RegimeStabilityBars,
    useScoringFilter: UseScoringFilter,
    scoreThreshold: ScoreThreshold,
    useSmoothedVpm: UseSmoothedVpm,
    vpmEmaSpan: VpmEmaSpan,
    minVpm: MinVpm,
    minAdx: MinAdx,
    momentumLookback: MomentumLookback,
    extendedLogging: ExtendedLogging,
    computeExitSignals: ComputeExitSignals,
    exitProfitAtrMult: ExitProfitAtrMult,
    instanceId: InstanceId,                         // string
    sensitivity: Sensitivity,                       // double
    emaEnergy: EmaEnergy,                           // bool
    keltnerLength: KeltnerLength,
    atrLength: AtrLength,
    rangeMinLength: RangeMinLength,
    rangeWidthMult: RangeWidthMult,                 // double
    rangeAtrLen: RangeAtrLen,
    enableLogging: EnableLogging,
    logSignalsOnly: LogSignalsOnly,
    heartbeatEveryNBars: HeartbeatEveryNBars,
    logFolder: LogFolder,                          // string
    scaleOscillatorToATR: ScaleOscillatorToATR,
    oscAtrMult: OscAtrMult,
    logDrawnSignals: LogDrawnSignals,
    colorBarsByTrend: ColorBarsByTrend,
    realtimeBullNetflowMin: RealtimeBullNetflowMin,
    realtimeBullObjectionMax: RealtimeBullObjectionMax,
    realtimeBullEmaColorMin: RealtimeBullEmaColorMin,
    realtimeBullUseAttract: RealtimeBullUseAttract,
    realtimeBullAttractMin: RealtimeBullAttractMin,
    realtimeBullScoreMin: RealtimeBullScoreMin,
    realtimeBearNetflowMax: RealtimeBearNetflowMax,
    realtimeBearObjectionMin: RealtimeBearObjectionMin,
    realtimeBearEmaColorMax: RealtimeBearEmaColorMax,
    realtimeBearUsePriceToBand: RealtimeBearUsePriceToBand,
    realtimeBearPriceToBandMax: RealtimeBearPriceToBandMax,
    realtimeBearScoreMin: RealtimeBearScoreMin,
    realtimeFlatTolerance: RealtimeFlatTolerance,
    showRealtimeStatePlot: ShowRealtimeStatePlot,
    plotRealtimeSignals: PlotRealtimeSignals,
    flipConfirmationBars: FlipConfirmationBars
);





                //st = CBASTestingIndicator3(InstanceId, Sensitivity, EmaEnergy, KeltnerLength, AtrLength, RangeMinLength, RangeWidthMult, RangeAtrLen, EnableLogging, LogSignalsOnly, HeartbeatEveryNBars, LogFolder);
                atr = ATR(14);
                if (ShowIndicator) AddChartIndicator(st);
                RefreshDynamicParameters();
            }
            else if (State == State.Configure || State == State.DataLoaded)
            {
                if (string.IsNullOrWhiteSpace(InstanceId))
                    InstanceId = Guid.NewGuid().ToString("N");
            }
            else if (State == State.Realtime && !allowLogging)
            {
                allowLogging = true; // just for testing
                LogPrint($"Test line at {Time[0]:O}");
            }
        }

        #endregion
        #region Entry/exit submission and gating
        private bool EntriesOnFirstTickOfBarOnly() => EntriesOnFirstTickOnly;
        private bool EntryWorking()
        {
            return IsOrderActive(longAEntry)
                || IsOrderActive(longBEntryOrder)
                || IsOrderActive(shortAEntry)
                || IsOrderActive(shortBEntryOrder);
        }
        private bool RealtimeEntriesBlocked(out double secondsRemaining)
        {
            secondsRemaining = 0;

            if (!TradeRealtimeOnly)
                return false;

            // Historical or transitioning before State.Realtime -> block entries
            if (State != State.Realtime)
            {
                secondsRemaining = double.PositiveInfinity;
                return true;
            }

            if (RealtimeStartDelaySec <= 0)
                return false;

            var now = DateTime.UtcNow;
            if (now < realtimeTradingAllowedUtc)
            {
                secondsRemaining = (realtimeTradingAllowedUtc - now).TotalSeconds;
                return true;
            }

            return false;
        }
        private void SubmitLongAB()
        {
            LogPrint($"16_{Time[0]} SubmitLongAB: QtyA={activeQtyA} QtyB={activeQtyB}");

            if (activeQtyA > 0)
            {
                longAEntry = EnterLong(activeQtyA, "LongA");
                lastEntrySignalA = "LongA";
                longAPending = longAEntry != null;
                if (longAEntry == null)
                    LogPrint($"17_{Time[0]} WARN: LongA submission returned null (likely ignored).");
            }
            if (activeQtyB > 0)
            {
                longBEntryOrder = EnterLong(activeQtyB, "LongB");
                lastEntrySignalB = "LongB";
                longBPending = longBEntryOrder != null;
                if (longBEntryOrder == null)
                    LogPrint($"18_{Time[0]} WARN: LongB submission returned null (likely ignored).");
            }

            lastEntrySubmitUtc = StrategyNowUtc();
        }
        private void SubmitShortAB()
        {
            LogPrint($"19_{Time[0]} SubmitShortAB: QtyA={activeQtyA} QtyB={activeQtyB}");

            if (activeQtyA > 0)
            {
                shortAEntry = EnterShort(activeQtyA, "ShortA");
                lastEntrySignalA = "ShortA";
                shortAPending = shortAEntry != null;
                if (shortAEntry == null)
                    LogPrint($"20_{Time[0]} WARN: ShortA submission returned null (likely ignored).");
            }
            if (activeQtyB > 0)
            {
                shortBEntryOrder = EnterShort(activeQtyB, "ShortB");
                lastEntrySignalB = "ShortB";
                shortBPending = shortBEntryOrder != null;
                if (shortBEntryOrder == null)
                    LogPrint($"21_{Time[0]} WARN: ShortB submission returned null (likely ignored).");
            }

            lastEntrySubmitUtc = StrategyNowUtc();
        }

        #endregion
        #region Stop/target computation and management

        private void ArmOrTightenStop(
    bool isLong,
    string fromEntry,        // "LongA" | "LongB" | "ShortA" | "ShortB" | "" (failsafe)
    string stopName,         // "SL_A" | "SL_B" | "SL_FAILSAFE" (keep one stable name for B: "SL_B")
    double desired,          // unrounded desired
    double guard,            // 1-tick guard (may be rounded already; ok)
    ref Order stopRef,       // working stop order ref
    ref double stopCache,    // cached last stop price
    string logTag
)
        {
            // Skip if a change/cancel is in-flight to avoid churn
            if (IsChangePending(stopRef) || IsCancelPending(stopRef))
                return;

            // Compute tighten-only target; single rounding happens here
            double newStop = isLong
                ? TightenLong(desired, guard, stopCache)
                : TightenShort(desired, guard, stopCache);

            double eps = Eps();
            bool needArm = !IsActiveOrWorking(stopRef);

            // Only arm or tighten materially; never loosen
            bool isTighter =
                needArm ||
                !IsValid(stopCache) ||
                (isLong ? newStop > stopCache + eps : newStop < stopCache - eps);

            if (!isTighter)
                return;

            // Submit/amend using qty=0 (Managed engine will amend and auto-resize to remaining position for FromEntry)
            stopRef = isLong
                ? ExitLongStopMarket(0, true, 0, newStop, stopName, fromEntry)
                : ExitShortStopMarket(0, true, 0, newStop, stopName, fromEntry);

            stopCache = newStop;
            LogPrint($"[{logTag}] {fromEntry} {stopName} -> {newStop}");
        }

        private void EnsureLegStopsCompact()
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;

            // 1) Orphan-stop cleanup if a leg is closed
            if (!isLongALegOpen && IsOrderActive(slALong)) { CancelOrder(slALong); slALong = null; longAStop = double.NaN; }
            if (!isShortALegOpen && IsOrderActive(slAShort)) { CancelOrder(slAShort); slAShort = null; shortAStop = double.NaN; }
            if (!isLongBLegOpen && IsOrderActive(slBLong)) { CancelOrder(slBLong); slBLong = null; longBStop = double.NaN; }
            if (!isShortBLegOpen && IsOrderActive(slBShort)) { CancelOrder(slBShort); slBShort = null; shortBStop = double.NaN; }

            // 2) Proceed with normal ensure/tighten logic
            BestPrices(out double bid, out double ask);
            DateTime now = StrategyNowUtc();
            double t = T();

            // Long A
            if (isLongALegOpen && !longA_ClosingRequested && now >= nextEnsureALongUtc)
            {
                if (longATrailMode)
                {
                    double desired = DesiredLongStopTicks(bid, activeStopTicks);
                    double guard = RT(bid - t);
                    ArmOrTightenStop(true, "LongA", "SL_A", desired, guard, ref slALong, ref longAStop, "EnsureA");
                }
                else
                {
                    double desired = !double.IsNaN(longAStop) && longAStop > 0
                        ? longAStop
                        : DesiredLongStopTicks(longAEntryPrice, activeStopTicks);
                    double guard = desired; // fixed risk: prevent tightening toward price
                    ArmOrTightenStop(true, "LongA", "SL_A", desired, guard, ref slALong, ref longAStop, "EnsureA-Fixed");
                }
                nextEnsureALongUtc = now.AddMilliseconds(EnsureCooldownMs);
            }

            // Long B
            if (isLongBLegOpen && !longB_ClosingRequested && now >= nextEnsureBLongUtc)
            {
                if (!longB_BE_armed)
                {
                    if (now >= nextBLongStopOpUtc && !IsChangePending(slBLong) && !IsCancelPending(slBLong))
                    {
                        double desired = (longBStop > 0 && !double.IsNaN(longBStop))
                            ? longBStop
                            : DesiredLongStopTicks(longBEntryPrice, activeStopTicks);
                        double guard = desired; // fixed risk
                        ArmOrTightenStop(true, "LongB", "SL_B", desired, guard, ref slBLong, ref longBStop, "EnsureB-Fixed");
                        nextBLongStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                    }
                }
                // else runner manager handles trailing
                nextEnsureBLongUtc = now.AddMilliseconds(EnsureCooldownMs);
            }

            // Short A
            if (isShortALegOpen && !shortA_ClosingRequested && now >= nextEnsureAShortUtc)
            {
                if (shortATrailMode)
                {
                    double desired = DesiredShortStopTicks(ask, activeStopTicks);
                    double guard = RT(ask + t);
                    ArmOrTightenStop(false, "ShortA", "SL_A", desired, guard, ref slAShort, ref shortAStop, "EnsureA");
                }
                else
                {
                    double desired = !double.IsNaN(shortAStop) && shortAStop > 0
                        ? shortAStop
                        : DesiredShortStopTicks(shortAEntryPrice, activeStopTicks);
                    double guard = desired;
                    ArmOrTightenStop(false, "ShortA", "SL_A", desired, guard, ref slAShort, ref shortAStop, "EnsureA-Fixed");
                }
                nextEnsureAShortUtc = now.AddMilliseconds(EnsureCooldownMs);
            }

            // Short B
            if (isShortBLegOpen && !shortB_ClosingRequested && now >= nextEnsureBShortUtc)
            {
                if (!shortB_BE_armed)
                {
                    if (now >= nextBShortStopOpUtc && !IsChangePending(slBShort) && !IsCancelPending(slBShort))
                    {
                        double desired = (shortBStop > 0 && !double.IsNaN(shortBStop))
                            ? shortBStop
                            : DesiredShortStopTicks(shortBEntryPrice, activeStopTicks);
                        double guard = desired; // fixed risk
                        ArmOrTightenStop(false, "ShortB", "SL_B", desired, guard, ref slBShort, ref shortBStop, "EnsureB-Fixed");
                        nextBShortStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                    }
                }
                nextEnsureBShortUtc = now.AddMilliseconds(EnsureCooldownMs);
            }
        }


        private void LogAConvert(string reason, string side, int effTicks, double last, double usedStop, double distToPT, double elapsedSec)
        {
            LogPrint($"[A-Convert] reason={reason} side={side} ticks={effTicks} last={last:F2} stop={usedStop:F2} distToPT={distToPT:F2} elapsed={elapsedSec:F0}s t={Time[0]:HH:mm:ss.fff}");
        }

        private void ManageATrail()
        {
            if (IsFirstTickOfBar && Position.MarketPosition == MarketPosition.Flat)
                return;

            DateTime nowUtc = StrategyNowUtc();
            double t = TickSizeSafe;

            // Per-side trail ticks
            int baseATicks = (ATrailTicks > 0 ? ATrailTicks : EffectiveRunnerTrailTicks());
            int longTrailTicks = (longATrailMode && aTrailTicksOverrideLongA.HasValue) ? aTrailTicksOverrideLongA.Value : baseATicks;
            int shortTrailTicks = (shortATrailMode && aTrailTicksOverrideShortA.HasValue) ? aTrailTicksOverrideShortA.Value : baseATicks;

            if (longTrailTicks <= 0 && shortTrailTicks <= 0)
                return;

            // Per-side delays (0 once in trail mode)
            int computedDelay = ComputeATrailDelaySec();
            int delaySecLong = longATrailMode ? 0 : computedDelay;
            int delaySecShort = shortATrailMode ? 0 : computedDelay;

            // Long A time-based conversion
            if (!longATrailMode && isLongALegOpen && !longA_ClosingRequested && longAEntryUtc != DateTime.MinValue)
            {
                double elapsed = (nowUtc - longAEntryUtc).TotalSeconds;
                if (elapsed >= delaySecLong)
                {
                    if (ptA != null && IsOrderActive(ptA) && ptA.FromEntrySignal == "LongA")
                        CancelOrder(ptA);

                    longATrailMode = true;
                    aTrailTicksOverrideLongA = baseATicks;

                    double bid = BidSafe();
                    double desired = DesiredLongStopTicks(bid, aTrailTicksOverrideLongA.Value);
                    double guard = RT(bid - t);
                    ArmOrTightenStop(true, "LongA", "SL_A", desired, guard, ref slALong, ref longAStop, "A-TimeTrail");

                    // One-time sync for LongB if open (with throttle + defers)
                    if (isLongBLegOpen && !longB_ClosingRequested)
                    {
                        DateTime now = StrategyNowUtc();
                        if (now >= nextBLongStopOpUtc && !IsChangePending(slBLong) && !IsCancelPending(slBLong))
                        {
                            bid = BidSafe();
                            double dB = DesiredLongStopTicks(bid, aTrailTicksOverrideLongA.Value);
                            double gB = RT(bid - T());
                            ArmOrTightenStop(true, "LongB", "SL_B", dB, gB, ref slBLong, ref longBStop, "B-SyncOnAConvert");

                            runnerNextBLongUtc = now.AddMilliseconds(Math.Max(RunnerTightenDelayMs, 50));
                            nextEnsureBLongUtc = now.AddMilliseconds(Math.Max(EnsureCooldownMs, 50));
                            nextBLongStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                        }
                    }

                    int eff = aTrailTicksOverrideLongA ?? baseATicks;
                    double last = BidSafe();
                    double used = double.IsNaN(longAStop) ? desired : longAStop;
                    double distToPT = double.IsNaN(longATargetPrice) ? double.NaN : (longATargetPrice - last) / t;
                    LogAConvert("time", "LongA", eff, last, used, distToPT, (nowUtc - longAEntryUtc).TotalSeconds);
                }
            }

            // Short A time-based conversion
            if (!shortATrailMode && isShortALegOpen && !shortA_ClosingRequested && shortAEntryUtc != DateTime.MinValue)
            {
                double elapsed = (nowUtc - shortAEntryUtc).TotalSeconds;
                if (elapsed >= delaySecShort)
                {
                    if (ptA != null && IsOrderActive(ptA) && ptA.FromEntrySignal == "ShortA")
                        CancelOrder(ptA);

                    shortATrailMode = true;
                    aTrailTicksOverrideShortA = baseATicks;

                    double ask = AskSafe();
                    double desired = DesiredShortStopTicks(ask, aTrailTicksOverrideShortA.Value);
                    double guard = RT(ask + t);
                    ArmOrTightenStop(false, "ShortA", "SL_A", desired, guard, ref slAShort, ref shortAStop, "A-TimeTrail");

                    // One-time sync for ShortB if open (with throttle + defers)
                    if (isShortBLegOpen && !shortB_ClosingRequested)
                    {
                        DateTime now = StrategyNowUtc();
                        if (now >= nextBShortStopOpUtc && !IsChangePending(slBShort) && !IsCancelPending(slBShort))
                        {
                            ask = AskSafe();
                            double dB = DesiredShortStopTicks(ask, aTrailTicksOverrideShortA.Value);
                            double gB = RT(ask + T());
                            ArmOrTightenStop(false, "ShortB", "SL_B", dB, gB, ref slBShort, ref shortBStop, "B-SyncOnAConvert");

                            runnerNextBShortUtc = now.AddMilliseconds(Math.Max(RunnerTightenDelayMs, 50));
                            nextEnsureBShortUtc = now.AddMilliseconds(Math.Max(EnsureCooldownMs, 50));
                            nextBShortStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                        }
                    }

                    int eff = aTrailTicksOverrideShortA ?? baseATicks;
                    double last = AskSafe();
                    double used = double.IsNaN(shortAStop) ? desired : shortAStop;
                    double distToPT = double.IsNaN(shortATargetPrice) ? double.NaN : (last - shortATargetPrice) / t;
                    LogAConvert("time", "ShortA", eff, last, used, distToPT, (nowUtc - shortAEntryUtc).TotalSeconds);
                }
            }

            // Throttle gate for A continuous updates (unchanged)
            if (nowUtc < aTrailNextUtc)
                return;

            // Continuous trailing updates (tighten-only) via ArmOrTightenStop

            // Long A trail
            if (isLongALegOpen && !longA_ClosingRequested && longAEntryUtc != DateTime.MinValue && longTrailTicks > 0)
            {
                double elapsed = (nowUtc - longAEntryUtc).TotalSeconds;
                if (elapsed >= delaySecLong)
                {
                    double bid = BidSafe();
                    double desired = DesiredLongStopTicks(bid, longTrailTicks);
                    double guard = RT(bid - t);
                    // Internal tighten-only and pending guards prevent churn
                    ArmOrTightenStop(true, "LongA", "SL_A", desired, guard, ref slALong, ref longAStop, "ATrail");
                }
            }

            // Short A trail
            if (isShortALegOpen && !shortA_ClosingRequested && shortAEntryUtc != DateTime.MinValue && shortTrailTicks > 0)
            {
                double elapsed = (nowUtc - shortAEntryUtc).TotalSeconds;
                if (elapsed >= delaySecShort)
                {
                    double ask = AskSafe();
                    double desired = DesiredShortStopTicks(ask, shortTrailTicks);
                    double guard = RT(ask + t);
                    ArmOrTightenStop(false, "ShortA", "SL_A", desired, guard, ref slAShort, ref shortAStop, "ATrail");
                }
            }

            aTrailNextUtc = nowUtc.AddMilliseconds(ATrailThrottleMs);
        }

        private void ManageATrailProximity()
        {
            if (!ConvertAToTrailOnProximity) return;

            double t = TickSizeSafe;
            int proxTicks = Math.Max(1, ProximityTicksToConvertA);
            int trailTicks = Math.Max(1, ProximityTrailTicks);

            // Long A proximity
            if (isLongALegOpen && !longA_ClosingRequested && ptA != null && IsOrderActive(ptA)
                && ptA.FromEntrySignal == "LongA" && !double.IsNaN(longATargetPrice))
            {
                double bid = BidSafe();
                double distTicks = (longATargetPrice - bid) / t; // bid to target
                if (distTicks <= proxTicks)
                {
                    CancelOrder(ptA);              // stop using fixed PT_A
                    longATrailMode = true;
                    aTrailTicksOverrideLongA = trailTicks;

                    double desired = DesiredLongStopTicks(bid, trailTicks); // unrounded
                    double guard = RT(bid - t);
                    ArmOrTightenStop(true, "LongA", "SL_A", desired, guard, ref slALong, ref longAStop, "A-ProxTrail");

                    // One-time sync for LongB if open (with throttle and defers)
                    if (isLongBLegOpen && !longB_ClosingRequested)
                    {
                        DateTime now = StrategyNowUtc();
                        if (now >= nextBLongStopOpUtc && !IsChangePending(slBLong) && !IsCancelPending(slBLong))
                        {
                            bid = BidSafe();
                            double dB = DesiredLongStopTicks(bid, trailTicks);
                            double gB = RT(bid - T());
                            ArmOrTightenStop(true, "LongB", "SL_B", dB, gB, ref slBLong, ref longBStop, "B-SyncOnAConvert");
                            // Defer runner and ensure to avoid immediate double-write
                            runnerNextBLongUtc = now.AddMilliseconds(Math.Max(RunnerTightenDelayMs, 50));
                            nextEnsureBLongUtc = now.AddMilliseconds(Math.Max(EnsureCooldownMs, 50));
                            nextBLongStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                        }
                    }

                    int eff = aTrailTicksOverrideLongA ?? trailTicks;
                    double last = BidSafe();
                    double used = double.IsNaN(longAStop) ? desired : longAStop;
                    double distToPT = (longATargetPrice - last) / t;
                    LogAConvert("proximity", "LongA", eff, last, used, distToPT, (StrategyNowUtc() - longAEntryUtc).TotalSeconds);
                }
            }

            // Short A proximity (fixed)
            if (isShortALegOpen && !shortA_ClosingRequested && ptA != null && IsOrderActive(ptA)
                && ptA.FromEntrySignal == "ShortA" && !double.IsNaN(shortATargetPrice))
            {
                double ask = AskSafe();
                double distTicks = (ask - shortATargetPrice) / t; // ask to target
                if (distTicks <= proxTicks)
                {
                    CancelOrder(ptA);
                    shortATrailMode = true;
                    aTrailTicksOverrideShortA = trailTicks;

                    double desired = DesiredShortStopTicks(ask, trailTicks); // unrounded
                    double guard = RT(ask + t);
                    ArmOrTightenStop(false, "ShortA", "SL_A", desired, guard, ref slAShort, ref shortAStop, "A-ProxTrail");

                    // One-time sync for ShortB if open (with throttle and defers)
                    if (isShortBLegOpen && !shortB_ClosingRequested)
                    {
                        DateTime now = StrategyNowUtc();
                        if (now >= nextBShortStopOpUtc && !IsChangePending(slBShort) && !IsCancelPending(slBShort))
                        {
                            ask = AskSafe();
                            double dB = DesiredShortStopTicks(ask, trailTicks);
                            double gB = RT(ask + T());
                            ArmOrTightenStop(false, "ShortB", "SL_B", dB, gB, ref slBShort, ref shortBStop, "B-SyncOnAConvert");
                            // Defer runner and ensure to avoid immediate double-write
                            runnerNextBShortUtc = now.AddMilliseconds(Math.Max(RunnerTightenDelayMs, 50));
                            nextEnsureBShortUtc = now.AddMilliseconds(Math.Max(EnsureCooldownMs, 50));
                            nextBShortStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                        }
                    }

                    int eff = aTrailTicksOverrideShortA ?? trailTicks;
                    double last = AskSafe();
                    double used = double.IsNaN(shortAStop) ? desired : shortAStop;
                    double distToPT = (last - shortATargetPrice) / t;
                    LogAConvert("proximity", "ShortA", eff, last, used, distToPT, (StrategyNowUtc() - shortAEntryUtc).TotalSeconds);
                }
            }
        }

        private void ManageRunnerStopsCompact()
        {
            if (activeQtyB <= 0 && QtyB <= 0)
                return;

            DateTime now = StrategyNowUtc();

            // Side-specific post-fill delay
            bool blockLongB = false, blockShortB = false;
            if (RunnerTightenDelayMs > 0)
            {
                blockLongB = isLongBLegOpen && longBLastFillUtc != DateTime.MinValue &&
                             (now - longBLastFillUtc).TotalMilliseconds < RunnerTightenDelayMs;

                blockShortB = isShortBLegOpen && shortBLastFillUtc != DateTime.MinValue &&
                              (now - shortBLastFillUtc).TotalMilliseconds < RunnerTightenDelayMs;
            }

            // Also honor defers set right after A->B sync
            if (now < runnerNextBLongUtc) blockLongB = true;
            if (now < runnerNextBShortUtc) blockShortB = true;

            // Side-aware, property-respecting arming.
            // If no A is open, keep armed for protection.
            // If RunnerTrailsWithA = true, only arm when the matching A side is actually trailing.
            bool aLongTrailing = isLongALegOpen && longATrailMode;
            bool aShortTrailing = isShortALegOpen && shortATrailMode;
            bool noAOpen = !isLongALegOpen && !isShortALegOpen;

            longB_BE_armed = noAOpen || (RunnerTrailsWithA ? aLongTrailing : false);
            shortB_BE_armed = noAOpen || (RunnerTrailsWithA ? aShortTrailing : false);

            // Optional: debug the arming state
            // After computing aLongTrailing/aShortTrailing and longB_BE_armed/shortB_BE_armed
            if (lastLongBArmed != longB_BE_armed || lastShortBArmed != shortB_BE_armed
                || lastALongTrailing != aLongTrailing || lastAShortTrailing != aShortTrailing)
            {
                LogPrint($"[RunnerArm] LAopen={isLongALegOpen} LAmode={longATrailMode} SAopen={isShortALegOpen} SAmode={shortATrailMode} " +
                                     $"armedLB={longB_BE_armed} armedSB={shortB_BE_armed} RunnerTrailsWithA={RunnerTrailsWithA} activeQtyA={activeQtyA}");

                lastLongBArmed = longB_BE_armed;
                lastShortBArmed = shortB_BE_armed;
                lastALongTrailing = aLongTrailing;
                lastAShortTrailing = aShortTrailing;
            }



            double t = T();

            int effRunnerTicks = EffectiveRunnerTrailTicks(); // If you prefer, snapshot per trade at fill

            // Long B runner
            if (!double.IsNaN(longBEntryPrice) &&
                        Position.MarketPosition == MarketPosition.Long)
            {
                if (!longB_BE_armed)
                    LogPrint("[RunnerSkip] LongB not armed");
                else if (blockLongB)
                    LogPrint("[RunnerSkip] LongB blocked (RunnerTightenDelay/defer)");
                else if (now < nextBLongStopOpUtc)
                    LogPrint("[RunnerSkip] LongB throttle");
                else if (IsChangePending(slBLong) || IsCancelPending(slBLong))
                    LogPrint("[RunnerSkip] LongB order pending");
                else
                {
                    if (now >= nextBLongStopOpUtc)
                    {
                        double bid = BidSafe();
                        effRunnerTicks = EffectiveRunnerTrailTicks(); // or your snapshot if implemented
                        double desired = DesiredLongStopTicks(bid, effRunnerTicks);
                        double guard = RT(bid - T());
                        LogPrint($"[RunnerDo] LongB tighten desired={desired} guard={guard} prev={longBStop}");
                        ArmOrTightenStop(true, "LongB", "SL_B", desired, guard, ref slBLong, ref longBStop, "Runner");
                        nextBLongStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                    }
                }
            }


            // Short B runner
            if (!double.IsNaN(shortBEntryPrice) &&
                Position.MarketPosition == MarketPosition.Short)
            {
                if (!shortB_BE_armed)
                    LogPrint("[RunnerSkip] ShortB not armed");
                else if (blockShortB)
                    LogPrint("[RunnerSkip] ShortB blocked (RunnerTightenDelay/defer)");
                else if (now < nextBShortStopOpUtc)
                    LogPrint("[RunnerSkip] ShortB throttle");
                else if (IsChangePending(slBShort) || IsCancelPending(slBShort))
                    LogPrint("[RunnerSkip] ShortB order pending");
                else
                {
                    if (now >= nextBShortStopOpUtc)
                    {
                        double ask = AskSafe();
                        effRunnerTicks = EffectiveRunnerTrailTicks(); // or snapshot
                        double desired = DesiredShortStopTicks(ask, effRunnerTicks);
                        double guard = RT(ask + T());
                        LogPrint($"[RunnerDo] ShortB tighten desired={desired} guard={guard} prev={shortBStop}");
                        ArmOrTightenStop(false, "ShortB", "SL_B", desired, guard, ref slBShort, ref shortBStop, "Runner");
                        nextBShortStopOpUtc = now.AddMilliseconds(StopOpThrottleMs);
                    }
                }
            }


            if (Position.MarketPosition == MarketPosition.Flat)
                ResetPositionState();
        }

        private double SafeShownStop(Order order, double eventStopPrice)
        {
            double stp = eventStopPrice;

            // 1) use the provided price if it looks valid
            if (stp > 0 && !double.IsNaN(stp) && !double.IsInfinity(stp))
                return Instrument.MasterInstrument.RoundToTickSize(stp);

            // 2) fall back to cached per-leg values (only if valid)
            if (order != null)
            {
                double candidate = double.NaN;

                if (order.Name == "SL_A")
                    candidate = order.FromEntrySignal == "LongA" ? longAStop : shortAStop;
                else if (order.Name == "SL_B" || order.Name == "PT_B_TRAIL")
                    candidate = order.FromEntrySignal == "LongB" ? longBStop : shortBStop;
                else if (order.Name == "SL_FAILSAFE")
                    candidate = (order.OrderAction == OrderAction.Sell) ? rescueStopLongPrice : rescueStopShortPrice;

                if (candidate > 0 && !double.IsNaN(candidate) && !double.IsInfinity(candidate))
                    return Instrument.MasterInstrument.RoundToTickSize(candidate);
            }
            if (stp > 0 && !double.IsNaN(stp) && !double.IsInfinity(stp))
                return Instrument.MasterInstrument.RoundToTickSize(stp);

            // 3) synthesize from current market if all else failed
            double t = TickSizeSafe;
            int ticks = Math.Max(1, activeStopTicks);

            if (order != null && (order.OrderAction == OrderAction.Sell || order.FromEntrySignal == "LongA" || order.FromEntrySignal == "LongB"))
            {
                double bid = SafeBid();
                stp = Instrument.MasterInstrument.RoundToTickSize(bid - ticks * t);
            }
            else
            {
                double ask = SafeAsk();
                stp = Instrument.MasterInstrument.RoundToTickSize(ask + ticks * t);
            }
            return stp;
        }
        private bool CanRearmNow(string fromEntry, DateTime nowUtc)
        {
            switch (fromEntry)
            {
                case "LongA": return nowUtc >= nextPartialRearmLongAUtc;
                case "LongB": return nowUtc >= nextPartialRearmLongBUtc;
                case "ShortA": return nowUtc >= nextPartialRearmShortAUtc;
                case "ShortB": return nowUtc >= nextPartialRearmShortBUtc;
                default: return true;
            }
        }


        #endregion
        #region Order/position state and maintenance


        // Replace or add this exact version
        private static bool IsOrderActive(Order o) =>
            o != null && (o.OrderState == OrderState.Working ||
                          o.OrderState == OrderState.Accepted ||
                          o.OrderState == OrderState.PartFilled ||
                          o.OrderState == OrderState.Submitted ||
                          o.OrderState == OrderState.ChangePending ||
                          o.OrderState == OrderState.ChangeSubmitted);

        private void ResetPositionState()
        {
            aTrailTicksOverrideLongA = null;
            aTrailTicksOverrideShortA = null;
            longATrailMode = false;
            shortATrailMode = false;

            // Clear runner state
            longBEntryPrice = double.NaN;
            shortBEntryPrice = double.NaN;
            longB_BE_armed = false;
            shortB_BE_armed = false;

            // Clear A-leg state
            longAEntryPrice = double.NaN;
            shortAEntryPrice = double.NaN;
            longATargetPrice = double.NaN;
            shortATargetPrice = double.NaN;

            // Clear stop price caches
            longBStop = double.NaN;
            shortBStop = double.NaN;
            longAStop = double.NaN;
            shortAStop = double.NaN;

            // Clear leg-open flags
            isLongALegOpen = false;
            isLongBLegOpen = false;
            isShortALegOpen = false;
            isShortBLegOpen = false;

            // Clear pending close flags
            longA_ClosingRequested = false;
            shortA_ClosingRequested = false;
            longB_ClosingRequested = false;
            shortB_ClosingRequested = false;

            // Keep pendingLong/pendingShort so reversals can trigger after flat

            // Clear entry order refs
            longAEntry = null;
            longBEntryOrder = null;
            shortAEntry = null;
            shortBEntryOrder = null;

            slALong = slAShort = slBLong = slBShort = null;
            exitA = null;
            exitB = null;

            // Clear pending-entry flags
            longAPending = false;
            longBPending = false;
            shortAPending = false;
            shortBPending = false;

            // Clear last submitted entry signal/time context
            lastEntrySignalA = null;
            lastEntrySignalB = null;
            lastEntrySubmitUtc = DateTime.MinValue;

            qtyLongAOpen = qtyLongBOpen = qtyShortAOpen = qtyShortBOpen = 0;
        }
        private void CancelPTAIfActiveFor(string entrySignal)
        {
            if (ptA != null && IsOrderActive(ptA) && ptA.FromEntrySignal == entrySignal)
                CancelOrder(ptA);
        }


        #endregion
        #region protection and watchdog
        private bool AnyLegStopActive() =>
IsOrderActive(slALong) || IsOrderActive(slAShort) || IsOrderActive(slBLong) || IsOrderActive(slBShort);
        private void AuditProtection(string tag, DateTime eventTime)
        {
            // Audit logging disabled to reduce output window spam
            return;
            
            /* Original audit code commented out
            bool slAActive = IsOrderActive(slALong) || IsOrderActive(slAShort);
            bool slBActive = IsOrderActive(slBLong) || IsOrderActive(slBShort);

            int posQty = Position.Quantity;
            bool aOpen = isLongALegOpen || isShortALegOpen;
            bool bOpen = isLongBLegOpen || isShortBLegOpen;

            // Consider small float noise equal if within half-tick
            double t = TickSizeSafe;
            bool sameLA = (double.IsNaN(auditLastLAStop) && double.IsNaN(longAStop)) ||
                          (!double.IsNaN(auditLastLAStop) && !double.IsNaN(longAStop) && Math.Abs(auditLastLAStop - longAStop) < 0.5 * t);
            bool sameLB = (double.IsNaN(auditLastLBStop) && double.IsNaN(longBStop)) ||
                          (!double.IsNaN(auditLastLBStop) && !double.IsNaN(longBStop) && Math.Abs(auditLastLBStop - longBStop) < 0.5 * t);
            bool sameSA = (double.IsNaN(auditLastSAStop) && double.IsNaN(shortAStop)) ||
                          (!double.IsNaN(auditLastSAStop) && !double.IsNaN(shortAStop) && Math.Abs(auditLastSAStop - shortAStop) < 0.5 * t);
            bool sameSB = (double.IsNaN(auditLastSBStop) && double.IsNaN(shortBStop)) ||
                          (!double.IsNaN(auditLastSBStop) && !double.IsNaN(shortBStop) && Math.Abs(auditLastSBStop - shortBStop) < 0.5 * t);

            bool changed =
                posQty != auditLastPosQty ||
                aOpen != auditLastAOpen ||
                bOpen != auditLastBOpen ||
                slAActive != auditLastSLA ||
                slBActive != auditLastSLB ||
                !sameLA || !sameLB || !sameSA || !sameSB;

            // Optional heartbeat
            bool heartbeat = false;
            if (AuditHeartbeatSec > 0)
            {
                var nowUtc = StrategyNowUtc();
                heartbeat = (auditLastLogUtc == DateTime.MinValue) ||
                            (nowUtc - auditLastLogUtc).TotalSeconds >= AuditHeartbeatSec;
            }

            if (changed || heartbeat)
            {
                bool rescueActive = IsOrderActive(rescueStopLong) || IsOrderActive(rescueStopShort);
                LogPrint($"[Audit] {tag} t={eventTime:O} PosQty={Position.Quantity} " +
                         $"Aopen={(isLongALegOpen || isShortALegOpen)} Bopen={(isLongBLegOpen || isShortBLegOpen)} " +
                         $"SL_A_active={(IsOrderActive(slALong) || IsOrderActive(slAShort))} SL_B_active={(IsOrderActive(slBLong) || IsOrderActive(slBShort))} " +
                         $"RescueActive={rescueActive} LA_Stop={longAStop} LB_Stop={longBStop} SA_Stop={shortAStop} SB_Stop={shortBStop} " +
                         $"armedLB={longB_BE_armed} armedSB={shortB_BE_armed}");

                // Update cache
                auditLastPosQty = posQty;
                auditLastAOpen = aOpen;
                auditLastBOpen = bOpen;
                auditLastSLA = slAActive;
                auditLastSLB = slBActive;
                auditLastLAStop = longAStop;
                auditLastLBStop = longBStop;
                auditLastSAStop = shortAStop;
                auditLastSBStop = shortBStop;
                auditLastLogUtc = StrategyNowUtc();
            }
            */
        }
        private void EnsureRescueProtection()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (IsOrderActive(rescueStopLong)) CancelOrder(rescueStopLong);
                if (IsOrderActive(rescueStopShort)) CancelOrder(rescueStopShort);
                rescueStopLong = rescueStopShort = null;
                rescueStopLongPrice = rescueStopShortPrice = double.NaN;
                return;
            }

            // If any leg stop is active, drop rescue stops
            if (AnyLegStopActive())
            {
                if (IsOrderActive(rescueStopLong)) CancelOrder(rescueStopLong);
                if (IsOrderActive(rescueStopShort)) CancelOrder(rescueStopShort);
                rescueStopLong = rescueStopShort = null;
                rescueStopLongPrice = rescueStopShortPrice = double.NaN;
                return;
            }

            BestPrices(out double bid, out double ask);
            double t = T();

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double desired = DesiredLongStopTicks(bid, activeStopTicks);
                double guard = RT(bid - t);
                ArmOrTightenStop(true, "", "SL_FAILSAFE", desired, guard, ref rescueStopLong, ref rescueStopLongPrice, "Rescue");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double desired = DesiredShortStopTicks(ask, activeStopTicks);
                double guard = RT(ask + t);
                ArmOrTightenStop(false, "", "SL_FAILSAFE", desired, guard, ref rescueStopShort, ref rescueStopShortPrice, "Rescue");
            }
        }

        #endregion
        #region Timing, delays, and cooldowns
        // Returns a non-negative trailing delay in seconds.
        // In Historical or before ATR is ready, falls back to the configured fixed delay.
        private int ComputeATrailDelaySec()
        {
            // Basic fixed delay fallback
            int fixedDelay = Math.Max(0, ATrailDelaySec);

            if (!ATrailUseAdaptive)
                return fixedDelay;

            // Guard: ATR series must exist and have data, and we must be in a bar context
            // where indexing <a href="" class="citation-link" target="_blank" style="vertical-align: super; font-size: 0.8em; margin-left: 3px;">[0]</a> is valid.
            if (atr == null || CurrentBar < 1)
                return fixedDelay;

            double atrVal = atr[0]; double t = TickSizeSafe;

            if (double.IsNaN(atrVal) || atrVal <= 0 || t <= 0)
                return fixedDelay;

            int trailTicks = ATrailTicks > 0 ? ATrailTicks : RunnerTrailTicks;
            if (trailTicks <= 0)
                trailTicks = 1;

            double raw = ATrailAdaptiveK * (atrVal / (trailTicks * t));
            int sec = (int)Math.Round(raw);

            // Clamp to configured min/max
            sec = Math.Max(ATrailMinSec, Math.Min(ATrailMaxSec, sec));
            return sec;
        }

        private bool IsSameDirCooldownActive(bool wantLong)
        {
            if (StopLossSameDirCooldownSec <= 0)
                return false;
            if (State != State.Realtime)
                return false; // do not gate in historical

            DateTime exp = wantLong ? longCooldownExpireUtc : shortCooldownExpireUtc;
            return exp != DateTime.MinValue && DateTime.UtcNow < exp;
        }

        private double SameDirCooldownRemainingSec(bool wantLong)
        {
            if (StopLossSameDirCooldownSec <= 0)
                return 0;
            if (State != State.Realtime)
                return 0; // do not gate in historical

            DateTime exp = wantLong ? longCooldownExpireUtc : shortCooldownExpireUtc;
            if (exp == DateTime.MinValue)
                return 0;

            double rem = (exp - DateTime.UtcNow).TotalSeconds;
            return rem > 0 ? rem : 0;
        }


        private void SanitizeRealtimeClocks()
        {
            DateTime now = DateTime.UtcNow;

            // Cap the future window
            double cap = Math.Max(StopLossSameDirCooldownSec * 2, 30);
            if (longCooldownExpireUtc > now.AddSeconds(cap))
                longCooldownExpireUtc = DateTime.MinValue;
            if (shortCooldownExpireUtc > now.AddSeconds(cap))
                shortCooldownExpireUtc = DateTime.MinValue;

            // Clear any stamps "in the future"
            if (lastEntryAttemptUtc > now) lastEntryAttemptUtc = DateTime.MinValue;
            if (lastEntrySubmitUtc > now) lastEntrySubmitUtc = DateTime.MinValue;
            if (lastFlatUtc > now) lastFlatUtc = DateTime.MinValue;
        }

        private DateTime StrategyNowUtc()
        {
            if (State == State.Realtime)
                return DateTime.UtcNow;
            var tz = Bars?.TradingHours?.TimeZoneInfo ?? TimeZoneInfo.Utc;
            return TimeZoneInfo.ConvertTimeToUtc(Time[0], tz);
        }

        #endregion
        #region  Market data and pricing utilities

        private double AskSafe() { BestPrices(out _, out var a); return a; }
        private double BidSafe() { BestPrices(out var b, out _); return b; }
        //private void BestPrices(out double bid, out double ask)
        //{
        //    bid = GetCurrentBid(); ask = GetCurrentAsk();
        //    bool badB = !IsValid(bid), badA = !IsValid(ask);
        //    double t = T(), last = (CurrentBar >= 0 ? Close[0] : 0);

        //    if (!badB && !badA) return;
        //    if (badB && !badA) { bid = Math.Max(ask - t, t); return; }
        //    if (!badB && badA) { ask = bid + t; return; }
        //    if (IsValid(last)) { bid = last - t; ask = last + t; }
        //    else { bid = Math.Max(t, t); ask = bid + t; }
        //}

        private void BestPrices(out double bid, out double ask)
        {
            // Try to read raw L1
            double rawBid = double.NaN, rawAsk = double.NaN;
            try { rawBid = GetCurrentBid(); } catch { }
            try { rawAsk = GetCurrentAsk(); } catch { }

            double tick = T();
            double last = (CurrentBar >= 0 && IsValid(Close[0])) ? Close[0] : double.NaN;
            bool bidValid = IsValid(rawBid) && rawBid > 0;
            bool askValid = IsValid(rawAsk) && rawAsk > 0;

            if (!bidValid && !askValid)
            {
                // Both invalid: synthesize around last or a neutral value
                if (!IsValid(last) || last <= 0)
                    last = 10 * tick; // minimal positive fallback

                rawBid = last - tick;
                rawAsk = last + tick;
            }
            else if (!bidValid && askValid)
            {
                // Only ask valid: synthesize bid one tick below ask (but keep > 0)
                rawBid = Math.Max(askValid ? rawAsk - tick : tick, tick);
            }
            else if (bidValid && !askValid)
            {
                // Only bid valid: synthesize ask one tick above bid
                rawAsk = rawBid + tick;
            }

            // If still inverted or spread too tight, normalize to at least 1 tick
            if (rawBid > rawAsk)
            {
                // Collapse to mid around last or average, then enforce 1 tick
                double mid = IsValid(last) && last > 0 ? last : (rawBid + rawAsk) * 0.5;
                rawBid = mid - tick * 0.5;
                rawAsk = mid + tick * 0.5;
            }

            // Enforce minimum 1-tick spread when we output
            if (!double.IsNaN(rawBid) && !double.IsNaN(rawAsk))
            {
                if (rawAsk - rawBid < tick)
                    rawAsk = rawBid + tick;
            }

            // Round to tick grid
            bid = RT(rawBid);
            ask = RT(rawAsk);

            // Final safety: ensure bid <= ask and at least 1 tick apart
            if (bid > ask) { double mid = (bid + ask) * 0.5; bid = RT(mid - tick * 0.5); ask = RT(mid + tick * 0.5); }
            if (ask - bid < tick) ask = RT(bid + tick);
        }






        private double SafeAsk()
        {
            double t = TickSizeSafe;

            // Level I ask in realtime (often 0 in historical)
            double ask = GetCurrentAsk();
            if (ask > 0 && !double.IsNaN(ask) && !double.IsInfinity(ask))
                return ask;

            // Fall back to last traded price (Close[0]) if available
            if (CurrentBar >= 0)
            {
                double last = Close[0];
                if (last > 0 && !double.IsNaN(last) && !double.IsInfinity(last))
                    return last + t; // nudge 1 tick up to behave like an ask
            }

            // Last resort
            return Math.Max(t, Instrument?.MasterInstrument?.TickSize ?? 0.25) + t;
        }
        private double SafeBid()
        {
            double t = TickSizeSafe;

            // Level I bid in realtime (often 0 in historical)
            double bid = GetCurrentBid();
            if (bid > 0 && !double.IsNaN(bid) && !double.IsInfinity(bid))
                return bid;

            // Fall back to last traded price (Close[0]) if available
            if (CurrentBar >= 0)
            {
                double last = Close[0];
                if (last > 0 && !double.IsNaN(last) && !double.IsInfinity(last))
                    return last - t; // nudge 1 tick down to behave like a bid
            }

            // Last resort: a minimal positive price to avoid invalid stops
            return Math.Max(t, Instrument?.MasterInstrument?.TickSize ?? 0.25);
        }
        #endregion
        #region Dynamic params, refresh Logging/diagnostics

        // CSV escaping: wrap in quotes if needed, double any quotes inside
        private static string Csv(string s)
        {
            if (s == null) return "";
            bool needs = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
            if (!needs) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        private void GateDump(string tag)
        {
            var nowUtc = StrategyNowUtc();
            string line =
                $"[GATE] {tag} barT={Time[0]:O} nowUtc={nowUtc:O} pos={Position.MarketPosition} " +
                $"firstTick={IsFirstTickOfBar} E1stOnly={EntriesOnFirstTickOnly} " +
                $"entryWorking={EntryWorking()} " +
                $"revCdMs={(lastFlatUtc == DateTime.MinValue ? -1 : (int)(nowUtc - lastFlatUtc).TotalMilliseconds)}/{ReverseCooldownMs} " +
                $"entCdMs={(lastEntryAttemptUtc == DateTime.MinValue ? -1 : (int)(nowUtc - lastEntryAttemptUtc).TotalMilliseconds)}/{EntryCooldownMs} " +
                $"CD_long={SameDirCooldownRemainingSec(true):F1}s CD_short={SameDirCooldownRemainingSec(false):F1}s";
            if (gateLastBarPrinted != CurrentBar)
            {
                LogPrint(line);
                gateLastBarPrinted = CurrentBar;
            }
        }

        protected void LogPrint(object message, [CallerMemberName] string caller = null)
        {
            // Turn logging on the first time realtime trading actually starts
            //if (!allowLogging) 123456789
            //{
            //    if (!ShouldStartLoggingNow())
            //        return;
            //    allowLogging = true;
            //}

            string msg = message == null ? "" : message.ToString();

            if (EchoToOutput)
                Print(msg);

            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string bip = BarsInProgress.ToString();
            string st = State.ToString();
            string inst = Instrument?.FullName ?? "";
            string acct = Account?.Name ?? "";

            string rec = string.Join(",",
                Csv(ts),
                Csv(caller ?? ""),
                Csv(bip),
                Csv(st),
                Csv(inst),
                Csv(acct),
                Csv(msg));

            try
            {
                if (logInitialized)
                {
                    lock (logLock)
                    {
                        logWriter?.WriteLine(rec);
                    }
                }
            }
            catch (Exception ex)
            {
                if (EchoToOutput)
                    Print($"[LogWriteError] {ex.Message}");
            }

            // Publish to terminal bus
            try
            {
                CBASTerminalBus.Publish(new CBASTerminalLogEntry
                {
                    InstanceId = string.IsNullOrWhiteSpace(InstanceId) ? "<unassigned>" : InstanceId,
                    Timestamp = DateTime.UtcNow,
                    Level = "INFO",
                    Instrument = inst,
                    Bar = CurrentBar,
                    Message = msg
                });
            }
            catch
            {
                // never throw from logging
            }
        }

        private bool ShouldStartLoggingNow()
        {
            // Must be in Realtime and not blocked by TradeRealtimeOnly/start delay
            if (State != State.Realtime) return false;
            if (RealtimeEntriesBlocked(out _)) return false;

            // Consider we are "trading" if we have a position or any working entry/stop/target/failsafe
            if (Position.MarketPosition != MarketPosition.Flat) return true;
            if (EntryWorking()) return true;
            if (AnyLegStopActive()) return true;
            if (IsOrderActive(rescueStopLong) || IsOrderActive(rescueStopShort)) return true;
            if (IsOrderActive(ptA) || IsOrderActive(ptB) || IsOrderActive(exitA) || IsOrderActive(exitB)) return true;

            return false;
        }
        private void RefreshDynamicParameters()
        {
            int newTargetA = TargetATicks;
            int newStop = StopTicks;
            int newQtyA = QtyA;
            int newQtyB = QtyB;

            if (UseVolumeDrivenSizing && CurrentBar >= Math.Max(VolumeLookback, BarsRequiredToTrade))
            {
                double sumVol = 0;
                int n = Math.Max(1, VolumeLookback);
                for (int i = 1; i <= n; i++)
                    sumVol += Volume[i];

                if (AutoTargetAFromVolume && TargetADivisor > 0)
                    newTargetA = (int)Math.Max(1, Math.Round(sumVol / TargetADivisor));

                if (AutoStopFromVolume && StopDivisor > 0)
                    newStop = (int)Math.Max(1, Math.Round(newTargetA / StopDivisor));

                if (AutoQtyFromVolume && QtyDivisor > 0)
                {
                    int totalQty = (int)Math.Round(sumVol / QtyDivisor);
                    totalQty = Math.Max(MinTotalQty, Math.Min(MaxTotalQty, totalQty));
                    int qtyAshare = Math.Max(0, Math.Min(100, QtyASharePercent));
                    newQtyA = (int)Math.Round(totalQty * qtyAshare / 100.0);
                    newQtyB = Math.Max(0, totalQty - newQtyA);

                    if (totalQty >= 2)
                    {
                        if (newQtyA == 0) { newQtyA = 1; newQtyB = Math.Max(0, totalQty - 1); }
                        if (newQtyB == 0) { newQtyB = 1; newQtyA = Math.Max(0, totalQty - 1); }
                    }
                }
            }
            //Part 2
            activeTargetATicks = newTargetA;
            activeStopTicks = newStop;
            activeQtyA = newQtyA;
            activeQtyB = newQtyB;

            if (activeTargetATicks != lastDynTargetA
        || activeStopTicks != lastDynStop
        || activeQtyA != lastDynQtyA
        || activeQtyB != lastDynQtyB)
            {
                LogPrint($"2_{Time[0]} DynSizing -> TA:{activeTargetATicks} SL:{activeStopTicks} QtyA:{activeQtyA} QtyB:{activeQtyB} (UseVol={UseVolumeDrivenSizing})");
                lastDynTargetA = activeTargetATicks;
                lastDynStop = activeStopTicks;
                lastDynQtyA = activeQtyA;
                lastDynQtyB = activeQtyB;
            }
        }
        private void NoteRearm(string fromEntry, DateTime nowUtc)
        {
            var next = nowUtc.AddMilliseconds(PartialRearmDelayMs);
            switch (fromEntry)
            {
                case "LongA": nextPartialRearmLongAUtc = next; break;
                case "LongB": nextPartialRearmLongBUtc = next; break;
                case "ShortA": nextPartialRearmShortAUtc = next; break;
                case "ShortB": nextPartialRearmShortBUtc = next; break;
            }
        }
        private void WriteCsvLine(string line)
        {
            try
            {
                lock (logLock)
                {
                    logWriter?.WriteLine(line);
                }
            }
            catch { /* ignore */ }
        }

        #endregion


        // Helper
        void ReconcileLegOpenFlags()
        {
            if (qtyLongBOpen <= 0 && isLongBLegOpen)
            {
                isLongBLegOpen = false; longB_ClosingRequested = false; longB_BE_armed = false; longBEntryPrice = double.NaN;
                if (IsOrderActive(slBLong)) { CancelOrder(slBLong); }
                slBLong = null; longBStop = double.NaN;
                // Optional snapshot/defer reset for long B:
                // bTrailTicksLongSnapshot = -1;
                runnerNextBLongUtc = DateTime.MinValue;
            }
            if (qtyShortBOpen <= 0 && isShortBLegOpen)
            {
                isShortBLegOpen = false; shortB_ClosingRequested = false; shortB_BE_armed = false; shortBEntryPrice = double.NaN;
                if (IsOrderActive(slBShort)) { CancelOrder(slBShort); }
                slBShort = null; shortBStop = double.NaN;
                // Optional snapshot/defer reset for short B:
                // bTrailTicksShortSnapshot = -1;
                runnerNextBShortUtc = DateTime.MinValue;
            }
            if (qtyLongAOpen <= 0 && isLongALegOpen)
            {
                isLongALegOpen = false; longA_ClosingRequested = false; longAStop = double.NaN; longATrailMode = false; aTrailTicksOverrideLongA = null;
                if (IsOrderActive(slALong)) { CancelOrder(slALong); }
                slALong = null;
            }
            if (qtyShortAOpen <= 0 && isShortALegOpen)
            {
                isShortALegOpen = false; shortA_ClosingRequested = false; shortAStop = double.NaN; shortATrailMode = false; aTrailTicksOverrideShortA = null;
                if (IsOrderActive(slAShort)) { CancelOrder(slAShort); }
                slAShort = null;
            }
        }
        private int EffectiveRunnerTrailTicks()
        {
            if (RunnerTrailTicks > 0) return RunnerTrailTicks;
            double t = TickSizeSafe;
            // Fallbacks to a minimum of 1 tick
            double avgRange = (atrRunner7 != null && CurrentBar > 7) ? atrRunner7[0] : Math.Max(Instrument.MasterInstrument.TickSize, High[0] - Low[0]);
            return Math.Max(1, (int)Math.Round(avgRange / t));
        }
        private static bool IsChangePending(Order o) =>
        o != null && (o.OrderState == OrderState.ChangePending || o.OrderState == OrderState.ChangeSubmitted);
        private static bool IsCancelPending(Order o) =>
            o != null && (o.OrderState == OrderState.CancelPending || o.OrderState == OrderState.CancelSubmitted);
        private static bool IsActiveOrWorking(Order o) =>
            o != null && (o.OrderState == OrderState.Working ||
                          o.OrderState == OrderState.Accepted ||
                          o.OrderState == OrderState.PartFilled ||
                          o.OrderState == OrderState.Submitted);
        // Return unrounded desired; single rounding happens in Tighten*
        private double DesiredLongStopTicks(double refPx, int ticks) => refPx - Math.Max(1, ticks) * T();
        private double DesiredShortStopTicks(double refPx, int ticks) => refPx + Math.Max(1, ticks) * T();
        // Never-loosen, single rounding at the end
        private double TightenLong(double desired, double guard, double current)
        {
            double cur = IsValid(current) ? current : double.NegativeInfinity;
            double candidate = Math.Min(desired, guard); // cannot be closer than guard
            double tightened = Math.Max(candidate, cur); // never go below current (never loosen)
            return RT(tightened);
        }
        private double TightenShort(double desired, double guard, double current)
        {
            double cur = IsValid(current) ? current : double.PositiveInfinity;
            double candidate = Math.Max(desired, guard); // cannot be closer than guard
            double tightened = Math.Min(candidate, cur); // never go above current (never loosen)
            return RT(tightened);
        }
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Only process in real-time
            if (State != State.Realtime || !tpmEnabled)
                return;

            // Only count for the primary instrument
            if (e.Instrument != Instrument)
                return;

            // Which updates to count
            bool isCountable =
                e.MarketDataType == MarketDataType.Last ||
                (tpmIncludeBidAsk && (e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask));

            if (!isCountable)
                return;

            var now = DateTime.UtcNow;
            var cutoff = now.AddSeconds(-60);

            lock (tpmSync)
            {
                tpmTicks.Enqueue(now);

                // Prune older than 60 seconds
                while (tpmTicks.Count > 0 && tpmTicks.Peek() < cutoff)
                    tpmTicks.Dequeue();

                // Publish at most once per second
                if ((now - tpmLastPublish).TotalSeconds >= 1)
                {
                    PublishLog($"[TPM] {tpmTicks.Count}");
                    tpmLastPublish = now;
                }
            }
        }


    }
}