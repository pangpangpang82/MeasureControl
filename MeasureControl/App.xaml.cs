using System.Windows;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;
using MeasureControl.ViewModels.Database;
using MeasureControl.ViewModels.Dialogs;
using MeasureControl.ViewModels.Hardware;
using MeasureControl.ViewModels.IcdConfig;
using MeasureControl.ViewModels.TdmSystem;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using MeasureControl.ViewModels.SingleBoardTest;
using MeasureControl.ViewModels.SingleBoardTest.AirController;
using MeasureControl.ViewModels.SingleBoardTest.HydraulicController;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Views;
using MeasureControl.Views.Common;
using MeasureControl.Views.ConfigTabel;
using MeasureControl.Views.Database;
using MeasureControl.Views.Dialogs;
using MeasureControl.Views.Hardware;
// Removed incorrect using aliases introduced during merge that do not exist as namespaces
using MeasureControl.Views.TdmSystem;
using MeasureControl.Views.TestContent;
using MeasureControl.Views.TestTask;
using MeasureControl.Views.SingleBoardTest;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Regions;
namespace MeasureControl
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : PrismApplication
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            // Replace existing Debug/Trace listeners with a formatted wrapper so Output 窗口显示统一的时间戳和线程信息。
            try
            {
                var existing = System.Diagnostics.Debug.Listeners;
                var snapshot = new System.Collections.Generic.List<System.Diagnostics.TraceListener>();
                foreach (System.Diagnostics.TraceListener tl in existing) snapshot.Add(tl);
                System.Diagnostics.Debug.Listeners.Clear();
                foreach (var tl in snapshot)
                {
                    System.Diagnostics.Debug.Listeners.Add(new MeasureControl.Helpers.FormattedTraceListener(tl));
                }
            }
            catch
            {
                // 不抛出异常以免影响程序启动
            }
            base.OnStartup(e);
        }
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
            //var login = Container.Resolve<Login>();
            //if (login.DataContext is LoginViewModel vm)
            //{
            //    vm.LoginSuccess += () =>
            //    {
            //        var mainWindow = Container.Resolve<MainWindow>();
            //        var regionManager = Container.Resolve<IRegionManager>();
            //        RegionManager.SetRegionManager(mainWindow, regionManager);
            //        RegionManager.UpdateRegions();
            //        Application.Current.MainWindow = mainWindow;
            //        mainWindow.Show();
            //        login.Close();
            //    };
            //}
            //return login;
        }
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 注册核心服务为单例
            containerRegistry.RegisterSingleton<ProjectService>();
            containerRegistry.RegisterSingleton<ChannelManager>();  // 通道管理器
            containerRegistry.RegisterSingleton<IPxiChassisService, PxiChassisService>();
            containerRegistry.RegisterSingleton<IProjectTreeService, ProjectTreeService>();
            containerRegistry.RegisterSingleton<IDialogService, DialogService>();
            containerRegistry.RegisterSingleton<IDragDropService, DragDropService>();
            containerRegistry.RegisterSingleton<IWindowManagerService, WindowManagerService>();
            containerRegistry.RegisterSingleton<IProjectSaveStateService, ProjectSaveStateService>();
            containerRegistry.RegisterSingleton<IChassisConnectionService, ChassisConnectionService>();
            containerRegistry.RegisterSingleton<IChannelBindingService, ChannelBindingService>();
            containerRegistry.RegisterSingleton<INavigationStateService, NavigationStateService>();
            containerRegistry.RegisterSingleton<IDocumentManagerService, DocumentManagerService>();
            containerRegistry.RegisterSingleton<SignalValueUpdateService>();
            containerRegistry.RegisterSingleton<ISingleBoardTestContextService, SingleBoardTestContextService>();
            containerRegistry.RegisterSingleton<IBoardPowerService, BoardPowerService>();
            containerRegistry.RegisterSingleton<MatrixSwitchTcpServerAutoStartService>();
            // 注册硬件API（可选，用于单板测试）
            containerRegistry.Register<IDmmApi, DmmSocketApi>();
            containerRegistry.RegisterSingleton<IComponentPowerStateApi, ComponentPowerStateApi>();
            // 注册导航服务（新重构的服务）
            containerRegistry.RegisterSingleton<NavigationRegistry>();
            containerRegistry.RegisterSingleton<INavigationService, NavigationService>();
            // 注册ViewModel
            containerRegistry.Register<MainWindowViewModel>();
            containerRegistry.Register<HardwareConfigViewModel>();
            containerRegistry.Register<PxiChassisViewModel>();
            containerRegistry.Register<LoginViewModel>();
            containerRegistry.Register<ReMessageBoxViewModel>();
            containerRegistry.Register<TestStartDialogViewModel>();
            containerRegistry.Register<ChannelConfigTabelViewModel>();
            containerRegistry.Register<SignalConfigTabelViewModel>();
            containerRegistry.Register<IcdMappingTabelViewModel>();
            containerRegistry.Register<MatrixSwitchConfigTableViewModel>();
            containerRegistry.Register<IcdConfigTabelViewModel>();
            containerRegistry.Register<DatabaseConfigViewModel>();
            containerRegistry.Register<FloatingWindowViewModel>();
            containerRegistry.Register<DataCalibrationViewModel>();
            containerRegistry.RegisterSingleton<HC_6_1ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_10ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_3ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_4ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_5ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_6ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_7ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_8ViewModel>();
            containerRegistry.RegisterSingleton<HC_6_9ViewModel>();
            containerRegistry.RegisterSingleton<PowerImpedanceTestViewModel>();
            containerRegistry.RegisterSingleton<SecondaryPowerTestViewModel>();
            containerRegistry.RegisterSingleton<LowVoltageAlarmTestViewModel>();
            containerRegistry.RegisterSingleton<TemperatureAcquisitionTestViewModel>();
            containerRegistry.RegisterSingleton<DiscreteInputTestViewModel>();
            containerRegistry.RegisterSingleton<DiscreteOutputTestViewModel>();
            containerRegistry.RegisterSingleton<RS422CommunicationFunctionTestViewModel>();
            containerRegistry.RegisterSingleton<RS422SelfCheckTestViewModel>();
            containerRegistry.RegisterSingleton<PowerToGroundImpedanceTestViewModel>();
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.PowerImpedanceTestViewModel>();
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardPowerImpedanceTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var dmm = provider.Resolve<IDmmApi>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                var pxi = provider.Resolve<IPxiChassisService>();

                return new MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardPowerImpedanceTestViewModel(
                    ctx,
                    proj,
                    ea,
                    dmm,
                    cps,
                    pxi);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardDiscreteInputModuleTestViewModel>(provider =>
            {
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardDiscreteInputModuleTestViewModel(ea, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.DiscreteOutputModuleTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var dmm = provider.Resolve<IDmmApi>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.DiscreteOutputModuleTestViewModel(ctx, proj, ea, dmm, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.TemperatureSensorSignalAcquisitionTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.TemperatureSensorSignalAcquisitionTestViewModel(ctx, ea, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.PressureSensorSignalAcquisitionTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.PressureSensorSignalAcquisitionTestViewModel(ctx, ea, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.OxygenSensorSignalAcquisitionTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.OxygenSensorSignalAcquisitionTestViewModel(ctx, ea, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.SecondaryTertiaryPowerTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var dmm = provider.Resolve<IDmmApi>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.SecondaryTertiaryPowerTestViewModel(ctx, proj, ea, dmm, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.PowerMonitorTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var dmm = provider.Resolve<IDmmApi>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.PowerMonitorTestViewModel(ctx, proj, ea, dmm, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.TcvMotorDriveTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.TcvMotorDriveTestViewModel(ctx, proj, ea, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.OverTemperatureCutoffTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var dmm = provider.Resolve<IDmmApi>();
                var pxi = provider.Resolve<IPxiChassisService>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.OverTemperatureCutoffTestViewModel(ctx, proj, ea, dmm, pxi, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.LatchModuleCircuitTestViewModel>(provider =>
            {
                var pxi = provider.Resolve<IPxiChassisService>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.LatchModuleCircuitTestViewModel(pxi, cps);
            });
            containerRegistry.RegisterSingleton<MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardSecondaryTertiaryPowerTestViewModel>(provider =>
            {
                var ctx = provider.Resolve<ISingleBoardTestContextService>();
                var proj = provider.Resolve<ProjectService>();
                var ea = provider.Resolve<Prism.Events.IEventAggregator>();
                var pxi = provider.Resolve<IPxiChassisService>();
                var cps = provider.Resolve<IComponentPowerStateApi>();
                return new MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardSecondaryTertiaryPowerTestViewModel(ctx, proj, ea, pxi, cps);
            });
            containerRegistry.Register<A_C_8_1ViewModel>();
            containerRegistry.Register<A_C_7_1ViewModel>();
            // 注册导航页面
            // 单例页面（IsNavigationTarget返回true，重用实例）
            containerRegistry.RegisterForNavigation<HomePage, HomePageViewModel>();  // 首页
            containerRegistry.RegisterForNavigation<HardwareConfig>();              // 设备与网络唯一实例
            containerRegistry.RegisterForNavigation<TDMSystem, TDMSystemViewModel>();  // TDM系统唯一实例
            containerRegistry.RegisterForNavigation<DatabaseConfig, DatabaseConfigViewModel>();  // 数据库管理唯一实例
            containerRegistry.RegisterForNavigation<TaskConfigDatabase>();          // 任务数据库子页面
            containerRegistry.RegisterForNavigation<TestDataBase>();                // 测试数据库子页面
            // 多例页面（IsNavigationTarget返回false，每次导航创建新实例）
            containerRegistry.RegisterForNavigation<PxiChassis, PxiChassisViewModel>();                  // 每个机箱独立实例
            containerRegistry.RegisterForNavigation<ChannelConfigTabel, ChannelConfigTabelViewModel>();  // 每个配置表独立实例
            containerRegistry.RegisterForNavigation<SignalConfigTabel, SignalConfigTabelViewModel>();    // 每个配置表独立实例
            containerRegistry.RegisterForNavigation<IcdMappingTabel, IcdMappingTabelViewModel>();    // ICD映射表独立实例
            containerRegistry.RegisterForNavigation<MatrixSwitchConfigTable, MatrixSwitchConfigTableViewModel>();    // 矩阵开关配置表独立实例
            containerRegistry.RegisterForNavigation<IcdConfigTabel, IcdConfigTabelViewModel>();          // 每个配置表独立实例
            containerRegistry.RegisterForNavigation<TestSequence, TestSequenceViewModel>();              // 每个测试序列独立实例
            containerRegistry.RegisterForNavigation<ReportConfigTabel, ReportConfigTabelViewModel>();    // 每个报表配置独立实例
            containerRegistry.RegisterForNavigation<TestInterface, TestInterfaceViewModel>();            // 每个测试界面独立实例
            containerRegistry.RegisterForNavigation<BoardTest, BoardTestViewModel>();                     // 每个单板测试独立实例
            // 注册窗口
            containerRegistry.Register<MainWindow>();
            containerRegistry.Register<Login>();
            containerRegistry.Register<ReMessageBox>();
            // 注册对话框
            containerRegistry.RegisterDialog<TestStartDialog, TestStartDialogViewModel>();
        }
    }
}
