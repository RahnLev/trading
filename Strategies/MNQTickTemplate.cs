#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;   // <-- needed for [Range], [Display]
using NinjaTrader.Cbi; // Needed if you later use OnOrderUpdate/OnExecutionUpdate
using System.ComponentModel.DataAnnotations; // <-- needed for [Range]

#endregion

// MNQ 200-Tick Enhanced Template
// Adds: time windows, 5-min EMA trend filter, volatility band, break-even and ATR trailing,
// daily loss cap and max trades per day.

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MNQ200Tick_ImprovedOM : Strategy
    {
        #region Inputs
        // Core
        [NinjaScriptProperty, Range(5,200)]
        [Display(Name="FastMAPeriod", GroupName="Core", Order=1)]
        public int FastMAPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(10,400)]
        [Display(Name="SlowMAPeriod", GroupName="Core", Order=2)]
        public int SlowMAPeriod { get; set; } = 50;

        [NinjaScriptProperty, Range(5,100)]
        [Display(Name="ATRPeriod", GroupName="Core", Order=3)]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.5,10)]
        [Display(Name="ATRStopMult", GroupName="Core", Order=4)]
        public double ATRStopMult { get; set; } = 1.75;

        [NinjaScriptProperty, Range(0.5,20)]
        [Display(Name="ATRTakeProfitMult", GroupName="Core", Order=5)]
        public double ATRTakeProfitMult { get; set; } = 4.0;

        [NinjaScriptProperty, Range(1,10)]
        [Display(Name="Contracts", GroupName="Core", Order=6)]
        public int Contracts { get; set; } = 1;

        // Filters
        [NinjaScriptProperty]
        [Display(Name="UseTimeWindows", GroupName="Filters", Order=1)]
        public bool UseTimeWindows { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Window1Start (HHmmss)", GroupName="Filters", Order=2)]
        public int Window1Start { get; set; } = 83500;   // 08:35:00

        [NinjaScriptProperty]
        [Display(Name="Window1End (HHmmss)", GroupName="Filters", Order=3)]
        public int Window1End { get; set; } = 103000;    // 10:30:00

        [NinjaScriptProperty]
        [Display(Name="Window2Start (HHmmss)", GroupName="Filters", Order=4)]
        public int Window2Start { get; set; } = 130000;  // 13:00:00

        [NinjaScriptProperty]
        [Display(Name="Window2End (HHmmss)", GroupName="Filters", Order=5)]
        public int Window2End { get; set; } = 143000;    // 14:30:00

        [NinjaScriptProperty]
        [Display(Name="UseHTFTrendFilter", GroupName="Filters", Order=6)]
        public bool UseHTFTrendFilter { get; set; } = true;

        [NinjaScriptProperty, Range(10,300)]
        [Display(Name="HTF_EMA_Period", GroupName="Filters", Order=7)]
        public int HTF_EMA_Period { get; set; } = 100;

        // Management
        [NinjaScriptProperty]
        [Display(Name="UseBreakEven", GroupName="Mgmt", Order=1)]
        public bool UseBreakEven { get; set; } = true;

        [NinjaScriptProperty, Range(0.5,5)]
        [Display(Name="BreakEvenATR", GroupName="Mgmt", Order=2)]
        public double BreakEvenATR { get; set; } = 1.75;

        [NinjaScriptProperty]
        [Display(Name="UseATRTrail", GroupName="Mgmt", Order=3)]
        public bool UseATRTrail { get; set; } = false;

        [NinjaScriptProperty, Range(0.5,5)]
        [Display(Name="TrailATRMult", GroupName="Mgmt", Order=4)]
        public double TrailATRMult { get; set; } = 1.5;

        // Risk
        [NinjaScriptProperty, Range(1,50)]
        [Display(Name="MaxTradesPerDay", GroupName="Risk", Order=1)]
        public int MaxTradesPerDay { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name="DailyLossLimit ($)", GroupName="Risk", Order=2)]
        public double DailyLossLimit { get; set; } = 200.0;

        [NinjaScriptProperty]
        [Display(Name="FlatAfterDailyLoss", GroupName="Risk", Order=3)]
        public bool FlatAfterDailyLoss { get; set; } = true;
        #endregion

        // Internals
        private SMA smaFast, smaSlow;
        private ATR atr;
        private EMA emaHTF;
        private int tradesToday = 0;
        private double sessionStartCumProfit = 0;
                private bool movedToBE = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MNQ200Tick_ImprovedOM";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 5;
                BarsRequiredToTrade = 150;
            }
            else if (State == State.Configure)
            {
                // Add ONE secondary series: 5-minute for HTF trend filter
                AddDataSeries(BarsPeriodType.Minute, 5); // BIP=1
            }
            else if (State == State.DataLoaded)
            {
                smaFast = SMA(FastMAPeriod);
                smaSlow = SMA(SlowMAPeriod);
                atr     = ATR(ATRPeriod);

                // Guard: only create EMA if secondary series exists
                if (BarsArray.Length > 1)
emaHTF = EMA(Closes[1], HTF_EMA_Period);            }
        }

        protected override void OnBarUpdate()
        {
            // Only run signals on primary (your chart/analyzer should be 200-tick)
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < Math.Max(SlowMAPeriod, ATRPeriod) + 2)
                return;

            // New session reset
            if (Bars.IsFirstBarOfSession)
            {
                tradesToday = 0;
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                movedToBE = false;
            }

            // Daily loss gate
            double sessionPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
            if (FlatAfterDailyLoss && sessionPnL <= -Math.Abs(DailyLossLimit))
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitPositions();
                return;
            }

            // Time window filter
            if (UseTimeWindows && !InTimeWindow())
                return;

            // HTF direction gating (guard for secondary bars availability)
            bool allowLong = true, allowShort = true;
