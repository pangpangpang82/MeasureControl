using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Models;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// 添加ICD映射对话框ViewModel
    /// </summary>
    public class AddIcdMappingDialogViewModel : BindableBase, IDialogAware
    {
        private readonly MeasureControl.Services.IDialogService _customDialogService;

        // 数据类型到位长的映射
        private static readonly Dictionary<string, int> DataTypeBitLengths = new Dictionary<string, int>
        {
            ["UInt8"] = 8,
            ["Int8"] = 8,
            ["Boolean"] = 8,
            ["UInt16"] = 16,
            ["Int16"] = 16,
            ["UInt32"] = 32,
            ["Int32"] = 32,
            ["Float32"] = 32,
            ["Float64"] = 64
        };

        // 可用的数据类型列表
        public static readonly List<string> AvailableDataTypes = new List<string>
        {
            "UInt8", "Int8", "Boolean", "UInt16", "Int16", "UInt32", "Int32", "Float32", "Float64"
        };

        public string Title => "添加ICD映射";

        public event Action<IDialogResult> RequestClose;

        private IcdMappingItem _mappingItem;
        public IcdMappingItem MappingItem
        {
            get => _mappingItem;
            set => SetProperty(ref _mappingItem, value);
        }

        private ObservableCollection<string> _availableIcdTabels;
        public ObservableCollection<string> AvailableIcdTabels
        {
            get => _availableIcdTabels;
            set => SetProperty(ref _availableIcdTabels, value);
        }

        private ObservableCollection<IcdFrameItem> _availableFrames;
        public ObservableCollection<IcdFrameItem> AvailableFrames
        {
            get => _availableFrames;
            set => SetProperty(ref _availableFrames, value);
        }

        private bool _isSaveEnabled;
        public bool IsSaveEnabled
        {
            get => _isSaveEnabled;
            set => SetProperty(ref _isSaveEnabled, value);
        }

        private string _validationMessage;
        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        // 基础信息（只读）
        private string _frameName;
        public string FrameName
        {
            get => _frameName;
            set => SetProperty(ref _frameName, value);
        }

        private string _messageIdDisplay;
        public string MessageIdDisplay
        {
            get => _messageIdDisplay;
            set => SetProperty(ref _messageIdDisplay, value);
        }

        private string _channelDisplay;
        public string ChannelDisplay
        {
            get => _channelDisplay;
            set => SetProperty(ref _channelDisplay, value);
        }

        private int _dlcDisplay;
        public int DlcDisplay
        {
            get => _dlcDisplay;
            set => SetProperty(ref _dlcDisplay, value);
        }

        private string _directionDisplay;
        public string DirectionDisplay
        {
            get => _directionDisplay;
            set => SetProperty(ref _directionDisplay, value);
        }

        private string _cycleDisplay;
        public string CycleDisplay
        {
            get => _cycleDisplay;
            set => SetProperty(ref _cycleDisplay, value);
        }

        // 命令
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public AddIcdMappingDialogViewModel(MeasureControl.Services.IDialogService dialogService)
        {
            _customDialogService = dialogService;

            MappingItem = new IcdMappingItem();
            AvailableIcdTabels = new ObservableCollection<string>();
            AvailableFrames = new ObservableCollection<IcdFrameItem>();

            SaveCommand = new DelegateCommand(Save, CanSave);
            CancelCommand = new DelegateCommand(Cancel);

            // 监听属性变化进行验证
            MappingItem.PropertyChanged += (s, e) => ValidateAndUpdateSaveState();
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 从参数中获取可用的ICD表和帧数据
            if (parameters.TryGetValue("AvailableIcdTabels", out ObservableCollection<string> icdTabels))
            {
                AvailableIcdTabels = icdTabels;
            }

            if (parameters.TryGetValue("AvailableFrames", out ObservableCollection<IcdFrameItem> frames))
            {
                // 过滤掉远程帧，只显示可以映射的帧
                AvailableFrames = new ObservableCollection<IcdFrameItem>(
                    frames.Where(f => f != null && !IsRemoteFrame(f)));
            }

            ValidateAndUpdateSaveState();
        }

        private void ValidateAndUpdateSaveState()
        {
            var errors = new List<string>();

            // 必填字段验证
            if (string.IsNullOrWhiteSpace(MappingItem.SignalId))
                errors.Add("信号标识不能为空");
            if (string.IsNullOrWhiteSpace(MappingItem.Description))
                errors.Add("信号说明不能为空");
            if (string.IsNullOrWhiteSpace(MappingItem.IcdTabelId))
                errors.Add("必须选择ICD配置表");
            if (string.IsNullOrWhiteSpace(MappingItem.FrameId))
                errors.Add("必须选择绑定帧");
            if (string.IsNullOrWhiteSpace(MappingItem.DataType))
                errors.Add("必须选择数据类型");

            // 标定公式验证
            if (!string.IsNullOrWhiteSpace(MappingItem.CalibrationFormula) &&
                MappingItem.CalibrationFormula != "/" &&
                !IsValidCalibrationFormula(MappingItem.CalibrationFormula))
            {
                errors.Add("标定公式格式不正确");
            }

            // 拼接验证
            var concatValidationResult = ValidateConcatenationDetailed();
            if (!concatValidationResult.IsValid)
            {
                errors.Add(concatValidationResult.ErrorMessage);
            }

            ValidationMessage = errors.Any() ? string.Join("；", errors) : string.Empty;
            IsSaveEnabled = !errors.Any();

            // 更新保存命令状态
            (SaveCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }

        private bool IsValidCalibrationFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula) || formula == "/")
                return true;

            // 简单的数学表达式验证
            // 允许：数字、小数点、运算符(+ - * / ^)、括号、字母(变量名)、下划线
            var pattern = @"^[\d\s\w_.+\-*/^()]+$";
            return Regex.IsMatch(formula, pattern) && formula.Length <= 200;
        }

        private (bool IsValid, string ErrorMessage) ValidateConcatenationDetailed()
        {
            var concatCount = MappingItem.ConcatCount;
            var doubleWordBits = MappingItem.DoubleWordBits;
            var positionInWord = MappingItem.PositionInWord;

            // 如果三个字段都是0，则不参与拼接，验证通过
            if (concatCount == 0 && doubleWordBits == 0 && positionInWord == 0)
                return (true, string.Empty);

            // 如果参与拼接，concatCount必须>=1
            if (concatCount < 1)
                return (false, "参与拼接时，拼接个数必须大于0");

            // 检查位长限制：拼接总位长不能超过帧的DLC字节数*8
            var dataTypeBitLength = DataTypeBitLengths.TryGetValue(MappingItem.DataType, out var bitLength) ? bitLength : 0;
            var totalBitsRequired = concatCount * dataTypeBitLength;
            var maxAvailableBits = DlcDisplay * 8;

            if (totalBitsRequired > maxAvailableBits)
                return (false, $"拼接总位长({totalBitsRequired}位)超过帧的最大可用位长({maxAvailableBits}位)");

            // 检查位置参数的有效性
            if (doubleWordBits < 0 || positionInWord < 0)
                return (false, "双字位数和字内位置不能为负数");

            // 检查拼接位置是否会导致越界
            var startBitOffset = (doubleWordBits * 8) + positionInWord;
            if (startBitOffset + dataTypeBitLength > maxAvailableBits)
                return (false, $"拼接起始位置({startBitOffset}位)加上数据位长({dataTypeBitLength}位)超过帧的最大可用位长({maxAvailableBits}位)");

            return (true, string.Empty);
        }

        private bool ValidateConcatenation()
        {
            return ValidateConcatenationDetailed().IsValid;
        }

        private bool CanSave()
        {
            return IsSaveEnabled;
        }

        private void Save()
        {
            // 更新映射项的只读属性
            UpdateMappingFromFrame();

            var parameters = new DialogParameters();
            parameters.Add("MappingItem", MappingItem);

            RequestClose?.Invoke(new DialogResult(ButtonResult.OK, parameters));
        }

        private void Cancel()
        {
            RequestClose?.Invoke(new DialogResult(ButtonResult.Cancel));
        }

        /// <summary>
        /// 当选择帧时调用，更新显示的基础信息
        /// </summary>
        public void OnFrameSelected(IcdFrameItem selectedFrame)
        {
            if (selectedFrame == null)
            {
                ClearFrameInfo();
                MappingItem.FrameId = null;
                ValidateAndUpdateSaveState();
                return;
            }

            // 检查是否为远程帧
            if (IsRemoteFrame(selectedFrame))
            {
                _customDialogService.ShowWarningDialog("远程帧不能进行信号映射", "不支持的帧类型");
                MappingItem.FrameId = null; // 清空选择
                ClearFrameInfo();
                IsSaveEnabled = false; // 禁用保存
                ValidationMessage = "远程帧不能进行信号映射";
                return;
            }

            FrameName = selectedFrame.FrameName ?? selectedFrame.FrameId;
            MessageIdDisplay = FormatMessageId(selectedFrame.FrameIdFieldValue);
            ChannelDisplay = GetFrameChannel(selectedFrame);
            DlcDisplay = GetFrameDlc(selectedFrame);
            DirectionDisplay = GetFrameDirection(selectedFrame);
            CycleDisplay = GetFrameCycle(selectedFrame);

            // 更新映射项
            MappingItem.FrameId = selectedFrame.FrameId;
            MappingItem.MessageId = selectedFrame.FrameIdFieldValue;
            MappingItem.Channel = ChannelDisplay;
            MappingItem.Dlc = DlcDisplay;
            MappingItem.Direction = DirectionDisplay;
            MappingItem.Cycle = CycleDisplay;

            // 重新验证
            ValidateAndUpdateSaveState();
        }

        /// <summary>
        /// 当数据类型改变时调用，更新位长
        /// </summary>
        public void OnDataTypeChanged()
        {
            MappingItem.BitLength = DataTypeBitLengths.TryGetValue(MappingItem.DataType, out var bitLength) ? bitLength : 0;
            ValidateAndUpdateSaveState();
        }

        private void ClearFrameInfo()
        {
            FrameName = string.Empty;
            MessageIdDisplay = string.Empty;
            ChannelDisplay = string.Empty;
            DlcDisplay = 0;
            DirectionDisplay = string.Empty;
            CycleDisplay = string.Empty;
        }

        private void UpdateMappingFromFrame()
        {
            MappingItem.MessageId = MessageIdDisplay;
            MappingItem.Channel = ChannelDisplay;
            MappingItem.Dlc = DlcDisplay;
            MappingItem.Direction = DirectionDisplay;
            MappingItem.Cycle = CycleDisplay;
        }

        private bool IsRemoteFrame(IcdFrameItem frame)
        {
            // 检查帧的字段配置来判断是否为远程帧
            if (frame?.Fields == null)
                return false;

            // 查找帧类型字段
            var frameTypeField = frame.Fields.FirstOrDefault(f => f.Name == "帧类型" || f.DisplayName == "帧类型");
            if (frameTypeField?.ConfigItems == null)
                return false;

            // 检查是否有"远程帧"配置
            var frameTypeConfig = frameTypeField.ConfigItems.FirstOrDefault(c => c.Name == "帧类型");
            if (frameTypeConfig?.Value != null)
            {
                return frameTypeConfig.Value.ToString().Contains("远程") ||
                       frameTypeConfig.Value.ToString().Contains("Remote");
            }

            return false;
        }

        private string FormatMessageId(string frameId)
        {
            if (string.IsNullOrEmpty(frameId))
                return string.Empty;

            // 假设frameId已经是十六进制格式，如果不是则转换
            if (!frameId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(frameId, out var intValue))
                {
                    return $"0x{intValue:X}";
                }
                return frameId;
            }

            return frameId.ToUpper();
        }

        private string GetFrameChannel(IcdFrameItem frame)
        {
            // 从帧的配置项中获取通道信息
            // 这里需要根据实际的帧字段结构来实现
            return "CAN1"; // 临时返回值
        }

        private int GetFrameDlc(IcdFrameItem frame)
        {
            // 从帧的配置项中获取DLC信息
            // 这里需要根据实际的帧字段结构来实现
            return 8; // 临时返回值
        }

        private string GetFrameDirection(IcdFrameItem frame)
        {
            // 根据帧类型判断方向
            // 对于CAN帧，通常需要检查是否有发送/接收的配置
            // 这里简化处理：假设TX帧为STOF，RX帧为FTOS
            if (frame?.Fields == null)
                return "FTOS";

            // 检查是否有发送周期配置，如果有则认为是TX帧
            var cycleField = frame.Fields.FirstOrDefault(f =>
                f.Name.Contains("周期") || f.DisplayName.Contains("周期"));
            if (cycleField?.ConfigItems != null)
            {
                var cycleConfig = cycleField.ConfigItems.FirstOrDefault(c =>
                    c.Name.Contains("周期") || c.Value != null);
                if (cycleConfig != null && !string.IsNullOrEmpty(cycleConfig.Value?.ToString()) &&
                    cycleConfig.Value.ToString() != "/" && cycleConfig.Value.ToString() != "0")
                {
                    return "STOF"; // System to Test Object (发送帧)
                }
            }

            // 默认认为是接收帧
            return "FTOS"; // From Test Object to System (接收帧)
        }

        private string GetFrameCycle(IcdFrameItem frame)
        {
            // 从帧的配置项中获取周期信息
            if (frame?.Fields == null)
                return "/";

            // 查找周期字段
            var cycleField = frame.Fields.FirstOrDefault(f =>
                f.Name.Contains("周期") || f.DisplayName.Contains("周期"));
            if (cycleField?.ConfigItems != null)
            {
                var cycleConfig = cycleField.ConfigItems.FirstOrDefault(c =>
                    c.Name.Contains("周期"));
                if (cycleConfig != null && !string.IsNullOrEmpty(cycleConfig.Value?.ToString()))
                {
                    var cycleValue = cycleConfig.Value.ToString();
                    if (cycleValue != "/" && cycleValue != "0")
                    {
                        return cycleValue;
                    }
                }
            }

            return "/"; // 无周期信息
        }
    }
}