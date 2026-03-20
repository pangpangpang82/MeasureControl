namespace MeasureControl.ViewModels
{
    /// <summary>
    /// 简单的关闭前确认接口，用于阻止未保存内容被关闭
    /// </summary>
    public interface ICloseGuard
    {
        /// <summary>
        /// 在关闭面板或切换内容前调用，返回false可阻止关闭
        /// </summary>
        bool CanClose();
    }
}
