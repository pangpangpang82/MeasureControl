namespace MeasureControl.Models
{
    public class DropPxiChassisArgs
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public string ChassisModel { get; set; }

        public DropPxiChassisArgs(int row, int column, string chassisModel = null)
        {
            Row = row;
            Column = column;
            ChassisModel = chassisModel ?? "PXIe-2722G2"; // 默认为9槽机箱型号
        }
    }
}
