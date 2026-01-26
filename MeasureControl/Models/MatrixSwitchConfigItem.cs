using System;
using Newtonsoft.Json;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 矩阵开关配置项（用于变量表2）
    /// </summary>
    public class MatrixSwitchConfigItem : BindableBase
    {
        private string _id;
        private int _index;
        private string _matrixSwitchName;
        private string _instrumentType;
        private string _topology;
        private string _matrixInput;
        private string _matrixOutput;
        private string _remarks;
        private bool _isEmpty;

        /// <summary>
        /// 唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 序号
        /// </summary>
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        /// <summary>
        /// 矩阵开关名称（来自已添加的SwitchDevice）
        /// 格式：矩阵开关1 欧开PXI-3022
        /// </summary>
        public string MatrixSwitchName
        {
            get => _matrixSwitchName;
            set => SetProperty(ref _matrixSwitchName, value);
        }

        /// <summary>
        /// 仪器类型
        /// </summary>
        public string InstrumentType
        {
            get => _instrumentType;
            set => SetProperty(ref _instrumentType, value);
        }

        /// <summary>
        /// 拓扑
        /// </summary>
        public string Topology
        {
            get => _topology;
            set 
            {
                if (SetProperty(ref _topology, value))
                {
                    // 当拓扑变化时，重置输入输出选项
                    MatrixInput = null;
                    MatrixOutput = null;
                }
            }
        }

        /// <summary>
        /// 矩阵开关的输入
        /// </summary>
        public string MatrixInput
        {
            get => _matrixInput;
            set => SetProperty(ref _matrixInput, value);
        }

        /// <summary>
        /// 矩阵开关的输出
        /// </summary>
        public string MatrixOutput
        {
            get => _matrixOutput;
            set => SetProperty(ref _matrixOutput, value);
        }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        /// <summary>
        /// 是否为空项
        /// </summary>
        public bool IsEmpty
        {
            get => _isEmpty;
            set => SetProperty(ref _isEmpty, value);
        }

        /// <summary>
        /// 构造函数 - 自动生成唯一Id
        /// </summary>
        public MatrixSwitchConfigItem()
        {
            _id = Guid.NewGuid().ToString("N");
        }

        public MatrixSwitchConfigItem Clone() => new MatrixSwitchConfigItem
        {
            Id = Id,
            Index = Index,
            MatrixSwitchName = MatrixSwitchName,
            InstrumentType = InstrumentType,
            Topology = Topology,
            MatrixInput = MatrixInput,
            MatrixOutput = MatrixOutput,
            Remarks = Remarks,
            IsEmpty = IsEmpty
        };
    }
}