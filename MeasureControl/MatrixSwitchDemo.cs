using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeasureControl.Services; // 添加服务命名空间

class MatrixSwitchDemo
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("PXI-2601 矩阵开关并发控制 Demo");
        Console.WriteLine("================================");

        var svc = MatrixControlService.Instance;

        try
        {
            // 并发示例：同一IP，不同slot（不同端口 50200 + slotIndex）
            // 任务列表：多路并行连接，然后等待全部完成，再并行断开
            var operations = new List<(string inNode, string outNode, int slot, string ip)>
            {
                ("I1","O1", 4, "192.168.1.3"), // port 50201
                ("I2","O2", 5, "192.168.1.3"), // port 50202
                ("I3","O3", 6, "192.168.1.3"), // port 50203
            };

            Console.WriteLine("并发：同时向不同端口发起连接请求...");
            var connectTasks = operations.Select(op =>
                Task.Run(async () =>
                {
                    Console.WriteLine($"开始连接 {op.inNode} -> {op.outNode} (slot={op.slot}, ip={op.ip})");
                    bool ok = await svc.ConnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip);
                    Console.WriteLine(ok
                        ? $"连接成功: {op.inNode}->{op.outNode} (slot={op.slot})"
                        : $"连接失败: {op.inNode}->{op.outNode} (slot={op.slot})");
                    return (op, ok);
                })
            ).ToArray();

            var connectResults = await Task.WhenAll(connectTasks);

            Console.WriteLine("\n所有并发连接尝试已完成，结果：");
            foreach (var r in connectResults)
                Console.WriteLine($"{r.op.inNode}->{r.op.outNode} (slot={r.op.slot}) => {(r.ok ? "OK" : "FAIL")}");

            // 等待一会儿模拟工作
            await Task.Delay(500);

            Console.WriteLine("\n并发：同时向不同端口发起断开请求...");
            var disconnectTasks = operations.Select(op =>
                Task.Run(async () =>
                {
                    bool ok = await svc.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip);
                    Console.WriteLine(ok
                        ? $"断开成功: {op.inNode}->{op.outNode} (slot={op.slot})"
                        : $"断开失败: {op.inNode}->{op.outNode} (slot={op.slot})");
                    return (op, ok);
                })
            ).ToArray();

            var disconnectResults = await Task.WhenAll(disconnectTasks);
            Console.WriteLine("\n所有并发断开尝试已完成。");

            // 示范：混合并发（部分连接、部分原始命令）
            Console.WriteLine("\n混合并发示例：并发发送原始命令到不同端口...");
            var rawTasks = operations.Select(op =>
                svc.SendMatrixCommandAsync(op.inNode, op.outNode, 0 /*connect*/, op.slot, op.ip)
            ).ToArray();

            var rawResults = await Task.WhenAll(rawTasks);
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                Console.WriteLine(rawResults[i]
                    ? $"原始命令成功: {op.inNode}->{op.outNode} (slot={op.slot})"
                    : $"原始命令失败: {op.inNode}->{op.outNode} (slot={op.slot})");
            }

            Console.WriteLine("\n并发 Demo 完成。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生异常: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            // 推荐在程序退出时释放单例持有的连接
            try { svc.Dispose(); } catch { }
        }
    }
}