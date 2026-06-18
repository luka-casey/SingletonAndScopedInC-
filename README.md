# Sharing Live State Across Users in Blazor Server: Singletons vs. Scoped Services

## Introduction

Building a real-time, collaborative web application often requires balancing two competing needs:
1. **Global, live state** that all users see instantly (think: shared progress, live notifications, or leaderboards)
2. **Per-user isolation** for authentication, preferences, or private data

In this article, I'll walk you through how to achieve both in a **Blazor Server** application using .NET's dependency injection system. We'll use a real-world example: a progress tracker that updates live for all users, but requires individual login.

## The Challenge

Imagine you're building a wizard or multi-step form that displays progress in real-time. You want:
- All connected users to see progress updates **instantly**
- Each user to have their own **separate login state**
- No database calls or external infrastructure for demo simplicity
- Proper cleanup when components are destroyed

Blazor Server's in-memory DI system makes this surprisingly elegant.

## The Solution: Singleton + Scoped Services

### What Are Services and Lifetimes?

In ASP.NET Core (and Blazor Server), services are registered in the **dependency injection container** with one of three lifetimes:

| Lifetime | Instance Count | Scope | Use Case |
|----------|---|---|---|
| **Singleton** | One per app lifetime | Shared across all requests/circuits | Global state, caches, app-wide settings |
| **Scoped** | One per request/circuit | Isolated per Blazor circuit | Per-user data, auth state |
| **Transient** | One per injection | Always new | Stateless utilities, helpers |

### Our Approach

```csharp
builder.Services.AddSingleton<ProgressState>();   // Shared across all users
builder.Services.AddScoped<AuthState>();           // One per user's circuit
```

- **`ProgressState`** (Singleton): Holds `CurrentStep` and raises an `OnChange` event when the step updates. All connected users subscribe to this event.
- **`AuthState`** (Scoped): Stores a simple `IsLoggedIn` boolean. Each user gets their own instance.

## Implementation Deep Dive

### 1. The Singleton Service: Shared Progress State

```csharp
namespace BlazorApp1;

public class ProgressState
{
    private readonly object _lock = new();

    public event Action? OnChange;

    private int _currentStep = 1;

    public int CurrentStep
    {
        get
        {
            lock (_lock)
            {
                return _currentStep;
            }
        }
        set
        {
            Action? handlers = null;

            lock (_lock)
            {
                if (_currentStep == value) return;

                _currentStep = value;
                handlers = OnChange;
            }

            // Invoke outside the lock
            handlers?.Invoke();
        }
    }
}
```

**Key Points:**
- The `_lock` ensures thread-safe reads and writes when multiple user circuits update simultaneously.
- We capture `OnChange` **inside** the lock, but invoke it **outside** to avoid potential deadlocks.
- The property check (`if (_currentStep == value) return`) avoids redundant events.

### 2. The Scoped Service: Per-User Authentication

```csharp
namespace BlazorApp1;

public class AuthState
{
    public bool IsLoggedIn { get; private set; }

    public void Login() => IsLoggedIn = true;

    public void Logout() => IsLoggedIn = false;
}
```

Simple and isolated. Each user gets their own copy via the scoped lifetime.

### 3. Registering in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<ProgressState>();   // Shared
builder.Services.AddScoped<AuthState>();          // Per-circuit
```

That's it. The DI container handles the rest.

### 4. Injecting and Using in Components

In your Razor component, inject both services:

```razor
@inject ProgressState ProgressState
@inject AuthState AuthState
@inject NavigationManager Navigation

@if(AuthState.IsLoggedIn == true)
{
    <h3>Progress</h3>
    <p>Current Step: @ProgressState.CurrentStep</p>
    
    <button @onclick="NextStep">Next</button>
}
else 
{
    <p>Access denied.</p>
}

