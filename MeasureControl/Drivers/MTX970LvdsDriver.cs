using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;

namespace MeasureControl.Drivers
{
    public class MTX970LvdsDriver : IDeviceDriver
    {
        private const string DLL_NAME = "SharedLib.dll";

        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged
        {
            add { }
            remove { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DevConditionCluster
        {
            public IntPtr Model;
            public IntPtr ID;
            public IntPtr PXISlot;
        }

        [DllImport(DLL_NAME, EntryPoint = "MTLVDSLoopback_host", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte MTLVDSLoopback_host(
            byte configOsc,
            double clockFrequencyHz,
            byte staticTCountUpF,
            ushort lvdsDataSampleWr,
            ushort patternMatch,
            ushort numSamples,
            ref DevConditionCluster devCondition,
            out int indexOfElement,
            out byte triggerSampleLocation,
            [Out] ushort[] arrayWSubsetDeleted,
            int len);

        [DllImport(DLL_NAME, EntryPoint = "LVDLLStatus", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LVDLLStatus(
            [Out] StringBuilder errStr,
            int errStrLen,
            IntPtr module);

        [DllImport(DLL_NAME, EntryPoint = "SetExecuteVIsInPrivateExecutionSystem", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetExecuteVIsInPrivateExecutionSystem(int value);

        private readonly DeviceBase _device;
        private bool _isConnected;

        public string DeviceId => _device?.Id ?? string.Empty;

        public string DeviceName => _device?.Name ?? "MT-X970";

        public bool IsConnected => _isConnected;

        public bool IsSimulated => false;

        /// <summary>
        /// MTX970是LVDS通信设备
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Communication;

        public MTX970LvdsDriver(DeviceBase device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public async Task<bool> ConnectAsync()
        {
            // 在后台线程执行 DLL 加载/验证，避免在 UI 线程触发 LoaderLock
            return await Task.Run(() =>
            {
                try
                {
                    string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DLL_NAME);
                    if (!File.Exists(dllPath))
                    {
                        throw new FileNotFoundException($"DLL文件不存在: {dllPath}");
                    }

                    // 调用一次导出函数以验证 DLL 可被成功加载/解析。
                    // 如果位数不匹配或缺少依赖，会在此处抛出异常。
                    var sb = new StringBuilder(2048);
                    _ = LVDLLStatus(sb, sb.Capacity, IntPtr.Zero);

                    // 可选：切换 LabVIEW 执行系统。
                    SetExecuteVIsInPrivateExecutionSystem(1);

                    _isConnected = true;
                    return true;
                }
                catch (FileNotFoundException ex)
                {
                    Debug.WriteLine($"[MTX970LvdsDriver] ConnectAsync DLL文件未找到: {ex.Message}");
                    _isConnected = false;
                    return false;
                }
                catch (DllNotFoundException ex)
                {
                    Debug.WriteLine($"[MTX970LvdsDriver] ConnectAsync 无法加载DLL或依赖缺失: {ex.Message}");
                    _isConnected = false;
                    return false;
                }
                catch (BadImageFormatException ex)
                {
                    Debug.WriteLine($"[MTX970LvdsDriver] ConnectAsync DLL位数不匹配或格式错误: {ex.Message}");
                    _isConnected = false;
                    return false;
                }
                catch (EntryPointNotFoundException ex)
                {
                    Debug.WriteLine($"[MTX970LvdsDriver] ConnectAsync 找不到导出函数(EntryPoint): {ex.Message}");
                    _isConnected = false;
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MTX970LvdsDriver] ConnectAsync 失败: {ex}");
                    _isConnected = false;
                    return false;
                }
            });
        }

        public Task<bool> DisconnectAsync()
        {
            _isConnected = false;
            return Task.FromResult(true);
        }

        public Task<double> ReadChannelAsync(string channelId)
        {
            return Task.FromResult(0.0);
        }

        public Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            return Task.FromResult(new Dictionary<string, double>());
        }

        public Task<bool> WriteChannelAsync(string channelId, double value)
        {
            return Task.FromResult(false);
        }

        public Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            return Task.FromResult(false);
        }

        public Task<bool> StartAcquisitionAsync()
        {
            return Task.FromResult(false);
        }

        public Task<bool> StopAcquisitionAsync()
        {
            return Task.FromResult(false);
        }

        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            return Task.FromResult(new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "Status", _device?.Status ?? string.Empty }
            });
        }

        public Task<bool> ResetAsync()
        {
            return Task.FromResult(false);
        }

        public Task<bool> SelfTestAsync()
        {
            return Task.FromResult(false);
        }

        public Task<LoopbackResult> RunLoopbackAsync(
            bool configOsc,
            double clockFrequencyHz,
            bool staticTCountUpF,
            ushort lvdsDataSampleWr,
            ushort patternMatch,
            ushort numSamples,
            string devConditionModel = "",
            string devConditionId = "",
            string devConditionPxiSlot = "")
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("设备未连接");
            }

            return Task.Run(() =>
            {
                int indexOfElement;
                byte triggerSampleLocation;

                var array = new ushort[numSamples];
                var dev = new DevConditionCluster();
                IntPtr hModel = IntPtr.Zero;
                IntPtr hId = IntPtr.Zero;
                IntPtr hSlot = IntPtr.Zero;

                try
                {
                    hModel = CreateLStrHandle(devConditionModel);
                    hId = CreateLStrHandle(devConditionId);
                    hSlot = CreateLStrHandle(devConditionPxiSlot);

                    dev.Model = hModel;
                    dev.ID = hId;
                    dev.PXISlot = hSlot;

                    byte code = MTLVDSLoopback_host(
                        configOsc ? (byte)1 : (byte)0,
                        clockFrequencyHz,
                        staticTCountUpF ? (byte)1 : (byte)0,
                        lvdsDataSampleWr,
                        patternMatch,
                        numSamples,
                        ref dev,
                        out indexOfElement,
                        out triggerSampleLocation,
                        array,
                        array.Length);

                    string err = TryGetLastError();

                    return new LoopbackResult
                    {
                        ReturnCode = code,
                        IndexOfElement = indexOfElement,
                        TriggerSampleLocation = triggerSampleLocation,
                        ArrayWSubsetDeleted = array,
                        ErrorMessage = err
                    };
                }
                finally
                {
                    FreeLStrHandle(hModel);
                    FreeLStrHandle(hId);
                    FreeLStrHandle(hSlot);
                }
            });
        }

