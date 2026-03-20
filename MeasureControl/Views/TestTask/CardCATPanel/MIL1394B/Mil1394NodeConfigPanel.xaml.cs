using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MeasureControl.Drivers;
using MeasureControl.ViewModels.TestTask;
using MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Views.TestTask.CardCATPanel.Mil1394B
{
    /// <summary>
    /// Mil1394NodeConfigPanel.xaml 的交互逻辑
    /// WPF版本的节点配置界面，替代WinForms的NodeConfigForm
    /// </summary>
    public partial class Mil1394NodeConfigPanel : UserControl
    {
        private readonly uint _cardNumber;
        private readonly uint _nodeNumber;
        private readonly IntPtr[] _pnode;
        private readonly Mil1394NodeConfigPanelViewModel _viewModel;
        private Mil1394TestPanelViewModel _parentViewModel;

        // 节点配置状态（用于保存和恢复）
        private NodeConfigState _configState;
        private bool _isInitialized = false;

        /// <summary>
        /// 设置父级ViewModel（用于访问测试任务和连接状态）
        /// </summary>
        public void SetParentViewModel(Mil1394TestPanelViewModel parentViewModel)
        {
            _parentViewModel = parentViewModel;
            UpdateButtonStates();

            // 监听连接状态变化
            if (_parentViewModel != null)
            {
                _parentViewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Mil1394TestPanelViewModel.IsDeviceConnected))
                    {
                        UpdateButtonStates();
                    }
                    if (e.PropertyName == nameof(Mil1394TestPanelViewModel.SelectedTestTask))
                    {
                        // 测试任务改变时，自动读取新任务的配置
                        if (!_isInitialized)
                        {
                            LoadConfigForCurrentTask();
                        }
                    }
                };
            }
        }

        /// <summary>
        /// 更新按钮启用/禁用状态
        /// 参考例程：连接板卡后可以对节点进行配置
        /// </summary>
        private void UpdateButtonStates()
        {
            bool isDeviceConnected = _parentViewModel?.IsDeviceConnected ?? false;
            // 修改逻辑：连接板卡后允许配置节点（和例程一致）
            bool isEnabled = true; // 连接后可以对节点进行配置

            // 查找保存配置和读取配置按钮
            var saveButton = FindName("BtnSaveConfig") as System.Windows.Controls.Button;
            var readButton = FindName("BtnReadConfig") as System.Windows.Controls.Button;

            // 查找STOF配置和异步流包接收配置按钮
            var stofConfigButton = FindName("BtnSTOFConfig") as System.Windows.Controls.Button;
            var asyncReceiveConfigButton = FindName("BtnAsyncReceiveConfig") as System.Windows.Controls.Button;

            // 查找添加Async、删除数据、编辑数据按钮
            var addAsyncButton = FindName("BtnAddAsync") as System.Windows.Controls.Button;
            var delButton = FindName("BtnDel") as System.Windows.Controls.Button;
            var changeButton = FindName("BtnChange") as System.Windows.Controls.Button;

            // 更新按钮状态
            if (saveButton != null)
            {
                saveButton.IsEnabled = isEnabled;
            }
            if (readButton != null)
            {
                readButton.IsEnabled = isEnabled;
            }
            if (stofConfigButton != null)
            {
                stofConfigButton.IsEnabled = isEnabled;
            }
            if (asyncReceiveConfigButton != null)
            {
                asyncReceiveConfigButton.IsEnabled = isEnabled;
            }
            if (addAsyncButton != null)
            {
                addAsyncButton.IsEnabled = isEnabled;
            }
            if (delButton != null)
            {
                delButton.IsEnabled = isEnabled;
            }
            if (changeButton != null)
            {
                changeButton.IsEnabled = isEnabled;
            }
        }

        public Mil1394NodeConfigPanel(uint cardNum, uint nodeNumber, IntPtr[] pnode, Mil1394NodeConfigPanelViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                _cardNumber = cardNum;
                _nodeNumber = nodeNumber;
                _pnode = pnode ?? throw new ArgumentNullException(nameof(pnode));
                _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

                DataContext = _viewModel;

                // 延迟初始化，确保控件完全加载后再初始化数据
                Loaded += Mil1394NodeConfigPanel_Loaded;
                // 界面卸载时保存状态
                Unloaded += Mil1394NodeConfigPanel_Unloaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mil1394NodeConfigPanel构造函数异常: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 界面卸载时保存配置状态
        /// </summary>
        private void Mil1394NodeConfigPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveConfigState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mil1394NodeConfigPanel_Unloaded异常: {ex}");
            }
        }

        /// <summary>
        /// 控件加载完成后初始化
        /// </summary>
        private void Mil1394NodeConfigPanel_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果已经初始化过且有保存的状态，优先恢复保存的状态
                if (_isInitialized && _configState != null)
                {
                    RestoreConfigState();
                }
                else
                {
                    // 首次初始化
                    // 先尝试从设备读取已保存的配置
                    LoadConfigForCurrentTask();
                    
                    // 如果读取的配置中没有异步接收配置，则初始化默认的128项
                    if (DgvRecvAsync != null && (DgvRecvAsync.ItemsSource == null || 
                        (DgvRecvAsync.ItemsSource as ObservableCollection<AsyncReceiveConfigItem>)?.Count == 0))
                    {
                        InitializeAsyncReceiveGrid();
                    }
                    
                    // 如果读取的配置中没有异步发送配置，则初始化为空列表
                    if (Dgv1394CfgData != null && (Dgv1394CfgData.ItemsSource == null || 
                        (Dgv1394CfgData.ItemsSource as ObservableCollection<AsyncSendConfigItem>)?.Count == 0))
                    {
                        InitializeAsyncSendGrid();
                    }
                    
                    // 初始化STOF发送方式状态
                    if (CboSTOFSendStyle != null)
                    {
                        CboSTOFSendStyle_SelectionChanged(CboSTOFSendStyle, null);
                    }
                    
                    _isInitialized = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mil1394NodeConfigPanel_Loaded异常: {ex}");
            }
        }

        /// <summary>
        /// 保存当前配置状态
        /// </summary>
        public void SaveConfigState()
        {
            try
            {
                if (ComboBoxNodeType == null || ComboBoxNodeRate == null ||
                    CboSTOFSendStyle == null || TxtSTOFPeriod == null ||
                    TxtSTOFSendTimes == null || RecvAsyncChannel == null ||
                    TextBoxSTOFVPC == null || DgvRecvAsync == null ||
                    Dgv1394CfgData == null)
                {
                    return;
                }

                // 创建AsyncReceiveConfig的副本，避免引用问题
                ObservableCollection<AsyncReceiveConfigItem> recvConfigCopy = null;
                if (DgvRecvAsync.ItemsSource is ObservableCollection<AsyncReceiveConfigItem> recvConfig)
                {
                    recvConfigCopy = new ObservableCollection<AsyncReceiveConfigItem>();
                    foreach (var item in recvConfig)
                    {
                        recvConfigCopy.Add(new AsyncReceiveConfigItem
                        {
                            IsSelected = item.IsSelected,
                            MsgID = item.MsgID,
                            DataLength = item.DataLength
                        });
                    }
                }

                // 创建AsyncSendConfig的副本，避免引用问题
                ObservableCollection<AsyncSendConfigItem> sendConfigCopy = null;
                if (Dgv1394CfgData.ItemsSource is ObservableCollection<AsyncSendConfigItem> sendConfig)
                {
                    sendConfigCopy = new ObservableCollection<AsyncSendConfigItem>();
                    foreach (var item in sendConfig)
                    {
                        sendConfigCopy.Add(new AsyncSendConfigItem
                        {
                            MessageID = item.MessageID,
                            Channel = item.Channel,
                            Heartbeat = item.Heartbeat,
                            Health = item.Health,
                            HeartbeatStep = item.HeartbeatStep,
                            PayloadLength = item.PayloadLength,
                            SendOffset = item.SendOffset,
                            VPC = item.VPC,
                            VPCAsync = item.VPCAsync,
                            Security = item.Security,
                            Priority = item.Priority,
                            PayloadData = item.PayloadData != null ? (uint[])item.PayloadData.Clone() : new uint[500],
                            TransmitOffset = item.TransmitOffset,
                            ReceiveOffset = item.ReceiveOffset,
                            PHMOffset = item.PHMOffset
                        });
                    }
                }

                _configState = new NodeConfigState
                {
                    NodeType = ComboBoxNodeType.Text ?? "BM",
                    NodeRate = ComboBoxNodeRate.Text ?? "400M",
                    STOFSendStyleIndex = CboSTOFSendStyle.SelectedIndex >= 0 ? CboSTOFSendStyle.SelectedIndex : 1,
                    STOFPeriod = TxtSTOFPeriod.Text ?? "15",
                    STOFSendTimes = TxtSTOFSendTimes.Text ?? "100",
                    RecvAsyncChannel = RecvAsyncChannel.Text ?? "0",
                    STOFVPC = TextBoxSTOFVPC.Text ?? "0",
                    STOFPayload = GetSTOFPayload(),
                    AsyncReceiveConfig = recvConfigCopy,
                    AsyncSendConfig = sendConfigCopy
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveConfigState异常: {ex}");
            }
        }

        /// <summary>
        /// 恢复配置状态（公开方法，供外部调用）
        /// </summary>
        public void RestoreConfigState()
        {
            try
            {
                if (_configState == null)
                {
                    return;
                }

                // 恢复节点初始化配置
                if (ComboBoxNodeType != null)
                {
                    ComboBoxNodeType.Text = _configState.NodeType;
                }
                if (ComboBoxNodeRate != null)
                {
                    ComboBoxNodeRate.Text = _configState.NodeRate;
                }

                // 恢复STOF配置
                if (CboSTOFSendStyle != null)
                {
                    CboSTOFSendStyle.SelectedIndex = _configState.STOFSendStyleIndex;
                }
                if (TxtSTOFPeriod != null)
                {
                    TxtSTOFPeriod.Text = _configState.STOFPeriod;
                }
                if (TxtSTOFSendTimes != null)
                {
                    TxtSTOFSendTimes.Text = _configState.STOFSendTimes;
                }
                if (TextBoxSTOFVPC != null)
                {
                    TextBoxSTOFVPC.Text = _configState.STOFVPC;
                }

                // 恢复STOF Payload
                if (_configState.STOFPayload != null && _configState.STOFPayload.Length >= 9)
                {
                    if (TextBox0 != null) TextBox0.Text = _configState.STOFPayload[0].ToString();
                    if (TextBox1 != null) TextBox1.Text = _configState.STOFPayload[1].ToString();
                    if (TextBox2 != null) TextBox2.Text = _configState.STOFPayload[2].ToString();
                    if (TextBox3 != null) TextBox3.Text = _configState.STOFPayload[3].ToString();
                    if (TextBox4 != null) TextBox4.Text = _configState.STOFPayload[4].ToString();
                    if (TextBox5 != null) TextBox5.Text = _configState.STOFPayload[5].ToString();
                    if (TextBox6 != null) TextBox6.Text = _configState.STOFPayload[6].ToString();
                    if (TextBox7 != null) TextBox7.Text = _configState.STOFPayload[7].ToString();
                    if (TextBox8 != null) TextBox8.Text = _configState.STOFPayload[8].ToString();
                }

                // 恢复异步流包接收配置
                if (RecvAsyncChannel != null)
                {
                    RecvAsyncChannel.Text = _configState.RecvAsyncChannel;
                }
                if (DgvRecvAsync != null)
                {
                    if (_configState.AsyncReceiveConfig != null && _configState.AsyncReceiveConfig.Count > 0)
                    {
                        // 创建新的集合，避免引用问题
                        var restoredRecvConfig = new ObservableCollection<AsyncReceiveConfigItem>();
                        foreach (var item in _configState.AsyncReceiveConfig)
                        {
                            restoredRecvConfig.Add(new AsyncReceiveConfigItem
                            {
                                IsSelected = item.IsSelected,
                                MsgID = item.MsgID,
                                DataLength = item.DataLength
                            });
                        }
                        DgvRecvAsync.ItemsSource = restoredRecvConfig;
                    }
                    else
                    {
                        // 如果没有保存的状态，初始化默认的128项（00-7F）
                        InitializeAsyncReceiveGrid();
                    }
                }

                // 恢复异步流包发送配置
                if (Dgv1394CfgData != null && _configState.AsyncSendConfig != null)
                {
                    // 创建新的集合，避免引用问题
                    var restoredSendConfig = new ObservableCollection<AsyncSendConfigItem>();
                    foreach (var item in _configState.AsyncSendConfig)
                    {
                        restoredSendConfig.Add(new AsyncSendConfigItem
                        {
                            MessageID = item.MessageID,
                            Channel = item.Channel,
                            Heartbeat = item.Heartbeat,
                            Health = item.Health,
                            HeartbeatStep = item.HeartbeatStep,
                            PayloadLength = item.PayloadLength,
                            SendOffset = item.SendOffset,
                            VPC = item.VPC,
                            VPCAsync = item.VPCAsync,
                            Security = item.Security,
                            Priority = item.Priority,
                            PayloadData = item.PayloadData != null ? (uint[])item.PayloadData.Clone() : new uint[500],
                            TransmitOffset = item.TransmitOffset,
                            ReceiveOffset = item.ReceiveOffset,
                            PHMOffset = item.PHMOffset
                        });
                    }
                    Dgv1394CfgData.ItemsSource = restoredSendConfig;
                }

                // 恢复STOF发送方式状态
                if (CboSTOFSendStyle != null)
                {
                    CboSTOFSendStyle_SelectionChanged(CboSTOFSendStyle, null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreConfigState异常: {ex}");
            }
        }

        /// <summary>
        /// 读取配置按钮点击事件 - 从设备CardConfigData读取配置（按测试任务）
        /// </summary>
        private void RestoreConfigState_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                string testTaskName = _parentViewModel?.SelectedTestTask;
                if (string.IsNullOrEmpty(testTaskName))
                {
                    ReMessageBox.Show("请先选择测试任务", "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 从设备读取配置（按测试任务）
                var nodeConfig = _viewModel?.LoadNodeConfig(testTaskName);
                if (nodeConfig == null)
                {
                    ReMessageBox.Show("读取配置失败，设备未初始化", "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 加载配置到UI
                LoadNodeConfigToUI(nodeConfig, showMessage: true);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"读取配置失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"RestoreConfigState_Click异常: {ex}");
            }
        }

        /// <summary>
        /// 为当前测试任务加载配置
        /// </summary>
        private void LoadConfigForCurrentTask()
        {
            try
            {
                string testTaskName = _parentViewModel?.SelectedTestTask;
                if (string.IsNullOrEmpty(testTaskName))
                {
                    return;
                }

                var nodeConfig = _viewModel?.LoadNodeConfig(testTaskName);
                if (nodeConfig != null && nodeConfig.NodeNumber == _nodeNumber &&
                    (!string.IsNullOrEmpty(nodeConfig.NodeType) ||
                     (nodeConfig.AsyncSendConfig != null && nodeConfig.AsyncSendConfig.Count > 0) ||
                     (nodeConfig.AsyncReceiveConfig != null && nodeConfig.AsyncReceiveConfig.Count > 0)))
                {
                    // 如果配置存在，恢复配置（静默加载，不显示提示）
                    LoadNodeConfigToUI(nodeConfig, showMessage: false);
                }
            }
            catch (Exception loadEx)
            {
                // 读取配置失败不影响界面初始化
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {loadEx.Message}");
            }
        }

        /// <summary>
        /// 将节点配置加载到UI
        /// </summary>
        private void LoadNodeConfigToUI(Models.Mil1394BNodeConfig nodeConfig, bool showMessage = true)
        {
            // 恢复节点初始化配置
            if (ComboBoxNodeType != null)
            {
                ComboBoxNodeType.Text = nodeConfig.NodeType ?? "BM";
            }
            if (ComboBoxNodeRate != null)
            {
                ComboBoxNodeRate.Text = nodeConfig.NodeRate ?? "400M";
            }
            if (CbxEnableBM != null)
            {
                CbxEnableBM.IsChecked = nodeConfig.BmEnabled;
            }

            // 恢复STOF配置
            if (CboSTOFSendStyle != null)
            {
                CboSTOFSendStyle.SelectedIndex = nodeConfig.StofSendStyleIndex;
            }
            if (TxtSTOFPeriod != null)
            {
                TxtSTOFPeriod.Text = nodeConfig.StofPeriod ?? "15";
            }
            if (TxtSTOFSendTimes != null)
            {
                TxtSTOFSendTimes.Text = nodeConfig.StofSendTimes ?? "100";
            }
            if (TextBoxSTOFVPC != null)
            {
                TextBoxSTOFVPC.Text = nodeConfig.StofVpc ?? "0";
            }

            // 恢复STOF Payload
            if (nodeConfig.StofPayload != null && nodeConfig.StofPayload.Length >= 9)
            {
                if (TextBox0 != null) TextBox0.Text = nodeConfig.StofPayload[0].ToString();
                if (TextBox1 != null) TextBox1.Text = nodeConfig.StofPayload[1].ToString();
                if (TextBox2 != null) TextBox2.Text = nodeConfig.StofPayload[2].ToString();
                if (TextBox3 != null) TextBox3.Text = nodeConfig.StofPayload[3].ToString();
                if (TextBox4 != null) TextBox4.Text = nodeConfig.StofPayload[4].ToString();
                if (TextBox5 != null) TextBox5.Text = nodeConfig.StofPayload[5].ToString();
                if (TextBox6 != null) TextBox6.Text = nodeConfig.StofPayload[6].ToString();
                if (TextBox7 != null) TextBox7.Text = nodeConfig.StofPayload[7].ToString();
                if (TextBox8 != null) TextBox8.Text = nodeConfig.StofPayload[8].ToString();
            }

            // 恢复异步流包接收配置
            if (RecvAsyncChannel != null)
            {
                RecvAsyncChannel.Text = nodeConfig.RecvAsyncChannel ?? "0";
            }
            if (DgvRecvAsync != null)
            {
                if (nodeConfig.AsyncReceiveConfig != null && nodeConfig.AsyncReceiveConfig.Count > 0)
                {
                    // 从保存的配置恢复
                    var restoredRecvConfig = new ObservableCollection<AsyncReceiveConfigItem>();
                    foreach (var item in nodeConfig.AsyncReceiveConfig)
                    {
                        restoredRecvConfig.Add(new AsyncReceiveConfigItem
                        {
                            IsSelected = item.IsSelected,
                            MsgID = item.MsgID,
                            DataLength = item.DataLength
                        });
                    }
                    DgvRecvAsync.ItemsSource = restoredRecvConfig;
                }
                else
                {
                    // 如果没有保存的配置，初始化默认的128项（00-7F）
                    InitializeAsyncReceiveGrid();
                }
            }

            // 恢复异步流包发送配置
            if (Dgv1394CfgData != null && nodeConfig.AsyncSendConfig != null && nodeConfig.AsyncSendConfig.Count > 0)
            {
                var restoredSendConfig = new ObservableCollection<AsyncSendConfigItem>();
                foreach (var item in nodeConfig.AsyncSendConfig)
                {
                    restoredSendConfig.Add(new AsyncSendConfigItem
                    {
                        MessageID = item.MessageID,
                        Channel = item.Channel,
                        Heartbeat = item.Heartbeat,
                        Health = item.Health,
                        HeartbeatStep = item.HeartbeatStep,
                        PayloadLength = item.PayloadLength,
                        SendOffset = item.SendOffset,
                        VPC = item.VPC,
                        VPCAsync = item.VPCAsync,
                        Security = item.Security,
                        Priority = item.Priority,
                        PayloadData = item.PayloadData != null ? (uint[])item.PayloadData.Clone() : new uint[500],
                        TransmitOffset = item.TransmitOffset,
                        ReceiveOffset = item.ReceiveOffset,
                        PHMOffset = item.PHMOffset
                    });
                }
                Dgv1394CfgData.ItemsSource = restoredSendConfig;
            }

            // 恢复STOF发送方式状态
            if (CboSTOFSendStyle != null)
            {
                CboSTOFSendStyle_SelectionChanged(CboSTOFSendStyle, null);
            }

            if (showMessage)
            {
                ReMessageBox.Show("配置读取成功", "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 初始化异步接收配置表格
        /// </summary>
        private void InitializeAsyncReceiveGrid()
        {
            var items = new ObservableCollection<AsyncReceiveConfigItem>();
            // 创建128个数据项（0x00到0x7F）
            for (int i = 0; i < 128; i++)
            {
                items.Add(new AsyncReceiveConfigItem
                {
                    IsSelected = false,
                    MsgID = Convert.ToString(i, 16).ToUpper().PadLeft(2, '0'), // 格式化为两位十六进制，如00, 01, 0A, 7F
                    DataLength = 64 // 默认64字节
                });
            }
            DgvRecvAsync.ItemsSource = items;
        }

        /// <summary>
        /// 初始化异步发送配置表格
        /// </summary>
        private void InitializeAsyncSendGrid()
        {
            var items = new ObservableCollection<AsyncSendConfigItem>();
            Dgv1394CfgData.ItemsSource = items;
        }

        private void BtnAddAsync_Click(object sender, RoutedEventArgs e)
        {
            var items = Dgv1394CfgData.ItemsSource as ObservableCollection<AsyncSendConfigItem>;
            if (items != null)
            {
                // 限制最多128行（0x00到0x7F）
                if (items.Count >= 128)
                {
                    ReMessageBox.Show("最多只能添加128行数据（MsgID从00到7F）", "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 按照官方例程的逻辑：MessageID设置为当前行号的16进制值
                // 参考官方例程：tmpdata.MessageID = uint.Parse(Convert.ToString(this.dgv_1394cfgdata.Rows.Count, 16), System.Globalization.NumberStyles.HexNumber);
                // 这里Rows.Count是添加前的行数（从0开始），转换为16进制后解析为uint
                uint messageId = uint.Parse(Convert.ToString(items.Count, 16), System.Globalization.NumberStyles.HexNumber);
                
                items.Add(new AsyncSendConfigItem
                {
                    MessageID = (int)messageId, // 按照官方例程的方式设置MessageID
                    Channel = 0,
                    Heartbeat = 0,
                    Health = 0,
                    HeartbeatStep = 0,
                    PayloadLength = 64, // 默认64字节
                    SendOffset = 0,
                    VPC = false,
                    VPCAsync = 0,
                    Security = 0,
                    Priority = 0,
                    PayloadData = new uint[500], // 初始化PayloadData数组
                    TransmitOffset = 0,
                    ReceiveOffset = 0,
                    PHMOffset = 0
                });
                
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 添加发送配置：MessageID=0x{messageId:X2}({messageId}), 当前行数={items.Count}");
            }
        }

        private void BtnDel_Click(object sender, RoutedEventArgs e)
        {
            if (Dgv1394CfgData.SelectedItem is AsyncSendConfigItem selectedItem)
            {
                var items = Dgv1394CfgData.ItemsSource as ObservableCollection<AsyncSendConfigItem>;
                items?.Remove(selectedItem);
            }
        }

        private void BtnChange_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Dgv1394CfgData == null || Dgv1394CfgData.SelectedItem == null)
                {
                    ReMessageBox.Show("请先选择要编辑的数据行", "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var selectedItem = Dgv1394CfgData.SelectedItem as AsyncSendConfigItem;
                if (selectedItem == null)
                {
                    return;
                }

                // 打开编辑数据窗口
                var editWindow = new Mil1394EditPayloadWindow(selectedItem, _nodeNumber);
                if (editWindow.ShowDialog() == true)
                {
                    try
                    {
                        // 更新数据
                        var items = Dgv1394CfgData.ItemsSource as ObservableCollection<AsyncSendConfigItem>;
                        if (items != null)
                        {
                            int index = items.IndexOf(selectedItem);
                            if (index >= 0)
                            {
                                // 从编辑窗口获取更新后的数据
                                var updatedItem = editWindow.GetUpdatedItem();
                                
                                // 确保PayloadLength有效（避免空值错误）
                                if (updatedItem.PayloadLength <= 0)
                                {
                                    updatedItem.PayloadLength = 64; // 默认64字节
                                }
                                
                                // 确保PayloadData不为null
                                if (updatedItem.PayloadData == null || updatedItem.PayloadData.Length == 0)
                                {
                                    updatedItem.PayloadData = new uint[500]; // 默认500个uint
                                }
                                
                                items[index] = updatedItem;
                                
                                // 刷新DataGrid显示
                                Dgv1394CfgData.Items.Refresh();
                                
                                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 编辑数据完成：MessageID=0x{updatedItem.MessageID:X2}, PayloadLength={updatedItem.PayloadLength}");
                            }
                        }
                    }
                    catch (Exception updateEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 更新数据失败: {updateEx.Message}");
                        ReMessageBox.Show($"更新数据失败: {updateEx.Message}", "错误",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"编辑数据失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"BtnChange_Click异常: {ex}");
            }
        }

        /// <summary>
        /// 保存配置按钮点击事件 - 保存配置到设备CardConfigData（按测试任务保存，不连接节点）
        /// </summary>
        private async void ConfigOk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 检查控件是否已初始化
                if (ComboBoxNodeType == null || ComboBoxNodeRate == null ||
                    CboSTOFSendStyle == null || TxtSTOFPeriod == null ||
                    TxtSTOFSendTimes == null || RecvAsyncChannel == null ||
                    TextBoxSTOFVPC == null || DgvRecvAsync == null ||
                    Dgv1394CfgData == null)
                {
                    ReMessageBox.Show("界面控件未完全初始化，请稍候再试", "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 检查测试任务是否已选择
                string testTaskName = _parentViewModel?.SelectedTestTask;
                if (string.IsNullOrEmpty(testTaskName))
                {
                    ReMessageBox.Show("请先选择测试任务", "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 确保DataGrid的绑定已更新（提交所有待处理的编辑）
                if (DgvRecvAsync != null)
                {
                    DgvRecvAsync.CommitEdit(); // 提交当前编辑
                    DgvRecvAsync.CommitEdit(DataGridEditingUnit.Row, true); // 提交所有行的编辑
                }

                // 获取最新的ItemsSource（确保获取的是最新的选中状态）
                var recvConfig = DgvRecvAsync?.ItemsSource as ObservableCollection<AsyncReceiveConfigItem>;
                var sendConfig = Dgv1394CfgData?.ItemsSource as ObservableCollection<AsyncSendConfigItem>;

                // 调试输出：检查选中的项
                if (recvConfig != null)
                {
                    int selectedCount = recvConfig.Count(item => item.IsSelected);
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 保存配置前检查：总共有{recvConfig.Count}项，选中{selectedCount}项");
                    if (selectedCount > 0)
                    {
                        var selectedItems = recvConfig.Where(item => item.IsSelected).Select(item => $"0x{item.MsgID}({item.MsgID})").Take(10);
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 选中的MessageID（前10个）: {string.Join(", ", selectedItems)}");
                    }
                }

                // 1. 首先保存配置到设备CardConfigData（按测试任务保存）
                bool saveSuccess = _viewModel?.SaveNodeConfig(
                    testTaskName,
                    ComboBoxNodeType.Text ?? "BM",
                    ComboBoxNodeRate.Text ?? "400M",
                    CbxEnableBM?.IsChecked ?? true,
                    CboSTOFSendStyle.SelectedIndex >= 0 ? CboSTOFSendStyle.SelectedIndex : 1,
                    TxtSTOFPeriod.Text ?? "15",
                    TxtSTOFSendTimes.Text ?? "100",
                    TextBoxSTOFVPC.Text ?? "0",
                    GetSTOFPayload(),
                    RecvAsyncChannel.Text ?? "0",
                    recvConfig,
                    sendConfig
                ) ?? false;

                if (!saveSuccess)
                {
                    ReMessageBox.Show("配置保存失败，请检查设备状态", "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 同时保存到内存状态（用于Tab切换时的恢复）
                SaveConfigState();

                // 2. 如果板卡已连接，按需打开当前节点后再应用配置到硬件（按官方方式：选中的node才打开/才操作）
                if (_parentViewModel?.IsDeviceConnected == true)
                {
                    try
                    {
                        if (_pnode != null && _pnode[_nodeNumber] == IntPtr.Zero)
                        {
                            var handle = await _parentViewModel.EnsureNodeOpenedAsync(_nodeNumber);
                            if (handle == IntPtr.Zero)
                            {
                                ReMessageBox.Show("节点打开失败，无法应用配置到硬件", "错误",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                                return;
                            }
                        }

                        if (_pnode == null || _pnode[_nodeNumber] == IntPtr.Zero)
                        {
                            ReMessageBox.Show("节点未连接，无法应用配置到硬件", "错误",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                            return;
                        }

                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}已连接，立即应用配置到硬件（严格按照例程逻辑）");
                        
                        // 确保在应用配置前，DataGrid的绑定已更新
                        if (DgvRecvAsync != null)
                        {
                            DgvRecvAsync.CommitEdit(); // 提交当前编辑
                            DgvRecvAsync.CommitEdit(DataGridEditingUnit.Row, true); // 提交所有行的编辑
                        }

                        // 获取最新的ItemsSource（确保获取的是最新的选中状态）
                        var recvConfigForApply = DgvRecvAsync?.ItemsSource as ObservableCollection<AsyncReceiveConfigItem>;
                        var sendConfigForApply = Dgv1394CfgData?.ItemsSource as ObservableCollection<AsyncSendConfigItem>;

                        // 调试输出：检查应用配置时的选中状态
                        if (recvConfigForApply != null)
                        {
                            int selectedCountForApply = recvConfigForApply.Count(item => item.IsSelected);
                            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 应用配置前检查：总共有{recvConfigForApply.Count}项，选中{selectedCountForApply}项");
                        }

                        // 严格按照例程中的configOk_Click逻辑应用配置
                        // 例程中会执行：节点初始化、STOF配置、异步接收配置、异步发送配置、启动模拟错误
                        _viewModel?.ApplyConfiguration(
                            ComboBoxNodeType.Text ?? "BM",
                            ComboBoxNodeRate.Text ?? "400M",
                            CbxEnableBM?.IsChecked ?? true,
                            CboSTOFSendStyle.SelectedIndex >= 0 ? CboSTOFSendStyle.SelectedIndex : 1,
                            TxtSTOFPeriod.Text ?? "15",
                            TxtSTOFSendTimes.Text ?? "100",
                            RecvAsyncChannel.Text ?? "0",
                            GetSTOFPayload(),
                            TextBoxSTOFVPC.Text ?? "0",
                            recvConfigForApply,
                            sendConfigForApply,
                            false // 保存配置时不自动重启发送（和例程一致，例程中不会自动重启）
                        );
                        
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 配置已应用到硬件");
                        
                        // 3. 显示"配置完成"消息（和例程一致）
                        ReMessageBox.Show("配置完成", "提示", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 应用配置到硬件失败: {ex.Message}");
                        ReMessageBox.Show($"配置保存成功，但应用到硬件失败: {ex.Message}", "错误", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Error);
                    }
                }
                else
                {
                    // 节点未连接，只保存配置，不应用硬件
                    ReMessageBox.Show("配置保存成功\n\n注意：板卡未连接，配置已保存到配置文件但未应用到硬件\n\n连接板卡后，请再次点击\"保存配置\"按钮以将配置应用到硬件", "提示", 
                        System.Windows.MessageBoxButton.OK, 
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"配置保存失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"ConfigOk_Click异常: {ex}");
            }
        }

        /// <summary>
        /// 获取STOF Payload值
        /// </summary>
        private uint[] GetSTOFPayload()
        {
            return new uint[]
            {
                ParseUInt(TextBox0.Text),
                ParseUInt(TextBox1.Text),
                ParseUInt(TextBox2.Text),
                ParseUInt(TextBox3.Text),
                ParseUInt(TextBox4.Text),
                ParseUInt(TextBox5.Text),
                ParseUInt(TextBox6.Text),
                ParseUInt(TextBox7.Text),
                ParseUInt(TextBox8.Text)
            };
        }

        private uint ParseUInt(string text)
        {
            if (uint.TryParse(text, out uint result))
                return result;
            return 0;
        }

        /// <summary>
        /// 异步接收配置项
        /// </summary>
        public class AsyncReceiveConfigItem : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isSelected;
            private string _msgID;
            private int _dataLength;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public string MsgID
            {
                get => _msgID;
                set
                {
                    if (_msgID != value)
                    {
                        _msgID = value;
                        OnPropertyChanged(nameof(MsgID));
                    }
                }
            }

            public int DataLength
            {
                get => _dataLength;
                set
                {
                    if (_dataLength != value)
                    {
                        _dataLength = value;
                        OnPropertyChanged(nameof(DataLength));
                    }
                }
            }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// 异步发送配置项
        /// </summary>
        public class AsyncSendConfigItem
        {
            public int MessageID { get; set; }
            public int Channel { get; set; }
            public int Heartbeat { get; set; }
            public int Health { get; set; }
            public int HeartbeatStep { get; set; }
            public int PayloadLength { get; set; }
            public int SendOffset { get; set; }
            public bool VPC { get; set; }
            public int VPCAsync { get; set; }

            // Payload数据字段（用于编辑窗口）
            public uint Security { get; set; } = 0;
            public uint Priority { get; set; } = 0;
            public uint[] PayloadData { get; set; } = new uint[500]; // 最大500个uint
            public uint TransmitOffset { get; set; } = 0;
            public uint ReceiveOffset { get; set; } = 0;
            public uint PHMOffset { get; set; } = 0;
        }

        private void Dgv1394CfgData_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        /// <summary>
        /// STOF发送方式切换事件处理
        /// </summary>
        private void CboSTOFSendStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (CboSTOFSendStyle == null || TxtSTOFPeriod == null || TxtSTOFSendTimes == null)
                {
                    return; // 控件未初始化，忽略
                }

                if (CboSTOFSendStyle.SelectedIndex == 0) // 按周期
                {
                    TxtSTOFPeriod.IsEnabled = true;
                    TxtSTOFSendTimes.IsEnabled = false;
                }
                else if (CboSTOFSendStyle.SelectedIndex == 1) // 按次数
                {
                    TxtSTOFPeriod.IsEnabled = false;
                    TxtSTOFSendTimes.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CboSTOFSendStyle_SelectionChanged异常: {ex}");
            }
        }

        /// <summary>
        /// 全选/取消全选 - 全选事件
        /// </summary>
        private void RecvChk_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DgvRecvAsync == null)
                {
                    return; // 控件未初始化，忽略
                }

                var items = DgvRecvAsync.ItemsSource as ObservableCollection<AsyncReceiveConfigItem>;
                if (items != null)
                {
                    // 先暂停数据绑定更新，提高性能
                    DgvRecvAsync.ItemsSource = null;

                    // 批量更新所有项
                    foreach (var item in items)
                    {
                        item.IsSelected = true;
                    }

                    // 恢复数据绑定
                    DgvRecvAsync.ItemsSource = items;

                    // 强制刷新显示
                    DgvRecvAsync.Items.Refresh();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecvChk_Checked异常: {ex}");
            }
        }

        /// <summary>
        /// 全选/取消全选 - 取消全选事件
        /// </summary>
        private void RecvChk_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DgvRecvAsync == null)
                {
                    return; // 控件未初始化，忽略
                }

                var items = DgvRecvAsync.ItemsSource as ObservableCollection<AsyncReceiveConfigItem>;
                if (items != null)
                {
                    // 先暂停数据绑定更新，提高性能
                    DgvRecvAsync.ItemsSource = null;

                    // 批量更新所有项
                    foreach (var item in items)
                    {
                        item.IsSelected = false;
                    }

                    // 恢复数据绑定
                    DgvRecvAsync.ItemsSource = items;

                    // 强制刷新显示
                    DgvRecvAsync.Items.Refresh();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecvChk_Unchecked异常: {ex}");
            }
        }

        /// <summary>
        /// DataGrid加载完成后，查找列头复选框
        /// </summary>
        private void DgvRecvAsync_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 查找列头中的复选框
                var checkBox = FindVisualChild<CheckBox>(DgvRecvAsync);
                if (checkBox != null && checkBox.Name == "RecvChk")
                {
                    // 确保事件已绑定
                    checkBox.Checked -= RecvChk_Checked;
                    checkBox.Unchecked -= RecvChk_Unchecked;
                    checkBox.Checked += RecvChk_Checked;
                    checkBox.Unchecked += RecvChk_Unchecked;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DgvRecvAsync_Loaded异常: {ex}");
            }
        }

        /// <summary>
        /// 查找可视化子元素
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        /// <summary>
        /// STOF配置按钮点击事件
        /// </summary>
        private void BtnSTOFConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (STOFConfigPanel != null)
                {
                    STOFConfigPanel.Visibility = Visibility.Visible;
                }
                if (AsyncReceiveConfigPanel != null)
                {
                    AsyncReceiveConfigPanel.Visibility = Visibility.Collapsed;
                }

                // 更新按钮样式
                if (BtnSTOFConfig != null)
                {
                    BtnSTOFConfig.Style = (Style)FindResource("PrimaryButtonStyle");
                }
                if (BtnAsyncReceiveConfig != null)
                {
                    BtnAsyncReceiveConfig.Style = (Style)FindResource("PrimaryButtonStyle");
                    BtnAsyncReceiveConfig.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnSTOFConfig_Click异常: {ex}");
            }
        }

        /// <summary>
        /// 异步流包接收配置按钮点击事件
        /// </summary>
        private void BtnAsyncReceiveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (STOFConfigPanel != null)
                {
                    STOFConfigPanel.Visibility = Visibility.Collapsed;
                }
                if (AsyncReceiveConfigPanel != null)
                {
                    AsyncReceiveConfigPanel.Visibility = Visibility.Visible;
                }

                // 更新按钮样式
                if (BtnAsyncReceiveConfig != null)
                {
                    BtnAsyncReceiveConfig.Style = (Style)FindResource("PrimaryButtonStyle");
                }
                if (BtnSTOFConfig != null)
                {
                    BtnSTOFConfig.Style = (Style)FindResource("PrimaryButtonStyle");
                    BtnSTOFConfig.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnAsyncReceiveConfig_Click异常: {ex}");
            }
        }

        /// <summary>
        /// 节点配置状态类
        /// </summary>
        private class NodeConfigState
        {
            public string NodeType { get; set; }
            public string NodeRate { get; set; }
            public int STOFSendStyleIndex { get; set; }
            public string STOFPeriod { get; set; }
            public string STOFSendTimes { get; set; }
            public string RecvAsyncChannel { get; set; }
            public string STOFVPC { get; set; }
            public uint[] STOFPayload { get; set; }
            public ObservableCollection<AsyncReceiveConfigItem> AsyncReceiveConfig { get; set; }
            public ObservableCollection<AsyncSendConfigItem> AsyncSendConfig { get; set; }
        }

        /// <summary>
        /// 处理ScrollViewer的鼠标滚轮事件，确保可以滚动
        /// </summary>
        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ScrollViewer scrollViewer)
            {
                // 使用PageUp/PageDown实现更流畅的滚动
                if (e.Delta > 0)
                {
                    scrollViewer.PageUp();
                }
                else
                {
                    scrollViewer.PageDown();
                }
                e.Handled = true;
            }
        }
    }
}