@code {
    protected override void OnInitialized()
    {
        // Subscribe to live updates
        ProgressState.OnChange += HandleStateChanged;
    }

    private void HandleStateChanged()
    {
        // Re-render this component
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        // Unsubscribe to prevent memory leaks
        ProgressState.OnChange -= HandleStateChanged;
    }

    private void NextStep()
    {
        if (ProgressState.CurrentStep < 3)
            ProgressState.CurrentStep++;
    }
}
```

**Critical Pattern:**
1. Subscribe in `OnInitialized()` to the singleton's event.
2. Call `InvokeAsync(StateHasChanged)` to safely request a re-render.
3. **Unsubscribe in `Dispose()`** — this is essential to avoid memory leaks and ghost event handlers.

### 5. The Login Flow

In your login page:

```razor
@inject AuthState AuthState
@inject NavigationManager Navigation

<input @bind="Username" placeholder="Username" />
<input @bind="Password" type="password" placeholder="Password" />
<button @onclick="LoginToPage">Login</button>

@code {
    private string Username = "";
    private string Password = "";

    private void LoginToPage()
    {
        if (Username == "admin" && Password == "admin")
        {
            AuthState.Login();
            Navigation.NavigateTo("/progress");
        }
    }
}
```

After login, `AuthState.IsLoggedIn` becomes `true` for that user's circuit only.

## Why This Works

1. **Blazor Server Model:** Each connected user has a persistent, long-lived "circuit" (WebSocket connection). When a circuit is destroyed (user disconnects), so is its scoped services.

2. **Singletons Persist:** The singleton `ProgressState` lives for the entire app lifetime. All circuits can access and modify it.

3. **Events Are Efficient:** Instead of polling or polling via SignalR, components subscribe to events and only re-render when the state changes.

4. **Thread Safety:** The `lock` ensures concurrent updates don't corrupt the state (e.g., if two users click "Next" simultaneously).

## Caveats and Considerations

### ⚠️ Thread Safety
Ensure your singleton is thread-safe. Use `lock`, `Interlocked`, or concurrent collections. In this example, we lock around reads and writes to `_currentStep`.

### ⚠️ Memory Leaks
Always unsubscribe in `Dispose()`. If you forget, old event handlers remain in memory even after the user disconnects.

### ⚠️ Single Server Only
This approach works great for a single server instance. If you scale to **multiple servers** (load-balanced), each server has its own singleton instance. State won't sync across servers. For that, you'd need:
- A distributed cache (Redis)
- A shared database
- SignalR with a backplane (Redis/SQL)

### ⚠️ Authentication Is Not Complete
A scoped `AuthState` boolean is a teaching tool. For production, use ASP.NET Core's built-in **authentication system** with `AuthenticationStateProvider`, claims, and proper session management.

### ⚠️ Event Invocation Must Be Safe
Invoking delegates from within a lock can cause deadlocks. We snapshot the delegate inside the lock and invoke outside—this prevents the event handler from trying to acquire the same lock.

## Best Practices

1. **Use `OnChange` for change notifications, not state transfer.** The event shouldn't pass data; subscribers should query the service for the current state.

2. **Make singletons stateless when possible.** If your singleton is just a hub for events or caches, it's easier to reason about.

3. **Prefer scoped services for per-user data.** They have a clear lifecycle tied to the user's circuit.

4. **Consider transient for stateless utilities.** Helpers, validators, and formatters don't need to persist.

5. **Test thread safety.** If your singleton is accessed by multiple users concurrently, write concurrent unit tests.

## Going Further

### Option 1: Add Async Support
Use `TaskCompletionSource` or `Channels<T>` for async-friendly event patterns.

### Option 2: Distribute State
Swap the in-memory singleton for a Redis-backed service or a shared database with polling/SignalR.

### Option 3: Real Authentication
Replace `AuthState` with `AuthenticationStateProvider` and integrate with ASP.NET Core's identity system.

### Option 4: Logging & Monitoring
Add logging to `OnChange` events to track who triggered updates and when.

## Conclusion

Combining **singleton** and **scoped** services in Blazor Server is a clean, idiomatic way to achieve global real-time state alongside per-user isolation. With proper thread safety, cleanup (unsubscribe in `Dispose()`), and awareness of scaling limits, this pattern is production-ready for small to medium deployments.

Happy coding! 🚀