        private static IntPtr CreateLStrHandle(string value)
        {
            value ??= string.Empty;

            // LabVIEW LStrHandle is a handle (pointer-to-pointer) to a string structure:
            //   handle -> dataPtr
            //   dataPtr: [int32 length][byte[length] data]
            // The callee expects an LStrHandle (handle), not a direct pointer to the string data.
            byte[] bytes = Encoding.UTF8.GetBytes(value);

            IntPtr dataPtr = Marshal.AllocHGlobal(4 + bytes.Length);
            Marshal.WriteInt32(dataPtr, bytes.Length);
            if (bytes.Length > 0)
            {
                Marshal.Copy(bytes, 0, IntPtr.Add(dataPtr, 4), bytes.Length);
            }

            IntPtr handle = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(handle, dataPtr);
            return handle;
        }

        private static void FreeLStrHandle(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                try
                {
                    IntPtr dataPtr = Marshal.ReadIntPtr(handle);
                    if (dataPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(dataPtr);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(handle);
                }
            }
        }

        private static string TryGetLastError()
        {
            try
            {
                var sb = new StringBuilder(2048);
                int res = LVDLLStatus(sb, sb.Capacity, IntPtr.Zero);
                if (res != 0)
                {
                    return $"LVDLLStatus={res}: {sb}";
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public sealed class LoopbackResult
        {
            public byte ReturnCode { get; set; }
            public int IndexOfElement { get; set; }
            public byte TriggerSampleLocation { get; set; }
            public ushort[] ArrayWSubsetDeleted { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}
