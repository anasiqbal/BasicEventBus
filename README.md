# EventBus

A strongly-typed, dependency-free event bus in pure C#. No framework, no Unity, no string keys.

---

## Installation

### Unity (UPM)

Install through the Package Manager using the Git URL:

1. Open **Window → Package Manager**.
2. Click **+ → Add package from git URL...**
3. Paste:
   ```
   https://github.com/anasiqbal/BasicEventBus.git
   ```

Or add it to `Packages/manifest.json` directly:

```json
"dependencies": {
  "com.anasiqbal.basic-event-bus": "https://github.com/anasiqbal/BasicEventBus.git"
}
```

Pin to a specific version or commit by appending a ref, e.g.
`...BasicEventBus.git#v1.0.0`.

### .NET

It's not published to NuGet, so pull the source in directly — the runtime is a single
dependency-free file:

- **Copy** `Runtime/EventBus.cs` into your project, or
- **Add it as a git submodule** and include the runtime in your `.csproj`:
  ```xml
  <ItemGroup>
    <Compile Include="path/to/BasicEventBus/Runtime/**/*.cs" />
  </ItemGroup>
  ```

---

## Usage

```csharp
// Define a base event interface, all events must implement it
public interface IGameEvent { }

var bus = new EventBus<IGameEvent>();

// Define events, structs recommended to avoid allocation
public struct PlayerDiedEvent : IGameEvent
{
    public int FinalScore;
}

// Subscribe
bus.Subscribe<PlayerDiedEvent>(e => Debug.Log($"Score: {e.FinalScore}"));

// Publish
bus.Publish(new PlayerDiedEvent { FinalScore = 42 });

// Unsubscribe
void OnPlayerDied(PlayerDiedEvent e) { ... }
bus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
bus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);

// One-shot subscriptions, auto-unsubscribes before the handler fires
bus.Once<PlayerDiedEvent>(e => Debug.Log("Fires once, then never again"));

// Scoped subscriptions, unsubscribes automatically when the token is disposed
IDisposable token = bus.Scoped<PlayerDiedEvent>(OnPlayerDied);
token.Dispose(); // unsubscribed

// Consume an event, stops remaining handlers in the current dispatch cycle
bus.Subscribe<PlayerDiedEvent>(_ => bus.ConsumeCurrent());

// Priority, higher runs first. Default is 0, so anything above runs early
bus.Subscribe<PlayerDiedEvent>(_ => SaveCheckpoint(), priority: 100); // runs first
bus.Subscribe<PlayerDiedEvent>(_ => UpdateUI());                      // runs after (priority 0)
```
---

## API

```csharp
public interface IEventBus<TBaseEvent>
{
    void Subscribe<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : TBaseEvent;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : TBaseEvent;
    void Once<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : TBaseEvent;
    IDisposable Scoped<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : TBaseEvent;
    void Publish<TEvent>(TEvent message) where TEvent : TBaseEvent;
    void ConsumeCurrent();
}
```
---

## Behaviour Guarantees

#### Subscribers fire in priority order, highest first.

Pass a `priority` to control firing order. Subscribers with higher priority runs first, default is `0`. Subscriber with same priority fire in subscription order.

#### All subscribers run on Publish, even if one throws.
If any subscribers throw an exception, the event queue is not blocked/cancelled, instead, an `AggregateException` is raised after the full dispatch cycle completes. Inner exceptions are the original exceptions with their stack traces intact. The message includes the event type name and failure count:
```
EventBus.Publish<PlayerDiedEvent>: 1 of 3 handler(s) threw. See InnerExceptions for details.
```

#### Subscribe/Unsubscribe during Publish is safe.

The dispatch loop works from a snapshot of the subscriber list taken at the start of each dispatch cycle. A subscriber that unsubscribes itself or subscribes a new one mid-dispatch won't affect the current cycle.

#### `Once` unsubscribes before invocation.

If the subscriber throws an exception, it still won't fire again. Manually unsubscribing a once-subscriber before it fires also works correctly.

#### Events published during dispatch are queued, not dispatched immediately.

A subscriber that calls `Publish` during an active dispatch will have its event queued and processed after the current cycle completes (FIFO).

#### `ConsumeCurrent` stops remaining subscribers, not already-collected exceptions.

Any exceptions thrown before `ConsumeCurrent` is called are still surfaced via `AggregateException`.

#### `Unsubscribe` is safe if the subscriber isn't registered.

---

## Running Tests

### In Unity

1. Open **Window → General → Test Runner**.
2. Switch to the **EditMode** tab.
3. Click **Run All** on bottom left.

### Without Unity

The runtime and tests are plain C# (the tests only need NUnit), so you can run them straight from
your own .NET solution once the package is imported.

1. Add the NUnit packages to the test project that will host the tests (skip any you already have):
   ```bash
   dotnet add package NUnit
   dotnet add package NUnit3TestAdapter
   dotnet add package Microsoft.NET.Test.Sdk
   ```
2. Make sure the test project compiles the package's `Tests` folder and references the runtime.
   If the imported package isn't already part of the build, add it to the test `.csproj` (adjust
   the path to where you imported it):
   ```xml
   <ItemGroup>
     <Compile Include="path/to/BasicEventBus/Runtime/**/*.cs" />
     <Compile Include="path/to/BasicEventBus/Tests/**/*.cs" />
   </ItemGroup>
   ```
3. Run them:
   ```bash
   dotnet test
   ```
