#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Strategies;
#endregion


// ──────────────────────────────────────────────────────────────
//  Cory SuperTrend Strategy   –  NinjaTrader 8                  //
//  (pure SuperTrend entries + ATR stop, session filter)         //
// ──────────────────────────────────────────────────────────────
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CorySuperTrendStrategy : Strategy
    {
        /* ───── Pine-equivalent inputs ───────────────────────── */
        [NinjaScriptProperty, Range(0.5, 10)]
        [Display(Name = "Sensitivity", Order = 1)]
        public double Sensitivity { get; set; } = 0.5;          // “factor” in Pine

        [NinjaScriptProperty, Display(Name = "Keltner Length", Order = 2)]
        public int KelLen { get; set; } = 10;

        [NinjaScriptProperty, Display(Name = "ATR Length (SuperTrend)", Order = 3)]
        public int AtrLenST { get; set; } = 10;

        /* Dynamic ATR stop */
        [NinjaScriptProperty, Display(Name = "ATR-SL Period", Order = 4)]
        public int AtrSLPeriod { get; set; } = 14;

        [NinjaScriptProperty, Display(Name = "ATR-SL Mult", Order = 5)]
        public double AtrSLMult { get; set; } = 2.0;

        /* End-of-day flatten */
        [NinjaScriptProperty, Display(Name = "Flatten at 23:59", Order = 6)]
        public bool EodClose { get; set; } = true;

        /* ───── internal series / vars ───────────────────────── */
        private Series<double> _st, _ub, _lb;  // supertrend + raw bands
        private double _longSL  = double.NaN;
        private double _shortSL = double.NaN;
        private int    _prevQty = 0;

        /* ───── strategy life-cycle ───────────────────────────── */
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                = "CorySuperTrendStrategy";
                Description                         = "SuperTrend cross strategy (port from Pine).";
                Calculate                           = Calculate.OnEachTick;
                EntriesPerDirection                 = 1;
                EntryHandling                       = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy        = false;
                BarsRequiredToTrade                 = 50;
            }
            else if (State == State.DataLoaded)
            {
                _st = new Series<double>(this);
                _ub = new Series<double>(this);
                _lb = new Series<double>(this);
            }
        }

        /* ───── main bar logic ───────────────────────────────── */
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(AtrLenST, AtrSLPeriod))
                return;

            /* 1) --- SuperTrend calculation (identical to Pine) --- */
            double upperK = SMA(Highs[0], KelLen)[0] + (High[0] - Low[0]);
            double lowerK = SMA(Lows [0], KelLen)[0] - (High[0] - Low[0]);
            double rangeC = upperK - lowerK;

            double ub = Close[0] + Sensitivity * rangeC;
            double lb = Close[0] - Sensitivity * rangeC;

            if (CurrentBar > 0)
            {
                ub = (ub < _ub[1] || Close[1] > _ub[1]) ? ub : _ub[1];
                lb = (lb > _lb[1] || Close[1] < _lb[1]) ? lb : _lb[1];
            }

            _ub[0] = ub;
            _lb[0] = lb;

            int dir = 1;
            if (CurrentBar == 0)
                dir = 1;
            else if (_st[1] == _ub[1])
                dir = Close[0] > ub ? -1 : 1;
            else
                dir = Close[0] < lb ? 1 : -1;

            _st[0] = dir == -1 ? lb : ub;

            /* 2) --- Session filter (Mon-Fri, 01:00-23:59 Jerusalem) */
            if (!InTradingSession())
            {
                _prevQty = Position.Quantity;
                return;
            }

            /* 3) --- Entry signals --------------------------------- */
            bool bull = CrossAbove(Close, _st, 1);
            bool bear = CrossBelow(Close, _st, 1);

            if (bull)
                EnterLong("Long");
            if (bear)
                EnterShort("Short");

            /* 4) --- Dynamic ATR stop ------------------------------ */
            double atrSL = ATR(AtrSLPeriod)[0] * AtrSLMult;
            bool freshLong  = Position.MarketPosition == MarketPosition.Long  && _prevQty == 0;
            bool freshShort = Position.MarketPosition == MarketPosition.Short && _prevQty == 0;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (freshLong)
                    _longSL = Position.AveragePrice - atrSL;
                else
                    _longSL = Math.Max(_longSL, Position.AveragePrice - atrSL);

                ExitLongStopMarket(0, true, Position.Quantity, _longSL, "L-SL", "Long");
            }
            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (freshShort)
                    _shortSL = Position.AveragePrice + atrSL;
                else
                    _shortSL = Math.Min(_shortSL, Position.AveragePrice + atrSL);

                ExitShortStopMarket(0, true, Position.Quantity, _shortSL, "S-SL", "Short");
            }
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                _longSL = _shortSL = double.NaN;
            }

            /* 5) --- End-of-day flatten (23:59 Jerusalem) ---------- */
            if (EodClose && IsTime(23, 59))
                FlattenAll();

            /* track previous position size */
            _prevQty = Position.Quantity;
        }

        /* ───── helper: allowed trading window ───────────────── */
        private bool InTradingSession()
        {
            DateTime t = Time[0];                         // chart TZ
            if (t.DayOfWeek == DayOfWeek.Saturday || t.DayOfWeek == DayOfWeek.Sunday)
                return false;

            int hhmm = t.Hour * 100 + t.Minute;
            return hhmm >= 100 && hhmm <= 2359;           // 01:00-23:59
        }

        private bool IsTime(int hour, int minute)
        {
            DateTime t = Time[0];
            return t.Hour == hour && t.Minute == minute;
        }

        private void FlattenAll()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("EOD", "Long");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("EOD", "Short");
        }
    }
}
