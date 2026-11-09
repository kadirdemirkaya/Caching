# Caching Library for .NET

| Package | Downloads | License |
|---------|-----------|---------|
| [![NuGet](https://img.shields.io/nuget/v/Base.Caching)](https://www.nuget.org/packages/Base.Caching) | [![Downloads](https://img.shields.io/nuget/dt/Base.Caching)](https://www.nuget.org/packages/Base.Caching) | [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/kadirdemirkaya/Caching/blob/main/LICENSE.txt) |

A caching library supporting in-memory, distributed (Redis), and hybrid (L1/L2) cache with atomic distributed locking and automatic fallback.

## How It Works

The library provides three cache managers that implement `ICacheManager` and `ICacheLocker`:

- **MemoryCacheManager** — Single-instance in-memory cache with `ConcurrentDictionary`-based atomic locks and cancellation token invalidation. Ideal for single-instance apps that don't need Redis.
- **DistributedCacheManager** — Redis-backed cache with `SET NX` locks and automatic in-memory fallback when Redis is unavailable. Best for multi-instance deployments and write-heavy scenarios (`HybridCacheEnabled=false`).
- **HybridCacheManager** — Combines both: reads from local memory (L1) first, falls back to Redis (L2), and synchronizes across instances via Redis Pub/Sub invalidation. Best for read-heavy scenarios — L1 reduces Redis load.

The active manager is selected automatically based on your configuration.

## Features

- In-memory & Redis distributed cache
- Hybrid L1/L2 cache with Pub/Sub invalidation
- Atomic distributed locks (Redis `SET NX` / `ConcurrentDictionary`)
- Automatic fallback to memory when Redis is down
- Prefix-based cache invalidation (memory: by the key's prefix tags, Redis: by key name — see Prefix Invalidation)
- Per-request cache within HTTP scope
- Expiration in minutes or as a `TimeSpan`, with optional sliding expiration
- Optional `CancellationToken` on every asynchronous method
- Registration from configuration or in code (`AddCaching(options => ...)`)

## Available Services

| Inject | Description |
|--------|-------------|
| `ICacheManager` | Get, set, remove, clear cache entries |
| `ICacheLocker` | Atomic distributed locking (sync/async) |
| `MemoryCacheManager` | Direct in-memory cache access |
| `DistributedCacheManager` | Direct Redis cache access |

## Quick Start

```csharp
// Register (from appsettings.json — see Configuration section below)
builder.Services.AddCaching(configuration);

// Or in code
builder.Services.AddCaching(options =>
{
    options.Cache.DefaultCacheTime = 60;
    options.DistributedCache.Enabled = true;
    options.DistributedCache.ConnectionString = "localhost:6379";
    options.DistributedCache.InstanceName = "MyApp:";
});

// Use
public class MyService
{
    private readonly ICacheManager _cache;

    public MyService(ICacheManager cache) => _cache = cache;

    public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        var key = new CacheKey("user.{0}", "users");
        return await _cache.GetAsync(_cache.PrepareKey(key, id),
            async ct => await FetchFromDb(id, ct), cancellationToken);
    }
}
```

`AddCaching(null, new CacheConfiguration { ... })` keeps working; the `options` overload is the shorter form of the same settings.

## Configuration

```json
{
  "Caching": {
    "Cache": {
      "DefaultCacheTime": 60,
      "ShortTermCacheTime": 3
    },
    "DistributedCache": {
      "Enabled": true,
      "HybridCacheEnabled": false,
      "ConnectionString": "localhost:6379",
      "InstanceName": "MyApp:"
    }
  }
}
```

| Key | Meaning |
|-----|---------|
| `Cache:DefaultCacheTime` | Minutes an entry lives when its `CacheKey` has no explicit expiration, and the time used by `PrepareKeyForDefaultCache`. `0` or missing keeps the built-in 120. |
| `Cache:ShortTermCacheTime` | Minutes used by `PrepareKeyForShortTermCache`. `0` or missing keeps the built-in 1. |
| `DistributedCache:InstanceName` | Prefix of every Redis key written by the package. `ClearAsync` deletes only keys with this prefix (scanned on primary servers, deleted in batches of 1000). Glob characters (`*`, `?`, `[`, `]`, `\`) in the instance name or in a `RemoveByPrefixAsync` prefix are matched literally. |
| `DistributedCache:AllowClearWithoutInstanceName` | `false` (default): with an empty `InstanceName`, `ClearAsync` (and `RemoveByPrefixAsync` with an empty prefix) deletes nothing in Redis and logs a warning, so other applications' keys in the same database are kept. `true`: deletes every key of the database. |

An explicit expiration always wins: `new CacheKey("user.{0}", 5, "users")` (or `CacheKey.Create(key, 5)`) is cached for 5 minutes, also after `PrepareKey`. The configured times are applied process-wide by `AddCaching` (through `CachingDefaults`).

The `DistributedCache` section is optional: without it the distributed cache is disabled and `MemoryCacheManager` is used. A missing `Cache` section throws an `InvalidOperationException` naming the section, and so does `Enabled: true` without a `ConnectionString` (the `CacheConfiguration` and `options` overloads throw an `ArgumentException` for the same mistakes).

With the distributed cache enabled, the Redis connection is opened when the first distributed or hybrid manager is created; that first call waits until the connection succeeds or its connect timeout elapses. If Redis is unreachable, the managers fall back to memory instead of throwing.

## Expiration

```csharp
var minutes = new CacheKey("user.{0}", 5, "users");                        // 5 minutes
var seconds = new CacheKey("quote.{0}", TimeSpan.FromSeconds(30));          // 30 seconds
var sliding = CacheKey.Create("session.{0}", TimeSpan.FromHours(8));
sliding.SlidingExpiration = TimeSpan.FromMinutes(20);                       // removed after 20 idle minutes, at the latest after 8 hours
```

- `CacheDuration` (`TimeSpan`) is the absolute expiration; `CacheTime` is the same value in minutes. A duration that is not a whole number of minutes reads as the next whole minute in `CacheTime` (30 seconds reads as 1). Zero or less disables caching for the key.
- `SlidingExpiration` is optional (`null` by default) and must be positive. It applies to memory and Redis and is kept by `PrepareKey`.

## Cancellation

Every asynchronous method of `ICacheManager` and `ICacheLocker` takes an optional `CancellationToken`:

```csharp
var userKey = new CacheKey("user.{0}", "user.");

await _cache.GetAsync(_cache.PrepareKey(userKey, id), async ct => await FetchFromDb(id, ct), cancellationToken);
await _cache.SetAsync(_cache.PrepareKey(userKey, id), user, cancellationToken);
await _cache.RemoveAsync(userKey, cancellationToken, id);
await _cache.RemoveByPrefixAsync("user.", cancellationToken);
await _cache.ClearAsync(cancellationToken);
await _locker.PerformActionWithLockAsync("order:1", TimeSpan.FromSeconds(30), async ct => await SaveAsync(ct), cancellationToken);
```

- A cancelled token throws `OperationCanceledException`; it is never treated as a Redis failure, so no in-memory fallback runs.
- The token is passed to the distributed cache and to the acquire function or lock action that takes a `CancellationToken`. `ClearAsync` and `RemoveByPrefixAsync` check it between delete batches, so a cancelled call may have removed part of the keys.
- A lock is always released after its action, also when the token is cancelled during the action.

## Prefix Invalidation

`RemoveByPrefixAsync(prefix)` compares the prefix with a different part of the entry depending on the store:

| Store | What is removed |
|-------|-----------------|
| Memory (`MemoryCacheManager`, the in-memory fallback of `DistributedCacheManager`, the local level of `HybridCacheManager`) | Entries whose `CacheKey` lists the prefix among its prefixes (the `params string[] prefixes` of the constructor), compared as an exact, case-sensitive string. The key name is not compared. |
| Redis (`DistributedCacheManager`, the distributed level of `HybridCacheManager`) | Keys whose name starts with `{InstanceName}{prefix}`, compared literally. The prefixes of a `CacheKey` are not stored in Redis. |

`DistributedCacheManager` also removes the values it cached for the current HTTP request whose key starts with the prefix.

The Quick Start key `new CacheKey("user.{0}", "users")` is removed by `RemoveByPrefixAsync("users")` from memory, but **not from Redis**, because the Redis key name `user.1` does not start with `users`. To remove a key from every store with one call, start its name with the prefix and list the same prefix on the key:

```csharp
var key = new CacheKey("user.{0}", "user.");
await _cache.SetAsync(_cache.PrepareKey(key, id), user);

await _cache.RemoveByPrefixAsync("user.");
```

## Distributed Locking

```csharp
public class OrderService
{
    private readonly ICacheLocker _locker;

    public OrderService(ICacheLocker locker) => _locker = locker;

    public async Task ProcessAsync(int orderId)
    {
        var acquired = await _locker.PerformActionWithLockAsync(
            $"order:{orderId}:lock",
            TimeSpan.FromSeconds(30),
            async () => await UpdateInventoryAsync(orderId));

        if (!acquired)
            throw new ConcurrencyException("Already processing.");
    }
}
```

- Both methods return `false` when the lock is held, otherwise run the action and return `true`. The lock is released when the action finishes, also when it throws (the exception is rethrown).
- In Redis the lock is stored under `{InstanceName}lock:{resource}` with a unique token per acquisition, and released only if that token still owns it: when the action outlives `expirationTime` and another caller has taken the lock, the first caller's release leaves it in place. `ClearAsync` and `RemoveByPrefixAsync` do not delete lock keys.
- A lock taken in Redis is released in Redis and a lock taken in memory (Redis unavailable) is released in memory, even if the connection changes while the action runs.

## Installation

```shell
dotnet add package Base.Caching --version 1.1.0
```

## Requirements

- .NET 8.0+ (net8.0, net9.0, net10.0)
- Redis (optional, for distributed/hybrid features)
- **ASP.NET Core** — this library depends on `Microsoft.AspNetCore.App`. For console/worker projects, add the following to your `.csproj`:

  ```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  ```

## Build & Test

```shell
dotnet build
dotnet test          # memory tests work without Redis
dotnet pack -c Release
```
