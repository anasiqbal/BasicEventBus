using System;

namespace BasicEventBus
{
    public interface IEventBus<in TBaseEvent>
    {
        /// <summary>
        /// Subscribe to an Event Type.
        /// </summary>
        void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent;
        
        /// <summary>
        /// Subscribe once to an Event Type. Auto-Unsubscribe before firing.
        /// </summary>
        void Once<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent;
        
        /// <summary>
        /// Subscribe to an event and get an IDisposable token.<br/>
        /// Disposing the token auto-unsubscribes the handler. It is safe to dispose multiple times.
        /// </summary>
        /// <returns>IDisposable Token</returns>
        IDisposable Scoped<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent;
        
        /// <summary>
        /// Remove a previously registered handler. Does nothing if the handler was never subscribed.
        /// </summary>
        void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent;
        
        /// <summary>
        /// Publish an event to all handler of `TEvent`.<br/>
        /// If a dispatch is in process, event is queued.<br/>
        /// Execution continues even if a handler throws an exception.
        /// Exceptions are collected and thrown after all handlers are done or <see cref="ConsumeCurrent"/> is called.
        /// </summary>
        void Publish<TEvent>(TEvent message) where TEvent : TBaseEvent;
        
        /// <summary>
        /// Stop propagation of currently dispatching event.
        /// </summary>
        void ConsumeCurrent();
    }
}