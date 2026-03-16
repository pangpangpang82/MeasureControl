using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MeasureControl.Views.Dialogs;
using MeasureControl.Views.TestTask;

namespace MeasureControl.Views.TestTask.CardCATPanel.Mil1394B
{
    /// <summary>
    /// Mil1394EditPayloadWindow.xaml 的交互逻辑
    /// 编辑异步流包数据的窗口
    /// </summary>
    public partial class Mil1394EditPayloadWindow : Window
    {
        private readonly Mil1394NodeConfigPanel.AsyncSendConfigItem _originalItem;
        private readonly uint _nodeNumber;
        private ObservableCollection<PayloadFieldItem> _payloadFields;

        public Mil1394EditPayloadWindow(Mil1394NodeConfigPanel.AsyncSendConfigItem item, uint nodeNumber)
        {
            try
            {
                if (item == null)
                {
                    throw new ArgumentNullException(nameof(item));
                }

                InitializeComponent();
                _originalItem = item;
                _nodeNumber = nodeNumber;

                // 延迟初始化，确保控件完全加载后再设置数据源
                Loaded += Mil1394EditPayloadWindow_Loaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mil1394EditPayloadWindow构造函数异常: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 窗口加载完成后初始化
        /// </summary>
        private void Mil1394EditPayloadWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializePayloadFields();
                if (DgvPayload != null)
                {
                    DgvPayload.ItemsSource = _payloadFields;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mil1394EditPayloadWindow_Loaded异常: {ex}");
                ReMessageBox.Show($"初始化编辑窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 初始化负载字段
        /// </summary>
        private void InitializePayloadFields()
        {
            _payloadFields = new ObservableCollection<PayloadFieldItem>();

            // PayloadLength 现在是字节数（例如：200字节）
            int payloadLengthBytes = _originalItem?.PayloadLength ?? 64; // 默认64字节
            if (payloadLengthBytes < 32) // 最小32字节
            {
                payloadLengthBytes = 32;
            }

            // Security（安全字）
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "Security",
                Value = Convert.ToString(_originalItem?.Security ?? 0, 16),
                FieldType = PayloadFieldType.Security
            });

            // Priority（优先级）
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "Priority",
                Value = Convert.ToString(_originalItem?.Priority ?? 0, 16),
                FieldType = PayloadFieldType.Priority
            });

            // PayLoadLength（负载长度，单位：字节）- 不可编辑
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "PayLoadLength",
                Value = payloadLengthBytes.ToString(),
                FieldType = PayloadFieldType.PayLoadLength,
                IsReadOnly = true
            });

