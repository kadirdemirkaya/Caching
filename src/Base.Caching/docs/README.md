# Base.Caching

In-memory, Redis distributed, and hybrid (L1/L2) caching library for .NET with atomic distributed locking.

## Features

- **In-Memory Cache** — Thread-safe with `ConcurrentDictionary` lock primitives and cancellation token prefix invalidation. Ideal for single-instance apps.
- **Distributed Cache** — Redis via `IDistributedCache` (`StackExchange.Redis`). Best for multi-instance and write-heavy scenarios.
- **Hybrid Cache (L1/L2)** — Memory (L1) + Redis (L2) with Pub/Sub invalidation across instances. Best for read-heavy scenarios.
- **Atomic Distributed Locks** — `PerformActionWithLock` / `PerformActionWithLockAsync` (Redis `SET NX` or `ConcurrentDictionary`)
- **Fallback** — Automatic in-memory fallback when Redis is unreachable
- **Prefix Invalidation** — `RemoveByPrefixAsync` (cancellation tokens for memory, `IServer.Keys()` SCAN for Redis)
- **Per-Request Cache** — Automatic HTTP request-scoped caching

## Installation

```shell
dotnet add package Base.Caching --version 1.1.0
```

## Quick Start

```csharp
// Program.cs
services.AddCaching(configuration);

// Usage
var user = await _cache.GetAsync(
    _cache.PrepareKey(new CacheKey("user.{0}", "users"), id),
    async () => await db.FindAsync(id));
```

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

## API

| Interface | Methods |
|-----------|---------|
| `ICacheManager` | `Get/GetAsync`, `SetAsync`, `RemoveAsync`, `RemoveByPrefixAsync`, `ClearAsync`, `PrepareKey` |
| `ICacheLocker` | `PerformActionWithLock` / `PerformActionWithLockAsync` |

### Locks

- Both methods return `false` when the lock is held, otherwise run the action and return `true`. The lock is released when the action finishes, also when it throws (the exception is rethrown).
- In Redis the lock is stored under `{InstanceName}lock:{resource}` with a unique token per acquisition, and released only if that token still owns it: when the action outlives `expirationTime` and another caller has taken the lock, the first caller's release leaves it in place. `ClearAsync` and `RemoveByPrefixAsync` do not delete lock keys.
- A lock taken in Redis is released in Redis and a lock taken in memory (Redis unavailable) is released in memory, even if the connection changes while the action runs.

## Manager Resolution

| Config | Resolves to |
|--------|-------------|
| `Enabled=false` | `MemoryCacheManager` |
| `Enabled=true` | `DistributedCacheManager` |
| `HybridCacheEnabled=true` | `HybridCacheManager` (L1/L2) |

## Build

```shell
dotnet build
dotnet test
dotnet pack -c Release
```

Supports .NET 8.0, 9.0, 10.0.

## Requirements

- **ASP.NET Core** — this package depends on `Microsoft.AspNetCore.App`. For console/worker projects, add the following to your `.csproj`:

  ```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  ```
