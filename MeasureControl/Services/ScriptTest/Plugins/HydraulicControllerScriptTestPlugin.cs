// ============================================================================
// 液压控制器脚本测试插件。
// 配置来源：HydraulicControllerScriptTemplate（行数模板）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.Services.ScriptTest.Runners;
using Prism.Ioc;

namespace MeasureControl.Services.ScriptTest.Plugins
{
    public sealed class HydraulicControllerScriptTestPlugin : IScriptTestPlugin
    {
        public string BoardType => "液压单板";
        public string DisplayName => "液压控制器";
        public string ScriptTitle => HydraulicControllerScriptTemplate.ScriptTitle;
        public IReadOnlyList<FcSpec> Specs => HydraulicControllerScriptTemplate.Specs;
        public (double Min, double Max)? PowerInVoltageRange => (27.0, 29.0);

        public IReadOnlyList<IFcRunner> CreateRunners()
        {
            return new IFcRunner[]
            {
                new Hc1Runner(),
                new Hc2Runner(),
                new Hc3Runner(),
                new Hc4Runner(),
                new Hc5Runner(),
                new Hc6Runner(),
                new Hc7Runner(),
                new Hc8Runner(),
                new Hc9Runner(),
                new Hc10Runner(),
            };
        }

        public async Task TeardownAsync(Action<string> log, CancellationToken cancellationToken)
        {
            try
            {
                var pwr = ContainerLocator.Container.Resolve<IBoardPowerService>();
                if (pwr?.IsPowered == true)
                {
                    log?.Invoke("[HC Teardown] 液压控制器 脚本测试完成，正在下电...");
                    await pwr.PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
                    log?.Invoke("[HC Teardown] 下电完成");
                }
                else
                {
                    log?.Invoke("[HC Teardown] 液压控制器 电源已处于未上电状态");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[HC Teardown] 下电异常: {ex.Message}");
            }
        }
    }
}
