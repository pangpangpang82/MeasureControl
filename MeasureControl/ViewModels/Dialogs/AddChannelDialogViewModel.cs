using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class AddChannelDialogViewModel : BindableBase
    {
        private readonly IPxiChassisService _pxiChassisService;
        private readonly string _chassisName; // 当前机箱名称
        private readonly string _testTaskName; // 当前测试任务名称

        #region Private Fields

        private string _selectedChannelType;
        private string _channelName;
        private DeviceBase _selectedCard;
        private string _selectedInputOutputType;
        private string _selectedAssociatedChannel;
        private string _remarks;
        private ObservableCollection<DeviceBase> _availableCards;
        private ObservableCollection<string> _availableInputOutputTypes;
        private ObservableCollection<string> _availableChannels;
        private ChannelTabelItem _result;
        private string _autoFilledChannelName; // 记录自动填充的通道名称
        private bool _isUserEditedChannelName; // 标记用户是否手动编辑过通道名称
        private bool _isUpdatingChannelName; // 标记是否正在程序更新通道名称（非用户输入）
        private readonly Dictionary<DeviceBase, string> _cardChassisMap = new Dictionary<DeviceBase, string>();
        private string _selectedCardChassisName;

        #endregion

        #region Properties

        public string SelectedChannelType
        {
            get => _selectedChannelType;
            set
            {
                if (SetProperty(ref _selectedChannelType, value))
                {
                    OnChannelTypeChanged();
                }
            }
        }

        public string ChannelName
        {
            get => _channelName;
            set
            {
                // 如果不是程序更新，且值改变了，则标记为用户编辑
                // 程序更新时会设置 _isUpdatingChannelName = true，所以这里只处理用户输入
                if (!_isUpdatingChannelName && value != _channelName)
                {
                    _isUserEditedChannelName = true;
                }
                SetProperty(ref _channelName, value);
            }
        }

        /// <summary>
        /// 标记通道名称为用户已编辑（从 View 层调用）
        /// </summary>
        public void MarkChannelNameAsUserEdited()
        {
            _isUserEditedChannelName = true;
        }

        public DeviceBase SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (SetProperty(ref _selectedCard, value))
                {
                    OnCardChanged();
                }
            }
        }

        public string SelectedInputOutputType
        {
            get => _selectedInputOutputType;
            set
            {
                if (SetProperty(ref _selectedInputOutputType, value))
                {
                    OnInputOutputTypeChanged();
                }
            }
        }

        public string SelectedAssociatedChannel
        {
            get => _selectedAssociatedChannel;
            set
            {
                if (SetProperty(ref _selectedAssociatedChannel, value))
                {
                    OnAssociatedChannelChanged();
                }
            }
        }

        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        public ObservableCollection<DeviceBase> AvailableCards
        {
            get => _availableCards;
            set => SetProperty(ref _availableCards, value);
        }

        public ObservableCollection<string> AvailableInputOutputTypes
        {
            get => _availableInputOutputTypes;
            set => SetProperty(ref _availableInputOutputTypes, value);
        }

        public ObservableCollection<string> AvailableChannels
        {
            get => _availableChannels;
            set => SetProperty(ref _availableChannels, value);
        }

        public ObservableCollection<string> AvailableChannelTypes { get; }

        public ChannelTabelItem Result
        {
            get => _result;
            private set => SetProperty(ref _result, value);
        }

        /// <summary>
        /// 外部（如 ChannelConfigTabelViewModel）使用的机箱名称绑定，内部实际存入 _selectedCardChassisName。
        /// </summary>
        public string SelectedChassis
        {
            get => _selectedCardChassisName;
            set
            {
                if (SetProperty(ref _selectedCardChassisName, value))
                {
                    // 机箱变更时，重算可用卡片列表
                    LoadCardsForChannelType();
                    ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
                }
            }
        }

        #endregion

        #region Visibility Properties

        public bool IsChannelNameVisible => true;
        public bool IsCardVisible => true;
        public bool IsInputOutputTypeVisible => true;
        public bool IsAssociatedChannelVisible => true;
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

        public AddChannelDialogViewModel(IPxiChassisService pxiChassisService, string chassisName = null, string testTaskName = null)
        {
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _chassisName = chassisName;
            _testTaskName = testTaskName;
            
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] 构造函数: ChassisName={_chassisName}, TestTaskName={_testTaskName}");

            AvailableCards = new ObservableCollection<DeviceBase>();
            AvailableInputOutputTypes = new ObservableCollection<string>();
            AvailableChannels = new ObservableCollection<string>();
            AvailableChannelTypes = new ObservableCollection<string>
            {
                "离散量通道",
                "模拟量通道",
                "通讯通道",
                "其他通道"
            };

            OkCommand = new DelegateCommand(OnOk, CanOk);
            CancelCommand = new DelegateCommand(OnCancel);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 预填充对话框数据（从树节点信息）
        /// </summary>
        public void PrefillData(ChannelTabelItem template)
        {
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] PrefillData 开始: ChassisName={_chassisName}, TestTaskName={_testTaskName}, template.ChassisName={template?.ChassisName}, template.CardName={template?.CardName}, template.ChannelName={template?.ChannelName}");
            if (template == null)
                return;

            if (!string.IsNullOrEmpty(template.ChannelType))
            {
                SelectedChannelType = template.ChannelType;
            }

            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrEmpty(template.CardName))
                {
                    // 优先使用构造函数传入的机箱名称，如果没有则使用template中的
                    string chassisNameToUse = !string.IsNullOrEmpty(_chassisName) ? _chassisName : template.ChassisName;
                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] PrefillData: 查找板卡，使用机箱名称={chassisNameToUse}");
                    var card = FindCardByNames(chassisNameToUse, template.CardName);
                    if (card != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] PrefillData: 找到板卡 {card.CardName}");
                        SelectedCard = card;

                        if (!string.IsNullOrEmpty(template.InputOutputType))
                        {
                            SelectedInputOutputType = template.InputOutputType;

                            if (!string.IsNullOrEmpty(template.AssociatedChannel))
                            {
                                SelectedAssociatedChannel = template.AssociatedChannel;
                            }
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(template.InputOutputType))
                    {
                        SelectedInputOutputType = template.InputOutputType;
                    }

                    if (!string.IsNullOrEmpty(template.AssociatedChannel))
                    {
                        SelectedAssociatedChannel = template.AssociatedChannel;
                    }
                }

                if (!string.IsNullOrEmpty(template.Remarks))
                {
                    Remarks = template.Remarks;
                }
            }), DispatcherPriority.Loaded);
        }

        #endregion

        #region Private Methods

        private void OnChannelTypeChanged()
        {
            SelectedCard = null;
            SelectedInputOutputType = null;
            SelectedAssociatedChannel = null;
            _selectedCardChassisName = null;
            _isUpdatingChannelName = true;
            try
            {
                ChannelName = null;
                Remarks = null;
                _autoFilledChannelName = null;
                _isUserEditedChannelName = false;
            }
            finally
            {
                _isUpdatingChannelName = false;
            }

            AvailableCards.Clear();
            AvailableInputOutputTypes.Clear();
            AvailableChannels.Clear();

            LoadCardsForChannelType();

            RaisePropertyChanged(nameof(IsChannelNameVisible));
            RaisePropertyChanged(nameof(IsCardVisible));
            RaisePropertyChanged(nameof(IsInputOutputTypeVisible));
            RaisePropertyChanged(nameof(IsAssociatedChannelVisible));
            RaisePropertyChanged(nameof(IsRemarksVisible));
            ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
        }

        private void LoadCardsForChannelType()
        {
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] LoadCardsForChannelType 开始: ChassisName={_chassisName}, TestTaskName={_testTaskName}");
            AvailableCards.Clear();
            _cardChassisMap.Clear();

            var allChassisList = _pxiChassisService?.GetAllChassis();
            if (allChassisList == null)
            {
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] LoadCardsForChannelType: 没有找到机箱列表");
                RaisePropertyChanged(nameof(IsCardVisible));
                return;
            }

            // 如果指定了机箱名称，只遍历该机箱
            IEnumerable<ChassisModel> chassisList = allChassisList;
            if (!string.IsNullOrEmpty(_chassisName))
            {
                chassisList = allChassisList.Where(c => string.Equals(c.Name, _chassisName, StringComparison.Ordinal)).ToList();
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] LoadCardsForChannelType: 过滤后找到 {chassisList.Count()} 个匹配的机箱（机箱名称={_chassisName}）");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] LoadCardsForChannelType: 找到 {chassisList.Count()} 个机箱（未指定机箱名称，显示所有机箱）");
            }

            foreach (var chassis in chassisList)
            {
                if (chassis?.Devices == null)
                {
                    continue;
                }

                foreach (var card in chassis.Devices)
                {
                    if (!ShouldIncludeCard(card))
                    {
                        continue;
                    }

                    AvailableCards.Add(card);
                    _cardChassisMap[card] = chassis.Name;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] LoadCardsForChannelType: 加载了 {AvailableCards.Count} 个板卡");
            RaisePropertyChanged(nameof(IsCardVisible));
        }

        private bool ShouldIncludeCard(DeviceBase card)
        {
            if (card == null || card.DeviceType != "Card")
            {
                return false;
            }

            // 如果外部指定了机箱，仅加载该机箱的卡
            if (!string.IsNullOrWhiteSpace(_selectedCardChassisName))
            {
                var chassisName = GetChassisNameForCard(card);
                if (!string.Equals(chassisName, _selectedCardChassisName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(SelectedChannelType))
            {
                return true;
            }

            switch (SelectedChannelType)
            {
                case "离散量通道":
                    // 矩阵开关不属于离散量通道，只包含数字IO设备
                    return card is DigitalIODevice;
                case "模拟量通道":
                    return card is AnalogAcquisitionDevice ||
                           card is AnalogOutputDevice ||
                           card is ProgrammableResistorDevice;
                case "通讯通道":
                    return card is CanBusDevice ||
                           card is Arinc429Device ||
                           card is Mil1553BDevice ||
                           card is Mil1394BDevice;
                case "其他通道":
                    return card is ProgrammableResistorDevice ||
                           card is LvdtSimulatorDevice ||
                           card is ResolverSimulatorDevice ||
                           card is LvdsDevice ||
                           card is SwitchDevice; // 矩阵开关属于其他通道
                default:
                    return true;
            }
        }

        private DeviceBase FindCardByNames(string chassisName, string cardName)
        {
            if (string.IsNullOrEmpty(cardName))
            {
                return null;
            }

            // 优先使用构造函数传入的机箱名称
            string chassisNameToUse = !string.IsNullOrEmpty(_chassisName) ? _chassisName : chassisName;
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] FindCardByNames: 查找板卡 {cardName}，机箱名称={chassisNameToUse}，AvailableCards数量={AvailableCards?.Count ?? 0}");

            return AvailableCards.FirstOrDefault(card =>
            {
                var currentChassis = GetChassisNameForCard(card);
                if (!string.IsNullOrEmpty(chassisNameToUse) &&
                    !string.Equals(currentChassis, chassisNameToUse, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var displayName = !string.IsNullOrEmpty(card.CardName) ? card.CardName : card.Model;
                bool nameMatches = string.Equals(displayName, cardName, StringComparison.OrdinalIgnoreCase);
                if (nameMatches)
                {
                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] FindCardByNames: 找到匹配的板卡 {displayName}，机箱={currentChassis}");
                }
                return nameMatches;
            });
        }

        private string GetChassisNameForCard(DeviceBase card)
        {
            if (card == null)
            {
                return null;
            }

            if (_cardChassisMap.TryGetValue(card, out var chassisName))
            {
                return chassisName;
            }

            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList != null)
            {
                foreach (var chassis in chassisList)
                {
                    if (chassis?.Devices == null)
                    {
                        continue;
                    }

                    if (chassis.Devices.Any(d => d.Id == card.Id))
                    {
                        _cardChassisMap[card] = chassis.Name;
                        return chassis.Name;
                    }
                }
            }

            return null;
        }

        private void OnCardChanged()
        {
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] OnCardChanged: ChassisName={_chassisName}, TestTaskName={_testTaskName}, SelectedCard={SelectedCard?.CardName}");
            SelectedInputOutputType = null;
            SelectedAssociatedChannel = null;
            _selectedCardChassisName = GetChassisNameForCard(SelectedCard);
            // 只有在通道名称是自动填充的情况下才清空
            if (!_isUserEditedChannelName && ChannelName == _autoFilledChannelName)
            {
                _isUpdatingChannelName = true;
                try
                {
                    ChannelName = null;
                    _autoFilledChannelName = null;
                }
                finally
                {
                    _isUpdatingChannelName = false;
                }
            }

            AvailableInputOutputTypes.Clear();
            AvailableChannels.Clear();

            if (SelectedCard != null)
            {
                if (SelectedChannelType == "离散量通道" || SelectedChannelType == "模拟量通道")
                {
                    LoadInputOutputTypesForCard();
                }
                else if (SelectedChannelType == "通讯通道")
                {
                    // 如果是1394B板卡，加载节点配置
                    if (SelectedCard is Mil1394BDevice)
                    {
                        Load1394BNodes();
                    }
                    else
                    {
                        var communicationType = GetCommunicationTypeName(SelectedCard);
                        if (!string.IsNullOrEmpty(communicationType) && !AvailableInputOutputTypes.Contains(communicationType))
                        {
                            AvailableInputOutputTypes.Add(communicationType);
                        }

                        if (AvailableInputOutputTypes.Count == 1)
                        {
                            SelectedInputOutputType = AvailableInputOutputTypes[0];
                        }
                    }
                }
            }

            RaisePropertyChanged(nameof(IsInputOutputTypeVisible));
            RaisePropertyChanged(nameof(IsAssociatedChannelVisible));
            RaisePropertyChanged(nameof(IsRemarksVisible));
            ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
        }

        private string GetCommunicationTypeName(DeviceBase card)
        {
            if (card == null)
                return null;

            if (card is CanBusDevice)
                return "CAN";
            if (card is Arinc429Device)
                return "ARINC429";
            if (card is Mil1553BDevice)
                return "MIL-1553B";
            if (card is Mil1394BDevice)
                return "MIL-1394B";

            return null;
        }

        /// <summary>
        /// 加载1394B板卡的节点配置（节点0-3）
        /// </summary>
        private void Load1394BNodes()
        {
            AvailableInputOutputTypes.Clear();
            
            if (SelectedCard is Mil1394BDevice card)
            {
                var cardConfig = card.CardConfigData as Models.Mil1394BCardConfig;
                if (cardConfig == null)
                {
                    System.Diagnostics.Debug.WriteLine("[AddChannelDialog] Load1394BNodes: CardConfigData为null或不是Mil1394BCardConfig类型");
                    return;
                }

                // 获取测试任务配置
                Models.Mil1394BTestTaskConfig taskConfig = null;
                if (!string.IsNullOrEmpty(_testTaskName) && cardConfig.TestTaskConfigs != null)
                {
                    taskConfig = cardConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == _testTaskName);
                }

                // 获取节点配置列表（优先从测试任务配置中获取）
                var nodeConfigs = taskConfig?.NodeConfigs ?? cardConfig.NodeConfigs;
                
                if (nodeConfigs == null || nodeConfigs.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[AddChannelDialog] Load1394BNodes: 没有找到节点配置");
                    // 即使没有配置，也显示节点0-3供选择
                    for (uint i = 0; i < 4; i++)
                    {
                        AvailableInputOutputTypes.Add($"节点{i}");
                    }
                    return;
                }

                // 显示所有已配置的节点（节点0-3）
                for (uint i = 0; i < 4; i++)
                {
                    var nodeConfig = nodeConfigs.FirstOrDefault(n => n.NodeNumber == i);
                    if (nodeConfig != null)
                    {
                        AvailableInputOutputTypes.Add($"节点{i}");
                    }
                }

                // 如果没有任何节点配置，至少显示节点0-3
                if (AvailableInputOutputTypes.Count == 0)
                {
                    for (uint i = 0; i < 4; i++)
                    {
                        AvailableInputOutputTypes.Add($"节点{i}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] Load1394BNodes: 加载了 {AvailableInputOutputTypes.Count} 个节点");
            }
        }

        /// <summary>
        /// 加载1394B指定节点下的通道（从AsyncSendConfig获取）
        /// </summary>
        private void Load1394BNodeChannels(string nodeText)
        {
            AvailableChannels.Clear();

            if (SelectedCard is Mil1394BDevice card && !string.IsNullOrEmpty(nodeText))
            {
                // 解析节点号（从"节点0"中提取0）
                if (!uint.TryParse(nodeText.Replace("节点", ""), out uint nodeNumber))
                {
                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] Load1394BNodeChannels: 无法解析节点号: {nodeText}");
                    return;
                }

                var cardConfig = card.CardConfigData as Models.Mil1394BCardConfig;
                if (cardConfig == null)
                {
                    System.Diagnostics.Debug.WriteLine("[AddChannelDialog] Load1394BNodeChannels: CardConfigData为null或不是Mil1394BCardConfig类型");
                    return;
                }

                // 获取测试任务配置
                Models.Mil1394BTestTaskConfig taskConfig = null;
                if (!string.IsNullOrEmpty(_testTaskName) && cardConfig.TestTaskConfigs != null)
                {
                    taskConfig = cardConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == _testTaskName);
                }

                // 获取节点配置（优先从测试任务配置中获取）
                var nodeConfigs = taskConfig?.NodeConfigs ?? cardConfig.NodeConfigs;
                var nodeConfig = nodeConfigs?.FirstOrDefault(n => n.NodeNumber == nodeNumber);

                if (nodeConfig == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] Load1394BNodeChannels: 节点{nodeNumber}配置不存在");
                    return;
                }

                // 从AsyncSendConfig中获取通道，使用通道号（0-63）
                if (nodeConfig.AsyncSendConfig != null && nodeConfig.AsyncSendConfig.Count > 0)
                {
                    // 按通道号排序并去重
                    var channels = nodeConfig.AsyncSendConfig
                        .Where(item => item.Channel >= 0 && item.Channel <= 63)
                        .Select(item => item.Channel)
                        .Distinct()
                        .OrderBy(c => c)
                        .ToList();

                    foreach (var channelNum in channels)
                    {
                        // 通道名称格式：节点X-通道Y（Y为通道号0-63）
                        string channelName = $"节点{nodeNumber}-通道{channelNum}";
                        AvailableChannels.Add(channelName);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] Load1394BNodeChannels: 节点{nodeNumber}没有配置AsyncSendConfig");
                }

                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] Load1394BNodeChannels: 节点{nodeNumber}加载了 {AvailableChannels.Count} 个通道");
            }
        }

        private void LoadInputOutputTypesForCard()
        {
            AvailableInputOutputTypes.Clear();
            if (SelectedCard == null)
                return;

            var addedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAddType(string type)
            {
                if (!string.IsNullOrWhiteSpace(type) && addedTypes.Add(type))
                {
                    AvailableInputOutputTypes.Add(type);
                }
            }

            if (SelectedCard is DigitalIODevice digitalCard)
            {
                if (digitalCard.DiNode != null)
                {
                    TryAddType("DI");
                }

                if (digitalCard.DoNode != null)
                {
                    TryAddType("DO");
                }
            }

            if (SelectedCard is ProgrammableResistorDevice)
            {
                // 电阻输出设备支持RO类型
                TryAddType("RO");
            }

            if (SelectedCard.Children != null)
            {
                foreach (var child in SelectedCard.Children)
                {
                    if (child is AnalogInputNode)
                    {
                        TryAddType("AI");
                    }
                    else if (child is AnalogOutputNode)
                    {
                        TryAddType("AO");
                    }
                    else if (child is DigitalInputNode)
                    {
                        TryAddType("DI");
                    }
                    else if (child is DigitalOutputNode)
                    {
                        TryAddType("DO");
                    }
                }
            }
        }

        private void OnInputOutputTypeChanged()
        {
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] OnInputOutputTypeChanged: ChassisName={_chassisName}, TestTaskName={_testTaskName}, SelectedCard={SelectedCard?.CardName}, SelectedInputOutputType={SelectedInputOutputType}");
            SelectedAssociatedChannel = null;
            // 只有在通道名称是自动填充的情况下才清空
            if (!_isUserEditedChannelName && ChannelName == _autoFilledChannelName)
            {
                _isUpdatingChannelName = true;
                try
                {
                    ChannelName = null;
                    _autoFilledChannelName = null;
                }
                finally
                {
                    _isUpdatingChannelName = false;
                }
            }

            AvailableChannels.Clear();

            if (!string.IsNullOrEmpty(SelectedInputOutputType))
            {
                LoadChannelsForInputOutputType();
            }

            RaisePropertyChanged(nameof(IsAssociatedChannelVisible));
            RaisePropertyChanged(nameof(IsRemarksVisible));
            ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
        }

        private void LoadChannelsForInputOutputType()
        {
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] LoadChannelsForInputOutputType 开始: ChassisName={_chassisName}, TestTaskName={_testTaskName}, SelectedCard={SelectedCard?.CardName}, SelectedInputOutputType={SelectedInputOutputType}");
            AvailableChannels.Clear();
            if (SelectedChannelType == "通讯通道")
            {
                // 如果是1394B板卡，加载节点下的通道
                if (SelectedCard is Mil1394BDevice)
                {
                    Load1394BNodeChannels(SelectedInputOutputType);
                }
                else
                {
                    foreach (var channel in GetCommunicationChannels(SelectedCard, SelectedInputOutputType))
                    {
                        // 过滤掉空字符串和空白字符串
                        if (!string.IsNullOrWhiteSpace(channel))
                        {
                            AvailableChannels.Add(channel);
                        }
                    }
                }
                return;
            }

            DeviceBase targetNode = null;
            if (SelectedCard is DigitalIODevice digitalCard)
            {
                if (SelectedInputOutputType == "DI")
                {
                    targetNode = digitalCard.DiNode;
                }
                else if (SelectedInputOutputType == "DO")
                {
                    targetNode = digitalCard.DoNode;
                }
            }

            if (SelectedCard is ProgrammableResistorDevice resistorDevice && SelectedInputOutputType == "RO")
            {
                // 对于电阻输出设备，直接从设备获取通道信息
                // 电阻输出设备通常有固定数量的通道，如9个
                int channelCount = 9; // 或者从设备属性获取
                for (int i = 0; i < channelCount; i++)
                {
                    string channelName = $"RO{i}";
                    bool isEnabled = IsChannelEnabled(SelectedCard, channelName);
                    if (isEnabled)
                    {
                        AvailableChannels.Add(channelName);
                    }
                }
                return;
            }

            if (targetNode == null && SelectedCard?.Children != null)
            {
                foreach (var child in SelectedCard.Children)
                {
                    if ((SelectedInputOutputType == "AI" && child is AnalogInputNode) ||
                        (SelectedInputOutputType == "AO" && child is AnalogOutputNode) ||
                        (SelectedInputOutputType == "DI" && child is DigitalInputNode) ||
                        (SelectedInputOutputType == "DO" && child is DigitalOutputNode))
                    {
                        targetNode = child;
                        break;
                    }
                }
            }

            if (targetNode != null && !string.IsNullOrEmpty(targetNode.SlotPosition))
            {
                var channels = ParseChannelRange(targetNode.SlotPosition);
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] 解析到 {channels.Count} 个通道: {string.Join(", ", channels)}");
                foreach (var channel in channels)
                {
                    // 过滤掉空字符串和空白字符串
                    if (string.IsNullOrWhiteSpace(channel))
                        continue;

                    // 只显示在对应板卡配置中已使能的通道
                    bool isEnabled = IsChannelEnabled(SelectedCard, channel);
                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] 通道 {channel} 使能状态: {isEnabled} (TestTaskName={_testTaskName})");
                    if (isEnabled)
                    {
                        AvailableChannels.Add(channel);
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] 最终可用通道列表: {string.Join(", ", AvailableChannels)}");
            }
        }

        private List<string> ParseChannelRange(string slotPosition)
        {
            var channels = new List<string>();
            if (string.IsNullOrEmpty(slotPosition))
                return channels;

            // 解析格式如 "AI0–AI15", "DI0–DI7", "AO0–AO3"
            if (slotPosition.Contains("–") || slotPosition.Contains("-"))
            {
                string separator = slotPosition.Contains("–") ? "–" : "-";
                var parts = slotPosition.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    string prefix = new string(parts[0].TakeWhile(c => !char.IsDigit(c)).ToArray());
                    if (int.TryParse(new string(parts[0].SkipWhile(c => !char.IsDigit(c)).ToArray()), out int start) &&
                        int.TryParse(new string(parts[1].SkipWhile(c => !char.IsDigit(c)).ToArray()), out int end))
                    {
                        for (int i = start; i <= end; i++)
                        {
                            channels.Add($"{prefix}{i}");
                        }
                    }
                }
            }
            else
            {
                // 单个通道
                channels.Add(slotPosition);
            }

            // 过滤掉空字符串和空白字符串
            return channels.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        }

        private IEnumerable<string> GetCommunicationChannels(DeviceBase card, string communicationType)
        {
            if (card == null || string.IsNullOrEmpty(communicationType))
                return Enumerable.Empty<string>();

            if (communicationType == "CAN")
            {
                return GetEnabledCanChannels(card);
            }

            var firstNode = card.Children?.FirstOrDefault();
            if (firstNode != null)
            {
                return ParseChannelRange(firstNode.SlotPosition);
            }

            return Enumerable.Empty<string>();
        }

        private IEnumerable<string> GetEnabledCanChannels(DeviceBase card)
        {
            if (card == null)
                return Enumerable.Empty<string>();

            if (card.CardConfigData is CanCardConfig canConfig)
            {
                IEnumerable<CanChannelConfig> configs = null;
                if (!string.IsNullOrEmpty(_testTaskName))
                {
                    configs = canConfig.TestTaskConfigs?.FirstOrDefault(t => t.TestTaskName == _testTaskName)?.Channels;
                }

                configs ??= canConfig.Channels;
                if (configs != null)
                {
                    return configs.Where(c => c.IsEnabled).Select(c => c.ChannelName).ToList();
                }
            }

            var canNode = card.Children?.OfType<CanBusNode>().FirstOrDefault();
            if (canNode != null)
            {
                var parsed = ParseChannelRange(canNode.SlotPosition);
                return parsed.Where(name => IsChannelEnabled(card, name)).ToList();
            }

            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// 检查指定板卡上的通道是否已在配置中使能
        /// 优先从 CardConfigData 检查，对于有测试任务隔离的板卡，优先从测试任务配置中查找
        /// </summary>
        /// <param name="card">板卡设备</param>
        /// <param name="channelName">通道名称（如 DI0、DO1、AI0、AO3）</param>
        /// <returns>通道是否已使能</returns>
        private bool IsChannelEnabled(DeviceBase card, string channelName)
        {
            if (card == null)
            {
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: card为null");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 检查通道 {channelName} (板卡={card.CardName}, TestTaskName={_testTaskName})");

            if (card.CardConfigData != null)
            {
                // 模拟量输入板卡（没有测试任务隔离，使用全局配置）
                if (card.CardConfigData is AnalogInputCardConfig aiConfig)
                {
                    if (!string.IsNullOrEmpty(_testTaskName) && aiConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = aiConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == _testTaskName);
                        if (taskConfig?.Channels != null)
                        {
                            var taskChannel = taskConfig.Channels.FirstOrDefault(c => c.ChannelName == channelName);
                            if (taskChannel != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 模拟量输入 {channelName} = {taskChannel.IsEnabled} (测试任务 '{_testTaskName}' 配置)");
                                return taskChannel.IsEnabled;
                            }
                        }
                    }

                    var ch = aiConfig.Channels?.FirstOrDefault(c => c.ChannelName == channelName);
                    if (ch != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 模拟量输入 {channelName} = {ch.IsEnabled} (全局配置)");
                        return ch.IsEnabled;
                    }
                }
                // 模拟量输出板卡（有测试任务隔离）
                else if (card.CardConfigData is AnalogOutputCardConfig aoConfig)
                {
                    // 如果指定了测试任务，优先从测试任务配置中查找
                    if (!string.IsNullOrEmpty(_testTaskName) && aoConfig.TestTaskConfigs != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 查找测试任务 '{_testTaskName}' 的配置，共有 {aoConfig.TestTaskConfigs.Count} 个测试任务配置");
                        var taskConfig = aoConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == _testTaskName);
                        if (taskConfig != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 找到测试任务配置，通道数={taskConfig.Channels?.Count ?? 0}");
                            if (taskConfig.Channels != null)
                            {
                                var channelConfig = taskConfig.Channels.FirstOrDefault(c => c.ChannelName == channelName);
                                if (channelConfig != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 模拟量输出 {channelName} = {channelConfig.IsEnabled} (测试任务 '{_testTaskName}' 配置)");
                                    return channelConfig.IsEnabled;
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 在测试任务 '{_testTaskName}' 配置中未找到通道 {channelName}");
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 未找到测试任务 '{_testTaskName}' 的配置");
                        }
                    }
                    
                    // 回退到全局配置
                    var globalChannelConfig = aoConfig.Channels?.FirstOrDefault(c => c.ChannelName == channelName);
                    if (globalChannelConfig != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 模拟量输出 {channelName} = {globalChannelConfig.IsEnabled} (全局配置)");
                        return globalChannelConfig.IsEnabled;
                    }
                }
                // 离散量板卡（有测试任务隔离）
                else if (card.CardConfigData is DigitalIOCardConfig dioConfig)
                {
                    // 如果指定了测试任务，优先从测试任务配置中查找
                    if (!string.IsNullOrEmpty(_testTaskName) && dioConfig.TestTaskConfigs != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 查找测试任务 '{_testTaskName}' 的配置，共有 {dioConfig.TestTaskConfigs.Count} 个测试任务配置");
                        var taskConfig = dioConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == _testTaskName);
                        if (taskConfig != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 找到测试任务配置，输入通道数={taskConfig.InputChannels?.Count ?? 0}，输出通道数={taskConfig.OutputChannels?.Count ?? 0}");
                            
                            // 检查输入通道
                            var inputChannel = taskConfig.InputChannels?.FirstOrDefault(c => c.ChannelName == channelName);
                            if (inputChannel != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 离散量输入 {channelName} = {inputChannel.IsEnabled} (测试任务 '{_testTaskName}' 配置)");
                                return inputChannel.IsEnabled;
                            }
                            
                            // 检查输出通道
                            var outputChannel = taskConfig.OutputChannels?.FirstOrDefault(c => c.ChannelName == channelName);
                            if (outputChannel != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 离散量输出 {channelName} = {outputChannel.IsEnabled} (测试任务 '{_testTaskName}' 配置)");
                                return outputChannel.IsEnabled;
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 在测试任务 '{_testTaskName}' 配置中未找到通道 {channelName}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 未找到测试任务 '{_testTaskName}' 的配置");
                        }
                    }
                    
                    // 回退到全局配置
                    var globalInputChannel = dioConfig.InputChannels?.FirstOrDefault(c => c.ChannelName == channelName);
                    if (globalInputChannel != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 离散量输入 {channelName} = {globalInputChannel.IsEnabled} (全局配置)");
                        return globalInputChannel.IsEnabled;
                    }

                    var globalOutputChannel = dioConfig.OutputChannels?.FirstOrDefault(c => c.ChannelName == channelName);
                    if (globalOutputChannel != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 离散量输出 {channelName} = {globalOutputChannel.IsEnabled} (全局配置)");
                        return globalOutputChannel.IsEnabled;
                    }
                }
                else if (card.CardConfigData is ResistanceOutputCardConfig resistanceConfig)
                {
                    // 检查电阻输出配置
                    if (!string.IsNullOrEmpty(_testTaskName) && resistanceConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = resistanceConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == _testTaskName);
                        var taskChannel = taskConfig?.Channels?.FirstOrDefault(c => c.ChannelName == channelName);
                        if (taskChannel != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 电阻输出 {card.CardName} 通道 {channelName} 使用测试任务 '{_testTaskName}' 配置: IsEnabled={taskChannel.IsEnabled}");
                            return taskChannel.IsEnabled;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 电阻输出 {card.CardName} 通道 {channelName} 未找到测试任务配置，默认禁用");
                    return false;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 板卡 {card.CardName} 的 CardConfigData 为 null");
            }

            // 无配置信息时，默认视为未使能，避免误用
            System.Diagnostics.Debug.WriteLine($"[AddChannelDialog] IsChannelEnabled: 通道 {channelName} 未找到配置，返回 false");
            return false;
        }

        private void OnAssociatedChannelChanged()
        {
            if (!string.IsNullOrEmpty(SelectedAssociatedChannel))
            {
                // 只有在通道名称为空，或者是自动填充的情况下，才自动填充
                // 如果用户已经手动编辑过，则保留用户输入的内容
                if (string.IsNullOrEmpty(ChannelName) || 
                    (!_isUserEditedChannelName && ChannelName == _autoFilledChannelName))
                {
                    _isUpdatingChannelName = true;
                    try
                    {
                        ChannelName = SelectedAssociatedChannel;
                        _autoFilledChannelName = SelectedAssociatedChannel;
                        _isUserEditedChannelName = false; // 自动填充的，不是用户编辑的
                    }
                    finally
                    {
                        _isUpdatingChannelName = false;
                    }
                }
            }
            else
            {
                // 如果清空关联通道，且通道名称是自动填充的，则清空通道名称
                if (!_isUserEditedChannelName && ChannelName == _autoFilledChannelName)
                {
                    _isUpdatingChannelName = true;
                    try
                    {
                        ChannelName = null;
                        _autoFilledChannelName = null;
                    }
                    finally
                    {
                        _isUpdatingChannelName = false;
                    }
                }
            }

            RaisePropertyChanged(nameof(IsRemarksVisible));
            ((DelegateCommand)OkCommand).RaiseCanExecuteChanged();
        }

        private bool CanOk()
        {
            // 验证必填字段
            if (string.IsNullOrEmpty(SelectedChannelType))
                return false;

            if (string.IsNullOrEmpty(ChannelName))
                return false;

            if (SelectedCard == null)
                return false;

            if (string.IsNullOrEmpty(_selectedCardChassisName))
                return false;

            // 离散量和模拟量通道需要选择输入输出类型和关联通道
            if (SelectedChannelType == "离散量通道" || SelectedChannelType == "模拟量通道")
            {
                if (string.IsNullOrEmpty(SelectedInputOutputType))
                    return false;

                if (string.IsNullOrEmpty(SelectedAssociatedChannel))
                    return false;
            }
            else if (SelectedChannelType == "通讯通道")
            {
                if (string.IsNullOrEmpty(SelectedInputOutputType))
                    return false;

                if (string.IsNullOrEmpty(SelectedAssociatedChannel))
                    return false;
            }

            return true;
        }

        private void OnOk()
        {
            if (!ValidateInput())
                return;

            // 创建结果
            Result = new ChannelTabelItem
            {
                ChannelType = SelectedChannelType,
                ChannelName = ChannelName,
                ChassisName = _selectedCardChassisName,
                CardName = !string.IsNullOrEmpty(SelectedCard.CardName) ? SelectedCard.CardName : SelectedCard.Model,
                InputOutputType = SelectedInputOutputType,
                AssociatedChannel = SelectedAssociatedChannel,
                Remarks = Remarks ?? string.Empty
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
            // 验证通道名称不能为空
            if (string.IsNullOrWhiteSpace(ChannelName))
            {
                return false;
            }

            return true;
        }

        #endregion
    }
}

