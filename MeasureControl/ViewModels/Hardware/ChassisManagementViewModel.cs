using System;
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
using Prism.Regions;

namespace MeasureControl.ViewModels.Hardware
{
    /// <summary>
    /// 机箱管理ViewModel - 负责机箱选择和设备添加
    /// </summary>
    public class ChassisManagementViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;

        private string _chassisName;
        private ObservableCollection<string> _availableChassis;
        private string _selectedChassis;
        private bool _showDropHint;

        #endregion

        #region Public Properties

        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        /// <summary>
        /// 可用机箱列表
        /// </summary>
        public ObservableCollection<string> AvailableChassis
        {
            get => _availableChassis;
            set => SetProperty(ref _availableChassis, value);
        }

        /// <summary>
        /// 选中的机箱
        /// </summary>
        public string SelectedChassis
        {
            get => _selectedChassis;
            set
            {
                if (SetProperty(ref _selectedChassis, value))
                {
                    OnChassisSelectionChanged();
                }
            }
        }

        /// <summary>
        /// 是否显示拖拽提示
        /// </summary>
        public bool ShowDropHint
        {
            get => _showDropHint;
            set => SetProperty(ref _showDropHint, value);
        }

        #endregion

        #region Commands

        public ICommand AddChassisCommand { get; private set; }
        public ICommand SelectChassisCommand { get; private set; }

        #endregion

        #region Constructor

        public ChassisManagementViewModel(
            IPxiChassisService pxiChassisService,
            IEventAggregator eventAggregator,
            IDialogService dialogService)
        {
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            InitializeCollections();
            InitializeCommands();
            SubscribeToEvents();
            InitializeAvailableChassis();
        }

        #endregion

        #region Private Methods

        private void InitializeCollections()
        {
            AvailableChassis = new ObservableCollection<string>();
            ChassisName = "PXI机箱1";
        }

        private void InitializeCommands()
        {
            AddChassisCommand = new DelegateCommand(OnAddChassis);
            SelectChassisCommand = new DelegateCommand<string>(OnSelectChassis);
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Subscribe(OnPxiChassisSelected);
            //_eventAggregator.GetEvent<AddPxiChassisEvent>().Subscribe(OnAddPxiChassis);
            _eventAggregator.GetEvent<DeletePxiChassisEvent>().Subscribe(OnDeletePxiChassis);
        }

        private void InitializeAvailableChassis()
        {
            try
            {
                AvailableChassis.Clear();
                var chassisList = _pxiChassisService.GetAllChassis();
                foreach (var chassis in chassisList)
                {
                    AvailableChassis.Add(chassis.Name);
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnChassisSelectionChanged()
        {
            if (!string.IsNullOrEmpty(SelectedChassis))
            {
                ChassisName = SelectedChassis;
                _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Publish(new PxiChassisSelectedEventArgs
                {
                    ChassisName = SelectedChassis,
                    ChassisId = _pxiChassisService.GetChassisByName(SelectedChassis)?.Id
                });
            }
        }

        #endregion

        #region Command Implementations

        private void OnAddChassis()
        {
            try
            {
                var uniqueName = _pxiChassisService.GenerateUniqueName("PXI机箱");
                var newChassis = new ChassisModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = uniqueName,
                    GridRow = 0,
                    GridColumn = 0
                };

                if (_pxiChassisService.AddChassis(newChassis))
                {
                    AvailableChassis.Add(uniqueName);
                    SelectedChassis = uniqueName;
                    _eventAggregator.GetEvent<AddPxiChassisEvent>().Publish(uniqueName);
                }
                else
                {
                    _dialogService.ShowWarningDialog("添加机箱失败", "警告");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"添加机箱失败: {ex.Message}", "错误");
            }
        }

        private void OnSelectChassis(string chassisName)
        {
            SelectedChassis = chassisName;
        }

        #endregion

        #region Event Handlers

        private void OnPxiChassisSelected(PxiChassisSelectedEventArgs args)
        {
            if (args?.ChassisName != null && AvailableChassis.Contains(args.ChassisName))
            {
                SelectedChassis = args.ChassisName;
            }
        }

        //private void OnAddPxiChassis(string chassisName)
        //{
        //    if (!string.IsNullOrEmpty(chassisName) && !AvailableChassis.Contains(chassisName))
        //    {
        //        AvailableChassis.Add(chassisName);
        //    }
        //}

        private void OnDeletePxiChassis(string chassisName)
        {
            if (!string.IsNullOrEmpty(chassisName) && AvailableChassis.Contains(chassisName))
            {
                AvailableChassis.Remove(chassisName);
                if (SelectedChassis == chassisName)
                {
                    SelectedChassis = AvailableChassis.FirstOrDefault();
                }
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
                ResourceCleanupHelper.CleanupCollection(_availableChassis);
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
