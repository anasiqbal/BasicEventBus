using System;
using System.Collections.Generic;

namespace BasicEventBus
{
    public sealed class EventBus<TBaseEvent>
    {
        private readonly Dictionary<Type, List<HandlerEntry>> _handlers = new();
        private readonly Dictionary<Type, HashSet<Delegate>> _onceHandlers = new();

        private readonly Queue<Action> _queuedPublishes = new();
        private bool _isDispatching;
        private bool _isConsumed;
        
        public void Subscribe<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : TBaseEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var key = typeof(TEvent);
            if (!_handlers.TryGetValue(key, out List<HandlerEntry> list))
            {
                list = new List<HandlerEntry>();
                _handlers[key] = list;
            }

            int index = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Priority < priority)
                {
                    index = i;
                    break;
                }
            }

            list.Insert(index, new HandlerEntry(priority, handler));
        }

        public void Once<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : TBaseEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            
            var key = typeof(TEvent);
            if (!_onceHandlers.TryGetValue(key, out HashSet<Delegate> onceSet))
            {
                onceSet = new HashSet<Delegate>();
                _onceHandlers[key] = onceSet;
            }

            onceSet.Add(handler);
            Subscribe(handler, priority);
        }

        public IDisposable Scoped<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : TBaseEvent
        {
            Subscribe(handler, priority);
            return new Subscription<TEvent>(this, handler);
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            
            var key = typeof(TEvent);
            if (_handlers.TryGetValue(key, out List<HandlerEntry> list))
            {
                int index = list.FindIndex(e => e.Handler == (Delegate)handler);
                if (index >= 0) list.RemoveAt(index);
                
                if (list.Count == 0)
                    _handlers.Remove(key);
            }

            if (_onceHandlers.TryGetValue(key, out HashSet<Delegate> onceSet))
            {
                onceSet.Remove(handler);
                if (onceSet.Count == 0)
                    _onceHandlers.Remove(key);
            }
        }

        public void Publish<TEvent>(TEvent message) where TEvent : TBaseEvent
        {
            if (_isDispatching)
            {
                _queuedPublishes.Enqueue(() => Dispatch(message));
                return;
            }

            Dispatch(message);

            // Execute pending queues
            // Intentionally using loop instead of recursion
            // in case of large bursts of publish calls, recursion may become expensive
            while (_queuedPublishes.TryDequeue(out var queuedAction))
            {
                queuedAction();
            }
        }

        public void ConsumeCurrent()
        {
            _isConsumed = true;
        }

        private void Dispatch<TEvent>(TEvent message) where TEvent : TBaseEvent
        {
            var key = typeof(TEvent);

            if (!_handlers.TryGetValue(key, out List<HandlerEntry> list) || list.Count == 0)
                return;

            _isDispatching = true;

            try
            {
                HandlerEntry[] snapshot = list.ToArray();

                // Remove once-handlers before invoking. If a handler throws, it still won't fire again.
                if (_onceHandlers.TryGetValue(key, out HashSet<Delegate> onceSet))
                {
                    foreach (var entry in snapshot)
                    {
                        if (!onceSet.Contains(entry.Handler)) continue;

                        onceSet.Remove(entry.Handler);
                        list.Remove(entry);
                    }

                    if (onceSet.Count == 0) _onceHandlers.Remove(key);
                    if (list.Count == 0) _handlers.Remove(key);
                }

                _isConsumed = false;
                List<Exception> failures = null;

                foreach (var entry in snapshot)
                {
                    if (_isConsumed)
                        break;

                    try
                    {
                        ((Action<TEvent>)entry.Handler)(message);
                    }
                    catch (Exception ex)
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                    }
                }

                if (failures != null)
                {
                    var logMessage =
                        $"EventBus.Publish<{typeof(TEvent).Name}>: {failures.Count} of {snapshot.Length} handler(s) threw. See InnerExceptions for details.";
                    throw new AggregateException(logMessage, failures);
                }
            }
            finally
            {
                _isDispatching = false;
            }
        }

        #region Internal Types

        private readonly struct HandlerEntry : IEquatable<HandlerEntry>
        {
            public readonly int Priority;
            public readonly Delegate Handler;

            public HandlerEntry(int priority, Delegate handler)
            {
                Priority = priority;
                Handler = handler;
            }

            public bool Equals(HandlerEntry other)
            {
                return Priority == other.Priority && Equals(Handler, other.Handler);
            }

            public override bool Equals(object obj)
            {
                return obj is HandlerEntry other && Equals(other);
            }

            public override int GetHashCode()
            {
#if NET_STANDARD_2_1
                return HashCode.Combine(Priority, Handler);
#else
                unchecked { return (Priority * 397) ^ (Handler?.GetHashCode() ?? 0); }
#endif
            }
        }
        
        private sealed class Subscription<TEvent> : IDisposable
            where TEvent : TBaseEvent
        {
            private readonly EventBus<TBaseEvent> _bus;
            private readonly Action<TEvent> _handler;
            private bool _disposed;

            public Subscription(EventBus<TBaseEvent> bus, Action<TEvent> handler)
            {
                _bus = bus;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _bus.Unsubscribe(_handler);
            }
        }

        #endregion
    }
}