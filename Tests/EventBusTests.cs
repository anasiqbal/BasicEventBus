using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BasicEventBus.Tests
{
    public class EventBusTests
    {
        private EventBus<ITestEvent> _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new EventBus<ITestEvent>();
        }

        #region Basic Subscribe/ Unsubscribe Tests

        [Test]
        public void Subscribe_SingleHandler_ReceivesEvent()
        {
            PlayerDiedEvent? received = null;
            _bus.Subscribe<PlayerDiedEvent>(e => received = e);

            _bus.Publish(new PlayerDiedEvent { Score = 42 });

            Assert.IsNotNull(received);
            Assert.AreEqual(42, received.Value.Score);
        }

        [Test]
        public void Subscribe_MultipleHandlers_FireInSubscriptionOrder()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(3));
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1));
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(2));

            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 3, 1, 2 }, results);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var count = 0;
            void Handler(PlayerDiedEvent e) => count++;

            _bus.Subscribe<PlayerDiedEvent>(Handler);
            _bus.Publish(new PlayerDiedEvent());
            _bus.Unsubscribe<PlayerDiedEvent>(Handler);
            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Unsubscribe_HandlerNotSubscribed_DoesNotThrow()
        {
            void Handler(PlayerDiedEvent e)
            {
            }

            Assert.DoesNotThrow(() => _bus.Unsubscribe<PlayerDiedEvent>(Handler));
        }

        [Test]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bus.Publish(new PlayerDiedEvent()));
        }

        [Test]
        public void Subscribe_DifferentEventTypes_DoNotInterfere()
        {
            var playerDiedFired = false;
            var gamePausedFired = false;

            _bus.Subscribe<PlayerDiedEvent>(_ => playerDiedFired = true);
            _bus.Subscribe<GamePausedEvent>(_ => gamePausedFired = true);

            _bus.Publish(new PlayerDiedEvent());

            Assert.IsTrue(playerDiedFired);
            Assert.IsFalse(gamePausedFired);
        }

        #endregion

        #region Priority

        [Test]
        public void Subscribe_HigherPriority_FiresFirst()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1)); // default priority 0
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(2), priority: 10);

            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 2, 1 }, results);
        }

        [Test]
        public void Subscribe_MixedPriorities_FireHighToLow()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(0), priority: 0);
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(10), priority: 10);
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(-5), priority: -5);
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(5), priority: 5);

            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 10, 5, 0, -5 }, results);
        }

        [Test]
        public void Subscribe_EqualPriority_FiresInSubscriptionOrder()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1), priority: 5);
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(2), priority: 5);
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(3), priority: 5);

            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 1, 2, 3 }, results);
        }

        [Test]
        public void Subscribe_NegativePriority_FiresAfterDefault()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(-1), priority: -1); // subscribed first
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(0)); // default priority 0

            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 0, -1 }, results);
        }

        [Test]
        public void Once_RespectsPriority()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1));
            _bus.Once<PlayerDiedEvent>(_ => results.Add(2), priority: 10);

            _bus.Publish(new PlayerDiedEvent());
            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 2, 1, 1 }, results);
        }

        [Test]
        public void Scoped_RespectsPriority()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1));
            var token = _bus.Scoped<PlayerDiedEvent>(_ => results.Add(2), priority: 10);

            _bus.Publish(new PlayerDiedEvent());
            token.Dispose();
            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 2, 1, 1 }, results);
        }

        [Test]
        public void ConsumeCurrent_HighPriorityHandlerConsumes_LowerPriorityHandlersDoNotFire()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1)); // default priority 0
            _bus.Subscribe<PlayerDiedEvent>(_ =>
            {
                results.Add(2);
                _bus.ConsumeCurrent();
            }, priority: 10);

            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(new[] { 2 }, results);
        }

        #endregion

        #region Once

        [Test]
        public void Once_FiresExactlyOnce()
        {
            var count = 0;
            _bus.Once<PlayerDiedEvent>(_ => count++);

            _bus.Publish(new PlayerDiedEvent());
            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Once_HandlerThrows_DoesNotFireAgain()
        {
            var count = 0;
            _bus.Once<PlayerDiedEvent>(_ =>
            {
                count++;
                throw new InvalidOperationException("boom");
            });

            Assert.Throws<AggregateException>(() => _bus.Publish(new PlayerDiedEvent()));
            Assert.DoesNotThrow(() => _bus.Publish(new PlayerDiedEvent()));
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Once_ManualUnsubscribeBeforeFire_DoesNotFire()
        {
            var count = 0;
            void Handler(PlayerDiedEvent e) => count++;

            _bus.Once<PlayerDiedEvent>(Handler);
            _bus.Unsubscribe<PlayerDiedEvent>(Handler);
            _bus.Publish(new PlayerDiedEvent());

            Assert.AreEqual(0, count);
        }

        #endregion

        #region Scoped

        [Test]
        public void Scoped_ReceivesEventWhileScopeAlive()
        {
            var count = 0;
            var token = _bus.Scoped<PlayerDiedEvent>(_ => count++);
 
            _bus.Publish(new PlayerDiedEvent());
            token.Dispose();
 
            Assert.AreEqual(1, count);
        }
 
        [Test]
        public void Scoped_DoesNotReceiveEventAfterDispose()
        {
            var count = 0;
            var token = _bus.Scoped<PlayerDiedEvent>(_ => count++);
 
            token.Dispose();
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(0, count);
        }
 
        [Test]
        public void Scoped_DoubleDispose_DoesNotThrow()
        {
            var token = _bus.Scoped<PlayerDiedEvent>(_ => { });
            token.Dispose();
            Assert.DoesNotThrow(() => token.Dispose());
        }

        #endregion

        #region Consume Events Tests

        [Test]
        public void ConsumeCurrent_StopsRemainingHandlers()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1));
            _bus.Subscribe<PlayerDiedEvent>(_ =>
            {
                results.Add(2);
                _bus.ConsumeCurrent();
            });
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(3));
 
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(new[] { 1, 2 }, results);
        }
 
        [Test]
        public void ConsumeCurrent_DoesNotSuppressEarlierExceptions()
        {
            _bus.Subscribe<PlayerDiedEvent>(_ => throw new InvalidOperationException());
            _bus.Subscribe<PlayerDiedEvent>(_ => _bus.ConsumeCurrent());
 
            var agg = Assert.Throws<AggregateException>(() => _bus.Publish(new PlayerDiedEvent()));
            Assert.AreEqual(1, agg.InnerExceptions.Count);
        }
 
        [Test]
        public void ConsumeCurrent_OutsideDispatch_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bus.ConsumeCurrent());
        }
 
        [Test]
        public void ConsumeCurrent_ResetsForNextPublish()
        {
            var count = 0;
            _bus.Subscribe<PlayerDiedEvent>(_ => { _bus.ConsumeCurrent(); });
            _bus.Subscribe<PlayerDiedEvent>(_ => count++);
            _bus.Subscribe<GamePausedEvent>(_ => count++);
 
            _bus.Publish(new PlayerDiedEvent()); // consumed — count stays 0
            _bus.Publish(new GamePausedEvent()); // fresh cycle — count becomes 1
 
            Assert.AreEqual(1, count);
        }

        #endregion

        #region Event Queuing Tests

        [Test]
        public void Publish_DuringDispatch_IsQueued_NotImmediate()
        {
            var results = new List<string>();
 
            _bus.Subscribe<PlayerDiedEvent>(_ =>
            {
                results.Add("A-start");
                _bus.Publish(new GamePausedEvent());
                results.Add("A-end");
            });
            _bus.Subscribe<GamePausedEvent>(_ => results.Add("B"));
 
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(new[] { "A-start", "A-end", "B" }, results);
        }
 
        [Test]
        public void Publish_QueuedEvents_ProcessedFIFO()
        {
            var results = new List<string>();
 
            _bus.Subscribe<PlayerDiedEvent>(_ =>
            {
                _bus.Publish(new ChainEvent { Label = "first" });
                _bus.Publish(new ChainEvent { Label = "second" });
            });
            _bus.Subscribe<ChainEvent>(e => results.Add(e.Label));
 
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(new[] { "first", "second" }, results);
        }
 
        [Test]
        public void Publish_QueuedEvent_CanItselfQueueFurtherEvents()
        {
            var results = new List<string>();
 
            _bus.Subscribe<PlayerDiedEvent>(_ =>
            {
                results.Add("root");
                _bus.Publish(new ChainEvent { Label = "level-1" });
            });
 
            _bus.Subscribe<ChainEvent>(e =>
            {
                results.Add(e.Label);
                if (e.Label == "level-1")
                    _bus.Publish(new ChainEvent { Label = "level-2" });
            });
 
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(new[] { "root", "level-1", "level-2" }, results);
        }

        #endregion

        #region Modifications During Dispatch Tests

        [Test]
        public void Publish_HandlerUnsubscribesItself_OtherHandlersStillFire()
        {
            var results = new List<int>();
 
            void SelfRemoving(PlayerDiedEvent e)
            {
                results.Add(1);
                _bus.Unsubscribe<PlayerDiedEvent>(SelfRemoving);
            }
 
            _bus.Subscribe<PlayerDiedEvent>(SelfRemoving);
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(2));
 
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(new[] { 1, 2 }, results);
        }
 
        [Test]
        public void Publish_HandlerSubscribesNewHandler_NewHandlerDoesNotFireInCurrentCycle()
        {
            var results = new List<int>();
 
            _bus.Subscribe<PlayerDiedEvent>(_ =>
            {
                results.Add(1);
                _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(99));
            });
 
            _bus.Publish(new PlayerDiedEvent());
 
            Assert.AreEqual(new[] { 1 }, results);
        }

        #endregion

        #region Exception Handling Tests

        [Test]
        public void Publish_OneHandlerThrows_RemainingHandlersStillFire()
        {
            var results = new List<int>();
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(1));
            _bus.Subscribe<PlayerDiedEvent>(_ => throw new InvalidOperationException());
            _bus.Subscribe<PlayerDiedEvent>(_ => results.Add(3));
 
            Assert.Throws<AggregateException>(() => _bus.Publish(new PlayerDiedEvent()));
            Assert.AreEqual(new[] { 1, 3 }, results);
        }
 
        [Test]
        public void Publish_HandlerThrows_AggregateExceptionContainsOriginal()
        {
            var original = new InvalidOperationException("original");
            _bus.Subscribe<PlayerDiedEvent>(_ => throw original);
 
            var agg = Assert.Throws<AggregateException>(() => _bus.Publish(new PlayerDiedEvent()));
 
            Assert.AreEqual(1, agg.InnerExceptions.Count);
            Assert.AreSame(original, agg.InnerExceptions[0]);
        }
 
        [Test]
        public void Publish_HandlerThrows_AggregateMessageContainsEventTypeAndCount()
        {
            _bus.Subscribe<PlayerDiedEvent>(_ => throw new Exception());
            _bus.Subscribe<PlayerDiedEvent>(_ => throw new Exception());
            _bus.Subscribe<PlayerDiedEvent>(_ => { });
 
            var agg = Assert.Throws<AggregateException>(() => _bus.Publish(new PlayerDiedEvent()));
 
            StringAssert.Contains("PlayerDiedEvent", agg.Message);
            StringAssert.Contains("2 of 3", agg.Message);
        }

        #endregion
    }
}