            // Health State（健康状态字）
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "Health State",
                Value = Convert.ToString(_originalItem?.Health ?? 0, 16),
                FieldType = PayloadFieldType.HealthState
            });

            // HeartBeat（心跳）
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "HeartBeat",
                Value = Convert.ToString(_originalItem?.Heartbeat ?? 0, 16),
                FieldType = PayloadFieldType.HeartBeat
            });

            // Quadlet Data[]（负载数据数组）
            // PayloadLength 是总字节数（包括 Security 和 Priority）
            // Security 和 Priority 各占 4 字节，共 8 字节
            // Quadlet Data 数量 = (总字节数 - 8) / 4
            // 例如：PayloadLength = 200 字节，则 Quadlet Data = (200 - 8) / 4 = 48 个
            int quadletDataCount = Math.Max(0, (payloadLengthBytes - 8) / 4);
            if (quadletDataCount > 500)
            {
                quadletDataCount = 500; // 最大500个Quadlet（2000字节的Quadlet Data）
            }

            // 确保PayloadData不为null
            uint[] payloadData = _originalItem?.PayloadData;
            if (payloadData == null || payloadData.Length == 0)
            {
                payloadData = new uint[500]; // 默认500个uint
            }

            for (int i = 0; i < quadletDataCount; i++)
            {
                uint dataValue = (i < payloadData.Length) ? payloadData[i] : 0;
                _payloadFields.Add(new PayloadFieldItem
                {
                    FieldName = $"Quadlet Data[{i}]",
                    Value = Convert.ToString(dataValue, 16),
                    FieldType = PayloadFieldType.QuadletData,
                    DataIndex = i
                });
            }

            // TransmitOffset
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "TransmitOffset",
                Value = Convert.ToString(_originalItem?.TransmitOffset ?? 0, 16),
                FieldType = PayloadFieldType.TransmitOffset
            });

            // ReceiveOffset
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "ReceiveOffset",
                Value = Convert.ToString(_originalItem?.ReceiveOffset ?? 0, 16),
                FieldType = PayloadFieldType.ReceiveOffset
            });

            // PHMOffset
            _payloadFields.Add(new PayloadFieldItem
            {
                FieldName = "PHMOffset",
                Value = Convert.ToString(_originalItem?.PHMOffset ?? 0, 16),
                FieldType = PayloadFieldType.PHMOffset
            });
        }

        /// <summary>
        /// 单元格开始编辑事件
        /// </summary>
        private void DgvPayload_BeginningEdit(object sender, System.Windows.Controls.DataGridBeginningEditEventArgs e)
        {
            var fieldItem = e.Row.Item as PayloadFieldItem;
            if (fieldItem != null && fieldItem.IsReadOnly)
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// 单元格编辑结束事件
        /// </summary>
        private void DgvPayload_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == System.Windows.Controls.DataGridEditAction.Cancel)
            {
                return;
            }

            var fieldItem = e.Row.Item as PayloadFieldItem;
            if (fieldItem == null)
            {
                return;
            }

            // 只处理第二列（值列）
            if (e.Column.DisplayIndex != 1)
            {
                return;
            }

            var textBox = e.EditingElement as System.Windows.Controls.TextBox;
            if (textBox == null)
            {
                return;
            }

            string newValue = textBox.Text.Trim();

            // 验证十六进制格式
            if (!IsValidHex(newValue, fieldItem.FieldType))
            {
                ReMessageBox.Show($"无效的十六进制值: {newValue}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }

            // 更新值
            fieldItem.Value = newValue;
        }

        /// <summary>
        /// 验证十六进制值
        /// </summary>
        private bool IsValidHex(string value, PayloadFieldType fieldType)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // 验证是否为有效的十六进制数
            uint result;
            if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out result))
            {
                return false;
            }

            // 根据字段类型验证范围
            switch (fieldType)
            {
                case PayloadFieldType.Priority:
                    // Priority只能是1-2位十六进制数（0-FF）
                    return value.Length <= 2;
                case PayloadFieldType.PayLoadLength:
                    // PayLoadLength不可编辑
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 确定按钮点击事件
        /// </summary>
        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            // 验证所有可编辑字段（跳过只读字段）
            foreach (var field in _payloadFields)
            {
                // 跳过只读字段（如PayLoadLength）
                if (field.IsReadOnly)
                {
                    continue;
                }

                if (!IsValidHex(field.Value, field.FieldType))
                {
                    ReMessageBox.Show($"字段 {field.FieldName} 的值无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 取消按钮点击事件
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 获取更新后的数据项
        /// </summary>
        public Mil1394NodeConfigPanel.AsyncSendConfigItem GetUpdatedItem()
        {
            // 确保PayloadData不为null
            uint[] originalPayloadData = _originalItem?.PayloadData;
            if (originalPayloadData == null || originalPayloadData.Length == 0)
            {
                originalPayloadData = new uint[500]; // 默认500个uint
            }

            // 确保PayloadLength有效
            int payloadLength = _originalItem?.PayloadLength ?? 64;
            if (payloadLength < 32)
            {
                payloadLength = 32; // 最小32字节
            }

            var updatedItem = new Mil1394NodeConfigPanel.AsyncSendConfigItem
            {
                MessageID = _originalItem?.MessageID ?? 0,
                Channel = _originalItem?.Channel ?? 0,
                Heartbeat = _originalItem?.Heartbeat ?? 0,
                Health = _originalItem?.Health ?? 0,
                HeartbeatStep = _originalItem?.HeartbeatStep ?? 0,
                PayloadLength = payloadLength,
                SendOffset = _originalItem?.SendOffset ?? 0,
                VPC = _originalItem?.VPC ?? false,
                VPCAsync = _originalItem?.VPCAsync ?? 0,
                Security = _originalItem?.Security ?? 0,
                Priority = _originalItem?.Priority ?? 0,
                PayloadData = new uint[originalPayloadData.Length],
                TransmitOffset = _originalItem?.TransmitOffset ?? 0,
                ReceiveOffset = _originalItem?.ReceiveOffset ?? 0,
                PHMOffset = _originalItem?.PHMOffset ?? 0
            };

            // 复制原始PayloadData
            Array.Copy(originalPayloadData, updatedItem.PayloadData, Math.Min(originalPayloadData.Length, updatedItem.PayloadData.Length));

            // 更新可编辑字段
            foreach (var field in _payloadFields)
            {
                if (field.IsReadOnly)
                {
                    continue;
                }

                // 检查字段值是否为空
                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394EditPayload] 字段 {field.FieldName} 的值为空，跳过");
                    continue;
                }

                uint value;
                try
                {
                    value = Convert.ToUInt32(field.Value, 16);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394EditPayload] 字段 {field.FieldName} 的值转换失败: {field.Value}, 错误: {ex.Message}");
                    continue; // 跳过无效值
                }

                switch (field.FieldType)
                {
                    case PayloadFieldType.Security:
                        updatedItem.Security = value;
                        break;
                    case PayloadFieldType.Priority:
                        updatedItem.Priority = value;
                        break;
                    case PayloadFieldType.HealthState:
                        updatedItem.Health = (int)value;
                        break;
                    case PayloadFieldType.HeartBeat:
                        updatedItem.Heartbeat = (int)value;
                        break;
                    case PayloadFieldType.QuadletData:
                        if (field.DataIndex >= 0 && field.DataIndex < updatedItem.PayloadData.Length)
                        {
                            updatedItem.PayloadData[field.DataIndex] = value;
                        }
                        break;
                    case PayloadFieldType.TransmitOffset:
                        updatedItem.TransmitOffset = value;
                        break;
                    case PayloadFieldType.ReceiveOffset:
                        updatedItem.ReceiveOffset = value;
                        break;
                    case PayloadFieldType.PHMOffset:
                        updatedItem.PHMOffset = value;
                        break;
                }
            }

            return updatedItem;
        }
    }

    /// <summary>
    /// 负载字段项
    /// </summary>
    public class PayloadFieldItem
    {
        public string FieldName { get; set; }
        public string Value { get; set; }
        public PayloadFieldType FieldType { get; set; }
        public bool IsReadOnly { get; set; }
        public int DataIndex { get; set; } = -1; // 用于Quadlet Data数组索引
    }

    /// <summary>
    /// 负载字段类型
    /// </summary>
    public enum PayloadFieldType
    {
        Security,
        Priority,
        PayLoadLength,
        HealthState,
        HeartBeat,
        QuadletData,
        TransmitOffset,
        ReceiveOffset,
        PHMOffset
    }
}
