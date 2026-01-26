using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 开关设备类
    /// </summary>
    public class SwitchDevice : PxiDeviceBase
    {
        private string _matrixConfiguration;
        private string _deviceTypeName;
        public override string DeviceTypeName => _deviceTypeName ?? "矩阵开关";
        private SwitchChannelNode _switchChannelNode;

        /// <summary>
        /// 开关通道子节点
        /// </summary>
        public SwitchChannelNode SwitchChannelNode
        {
            get => _switchChannelNode;
            set => SetProperty(ref _switchChannelNode, value);
        }
        
        /// <summary>
        /// 矩阵配置
        /// </summary>
        public string MatrixConfiguration
        {
            get => _matrixConfiguration;
            set => SetProperty(ref _matrixConfiguration, value);
        }

        public SwitchDevice() : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            InitializeChildren();
        }

        public SwitchDevice(string name, string slotPosition) : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ParseDeviceName(name); 
            SlotPosition = slotPosition;

            if (Model.Contains("3022"))
            {
                Name = "矩阵开关";
                Model = "PXI-3022";
                MatrixConfiguration = "4×64 / 8×32";
            }
            else if (Model.Contains("2601"))
            {
                Name = "矩阵开关";
                Model = "PXI-2601";
                MatrixConfiguration = "4×32 / 8×16";
            }
            
            InitializeChildren();
        }

        /// <summary>
        /// 设置设备类型名称
        /// </summary>
        public void SetDeviceTypeName(string typeName)
        {
            _deviceTypeName = typeName;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
            
            // 创建矩阵配置子节点
            SwitchChannelNode = new SwitchChannelNode
            {
                Name = "矩阵通道组",
                ParentNode = "矩阵开关",
                MatrixConfig = MatrixConfiguration,
                Model =  MatrixConfiguration,
                SlotPosition = "Matrix",
                Status = DeviceConstants.Status.Normal
            };
            
            Children.Add(SwitchChannelNode);
        }

        /// <summary>
        /// 获取设备信息项列表
        /// </summary>
        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            foreach (var child in Children)
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(child, true);
                if (subNodeInfo != null)
                {
                    items.Add(subNodeInfo);
                }
            }

            return items;
        }

        public override string GetConnectionString()
        {
            return $"Switch::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            bool baseValid = base.ValidateConfiguration();

            return baseValid;
        }
    }

    /// <summary>
    /// 矩阵开关通道配置子节点
    /// </summary>
    public class SwitchChannelNode : SubNodeBase
    {
        private string _matrixConfig;
        private int _crosspoints;

        public string MatrixConfig
        {
            get => _matrixConfig;
            set => SetProperty(ref _matrixConfig, value);
        }

        public int Crosspoints
        {
            get => _crosspoints;
            set => SetProperty(ref _crosspoints, value);
        }

        public override string DeviceTypeName => "矩阵通道组";

        public SwitchChannelNode() : base("矩阵通道组", "矩阵开关")
        {
            MatrixConfig = "4×64";
            SlotPosition = "Matrix";
        }

        public override string GetConnectionString()
        {
            return $"MatrixSwitch::{MatrixConfig}::{Crosspoints}";
        }
    }
}
