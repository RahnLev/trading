using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;          // ToDxBrush
using NinjaTrader.NinjaScript;
using SharpDX;
// Avoid Brush ambiguity
using MediaBrush = System.Windows.Media.Brush;
using DxBrush    = SharpDX.Direct2D1.Brush;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class StackedBuySellVolume : Indicator
    {
        // Live accumulators for the developing bar
        private double bidVolCur, askVolCur;

    // Stored results per finished bar
    private DoubleBuffer bidHist;
    private DoubleBuffer askHist;

    // Real-time tracking
    private bool isHistorical;
    private double lastBid, lastAsk, lastLastPrice;

    // Brushes
    [NinjaScriptProperty, XmlIgnore]
    [Display(Name = "AskBrush", GroupName = "Visual", Order = 1)]
    public MediaBrush AskBrush { get; set; } = System.Windows.Media.Brushes.ForestGreen;
    [Browsable(false)]
    public string AskBrushSerialize { get => Serialize.BrushToString(AskBrush); set => AskBrush = Serialize.StringToBrush(value); }

    [NinjaScriptProperty, XmlIgnore]
    [Display(Name = "BidBrush", GroupName = "Visual", Order = 2)]
    public MediaBrush BidBrush { get; set; } = System.Windows.Media.Brushes.Crimson;
    [Browsable(false)]
    public string BidBrushSerialize { get => Serialize.BrushToString(BidBrush); set => BidBrush = Serialize.StringToBrush(value); }

    // Visual
    [NinjaScriptProperty]
    [Range(0.5, 10)]
    [Display(Name = "Bar width factor", GroupName = "Visual", Order = 3)]
    public double BarWidthFactor { get; set; } = 2.0;

    [NinjaScriptProperty]
    [Range(0.0, 3.0)]
    [Display(Name = "Min segment pixels", GroupName = "Visual", Order = 4)]
    public double MinSegmentPixels { get; set; } = 1.0;

    // History handling (live-focused)
    [NinjaScriptProperty]
    [Display(Name = "Historical fallback (single-color)", GroupName = "History", Order = 1)]
    public bool UseHistoricalFallback { get; set; } = true;

    // Count choice
    [NinjaScriptProperty]
    [Display(Name = "Count trades (instead of volume)", GroupName = "Data", Order = 1)]
    public bool CountTrades { get; set; } = false; // false = contracts (volume), true = trades

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name = "StackedBuySellVolume";
            IsOverlay = false;
            Calculate = Calculate.OnEachTick;
            DisplayInDataBox = true;
            PaintPriceMarkers = false;
            BarsRequiredToPlot = 0;
            MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
            // No hidden plot; we will drive the y-scale via OnCalculateMinMax
        }
        else if (State == State.Configure)
        {
            MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
        }
        else if (State == State.DataLoaded)
        {
            bidHist = new DoubleBuffer();
            askHist = new DoubleBuffer();
        }
        else if (State == State.Historical)
        {
            isHistorical = true;
        }
        else if (State == State.Realtime)
        {
            isHistorical = false;
            bidVolCur = askVolCur = 0;
            lastBid = lastAsk = lastLastPrice = 0;
        }
    }

    protected override void OnBarUpdate()
    {
        if (CurrentBar < 1)
        {
            bidHist.EnsureSize(1);
            askHist.EnsureSize(1);
            return;
        }

        // When a new bar starts, commit the finished previous bar
        if (IsFirstTickOfBar)
        {
            int prev = CurrentBar - 1;
            double b = bidVolCur;
            double a = askVolCur;

            // Historicals loaded with the chart: optional single-color by direction
            if (isHistorical && UseHistoricalFallback && a == 0 && b == 0)
            {
                double volPrev = Volume[1];
                if (Close[1] >= Open[1]) { a = volPrev; b = 0; }
                else                     { b = volPrev; a = 0; }
            }

            bidHist.Set(prev, b);
            askHist.Set(prev, a);

            // Reset for the new live bar
            bidVolCur = askVolCur = 0;
        }

        // Ensure buffers can address current index for rendering
        bidHist.EnsureSize(CurrentBar + 1);
        askHist.EnsureSize(CurrentBar + 1);
    }

    // Real-time tick stream classification
    protected override void OnMarketData(MarketDataEventArgs e)
    {
        if (isHistorical) return; // live-only behavior

        switch (e.MarketDataType)
        {
            case MarketDataType.Bid:
                if (e.Price > 0) lastBid = e.Price;
                break;

            case MarketDataType.Ask:
                if (e.Price > 0) lastAsk = e.Price;
                break;

            case MarketDataType.Last:
                double inc = CountTrades
                             ? 1.0                                   // one per trade
                             : (e.Volume > 0 ? e.Volume : 0.0);      // contracts
                if (inc <= 0) return;

                double price = e.Price;

                // Prefer L1 classification if we have both sides
                if (lastBid > 0 && lastAsk > 0)
                {
                    double near = TickSize * 0.25;
                    if      (price >= lastAsk - near) askVolCur += inc;
                    else if (price <= lastBid + near) bidVolCur += inc;
                    else
                    {
                        // ambiguous: uptick/downtick
                        if (lastLastPrice == 0 || price >= lastLastPrice) askVolCur += inc;
                        else                                               bidVolCur += inc;
                    }
                }
                else
                {
                    // No L1 yet: uptick/downtick
                    if (lastLastPrice == 0 || price >= lastLastPrice) askVolCur += inc;
                    else                                               bidVolCur += inc;
                }

                lastLastPrice = price;
                break;
        }
    }

    // Provide real min/max so the right axis shows true values and scales correctly
    public override void OnCalculateMinMax()
	{
	    base.OnCalculateMinMax();
	
	double max = 0;
	int first = 0;
	int last  = CurrentBar;
	
	if (ChartBars != null)
	{
	    first = Math.Max(ChartBars.FromIndex, 0);
	    last  = Math.Min(ChartBars.ToIndex, CurrentBar);
	}
	
	for (int i = first; i <= last; i++)
	{
	    double b = (i == CurrentBar) ? bidVolCur : bidHist.Get(i);
	    double a = (i == CurrentBar) ? askVolCur : askHist.Get(i);
	    double t = a + b;
	    if (t > max) max = t;
	}
	
	MinValue = 0;
	MaxValue = Math.Max(1, max); // ensure nonzero range
	}

    // Draw using chart scale so the right axis matches drawn heights
    protected override void OnRender(ChartControl cc, ChartScale cs)
    {
        if (cc == null || ChartBars == null || RenderTarget == null) return;
        if (bidHist == null || askHist == null) return;

        int first = Math.Max(ChartBars.FromIndex, 0);
        int last  = Math.Min(ChartBars.ToIndex, CurrentBar);
        if (last < first) return;

        double curBid = bidVolCur;
        double curAsk = askVolCur;

        float baseY = (float)cs.GetYByValue(0); // y at value 0
        float w     = Math.Max(1f, (float)(cc.BarWidth * BarWidthFactor));

        DxBrush dxAsk = AskBrush.ToDxBrush(RenderTarget);
        DxBrush dxBid = BidBrush.ToDxBrush(RenderTarget);
        float minPx   = (float)Math.Max(0.0, MinSegmentPixels);

        for (int i = first; i <= last; i++)
        {
            double bid = (i == CurrentBar) ? curBid : bidHist.Get(i);
            double ask = (i == CurrentBar) ? curAsk : askHist.Get(i);
            double tot = bid + ask;
            if (tot <= 0) continue;

            float xC = (float)cc.GetXByBarIndex(ChartBars, i);
            float xL = xC - w * 0.5f;

            // Stronger side on bottom
            bool bottomIsAsk = ask >= bid;

            double bottomVol = bottomIsAsk ? ask : bid;
            double topVol    = bottomIsAsk ? bid : ask;

            // Convert values to Y using the panel's scale
            float yBottomTop = (float)cs.GetYByValue(bottomVol);
            float yTopTop    = (float)cs.GetYByValue(bottomVol + topVol);

            float hBottom = baseY - yBottomTop;
            float hTop    = yBottomTop - yTopTop;

            if (minPx > 0f)
            {
                if (hBottom > 0 && hBottom < minPx) hBottom = minPx;
                if (hTop    > 0 && hTop    < minPx) hTop    = minPx;
            }

            if (hBottom > 0)
                RenderTarget.FillRectangle(new RectangleF(xL, yBottomTop, w, hBottom),
                                           bottomIsAsk ? dxAsk : dxBid);
            if (hTop > 0)
                RenderTarget.FillRectangle(new RectangleF(xL, yTopTop, w, hTop),
                                           bottomIsAsk ? dxBid : dxAsk);
        }
    }

    // Simple dynamically-sized double buffer
    private sealed class DoubleBuffer
    {
        private double[] data = new double[0];
        public int Count { get; private set; }

        public void EnsureSize(int size)
        {
            if (size <= Count) return;
            if (size > data.Length)
            {
                int newLen = data.Length == 0 ? Math.Max(16, size) : Math.Max(size, data.Length * 2);
                Array.Resize(ref data, newLen);
            }
            Count = size;
        }

        public double Get(int index)
        {
            if (index < 0 || index >= Count) return 0.0;
            return data[index];
        }

        public void Set(int index, double value)
        {
            EnsureSize(index + 1);
            data[index] = value;
        }
    }
}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private StackedBuySellVolume[] cacheStackedBuySellVolume;
		public StackedBuySellVolume StackedBuySellVolume(MediaBrush askBrush, MediaBrush bidBrush, double barWidthFactor, double minSegmentPixels, bool useHistoricalFallback, bool countTrades)
		{
			return StackedBuySellVolume(Input, askBrush, bidBrush, barWidthFactor, minSegmentPixels, useHistoricalFallback, countTrades);
		}

		public StackedBuySellVolume StackedBuySellVolume(ISeries<double> input, MediaBrush askBrush, MediaBrush bidBrush, double barWidthFactor, double minSegmentPixels, bool useHistoricalFallback, bool countTrades)
		{
			if (cacheStackedBuySellVolume != null)
				for (int idx = 0; idx < cacheStackedBuySellVolume.Length; idx++)
					if (cacheStackedBuySellVolume[idx] != null && cacheStackedBuySellVolume[idx].AskBrush == askBrush && cacheStackedBuySellVolume[idx].BidBrush == bidBrush && cacheStackedBuySellVolume[idx].BarWidthFactor == barWidthFactor && cacheStackedBuySellVolume[idx].MinSegmentPixels == minSegmentPixels && cacheStackedBuySellVolume[idx].UseHistoricalFallback == useHistoricalFallback && cacheStackedBuySellVolume[idx].CountTrades == countTrades && cacheStackedBuySellVolume[idx].EqualsInput(input))
						return cacheStackedBuySellVolume[idx];
			return CacheIndicator<StackedBuySellVolume>(new StackedBuySellVolume(){ AskBrush = askBrush, BidBrush = bidBrush, BarWidthFactor = barWidthFactor, MinSegmentPixels = minSegmentPixels, UseHistoricalFallback = useHistoricalFallback, CountTrades = countTrades }, input, ref cacheStackedBuySellVolume);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.StackedBuySellVolume StackedBuySellVolume(MediaBrush askBrush, MediaBrush bidBrush, double barWidthFactor, double minSegmentPixels, bool useHistoricalFallback, bool countTrades)
		{
			return indicator.StackedBuySellVolume(Input, askBrush, bidBrush, barWidthFactor, minSegmentPixels, useHistoricalFallback, countTrades);
		}

		public Indicators.StackedBuySellVolume StackedBuySellVolume(ISeries<double> input , MediaBrush askBrush, MediaBrush bidBrush, double barWidthFactor, double minSegmentPixels, bool useHistoricalFallback, bool countTrades)
		{
			return indicator.StackedBuySellVolume(input, askBrush, bidBrush, barWidthFactor, minSegmentPixels, useHistoricalFallback, countTrades);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.StackedBuySellVolume StackedBuySellVolume(MediaBrush askBrush, MediaBrush bidBrush, double barWidthFactor, double minSegmentPixels, bool useHistoricalFallback, bool countTrades)
		{
			return indicator.StackedBuySellVolume(Input, askBrush, bidBrush, barWidthFactor, minSegmentPixels, useHistoricalFallback, countTrades);
		}

		public Indicators.StackedBuySellVolume StackedBuySellVolume(ISeries<double> input , MediaBrush askBrush, MediaBrush bidBrush, double barWidthFactor, double minSegmentPixels, bool useHistoricalFallback, bool countTrades)
		{
			return indicator.StackedBuySellVolume(input, askBrush, bidBrush, barWidthFactor, minSegmentPixels, useHistoricalFallback, countTrades);
		}
	}
}

#endregion
