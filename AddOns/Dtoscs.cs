// Documents/NinjaTrader 8/bin/Custom/AddOns/StrategyHelpers/Dtos.cs
using System;
using System.Collections.Generic;

namespace CBASTerminal.StrategyHelpers
{
    public class TelemetryDto
    {
        public string InstanceId { get; set; }
        public string StrategyName { get; set; }
        public string Instrument { get; set; }
        public string Account { get; set; }
        public DateTime UtcTime { get; set; }
        public int BarIndex { get; set; }
        public bool IsBarClose { get; set; }
        public bool IsRealtime { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public double Last { get; set; }
        public double Spread { get; set; }
        public int PositionQty { get; set; }
        public double AvgPrice { get; set; }
        public double UnrealizedPnL { get; set; }
        public double RealizedPnL { get; set; }
        public bool Bull { get; set; }
        public bool Bear { get; set; }
        public bool ExitLong { get; set; }
        public bool ExitShort { get; set; }
        public double SuperTrend { get; set; }
        public int? Score { get; set; }
        public double? Vpm { get; set; }
        public double? Adx { get; set; }
        public bool? EmaStackBull { get; set; }
        public bool? EmaStackBear { get; set; }
        public bool? RangeBreak { get; set; }
        public double? Atr { get; set; }
        public double? AtrRatio { get; set; }
        public double? EmaSpread { get; set; }
        public double? PriceToBand { get; set; }
        public double? Momentum { get; set; }
        public string IntendedAction { get; set; }
        public List<string> ReasonCodes { get; set; }
        public double? Size { get; set; }
        public double? SlAtrMult { get; set; }
        public double? TpAtrMult { get; set; }
        public string LastError { get; set; }
        public string MisfireType { get; set; }
        public string CorrelationId { get; set; }

        public TelemetryDto()
        {
            ReasonCodes = new List<string>();
        }
    }

    public class CommandDto
    {
        public string CommandId { get; set; }
        public string Type { get; set; } // "Remark|UpdateParam|Pause|Resume|Flatten|SetRisk"
        public Dictionary<string, string> Meta { get; set; }
        public RemarkPayload Remark { get; set; }
        public UpdateParamPayload UpdateParam { get; set; }
        public SetRiskPayload SetRisk { get; set; }

        public CommandDto()
        {
            Meta = new Dictionary<string, string>();
        }
    }

    public class RemarkPayload
    {
        public string Message { get; set; }
    }

    public class UpdateParamPayload
    {
        // e.g., { "ScoreThresholdRt": "7", "UseScoringFilterRt": "true" }
        public Dictionary<string, string> Patch { get; set; }

        public UpdateParamPayload()
        {
            Patch = new Dictionary<string, string>();
        }
    }

    public class SetRiskPayload
    {
        public double? Size { get; set; }
        public double? SlAtrMult { get; set; }
        public double? TpAtrMult { get; set; }
    }

    public class AckDto
    {
        public string InstanceId { get; set; }
        public List<string> CommandIds { get; set; }

        public AckDto()
        {
            CommandIds = new List<string>();
        }
    }
}
