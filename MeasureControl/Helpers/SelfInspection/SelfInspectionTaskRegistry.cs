using System;
using System.Collections.Generic;
using MeasureControl.Models.Devices;

namespace MeasureControl.Helpers.SelfInspection
{
    internal static class SelfInspectionTaskRegistry
    {
        private static readonly List<ISelfInspectionTask> _tasks = new List<ISelfInspectionTask>
        {
            new PXIe7131DidoSelfInspectionTask(),
            new ART1553BSelfInspectionTask(),
            new Mil1394BSelfInspectionTask(),
            new MTX970LvdsSelfInspectionTask(),
            new PXI4087ASelfInspectionTask(),
            new PXI4087CSelfInspectionTask()
        };

        public static ISelfInspectionTask Resolve(DeviceBase device)
        {
            if (device == null)
            {
                return null;
            }

            foreach (var task in _tasks)
            {
                try
                {
                    if (task != null && task.CanHandle(device))
                    {
                        return task;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
