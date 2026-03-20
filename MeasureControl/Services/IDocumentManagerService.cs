using System.Collections.Generic;

namespace MeasureControl.Services
{
    /// <summary>
    /// 文档生命周期管理服务接口
    /// </summary>
    public interface IDocumentManagerService
    {
        // 打开文档（记录到指定Host）
        void OpenDocument(string documentId, string viewType, HostType hostType, string pageKey = null);
        
        // 关闭文档（从所有Host中移除）
        void CloseDocument(string documentId);
        
        // 检查文档是否打开
        bool IsDocumentOpen(string documentId);
        
        // 获取文档的打开状态信息
        DocumentState GetDocumentState(string documentId);
        
        // 获取所有打开的文档ID
        IEnumerable<string> GetOpenDocuments();
        
        // 根据DocumentId查找对应的pageKey（用于兼容旧系统）
        string GetPageKeyByDocumentId(string documentId);
        
        // 清空所有文档状态
        void Clear();
    }

    /// <summary>
    /// 文档宿主类型
    /// </summary>
    public enum HostType
    {
        MainRegion,      // 主区域
        FloatingWindow,  // 浮动窗口
        NavigationButton, // 导航按钮（已打开但可能未激活）
        Minimized        // 最小化
    }

    /// <summary>
    /// 文档状态信息
    /// </summary>
    public class DocumentState
    {
        public string DocumentId { get; set; }
        public string ViewType { get; set; }
        public HostType CurrentHost { get; set; }
        public string PageKey { get; set; } // 兼容旧系统
    }
}

