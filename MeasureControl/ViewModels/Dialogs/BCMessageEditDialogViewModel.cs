using MeasureControl.ViewModels.TestTask.CardCATPanel;
using MeasureControl.Views.Dialogs;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace MeasureControl.ViewModels.Dialogs
{
    /// <summary>
    /// BC消息编辑对话框ViewModel
    /// </summary>
    public class BCMessageEditDialogViewModel : BindableBase
    {
        private string _messageName;
        private string _messageType;
        private int _channelSelect = 1; // 0:通道B（备份通道）, 1:通道A（主通道）- 这是1553B双冗余总线的概念，不是物理通道0/1
        private int _dataLength = 1;
        private int _rtAddress = 1;
        private int _subAddress = 1;
        private int _rtAddress2 = 1;
        private int _subAddress2 = 1;
        private int _modeCode = 0;
        private ObservableCollection<DataWordItem> _dataWords;

        public BCMessageEditDialogViewModel()
        {
            MessageTypes = new List<string>
            {
                "BC->RT",
                "RT->BC",
                "RT->RT",
                "Mode Code",
                "Broadcast",
                "RT->RTs",
                "Broadcast Mode Code"
            };
            
            DataWords = new ObservableCollection<DataWordItem>();
        }

        public BCMessageEditDialogViewModel(ART1553BConfigPanelViewModel.MessageConfigItem message) : this()
        {
            if (message != null)
            {
                MessageName = message.MessageName;
                MessageType = message.MessageType;
                ChannelSelect = message.ChannelSelect;
                DataLength = message.DataLength > 0 ? message.DataLength : 32;
                RTAddress = message.RTAddress;
                SubAddress = message.SubAddress;
                RTAddress2 = message.RTAddress2;
                SubAddress2 = message.SubAddress2;
                ModeCode = message.ModeCode;
                
                // 解析数据
                ParseDataHex(message.DataHex);
            }
        }

        public List<string> MessageTypes { get; }

        public string MessageName
        {
            get => _messageName;
            set
            {
                SetProperty(ref _messageName, value);
            }
        }

        public string MessageType
        {
            get => _messageType;
            set
            {
                if (SetProperty(ref _messageType, value))
                {
                    RaisePropertyChanged(nameof(IsBCToRTMessage));
                    RaisePropertyChanged(nameof(IsRTToBCMessage));
                    RaisePropertyChanged(nameof(IsRTToRTMessage));
                    RaisePropertyChanged(nameof(IsModeCodeMessage));
                    RaisePropertyChanged(nameof(ShowRTDataHint));
                    RaisePropertyChanged(nameof(RTDataHintText));
                }
            }
        }

        public int ChannelSelect
        {
            get => _channelSelect;
            set => SetProperty(ref _channelSelect, value);
        }

        public int DataLength
        {
            get => _dataLength;
            set
            {
                if (SetProperty(ref _dataLength, value))
                {
                    UpdateDataWords();
                    RaisePropertyChanged(nameof(DataLengthHint));
                }
            }
        }

        public int RTAddress
        {
            get => _rtAddress;
            set
            {
                if (SetProperty(ref _rtAddress, value))
                {
                    RaisePropertyChanged(nameof(RTDataHintText));
                }
            }
        }

        public int SubAddress
        {
            get => _subAddress;
            set
            {
                if (SetProperty(ref _subAddress, value))
                {
                    RaisePropertyChanged(nameof(RTDataHintText));
                }
            }
        }

        public int RTAddress2
        {
            get => _rtAddress2;
            set
            {
                if (SetProperty(ref _rtAddress2, value))
                {
                    RaisePropertyChanged(nameof(RTDataHintText));
                }
            }
        }

        public int SubAddress2
        {
            get => _subAddress2;
            set
            {
                if (SetProperty(ref _subAddress2, value))
                {
                    RaisePropertyChanged(nameof(RTDataHintText));
                }
            }
        }

        public int ModeCode
        {
            get => _modeCode;
            set => SetProperty(ref _modeCode, value);
        }

        public ObservableCollection<DataWordItem> DataWords
        {
            get => _dataWords;
            set => SetProperty(ref _dataWords, value);
        }

        public bool IsBCToRTMessage => MessageType == "BC->RT";
        public bool IsRTToBCMessage => MessageType == "RT->BC";
        public bool IsRTToRTMessage => MessageType == "RT->RT";
        public bool IsModeCodeMessage => MessageType == "Mode Code" || MessageType == "Broadcast Mode Code";

        /// <summary>
        /// 是否显示RT数据配置提示（RT→BC和RT→RT模式）
        /// </summary>
        public bool ShowRTDataHint => IsRTToBCMessage || IsRTToRTMessage;

        /// <summary>
        /// RT数据配置提示文本
        /// </summary>
        public string RTDataHintText
        {
            get
            {
                if (IsRTToBCMessage)
                    return $"RT→BC模式：数据由RT{RTAddress}的子地址{SubAddress}提供，请在RT模式下配置RT{RTAddress}内部的子地址{SubAddress}发送数据。";
                if (IsRTToRTMessage)
                    return $"RT→RT模式：数据由发送方RT{RTAddress}的子地址{SubAddress}提供，请在RT模式下配置RT{RTAddress}内部的子地址{SubAddress}发送数据。接收方为RT{RTAddress2}子地址{SubAddress2}。";
                return string.Empty;
            }
        }

        public string DataLengthHint => $"共 {DataLength} 个字，每个字2字节（4个16进制字符），例如：1234";

        /// <summary>
        /// 更新数据字列表
        /// </summary>
        private void UpdateDataWords()
        {
            var currentCount = DataWords.Count;
            var targetCount = Math.Max(1, Math.Min(32, DataLength));

            // 添加或删除数据字
            if (targetCount > currentCount)
            {
                for (int i = currentCount; i < targetCount; i++)
                {
                    DataWords.Add(new DataWordItem { WordIndex = i + 1, Value = "0000" }); // 2字节，4个16进制字符
                }
            }
            else if (targetCount < currentCount)
            {
                while (DataWords.Count > targetCount)
                {
                    DataWords.RemoveAt(DataWords.Count - 1);
                }
            }
        }

        /// <summary>
        /// 解析16进制数据字符串
        /// </summary>
        private void ParseDataHex(string dataHex)
        {
            if (string.IsNullOrWhiteSpace(dataHex))
            {
                UpdateDataWords();
                return;
            }

            // 移除空格，提取16进制字符
            var hexString = Regex.Replace(dataHex, @"\s+", "");
            
            // 每4个字符为一个字（2字节）
            var words = new List<string>();
            for (int i = 0; i < hexString.Length; i += 4)
            {
                if (i + 4 <= hexString.Length)
                {
                    words.Add(hexString.Substring(i, 4));
                }
                else
                {
                    // 不足4个字符，补齐0
                    words.Add(hexString.Substring(i).PadRight(4, '0'));
                }
            }

            // 更新字数
            DataLength = Math.Max(1, Math.Min(32, words.Count));
            
            // 更新数据字列表
            DataWords.Clear();
            for (int i = 0; i < DataLength; i++)
            {
                var value = i < words.Count ? words[i] : "0000";
                DataWords.Add(new DataWordItem { WordIndex = i + 1, Value = value });
            }
        }

        /// <summary>
        /// 获取数据16进制字符串（每个字4个16进制字符，空格分隔，如：1111 2222 3333）
        /// </summary>
        public string GetDataHex()
        {
            var hexStrings = DataWords.Select(w => (w.Value ?? "0000").PadLeft(4, '0').ToUpper()).ToList();
            return string.Join(" ", hexStrings);
        }

        /// <summary>
        /// 验证输入
        /// </summary>
        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(MessageName))
            {
                ReMessageBox.Show("请输入消息名称", "验证错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(MessageType))
            {
                ReMessageBox.Show("请选择消息类型", "验证错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            if (DataLength < 1 || DataLength > 32)
            {
                ReMessageBox.Show("字数必须在1-32之间", "验证错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            // 验证数据字格式
            foreach (var word in DataWords)
            {
                if (string.IsNullOrWhiteSpace(word.Value))
                {
                    ReMessageBox.Show($"字{word.WordIndex}的数据不能为空", "验证错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return false;
                }

                // 验证16进制格式（4个字符，2字节）
                if (!Regex.IsMatch(word.Value, @"^[0-9A-Fa-f]{1,4}$"))
                {
                    ReMessageBox.Show($"字{word.WordIndex}的数据格式错误，请输入1-4个16进制字符", "验证错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 更新消息配置项
        /// </summary>
        public void UpdateMessageConfig(ART1553BConfigPanelViewModel.MessageConfigItem message)
        {
            if (message == null) return;

            message.MessageName = MessageName;
            message.MessageType = MessageType;
            message.ChannelSelect = ChannelSelect;
            message.DataLength = DataLength;
            message.RTAddress = RTAddress;
            message.SubAddress = SubAddress;
            message.RTAddress2 = RTAddress2;
            message.SubAddress2 = SubAddress2;
            message.ModeCode = ModeCode;
            message.DataHex = GetDataHex();
        }
    }

    /// <summary>
    /// 数据字项
    /// </summary>
    public class DataWordItem : BindableBase
    {
        private int _wordIndex;
        private string _value;

        public int WordIndex
        {
            get => _wordIndex;
            set => SetProperty(ref _wordIndex, value);
        }

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }
}

