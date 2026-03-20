using System;
using System.Collections.Generic;
using System.Linq;

namespace MeasureControl.Services
{
    /// <summary>
    /// 文档生命周期管理服务实现
    /// </summary>
    public class DocumentManagerService : IDocumentManagerService
    {
        private readonly object _lockObject = new object();
        private readonly Dictionary<string, DocumentState> _documentStates = new Dictionary<string, DocumentState>();

        /// <summary>
        /// 打开文档（记录到指定Host）
        /// </summary>
        public void OpenDocument(string documentId, string viewType, HostType hostType, string pageKey = null)
        {
            if (string.IsNullOrEmpty(documentId))
                return;

            lock (_lockObject)
            {
                if (_documentStates.ContainsKey(documentId))
                {
                    // 更新现有文档的状态
                    var state = _documentStates[documentId];
                    state.CurrentHost = hostType;
                    if (!string.IsNullOrEmpty(pageKey))
                    {
                        state.PageKey = pageKey;
                    }
                }
                else
                {
                    // 创建新文档状态
                    _documentStates[documentId] = new DocumentState
                    {
                        DocumentId = documentId,
                        ViewType = viewType,
                        CurrentHost = hostType,
                        PageKey = pageKey
                    };
                }
            }
        }

        /// <summary>
        /// 关闭文档（从所有Host中移除）
        /// </summary>
        public void CloseDocument(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
                return;

            lock (_lockObject)
            {
                _documentStates.Remove(documentId);
            }
        }

        /// <summary>
        /// 检查文档是否打开
        /// </summary>
        public bool IsDocumentOpen(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
                return false;

            lock (_lockObject)
            {
                return _documentStates.ContainsKey(documentId);
            }
        }

        /// <summary>
        /// 获取文档的打开状态信息
        /// </summary>
        public DocumentState GetDocumentState(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
                return null;

            lock (_lockObject)
            {
                return _documentStates.TryGetValue(documentId, out var state) ? state : null;
            }
        }

        /// <summary>
        /// 获取所有打开的文档ID
        /// </summary>
        public IEnumerable<string> GetOpenDocuments()
        {
            lock (_lockObject)
            {
                return _documentStates.Keys.ToList();
            }
        }

        /// <summary>
        /// 根据DocumentId查找对应的pageKey（用于兼容旧系统）
        /// </summary>
        public string GetPageKeyByDocumentId(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
                return null;

            lock (_lockObject)
            {
                return _documentStates.TryGetValue(documentId, out var state) ? state.PageKey : null;
            }
        }

        /// <summary>
        /// 清空所有文档状态
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _documentStates.Clear();
            }
        }
    }
}

