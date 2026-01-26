using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Helpers.OKAIPXIDevice;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class PXI4087ASelfInspectionTask : ISelfInspectionTask
    {
        private const double DefaultExcitationVoltageRms = 7.0;
        private const double DefaultExcitationFrequencyHz = 3300.0;
        private const double AllowedAmplitudeAbsErrorV = 0.01;
        private const double AllowedFrequencyAbsErrorHz = 10.0;

        private static bool Is4087A(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("4087A");
        }

        private static int GetSlotIndex(DeviceBase device)
        {
            if (device is PxiDeviceBase pxi)
            {
                return pxi.SlotIndex;
            }
            return -1;
        }

        public bool CanHandle(DeviceBase device)
        {
            return device != null && Is4087A(device);
        }

        public async Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!Is4087A(device))
            {
                context.Log($"跳过：非4087A板卡 {device.Name} Model={device.Model}");
                return;
            }

            var slotIndex = GetSlotIndex(device);

            UIntPtr vi = UIntPtr.Zero;
            ushort openedId = 0;
            bool ch0Started = false;
            bool ch1Started = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Log($"准备打开板卡 4087A (Slot={slotIndex})");

                var expectedSlot = slotIndex;
                bool needMatchSlot = expectedSlot > 0;

                for (ushort id = 1; id <= 32; id++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    UIntPtr h = UIntPtr.Zero;
                    try
                    {
                        h = PXI4087Native.pxi4087_openDevice(id);
                        if (h == UIntPtr.Zero)
                        {
                            continue;
                        }

                        ushort actualSlot;
                        int slotStatus = OKAIDaqNative.DAQDevice_getSlot(h, out actualSlot);
                        if (slotStatus != 0)
                        {
                            try { PXI4087Native.pxi4087_releaseDevice(h); } catch { }
                            continue;
                        }

                        if (!needMatchSlot || actualSlot == expectedSlot)
                        {
                            vi = h;
                            openedId = id;
                            context.Log($"打开板卡成功：Id={openedId} Slot={actualSlot}");
                            break;
                        }

                        try { PXI4087Native.pxi4087_releaseDevice(h); } catch { }
                    }
                    catch
                    {
                        try { if (h != UIntPtr.Zero) PXI4087Native.pxi4087_releaseDevice(h); } catch { }
                    }
                }

                if (vi == UIntPtr.Zero)
                {
                    context.Log("打开板卡失败：未能获取有效设备句柄");
                    throw new InvalidOperationException("打开板卡失败");
                }

                try
                {
                    PXI4087Native.pxi4087_reset(vi);
                }
                catch
                {
                }

                context.Log("CH0 设置：内部激励 7Vrms 3300Hz + VaVb输出(Va=7,Vb=0)");

                int status;

                status = PXI4087Native.pxi4087_setMode(
                    vi,
                    0,
                    (ushort)PXI4087Constants.pxi4087_Ch_Mode_Sim,
                    (ushort)PXI4087Constants.pxi4087_Ch_Exc_Sour_Int,
                    0,
                    0);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH0 setMode失败: {status}");
                }

                status = PXI4087Native.pxi4087_setIntExcSig(vi, 0, DefaultExcitationVoltageRms, DefaultExcitationFrequencyHz);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH0 setIntExcSig失败: {status}");
                }

                status = PXI4087Native.pxi4087_setLvdtVaVb(vi, 0, DefaultExcitationVoltageRms, 0.0);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH0 setLvdtVaVb失败: {status}");
                }

                status = PXI4087Native.pxi4087_lvdtStart(vi, 0);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH0 lvdtStart失败: {status}");
                }
                ch0Started = true;

                await Task.Delay(300, cancellationToken);

                context.Log("请确认：已将 CH0 VA 输出手动接到 CH1 EXC 端");

                context.Log("CH1 设置：外部激励，读取EXC有效值并与7Vrms比较");

                status = PXI4087Native.pxi4087_setMode(
                    vi,
                    1,
                    (ushort)PXI4087Constants.pxi4087_Ch_Mode_Test,
                    (ushort)PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext,
                    0,
                    0);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH1 setMode失败: {status}");
                }

                status = PXI4087Native.pxi4087_setSelExcCh0Flag(vi, 1, 0);
                if (status != 0)
                {
                    context.Log($"CH1 setSelExcCh0Flag返回非0: {status}");
                }

                status = PXI4087Native.pxi4087_setIntExcSig(vi, 1, DefaultExcitationVoltageRms, DefaultExcitationFrequencyHz);
                if (status != 0)
                {
                    context.Log($"CH1 setIntExcSig返回非0: {status}");
                }

                status = PXI4087Native.pxi4087_lvdtStart(vi, 1);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH1 lvdtStart失败: {status}");
                }
                ch1Started = true;

                await Task.Delay(300, cancellationToken);

                double excRms = 0.0;
                status = PXI4087Native.pxi4087_getLvdtExcSigRms(vi, 1, out excRms);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH1 getLvdtExcSigRms失败: {status}");
                }

                double excFreqHz = 0.0;
                status = PXI4087Native.pxi4087_getLvdtExcSigFreq(vi, 1, out excFreqHz);
                if (status != 0)
                {
                    throw new InvalidOperationException($"CH1 getLvdtExcSigFreq失败: {status}");
                }

                double ampAbsErrorV = Math.Abs(excRms - DefaultExcitationVoltageRms);
                double freqAbsErrorHz = Math.Abs(excFreqHz - DefaultExcitationFrequencyHz);
                bool pass = ampAbsErrorV <= AllowedAmplitudeAbsErrorV && freqAbsErrorHz <= AllowedFrequencyAbsErrorHz;

                context.Log(
                    $"测量结果：CH1 ExcRms={excRms.ToString("F6", CultureInfo.InvariantCulture)}Vrms, 期望={DefaultExcitationVoltageRms.ToString("F2", CultureInfo.InvariantCulture)}Vrms, " +
                    $"|幅值误差|={ampAbsErrorV.ToString("F6", CultureInfo.InvariantCulture)}V(阈值≤{AllowedAmplitudeAbsErrorV.ToString("F3", CultureInfo.InvariantCulture)}V), " +
                    $"ExcFreq={excFreqHz.ToString("F3", CultureInfo.InvariantCulture)}Hz, 期望={DefaultExcitationFrequencyHz.ToString("F1", CultureInfo.InvariantCulture)}Hz, " +
                    $"|频率误差|={freqAbsErrorHz.ToString("F3", CultureInfo.InvariantCulture)}Hz(阈值≤{AllowedFrequencyAbsErrorHz.ToString("F1", CultureInfo.InvariantCulture)}Hz) -> {(pass ? "PASS" : "FAIL")}");
            }
            finally
            {
                try
                {
                    if (vi != UIntPtr.Zero)
                    {
                        if (ch1Started)
                        {
                            try { PXI4087Native.pxi4087_lvdtStop(vi, 1); } catch { }
                        }

                        if (ch0Started)
                        {
                            try { PXI4087Native.pxi4087_lvdtStop(vi, 0); } catch { }
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    if (vi != UIntPtr.Zero)
                    {
                        context.Log($"释放板卡句柄 (Id={openedId})");
                        try { PXI4087Native.pxi4087_releaseDevice(vi); } catch { }
                    }
                }
                catch
                {
                }
            }
        }
    }
}
