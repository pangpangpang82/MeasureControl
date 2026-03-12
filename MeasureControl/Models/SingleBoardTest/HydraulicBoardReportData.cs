using System;
using System.Collections.Generic;

namespace MeasureControl.Models.SingleBoardTest
{
    public class HydraulicBoardReportData
    {
        public string BoardName { get; set; }

        public string BoardType { get; set; }

        public DateTime TestTime { get; set; }

        public string OverallResult { get; set; }

        public List<HydraulicTestItemReportData> TestItems { get; set; } = new List<HydraulicTestItemReportData>();
    }

    public class HydraulicTestItemReportData
    {
        public string TestItemName { get; set; }

        public string Result { get; set; }

        public string Criteria { get; set; }

        public string Notes { get; set; }

        public List<HydraulicMeasurementReportData> Measurements { get; set; } = new List<HydraulicMeasurementReportData>();
    }

    public class HydraulicMeasurementReportData
    {
        public string MeasurementName { get; set; }

        public string DisplayValue { get; set; }

        public double? NumericValue { get; set; }

        public string Unit { get; set; }

        public string Result { get; set; }

        public string Comment { get; set; }
    }
}
