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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SimpleGradientLabels : Indicator
    {
        private EMA fastEma;
        private double lastFastEmaSlope = double.NaN;
        private double lastFastEmaGradDeg = double.NaN;

        [NinjaScriptProperty]
        [Display(Name = "Show Bar Index", Order = 1, GroupName = "Parameters")]
        public bool ShowBarIndex { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Gradient", Order = 2, GroupName = "Parameters")]
        public bool ShowGradient { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Order = 3, GroupName = "Parameters")]
        public int FastEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Gradient Lookback Bars", Order = 4, GroupName = "Parameters")]
        public int GradientLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Chart-Scaled Gradient", Order = 5, GroupName = "Parameters")]
        public bool UseChartScaledGradient { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Simple indicator that displays bar index and/or EMA gradient labels on bars";
                Name = "SimpleGradientLabels";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                ShowBarIndex = true;
                ShowGradient = true;
                FastEmaPeriod = 10;
                GradientLookbackBars = 2;
                UseChartScaledGradient = true;
            }
            else if (State == State.DataLoaded)
            {
                fastEma = EMA(FastEmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            // Calculate gradient if needed
            if (ShowGradient)
            {
                int gradWindow = Math.Max(2, GradientLookbackBars);
                double regDeg;
                lastFastEmaSlope = ComputeFastEmaGradient(gradWindow, out regDeg);

                double chartDeg = double.NaN;
                if (UseChartScaledGradient)
                {
                    chartDeg = ComputeChartScaledFastEmaDeg(gradWindow);
                }

                lastFastEmaGradDeg = !double.IsNaN(chartDeg) ? chartDeg : regDeg;
            }

            // Draw bar index label
            if (ShowBarIndex && CurrentBar >= 0)
            {
                string tag = "BarLabel_" + CurrentBar;
                double yPosition = High[0] + (6 * TickSize);
                Draw.Text(this, tag, CurrentBar.ToString(), 0, yPosition, Brushes.Black);
            }

            // Draw gradient label on the PREVIOUS bar (same as BarsOnTheFlow)
            if (ShowGradient && CurrentBar >= 1 && !double.IsNaN(lastFastEmaGradDeg))
            {
                string gradTag = "GradLabel_" + (CurrentBar - 1);
                double gradY = High[1] + (14 * TickSize);
                string gradText = lastFastEmaGradDeg.ToString("F1");
                Draw.Text(this, gradTag, gradText, 1, gradY, Brushes.Black);
            }
        }

        private double ComputeFastEmaGradient(int window, out double degrees)
        {
            degrees = double.NaN;
            if (fastEma == null || CurrentBar < window)
                return double.NaN;

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            int n = window;
            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = fastEma[window - 1 - i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }
            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-10)
                return double.NaN;

            double slope = (n * sumXY - sumX * sumY) / denom;
            degrees = Math.Atan(slope) * (180.0 / Math.PI);
            return slope;
        }

        private double ComputeChartScaledFastEmaDeg(int window)
        {
            if (fastEma == null || CurrentBar < window || ChartControl == null || ChartBars == null || ChartPanel == null)
                return double.NaN;

            try
            {
                double emaOld = fastEma[window];
                double emaRecent = fastEma[1];
                double priceChange = emaRecent - emaOld;

                int barOld = CurrentBar - window;
                int barRecent = CurrentBar - 1;

                double x1 = ChartControl.GetXByBarIndex(ChartBars, barOld);
                double x2 = ChartControl.GetXByBarIndex(ChartBars, barRecent);
                double pixelDistance = x2 - x1;

                if (pixelDistance <= 0)
                    return double.NaN;

                double panelHeight = ChartPanel.H;
                double priceMax = ChartPanel.MaxValue;
                double priceMin = ChartPanel.MinValue;
                double priceRange = priceMax - priceMin;

                if (priceRange <= 0 || panelHeight <= 0)
                    return double.NaN;

                // Convert price to Y pixel (screen Y increases downward, so higher price = lower Y)
                double y1 = panelHeight * (priceMax - emaOld) / priceRange;
                double y2 = panelHeight * (priceMax - emaRecent) / priceRange;
                double pixelHeight = Math.Abs(y2 - y1);

                double angleRad = Math.Atan2(pixelHeight, pixelDistance);
                double angleMagnitude = angleRad * (180.0 / Math.PI);

                double angleDeg = priceChange >= 0 ? angleMagnitude : -angleMagnitude;

                return angleDeg;
            }
            catch
            {
                return double.NaN;
            }
        }

        public override string DisplayName
        {
            get { return Name; }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SimpleGradientLabels[] cacheSimpleGradientLabels;
		public SimpleGradientLabels SimpleGradientLabels(bool showBarIndex, bool showGradient, int fastEmaPeriod, int gradientLookbackBars, bool useChartScaledGradient)
		{
			return SimpleGradientLabels(Input, showBarIndex, showGradient, fastEmaPeriod, gradientLookbackBars, useChartScaledGradient);
		}

		public SimpleGradientLabels SimpleGradientLabels(ISeries<double> input, bool showBarIndex, bool showGradient, int fastEmaPeriod, int gradientLookbackBars, bool useChartScaledGradient)
		{
			if (cacheSimpleGradientLabels != null)
				for (int idx = 0; idx < cacheSimpleGradientLabels.Length; idx++)
					if (cacheSimpleGradientLabels[idx] != null && cacheSimpleGradientLabels[idx].ShowBarIndex == showBarIndex && cacheSimpleGradientLabels[idx].ShowGradient == showGradient && cacheSimpleGradientLabels[idx].FastEmaPeriod == fastEmaPeriod && cacheSimpleGradientLabels[idx].GradientLookbackBars == gradientLookbackBars && cacheSimpleGradientLabels[idx].UseChartScaledGradient == useChartScaledGradient && cacheSimpleGradientLabels[idx].EqualsInput(input))
						return cacheSimpleGradientLabels[idx];
			return CacheIndicator<SimpleGradientLabels>(new SimpleGradientLabels(){ ShowBarIndex = showBarIndex, ShowGradient = showGradient, FastEmaPeriod = fastEmaPeriod, GradientLookbackBars = gradientLookbackBars, UseChartScaledGradient = useChartScaledGradient }, input, ref cacheSimpleGradientLabels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SimpleGradientLabels SimpleGradientLabels(bool showBarIndex, bool showGradient, int fastEmaPeriod, int gradientLookbackBars, bool useChartScaledGradient)
		{
			return indicator.SimpleGradientLabels(Input, showBarIndex, showGradient, fastEmaPeriod, gradientLookbackBars, useChartScaledGradient);
		}

		public Indicators.SimpleGradientLabels SimpleGradientLabels(ISeries<double> input , bool showBarIndex, bool showGradient, int fastEmaPeriod, int gradientLookbackBars, bool useChartScaledGradient)
		{
			return indicator.SimpleGradientLabels(input, showBarIndex, showGradient, fastEmaPeriod, gradientLookbackBars, useChartScaledGradient);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SimpleGradientLabels SimpleGradientLabels(bool showBarIndex, bool showGradient, int fastEmaPeriod, int gradientLookbackBars, bool useChartScaledGradient)
		{
			return indicator.SimpleGradientLabels(Input, showBarIndex, showGradient, fastEmaPeriod, gradientLookbackBars, useChartScaledGradient);
		}

		public Indicators.SimpleGradientLabels SimpleGradientLabels(ISeries<double> input , bool showBarIndex, bool showGradient, int fastEmaPeriod, int gradientLookbackBars, bool useChartScaledGradient)
		{
			return indicator.SimpleGradientLabels(input, showBarIndex, showGradient, fastEmaPeriod, gradientLookbackBars, useChartScaledGradient);
		}
	}
}

#endregion
