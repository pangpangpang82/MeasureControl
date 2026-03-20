using System;
using MeasureControl.Drivers;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B
{
    /// <summary>
    /// 数据计数面板ViewModel
    /// </summary>
    public class Mil1394NodeDataCountPanelViewModel : BindableBase
    {
        private readonly HZ1394DriverInterface _driverInterface;

        public Mil1394NodeDataCountPanelViewModel(HZ1394DriverInterface driverInterface)
        {
            _driverInterface = driverInterface ?? throw new ArgumentNullException(nameof(driverInterface));
        }

        /// <summary>
        /// 获取数据计数
        /// </summary>
        public uint[] GetDataCounts(IntPtr nodeHandle)
        {
            // 移除对BM_CC_MSG_Cnt_Get的检查，计数获取应该始终可用
            // BM_CC_MSG_Cnt_Get标志只用于控制BM数据监控功能，不应该影响计数获取
            // if (!_driverInterface.BM_CC_MSG_Cnt_Get)
            // {
            //     return new uint[19]; // 返回全0数组
            // }

            if (nodeHandle == IntPtr.Zero)
            {
                return new uint[19];
            }

            try
            {
                uint[] data = new uint[19];
                for (uint i = 0; i < 19; i++)
                {
                    _driverInterface.HZ1394_CC_MSG_Cnt_Get(nodeHandle, i + 1, out data[i]);
                }
                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取数据计数失败: {ex.Message}");
                return new uint[19];
            }
        }
    }
}
