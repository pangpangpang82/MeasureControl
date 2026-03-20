using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.Views.TestControl;
using MeasureControl.Views.Dialogs;
using Prism.Events;
using Prism.Regions;
using MeasureControl.ViewModels.TestTask.ConfigTabel;

namespace MeasureControl.Views.TestContent
{
    /// <summary>
    /// TestInterface.xaml 的交互逻辑
    /// </summary>
    public partial class TestInterface : UserControl, IRegionMemberLifetime
    {
        private string _currentDragControlType;
        private Dictionary<string, int> _controlCounters = new Dictionary<string, int>();  // 每种控件类型独立计数
        private bool _isTestRunning = false;  // 测试是否正在运行（运行时禁止拖动和编辑）
        private Dictionary<string, FrameworkElement> _controlElements = new Dictionary<string, FrameworkElement>();

        // 硬件控制相关（UI 轮询定时器，从 HardwareControlService 读取值更新控件）
        private DispatcherTimer _hardwarePollingTimer;
        private Dictionary<string, LampControl> _boundLamps = new Dictionary<string, LampControl>();  // 变量路径 -> 指示灯控件
        private Dictionary<string, SwitchControl> _boundSwitches = new Dictionary<string, SwitchControl>();  // 变量路径 -> 开关控件
        private Dictionary<string, (object Control, int RefreshRate, DateTime LastUpdate)> _boundDisplayBoxes 
            = new Dictionary<string, (object, int, DateTime)>();  // 变量路径 -> (显示框/环形仪表控件, 刷新频率Hz, 上次更新时间)

        private TestInterfaceViewModel ViewModel => DataContext as TestInterfaceViewModel;
        
        /// <summary>
        /// IRegionMemberLifetime: 测试运行时保持 View 实例存活，否则允许销毁
        /// </summary>
        public bool KeepAlive => _isTestRunning || (ViewModel?.IsTestRunning == true);

        public TestInterface()
        {
            InitializeComponent();
            Loaded += TestInterface_Loaded;
            Unloaded += TestInterface_Unloaded;
            
            // 点击空白区域清除输入框/下拉框焦点
            PreviewMouseDown += TestInterface_PreviewMouseDown;
        }

