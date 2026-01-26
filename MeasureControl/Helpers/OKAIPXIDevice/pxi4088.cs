using System;
using System.Runtime.InteropServices;

namespace MeasureControl.Helpers.OKAIPXIDevice
{
    public class PXI4088Constants
    {
        /// <summary>DLL文件名常量</summary>
        public const string DllName = "pxi4088.dll";

        /// <summary>触发源 - 软件触发</summary>
        public const int pxi4088_Trig_Source_Soft = 1;
        /// <summary>触发源 - 硬件触发</summary>
        public const int pxi4088_Trig_Source_Hard = 2;

        /// <summary>数据缓冲支持的最大长度</summary>
        public const int pxi4088_Data_Buffer_Length = 2048;
        /// <summary>DMA缓冲个数</summary>
        public const int pxi4088_Data_Dma_Length = 1000 * 1000;
        /// <summary>lvdt/rvdt 通道数 8</summary>
        public const int pxi4088_Lvdt_Rvdt_Ch_Num = 8;

        /// <summary>通道输出方式为Rvdt/Lvdt,焊接的是Rvdt/Lvdt方式电阻</summary>
        public const int pxi4088_Ch_Out_Mode_Rvdt_Lvdt = 0;
        /// <summary>通道输出为旋变,焊接的是旋变方式电阻</summary>
        public const int pxi4088_Ch_Out_Mode_Resolver = 1;

        /// <summary>通道工作于仿真模式</summary>
        public const int pxi4088_Ch_Mode_Sim = 0;
        /// <summary>通道工作于测试模式</summary>
        public const int pxi4088_Ch_Mode_Test = 1;

        /// <summary>外部激励</summary>
        public const int pxi4088_Ch_Exc_Sour_Ext = 1;
        /// <summary>内部激励</summary>
        public const int pxi4088_Ch_Exc_Sour_Int = 0;

        /// <summary>正向输出</summary>
        public const int pxi4088_Ch_Exc_Sour_Pos = 0;
        /// <summary>反向输出</summary>
        public const int pxi4088_Ch_Exc_Sour_Neg = 1;

        /// <summary>单点，静态数据输出</summary>
        public const int pxi4088_Lvdt_Data_Out_Fix = 0;
        /// <summary>动态，数组缓冲输出</summary>
        public const int pxi4088_Lvdt_Data_Out_Buffer = 1;
    }

    /// <summary>
    /// PXI4088 Native 函数封装类
    /// </summary>
    public static class PXI4088Native
    {
        //连接模块，依次为1、2、3……
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr pxi4088_openDevice(ushort Id);

        ////关闭模块，释放资源，模块回到初始状态
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_releaseDevice(UIntPtr vi);

        //复位模块到初始状态,软件触发
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_reset(UIntPtr vi);

        //函数中的vi 模块句柄，由pxi4088_openDevice返回
        //函数中的chIndex 通道索引0～7

        //设置通道工作模式和激励信号源，每个通道可工作于仿真模式输出Lvdt/Rvdt信号和测试模式采集Lvdt/Rvdt信号
        //workMode 工作模式，0	通道工作于仿真模式，1   通道工作于测试模式，默认在仿真模式下
        //excSour 激励源选择，0 内部激励（在该情况不要输入外部激励，输入可能会损坏设备），1外部激励,默认外部激励.
        //VaQuadSel Va信号输出方向 0：正向输出，1：反向输出。VbQuadSel Vb信号输出方向 0：正向输出，1：反向输出
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setMode(UIntPtr vi, ushort chIndex, ushort workMode, ushort excSour, ushort VaQuadSel, ushort VbQuadSel);

        //设置在外部激励信号的传输比，即输出最大信号幅度和输入激励信号的有幅度比例系数，默认为1.0
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setTransRatio(UIntPtr vi, ushort chIndex, double transRatio);

        //激励信号选择CH0通道的外部激励信号标志,当选外部激励时excSelCh0flag为1选择CH0的外部激励作为激励信号，为0时选择本通道的外部激励信号为激励信号
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setSelExcCh0Flag(UIntPtr vi, ushort chIndex, ushort excSelCh0flag);

        //要配置激励信号的输出电压有效值和频率。
        //voltageRms 激励信号电压有效值（1～10Vrms） ,freq 输出信号频率（360～20000）Hz
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setIntExcSig(UIntPtr vi, ushort chIndex, double voltageRms, double freq);

        //设置数据输出方式，outMode数据输出方式， 0=单点(静态)输出，输出填下的固定点数据，1=缓冲区(动态)输出，自动输出数组中的数据
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtDataOutMode(UIntPtr vi, ushort chIndex, ushort outMode);

        //设置相位差，相位延迟单位100ns,范围为1-65535；延迟延迟角度 360*(phaseDelay*100)/(1000、000、000)
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtPhaseDelay(UIntPtr vi, ushort chIndex, ushort phaseDelay);

