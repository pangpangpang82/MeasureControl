namespace MeasureControl.Models.Devices.Configurators.PowerSupply
{
    /// <summary>
    /// IT-N6332B配置器
    /// 规格：CH1/CH2: 32V/10A/200W, CH3: 15V/5A/45W
    /// </summary>
    public class IT6332Configurator : DeviceConfiguratorBase
    {
        protected override string[] SupportedModelKeywords => new[] { "it6332", "it-6332", "it-n6332" };

        public override void Configure(DeviceBase device)
        {
            var ps = device as PowerSupplyDevice;
            if (ps == null) return;

            // 基本参数
            ps.MaxVoltage = 32;
            ps.MaxCurrent = 10;
            ps.PowerRating = 200;
            ps.ChannelCount = 3;
            ps.SeriesVoltageLimit = 96;     // CH1+CH2 串联最高 64V (32V*2), CH3 独立 15V, 总计约96V
            ps.ParallelCurrentLimit = 20;   // 并联最高20A
            ps.LoadRegulation = "≤0.01%+3mV (V), ≤0.01%+3mA (A)";
            ps.LineRegulation = "≤0.01%+3mV (V), ≤0.01%+3mA (A)";
            ps.OverVoltageProtection = "33V (CH1/CH2), 16V (CH3)";

            // CH3 独立规格
            ps.Ch3MaxVoltage = 15;
            ps.Ch3MaxCurrent = 5;
            ps.Ch3PowerRating = 45;
            ps.Ch3OverVoltageProtection = "16V";

            // 通信接口配置
            ConfigureInterfaces(ps, device.Model);
        }

        private void ConfigureInterfaces(PowerSupplyDevice ps, string modelName)
        {
            var lowerName = modelName?.ToLower() ?? "";

            // IT-N6332B 特殊配置：USB + LAN + RS232 + 数字I/O（标配），GPIB（选配IT-E252）
            if (lowerName.Contains("it-n6332b") || lowerName.Contains("it-n6332"))
            {
                ps.InterfaceRS232 = true;
                ps.InterfaceUSB = "USBTMC";
                ps.InterfaceGPIB = false; // GPIB为选配
                ps.InterfaceLAN = true;
                ps.InterfaceDigitalIO = true;
                ps.GpibAddressRange = "1-30 (选配IT-E252)";
            }
            // A系列：仅RS232
            else if (lowerName.Contains("6332a"))
            {
                ps.InterfaceRS232 = true;
                ps.InterfaceUSB = "";
                ps.InterfaceGPIB = false;
                ps.InterfaceLAN = false;
                ps.InterfaceDigitalIO = false;
            }
            // B系列：RS232 + USB + GPIB
            else if (lowerName.Contains("6332b"))
            {
                ps.InterfaceRS232 = true;
                ps.InterfaceUSB = "USBTMC";
                ps.InterfaceGPIB = true;
                ps.InterfaceLAN = false;
                ps.InterfaceDigitalIO = false;
                ps.GpibAddressRange = "1-30";
            }
            // C系列：RS232 + USB + LAN
            else if (lowerName.Contains("6332c"))
            {
                ps.InterfaceRS232 = true;
                ps.InterfaceUSB = "USBTMC";
                ps.InterfaceGPIB = false;
                ps.InterfaceLAN = true;
                ps.InterfaceDigitalIO = false;
            }
            else
            {
                // 默认B系列配置
                ps.InterfaceRS232 = true;
                ps.InterfaceUSB = "USBTMC";
                ps.InterfaceGPIB = true;
                ps.InterfaceLAN = false;
                ps.InterfaceDigitalIO = false;
                ps.GpibAddressRange = "1-30";
            }
        }
    }
}

