using System;
using System.Runtime.InteropServices;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// OLE 消息过滤器，用于处理 COM 调用被拒绝 (RPC_E_CALL_REJECTED) 的情况。
    /// 在进行 Excel COM 操作前调用 Register()，操作完成后调用 Revoke()。
    /// </summary>
    public class OleMessageFilter : IOleMessageFilter
    {
        private const int SERVERCALL_ISHANDLED = 0;
        private const int SERVERCALL_RETRYLATER = 2;
        private const int PENDINGMSG_WAITDEFPROCESS = 2;

        [DllImport("ole32.dll")]
        private static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);

        /// <summary>
        /// 注册消息过滤器，在 COM 操作前调用
        /// </summary>
        public static void Register()
        {
            IOleMessageFilter newFilter = new OleMessageFilter();
            CoRegisterMessageFilter(newFilter, out _);
        }

        /// <summary>
        /// 注销消息过滤器，在 COM 操作完成后调用
        /// </summary>
        public static void Revoke()
        {
            CoRegisterMessageFilter(null, out _);
        }

        /// <summary>
        /// 处理传入调用
        /// </summary>
        int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        {
            return SERVERCALL_ISHANDLED;
        }

        /// <summary>
        /// 处理调用被拒绝的情况 - 返回值 > 0 表示等待指定毫秒后重试
        /// </summary>
        int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        {
            if (dwRejectType == SERVERCALL_RETRYLATER)
            {
                // 等待 100 毫秒后重试
                return 100;
            }
            // 立即重试
            return 99;
        }

        /// <summary>
        /// 处理消息挂起
        /// </summary>
        int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        {
            return PENDINGMSG_WAITDEFPROCESS;
        }
    }

    /// <summary>
    /// OLE 消息过滤器接口
    /// </summary>
    [ComImport]
    [Guid("00000016-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }
}
