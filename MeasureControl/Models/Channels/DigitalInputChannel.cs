using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// 数字输入通道
    /// </summary>
    public class DigitalInputChannel : ChannelBase
    {
        public DigitalInputChannel()
        {
            ChannelType = "DI";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration();
        }
    }
}

