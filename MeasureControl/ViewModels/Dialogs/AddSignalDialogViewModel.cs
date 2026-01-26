using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Models;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class AddSignalDialogViewModel : BindableBase
    {
        #region Private Fields

        private string _chassisName;
        private string _testTaskName;
        private string _selectedSignalType;
        private string _signalName;
        private string _selectedActualChannel;
        private string _selectedRawValueUnit;
        private string _selectedRealTimeValueUnit;
        private string _remarks;
        private ObservableCollection<string> _availableSignalTypes;
        private ObservableCollection<string> _availableChannels;
        private ObservableCollection<string> _availableRawValueUnits;
        private ObservableCollection<string> _availableRealTimeValueUnits;
        private SignalConfigItem _result;

        #endregion

        #region Properties

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public string SelectedSignalType
        {
            get => _selectedSignalType;
            set
            {
                if (SetProperty(ref _selectedSignalType, value))
                {
                    OnSignalTypeChanged();
                }
            }
        }

        public string SignalName
        {
            get => _signalName;
            set
            {
                if (SetProperty(ref _signalName, value))
                {
                    ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string SelectedActualChannel
        {
            get => _selectedActualChannel;
            set
            {
                if (SetProperty(ref _selectedActualChannel, value))
                {
                    RaisePropertyChanged(nameof(IsRawValueUnitVisible));
                    RaisePropertyChanged(nameof(IsRealTimeValueUnitVisible));
                    ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string SelectedRawValueUnit
        {
            get => _selectedRawValueUnit;
            set
            {
                if (SetProperty(ref _selectedRawValueUnit, value))
                {
                    RaisePropertyChanged(nameof(IsRealTimeValueUnitVisible));
                }
            }
        }

        public string SelectedRealTimeValueUnit
        {
            get => _selectedRealTimeValueUnit;
            set => SetProperty(ref _selectedRealTimeValueUnit, value);
        }

        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        public ObservableCollection<string> AvailableSignalTypes
        {
            get => _availableSignalTypes;
            set => SetProperty(ref _availableSignalTypes, value);
        }

        public ObservableCollection<string> AvailableChannels
        {
            get => _availableChannels;
            set => SetProperty(ref _availableChannels, value);
        }

        public ObservableCollection<string> AvailableRawValueUnits
        {
            get => _availableRawValueUnits;
            set => SetProperty(ref _availableRawValueUnits, value);
        }

        public ObservableCollection<string> AvailableRealTimeValueUnits
        {
            get => _availableRealTimeValueUnits;
            set => SetProperty(ref _availableRealTimeValueUnits, value);
        }

        public SignalConfigItem Result
        {
            get => _result;
            private set => SetProperty(ref _result, value);
        }

        #endregion

        #region Visibility Properties

        // 所有字段都直接可见，不再依赖信号类型选择
        public bool IsSignalNameVisible => true;
        public bool IsActualChannelVisible => true;
        public bool IsRawValueUnitVisible => !string.IsNullOrEmpty(SelectedActualChannel);
        public bool IsRealTimeValueUnitVisible => !string.IsNullOrEmpty(SelectedRawValueUnit);

        /// <summary>数字量时单位不可选择</summary>
        public bool IsUnitSelectabel => SelectedSignalType != "数字量";
        public bool IsRemarksVisible => true;

        #endregion

        #region Commands

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        #region Events

        public event Action RequestClose;

        #endregion

        #region Constructor

        public AddSignalDialogViewModel(string testTaskName = null, string chassisName = null)
        {
            TestTaskName = testTaskName;
            ChassisName = chassisName;

            AvailableSignalTypes = new ObservableCollection<string>
            {
                "模拟量",
                "数字量",
                "通讯变量",
                "其他变量"
            };

            AvailableChannels = new ObservableCollection<string>();
            AvailableRawValueUnits = new ObservableCollection<string>();
            AvailableRealTimeValueUnits = new ObservableCollection<string>();

            OkCommand = new DelegateCommand(OnOk, CanOk);
            CancelCommand = new DelegateCommand(OnCancel);

            LoadAvailableChannels();
        }

        /// <summary>
        /// 编辑模式构造函数
        /// </summary>
        /// <param name="signal">要编辑的信号配置项</param>
        /// <param name="testTaskName">测试任务名称</param>
        /// <param name="chassisName">机箱名称</param>
        public AddSignalDialogViewModel(SignalConfigItem signal, string testTaskName = null, string chassisName = null)
        {
            TestTaskName = testTaskName;
            ChassisName = chassisName;

            AvailableSignalTypes = new ObservableCollection<string>
            {
                "模拟量",
                "数字量",
                "通讯变量",
                "其他变量"
            };

            AvailableChannels = new ObservableCollection<string>();
            AvailableRawValueUnits = new ObservableCollection<string>();
            AvailableRealTimeValueUnits = new ObservableCollection<string>();

            OkCommand = new DelegateCommand(OnOk, CanOk);
            CancelCommand = new DelegateCommand(OnCancel);

            // 预填充编辑数据
            if (signal != null)
            {
                // 设置所有字段的值
                SelectedSignalType = signal.SignalType;
                SignalName = signal.SignalName;
                SelectedActualChannel = signal.ActualChannel;
                SelectedRawValueUnit = signal.RawValueUnit;
                SelectedRealTimeValueUnit = signal.RealTimeValueUnit;
                Remarks = signal.Remarks;

                // 加载相关的选项列表
                LoadAvailableChannels();
                LoadAvailableUnits();
            }
            else
            {
                // 如果不是编辑模式，加载可用通道
                LoadAvailableChannels();
            }
        }

        #endregion

        #region Private Methods

        private void OnSignalTypeChanged()
        {
            // 不清除已选择的值，只重新加载相关选项
            LoadAvailableChannels();
            LoadAvailableUnits();

            // 不再需要更新可见性，因为所有字段都直接可见
            ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 加载可用通道列表（只显示当前测试任务的通道配置表数据）
        /// </summary>
        private void LoadAvailableChannels()
        {
            AvailableChannels.Clear();

            if (string.IsNullOrEmpty(SelectedSignalType))
                return;

            // 从ChannelConfigTabelViewModel获取所有通道配置表数据
            var allChannelTabelItems = ChannelConfigTabelViewModel.GetAllChannelTabelItems();

            var channels = new HashSet<string>();

            foreach (var kvp in allChannelTabelItems)
            {
                // key格式为"机箱名/测试任务名/配置表名"或"测试任务名/配置表名"（向后兼容）
                string expectedPrefix = null;
                if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
                {
                    expectedPrefix = $"{ChassisName}/{TestTaskName}/";
                }
                else if (!string.IsNullOrEmpty(TestTaskName))
                {
                    expectedPrefix = $"{TestTaskName}/";
                }

                // 如果指定了机箱名称和测试任务名称，只加载当前机箱和测试任务的通道
                if (!string.IsNullOrEmpty(expectedPrefix))
                {
                    if (!kvp.Key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(TestTaskName))
                {
                    // 向后兼容：只检查测试任务名称
                    if (!kvp.Key.StartsWith(TestTaskName + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // 从key中提取配置表名称
                string configTabelName = kvp.Key;
                if (!string.IsNullOrEmpty(expectedPrefix) && kvp.Key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    configTabelName = kvp.Key.Substring(expectedPrefix.Length);
                }
                else if (!string.IsNullOrEmpty(TestTaskName) && kvp.Key.StartsWith(TestTaskName + "/", StringComparison.OrdinalIgnoreCase))
                {
                    configTabelName = kvp.Key.Substring(TestTaskName.Length + 1);
                }
                else if (kvp.Key.Contains("/"))
                {
                    // 如果没有指定测试任务名称，但key包含"/"，则提取配置表名称（最后一个"/"之后的部分）
                    int lastSlashIndex = kvp.Key.LastIndexOf('/');
                    if (lastSlashIndex >= 0 && lastSlashIndex < kvp.Key.Length - 1)
                    {
                        configTabelName = kvp.Key.Substring(lastSlashIndex + 1);
                    }
                }

                foreach (var channel in kvp.Value)
                {
                    // 根据信号类型过滤通道
                    bool shouldAddChannel = false;

                    if (SelectedSignalType == "模拟量")
                    {
                        // 模拟量显示AI、AO和RO通道
                        shouldAddChannel = channel.InputOutputType == "AI" ||
                                         channel.InputOutputType == "AO" ||
                                         channel.InputOutputType == "RO";
                    }
                    else if (SelectedSignalType == "数字量")
                    {
                        // 数字量只显示DI和DO通道
                        shouldAddChannel = channel.InputOutputType == "DI" || channel.InputOutputType == "DO";
                    }
                    else if (SelectedSignalType == "通讯变量")
                    {
                        // 通讯变量显示CAN、ARINC429、1553B等通讯通道
                        shouldAddChannel = channel.InputOutputType == "CAN" ||
                                         channel.InputOutputType == "ARINC429" ||
                                         channel.InputOutputType == "1553B" ||
                                         channel.InputOutputType.Contains("通信") ||
                                         channel.InputOutputType.Contains("通讯");
                    }
                    else if (SelectedSignalType == "其他变量")
                    {
                        // 其他变量显示除上述类型外的所有通道
                        shouldAddChannel = channel.InputOutputType != "AI" &&
                                         channel.InputOutputType != "AO" &&
                                         channel.InputOutputType != "DI" &&
                                         channel.InputOutputType != "DO" &&
                                         channel.InputOutputType != "RO" &&
                                         channel.InputOutputType != "CAN" &&
                                         channel.InputOutputType != "ARINC429" &&
                                         channel.InputOutputType != "1553B";
                    }

                    if (shouldAddChannel)
                    {
                        // 格式：配置表名:通道名称（不显示测试任务名称）
                        string channelDisplay = $"{configTabelName}:{channel.ChannelName}";
                        channels.Add(channelDisplay);
                    }
                }
            }

            foreach (var channel in channels.OrderBy(c => c))
            {
                AvailableChannels.Add(channel);
            }
        }

        /// <summary>
        /// 加载可用单位列表
        /// </summary>
        private void LoadAvailableUnits()
        {
            AvailableRawValueUnits.Clear();
            AvailableRealTimeValueUnits.Clear();

            if (string.IsNullOrEmpty(SelectedSignalType))
                return;

            if (SelectedSignalType == "模拟量")
            {
                // 模拟量常用单位
                var analogUnits = new List<string>
                {
                    "V", "mV", "kV",
                    "A", "mA", "kA",
                    "℃", "℉", "K",
                    "Pa", "kPa", "MPa", "bar", "psi",
                    "rpm", "rps",
                    "Hz", "kHz", "MHz",
                    "%",
                    "m", "cm", "mm",
                    "kg", "g",
                    "m/s", "m/s²",
                    "N", "kN"
                };

                foreach (var unit in analogUnits)
                {
                    AvailableRawValueUnits.Add(unit);
                    AvailableRealTimeValueUnits.Add(unit);
                }
            }
            else if (SelectedSignalType == "数字量")
            {
                // 数字量单位固定为0/1
                AvailableRawValueUnits.Add("0/1");
                AvailableRealTimeValueUnits.Add("0/1");

                // 自动选中固定单位
                SelectedRawValueUnit = "0/1";
                SelectedRealTimeValueUnit = "0/1";
            }
            else if (SelectedSignalType == "通讯变量")
            {
                // 通讯变量常用单位
                var commUnits = new List<string>
                {
                    "bit", "byte", "word",
                    "Hz", "kHz", "MHz",
                    "bps", "kbps", "Mbps",
                    "V", "mV",
                    "°", "rad",
                    "rpm",
                    "count"
                };

                foreach (var unit in commUnits)
                {
                    AvailableRawValueUnits.Add(unit);
                    AvailableRealTimeValueUnits.Add(unit);
                }
            }
            else if (SelectedSignalType == "其他变量")
            {
                // 其他变量的常用单位
                var otherUnits = new List<string>
                {
                    "V", "mV", "kV",
                    "A", "mA", "kA",
                    "℃", "℉", "K",
                    "Pa", "kPa", "MPa",
                    "rpm", "rps",
                    "Hz", "kHz", "MHz",
                    "%",
                    "m", "cm", "mm",
                    "kg", "g",
                    "count", "step",
                    "°", "rad"
                };

                foreach (var unit in otherUnits)
                {
                    AvailableRawValueUnits.Add(unit);
                    AvailableRealTimeValueUnits.Add(unit);
                }
            }

            RaisePropertyChanged(nameof(IsUnitSelectabel));
        }

        private bool CanOk()
        {
            // 验证必填字段
            if (string.IsNullOrEmpty(SelectedSignalType))
                return false;

            if (string.IsNullOrWhiteSpace(SignalName))
                return false;

            if (string.IsNullOrEmpty(SelectedActualChannel))
                return false;

            // 验证同一变量表中变量名称唯一
            if (!IsSignalNameUniqueInCurrentTabel())
                return false;

            return true;
        }

        private void OnOk()
        {
            if (!ValidateInput())
                return;

            // 创建结果
            Result = new SignalConfigItem
            {
                SignalType = SelectedSignalType,
                SignalName = SignalName,
                ActualChannel = SelectedActualChannel,
                RawValueUnit = SelectedRawValueUnit ?? string.Empty,
                RealTimeValueUnit = SelectedRealTimeValueUnit ?? string.Empty,
                RawValue = 0,
                RealTimeValue = 0,
                Remarks = Remarks ?? string.Empty,
                IsEmpty = false
            };

            RequestClose?.Invoke();
        }

        private void OnCancel()
        {
            Result = null;
            RequestClose?.Invoke();
        }

        private bool ValidateInput()
        {
            // 验证信号名称不能为空
            if (string.IsNullOrWhiteSpace(SignalName))
            {
                return false;
            }

            // 验证同一变量表中变量名称唯一
            if (!IsSignalNameUniqueInCurrentTabel())
            {
                ReMessageBox.Show("该变量名称在当前变量表中已存在，请更换名称", "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查当前变量表中变量名称是否唯一
        /// </summary>
        private bool IsSignalNameUniqueInCurrentTabel()
        {
            // 未指定测试任务名或变量表名时，无法做表级校验，默认通过
            if (string.IsNullOrEmpty(TestTaskName))
                return true;

            // 通过静态字典获取当前测试任务下的所有变量表数据
            var allTabels = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allTabels == null || allTabels.Count == 0)
                return true;

            foreach (var kvp in allTabels)
            {
                // 只检查当前测试任务下的变量表（key 格式：测试任务名/变量表名）
                if (!kvp.Key.StartsWith(TestTaskName + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 在所有变量表中不允许重名
                if (kvp.Value != null && kvp.Value.Any(s => !s.IsEmpty && string.Equals(s.SignalName, SignalName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}


