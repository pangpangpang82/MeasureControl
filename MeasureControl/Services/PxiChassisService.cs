using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Events;

namespace MeasureControl.Services
{
    public class PxiChassisService : IPxiChassisService
    {
        private readonly ObservableCollection<ChassisModel> _chassisList;
        private readonly HashSet<string> _chassisNames;
        private readonly IEventAggregator _eventAggregator;
        private readonly ChannelManager _channelManager;
        private bool _isLoadingData; // 标记是否正在加载数据，避免加载时触发修改事件
        private bool _isUIInteraction; // 标记是否为UI交互操作（如点击板卡查看详情），避免触发修改事件

        public PxiChassisService(IEventAggregator eventAggregator, ChannelManager channelManager)
        {
            _chassisList = new ObservableCollection<ChassisModel>();
            _chassisNames = new HashSet<string>();
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
            _isLoadingData = false;
            _isUIInteraction = false;
        }
        
        public ObservableCollection<ChassisModel> GetAllChassis()
        {
            // 调试：输出 _chassisList 中所有设备的 HashCode
            foreach (var chassis in _chassisList)
            {
                if (chassis.Devices != null)
                {
                    foreach (var device in chassis.Devices)
                    {
                        if (device.DeviceType == "Card" && device.CardName == "离散量输入输出1")
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetAllChassis] 设备 {device.CardName}: ID={device.Id}, HashCode={device.GetHashCode()}");
                        }
                    }
                }
            }
            return _chassisList;
        }

        public bool AddChassis(ChassisModel chassis)
        {
            if (chassis == null || IsPositionOccupied(chassis.GridRow, chassis.GridColumn))
                return false;

            _chassisList.Add(chassis);
            // 确保名称已占用（如果之前已经通过ReserveChassisName占用，这里不会重复添加）
            if (!_chassisNames.Contains(chassis.Name))
            {
                _chassisNames.Add(chassis.Name);
            }
            
            // 订阅机箱属性更改事件
            SubscribeToChassisPropertyChanged(chassis);
            
            // 订阅机箱设备集合更改事件
            SubscribeToDevicesCollectionChanged(chassis);
            
            return true;
        }

        /// <summary>
        /// 确保机箱对应的 ChassisDevice 已创建并注册
        /// </summary>
        public ChassisDevice EnsureChassisDevice(string chassisName, string chassisModel)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
                return null;

            var chassis = GetChassisByName(chassisName);
            if (chassis == null)
                return null;

            if (chassis.Devices == null)
            {
                chassis.Devices = new ObservableCollection<DeviceBase>();
            }

            var existingChassisDevice = chassis.Devices.OfType<ChassisDevice>().FirstOrDefault();
            if (existingChassisDevice != null)
            {
                return existingChassisDevice;
            }

            var effectiveModel = !string.IsNullOrWhiteSpace(chassisModel) ? chassisModel : chassis.Model;
            var createdDevice = DeviceFactory.CreateDevice(effectiveModel ?? chassis.Name);

            ChassisDevice chassisDevice;
            if (createdDevice is ChassisDevice typedChassisDevice)
            {
                chassisDevice = typedChassisDevice;
            }
            else
            {
                chassisDevice = new ChassisDevice(effectiveModel ?? chassis.Name);
            }

            // 设置机箱设备的CardName为机箱名称
            chassisDevice.CardName = chassis.Name;

            // 同步机箱基础信息
            if (chassis.SlotCount > 0 && chassisDevice.SlotCount != chassis.SlotCount)
            {
                chassisDevice.SlotCount = chassis.SlotCount;
            }

            var parentNode = $"{chassisDevice.SlotCount}槽机箱";
            chassisDevice.ParentNode = parentNode;
            chassisDevice.ConnectionMethod = "详细信息";
            chassisDevice.Details = "详细信息";
            chassisDevice.DeviceType = AppConstants.DeviceTypeChassis;
            chassisDevice.Status = "正常";
            chassisDevice.IsExpanded = true;
            chassisDevice.Model = effectiveModel ?? chassisDevice.Model;
            chassisDevice.ChassisModel = effectiveModel ?? chassisDevice.ChassisModel;

            chassisDevice.Children ??= new ObservableCollection<DeviceBase>();

            AddDeviceToChassis(chassisName, chassisDevice);
            return chassisDevice;
        }

        public bool RemoveChassis(string chassisIdOrName)
        {
            // 首先尝试按ID查找
            var chassis = GetChassisById(chassisIdOrName);
            
            // 如果按ID找不到，尝试按名称查找
            if (chassis == null)
            {
                chassis = _chassisList.FirstOrDefault(c => c.Name == chassisIdOrName);
            }
            
            if (chassis == null) return false;

            // 取消订阅事件
            UnsubscribeFromChassisPropertyChanged(chassis);
            UnsubscribeFromDevicesCollectionChanged(chassis);
            
            _chassisNames.Remove(chassis.Name);
            _chassisList.Remove(chassis);
            
            // 删除后自动重新排列机箱
            RearrangeChassis();
            return true;
        }

        public bool UpdateChassisName(string chassisId, string newName)
        {
            var chassis = GetChassisById(chassisId);
            if (chassis == null || string.IsNullOrEmpty(newName) || _chassisNames.Contains(newName))
                return false;

            _chassisNames.Remove(chassis.Name);
            chassis.Name = newName;
            _chassisNames.Add(newName);
            return true;
        }

        public ChassisModel GetChassisById(string chassisId)
        {
            return _chassisList.FirstOrDefault(c => c.Id == chassisId);
        }

        public ChassisModel GetChassisByPosition(int row, int column)
        {
            return _chassisList.FirstOrDefault(c => c.GridRow == row && c.GridColumn == column);
        }

        /// <summary>
        /// 通过设备ID查找设备
        /// 优先在 chassis.Devices 中查找，确保返回的是权威数据源中的设备引用
        /// </summary>
        public DeviceBase GetDeviceById(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return null;
            
            // 第一遍：优先在 chassis.Devices 中直接查找
            foreach (var chassis in _chassisList)
            {
                if (chassis.Devices == null) continue;
                
                foreach (var device in chassis.Devices)
                {
                    if (device.Id == deviceId)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetDeviceById] 在 chassis.Devices 中找到设备: ID={deviceId}, HashCode={device.GetHashCode()}, CardName={device.CardName}");
                        return device;
                    }
                }
            }
            
            // 第二遍：如果在顶层没找到，再在子设备中查找
            foreach (var chassis in _chassisList)
            {
                if (chassis.Devices == null) continue;
                
                foreach (var device in chassis.Devices)
                {
                    // 检查子设备
                    if (device.Children != null)
                    {
                        foreach (var child in device.Children)
                        {
                            if (child.Id == deviceId)
                            {
                                System.Diagnostics.Debug.WriteLine($"[GetDeviceById] 在 device.Children 中找到设备: ID={deviceId}, HashCode={child.GetHashCode()}, CardName={child.CardName}, ParentDevice={device.Name}");
                                return child;
                            }
                        }
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[GetDeviceById] 未找到设备: ID={deviceId}");
            return null;
        }

        /// <summary>
        /// 更新设备的CardConfigData（同步到服务中的设备实例）
        /// </summary>
        public bool UpdateDeviceCardConfig(string deviceId, Models.CardConfigDataBase cardConfig)
        {
            System.Diagnostics.Debug.WriteLine($"[PxiChassisService] UpdateDeviceCardConfig: 查找设备 ID={deviceId}");
            var device = GetDeviceById(deviceId);
            if (device == null)
            {
                System.Diagnostics.Debug.WriteLine($"[PxiChassisService] UpdateDeviceCardConfig: 未找到设备 {deviceId}");
                return false;
            }
            
            System.Diagnostics.Debug.WriteLine($"[PxiChassisService] UpdateDeviceCardConfig: 找到设备 HashCode={device.GetHashCode()}, CardName={device.CardName}");
            device.CardConfigData = cardConfig;
            
            // 验证更新是否成功
            if (device.CardConfigData is Models.DigitalIOCardConfig dioConfig)
            {
                var enabledDI = dioConfig.InputChannels?.Count(c => c.IsEnabled) ?? 0;
                var enabledDO = dioConfig.OutputChannels?.Count(c => c.IsEnabled) ?? 0;
                System.Diagnostics.Debug.WriteLine($"[PxiChassisService] UpdateDeviceCardConfig: 已更新设备 {device.CardName ?? device.Name}, DI使能={enabledDI}, DO使能={enabledDO}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[PxiChassisService] UpdateDeviceCardConfig: 已更新设备 {device.CardName ?? device.Name} 的 CardConfigData");
            }
            return true;
        }

        public bool IsPositionOccupied(int row, int column)
        {
            return _chassisList.Any(c => c.GridRow == row && c.GridColumn == column);
        }

        public string GenerateUniqueName()
        {
            // 找到最小的可用编号，确保编号连续
            int candidateNumber = 1;

            while (candidateNumber <= int.MaxValue)
            {
                string candidateName = $"{AppConstants.DefaultChassisNamePrefix}{candidateNumber}";

                if (!_chassisNames.Contains(candidateName))
                {
                    _chassisNames.Add(candidateName);
                    return candidateName;
                }

                candidateNumber++;
            }

            return $"{AppConstants.DefaultChassisNamePrefix}{DateTime.Now.Ticks}";
        }

        public void LoadChassisData(ObservableCollection<ChassisModel> chassisData)
        {
            // 设置加载标志，避免加载时触发修改事件
            _isLoadingData = true;
            
            try
            {
                // 清空全局通道管理器，防止项目切换后残留通道
                _channelManager.Clear();

                // 取消订阅所有现有机箱的事件
                foreach (var chassis in _chassisList)
                {
                    UnsubscribeFromChassisPropertyChanged(chassis);
                    UnsubscribeFromDevicesCollectionChanged(chassis);
                }
                
                _chassisList.Clear();
                _chassisNames.Clear();
                
                if (chassisData != null)
                {
                    foreach (var chassis in chassisData)
                    {
                        FixDeviceTypes(chassis.Devices);
                        
                        // 确保从JSON加载后，设备的子节点属性（如AnalogInputNode）被正确设置
                        foreach (var device in chassis.Devices)
                        {
                            ReassignChildNodeProperties(device);
                            
                            // 调试：检查 CardConfigData 是否被正确加载
                            if (device.CardConfigData is Models.DigitalIOCardConfig digitalConfig)
                            {
                                var enabledDI = digitalConfig.InputChannels?.Count(c => c.IsEnabled) ?? 0;
                                var enabledDO = digitalConfig.OutputChannels?.Count(c => c.IsEnabled) ?? 0;
                                System.Diagnostics.Debug.WriteLine($"[PxiChassisService] 加载板卡 {device.CardName}: DI使能={enabledDI}, DO使能={enabledDO}");
                            }
                            else if (device.CardConfigData != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[PxiChassisService] 加载板卡 {device.CardName}: CardConfigData={device.CardConfigData.GetType().Name}");
                            }
                            else if (device.DeviceType == "Card")
                            {
                                System.Diagnostics.Debug.WriteLine($"[PxiChassisService] 加载板卡 {device.CardName}: CardConfigData=null");
                            }
                            
                            EnsureDefaultCardConfig(device, chassis.Name);
                        }
                        
                        // 为没有CardName的板卡分配默认名称
                        AssignDefaultCardNames(chassis);
                        
                        // 确保它们引用同一个对象实例，避免更新时出现实例不一致的问题
                        SyncChassisDeviceChildren(chassis);
                        
                        _chassisList.Add(chassis);
                        _chassisNames.Add(chassis.Name);
                        
                        // 订阅机箱属性更改事件
                        SubscribeToChassisPropertyChanged(chassis);
                        
                        // 订阅机箱设备集合更改事件
                        SubscribeToDevicesCollectionChanged(chassis);
                    }
                }
            }
            finally
            {
                // 重置加载标志
                _isLoadingData = false;
            }
        }
        
        /// <summary>
        /// 将错误识别为GenericDevice的设备转换为正确的类型
        /// </summary>
        private void FixDeviceTypes(ObservableCollection<DeviceBase> devices)
        {
            if (devices == null) return;
            
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                
                // 只处理GenericDevice类型的设备
                if (device.GetType().Name != "GenericDevice") 
                {
                    // 递归处理子设备
                    if (device.Children != null && device.Children.Count > 0)
                    {
                        FixDeviceTypes(device.Children);
                    }
                    continue;
                }
                
                // 根据设备名称重新识别设备类型
                var correctDevice = DeviceFactory.CreateDevice(device.Name, device.SlotPosition);
                
                // 如果识别出了不同的设备类型，则替换
                if (correctDevice != null && correctDevice.GetType().Name != "GenericDevice")
                {
                    // 保存原设备的子节点
                    var originalChildren = device.Children?.ToList();
                    
                    // 复制原设备的属性到新设备
                    correctDevice.Id = device.Id;
                    correctDevice.Status = device.Status;
                    correctDevice.Description = device.Description;
                    correctDevice.IsSelected = device.IsSelected;
                    correctDevice.IsExpanded = device.IsExpanded;
                    correctDevice.ConnectionMethod = device.ConnectionMethod;
                    correctDevice.ParentNode = device.ParentNode;
                    correctDevice.Details = device.Details;
                    correctDevice.CardName = device.CardName;
                    
                    // 如果原设备有子节点，保留它们；否则让新设备初始化自己的子节点
                    if (originalChildren != null && originalChildren.Count > 0)
                    {
                        correctDevice.Children.Clear();
                        foreach (var child in originalChildren)
                        {
                            correctDevice.Children.Add(child);
                        }
                        
                        // 重新关联特定设备类型的子节点属性
                        ReassignChildNodeProperties(correctDevice);
                    }
                    else
                    {
                        // 如果原设备没有子节点，让新设备初始化自己的子节点
                        correctDevice.InitializeChildren();
                    }
                    
                    // 替换设备
                    devices[i] = correctDevice;
                    
                }
                
                // 递归处理子设备
                if (device.Children != null && device.Children.Count > 0)
                {
                    FixDeviceTypes(device.Children);
                }
            }
        }

        /// <summary>
        /// 重新关联特定设备类型的子节点属性
        /// 某些设备类（如AnalogAcquisitionDevice）有特定的子节点属性（如AnalogInputNode），
        /// 当从JSON加载并保留子节点时，需要重新关联这些属性
        /// </summary>
        private void ReassignChildNodeProperties(DeviceBase device)
        {
            if (device == null || device.Children == null || device.Children.Count == 0)
                return;

            try
            {
                // 处理AnalogAcquisitionDevice的AnalogInputNode属性
                if (device is AnalogAcquisitionDevice analogDevice)
                {
                    var analogInputNode = device.Children.OfType<AnalogInputNode>().FirstOrDefault();
                    if (analogInputNode != null)
                    {
                        analogDevice.AiNode = analogInputNode;
                    }
                }
                // 处理AnalogOutputDevice的AnalogOutputNode属性
                else if (device is AnalogOutputDevice analogOutputDevice)
                {
                    var analogOutputNode = device.Children.OfType<AnalogOutputNode>().FirstOrDefault();
                    if (analogOutputNode != null)
                    {
                        analogOutputDevice.AoNode = analogOutputNode;
                    }
                }
                // 处理ElectronicLoadDevice的ElectronicLoadChannelNode属性
                else if (device is ElectronicLoadDevice loadDevice)
                {
                    var channelNode = device.Children.OfType<ElectronicLoadChannelNode>().FirstOrDefault();
                    if (channelNode != null)
                    {
                        loadDevice.ElectronicLoadChannelNode = channelNode;
                    }
                }
                // 处理SwitchDevice的SwitchChannelNode属性
                else if (device is SwitchDevice switchDevice)
                {
                    var channelNode = device.Children.OfType<SwitchChannelNode>().FirstOrDefault();
                    if (channelNode != null)
                    {
                        switchDevice.SwitchChannelNode = channelNode;
                    }
                }
                
                // 递归处理子设备
                foreach (var child in device.Children)
                {
                    ReassignChildNodeProperties(child);
                }
            }
            catch (Exception)
            {
                // 忽略错误，避免影响加载流程
            }
        }

        public void SaveChassisData(ObservableCollection<ChassisModel> chassisData)
        {
            chassisData.Clear();
            foreach (var chassis in _chassisList)
            {
                SyncChassisDeviceChildren(chassis);
            }
            foreach (var chassis in _chassisList)
            {
                chassisData.Add(chassis);
            }
        }

        /// <summary>
        /// 重新排列机箱位置，从左到右、从上到下的顺序
        /// </summary>
        public void RearrangeChassis()
        {
            if (_chassisList.Count == 0) return;

            // 按照创建时间或者当前位置排序，保持相对顺序
            var sortedChassis = _chassisList
                .OrderBy(c => c.GridRow)
                .ThenBy(c => c.GridColumn)
                .ToList();

            // 重新分配位置，从(0,0)开始，从左到右排列
            int currentRow = 0;
            int currentColumn = 0;

            foreach (var chassis in sortedChassis)
            {
                chassis.GridRow = currentRow;
                chassis.GridColumn = currentColumn;

                currentColumn++;
                if (currentColumn >= AppConstants.MaxChassisPerRow)
                {
                    currentColumn = 0;
                    currentRow++;
                }
            }
        }

        /// <summary>
        /// 获取下一个可用的位置（用于新增机箱时的自动定位）
        /// </summary>
        public (int Row, int Column)? GetNextAvailablePosition()
        {
            int row = 0;
            int column = 0;

            while (IsPositionOccupied(row, column))
            {
                column++;
                if (column >= AppConstants.MaxChassisPerRow)
                {
                    column = 0;
                    row++;
                }
                
                // 防止无限循环
                if (row >= AppConstants.MaxChassisRows) return null;
            }

            return (row, column);
        }

        /// <summary>
        /// 检查机箱名称是否已存在
        /// </summary>
        public bool ChassisExists(string chassisName)
        {
            return _chassisNames.Contains(chassisName);
        }

        /// <summary>
        /// 根据名称重命名机箱
        /// </summary>
        public bool RenameChassis(string oldName, string newName)
        {
            var chassis = _chassisList.FirstOrDefault(c => c.Name == oldName);
            if (chassis == null || string.IsNullOrEmpty(newName) || _chassisNames.Contains(newName))
                return false;

            _chassisNames.Remove(chassis.Name);
            chassis.Name = newName;
            _chassisNames.Add(newName);
            return true;
        }

        /// <summary>
        /// 开始UI交互操作（抑制修改事件触发）
        /// 用于包装UI交互操作（如点击板卡查看详情、展开/收起子节点等），避免这些操作触发项目修改事件
        /// </summary>
        public void BeginUIInteraction()
        {
            _isUIInteraction = true;
        }

        /// <summary>
        /// 结束UI交互操作（恢复修改事件触发）
        /// </summary>
        public void EndUIInteraction()
        {
            _isUIInteraction = false;
        }

        /// <summary>
        /// 根据机箱名称获取机箱
        /// </summary>
        public ChassisModel GetChassisByName(string chassisName)
        {
            return _chassisList.FirstOrDefault(c => c.Name == chassisName);
        }

        /// <summary>
        /// 为指定机箱添加设备
        /// </summary>
        public void AddDeviceToChassis(string chassisName, Models.Devices.DeviceBase device)
        {
            var chassis = GetChassisByName(chassisName);
            if (chassis == null || device == null)
                return;

            // 检查设备是否已存在（按唯一ID检测，而非型号名称）
            var existingDevice = chassis.Devices.FirstOrDefault(d => d.Id == device.Id);
            if (existingDevice != null)
                return;

            // 检查机箱的板卡数量限制（支持不同槽位数的机箱）
            if (device.DeviceType == AppConstants.DeviceTypeCard)
            {
                var chassisDevice = chassis.Devices.FirstOrDefault(d => d.DeviceType == AppConstants.DeviceTypeChassis);
                if (chassisDevice is Models.Devices.ChassisDevice chassisDeviceObj)
                {
                    int currentCardCount = chassis.Devices.Count(d => d.DeviceType == AppConstants.DeviceTypeCard);
                    if (currentCardCount >= chassisDeviceObj.SlotCount)
                    {
                        return; // 静默返回，错误提示已在ViewModel中处理
                    }
                }
                
                // 为板卡自动分配CardName（如果未设置）
                if (device.Name != "空槽" && string.IsNullOrEmpty(device.CardName))
                {
                    device.CardName = GenerateUniqueCardName(chassisName, device);
                }
            }

            chassis.Devices.Add(device);
            
            // 初始化默认板卡配置（例如CAN默认通道）
            EnsureDefaultCardConfig(device, chassis.Name);
            
            // 初始化设备通道并注册到ChannelManager（每块板卡独立维护自身通道，不做全局分配）
            if (device.Channels == null || device.Channels.Count == 0)
            {
                device.InitializeChannels();
            }
            _channelManager.RegisterDevice(device);
        }

        /// <summary>
        /// 从指定机箱移除设备
        /// </summary>
        public void RemoveDeviceFromChassis(string chassisName, string deviceId)
        {
            var chassis = GetChassisByName(chassisName);
            if (chassis == null)
                return;

            var device = chassis.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device == null)
                return;

            // 从ChannelManager注销设备
            _channelManager.UnregisterDevice(deviceId);
            
            chassis.Devices.Remove(device);
            
            // 删除设备后，不需要重新分配所有设备
            // 因为我们已经释放了通道，新添加的设备会自动填补空缺
            // 但我们仍然需要发布事件通知UI刷新
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
            {
                ChassisName = chassisName,
                ModificationType = "DeviceRemoved",
                DeviceInfo = $"已删除设备: {device.Name}"
            });
        }

        /// <summary>
        /// 获取指定机箱的设备列表
        /// </summary>
        public List<Models.Devices.DeviceBase> GetChassisDevices(string chassisName)
        {
            var chassis = GetChassisByName(chassisName);
            return chassis?.Devices?.ToList() ?? new List<Models.Devices.DeviceBase>();
        }


        /// <summary>
        /// 生成唯一名称（建议名称，不占用名称，只有在实际添加机箱时才占用）
        /// </summary>
        /// <param name="baseName">基础名称</param>
        /// <returns>唯一名称建议</returns>
        public string GenerateUniqueName(string baseName)
        {
            // 对于PXI机箱，使用"PXI机箱+数字"格式，从1开始
            if (baseName == AppConstants.DefaultChassisNamePrefix)
            {
                int counter = 1;
                string newName;
                do
                {
                    newName = $"{AppConstants.DefaultChassisNamePrefix}{counter}";
                    counter++;
                } while (_chassisNames.Contains(newName));

                // 不在这里添加到_chassisNames，只有在用户确认添加时才占用
                return newName;
            }

            // 其他情况保持原有逻辑
            if (!_chassisNames.Contains(baseName))
            {
                return baseName;
            }

            int otherCounter = 1;
            string otherNewName;
            do
            {
                otherNewName = $"{baseName}_{otherCounter}";
                otherCounter++;
            } while (_chassisNames.Contains(otherNewName));

            return otherNewName;
        }
        
        /// <summary>
        /// 占用机箱名称（在用户确认添加机箱时调用）
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        public void ReserveChassisName(string chassisName)
        {
            if (!string.IsNullOrEmpty(chassisName) && !_chassisNames.Contains(chassisName))
            {
                _chassisNames.Add(chassisName);
            }
        }

        /// <summary>
        /// 同步 ChassisDevice.Children 与 chassis.Devices 中的板卡引用
        /// 确保它们引用同一个对象实例，避免更新时出现实例不一致的问题
        /// </summary>
        private void SyncChassisDeviceChildren(ChassisModel chassis)
        {
            if (chassis?.Devices == null) return;

            // 找到机箱设备
            var chassisDevice = chassis.Devices.FirstOrDefault(d => d.DeviceType == AppConstants.DeviceTypeChassis);
            if (chassisDevice?.Children == null || chassisDevice.Children.Count == 0) return;

            // 获取 chassis.Devices 中的所有板卡（作为权威数据源）
            var cardsInDevices = chassis.Devices.Where(d => d.DeviceType == AppConstants.DeviceTypeCard).ToList();

            if (chassisDevice.Children != null && chassisDevice.Children.Count > 0)
            {
                foreach (var child in chassisDevice.Children)
                {
                    if (child == null) continue;
                    if (child.DeviceType != AppConstants.DeviceTypeCard) continue;

                    var normalizedChildSlot = (child.SlotPosition ?? string.Empty).Replace(" ", "").Trim();
                    bool exists = false;

                    if (!string.IsNullOrWhiteSpace(normalizedChildSlot))
                    {
                        exists = cardsInDevices.Any(c => string.Equals((c.SlotPosition ?? string.Empty).Replace(" ", "").Trim(), normalizedChildSlot, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (!string.IsNullOrWhiteSpace(child.Id))
                    {
                        exists = cardsInDevices.Any(c => string.Equals(c.Id, child.Id, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        exists = cardsInDevices.Any(c => string.Equals(c.CardName ?? string.Empty, child.CardName ?? string.Empty, StringComparison.Ordinal) &&
                                                        string.Equals(c.Model ?? string.Empty, child.Model ?? string.Empty, StringComparison.Ordinal));
                    }

                    if (!exists)
                    {
                        chassis.Devices.Add(child);
                        cardsInDevices.Add(child);
                    }
                }
            }

            // 遍历 ChassisDevice.Children，用 chassis.Devices 中的对应板卡替换
            for (int i = 0; i < chassisDevice.Children.Count; i++)
            {
                var child = chassisDevice.Children[i];
                if (child == null) continue;
                if (child.DeviceType != AppConstants.DeviceTypeCard) continue;



                DeviceBase matchingCard = null;
                string matchStrategy = "SlotPosition+Model";

                var normalizedSlot = (child.SlotPosition ?? string.Empty).Replace(" ", "").Trim();
                if (!string.IsNullOrWhiteSpace(normalizedSlot))
                {
                    if (!string.IsNullOrWhiteSpace(child.Model))
                    {
                        matchingCard = cardsInDevices.FirstOrDefault(c =>
                            string.Equals((c.SlotPosition ?? string.Empty).Replace(" ", "").Trim(), normalizedSlot, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.Model ?? string.Empty, child.Model ?? string.Empty, StringComparison.Ordinal));
                    }

                    if (matchingCard == null)
                    {
                        matchStrategy = "SlotPosition";
                        matchingCard = cardsInDevices.FirstOrDefault(c =>
                            string.Equals((c.SlotPosition ?? string.Empty).Replace(" ", "").Trim(), normalizedSlot, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (matchingCard == null)
                {
                    matchStrategy = "Id";
                    if (!string.IsNullOrEmpty(child.Id))
                    {
                        matchingCard = cardsInDevices.FirstOrDefault(c => string.Equals(c.Id, child.Id, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (matchingCard == null)
                {
                    matchStrategy = "CardName+SlotPosition";
                    if (!string.IsNullOrEmpty(child.CardName) && !string.IsNullOrEmpty(child.SlotPosition))
                    {
                        matchingCard = cardsInDevices.FirstOrDefault(c => c.CardName == child.CardName && c.SlotPosition == child.SlotPosition);
                    }
                }

                if (matchingCard == null)
                {
                    matchStrategy = "CardName";
                    if (!string.IsNullOrEmpty(child.CardName))
                    {
                        matchingCard = cardsInDevices.FirstOrDefault(c => c.CardName == child.CardName);
                    }
                }

                if (matchingCard == null)
                {
                    matchStrategy = "Model+SlotPosition";
                    if (!string.IsNullOrEmpty(child.Model) && !string.IsNullOrEmpty(child.SlotPosition))
                    {
                        matchingCard = cardsInDevices.FirstOrDefault(c => c.Model == child.Model && c.SlotPosition == child.SlotPosition);
                    }
                }

                if (matchingCard == null)
                {
                    matchStrategy = "Model";
                    if (!string.IsNullOrEmpty(child.Model))
                    {
                        matchingCard = cardsInDevices.FirstOrDefault(c => c.Model == child.Model);
                    }
                }

                if (matchingCard == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyncChassisDeviceChildren] 未找到匹配板卡: CardName={child.CardName}, Id={child.Id}, Model={child.Model}, Slot={child.SlotPosition}");
                    continue;
                }

                if (ReferenceEquals(matchingCard, child))
                {
                    continue;
                }

                if (matchingCard.CardConfigData == null && child.CardConfigData != null)
                {
                    matchingCard.CardConfigData = child.CardConfigData;
                }
                if (string.IsNullOrEmpty(matchingCard.CardName) && !string.IsNullOrEmpty(child.CardName))
                {
                    matchingCard.CardName = child.CardName;
                }
                if (string.IsNullOrEmpty(matchingCard.SlotPosition) && !string.IsNullOrEmpty(child.SlotPosition))
                {
                    matchingCard.SlotPosition = child.SlotPosition;
                }

                System.Diagnostics.Debug.WriteLine($"[SyncChassisDeviceChildren] 同步板卡引用({matchStrategy}): {child.CardName}, 旧HashCode={child.GetHashCode()}, 新HashCode={matchingCard.GetHashCode()}");
                chassisDevice.Children[i] = matchingCard;
            }
        }

        /// <summary>
        /// 为机箱内没有CardName的板卡分配默认名称
        /// </summary>
        private void AssignDefaultCardNames(ChassisModel chassis)
        {
            if (chassis == null || chassis.Devices == null)
                return;

            var cards = chassis.Devices.Where(d => d.DeviceType == AppConstants.DeviceTypeCard).ToList();
            int cardIndex = 1;

            foreach (var card in cards)
            {
                if (card.Name == "空槽")
                {
                    continue;
                }

                if (string.IsNullOrEmpty(card.CardName))
                {
                    // 生成唯一名称
                    string candidateName;
                    do
                    {
                        candidateName = $"板卡{cardIndex}";
                        cardIndex++;
                    } while (cards.Any(c => c.CardName == candidateName));

                    card.CardName = candidateName;
                }
            }
        }

        /// <summary>
        /// 为机箱内的板卡生成唯一名称（基于ParentNode，如"模拟量采集1"、"数字量采集2"）
        /// </summary>
        
        private void EnsureDefaultCardConfig(DeviceBase device, string chassisName)
        {
            if (device == null)
                return;

            if (device is CanBusDevice canDevice)
            {
                var canConfig = device.CardConfigData as CanCardConfig;
                if (canConfig == null)
                {
                    canConfig = new CanCardConfig();
                    device.CardConfigData = canConfig;
                }

                canConfig.CardId = device.Id;
                canConfig.CardName = device.CardName;
                canConfig.CardModel = device.Model;
                canConfig.ChassisName = chassisName;

                EnsureCanChannelConfigs(canDevice, canConfig);
            }

            if (device.Children != null)
            {
                foreach (var child in device.Children)
                {
                    EnsureDefaultCardConfig(child, chassisName);
                }
            }
        }

        private void EnsureCanChannelConfigs(CanBusDevice device, CanCardConfig config)
        {
            if (device == null || config == null)
                return;

            int totalChannels = device.ChannelCount > 0
                ? device.ChannelCount
                : device.Channels?.Count ?? 0;

            config.Channels ??= new ObservableCollection<CanChannelConfig>();

            var existingNames = new HashSet<string>(config.Channels.Select(c => c.ChannelName), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < totalChannels; i++)
            {
                string channelName = $"CAN{i}";
                if (existingNames.Add(channelName))
                {
                    config.Channels.Add(new CanChannelConfig
                    {
                        ChannelName = channelName,
                        BaudRate = device.MaxBaudRate
                    });
                }
            }

            if (totalChannels == 0 && device.Channels != null)
            {
                foreach (var channel in device.Channels)
                {
                    if (existingNames.Add(channel.Name))
                    {
                        config.Channels.Add(new CanChannelConfig
                        {
                            ChannelName = channel.Name,
                            BaudRate = device.MaxBaudRate
                        });
                    }
                }
            }
        }

public string GenerateUniqueCardName(string chassisName, DeviceBase device)
        {
            var chassis = GetChassisByName(chassisName);
            if (chassis == null || device == null)
                return "板卡1";

            // 获取设备的ParentNode作为命名前缀
            string prefix = device.ParentNode ?? "板卡";
            
            // 特殊处理：控制器类型固定返回"控制器"（不带数字）
            if (prefix.Contains("控制器"))
            {
                return "控制器";
            }

            // 获取机箱内所有板卡
            var cards = chassis.Devices.Where(d => d.DeviceType == AppConstants.DeviceTypeCard).ToList();
            
            // 找到相同前缀的最大编号
            int maxNumber = 0;
            foreach (var card in cards)
            {
                if (!string.IsNullOrEmpty(card.CardName) && card.CardName.StartsWith(prefix))
                {
                    // 提取数字部分
                    string numberPart = card.CardName.Substring(prefix.Length);
                    if (int.TryParse(numberPart, out int number))
                    {
                        maxNumber = Math.Max(maxNumber, number);
                    }
                }
            }

            // 返回下一个编号
            return $"{prefix}{maxNumber + 1}";
        }

        /// <summary>
        /// 重命名板卡，检查同机箱内名称唯一性
        /// </summary>
        public bool RenameCard(string chassisName, string deviceId, string newCardName)
        {
            if (string.IsNullOrWhiteSpace(newCardName))
                return false;

            var chassis = GetChassisByName(chassisName);
            if (chassis == null)
                return false;

            var device = chassis.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device == null || device.DeviceType != AppConstants.DeviceTypeCard)
                return false;

            // 验证名称唯一性
            if (!ValidateCardName(chassisName, newCardName, deviceId))
                return false;

            device.CardName = newCardName;
            
            // 发布设备修改事件
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Publish(new DeviceModifiedEventArgs
            {
                ChassisName = chassisName,
                ModificationType = "CardRenamed",
                DeviceInfo = $"板卡重命名为: {newCardName}"
            });
            
            return true;
        }

        /// <summary>
        /// 验证板卡名称在机箱内是否唯一
        /// </summary>
        public bool ValidateCardName(string chassisName, string cardName, string excludeDeviceId = null)
        {
            if (string.IsNullOrWhiteSpace(cardName))
                return false;

            var chassis = GetChassisByName(chassisName);
            if (chassis == null)
                return true;

            // 检查是否有其他板卡使用相同名称
            var cards = chassis.Devices.Where(d => d.DeviceType == AppConstants.DeviceTypeCard);
            foreach (var card in cards)
            {
                if (card.Id != excludeDeviceId && card.CardName == cardName)
                    return false;
            }

            return true;
        }

        #region Property Change Monitoring

        /// <summary>
        /// 订阅机箱属性更改事件
        /// </summary>
        private void SubscribeToChassisPropertyChanged(ChassisModel chassis)
        {
            if (chassis == null) return;
            
            chassis.PropertyChanged += OnChassisPropertyChanged;
        }

        /// <summary>
        /// 取消订阅机箱属性更改事件
        /// </summary>
        private void UnsubscribeFromChassisPropertyChanged(ChassisModel chassis)
        {
            if (chassis == null) return;
            
            chassis.PropertyChanged -= OnChassisPropertyChanged;
        }

        /// <summary>
        /// 订阅机箱设备集合更改事件
        /// </summary>
        private void SubscribeToDevicesCollectionChanged(ChassisModel chassis)
        {
            if (chassis == null || chassis.Devices == null) return;
            
            chassis.Devices.CollectionChanged += OnDevicesCollectionChanged;
            
            // 订阅现有设备的属性更改事件
            foreach (var device in chassis.Devices)
            {
                SubscribeToDevicePropertyChanged(device);
            }
        }

        /// <summary>
        /// 取消订阅机箱设备集合更改事件
        /// </summary>
        private void UnsubscribeFromDevicesCollectionChanged(ChassisModel chassis)
        {
            if (chassis == null || chassis.Devices == null) return;
            
            chassis.Devices.CollectionChanged -= OnDevicesCollectionChanged;
            
            // 取消订阅所有设备的属性更改事件
            foreach (var device in chassis.Devices)
            {
                UnsubscribeFromDevicePropertyChanged(device);
            }
        }

        /// <summary>
        /// 订阅设备属性更改事件（递归订阅子设备）
        /// </summary>
        private void SubscribeToDevicePropertyChanged(DeviceBase device)
        {
            if (device == null) return;
            
            device.PropertyChanged += OnDevicePropertyChanged;
            
            // 递归订阅子设备
            if (device.Children != null)
            {
                device.Children.CollectionChanged += OnDeviceChildrenCollectionChanged;
                
                foreach (var child in device.Children)
                {
                    SubscribeToDevicePropertyChanged(child);
                }
            }
        }

        /// <summary>
        /// 取消订阅设备属性更改事件（递归取消订阅子设备）
        /// </summary>
        private void UnsubscribeFromDevicePropertyChanged(DeviceBase device)
        {
            if (device == null) return;
            
            device.PropertyChanged -= OnDevicePropertyChanged;
            
            // 递归取消订阅子设备
            if (device.Children != null)
            {
                device.Children.CollectionChanged -= OnDeviceChildrenCollectionChanged;
                
                foreach (var child in device.Children)
                {
                    UnsubscribeFromDevicePropertyChanged(child);
                }
            }
        }

        /// <summary>
        /// 机箱属性更改事件处理
        /// </summary>
        private void OnChassisPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 加载数据时不触发修改事件
            if (_isLoadingData) return;
            
            // 忽略 IsSelected 属性的更改（UI选择状态不应标记项目为已修改）
            if (e.PropertyName == nameof(ChassisModel.IsSelected)) return;
            
            var chassis = sender as ChassisModel;
            if (chassis == null) return;
            
            // 发布项目修改事件
            _eventAggregator?.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "ChassisProperty",
                Description = $"修改机箱属性: {chassis.Name} - {e.PropertyName}"
            });
        }

        /// <summary>
        /// 设备集合更改事件处理
        /// </summary>
        private void OnDevicesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 加载数据时不触发修改事件
            if (_isLoadingData) return;
            
            // 处理新增的设备
            if (e.NewItems != null)
            {
                foreach (DeviceBase device in e.NewItems)
                {
                    SubscribeToDevicePropertyChanged(device);
                }
            }
            
            // 处理移除的设备
            if (e.OldItems != null)
            {
                foreach (DeviceBase device in e.OldItems)
                {
                    UnsubscribeFromDevicePropertyChanged(device);
                }
            }
            
            // 发布项目修改事件
            _eventAggregator?.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "DeviceCollection",
                Description = $"设备集合已更改"
            });
        }

        /// <summary>
        /// 设备属性更改事件处理
        /// </summary>
        private void OnDevicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 加载数据时不触发修改事件
            if (_isLoadingData) return;
            
            // UI交互操作时不触发修改事件（如点击板卡查看详情、展开/收起子节点等）
            if (_isUIInteraction) return;
            
            // 忽略 IsSelected 和 IsExpanded 属性的更改（UI状态不应标记项目为已修改）
            if (e.PropertyName == nameof(DeviceBase.IsSelected) || 
                e.PropertyName == nameof(DeviceBase.IsExpanded)) return;
            
            var device = sender as DeviceBase;
            if (device == null) return;
            
            // 发布项目修改事件
            _eventAggregator?.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "DeviceProperty",
                Description = $"修改设备属性: {device.Model} - {e.PropertyName}"
            });
        }

        /// <summary>
        /// 设备子节点集合更改事件处理
        /// </summary>
        private void OnDeviceChildrenCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 加载数据时不触发修改事件
            if (_isLoadingData) return;
            
            // UI交互操作时不触发修改事件
            if (_isUIInteraction) return;
            
            // 处理新增的子设备
            if (e.NewItems != null)
            {
                foreach (DeviceBase device in e.NewItems)
                {
                    SubscribeToDevicePropertyChanged(device);
                }
            }
            
            // 处理移除的子设备
            if (e.OldItems != null)
            {
                foreach (DeviceBase device in e.OldItems)
                {
                    UnsubscribeFromDevicePropertyChanged(device);
                }
            }
            
            // 发布项目修改事件
            _eventAggregator?.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "DeviceChildren",
                Description = $"设备子节点已更改"
            });
        }

        #endregion

    }
}

