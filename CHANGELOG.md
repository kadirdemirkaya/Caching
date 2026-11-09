# Changelog

All notable changes to `Base.Caching` are documented in this file.

## Unreleased

### Changed
- **Breaking:** target frameworks are now net8.0, net9.0 and net10.0; net6.0 and net7.0 are no longer built.
- `Microsoft.Extensions.Caching.StackExchangeRedis` raised to the latest patched release per target (8.0.33, 9.0.22, 10.0.14).
- Hybrid invalidation channel is always subscribed and published as a literal channel name, so an `InstanceName` containing `*` no longer turns the subscription into a pattern.
- Package version is defined once in `Directory.Build.props`.
- **Breaking:** `DistributedCacheManager.ClearAsync` (and `RemoveByPrefixAsync` with an empty prefix) no longer deletes every key of the Redis database when `InstanceName` is empty; it skips Redis and logs a warning. Set `DistributedCache:AllowClearWithoutInstanceName` to `true` to keep the previous behaviour.
- **Breaking:** Redis lock keys of `PerformActionWithLock` / `PerformActionWithLockAsync` are now `{InstanceName}lock:{resource}` (previously the bare `resource`) and hold a unique token instead of the resource name. Applications sharing a Redis database no longer block each other with equal resource names; during a rolling upgrade, old and new instances do not see each other's locks.

### Added
- `BaseDistributedCacheConfiguration.AllowClearWithoutInstanceName` (`DistributedCache:AllowClearWithoutInstanceName`), default `false`.

### Removed
- Unused `Nito.AsyncEx` dependency.

### Fixed
- Prefix/clear invalidation and in-process locks of `MemoryCacheManager` and of the `DistributedCacheManager` in-memory fallback are now bound to their `IMemoryCache` instance instead of being process-wide, so `ClearAsync`, `RemoveByPrefixAsync` or a held lock in one service provider no longer affects another. Managers that share the same `IMemoryCache` (one container, any scope) still share them.
- `ClearAsync` and `RemoveByPrefixAsync` are safe to call concurrently with writes; they no longer throw `ObjectDisposedException` under contention.
- `DistributedCacheManager.ClearAsync` also clears the entries it cached for the current HTTP request, so a read in the same request after a clear no longer returns the cleared value. Other `HttpContext.Items` entries are left untouched.
- `Caching:Cache:DefaultCacheTime` and `ShortTermCacheTime` (or `BaseCacheConfiguration` passed to `AddCaching`) are now applied: values greater than zero become the default and short-term cache times used by `PrepareKeyForDefaultCache`, `PrepareKeyForShortTermCache` and every `CacheKey` created without an explicit expiration (also keys created before `AddCaching`). `0` or missing keeps the built-in 120 and 1 minutes.
- An explicit expiration on a `CacheKey` (`new CacheKey(key, expireMinute, ...)`, `CacheKey.Create(key, expireMinute, ...)` or setting `CacheTime`) is kept by `PrepareKey`; it previously fell back to the default time.
- `HybridCacheManager` subscribes to the invalidation channel once per connection, memory cache and channel instead of once per scope, so handlers no longer accumulate with every request. All scopes of a process share one instance id, so a process ignores its own invalidation messages.
- Redis `ClearAsync` and `RemoveByPrefixAsync` scan only primary servers (replicas are skipped) and delete the matching keys in batches of 1000 while scanning, instead of collecting every key and deleting them in one call.
- Glob characters (`*`, `?`, `[`, `]`, `\`) in `InstanceName` or in a `RemoveByPrefixAsync` prefix are matched literally; they previously widened the deletion pattern to unrelated keys.
- A Redis lock is released only by the caller that acquired it: when an action outlived `expirationTime` and another caller took the lock, the first caller's release no longer deletes the second caller's lock.
- A lock is released on the path it was acquired on: a Redis lock is no longer left in Redis when the connection drops during the action, and an in-memory fallback lock is no longer leaked (while a foreign Redis key was deleted) when Redis comes back during the action.
- `ClearAsync` and `RemoveByPrefixAsync` no longer delete held lock keys.

## 1.1.0 - 2026-05-24

### Added
- `HybridCacheManager`: local memory (L1) in front of Redis (L2), with Pub/Sub invalidation across instances. Enabled with `DistributedCache:HybridCacheEnabled`.
- Automatic in-memory fallback in `DistributedCacheManager` when Redis is unreachable.
- Atomic locks for `ICacheLocker`: Redis `SET NX` in the distributed manager, a concurrent dictionary in the memory manager.
- Multi-targeting: net6.0, net7.0, net8.0 and net9.0.
- NuGet package readme (`src/Base.Caching/docs/README.md`) and package metadata.

### Changed
- `ICacheManager` and `ICacheLocker` moved to the `Interfaces` folder.
- `PerformActionWithLockAsync` takes a `Func<Task>` action and awaits it.
- `AddCaching` selects the memory, distributed or hybrid manager from configuration.
- Prefix invalidation: cancellation tokens for memory, key scan for Redis.

## 1.0.0 - 2025-07-10

### Added
- `ICacheManager` with async and sync get/set, remove, remove by prefix and clear.
- `MemoryCacheManager` and Redis-backed `DistributedCacheManager`.
- `ICacheLocker` distributed locking for critical sections.
- `CacheKey` / `CacheKeyService` key management with default and short-term cache times.
- `PerRequestCache` for HTTP request-scoped caching.
- `AddCaching` registration from `IConfiguration` or a `CacheConfiguration` object.
- Target framework: net8.0.