        //设置Lvdt仿真模式下的输出值
        //Vexc 激励信号电压有效值，pos 输出值,pos=(Va-Vb)/(Va+Vb),pos的有效范围为-1.0～1.0
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtOutPos(UIntPtr vi, ushort chIndex, double pos);

        //直接设置Va，Vb的输出电压有效值
        //Vexc激励信号电压有效值，VaVol A像输出电压有效值，VbVol B像输出电压有效值
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtVaVb(UIntPtr vi, ushort chIndex, double VaVol, double VbVol);

        //设置Lvdt 在4线独立模式下的输出值，4线置(Vsum Vdiff) Vsum=Va+Vb,Vdiff=Va-Vb
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtSumDiff(UIntPtr vi, ushort chIndex, double Vsum, double Vdiff);

        //设置在旋变Va,Vb相位差,为0没有相位差,大于0,Va角度大于Vb角度,小于0,Va角度小于Vb角度。单位度
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setResolverPhaseDiff(UIntPtr vi, ushort chIndex, double degree);

        //设置在旋变角度，角度范围0-360度,单位度
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setResolverOutAngle(UIntPtr vi, ushort chIndex, double degree);

        //设置在缓冲模式下的输出每个位置点的输出频率freq
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtScanFreq(UIntPtr vi, ushort chIndex, double freq);

        //设置在缓冲模式下的输出每个位置点的输出时间间隔s,单位为输出频率freq的倒数
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtScanPeriod(UIntPtr vi, ushort chIndex, double period);

        //设置在缓冲模式下数据输出波形个数。waveOut 波形个数，0 为连续输出。
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtWaveOut(UIntPtr vi, ushort chIndex, ushort waveOut);

        //设置缓冲区中的波形输出数据,dataLength 数据长度，取值1-2048。data 每个点的位置值
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtWaveData(UIntPtr vi, ushort chIndex, uint dataLength, [MarshalAs(UnmanagedType.LPArray)] double[] posdata);

        //设置旋变状态下缓冲区中的波形输出数据,dataLength 数据长度，取值1-2048。data 每个点的位置值,
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setResolverWaveData(UIntPtr vi, ushort chIndex, uint dataLength, [MarshalAs(UnmanagedType.LPArray)] double[] posdata);

        //旋变按波形进行运动,goBackFlag 是否进行往返运动，1进行往返运动，从起始角度运动到终止角度，再从终止角度运动到起始角度，完成一次波形；0从起始角度运动到终止角度，直接跳到起始角度，完成一次波形。
        //dataLength组成一个波形的点数,startDegree起始点角度(度),endDegree终止点角度(度)
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_autoLoadResolverWave(UIntPtr vi, ushort chIndex, ushort goBackFlag, uint dataLength, double startDegree, double endDegree);

        //模拟电机转速，需要把通道置1=缓冲区(动态)输出，speed 电机转速，单位转/分钟
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setResolverMotorSpeed(UIntPtr vi, ushort chIndex, double speed);

        //启动输出,输出Lvdt/Rvdt信号。
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_lvdtStart(UIntPtr vi, ushort chIndex);

        //停止输出,停止Lvdt/Rvdt信号输出。
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_lvdtStop(UIntPtr vi, ushort chIndex);

        //读外部激励信号有效值 ImpRmsVol  激励信号有效值,单位电压
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_getLvdtExcSigRms(UIntPtr vi, ushort chIndex, out double ImpRmsVol);

        //读外部激励信号有频率值，ImpFreqHz 激励信号频率，单位Hz
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_getLvdtExcSigFreq(UIntPtr vi, ushort chIndex, out double ImpFreqHz);

        //得到Va、Vb有效值数据,以及差和比
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_getLvdtRmsVol(UIntPtr vi, ushort chIndex, out double VaRms, out double VbRms, out double sumRatio);

        //加载波形含初始相位,initPhase,初始相位(0-360
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi7016_autoLoadWavePhase(UIntPtr vi, ushort chIndex, ushort waveType, double freq, double amplitude, double dutycycle, double offset, double initPhase);

        //保存用户校准系数，供应用软件使用
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_saveUserGainBaisToISF(UIntPtr vi, ushort chIndex, ushort groupIndex, double scaleA, double scaleB, double scaleC);

        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_readUserGainBaisFromISF(UIntPtr vi, ushort chIndex, ushort groupIndex, out double scaleA, out double scaleB, out double scaleC);

        //该量程档在二次调节时用，如激励信号加了外部放大，在pxi4088_setIntExcSig函数后调用,如果不调用将是自动调节，rangeIndex参数一般为3
        //0=G=0.444(Vrms=2.5V)	1=G=0.333(Vrms=3.3V)	2=G=0.222(Vrms=5.0V)	3=G=0.111(Vrms=10.0V)
        [DllImport(PXI4088Constants.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pxi4088_setLvdtAdcRange(UIntPtr vi, ushort lvdtchIndex, ushort rangeIndex);
    }
}
