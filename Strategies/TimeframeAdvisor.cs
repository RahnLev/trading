using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies
{
    // Lightweight heuristic advisor to suggest chart timeframe adjustments
    // Evaluates recent indicators and trade behavior to recommend 30s or 60s
    public class TimeframeAdvisor
    {
        public struct Context
        {
            public double ADX;
            public double ATR;
            public double FastGradient;
            public double SlowGradient;
            public double GradientStability;
            public int BarsSinceEntry;
            public int RecentWhipsawCount; // signal flips within <= 2 bars
            public int RecentTrades;       // trades over last N bars
        }

        public enum Recommendation
        {
            Keep,
            Prefer30s,
            Prefer60s
        }

        public Recommendation Evaluate(Context ctx)
        {
            // Heuristics:
            // - High ADX & moderate ATR with low whipsaw -> 30s ok (more responsive)
            // - Low/medium ADX, high whipsaws, high gradient instability -> prefer 60s (smoother)
            // - Very high ATR spikes combined with flips -> prefer 60s
            int score30 = 0;
            int score60 = 0;

            if (ctx.ADX >= 25) score30 += 2; else score60 += 1;
            if (ctx.ATR >= 6 && ctx.ATR <= 12) score30 += 1; // responsive but not chaotic
            if (ctx.RecentWhipsawCount >= 3) score60 += 3;   // frequent flips -> smooth out
            if (ctx.GradientStability > 1.2) score60 += 2;   // unstable gradients -> longer TF
            if (Math.Abs(ctx.FastGradient) < 0.08 && Math.Abs(ctx.SlowGradient) < 0.15) score60 += 1; // weak momentum -> longer TF
            if (ctx.RecentTrades >= 6) score60 += 1; // too chatty -> de-noise

            if (score60 - score30 >= 2) return Recommendation.Prefer60s;
            if (score30 - score60 >= 2) return Recommendation.Prefer30s;
            return Recommendation.Keep;
        }

        public string Explain(Context ctx, Recommendation rec)
        {
            switch (rec)
            {
                case Recommendation.Prefer60s:
                    return "Frequent whipsaws/unstable gradients detected; suggest 60s to reduce noise.";
                case Recommendation.Prefer30s:
                    return "Strong trend (ADX) with moderate ATR; 30s improves responsiveness.";
                default:
                    return "No strong signal to change timeframe; keep current.";
            }
        }
    }
}
