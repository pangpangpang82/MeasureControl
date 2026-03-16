using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Hardware
{
    /// <summary>
    /// 连接管理ViewModel - 负责机箱连接和连接线管理
    /// </summary>
    public class ConnectionManagementViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly IEventAggregator _eventAggregator;
        private readonly IChassisConnectionService _chassisConnectionService;
        private readonly IDialogService _dialogService;

        private ObservableCollection<ChassisConnection> _chassisConnections;
        private ConnectionDetails _selectedConnection;
        private bool _isConnectionDetailsVisible;

        #endregion

        #region Public Properties

        /// <summary>
        /// 机箱连接集合
        /// </summary>
        public ObservableCollection<ChassisConnection> ChassisConnections
        {
            get
            {
                if (_chassisConnections == null)
                {
                    _chassisConnections = new ObservableCollection<ChassisConnection>();
                    UpdateChassisConnections();
                }
                return _chassisConnections;
            }
        }

        /// <summary>
        /// 连接线列表（用于绘制）
        /// </summary>
        public List<ConnectionLine> ConnectionLines => _chassisConnectionService.GetConnectionLines();

        /// <summary>
        /// 选中的连接线详细信息
        /// </summary>
        public ConnectionDetails SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (SetProperty(ref _selectedConnection, value))
                {
                    UpdateConnectionDetails();
                }
            }
        }

        /// <summary>
        /// 连接线详细信息是否可见
        /// </summary>
        public bool IsConnectionDetailsVisible
        {
            get => _isConnectionDetailsVisible;
            set => SetProperty(ref _isConnectionDetailsVisible, value);
        }

        #endregion

        #region Commands

        public ICommand CreateConnectionCommand { get; private set; }
        public ICommand DeleteConnectionCommand { get; private set; }
        public ICommand ClearAllConnectionsCommand { get; private set; }
        public ICommand SelectConnectionCommand { get; private set; }
        public ICommand ClearConnectionSelectionCommand { get; private set; }

        #endregion

        #region Constructor

        public ConnectionManagementViewModel(
            IEventAggregator eventAggregator,
            IChassisConnectionService chassisConnectionService,
            IDialogService dialogService)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _chassisConnectionService = chassisConnectionService ?? throw new ArgumentNullException(nameof(chassisConnectionService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            InitializeCommands();
            SubscribeToEvents();
        }

        #endregion

        #region Private Methods

        private void InitializeCommands()
        {
            CreateConnectionCommand = new DelegateCommand<CreateConnectionEventArgs>(OnCreateConnection);
            DeleteConnectionCommand = new DelegateCommand<string>(OnDeleteConnection);
            ClearAllConnectionsCommand = new DelegateCommand(OnClearAllConnections);
            SelectConnectionCommand = new DelegateCommand<ConnectionDetails>(OnSelectConnection);
            ClearConnectionSelectionCommand = new DelegateCommand(OnClearConnectionSelection);
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<ChassisConnectionsLoadEvent>().Subscribe(OnChassisConnectionsLoad);
            _eventAggregator.GetEvent<ChassisConnectionsRequestEvent>().Subscribe(OnChassisConnectionsRequest);
            _eventAggregator.GetEvent<ConnectionLinesRequestEvent>().Subscribe(OnConnectionLinesRequest);
            _eventAggregator.GetEvent<ConnectionLinesLoadEvent>().Subscribe(OnConnectionLinesLoad);
            _eventAggregator.GetEvent<ProjectClosedEvent>().Subscribe(OnProjectClosed);
        }

        private void UpdateChassisConnections()
        {
            try
            {
                var currentConnections = _chassisConnectionService.GetAllConnections();
                if (_chassisConnections == null)
                {
                    _chassisConnections = new ObservableCollection<ChassisConnection>(currentConnections);
                }
                else
                {
                    // 更新现有集合，保持引用不变
                    var oldCount = _chassisConnections.Count;
                    _chassisConnections.Clear();
                    foreach (var connection in currentConnections)
                    {
                        _chassisConnections.Add(connection);
                    }
                }

                // 通知属性变更
                RaisePropertyChanged(nameof(ChassisConnections));
                RaisePropertyChanged(nameof(ConnectionLines));
            }
            catch (Exception)
            {
            }
        }

        private void UpdateConnectionDetails()
        {
            if (SelectedConnection == null)
            {
                IsConnectionDetailsVisible = false;
                return;
            }

            try
            {
                IsConnectionDetailsVisible = true;
                // 这里可以添加更多连接详细信息的处理逻辑
            }
            catch (Exception)
            {
                IsConnectionDetailsVisible = false;
            }
        }

        #endregion

        #region Command Implementations

        private void OnCreateConnection(CreateConnectionEventArgs args)
        {
            if (args?.Connection == null) return;

            try
            {
                if (_chassisConnectionService.AddConnection(args.Connection))
                {
                    UpdateChassisConnections();
                    _eventAggregator.GetEvent<ConnectionCreatedEvent>().Publish(args.Connection);
                }
                else
                {
                    _dialogService.ShowWarningDialog("创建连接失败", "警告");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"创建连接失败: {ex.Message}", "错误");
            }
        }

        private void OnDeleteConnection(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return;

            try
            {
                if (_chassisConnectionService.RemoveConnection(connectionId))
                {
                    UpdateChassisConnections();
                    _eventAggregator.GetEvent<ConnectionDeletedEvent>().Publish(connectionId);
                }
                else
                {
                    _dialogService.ShowWarningDialog("删除连接失败", "警告");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"删除连接失败: {ex.Message}", "错误");
            }
        }

        private void OnClearAllConnections()
        {
            try
            {
                _chassisConnectionService.ClearAllConnections();
                UpdateChassisConnections();
                _eventAggregator.GetEvent<AllConnectionsClearedEvent>().Publish();
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"清除所有连接失败: {ex.Message}", "错误");
            }
        }

        private void OnSelectConnection(ConnectionDetails connection)
        {
            SelectedConnection = connection;
        }

        private void OnClearConnectionSelection()
        {
            SelectedConnection = null;
        }

        #endregion

        #region Event Handlers

        private void OnChassisConnectionsLoad(ChassisConnectionsLoadEventArgs args)
        {
            if (args?.Connections != null)
            {
                _chassisConnections?.Clear();
                foreach (var connection in args.Connections)
                {
                    _chassisConnections?.Add(connection);
                }
                RaisePropertyChanged(nameof(ChassisConnections));
            }
        }

        private void OnChassisConnectionsRequest(ChassisConnectionsRequestEventArgs args)
        {
            if (args != null)
            {
                args.Connections = _chassisConnections?.ToList() ?? new List<ChassisConnection>();
            }
        }

        private void OnConnectionLinesRequest(ConnectionLinesRequestEventArgs args)
        {
            if (args != null)
            {
                args.ConnectionLines = ConnectionLines;
            }
        }

        private void OnConnectionLinesLoad(ConnectionLinesLoadEventArgs args)
        {
            if (args?.ConnectionLines != null)
            {
                RaisePropertyChanged(nameof(ConnectionLines));
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 刷新连接数据
        /// </summary>
        public void RefreshConnections()
        {
            UpdateChassisConnections();
        }

        /// <summary>
        /// 检查两个机箱是否已连接
        /// </summary>
        /// <param name="chassis1">机箱1</param>
        /// <param name="chassis2">机箱2</param>
        /// <returns>是否已连接</returns>
        public bool AreChassisConnected(string chassis1, string chassis2)
        {
            return _chassisConnectionService.AreChassisConnected(chassis1, chassis2);
        }

        /// <summary>
        /// 处理项目关闭事件
        /// </summary>
        private void OnProjectClosed()
        {
            try
            {
                // 清理连接数据
                if (_chassisConnections != null)
                {
                    _chassisConnections.Clear();
                }
                
                // 清理选中的连接
                SelectedConnection = null;
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 使用 ResourceCleanupHelper 清理集合
                ResourceCleanupHelper.CleanupCollection(_chassisConnections);
                
                // 清理选中的连接
                SelectedConnection = null;
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
