using System.Collections.Generic;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 手册注册表（模型名/关键字 -> 手册文件或 URL）
    /// 说明：
    /// - 当设备实例的 ManualUrl 为空时，ViewModel 会尝试在此注册表中查找对应手册并打开。
    /// </summary>
    public static class ManualRegistry
    {
        // 简单内存映射，后续可改为从配置文件或资源加载
        private static readonly Dictionary<string, string> _manualMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            {"PXIe-2722G2", "Resources/Manuals/1-PXIe-2722G2.pdf"}, // 18槽机箱
            {"PXIe-2519G2", "Resources/Manuals/2-PXIe-2519G2.pdf"}, // 9槽机箱

            {"PXIe-3987", "Resources/Manuals/3-PXIe-3987.pdf"}, // 控制器

            {"PXI-3022", "Resources/Manuals/4-PXI-3022.pdf"}, // 矩阵开关
            {"PXI-2601", "Resources/Manuals/5-PXI-2601.pdf"}, // 矩阵开关

            {"PXIe-7131", "Resources/Manuals/6-PXIe-7131.pdf"}, //模拟量输入输出
            {"PXIe-9774",  "Resources/Manuals/7-PXIe-9774.pdf"}, // 模拟量采集
            {"MT-X532", "Resources/Manuals/8-MT-X532.pdf"}, // 模拟量输出
            {"PXI-7012", "Resources/Manuals/9-PXI-7012.pdf"}, // 电阻输出
            {"PXI-4087A", "Resources/Manuals/10-PXI-4087A.pdf"}, // LVDT/RVDT
            {"PXI-4087C", "Resources/Manuals/11-PXI-4087C.pdf"}, // 旋转变压器
            {"PXI-4004", "Resources/Manuals/12-PXI-4004.pdf"}, // CAN
            {"PXIe-4227", "Resources/Manuals/13-PXIe-4227.pdf"}, //429
            {"PXI-4332", "Resources/Manuals/14-PXI-4332.pdf"}, // 1553B
            {"MIL-1394B", "Resources/Manuals/15-MIL1394B.pdf"}, // 1394B
            {"MT-X970", "Resources/Manuals/16-MT-X970.pdf"}, // LVDS
            
            {"6314A", "Resources/Manuals/20-Chroma6314A.pdf"}, // 串口

            {"DM3068", "Resources/Manuals/21-DM3068.pdf"}, // 多用表
            {"DG1032Z", "Resources/Manuals/22-DG1032Z.pdf"}, // 信号发生器
            {"53220A", "Resources/Manuals/25-53220A.pdf"}, // 频率计 
            {"IT-N6332B", "Resources/Manuals/17-IT-N6332B.pdf"}, // 程控电源
        };

        public static void Register(string key, string manualUrl)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _manualMap[key] = manualUrl ?? string.Empty;
        }

        public static bool TryGetManual(string key, out string manualUrl)
        {
            manualUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return false;
            return _manualMap.TryGetValue(key, out manualUrl) && !string.IsNullOrWhiteSpace(manualUrl);
        }

        public static string GetManualUrl(string key)
        {
            if (TryGetManual(key, out var url)) return url;
            return string.Empty;
        }
    }
}


