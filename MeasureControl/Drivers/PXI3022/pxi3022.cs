using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;


namespace OKAIPXIDevice
{

    public static class PXI3022Constants
    {
        public const int PXI3022_ROW_4ROW = 4;
        public const int PXI3022_COL_64COL = 64;
        public const int PXI3022_SCAN_TABLE_MAX_NUM = 1024;
        public const int TOTAL_RELAY_COUNT = PXI3022_ROW_4ROW * PXI3022_COL_64COL; // 256
        public const int RELAY_FLAG_ARRAY_SIZE = TOTAL_RELAY_COUNT;
    }
 

    public static class PXI3022Native
    {
        // DLL 函数声明 - 与头文件完全一致的函数名
        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr pxi3022_openDevice(ushort Id);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_reset(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_releaseDevice(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_setTrigSource(UIntPtr vi, ushort trigSource);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_setScaneMode(UIntPtr vi, ushort scaneMode);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_softImmTrig(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_start(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_stop(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_pause(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_continue(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_timerTrigMode(UIntPtr vi, ushort timerTrigMode);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_timerEnabled(UIntPtr vi, ushort timerEnabled);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_timerTrigPeriod(UIntPtr vi, double timerPeriod);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_timerTrig(UIntPtr vi);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_timerStatus(UIntPtr vi, out ushort timeStatus);

        // 注意：函数名在头文件中是 pxi3022_setRelalyFlag1D 和 pxi3022_getRelalyFlag1D
        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "pxi3022_setRelalyFlag1D")]
        public static extern int pxi3022_setRelalyFlag1D(UIntPtr vi, ushort scanIndex, [MarshalAs(UnmanagedType.LPArray, SizeConst = PXI3022Constants.RELAY_FLAG_ARRAY_SIZE)] ushort[] rowColFlag);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "pxi3022_getRelalyFlag1D")]
        public static extern int pxi3022_getRelalyFlag1D(UIntPtr vi, ushort scanIndex, [MarshalAs(UnmanagedType.LPArray, SizeConst = PXI3022Constants.RELAY_FLAG_ARRAY_SIZE)] ushort[] rowColFlag);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_setScanTableNum(UIntPtr vi, uint scanNum);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_getScanTableNum(UIntPtr vi, out uint scanNum);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_setScanTableIRQNum(UIntPtr vi, uint scanIRQNum);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_getWaitingTrigStatus(UIntPtr vi, out ushort statusFlag);

        [DllImport("pxi3022.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi3022_getCurrentScanTablePtr(UIntPtr vi, out uint scanTablePtr);
    }
}
