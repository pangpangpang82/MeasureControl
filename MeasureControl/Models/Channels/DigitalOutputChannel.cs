using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// 数字输出通道
    /// </summary>
    public class DigitalOutputChannel : ChannelBase
    {
        public DigitalOutputChannel()
        {
            ChannelType = "DO";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration();
        }
    }
}

