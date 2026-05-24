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

## API

| Interface | Methods |
|-----------|---------|
| `ICacheManager` | `Get/GetAsync`, `SetAsync`, `RemoveAsync`, `RemoveByPrefixAsync`, `ClearAsync`, `PrepareKey` |
| `ICacheLocker` | `PerformActionWithLock` / `PerformActionWithLockAsync` |

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

Supports .NET 6.0, 7.0, 8.0, 9.0.

## Requirements

- **ASP.NET Core** — this package depends on `Microsoft.AspNetCore.App`. For console/worker projects, add the following to your `.csproj`:

  ```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  ```
