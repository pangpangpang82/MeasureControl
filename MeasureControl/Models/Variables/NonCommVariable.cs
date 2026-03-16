using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Models.Variables
{
    /// <summary>
    /// 非通讯变量
    /// </summary>
    public sealed class NonCommVariable
    {
        public NonCommVariableConfig Config { get; }
        public NonCommVariableRuntime Runtime { get; } = new NonCommVariableRuntime();

        public NonCommVariable(NonCommVariableConfig cfg)
        {
            Config = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Runtime.CurrentValue = cfg.DefaultValue;
            Runtime.Timestamp = DateTime.Now;
        }

        public bool Validate(object value, out string message)
        {
            message = null;
            if (value == null)
            {
                message = "值为空";
                return false;
            }

            try
            {
                switch (Config.DataType)
                {
                    case VariableDataType.Double:
                        var d = ConvertToDouble(value);
                        if (Config.Min.HasValue && d < Config.Min.Value) { message = $"小于最小值 {Config.Min}"; return false; }
                        if (Config.Max.HasValue && d > Config.Max.Value) { message = $"大于最大值 {Config.Max}"; return false; }
                        if (Config.Step.HasValue && Config.Step.Value > 0)
                        {
                            var step = Config.Step.Value;
                            var remainder = Math.Abs((d - (Config.Min ?? 0)) % step);
                            if (remainder > 1e-9 && Math.Abs(remainder - step) > 1e-9)
                            {
                                message = $"不满足步进 {step}"; return false;
                            }
                        }
                        break;
                    case VariableDataType.Int:
                        _ = ConvertToInt(value);
                        break;
                    case VariableDataType.Bool:
                        _ = ConvertToBool(value);
                        break;
                    case VariableDataType.Enum:
                    case VariableDataType.String:
                        // 放宽校验
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                message = $"类型/范围校验失败: {ex.Message}";
                return false;
            }
        }

        public void SetValue(object value, bool autoApply = true)
        {
            if (!Validate(value, out var msg))
            {
                Runtime.IsValid = false;
                Runtime.ValidationMessage = msg;
                return;
            }

            Runtime.IsValid = true;
            Runtime.ValidationMessage = null;
            Runtime.CurrentValue = CoerceToTargetType(value);
            Runtime.Timestamp = DateTime.Now;
            Runtime.LastError = null;

            if (autoApply && Config.SourceType == VariableSourceType.ChannelBinding && Config.WriteMode == WriteMode.Immediate)
            {
                _ = ApplyAsync(null, CancellationToken.None); // 允许上层传入服务；此处为空则不执行
            }
        }

        /// <summary>
        /// 下发到硬件（当 SourceType=ChannelBinding 时）
        /// </summary>
        public async Task<bool> ApplyAsync(IChannelBindingService bindingService, CancellationToken ct)
        {
            if (Config.SourceType != VariableSourceType.ChannelBinding)
                return true; // 非绑定变量无需下发

            if (bindingService == null)
                return false;

            if (string.IsNullOrEmpty(Config.DeviceId) || string.IsNullOrEmpty(Config.ChannelId))
            {
                Runtime.LastError = "未配置设备/通道";
                return false;
            }

            object val = Runtime.CurrentValue;

            // 缩放 Gain/Offset
            if (val is double dv)
            {
                if (Config.Gain.HasValue) dv *= Config.Gain.Value;
                if (Config.Offset.HasValue) dv += Config.Offset.Value;
                val = dv;
            }

            // 类型收敛
            val = CoerceToTargetType(val);

            try
            {
                var ok = await bindingService.WriteAsync(Config.DeviceId, Config.ChannelId, val, ct).ConfigureAwait(false);
                Runtime.LastError = ok ? null : "写入失败";
                return ok;
            }
            catch (Exception ex)
            {
                Runtime.LastError = ex.Message;
                return false;
            }
        }

        private double ConvertToDouble(object value)
        {
            if (value is double dd) return dd;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is string s) return double.Parse(s, CultureInfo.InvariantCulture);
            return System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private int ConvertToInt(object value)
        {
            if (value is int i) return i;
            if (value is long l) return checked((int)l);
            if (value is double d) return checked((int)Math.Round(d));
            if (value is string s) return int.Parse(s, CultureInfo.InvariantCulture);
            return System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private bool ConvertToBool(object value)
        {
            if (value is bool b) return b;
            if (value is string s) return bool.Parse(s);
            if (value is int i) return i != 0;
            return System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private object CoerceToTargetType(object value)
        {
            switch (Config.DataType)
            {
                case VariableDataType.Double: return ConvertToDouble(value);
                case VariableDataType.Int: return ConvertToInt(value);
                case VariableDataType.Bool: return ConvertToBool(value);
                case VariableDataType.Enum:
                case VariableDataType.String:
                default:
                    return value?.ToString();
            }
        }
    }
}

