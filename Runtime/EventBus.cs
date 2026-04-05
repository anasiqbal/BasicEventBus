using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BasicEventBus
{
    public class EventBus<TBaseEvent> : IEventBus<TBaseEvent>
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly Dictionary<Type, HashSet<Delegate>> _onceHandlers = new();

        private readonly Queue<Action> _queuedPublishes = new();
        private bool _isDispatching;
        private bool _isConsumed;
        
        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent
        {
            Assert.IsNotNull(handler);

            var key = typeof(TEvent);
            if (!_handlers.TryGetValue(key, out List<Delegate> list))
            {
                list = new List<Delegate>();
                _handlers[key] = list;
            }

            list.Add(handler);
        }

        public void Once<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent
        {
            Assert.IsNotNull(handler);
            var key = typeof(TEvent);

            if (!_onceHandlers.TryGetValue(key, out HashSet<Delegate> onceSet))
            {
                onceSet = new HashSet<Delegate>();
                _onceHandlers[key] = onceSet;
            }

            onceSet.Add(handler);
            Subscribe(handler);
        }

        public IDisposable Scoped<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent
        {
            Subscribe(handler);
            return new Subscription<TEvent>(this, handler);
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent
        {
            Assert.IsNotNull(handler);
            var key = typeof(TEvent);

            if (_handlers.TryGetValue(key, out List<Delegate> list))
            {
                list.Remove(handler);
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

            if (!_handlers.TryGetValue(key, out List<Delegate> list) || list.Count == 0)
                return;

            _isDispatching = true;

            try
            {
                Delegate[] snapshot = list.ToArray();

                // Remove once-handlers before invoking. If a handler throws, it still won't fire again.
                if (_onceHandlers.TryGetValue(key, out HashSet<Delegate> onceSet))
                {
                    foreach (var d in snapshot)
                    {
                        if (!onceSet.Contains(d)) continue;

                        onceSet.Remove(d);
                        list.Remove(d);
                    }

                    if (onceSet.Count == 0) _onceHandlers.Remove(key);
                    if (list.Count == 0) _handlers.Remove(key);
                }

                _isConsumed = false;
                List<Exception> failures = null;

                foreach (var d in snapshot)
                {
                    if (_isConsumed)
                        break;

                    try
                    {
                        ((Action<TEvent>)d)(message);
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