using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MeasureControl.ViewModels;
using MeasureControl.Models;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// CreateIcdConfigTabelDialog.xaml 的交互逻辑
    /// </summary>
    public partial class CreateIcdConfigTabelDialog : Window, INotifyPropertyChanged
    {
        private readonly IDictionary<string, List<string>> _protocolChannelMap;
        private string _tabelName;
        private string _selectedProtocol;
        private string _selectedChannelBinding;

        public string TabelName
        {
            get => _tabelName;
            set
            {
                _tabelName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullTabelName));
                UpdateOkButtonState();
            }
        }

        public string SelectedProtocol
        {
            get => _selectedProtocol;
            set
            {
                _selectedProtocol = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullTabelName));
                UpdateAvailableChannels();
                OnPropertyChanged(nameof(ShowNoChannelMessage));
                UpdateOkButtonState();
            }
        }

        public string SelectedChannelBinding
        {
            get => _selectedChannelBinding;
            set
            {
                if (_selectedChannelBinding != value)
                {
                    _selectedChannelBinding = value;
                    OnPropertyChanged();
                    UpdateOkButtonState();
                }
            }
        }

        /// <summary>
        /// 完整的配置表名称（包含协议后缀）
        /// </summary>
        public string FullTabelName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TabelName))
                    return string.Empty;
                
                if (string.IsNullOrEmpty(SelectedProtocol))
                    return TabelName;
                
                // 获取协议后缀
                string protocolSuffix = GetProtocolSuffix(SelectedProtocol);
                return $"{TabelName.Trim()}-{protocolSuffix}";
            }
        }

        /// <summary>
        /// 获取协议后缀
        /// </summary>
        private string GetProtocolSuffix(string protocol)
        {
            return protocol switch
            {
                "CAN" => "CAN",
                "ARINC429" => "429",
                "1553B" => "1553",
                "MIL1394" => "1394",
                _ => protocol
            };
        }

        public ObservableCollection<string> AvailableProtocols { get; }
        public ObservableCollection<string> AvailableChannelBindings { get; } = new ObservableCollection<string>();

        public bool IsChannelSelectionVisible => AvailableChannelBindings.Count > 0;
        public bool ShowNoChannelMessage => !IsChannelSelectionVisible && !string.IsNullOrEmpty(SelectedProtocol);

        public CreateIcdConfigTabelDialog(string defaultName, IDictionary<string, List<string>> protocolChannelMap = null)
        {
            InitializeComponent();
            _protocolChannelMap = protocolChannelMap != null
                ? new Dictionary<string, List<string>>(protocolChannelMap, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            // Debug 输出：打印传入的协议-通道映射，便于诊断为何某协议的通道未出现
            Debug.WriteLine("CreateIcdConfigTabelDialog: protocolChannelMap contents:");
            if (_protocolChannelMap.Count == 0)
            {
                Debug.WriteLine("  (empty)");
            }
            else
            {
                foreach (var kv in _protocolChannelMap)
                {
                    var channelsText = kv.Value != null && kv.Value.Count > 0 ? string.Join(", ", kv.Value) : "(no channels)";
                    Debug.WriteLine($"  Protocol='{kv.Key}', Channels={channelsText}");
                }
            }
            // 如果传入映射为空，尝试从静态缓存获取所有通道（包含尚未打开但已保存的通道）
            if (_protocolChannelMap.Count == 0)
            {
                try
                {
                    var allChannels = ChannelConfigTabelViewModel.GetAllChannelTabelItems();
                    if (allChannels != null && allChannels.Count > 0)
                    {
                        Debug.WriteLine("CreateIcdConfigTabelDialog: Fallback - populating protocolChannelMap from cached channel tabels.");
                        foreach (var kvp in allChannels)
                        {
                            if (kvp.Value == null) continue;
                            foreach (var channel in kvp.Value)
                            {
                                if (channel == null || channel.IsEmpty) continue;
                                if (!string.Equals(channel.ChannelType, "通讯通道", StringComparison.Ordinal)) continue;
                                var protocolKey = (channel.InputOutputType ?? string.Empty).Trim();
                                if (string.IsNullOrEmpty(protocolKey)) continue;
                                var displayName = !string.IsNullOrEmpty(channel.CardName)
                                    ? $"{channel.ChannelName} ({channel.CardName})"
                                    : channel.ChannelName ?? string.Empty;
                                if (!_protocolChannelMap.TryGetValue(protocolKey, out var list))
                                {
                                    list = new List<string>();
                                    _protocolChannelMap[protocolKey] = list;
                                }
                                if (!list.Any(existing => string.Equals(existing, displayName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    list.Add(displayName);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CreateIcdConfigTabelDialog: fallback populate failed: {ex}");
                }
            }
            TabelName = defaultName;
            SelectedProtocol = null;
            AvailableProtocols = new ObservableCollection<string> { "CAN", "ARINC429", "1553B", "MIL1394" };
            DataContext = this;
            
            Loaded += (s, e) =>
            {
                NameTextBox.Focus();
                NameTextBox.SelectAll();
                UpdateOkButtonState();
            };
        }

        private void UpdateAvailableChannels()
        {
            Debug.WriteLine($"CreateIcdConfigTabelDialog.UpdateAvailableChannels: SelectedProtocol='{SelectedProtocol ?? "<null>"}' called.");
            string previousSelection = SelectedChannelBinding;
            AvailableChannelBindings.Clear();

            if (!string.IsNullOrEmpty(SelectedProtocol) &&
                _protocolChannelMap.TryGetValue(SelectedProtocol, out var channels) &&
                channels != null)
            {
                foreach (var name in channels.Where(n => !string.IsNullOrWhiteSpace(n)))
                {
                    AvailableChannelBindings.Add(name);
                }
            }

            Debug.WriteLine($"CreateIcdConfigTabelDialog.UpdateAvailableChannels: AvailableChannelBindings.Count={AvailableChannelBindings.Count}");

            if (AvailableChannelBindings.Count > 0)
            {
                if (!string.IsNullOrEmpty(previousSelection) &&
                    AvailableChannelBindings.Any(c => string.Equals(c, previousSelection, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedChannelBinding = previousSelection;
                }
                else
                {
                    SelectedChannelBinding = AvailableChannelBindings[0];
                }
            }
            else
            {
                SelectedChannelBinding = null;
            }

            OnPropertyChanged(nameof(IsChannelSelectionVisible));
            OnPropertyChanged(nameof(ShowNoChannelMessage));
        }

        private void UpdateOkButtonState()
        {
            if (OkButton != null)
            {
                bool hasChannelSelection = !string.IsNullOrEmpty(SelectedChannelBinding);
                OkButton.IsEnabled = !string.IsNullOrWhiteSpace(TabelName) &&
                                     !string.IsNullOrEmpty(SelectedProtocol) &&
                                     hasChannelSelection;
            }
        }

        private void NameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateOkButtonState();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TabelName))
            {
                ReMessageBox.Show("请输入配置表名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(SelectedProtocol))
            {
                ReMessageBox.Show("请选择一个协议", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProtocolComboBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(SelectedChannelBinding))
            {
                var message = IsChannelSelectionVisible
                    ? "请选择一个通讯通道"
                    : "当前协议没有可用的通讯通道，请先在通道配置表中创建。";
                ReMessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            TabelName = null;
            SelectedProtocol = null;
            SelectedChannelBinding = null;
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            TabelName = null;
            SelectedProtocol = null;
            SelectedChannelBinding = null;
            DialogResult = false;
            Close();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

