using System;
using System.Runtime.InteropServices;

namespace MeasureControl.Helpers.OKAIPXIDevice
{
    // 枚举和常量定义
    public enum PXI_INTERRUPT_WAIT_RESULT
    {
        PXI_INTERRUPT_RECEIVED = 0,
        PXI_INTERRUPT_STOPPED,
        PXI_INTERRUPT_INTERRUPTED,
    }

    // 结构体定义
    [StructLayout(LayoutKind.Sequential)]
    public struct PXI_WD_PCI_SLOT
    {
        public uint dwBus;
        public uint dwSlot;
        public uint dwFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PXI_INT_RESULT
    {
        public uint dwCounter;
        public uint dwLost;
        public PXI_INTERRUPT_WAIT_RESULT waitResult;
    }

    // 回调函数委托定义
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PXI_INT_HANDLER(
        UIntPtr vi,
        ref PXI_INT_RESULT pIntResult);

    public static class OKAIDaqNative
    {
        private const string DllName = "OKAIDaq.dll";

        // 连接模块
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int DAQDevice_getSlot(UIntPtr vi, out ushort slot);
    }
}

