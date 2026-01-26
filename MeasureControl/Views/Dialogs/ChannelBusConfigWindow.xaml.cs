using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Drivers.PXI4004CAN;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MeasureControl.Views.Dialogs
{
    public partial class ChannelBusConfigWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        #region 属性

        private string _nAccCodeA = "00000000";
        public string nAccCodeA
        {
            get => _nAccCodeA;
            set
            {
                if (_nAccCodeA != value)
                {
                    _nAccCodeA = value;
                    OnPropertyChanged(nameof(nAccCodeA));
                    UpdateUIFromAcceptanceCodes();
                }
            }
        }

        private string _nAccMaskA = "FFFFFFFF";
        public string nAccMaskA
        {
            get => _nAccMaskA;
            set
            {
                if (_nAccMaskA != value)
                {
                    _nAccMaskA = value;
                    OnPropertyChanged(nameof(nAccMaskA));
                    UpdateUIFromAcceptanceCodes();
                }
            }
        }

        // 其他现有属性
        public uint nBaudRate { get; set; } = 500000;
        public byte nWorkMode { get; set; } = 0;
        public byte nAccFilterCnt { get; set; } = 0;


        #endregion

        #region INotifyPropertyChanged

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        #endregion

        public ChannelBusConfigWindow()
        {
            InitializeComponent();
            this.DataContext = this; // 设置DataContext为自身
            // Ensure initial enable/disable state is applied based on current filter selection
            this.Loaded += ChannelBusConfigWindow_Loaded;
        }
        
        private void ChannelBusConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Apply current selection once on load to set buttons/toggles enabled state
                FilterCntCombo_SelectionChanged(FilterCntCombo, null);
            }
            catch
            {
                // ignore any errors during initial sync
            }

            // Populate controls from Tag (preferred) or DataContext if it contains a CAN param struct
            try
            {
                PXI4004.ARTCANX1_CAN_PARAM? boxedParam = null;
                if (this.Tag is PXI4004.ARTCANX1_CAN_PARAM tagParam)
                {
                    boxedParam = tagParam;
                }
                else if (this.DataContext is PXI4004.ARTCANX1_CAN_PARAM ctxParam)
                {
                    boxedParam = ctxParam;
                }

                if (boxedParam.HasValue)
                {
                    var param = boxedParam.Value;
                    // 初始化属性值（不直接强制覆盖验收码/屏蔽码，优先使用界面 ID 寄存器的选择计算）
                    nBaudRate = param.nBaudRate;
                    nWorkMode = (byte)param.nWorkMode;
                    nAccFilterCnt = (byte)param.nAccFilterCnt;

                    // BaudCombo: match Tag (uint)
                    try
                    {
                        foreach (var item in BaudCombo.Items)
                        {
                            if (item is ComboBoxItem cbi && cbi.Tag is uint u && u == param.nBaudRate)
                            {
                                BaudCombo.SelectedItem = cbi;
                                break;
                            }
                        }
                    }
                    catch { }

                    // WorkModeCombo: match Tag (int)
                    try
                    {
                        foreach (var item in WorkModeCombo.Items)
                        {
                            if (item is ComboBoxItem cbi && cbi.Tag is int iv && iv == param.nWorkMode)
                            {
                                WorkModeCombo.SelectedItem = cbi;
                                break;
                            }
                        }
                    }
                    catch { }

                    // FilterCntCombo: match Tag (int)
                    try
                    {
                        foreach (var item in FilterCntCombo.Items)
                        {
                            if (item is ComboBoxItem cbi && cbi.Tag is int iv && iv == param.nAccFilterCnt)
                            {
                                FilterCntCombo.SelectedItem = cbi;
                                break;
                            }
                        }
                    }
                    catch { }

                    // Update custom baud textbox to show numeric value
                    try
                    {
                        CustomBaudText.Text = param.nBaudRate.ToString();
                    }
                    catch { }

                    // 使用参数中的验收码和屏蔽码来初始化界面
                    nAccCodeA = param.nAccCodeA.ToString("X8");
                    nAccMaskA = param.nAccMaskA.ToString("X8");
                    UpdateUIFromAcceptanceCodes();
                }
                else
                {
                    // 如果没有外部参数，使用界面按钮初始值计算并更新（默认按钮均为 "x"）
                    UpdateAcceptanceCodesFromUI();
                }
            }
            catch
            {
                // ignore
            }
        }

        // Helper to find visual children
        private IEnumerable<T> FindChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) yield return t;
                foreach (var childOfChild in FindChildren<T>(child))
                    yield return childOfChild;
            }
        }


        /// <summary>
        /// 设置应用按钮：根据验收ID寄存器状态计算验收码和屏蔽码，然后构建CAN参数并应用到设备
        /// </summary>
        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 先根据ID寄存器按钮状态更新验收码和屏蔽码
                UpdateAcceptanceCodesFromUI();

                // 然后应用所有设置（波特率、工作模式、滤波配置等）
                Ok_Click(sender, e);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show(
                    $"设置应用失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Debug: 输出将要应用的参数，便于确认 UI 选择是否生效
                Debug.WriteLine($"[ChannelBusConfigWindow] Applying param from UI -> Baud={nBaudRate}, WorkMode={nWorkMode}, FilterCnt={nAccFilterCnt}, AccCodeA={nAccCodeA}, AccMaskA={nAccMaskA}");

                // Build CAN param from UI controls by traversing visual tree
                var param = new PXI4004.ARTCANX1_CAN_PARAM();

                var comboList = FindChildren<ComboBox>(this).ToList();
                var textList = FindChildren<TextBox>(this).ToList();
                var checkList = FindChildren<CheckBox>(this).ToList();

                // Baud rate - use bound property (nBaudRate) so UI selection and custom input are respected
                try
                {
                    param.nBaudRate = this.nBaudRate;
                }
                catch { }

                // Work mode - use bound property
                param.nWorkMode = this.nWorkMode;

                // Recv timestamp - first CheckBox (if exists)
                if (checkList.Count > 0)
                {
                    param.bRecvTimestampEn = (byte)(checkList[0].IsChecked == true ? 1 : 0);
                }

                // AccExtID - second CheckBox (if exists)
                if (checkList.Count > 1)
                {
                    param.bAccExtID = (byte)(checkList[1].IsChecked == true ? 1 : 0);
                }

                // Filter count - use bound property
                param.nAccFilterCnt = this.nAccFilterCnt;

                // AccCodeA / AccMaskA - 从属性获取
                if (uint.TryParse(nAccCodeA.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint codeA))
                    param.nAccCodeA = codeA;
                if (uint.TryParse(nAccMaskA.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint maskA))
                    param.nAccMaskA = maskA;

                // Initialize arrays to avoid nulls
                param.nReserved1 = new uint[7];
                param.nReserved2 = new uint[32];
                param.SendTrig = new PXI4004.ARTCANX1_TRIG_PARAM();

                // Store built param in Tag so caller can read it
                this.Tag = param;
            }
            catch
            {
                // ignore parsing errors; proceed to close and let caller handle defaults
            }
            this.DialogResult = true;
            this.Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void CycleRegisterValue(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Cycle content: "x" -> "0" -> "1" -> "x"
                string cur = btn.Content?.ToString() ?? "x";
                string next = cur switch
                {
                    "x" => "0",
                    "0" => "1",
                    "1" => "x",
                    _ => "x"
                };
                btn.Content = next;

                // 更新验收码和屏蔽码
                UpdateAcceptanceCodesFromUI();
            }
        }

        /// <summary>
        /// 从验收码和屏蔽码更新界面显示
        /// </summary>
        private void UpdateUIFromAcceptanceCodes()
        {
            try
            {
                // 获取所有按钮
                var buttons = IdButtonPanel?.Children.OfType<Button>().ToList() ?? new List<Button>();

                if (buttons.Count < 30) return; // 需要30个按钮

                // 解析验收码和屏蔽码
                if (!uint.TryParse(nAccCodeA.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint accCodeA))
                    accCodeA = 0;

                if (!uint.TryParse(nAccMaskA.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint accMaskA))
                    accMaskA = 0xFFFFFFFF;

                // CAN ID位映射：按钮索引0=RTR, 1=ID28, ..., 29=ID00
                for (int i = 0; i < 30; i++)
                {
                    var btn = buttons[i];
                    uint bitMask = (uint)(1 << (29 - i)); // 位映射：高位在前

                    bool isMasked = (accMaskA & bitMask) != 0; // 是否参与过滤
                    bool isSet = (accCodeA & bitMask) != 0;     // 验收码对应位值

                    if (!isMasked)
                    {
                        // 不参与过滤：显示"x"
                        btn.Content = "x";
                    }
                    else
                    {
                        // 参与过滤：显示验收码对应位的值
                        btn.Content = isSet ? "1" : "0";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateUIFromAcceptanceCodes error: {ex.Message}");
            }
        }

        /// <summary>
        /// 从界面更新验收码和屏蔽码
        /// </summary>
        private void UpdateAcceptanceCodesFromUI()
        {
            try
            {
                // 获取所有按钮
                var buttons = IdButtonPanel?.Children.OfType<Button>().ToList() ?? new List<Button>();

                if (buttons.Count < 30) return; // 需要30个按钮

                // CAN ID位映射：按钮索引0=RTR, 1=ID28, ..., 29=ID00
                uint accCodeA = 0;
                uint accMaskA = 0;

                for (int i = 0; i < 30; i++)
                {
                    var btn = buttons[i];
                    string value = btn.Content?.ToString() ?? "x";

                    if (value == "0")
                    {
                        // 验收码对应位设为0，屏蔽码对应位设为1（参与过滤）
                        accMaskA |= (uint)(1 << (29 - i)); // 位映射：高位在前
                    }
                    else if (value == "1")
                    {
                        // 验收码对应位设为1，屏蔽码对应位设为1（参与过滤）
                        accCodeA |= (uint)(1 << (29 - i));
                        accMaskA |= (uint)(1 << (29 - i));
                    }
                    else // "x"
                    {
                        // 屏蔽码对应位设为0（不参与过滤）
                        // 验收码位保持0
                    }
                }

                // 更新文本框（不触发属性变更，避免循环）
                _nAccCodeA = accCodeA.ToString("X8");
                _nAccMaskA = accMaskA.ToString("X8");

                // 触发属性变更通知
                OnPropertyChanged(nameof(nAccCodeA));
                OnPropertyChanged(nameof(nAccMaskA));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateAcceptanceCodesFromUI error: {ex.Message}");
            }
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If shown as dialog, set DialogResult = false; otherwise just close
                this.DialogResult = false;
            }
            catch
            {
                // ignore if not shown as dialog
            }
            this.Close();
        }
        
        private void FilterCntCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedFilterMode = 0;
            try
            {
                if (FilterCntCombo?.SelectedValue != null)
                {
                    if (!int.TryParse(FilterCntCombo.SelectedValue.ToString(), out selectedFilterMode))
                    {
                        // fallback to SelectedIndex
                        selectedFilterMode = FilterCntCombo.SelectedIndex;
                    }
                }
            }
            catch
            {
                // ignore parsing errors, use default value 0
            }

            // 更新过滤器计数属性
            nAccFilterCnt = (byte)selectedFilterMode;

            // Use named panels (assigned in XAML) for reliable access
            var toggles = IdTogglePanel?.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().ToList() ?? new List<System.Windows.Controls.Primitives.ToggleButton>();
            var buttons = IdButtonPanel?.Children.OfType<Button>().ToList() ?? new List<Button>();

            // Helper to extract the numeric bit number from a ToggleButton
            int? GetBitNumber(System.Windows.Controls.Primitives.ToggleButton tb)
            {
                var texts = new List<string>();
                void CollectTexts(DependencyObject obj)
                {
                    if (obj is TextBlock tb) texts.Add(tb.Text);
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
                    {
                        CollectTexts(VisualTreeHelper.GetChild(obj, i));
                    }
                }
                CollectTexts(tb);

                // Find the last numeric text (should be the bit number)
                for (int i = texts.Count - 1; i >= 0; i--)
                {
                    if (int.TryParse(texts[i], out int num))
                    {
                        return num;
                    }
                }
                return null; // No numeric text found
            }

            // Apply enable/disable logic based on filter mode
            for (int i = 0; i < toggles.Count; i++)
            {
                var tb = toggles[i];
                int? bitNumber = GetBitNumber(tb);
                bool shouldEnable = false;

                if (selectedFilterMode == 1) // Single filter mode
                {
                    if (bitNumber.HasValue)
                    {
                        // Enable only bits 18-29 in single filter mode (RTR29 and ID28-ID18 for standard frames)
                        shouldEnable = (bitNumber.Value >= 18 && bitNumber.Value <= 29);
                    }
                    else
                    {
                        // Special case: RTR bit (bit 29) is always enabled in single filter mode
                        shouldEnable = true;
                    }
                }
                // For selectedFilterMode == 0 (no filter), shouldEnable remains false

                // Apply the enabled state to the ToggleButton
                tb.IsEnabled = shouldEnable;

                // Apply the same enabled state to the corresponding small button (if exists)
                if (i < buttons.Count)
                {
                    buttons[i].IsEnabled = shouldEnable;
                }
            }

            // 根据滤波模式设置验收码和屏蔽码的默认值
            if (selectedFilterMode == 0)
            {
                // 不参与滤波：验收码和屏蔽码都设为0
                nAccCodeA = "00000000";
                nAccMaskA = "00000000";

                // 确保UI与当前值同步
                UpdateUIFromAcceptanceCodes();
            }
            else if (selectedFilterMode == 1)
            {
                // 单滤波验收：默认所有位都不参与过滤（显示"x"）
                nAccCodeA = "00000000"; // 验收码初始为0
                nAccMaskA = "00000000"; // 屏蔽码初始为全0，所有位都不参与过滤

                // 确保UI与当前值同步
                UpdateUIFromAcceptanceCodes();
            }
        }
    }
}
