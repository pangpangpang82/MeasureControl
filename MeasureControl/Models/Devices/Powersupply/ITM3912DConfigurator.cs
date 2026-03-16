namespace MeasureControl.Models.Devices.Configurators.PowerSupply
{
    /// <summary>
    /// IT-M3912D-500-72配置器（根据艾德克斯用户手册 V2.6/5, 2025）
    /// </summary>
    public class ITM3912DConfigurator : DeviceConfiguratorBase
    {
        protected override string[] SupportedModelKeywords => new[] { "it-m3912d", "m3912d" };

        public override void Configure(DeviceBase device)
        {
            var ps = device as PowerSupplyDevice;
            if (ps == null) return;

            ConfigureBasicParams(ps);
            ConfigureResolution(ps);
            ConfigureAccuracy(ps);
            ConfigureProtection(ps);
            ConfigureRippleAndTiming(ps);
            ConfigureRegulation(ps);
            ConfigureOutputControl(ps);
            ConfigureParallelConfig(ps);
            ConfigurePowerInput(ps);
            ConfigureFunctionalFeatures(ps);
            ConfigureCommunication(ps);
            ConfigurePhysicalParams(ps);
            ConfigureEnvironment(ps);
        }

        private void ConfigureBasicParams(PowerSupplyDevice ps)
        {
            ps.MaxVoltage = 500;
            ps.MaxCurrent = 72;
            ps.PowerRating = 12000; // 12kW
            ps.ChannelCount = 1; // 单通道
        }

        private void ConfigureResolution(PowerSupplyDevice ps)
        {
            ps.VoltageResolution = "0.01V";
            ps.CurrentResolution = "0.01A";
            ps.PowerResolution = "1W";
            ps.ResistanceResolution = "0.001Ω";
        }

        private void ConfigureAccuracy(PowerSupplyDevice ps)
        {
            // 设定值精确度（12个月校准，25°C ±5°C）
            ps.SetVoltageAccuracy = "≤0.03% + 0.03%FS";
            ps.SetCurrentAccuracy = "≤0.1% + 0.1%FS";
            ps.SetPowerAccuracy = "≤0.5% + 0.5%FS";
            ps.SetResistanceAccuracy = "≤1%FS";

            // 回读值精确度
            ps.ReadbackVoltageAccuracy = "≤0.03% + 0.03%FS";
            ps.ReadbackCurrentAccuracy = "≤0.1% + 0.1%FS";
            ps.ReadbackPowerAccuracy = "≤0.5% + 0.5%FS";

            // 温度系数
            ps.SetVoltageTempCoeff = "≤30ppm/℃";
            ps.SetCurrentTempCoeff = "≤50ppm/℃";
            ps.ReadbackVoltageTempCoeff = "≤30ppm/℃";
            ps.ReadbackCurrentTempCoeff = "≤50ppm/℃";
        }

        private void ConfigureProtection(PowerSupplyDevice ps)
        {
            ps.OverVoltageProtection = "505V";
            ps.OverCurrentProtection = "75A";
            ps.OverPowerProtection = "12240W";
        }

        private void ConfigureRippleAndTiming(PowerSupplyDevice ps)
        {
            // 纹波（20Hz~20MHz，三相交流输入）
            ps.VoltageRipplePeak = "≤500mVpp";
            ps.VoltageRippleRms = "≤100mV";

            // 时间参数
            ps.RiseTimeNoLoad = "≤30ms";
            ps.RiseTimeFullLoad = "≤60ms";
            ps.FallTimeNoLoad = "≤1s";
            ps.FallTimeFullLoad = "≤100ms";
            ps.DynamicResponseTime = "≤1ms（额定25%→90%，稳定度≤5V）";
            ps.ProgrammingResponseTime = "0.1ms";
        }

        private void ConfigureRegulation(PowerSupplyDevice ps)
        {
            // 调节率
            ps.LineRegulationVoltage = "≤0.01% + 0.01%FS";
            ps.LineRegulationCurrent = "≤0.03% + 0.03%FS";
            ps.LoadRegulationVoltage = "≤0.01% + 0.01%FS";
            ps.LoadRegulationCurrent = "≤0.05% + 0.05%FS";

            // 其他性能
            ps.CurrentHarmonic = "≤3%";
        }

        private void ConfigureOutputControl(PowerSupplyDevice ps)
        {
            ps.SeriesResistance = 0; // 默认0Ω，范围 0 ~ 0.35Ω（CV优先模式）
            ps.PriorityMode = PowerSupplyPriorityMode.CV_Priority;
            ps.SenseCompensationVoltage = 10; // ≤10V
        }

        private void ConfigureParallelConfig(PowerSupplyDevice ps)
        {
            ps.IsMasterUnit = true;
            ps.SlaveCount = 0;
            ps.FiberConnectionEnabled = false;
            ps.ParallelMaxUnits = 16; // 最多16台并联（光纤传输）
        }

        private void ConfigurePowerInput(PowerSupplyDevice ps)
        {
            ps.InputVoltageType = "三相 200V～480V / 单相 100V～240V";
            ps.MaxACApparentPower = 6.5; // kVA（估算）
            ps.PowerFactor = 0.99;
            ps.MaxEfficiency = 93;
        }

        private void ConfigureFunctionalFeatures(PowerSupplyDevice ps)
        {
            ps.SupportListFunction = true;
            ps.MaxListSteps = 200; // 最多200步骤，支持USB导入/导出
            ps.SupportArbitraryWaveform = true; // 内置函数发生器
            ps.BuiltInWebServer = true;
        }

        private void ConfigureCommunication(PowerSupplyDevice ps)
        {
            // 标配
            ps.InterfaceUSB = "USBTMC";
            ps.InterfaceLAN = true; // 以太网（远程控制/Web服务器）
            ps.InterfaceCAN = true; // CAN通讯
            ps.InterfaceDigitalIO = true; // P-IO（外部触发/报警）

            // 选配
            ps.InterfaceGPIB = false; // 选配IT-E176卡
            ps.InterfaceRS232 = false; // 选配IT-E177卡（RS232 & 模拟量）

            // 外部模拟量接口（选配IT-E177）
            ps.ExternalAnalogCurrentProgramming = "0V~10V → 0A~72A";
            ps.ExternalAnalogCurrentMonitoring = "0A~72A → 0V~10V";
            ps.ExternalAnalogVoltageProgramming = "0V~10V → 0~500V";
            ps.ExternalAnalogVoltageMonitoring = "0~500V → 0V~10V";
        }

        private void ConfigurePhysicalParams(PowerSupplyDevice ps)
        {
            ps.DimensionsOverall = "459mm × 56.9mm × 771.9mm (W×H×D)";
            ps.DimensionsBare = "437mm × 43.5mm × 744.22mm (W×H×D)";
            ps.NetWeight = 15; // kg
            ps.CoolingMethod = "风冷";
            ps.ProtectionRating = "IP20";
        }

        private void ConfigureEnvironment(PowerSupplyDevice ps)
        {
            ps.OperatingTemperature = "0℃ ~ 40℃";
            ps.StorageTemperature = "-10℃ ~ 70℃";
            ps.OperatingHumidity = "20% ~ 80% RH（非冷凝）";
            ps.AltitudeLimit = "操作 <2000m";
        }
    }
}

