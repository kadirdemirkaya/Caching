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
- Prefix-based cache invalidation (memory: cancellation token, Redis: SCAN)
- Per-request cache within HTTP scope

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

// Or programmatically with code
builder.Services.AddCaching(null, new CacheConfiguration
{
    baseCacheConfiguration = new BaseCacheConfiguration { DefaultCacheTime = 60 },
    baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
    {
        Enabled = true,
        ConnectionString = "localhost:6379"
    }
});

// Use
public class MyService
{
    private readonly ICacheManager _cache;

    public MyService(ICacheManager cache) => _cache = cache;

    public async Task<User> GetUserAsync(int id)
    {
        var key = new CacheKey("user.{0}", "users");
        return await _cache.GetAsync(_cache.PrepareKey(key, id),
            async () => await FetchFromDb(id));
    }
}
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

An explicit expiration always wins: `new CacheKey("user.{0}", 5, "users")` (or `CacheKey.Create(key, 5)`) is cached for 5 minutes, also after `PrepareKey`. The configured times are applied process-wide by `AddCaching` (through `CachingDefaults`).

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
