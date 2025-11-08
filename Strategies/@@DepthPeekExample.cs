using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Data;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class DepthPeekExample : Strategy
    {
        // Track depth by level index
        private readonly SortedDictionary<int, (double price, long vol)> bidLevels = new SortedDictionary<int, (double price, long vol)>();
        private readonly SortedDictionary<int, (double price, long vol)> askLevels = new SortedDictionary<int, (double price, long vol)>();

    // Track best bid/ask
    private double bestBid = double.NaN;
    private double bestAsk = double.NaN;

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name = "DepthPeekExample";
            Calculate = Calculate.OnEachTick; // So we can react immediately
            IsUnmanaged = false;
            IsInstantiatedOnEachOptimizationIteration = false;
        }
        else if (State == State.Configure)
        {
            // Primary series on MNQ (your MNQU5 front contract). Select the correct instrument when adding the strategy.
            // No extra series is required for depth; OnMarketDepth is event-driven.
        }
    }

    protected override void OnMarketDepth(MarketDepthEventArgs e)
    {
        // Update our book snapshot
        var dict = e.MarketDataType == MarketDataType.Bid ? bidLevels : askLevels;

        // e.Position is the level index (0 = best)
        if (e.Operation == Operation.Remove)
        {
            dict.Remove(e.Position);
        }
        else // Insert or Update
        {
            dict[e.Position] = (e.Price, e.Volume);
        }

        // Update cached inside market if level 0 changed
        if (e.MarketDataType == MarketDataType.Bid && e.Position == 0)
            bestBid = e.Volume > 0 ? e.Price : double.NaN;
        if (e.MarketDataType == MarketDataType.Ask && e.Position == 0)
            bestAsk = e.Volume > 0 ? e.Price : double.NaN;
    }

    protected override void OnMarketData(MarketDataEventArgs e)
    {
        // Optional: capture last trade, etc.
    }

    protected override void OnBarUpdate()
    {
        if (CurrentBar < 1)
            return;

        // Current instrument price context
        double last = Close[0];
        double bid = GetCurrentBid();
        double ask = GetCurrentAsk();
        double oneTick = Instrument.MasterInstrument.TickSize;

        // Example queries:
        long bidVolOneLevelBelowBest = GetBidVolumeAtLevel(1); // next bid level
        long askVolOneLevelAboveBest = GetAskVolumeAtLevel(1); // next ask level

        // Or, by exact price “one tick below best bid”
        double priceOneTickBelowBestBid = (!double.IsNaN(bid) ? bid : bestBid) - oneTick;
        long bidVolAtPrice = GetBidVolumeAtPrice(priceOneTickBelowBestBid);

        // For asks, “one tick below best ask” is inside the spread; usually zero unless spread > 1 tick.
        double priceOneTickBelowBestAsk = (!double.IsNaN(ask) ? ask : bestAsk) - oneTick;
        long askVolAtPriceBelowBestAsk = GetAskVolumeAtPrice(priceOneTickBelowBestAsk);

        // Do whatever you need with these values (log, condition, submit orders, etc.)
        Print(string.Format("Last={0} Bid={1} Ask={2} | BidLvl1Vol={3} AskLvl1Vol={4} | Bid@Bid-1tick={5} Ask@Ask-1tick={6}",
                            last, bid, ask, bidVolOneLevelBelowBest, askVolOneLevelAboveBest, bidVolAtPrice, askVolAtPriceBelowBestAsk));
    }

    private long GetBidVolumeAtLevel(int level)
    {
        if (bidLevels.TryGetValue(level, out var lv))
            return lv.vol;
        return 0;
    }

    private long GetAskVolumeAtLevel(int level)
    {
        if (askLevels.TryGetValue(level, out var lv))
            return lv.vol;
        return 0;
    }

    private long GetBidVolumeAtPrice(double price)
    {
        foreach (var kvp in bidLevels)
            if (kvp.Value.price == price)
                return kvp.Value.vol;
        return 0;
    }

    private long GetAskVolumeAtPrice(double price)
    {
        foreach (var kvp in askLevels)
            if (kvp.Value.price == price)
                return kvp.Value.vol;
        return 0;
    }
}
}