if (UseHTFTrendFilter && BarsArray.Length > 1 && CurrentBars[1] > HTF_EMA_Period + 1 && emaHTF != null)            {
                allowLong  = Closes[1][0] > emaHTF[0];
                allowShort = Closes[1][0] < emaHTF[0];
                                if (!allowLong && !allowShort)
                    return;
            }

            bool bull = CrossAbove(smaFast, smaSlow, 1);
            bool bear = CrossBelow(smaFast, smaSlow, 1);

            // Compute stop/target distances in ticks (relative to entry) BEFORE placing entry
double atrPrev = atr[1];
            int stopTicks = Math.Max(1, (int)Math.Round((ATRStopMult * atrPrev) / Instrument.MasterInstrument.TickSize));
            int tpTicks   = Math.Max(1, (int)Math.Round((ATRTakeProfitMult * atrPrev) / Instrument.MasterInstrument.TickSize));

            // Manage BE/trailing for existing position
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (UseBreakEven && !movedToBE)
                {
                    double beTrigger = BreakEvenATR * atrPrev;
if (Position.MarketPosition == MarketPosition.Long && Close[0] - Position.AveragePrice >= beTrigger)                    {
                        SetStopLoss(CalculationMode.Price, Position.AveragePrice);
                        movedToBE = true;
                    }
else if (Position.MarketPosition == MarketPosition.Short && Position.AveragePrice - Close[0] >= beTrigger)                    {
                        SetStopLoss(CalculationMode.Price, Position.AveragePrice);
                        movedToBE = true;
                    }
                }

                if (UseATRTrail)
                {
                    int trailTicks = Math.Max(1, (int)Math.Round((TrailATRMult * atrPrev) / Instrument.MasterInstrument.TickSize));
                    SetTrailStop(CalculationMode.Ticks, trailTicks);
                }
            }

            // Entries with initial stop/target in ticks
            if (Position.MarketPosition == MarketPosition.Flat && tradesToday < MaxTradesPerDay)
            {
                if (bull && allowLong)
                {
                    SetStopLoss(CalculationMode.Ticks, stopTicks);
                    SetProfitTarget(CalculationMode.Ticks, tpTicks);
                    EnterLong(Contracts, "L");
                    movedToBE = false;
                    tradesToday++;
                }
                else if (bear && allowShort)
                {
                    SetStopLoss(CalculationMode.Ticks, stopTicks);
                    SetProfitTarget(CalculationMode.Ticks, tpTicks);
                    EnterShort(Contracts, "S");
                    movedToBE = false;
                    tradesToday++;
                }
            }
        }

        private bool InTimeWindow()
        {
int t = ToTime(Time[0]); // HHmmss
            bool w1 = t >= Window1Start && t <= Window1End;
            bool w2 = t >= Window2Start && t <= Window2End;
            return w1 || w2;
        }

        private void ExitPositions()
        {
            if (Position.MarketPosition == MarketPosition.Long) ExitLong();
            else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
        }
    }
}