        private void TestInterface_Unloaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] Unloaded 事件触发");
            // 取消订阅事件
            var eventAggregator = Prism.Ioc.ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<TestRunningStateChangedEvent>().Unsubscribe(OnTestRunningStateChanged);
            eventAggregator?.GetEvent<ControlPropertyChangedEvent>().Unsubscribe(OnControlPropertyChanged);
        }

        /// <summary>
        /// 订阅测试运行状态事件
        /// </summary>
        private void SubscribeToTestRunningEvent()
        {
            var eventAggregator = Prism.Ioc.ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<TestRunningStateChangedEvent>().Subscribe(OnTestRunningStateChanged);
        }

        /// <summary>
        /// 测试运行状态变化处理
        /// </summary>
        private void OnTestRunningStateChanged(bool isRunning)
        {
            // 只更新CanExecuteTestCommands，控制按钮可用性
            // TestInterface的状态由自己的启动按钮独立控制
            Dispatcher.Invoke(() =>
            {
                if (ViewModel != null)
                {
                    ViewModel.CanExecuteTestCommands = isRunning;

                    // 如果MainWindow停止测试，而TestInterface还在运行，则自动停止TestInterface
                    if (!isRunning && ViewModel.IsTestRunning)
                    {
                        System.Diagnostics.Debug.WriteLine("[TestInterface.OnTestRunningStateChanged] MainWindow停止测试，自动停止TestInterface");
                        ViewModel.StopTest();
                    }
                }

                // 配置面板在MainWindow测试运行时禁用
                if (ConfigItemsControl != null)
                {
                    ConfigItemsControl.IsEnabled = !isRunning;
                }
            });

            System.Diagnostics.Debug.WriteLine($"[TestInterface.OnTestRunningStateChanged] MainWindow测试状态变化: {isRunning}, 更新CanExecuteTestCommands={isRunning}");
        }
        
        /// <summary>
        /// 重置所有控件的显示状态（停止测试时调用）
        /// </summary>
        private void ResetControlsDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                // 重置指示灯
                foreach (var lamp in _boundLamps.Values)
                {
                    lamp.SetValue(0);
                }
                
                // 重置开关（静默更新，不触发事件）
                foreach (var switchCtrl in _boundSwitches.Values)
                {
                    switchCtrl.SetValueSilent(false);
                }
                
                // 重置显示框和环形仪表
                foreach (var (control, _, _) in _boundDisplayBoxes.Values)
                {
                    if (control is DisplayBoxControl displayBox)
                    {
                        displayBox.Value = 0;
                    }
                    else if (control is CircularGaugeControl gauge)
                    {
                        gauge.Value = 0;
                    }
                    else if (control is VerticalGaugeControl vGauge)
                    {
                        vGauge.Value = 0;
                    }
                }
            });
        }

        /// <summary>
        /// 点击空白区域时清除焦点和取消控件选中
        /// </summary>
        private void TestInterface_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的元素
            var hitElement = e.OriginalSource as DependencyObject;
            
            // 检查是否点击了可聚焦控件（包括 ComboBoxItem）
            bool clickedOnFocusable = false;
            bool clickedOnDesignControl = false;
            bool clickedOnConfigPanel = false;
            
            var element = hitElement;
            while (element != null)
            {
                // 检查是否点击了 TextBox、ComboBox、ComboBoxItem 或 Popup（下拉菜单）
                if (element is TextBox || element is ComboBox || element is ComboBoxItem || 
                    element is System.Windows.Controls.Primitives.Popup)
                {
                    clickedOnFocusable = true;
                    clickedOnConfigPanel = true; // 下拉框属于配置面板的一部分
                }
                // 检查是否点击了设计控件（通过Tag识别 - 检查所有注册的控件）
                if (element is FrameworkElement fe && fe.Tag is string tag && _controlElements.ContainsKey(tag))
                {
                    clickedOnDesignControl = true;
                }
                // 检查是否点击了控件配置面板
                if (element == ControlConfigPanel)
                {
                    clickedOnConfigPanel = true;
                }
                element = VisualTreeHelper.GetParent(element);
            }
            
            // 如果没有点击到可聚焦控件，则清除焦点
            if (!clickedOnFocusable)
            {
                Keyboard.ClearFocus();
                this.Focus();
            }
            
            // 如果没有点击设计控件且没有点击配置面板，则取消选中
            if (!clickedOnDesignControl && !clickedOnConfigPanel)
            {
                DeselectControl();
            }
        }
        
        /// <summary>
        /// 取消选中控件
        /// </summary>
        private void DeselectControl()
        {
            if (!string.IsNullOrEmpty(_selectedControlId) && _controlElements.TryGetValue(_selectedControlId, out var control))
            {
                if (control is Border border)
                {
                    border.BorderBrush = new SolidColorBrush(Colors.Transparent);
                }
            }
            _selectedControlId = null;
            ViewModel?.SelectControl(null);
        }

        private void TestInterface_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] Loaded 事件触发，_controlElements.Count={_controlElements.Count}");
            
            // 订阅测试运行状态事件
            SubscribeToTestRunningEvent();
            
            // 订阅控件属性变更事件（ViewModel通知）
            var eventAggregator = Prism.Ioc.ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            if (eventAggregator != null)
            {
                eventAggregator.GetEvent<ControlPropertyChangedEvent>().Subscribe(OnControlPropertyChanged, ThreadOption.UIThread);
                System.Diagnostics.Debug.WriteLine("[TestInterface] 已订阅 ControlPropertyChangedEvent");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[TestInterface] ERROR: eventAggregator 为 null!");
            }

            // 加载已保存的控件
            LoadSavedControls();
            
            // 如果测试正在运行（复用实例），恢复 UI 轮询
            if (ViewModel?.IsTestRunning == true)
            {
                _isTestRunning = true;
                StartHardwarePolling(100);
                
                // 禁用配置面板
                if (ConfigItemsControl != null)
                {
                    ConfigItemsControl.IsEnabled = false;
                }
            }
        }

        private void OnControlPropertyChanged(ControlPropertyChangedEventArgs e)
        {
            // BoundVariablePath 变更需要立即更新控件的 BoundVariable 属性（不管控件是否被选中）
            if (e.PropertyName == "BoundVariablePath")
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface] OnControlPropertyChanged: ControlId={e.ControlId}, NewValue={e.NewValue}");
                System.Diagnostics.Debug.WriteLine($"[TestInterface] _controlElements 包含 {_controlElements.Count} 个控件: {string.Join(", ", _controlElements.Keys)}");
                
                // 已经在UI线程（ThreadOption.UIThread），直接执行
                if (_controlElements.TryGetValue(e.ControlId, out var controlElement))
                {
                    var innerControl = FindInnerControl(controlElement);
                    System.Diagnostics.Debug.WriteLine($"[TestInterface] FindInnerControl 返回: {innerControl?.GetType().Name ?? "null"}");
                    
                    string newPath = e.NewValue as string;
                    string oldPath = null;
                    
                    if (innerControl is SwitchControl switchCtrl)
                    {
                        oldPath = switchCtrl.BoundVariable;
                        switchCtrl.BoundVariable = newPath;
                        System.Diagnostics.Debug.WriteLine($"[TestInterface] 更新 SwitchControl.BoundVariable: '{oldPath}' -> '{newPath}'");
                        
                        // 更新 _boundSwitches 字典
                        if (!string.IsNullOrEmpty(oldPath) && _boundSwitches.ContainsKey(oldPath))
                        {
                            _boundSwitches.Remove(oldPath);
                        }
                        if (!string.IsNullOrEmpty(newPath))
                        {
                            _boundSwitches[newPath] = switchCtrl;
                        }
                    }
                    else if (innerControl is LampControl lamp)
                    {
                        oldPath = lamp.BoundVariable;
                        lamp.BoundVariable = newPath;
                        System.Diagnostics.Debug.WriteLine($"[TestInterface] 更新 LampControl.BoundVariable: '{oldPath}' -> '{newPath}'");
                        
                        // 更新 _boundLamps 字典
                        if (!string.IsNullOrEmpty(oldPath) && _boundLamps.ContainsKey(oldPath))
                        {
                            _boundLamps.Remove(oldPath);
                        }
                        if (!string.IsNullOrEmpty(newPath))
                        {
                            _boundLamps[newPath] = lamp;
                        }
                    }
                    else if (innerControl is DisplayBoxControl displayBox)
                    {
                        // 更新 _boundDisplayBoxes 字典
                        if (!string.IsNullOrEmpty(displayBox.BoundVariable) && _boundDisplayBoxes.ContainsKey(displayBox.BoundVariable))
                        {
                            var (_, refreshRate, _) = _boundDisplayBoxes[displayBox.BoundVariable];
                            _boundDisplayBoxes.Remove(displayBox.BoundVariable);
                            displayBox.BoundVariable = newPath;
                            if (!string.IsNullOrEmpty(newPath))
                            {
                                _boundDisplayBoxes[newPath] = (displayBox, refreshRate, DateTime.MinValue);
                            }
                        }
                        else
                        {
                            displayBox.BoundVariable = newPath;
                        }
                        // 更新单位
                        var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                        if (controlData != null)
                        {
                            displayBox.Unit = controlData.Unit ?? "";
                            displayBox.DecimalPlaces = controlData.DecimalPlaces > 0 ? controlData.DecimalPlaces : 2;
                        }
                    }
                    else if (innerControl is InputBoxControl inputBox)
                    {
                        inputBox.BoundVariable = newPath;
                        // 更新单位
                        var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                        if (controlData != null)
                        {
                            inputBox.Unit = controlData.Unit ?? "";
                            inputBox.DecimalPlaces = controlData.DecimalPlaces > 0 ? controlData.DecimalPlaces : 2;
                        }
                    }
                    else if (innerControl is CircularGaugeControl gauge)
                    {
                        // 更新 _boundDisplayBoxes 字典（复用DisplayBox的轮询机制）
                        if (!string.IsNullOrEmpty(gauge.BoundVariable) && _boundDisplayBoxes.ContainsKey(gauge.BoundVariable))
                        {
                            var (_, refreshRate, _) = _boundDisplayBoxes[gauge.BoundVariable];
                            _boundDisplayBoxes.Remove(gauge.BoundVariable);
                            gauge.BoundVariable = newPath;
                            if (!string.IsNullOrEmpty(newPath))
                            {
                                _boundDisplayBoxes[newPath] = (gauge, refreshRate, DateTime.MinValue);
                            }
                        }
                        else
                        {
                            gauge.BoundVariable = newPath;
                            if (!string.IsNullOrEmpty(newPath))
                            {
                                var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                                int refreshRate = controlData?.RefreshRate > 0 ? controlData.RefreshRate : 10;
                                _boundDisplayBoxes[newPath] = (gauge, refreshRate, DateTime.MinValue);
                            }
                        }
                        // 更新单位和最大值
                        var controlData2 = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                        if (controlData2 != null)
                        {
                            gauge.Unit = controlData2.Unit ?? "";
                            gauge.MaxValue = controlData2.MaxValue > 0 ? controlData2.MaxValue : 100.0;
                            gauge.DecimalPlaces = controlData2.DecimalPlaces > 0 ? controlData2.DecimalPlaces : 2;
                            gauge.ManualValue = controlData2.ManualValue;
                        }
                    }
                    else if (innerControl is VerticalGaugeControl vGauge)
                    {
                        // 更新 _boundDisplayBoxes 字典（复用DisplayBox的轮询机制）
                        if (!string.IsNullOrEmpty(vGauge.BoundVariable) && _boundDisplayBoxes.ContainsKey(vGauge.BoundVariable))
                        {
                            var (_, refreshRate, _) = _boundDisplayBoxes[vGauge.BoundVariable];
                            _boundDisplayBoxes.Remove(vGauge.BoundVariable);
                            vGauge.BoundVariable = newPath;
                            if (!string.IsNullOrEmpty(newPath))
                            {
                                _boundDisplayBoxes[newPath] = (vGauge, refreshRate, DateTime.MinValue);
                            }
                        }
                        else
                        {
                            vGauge.BoundVariable = newPath;
                            if (!string.IsNullOrEmpty(newPath))
                            {
                                var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                                int refreshRate = controlData?.RefreshRate > 0 ? controlData.RefreshRate : 10;
                                _boundDisplayBoxes[newPath] = (vGauge, refreshRate, DateTime.MinValue);
                            }
                        }
                        // 更新单位和最大值
                        var controlData3 = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                        if (controlData3 != null)
                        {
                            vGauge.Unit = controlData3.Unit ?? "";
                            vGauge.MaxValue = controlData3.MaxValue > 0 ? controlData3.MaxValue : 100.0;
                            vGauge.DecimalPlaces = controlData3.DecimalPlaces > 0 ? controlData3.DecimalPlaces : 2;
                            vGauge.ManualValue = controlData3.ManualValue;
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TestInterface] 未找到控件: ControlId={e.ControlId}");
                }
                return;
            }
            
            // 如果变更的是当前选中的控件，刷新显示
            if (e.ControlId == _selectedControlId)
            {
                // RefreshRate 变更也不需要刷新控件显示
                if (e.PropertyName == "RefreshRate")
                {
                    // RefreshRate 会在下次轮询时生效，暂不处理
                    return;
                }
                
                // DecimalPlaces 变更需要更新控件显示
                if (e.PropertyName == "DecimalPlaces")
                {
                    if (_controlElements.TryGetValue(e.ControlId, out var controlElement))
                    {
                        var innerControl = FindInnerControl(controlElement);
                        if (innerControl is DisplayBoxControl displayBox)
                        {
                            var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                            if (controlData != null)
                            {
                                displayBox.DecimalPlaces = controlData.DecimalPlaces;
                            }
                        }
                        else if (innerControl is InputBoxControl inputBox)
                        {
                            var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                            if (controlData != null)
                            {
                                inputBox.DecimalPlaces = controlData.DecimalPlaces;
                            }
                        }
                        else if (innerControl is CircularGaugeControl gauge)
                        {
                            var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                            if (controlData != null)
                            {
                                gauge.DecimalPlaces = controlData.DecimalPlaces;
                            }
                        }
                        else if (innerControl is VerticalGaugeControl vGauge)
                        {
                            var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                            if (controlData != null)
                            {
                                vGauge.DecimalPlaces = controlData.DecimalPlaces;
                            }
                        }
                    }
                    return;
                }
                
                // MaxValue、Unit 和 ManualValue 变更需要更新仪表控件
                if (e.PropertyName == "MaxValue" || e.PropertyName == "Unit" || e.PropertyName == "ManualValue")
                {
                    if (_controlElements.TryGetValue(e.ControlId, out var controlElement))
                    {
                        var innerControl = FindInnerControl(controlElement);
                        var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == e.ControlId);
                        if (controlData != null)
                        {
                            if (innerControl is CircularGaugeControl gauge)
                            {
                                if (e.PropertyName == "MaxValue" && double.TryParse(e.NewValue?.ToString(), out double maxValue) && maxValue > 0)
                                {
                                    gauge.MaxValue = maxValue;
                                }
                                else if (e.PropertyName == "Unit")
                                {
                                    gauge.Unit = controlData.Unit ?? "";
                                }
                                else if (e.PropertyName == "ManualValue")
                                {
                                    gauge.ManualValue = controlData.ManualValue;
                                    if (controlData.ManualValue.HasValue)
                                    {
                                        gauge.Value = controlData.ManualValue.Value;
                                    }
                                }
                            }
                            else if (innerControl is VerticalGaugeControl vGauge)
                            {
                                if (e.PropertyName == "MaxValue" && double.TryParse(e.NewValue?.ToString(), out double maxValue) && maxValue > 0)
                                {
                                    vGauge.MaxValue = maxValue;
                                }
                                else if (e.PropertyName == "Unit")
                                {
                                    vGauge.Unit = controlData.Unit ?? "";
                                }
                                else if (e.PropertyName == "ManualValue")
                                {
                                    vGauge.ManualValue = controlData.ManualValue;
                                    if (controlData.ManualValue.HasValue)
                                    {
                                        vGauge.Value = controlData.ManualValue.Value;
                                    }
                                }
                            }
                        }
                    }
                    return;
                }
                
                // 其他属性变更才需要刷新控件显示
                Dispatcher.Invoke(RefreshSelectedControl);
            }
        }
        
        /// <summary>
        /// 查找控件容器内的实际控件
        /// </summary>
        private FrameworkElement FindInnerControl(FrameworkElement container)
        {
            if (container is Border border)
            {
                // 结构可能是: Border -> StackPanel -> [TextBlock, 实际控件]
                // 或者: Border -> Grid -> 实际控件
                Panel panel = border.Child as Panel;
                if (panel != null)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is SwitchControl || child is LampControl || 
                            child is DisplayBoxControl || child is InputBoxControl ||
                            child is ButtonControl || child is CircularGaugeControl ||
                            child is TextBox)
                        {
                            return child as FrameworkElement;
                        }
                        // Button 包装在另一个 Border 中
                        if (child is Border innerBorder && innerBorder.Child is ButtonControl btn)
                        {
                            return btn;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 加载已保存的控件
        /// </summary>
        private void LoadSavedControls()
        {
            if (ViewModel?.Controls == null) return;
            
            // 如果已经加载过控件，不重复加载（复用实例情况）
            if (_controlElements.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine("[TestInterface] 控件已加载，跳过重复加载");
                return;
            }

            foreach (var controlData in ViewModel.Controls)
            {
                var control = RestoreControl(controlData);
                if (control != null)
                {
                    Canvas.SetLeft(control, controlData.PositionX);
                    Canvas.SetTop(control, controlData.PositionY);
                    DesignCanvas.Children.Insert(DesignCanvas.Children.Count - 1, control);
                    _controlElements[controlData.Id] = control;
                }
            }

            // 更新各类型计数器
            _controlCounters.Clear();
            foreach (var control in ViewModel.Controls)
            {
                var type = control.ControlType;
                if (!_controlCounters.ContainsKey(type))
                    _controlCounters[type] = 0;
                _controlCounters[type]++;
            }
        }

        /// <summary>
        /// 根据保存的数据恢复控件
        /// </summary>
        private FrameworkElement RestoreControl(TestInterfaceControlItem controlData)
        {
            // 使用统一的 CreateDesignControl 方法
            return CreateDesignControl(controlData);
        }

        #region 工具箱拖动开始

        /// <summary>
        /// 工具箱控件鼠标按下 - 开始拖动
        /// </summary>
        private void ToolItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string controlType)
            {
                _currentDragControlType = controlType;
                
                // 设置预览控件内容
                UpdateDragPreview(controlType);
                
                // 开始拖放操作
                var dragData = new DataObject();
                dragData.SetData("ControlType", controlType);
                DragDrop.DoDragDrop(element, dragData, DragDropEffects.Copy);
                
                // 拖放结束后隐藏预览
                DragPreview.Visibility = Visibility.Collapsed;
                _currentDragControlType = null;
            }
        }

        /// <summary>
        /// 根据控件类型更新拖动预览
        /// </summary>
        private void UpdateDragPreview(string controlType)
        {
            DragPreview.Child = CreateControlPreview(controlType);
            DragPreview.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 创建控件预览
        /// </summary>
        private FrameworkElement CreateControlPreview(string controlType)
        {
            switch (controlType)
            {
                case "Button":
                    return new ButtonControl { Text = "按钮" };

                case "Switch":
                    return new SwitchControl();

                case "Indicator":
                    return new LampControl();

                case "TextLabel":
                    return new TextLabelControl { Text = "标签" };

                case "DisplayBox":
                    return new DisplayBoxControl { ControlName = "显示框", Value = 0.00 };

                case "InputBox":
                    return new InputBoxControl { ControlName = "输入框", Value = 0.00 };

                case "CircularGauge":
                    return new CircularGaugeControl { ControlName = "环形仪表", Value = 0.00, MaxValue = 100.0 };

                case "VerticalGauge":
                    return new VerticalGaugeControl { ControlName = "竖形仪表", Value = 0.00, MaxValue = 100.0 };

                case "Label": // 兼容旧版
                    return new DisplayBoxControl { ControlName = "显示框", Value = 0.00 };

                default:
                    return new TextBlock { Text = controlType };
            }
        }

        #endregion

        #region Canvas 拖放事件

        /// <summary>
        /// 拖入Canvas区域
        /// </summary>
        private void DesignCanvas_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ControlType"))
            {
                e.Effects = DragDropEffects.Copy;
                DragPreview.Visibility = Visibility.Visible;
                
                // 更新预览位置
                var pos = e.GetPosition(DesignCanvas);
                Canvas.SetLeft(DragPreview, pos.X - DragPreview.ActualWidth / 2);
                Canvas.SetTop(DragPreview, pos.Y - DragPreview.ActualHeight / 2);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// 在Canvas区域拖动 - 更新预览位置
        /// </summary>
        private void DesignCanvas_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ControlType"))
            {
                e.Effects = DragDropEffects.Copy;
                
                // 实时更新预览位置跟随鼠标
                var pos = e.GetPosition(DesignCanvas);
                
                // 获取预览控件的实际尺寸
                DragPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var previewWidth = DragPreview.DesiredSize.Width;
                var previewHeight = DragPreview.DesiredSize.Height;
                
                // 居中显示在鼠标位置
                Canvas.SetLeft(DragPreview, pos.X - previewWidth / 2);
                Canvas.SetTop(DragPreview, pos.Y - previewHeight / 2);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// 拖出Canvas区域
        /// </summary>
        private void DesignCanvas_DragLeave(object sender, DragEventArgs e)
        {
            // 检查是否真的离开了Canvas区域
            var pos = e.GetPosition(DesignCanvas);
            if (pos.X < 0 || pos.Y < 0 || pos.X > DesignCanvas.ActualWidth || pos.Y > DesignCanvas.ActualHeight)
            {
                DragPreview.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }

        /// <summary>
        /// 在Canvas上放置控件
        /// </summary>
        private void DesignCanvas_Drop(object sender, DragEventArgs e)
        {
            // 测试运行时禁止添加控件
            if (_isTestRunning) return;
            
            if (e.Data.GetDataPresent("ControlType"))
            {
                var controlType = e.Data.GetData("ControlType") as string;
                var pos = e.GetPosition(DesignCanvas);
                
                // 隐藏预览
                DragPreview.Visibility = Visibility.Collapsed;
                
                // 直接使用默认参数创建控件（不弹出对话框）
                if (!_controlCounters.ContainsKey(controlType))
                    _controlCounters[controlType] = 0;
                _controlCounters[controlType]++;
                var defaultName = GetDefaultControlName(controlType);
                
                // 创建控件数据（默认参数，不绑定数据源）
                // 必须在此处生成唯一ID，确保 _controlElements 字典的键与 ViewModel 中的 ControlId 一致
                var controlData = new TestInterfaceControlItem
                {
                    Id = Guid.NewGuid().ToString("N"),  // 生成唯一ID
                    Name = defaultName,
                    ControlType = controlType,
                    BackgroundColor = "#e8ebed",
                    TextColor = "#000000"
                    // 数据源默认为空，需要用户在下方配置面板中选择
                };

                // 创建实际控件并放置
                var control = CreateDesignControl(controlData);
                if (control != null)
                {
                    // 居中放置
                    control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var left = pos.X - control.DesiredSize.Width / 2;
                    var top = pos.Y - control.DesiredSize.Height / 2;
                    
                    // 确保不超出边界
                    left = Math.Max(0, Math.Min(left, DesignCanvas.ActualWidth - control.DesiredSize.Width));
                    top = Math.Max(0, Math.Min(top, DesignCanvas.ActualHeight - control.DesiredSize.Height));
                    
                    Canvas.SetLeft(control, left);
                    Canvas.SetTop(control, top);
                    
                    // 保存位置到数据
                    controlData.PositionX = left;
                    controlData.PositionY = top;
                    
                    // 添加到Canvas（在预览控件之前）
                    DesignCanvas.Children.Insert(DesignCanvas.Children.Count - 1, control);
                    
                    // 保存控件数据
                    _controlElements[controlData.Id] = control;
                    ViewModel?.AddControl(controlData);
                    
                    // 选中新创建的控件
                    SelectControl(controlData.Id);
                }
            }
            e.Handled = true;
        }
        
        /// <summary>
        /// 获取默认控件名称
        /// </summary>
        private string GetDefaultControlName(string controlType)
        {
            string prefix = controlType switch
            {
                "Button" => "按钮",
                "Switch" => "开关",
                "Indicator" => "指示灯",
                "TextLabel" => "标签",
                "DisplayBox" => "显示框",
                "InputBox" => "输入框",
                "Label" => "显示框", // 兼容旧版
                _ => controlType
            };
            int count = _controlCounters.ContainsKey(controlType) ? _controlCounters[controlType] : 1;
            return $"{prefix}{count}";
        }
        
        /// <summary>
        /// 获取可用的变量列表（从当前测试任务的变量表获取）
        /// </summary>
        private List<VariableItem> GetAvailableVariables()
        {
            var variables = new List<VariableItem>();
            
            if (string.IsNullOrEmpty(ViewModel?.TestTaskName))
            {
                return variables;
            }

            // 从 SignalConfigTabelViewModel 的静态方法获取所有信号配置表数据
            var allSignalTabelItems = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allSignalTabelItems == null || allSignalTabelItems.Count == 0)
            {
                return variables;
            }

            // 遍历当前测试任务下的所有信号配置表
            var taskName = ViewModel.TestTaskName;
            foreach (var kvp in allSignalTabelItems)
            {
                // 只获取当前测试任务下的信号配置表
                if (kvp.Key.StartsWith($"{taskName}/"))
                {
                    foreach (var signal in kvp.Value)
                    {
                        if (signal.IsEmpty || string.IsNullOrEmpty(signal.SignalName))
                            continue;

                        variables.Add(new VariableItem
                        {
                            Name = signal.SignalName,
                            Type = signal.SignalType ?? "模拟量",
                            // 数字量没有实时值单位
                            Unit = signal.SignalType == "数字量" ? "" : signal.RealTimeValueUnit,
                            FullPath = $"{kvp.Key}/{signal.SignalName}"
                        });
                    }
                }
            }

            return variables;
        }

        /// <summary>
        /// 创建可拖动的设计器控件
        /// </summary>
        private FrameworkElement CreateDesignControl(TestInterfaceControlItem controlData)
        {
            FrameworkElement innerControl;
            bool showNameLabel = true; // 是否显示上方的控件名称
            
            // 解析颜色
            var bgColor = ParseColor(controlData.BackgroundColor, Color.FromRgb(0xe8, 0xeb, 0xed));
            var textColor = ParseColor(controlData.TextColor, Colors.Black);
            
            switch (controlData.ControlType)
            {
                case "Button":
                    var button = new ButtonControl 
                    { 
                        Text = controlData.ButtonText ?? controlData.Name,
                        BoundVariable = controlData.BoundVariablePath,
                        BackgroundColor = bgColor,
                        TextColor = textColor,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    button.Cursor = Cursors.Hand;
                    // 为按钮添加外层透明 Border，方便选中
                    var buttonWrapper = new Border
                    {
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Child = button,
                        MinWidth = 100,
                        MinHeight = 50
                    };
                    innerControl = buttonWrapper;
                    showNameLabel = false; // 按钮不需要上方名称
                    break;

                case "Switch":
                    var switchCtrl = new SwitchControl
                    {
                        BoundVariable = controlData.BoundVariablePath
                    };
                    switchCtrl.Cursor = Cursors.Hand;
                    // 订阅开关状态变化事件，用于硬件控制
                    switchCtrl.SwitchChanged += OnSwitchChanged;
                    // 注册到绑定字典
                    if (!string.IsNullOrEmpty(controlData.BoundVariablePath))
                    {
                        _boundSwitches[controlData.BoundVariablePath] = switchCtrl;
                    }
                    innerControl = switchCtrl;
                    break;

                case "Indicator":
                    var lamp = new LampControl
                    {
                        BoundVariable = controlData.BoundVariablePath
                    };
                    // 指示灯只用于展示状态，不可点击
                    lamp.Cursor = Cursors.Arrow;
                    lamp.IsHitTestVisible = false;
                    // 注册到绑定字典，用于硬件轮询更新
                    if (!string.IsNullOrEmpty(controlData.BoundVariablePath))
                    {
                        _boundLamps[controlData.BoundVariablePath] = lamp;
                    }
                    innerControl = lamp;
                    break;

                case "TextLabel":
                    var textLabel = new TextLabelControl
                    {
                        Text = controlData.ButtonText ?? controlData.Name,
                        TextColor = textColor
                    };
                    innerControl = textLabel;
                    showNameLabel = false; // 标签本身就是文字，不需要上方名称
                    break;

                case "DisplayBox":
                    var displayBox = new DisplayBoxControl
                    {
                        ControlName = controlData.Name,
                        Value = 0.00,
                        Unit = controlData.Unit ?? "",
                        BoundVariable = controlData.BoundVariablePath,
                        BackgroundColor = bgColor,
                        TextColor = textColor,
                        DecimalPlaces = controlData.DecimalPlaces > 0 ? controlData.DecimalPlaces : 2
                    };
                    // 注册到绑定字典，用于硬件轮询更新（按刷新频率）
                    if (!string.IsNullOrEmpty(controlData.BoundVariablePath))
                    {
                        int refreshRate = controlData.RefreshRate > 0 ? controlData.RefreshRate : 10;
                        _boundDisplayBoxes[controlData.BoundVariablePath] = (displayBox, refreshRate, DateTime.MinValue);
                    }
                    innerControl = displayBox;
                    showNameLabel = false; // DisplayBox 内部已包含名称
                    break;

                case "InputBox":
                    var inputBox = new InputBoxControl
                    {
                        ControlName = controlData.Name,
                        Value = 0.00,
                        Unit = controlData.Unit ?? "",
                        BoundVariable = controlData.BoundVariablePath,
                        BackgroundColor = bgColor,
                        TextColor = textColor,
                        DecimalPlaces = controlData.DecimalPlaces > 0 ? controlData.DecimalPlaces : 2
                    };
                    innerControl = inputBox;
                    showNameLabel = false; // InputBox 内部已包含名称
                    break;

                case "Label": // 兼容旧版
                    var legacyLabel = new DisplayBoxControl
                    {
                        ControlName = controlData.Name,
                        Value = 0.00,
                        Unit = controlData.Unit ?? "",
                        BoundVariable = controlData.BoundVariablePath,
                        BackgroundColor = bgColor,
                        TextColor = textColor
                    };
                    innerControl = legacyLabel;
                    showNameLabel = false;
                    break;

                case "CircularGauge":
                    var circularGauge = new CircularGaugeControl
                    {
                        ControlName = controlData.Name,
                        Value = controlData.ManualValue ?? 0.00,
                        MaxValue = controlData.MaxValue > 0 ? controlData.MaxValue : 100.0,
                        Unit = controlData.Unit ?? "",
                        BoundVariable = controlData.BoundVariablePath,
                        DecimalPlaces = controlData.DecimalPlaces > 0 ? controlData.DecimalPlaces : 2,
                        ManualValue = controlData.ManualValue
                    };
                    // 注册到绑定字典，用于硬件轮询更新（按刷新频率）
                    if (!string.IsNullOrEmpty(controlData.BoundVariablePath))
                    {
                        int refreshRate = controlData.RefreshRate > 0 ? controlData.RefreshRate : 10;
                        _boundDisplayBoxes[controlData.BoundVariablePath] = (circularGauge, refreshRate, DateTime.MinValue);
                    }
                    innerControl = circularGauge;
                    showNameLabel = false; // CircularGauge 内部已包含名称
                    break;

                case "VerticalGauge":
                    var verticalGauge = new VerticalGaugeControl
                    {
                        ControlName = controlData.Name,
                        Value = controlData.ManualValue ?? 0.00,
                        MaxValue = controlData.MaxValue > 0 ? controlData.MaxValue : 100.0,
                        Unit = controlData.Unit ?? "",
                        BoundVariable = controlData.BoundVariablePath,
                        DecimalPlaces = controlData.DecimalPlaces > 0 ? controlData.DecimalPlaces : 2,
                        ManualValue = controlData.ManualValue
                    };
                    // 注册到绑定字典，用于硬件轮询更新（按刷新频率）
                    if (!string.IsNullOrEmpty(controlData.BoundVariablePath))
                    {
                        int refreshRate = controlData.RefreshRate > 0 ? controlData.RefreshRate : 10;
                        _boundDisplayBoxes[controlData.BoundVariablePath] = (verticalGauge, refreshRate, DateTime.MinValue);
                    }
                    innerControl = verticalGauge;
                    showNameLabel = false; // VerticalGauge 内部已包含名称
                    break;

                default:
                    innerControl = new TextBlock { Text = controlData.ControlType };
                    break;
            }

            // 使用 StackPanel 包装（内容容器）
            var controlStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // 只有开关和指示灯需要上方显示名称
            if (showNameLabel)
            {
                var nameLabel = new TextBlock
                {
                    Text = controlData.Name,
                    FontSize = 20,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                controlStack.Children.Add(nameLabel);
            }
            controlStack.Children.Add(innerControl);

            // 使用 Viewbox 让内部控件跟随外层大小等比缩放
            var viewBox = new Viewbox
            {
                Stretch = Stretch.Fill,
                Child = controlStack
            };

            // 外层包装，支持选中和拖动
            var wrapper = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                Child = viewBox,
                Tag = controlData.Id,
                Cursor = Cursors.SizeAll,
                Width = controlData.Width > 0 ? controlData.Width : double.NaN,
                Height = controlData.Height > 0 ? controlData.Height : double.NaN
            };

            // 添加右键菜单（使用统一的样式）
            var contextMenu = new ContextMenu();
            var contextMenuStyle = Application.Current.TryFindResource("CustomContextMenuStyle") as Style;
            if (contextMenuStyle != null)
            {
                contextMenu.Style = contextMenuStyle;
            }
            var deleteMenuItem = new MenuItem { Header = "删除控件" };
            deleteMenuItem.Click += (s, e) => DeleteControl(controlData.Id);
            contextMenu.Items.Add(deleteMenuItem);
            wrapper.ContextMenu = contextMenu;

            // 添加拖动和选中支持
            AddDragSupport(wrapper, controlData.Id);
            
            // 添加大小调整支持
            AddResizeSupport(wrapper, controlData.Id);

            return wrapper;
        }
        
        /// <summary>
        /// 解析颜色字符串
        /// </summary>
        private Color ParseColor(string colorStr, Color defaultColor)
        {
            if (string.IsNullOrEmpty(colorStr)) return defaultColor;
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorStr);
            }
            catch
            {
                return defaultColor;
            }
        }
        
        /// <summary>
        /// 当前选中的控件ID
        /// </summary>
        private string _selectedControlId;
        
        /// <summary>
        /// 选中控件
        /// </summary>
        private void SelectControl(string controlId)
        {
            // 取消之前的选中状态
            if (!string.IsNullOrEmpty(_selectedControlId) && _controlElements.TryGetValue(_selectedControlId, out var prevControl))
            {
                if (prevControl is Border prevBorder)
                {
                    prevBorder.BorderBrush = Brushes.Transparent;
                }
            }
            
            // 设置新的选中状态
            _selectedControlId = controlId;
            if (_controlElements.TryGetValue(controlId, out var control))
            {
                if (control is Border border)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                }
            }
            
            // 通知 ViewModel 更新下方配置面板
            var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == controlId);
            ViewModel?.SelectControl(controlData);
        }

        /// <summary>
        /// 根据当前选中的控件，控制所有控件右下角调整手柄的显隐
        /// </summary>
        private void UpdateResizeHandlesVisibility(string selectedControlId)
        {
            foreach (var kvp in _controlElements)
            {
                if (kvp.Value is not Border border) continue;

                Grid grid = border.Child as Grid;
                if (grid == null) continue;

                foreach (var handle in grid.Children.OfType<Border>()
                             .Where(b => b.Cursor == Cursors.SizeNWSE))
                {
                    handle.Visibility = kvp.Key == selectedControlId
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }
        
        /// <summary>
        /// 删除控件
        /// </summary>
        private void DeleteControl(string controlId)
        {
            if (string.IsNullOrEmpty(controlId)) return;
            
            // 确认删除
            var result = ReMessageBox.Show("确定要删除此控件吗？", "确认删除", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;
            
            // 从画布中移除控件
            if (_controlElements.TryGetValue(controlId, out var element))
            {
                DesignCanvas.Children.Remove(element);
                _controlElements.Remove(controlId);
            }
            
            // 从 ViewModel 中移除
            ViewModel?.DeleteControl(controlId);
            
            // 如果删除的是当前选中的控件，清除选中状态
            if (_selectedControlId == controlId)
            {
                _selectedControlId = null;
                ViewModel?.ClearSelection();
            }
        }

        /// <summary>
        /// 为控件添加拖动支持（放置后可继续拖动调整位置）
        /// </summary>
        private void AddDragSupport(FrameworkElement element, string controlId)
        {
            bool isDragging = false;
            bool hasMoved = false;
            Point startPoint = new Point();
            double startLeft = 0, startTop = 0;

            element.MouseLeftButtonDown += (s, e) =>
            {
                // 测试运行时禁止拖动
                if (_isTestRunning) return;

                // 在缩放区域内按下时，不启动拖动（交给缩放逻辑）
                var posInElement = e.GetPosition(element);
                if (IsInResizeZone(element, posInElement))
                {
                    return;
                }
                
                isDragging = true;
                hasMoved = false;
                startPoint = e.GetPosition(DesignCanvas);
                startLeft = Canvas.GetLeft(element);
                startTop = Canvas.GetTop(element);
                element.CaptureMouse();
                e.Handled = true;
            };

            element.MouseMove += (s, e) =>
            {
                if (isDragging)
                {
                    var currentPos = e.GetPosition(DesignCanvas);
                    var offsetX = currentPos.X - startPoint.X;
                    var offsetY = currentPos.Y - startPoint.Y;
                    
                    // 检测是否真的移动了
                    if (Math.Abs(offsetX) > 3 || Math.Abs(offsetY) > 3)
                    {
                        hasMoved = true;
                    }
                    
                    var newLeft = startLeft + offsetX;
                    var newTop = startTop + offsetY;
                    
                    // 边界检查
                    newLeft = Math.Max(0, Math.Min(newLeft, DesignCanvas.ActualWidth - element.ActualWidth));
                    newTop = Math.Max(0, Math.Min(newTop, DesignCanvas.ActualHeight - element.ActualHeight));
                    
                    Canvas.SetLeft(element, newLeft);
                    Canvas.SetTop(element, newTop);
                }
            };

            element.MouseLeftButtonUp += (s, e) =>
            {
                if (isDragging)
                {
                    isDragging = false;
                    element.ReleaseMouseCapture();
                    
                    // 如果移动了，保存新位置
                    if (hasMoved)
                    {
                        var newLeft = Canvas.GetLeft(element);
                        var newTop = Canvas.GetTop(element);
                        ViewModel?.UpdateControlPosition(controlId, newLeft, newTop);
                    }
                    
                    // 无论是否移动，点击都选中控件
                    SelectControl(controlId);
                    
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// 为控件添加大小调整支持（拖拽边框/右下角区域调整大小，类似窗口缩放）
        /// </summary>
        private void AddResizeSupport(FrameworkElement element, string controlId)
        {
            // 测试运行时禁止调整大小
            if (_isTestRunning) return;

            bool isResizing = false;
            ResizeDirection resizeDirection = ResizeDirection.None;
            Point startPoint = new Point();
            double startWidth = 0, startHeight = 0;

            element.MouseMove += (s, e) =>
            {
                if (_isTestRunning) return;

                var pos = e.GetPosition(element);

                // 如果正在缩放，更新尺寸
                if (isResizing)
                {
                    var currentPoint = e.GetPosition(DesignCanvas);
                    var deltaX = currentPoint.X - startPoint.X;
                    var deltaY = currentPoint.Y - startPoint.Y;

                    double newWidth = element.Width;
                    double newHeight = element.Height;

                    if (resizeDirection.HasFlag(ResizeDirection.Right))
                    {
                        newWidth = Math.Max(50, startWidth + deltaX);
                    }
                    if (resizeDirection.HasFlag(ResizeDirection.Bottom))
                    {
                        newHeight = Math.Max(30, startHeight + deltaY);
                    }

                    element.Width = newWidth;
                    element.Height = newHeight;

                    e.Handled = true;
                }
                else
                {
                    // 根据鼠标位置更新光标形状（仅在未缩放时）
                    var dir = GetResizeDirection(element, pos);
                    if (dir == ResizeDirection.BottomRight)
                    {
                        element.Cursor = Cursors.SizeNWSE;
                    }
                    else if (dir == ResizeDirection.Right)
                    {
                        element.Cursor = Cursors.SizeWE;
                    }
                    else if (dir == ResizeDirection.Bottom)
                    {
                        element.Cursor = Cursors.SizeNS;
                    }
                    else
                    {
                        element.Cursor = Cursors.Arrow;
                    }
                }
            };

            element.MouseLeftButtonDown += (s, e) =>
            {
                if (_isTestRunning) return;

                var pos = e.GetPosition(element);
                var dir = GetResizeDirection(element, pos);
                if (dir == ResizeDirection.None)
                {
                    return;
                }

                isResizing = true;
                resizeDirection = dir;
                startPoint = e.GetPosition(DesignCanvas);
                startWidth = element.Width > 0 ? element.Width : element.ActualWidth;
                startHeight = element.Height > 0 ? element.Height : element.ActualHeight;
                element.CaptureMouse();
                e.Handled = true;
            };

            element.MouseLeftButtonUp += (s, e) =>
            {
                if (!isResizing) return;

                isResizing = false;
                resizeDirection = ResizeDirection.None;
                element.ReleaseMouseCapture();

                // 保存新大小
                ViewModel?.UpdateControlSize(controlId, element.Width, element.Height);
                e.Handled = true;
            };
        }

        /// <summary>
        /// 判断某个点是否在控件的缩放区域（右边缘 / 下边缘 / 右下角）
        /// </summary>
        private bool IsInResizeZone(FrameworkElement element, Point posInElement)
        {
            const double margin = 6.0;

            var w = element.ActualWidth;
            var h = element.ActualHeight;
            if (w <= 0 || h <= 0) return false;

            bool onRight = posInElement.X >= w - margin && posInElement.X <= w;
            bool onBottom = posInElement.Y >= h - margin && posInElement.Y <= h;

            return onRight || onBottom;
        }

        [Flags]
        private enum ResizeDirection
        {
            None = 0,
            Right = 1,
            Bottom = 2,
            BottomRight = Right | Bottom
        }

        /// <summary>
        /// 根据鼠标在元素中的位置判断缩放方向
        /// </summary>
        private ResizeDirection GetResizeDirection(FrameworkElement element, Point posInElement)
        {
            const double margin = 6.0;

            var w = element.ActualWidth;
            var h = element.ActualHeight;
            if (w <= 0 || h <= 0) return ResizeDirection.None;

            bool onRight = posInElement.X >= w - margin && posInElement.X <= w;
            bool onBottom = posInElement.Y >= h - margin && posInElement.Y <= h;

            if (onRight && onBottom) return ResizeDirection.BottomRight;
            if (onRight) return ResizeDirection.Right;
            if (onBottom) return ResizeDirection.Bottom;
            return ResizeDirection.None;
        }

        #endregion

        #region 窗口标题栏事件（保留原有功能）

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 原有标题栏拖动逻辑
        }

        private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            // 原有最小化逻辑
        }

        private void OnFloatButtonClick(object sender, RoutedEventArgs e)
        {
            // 原有浮动逻辑
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            // 原有关闭逻辑
        }

        #endregion

        #region 控件配置面板事件

        /// <summary>
        /// 配置文本框失去焦点
        /// </summary>
        private void ConfigTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is Models.ControlConfigItem configItem)
            {
                ViewModel?.UpdateControlProperty(configItem.PropertyName, configItem.Value);
                RefreshSelectedControl();
            }
        }

        /// <summary>
        /// 配置颜色选择器点击
        /// </summary>
        private void ConfigColorPicker_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is Models.ControlConfigItem configItem)
            {
                var currentColor = configItem.Value ?? "#e8ebed";
                var dialog = new Dialogs.ColorPickerDialog(currentColor);
                dialog.Owner = Window.GetWindow(this);
                if (dialog.ShowDialog() == true)
                {
                    configItem.Value = dialog.SelectedColor;
                    ViewModel?.UpdateControlProperty(configItem.PropertyName, dialog.SelectedColor);
                    RefreshSelectedControl();
                }
            }
        }
        /// <summary>
        /// 配置下拉框选择变化（数据源选择）
        /// </summary>
        private void ConfigComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 忽略由 DataContext 变化引起的空选择
            if (e.AddedItems.Count == 0) return;
            
            if (sender is ComboBox comboBox && comboBox.DataContext is Models.ControlConfigItem configItem)
            {
                var selectedName = comboBox.SelectedItem as string;
                if (!string.IsNullOrEmpty(selectedName))
                {
                    // 双向绑定已经更新了 configItem.Value，这里同步到 SelectedControl
                    ViewModel?.UpdateControlProperty(configItem.PropertyName, selectedName);
                }
            }
        }

        /// <summary>
        /// 简单下拉框选择变化（用于刷新频率等固定选项）
        /// </summary>
        private void ConfigSimpleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.DataContext is Models.ControlConfigItem configItem)
            {
                if (comboBox.SelectedItem is string selectedValue)
                {
                    // 从 "10 Hz" 提取数字 "10"
                    string numericValue = selectedValue.Replace(" Hz", "").Trim();
                    configItem.Value = numericValue;
                    
                    Debug.WriteLine($"[SimpleComboBox] 选择变化: {configItem.PropertyName} = {numericValue}");
                    
                    // 更新控件属性
                    ViewModel?.UpdateControlProperty(configItem.PropertyName, numericValue);
                }
            }
        }

        /// <summary>
        /// 刷新选中控件的显示
        /// </summary>
        private void RefreshSelectedControl()
        {
            if (string.IsNullOrEmpty(_selectedControlId)) return;
            
            var controlData = ViewModel?.Controls?.FirstOrDefault(c => c.Id == _selectedControlId);
            if (controlData == null) return;
            
            // 移除旧控件
            if (_controlElements.TryGetValue(_selectedControlId, out var oldControl))
            {
                DesignCanvas.Children.Remove(oldControl);
            }
            
            // 创建新控件
            var newControl = CreateDesignControl(controlData);
            Canvas.SetLeft(newControl, controlData.PositionX);
            Canvas.SetTop(newControl, controlData.PositionY);
            DesignCanvas.Children.Insert(DesignCanvas.Children.Count - 1, newControl);
            _controlElements[_selectedControlId] = newControl;
            
            // 保持选中状态
            if (newControl is Border border)
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            }
        }

        #endregion

        #region 硬件控制（UI 轮询，从 HardwareControlService 读取值更新控件）

        /// <summary>
        /// 启动 UI 轮询（从 HardwareControlService 读取变量值更新控件）
        /// </summary>
        /// <param name="intervalMs">轮询间隔（毫秒）</param>
        public void StartHardwarePolling(int intervalMs = 100)
        {
            if (_hardwarePollingTimer != null)
            {
                _hardwarePollingTimer.Stop();
            }

            _hardwarePollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };
            _hardwarePollingTimer.Tick += HardwarePollingTimer_Tick;
            _hardwarePollingTimer.Start();

            Debug.WriteLine($"[TestInterface] 硬件轮询已启动，间隔: {intervalMs}ms");
        }

        /// <summary>
        /// 停止硬件轮询
        /// </summary>
        public void StopHardwarePolling()
        {
            _hardwarePollingTimer?.Stop();
            _hardwarePollingTimer = null;
            Debug.WriteLine("[TestInterface] 硬件轮询已停止");
        }

        /// <summary>
        /// 硬件轮询定时器回调（从 HardwareControlService 读取变量值更新 UI 控件）
        /// </summary>
        private void HardwarePollingTimer_Tick(object sender, EventArgs e)
        {
            var hardwareService = Services.HardwareControlService.Instance;
            System.Diagnostics.Debug.WriteLine($"[TestInterface.HardwarePollingTimer_Tick] 硬件服务运行状态: {hardwareService.IsRunning}, 绑定指示灯数量: {_boundLamps.Count}");

            // 只在硬件服务运行时才进行轮询
            if (!hardwareService.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("[TestInterface.HardwarePollingTimer_Tick] 硬件服务未运行，跳过轮询");
                return;
            }

            try
            {
                // 轮询所有绑定的指示灯（DI 数字输入）
                foreach (var kvp in _boundLamps)
                {
                    string variablePath = kvp.Key;
                    LampControl lamp = kvp.Value;

                    // 从 HardwareControlService 获取变量值
                    double value = hardwareService.GetVariableValue(variablePath);
                    System.Diagnostics.Debug.WriteLine($"[TestInterface.HardwarePollingTimer_Tick] 获取变量值 {variablePath} = {value}");

                    // 更新指示灯状态
                    lamp.SetValue((int)value);
                }

                // 注意：开关（DO 数字输出）不需要轮询
                // DO/AO 是由测试界面控制写入到板卡的，用户点击开关 → 写入 DO

                // 轮询所有绑定的显示框和环形仪表（AI 模拟输入，按刷新频率更新）
                var now = DateTime.Now;
                var keysToUpdate = _boundDisplayBoxes.Keys.ToList();
                foreach (var variablePath in keysToUpdate)
                {
                    var (control, refreshRate, lastUpdate) = _boundDisplayBoxes[variablePath];
                    
                    // 计算刷新间隔（毫秒）
                    double intervalMs = 1000.0 / refreshRate;
                    
                    // 检查是否到了更新时间
                    if ((now - lastUpdate).TotalMilliseconds < intervalMs)
                        continue;

                    // 从 HardwareControlService 获取变量值
                    double value = hardwareService.GetVariableValue(variablePath);
                    
                    // 更新控件数值
                    if (control is DisplayBoxControl displayBox)
                    {
                        displayBox.Value = value;
                    }
                    else if (control is CircularGaugeControl gauge)
                    {
                        gauge.Value = value;
                    }
                    
                    // 更新上次刷新时间
                    _boundDisplayBoxes[variablePath] = (control, refreshRate, now);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestInterface] 硬件轮询异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 开关状态变化事件处理
        /// </summary>
        private async void OnSwitchChanged(object sender, SwitchChangedEventArgs e)
        {
            Debug.WriteLine($"[TestInterface] 开关状态变化: {e.VariableName} = {e.Value}");

            var hardwareService = Services.HardwareControlService.Instance;
            
            if (!hardwareService.IsRunning)
            {
                Debug.WriteLine("[TestInterface] 硬件服务未运行，无法写入");
                return;
            }

            try
            {
                // 使用 HardwareControlService 设置变量值（会自动写入对应通道）
                bool success = await hardwareService.SetVariableValueAsync(e.VariableName, e.Value);
                Debug.WriteLine($"[TestInterface] 写入变量 {e.VariableName} = {e.Value}, 结果: {success}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestInterface] 写入硬件通道异常: {ex.Message}");
            }
        }

        #endregion
    }
}
