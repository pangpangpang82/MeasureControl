using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;

namespace MeasureControl.Helpers.SelfInspection
{
    internal interface ISelfInspectionTask
    {
        bool CanHandle(DeviceBase device);

        Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken);
    }
}
