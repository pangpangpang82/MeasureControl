using System;
using System.Runtime.InteropServices;

namespace MeasureControl.Helpers.OKAIPXIDevice
{
    public class PXI4087Constants
    {
        public const int pxi4087_Trig_Source_Soft = 1;
        public const int pxi4087_Trig_Source_Hard = 2;
        public const int pxi4087_Data_Buffer_Length = 2048;
        public const int pxi4087_Data_Dma_Length = 1000 * 1000;
        public const int pxi4087_Lvdt_Rvdt_Ch_Num = 8;
        public const int pxi4087_Ch_Out_Mode_Rvdt_Lvdt = 0;
        public const int pxi4087_Ch_Out_Mode_Resolver = 1;
        public const int pxi4087_Ch_Mode_Sim = 0;
        public const int pxi4087_Ch_Mode_Test = 1;
        public const int pxi4087_Ch_Exc_Sour_Ext = 1;
        public const int pxi4087_Ch_Exc_Sour_Int = 0;
        public const int pxi4087_Ch_Exc_Sour_Pos = 0;
        public const int pxi4087_Ch_Exc_Sour_Neg = 1;
        public const int pxi4087_Lvdt_Data_Out_Fix = 0;
        public const int pxi4087_Lvdt_Data_Out_Buffer = 1;
    }

    public class PXI4087Native
    {
        // DLL 函数声明
        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr pxi4087_openDevice(ushort Id);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_releaseDevice(UIntPtr vi);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_reset(UIntPtr vi);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setMode(UIntPtr vi, ushort chIndex, ushort workMode,
            ushort excSour, ushort VaQuadSel, ushort VbQuadSel);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setTransRatio(UIntPtr vi, ushort chIndex, double transRatio);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setSelExcCh0Flag(UIntPtr vi, ushort chIndex, ushort excSelCh0flag);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setIntExcSig(UIntPtr vi, ushort chIndex, double voltageRms, double freq);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtDataOutMode(UIntPtr vi, ushort chIndex, ushort outMode);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtPhaseDelay(UIntPtr vi, ushort chIndex, ushort phaseDelay);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtOutPos(UIntPtr vi, ushort chIndex, double pos);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtVaVb(UIntPtr vi, ushort chIndex, double VaVol, double VbVol);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtSumDiff(UIntPtr vi, ushort chIndex, double Vsum, double Vdiff);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setResolverPhaseDiff(UIntPtr vi, ushort chIndex, double degree);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setResolverOutAngle(UIntPtr vi, ushort chIndex, double degree);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtScanFreq(UIntPtr vi, ushort chIndex, double freq);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtScanPeriod(UIntPtr vi, ushort chIndex, double period);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtWaveOut(UIntPtr vi, ushort chIndex, ushort waveOut);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtWaveData(UIntPtr vi, ushort chIndex, uint dataLength,
            [In] double[] posdata);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setResolverWaveData(UIntPtr vi, ushort chIndex, uint dataLength,
            [In] double[] posdata);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_autoLoadResolverWave(UIntPtr vi, ushort chIndex, ushort goBackFlag,
            uint dataLength, double startDegree, double endDegree);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setResolverMotorSpeed(UIntPtr vi, ushort chIndex, double speed);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_lvdtStart(UIntPtr vi, ushort chIndex);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_lvdtStop(UIntPtr vi, ushort chIndex);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_getLvdtExcSigRms(UIntPtr vi, ushort chIndex, out double ImpRmsVol);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_getLvdtExcSigFreq(UIntPtr vi, ushort chIndex, out double ImpFreqHz);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_getLvdtRmsVol(UIntPtr vi, ushort chIndex,
            out double VaRms, out double VbRms, out double sumRatio);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi7016_autoLoadWavePhase(UIntPtr vi, ushort chIndex, ushort waveType,
            double freq, double amplitude, double dutycycle, double offset, double initPhase);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_saveUserGainBaisToISF(UIntPtr vi, ushort chIndex,
            ushort groupIndex, double scaleA, double scaleB, double scaleC);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_readUserGainBaisFromISF(UIntPtr vi, ushort chIndex,
            ushort groupIndex, out double scaleA, out double scaleB, out double scaleC);

        [DllImport("pxi4087.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4087_setLvdtAdcRange(UIntPtr vi, ushort lvdtchIndex, ushort rangeIndex);
    }
}

