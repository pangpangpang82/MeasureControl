// ============================================================================
// 加放油控制器脚本测试插件。
// 配置来源：FuelControllerScriptTemplate（行数模板 + POWER_IN 档位）。
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
    public sealed class FuelControllerScriptTestPlugin : IScriptTestPlugin
    {
        public string BoardType => "加放油单板";
        public string DisplayName => "加放油控制器";
        public string ScriptTitle => FuelControllerScriptTemplate.ScriptTitle;
        public IReadOnlyList<FcSpec> Specs => FuelControllerScriptTemplate.Specs;

        // 加放油 POWER_IN 合法电压区间（闭区间）。覆盖原先写死的 {18, 28, 32.2} 三档位白名单，
        // 允许脚本里填任意在此区间内的电压（例如 4V/23V 等特殊工况）。
        public (double Min, double Max)? PowerInVoltageRange => (18.0, 32.2);

        public IReadOnlyList<IFcRunner> CreateRunners()
        {
            return new IFcRunner[]
            {
                new Fc1StubRunner(),
                new Fc2StubRunner(),
                new Fc3StubRunner(),
                new Fc4StubRunner(),
                new Fc5StubRunner(),
                new Fc6StubRunner(),
                new Fc7StubRunner(),
                new Fc8StubRunner(),
            };
        }

        public async Task TeardownAsync(Action<string> log, CancellationToken cancellationToken)
        {
            try
            {
                var pwr = ContainerLocator.Container.Resolve<IBoardPowerService>();
                if (pwr?.IsPowered == true)
                {
                    log?.Invoke("[FC Teardown] 加放油控制器 脚本测试完成，正在下电...");
                    await pwr.PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
                    log?.Invoke("[FC Teardown] 下电完成");
                }
                else
                {
                    pwr?.SetPoweredState(false);
                    log?.Invoke("[FC Teardown] 加放油控制器 电源已处于未上电状态");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[FC Teardown] 下电异常: {ex.Message}");
            }
        }
    }
}
