using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeasureControl.Events;

namespace MeasureControl.Services
{
    /// <summary>
    /// Dispatcher that allows PXI2601 viewmodels to register per-slot handlers
    /// and allows PxiChassisViewModel to dispatch remote matrix commands.
    /// Returns true if a handler was found and invoked.
    /// </summary>
    public class RemoteMatrixCommandDispatcher
    {
        private static readonly Lazy<RemoteMatrixCommandDispatcher> _instance =
            new Lazy<RemoteMatrixCommandDispatcher>(() => new RemoteMatrixCommandDispatcher());

        public static RemoteMatrixCommandDispatcher Instance => _instance.Value;

        // slotIndex -> stack of handlers (top wins)
        private readonly ConcurrentDictionary<int, ConcurrentStack<Func<RemoteMatrixCommandEventArgs, Task<bool>>>> _handlers
            = new ConcurrentDictionary<int, ConcurrentStack<Func<RemoteMatrixCommandEventArgs, Task<bool>>>>();

        private RemoteMatrixCommandDispatcher() { }

        public bool Register(int slotIndex, Func<RemoteMatrixCommandEventArgs, Task<bool>> handler)
        {
            if (handler == null) return false;
            var stack = _handlers.GetOrAdd(slotIndex, _ => new ConcurrentStack<Func<RemoteMatrixCommandEventArgs, Task<bool>>>());
            stack.Push(handler);
            return true;
        }

        public bool Unregister(int slotIndex)
        {
            if (!_handlers.TryGetValue(slotIndex, out var stack))
                return false;

            var popped = stack.TryPop(out _);
            if (!popped)
                return false;

            if (stack.IsEmpty)
            {
                _handlers.TryRemove(slotIndex, out _);
            }
            return true;
        }

        /// <summary>
        /// Dispatch the command to registered handler for the slot.
        /// Returns true if a handler existed and was invoked.
        /// </summary>
        public async Task<bool> DispatchAsync(RemoteMatrixCommandEventArgs args)
        {
            if (args == null) return false;
            if (!_handlers.TryGetValue(args.SlotIndex, out var stack))
                return false;

            if (!stack.TryPeek(out var handler) || handler == null)
                return false;

            try
            {
                return await handler(args).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }
    }
}

