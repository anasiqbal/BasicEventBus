namespace BasicEventBus.Tests
{
    internal interface ITestEvent { }
 
    internal struct PlayerDiedEvent : ITestEvent
    {
        public int Score;
    }
 
    internal struct GamePausedEvent : ITestEvent
    {
        public bool IsPaused;
    }
 
    internal struct ChainEvent : ITestEvent
    {
        public string Label;
    }
}