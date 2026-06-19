# Blazor Server State Sharing: Singleton vs Scoped

## Overview

This project is a small Blazor Server app that demonstrates how to share live state across users while keeping individual session state isolated.

It uses two service lifetimes:

- `Singleton` for shared, application-wide data
- `Scoped` for per-user session data

This is a common pattern for business applications such as dashboards, ETL monitors, workflow trackers, and collaborative status pages.

## Business Use Cases

### ETL progress monitor
A central process runs an ETL workflow with multiple stages. Operators and stakeholders should all see the same current step in real time.

### Workflow status dashboard
A team needs a live view of a multi-step workflow. Everyone sees the same status, but each person has their own login and session.

### Shared tracker with private user state
Use shared progress or shared notifications while still keeping authentication, preferences, or private state scoped per user.

## What this app demonstrates

### Shared state
The `ProgressState` service is registered as a singleton.

That means:
- there is one shared `ProgressState` instance for the entire app
- all connected users see the same `CurrentStep`
- updating the step notifies every subscribed component

### Per-user state
The `AuthState` service is registered as scoped.

That means:
- each user circuit gets its own `AuthState`
- login status is isolated per browser session/circuit
- one user's login state does not affect another user's session

## Core files

- `Program.cs` - service registration and Blazor Server setup
- `AuthState.cs` - scoped authentication state
- `ProgressState.cs` - singleton progress state and change notifications
- `Login.razor` - login page with scoped auth
- `Progress.razor` - progress page with shared state subscription

## How the app works

### 1. Program startup

`Program.cs` configures the Blazor Server app and registers services:

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<ProgressState>();
builder.Services.AddScoped<AuthState>();
```

- `AddSingleton<ProgressState>()` makes progress state available globally.
- `AddScoped<AuthState>()` gives each user their own auth state.

### 2. AuthState: scoped user login

`AuthState.cs` is intentionally simple:

```csharp
public class AuthState
{
    public bool IsLoggedIn { get; private set; }

    public void Login() => IsLoggedIn = true;

    public void Logout() => IsLoggedIn = false;
}
```

Because it is scoped, each connected user gets a separate instance.

### 3. ProgressState: shared live state

`ProgressState.cs` stores the shared progress value:

```csharp
public class ProgressState
{
    public event Action? OnChange;

    private int _currentStep = 1;

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (_currentStep == value) return;

            _currentStep = value;
            OnChange?.Invoke();
        }
    }
}
```

The `OnChange` event is the key mechanism for live updates.

### 4. Login page flow

`Login.razor` uses `AuthState` and `NavigationManager`.

When the user enters valid credentials, the component calls `AuthState.Login()` and navigates to `/progress`.

This gives each user their own authenticated session without sharing login state.

### 5. Progress page flow

`Progress.razor` injects both services:

- `ProgressState` to read and update the shared step
- `AuthState` to check whether the user is logged in

If the user is authenticated, the page renders progress controls and step status.

The component also subscribes to live state changes:

```csharp
protected override void OnInitialized()
{
    ProgressState.OnChange += HandleStateChanged;
}

private void HandleStateChanged()
{
    InvokeAsync(StateHasChanged);
}

public void Dispose()
{
    ProgressState.OnChange -= HandleStateChanged;
}
```

This ensures the UI updates whenever the shared step changes.

## Important design lessons

- `Singleton` is for shared application state.
- `Scoped` is for per-user, per-circuit state.
- Use events and `StateHasChanged()` to push UI updates in Blazor.
- Always unsubscribe in `Dispose()` to avoid memory leaks.

## Why this matters

For apps like ETL dashboards, shared workflow monitors, or collaboration tools, this pattern keeps the user experience responsive and consistent while preserving user isolation.

It is a lightweight alternative to storing every state update in a database or external cache for simple real-time state sharing.

## Run the app

Build and run the Blazor Server app from the `BlazorApp1` folder. Then open the login page, authenticate, and navigate to progress to see the shared progress state in action.

After login, `AuthState.IsLoggedIn` becomes `true` for that user's circuit only.

## Why This Works

1. **Blazor Server Model:** Each connected user has a persistent, long-lived "circuit" (WebSocket connection). When a circuit is destroyed (user disconnects), so is its scoped services.

2. **Singletons Persist:** The singleton `ProgressState` lives for the entire app lifetime. All circuits can access and modify it.

3. **Events Are Efficient:** Instead of polling or polling via SignalR, components subscribe to events and only re-render when the state changes.

4. **Thread Safety:** The `lock` ensures concurrent updates don't corrupt the state (e.g., if two users click "Next" simultaneously).

## Potential issues

### Thread Safety
Ensure your singleton is thread-safe. Use `lock`, `Interlocked`, or concurrent collections. In this example, we lock around reads and writes to `_currentStep`.

### Memory Leaks
Always unsubscribe in `Dispose()`. If you forget, old event handlers remain in memory even after the user disconnects.

### Single Server Only
This approach works great for a single server instance. If you scale to **multiple servers** (load-balanced), each server has its own singleton instance. State won't sync across servers. For that, you'd need:
- A distributed cache (Redis)
- A shared database
- SignalR with a backplane (Redis/SQL)

### Authentication Is Not Complete
A scoped `AuthState` boolean is a teaching tool. For production, use ASP.NET Core's built-in **authentication system** with `AuthenticationStateProvider`, claims, and proper session management.

### Event Invocation Must Be Safe
Invoking delegates from within a lock can cause deadlocks. We snapshot the delegate inside the lock and invoke outside—this prevents the event handler from trying to acquire the same lock.

## Best Practices

1. **Use `OnChange` for change notifications, not state transfer.** The event shouldn't pass data; subscribers should query the service for the current state.

2. **Make singletons stateless when possible.** If your singleton is just a hub for events or caches, it's easier to reason about.

3. **Prefer scoped services for per-user data.** They have a clear lifecycle tied to the user's circuit.

4. **Consider transient for stateless utilities.** Helpers, validators, and formatters don't need to persist.

5. **Test thread safety.** If your singleton is accessed by multiple users concurrently, write concurrent unit tests.

## Improvements

### Option 1: Add Async Support
Use `TaskCompletionSource` or `Channels<T>` for async-friendly event patterns.

### Option 2: Distribute State
Swap the in-memory singleton for a Redis-backed service or a shared database with polling/SignalR.

### Option 3: Real Authentication
Replace `AuthState` with `AuthenticationStateProvider` and integrate with ASP.NET Core's identity system.

### Option 4: Logging & Monitoring
Add logging to `OnChange` events to track who triggered updates and when.


