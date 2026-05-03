// ============================================================================
// 脚本测试插件抽象。
// 每个支持脚本测试的单板对应一个 IScriptTestPlugin 实现：
//   - 描述板型/显示名/脚本标题/FC 行数模板/POWER_IN 合法档位；
//   - 提供 Runner 工厂。
// 新增板卡只需实现本接口并在 ScriptTestFeature 中注册，即可出现在右键菜单里。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.Services.ScriptTest.Runners;

namespace MeasureControl.Services.ScriptTest.Plugins
{
    public interface IScriptTestPlugin
    {
        /// <summary>右键菜单匹配用的板型标签，如"加放油单板"。与 ProjectItem.Tag/Name 一致。</summary>
        string BoardType { get; }

        /// <summary>面向用户的友好名称，用于对话框标题/日志，如"加放油控制器"。</summary>
        string DisplayName { get; }

        /// <summary>脚本 xlsx 第一行第一格应匹配的标题字面量。</summary>
        string ScriptTitle { get; }

        /// <summary>FC 行数模板（顺序即脚本中 FC 出现顺序）。</summary>
        IReadOnlyList<FcSpec> Specs { get; }

        /// <summary>
        /// POWER_IN 合法电压区间（闭区间 [Min, Max]）。脚本校验 I 列时用。
        /// 返回 null 表示不做区间校验（只要求能解析为数值）。
        /// </summary>
        (double Min, double Max)? PowerInVoltageRange { get; }

        /// <summary>为一次脚本运行创建 Runner 集合（TestId → Runner）。</summary>
        IReadOnlyList<IFcRunner> CreateRunners();

        /// <summary>
        /// 所有测试项执行完毕（含中止/异常）后调用，用于下电/资源清理。
        /// 实现中应捕获自身异常，不向上抛出。
        /// 使用 CancellationToken.None 以保证即使测试被取消也能执行下电。
        /// </summary>
        Task TeardownAsync(Action<string> log, CancellationToken cancellationToken);
    }
}
