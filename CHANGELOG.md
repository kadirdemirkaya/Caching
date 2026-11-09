# Changelog

All notable changes to `Base.Caching` are documented in this file.

## 2.0.0 - 2026-11-09

Upgrading from 1.x: see "Upgrading from 1.x" in the README (https://github.com/kadirdemirkaya/Caching#upgrading-from-1x).

### Changed
- **Breaking:** target frameworks are now net8.0, net9.0 and net10.0; net6.0 and net7.0 are no longer built.
- `Microsoft.Extensions.Caching.StackExchangeRedis` raised to the latest patched release per target (8.0.33, 9.0.22, 10.0.14).
- Hybrid invalidation channel is always subscribed and published as a literal channel name, so an `InstanceName` containing `*` no longer turns the subscription into a pattern.
- Package version is defined once in `Directory.Build.props`.
- `RemoveByPrefixAsync` is documented per store (README and XML docs): in memory it removes entries whose `CacheKey` lists the prefix, in Redis keys whose name starts with `{InstanceName}{prefix}`. Behaviour is unchanged.
- **Breaking:** `DistributedCacheManager.ClearAsync` (and `RemoveByPrefixAsync` with an empty prefix) no longer deletes every key of the Redis database when `InstanceName` is empty; it skips Redis and logs a warning. Set `DistributedCache:AllowClearWithoutInstanceName` to `true` to keep the previous behaviour.
- **Breaking:** Redis lock keys of `PerformActionWithLock` / `PerformActionWithLockAsync` are now `{InstanceName}lock:{resource}` (previously the bare `resource`) and hold a unique token instead of the resource name. Applications sharing a Redis database no longer block each other with equal resource names; during a rolling upgrade, old and new instances do not see each other's locks.
- Nullable annotations on the public API: `ICacheManager.Get<T>(CacheKey)` (and the three managers) returns `T?`; `RouteHelper.GetDataInRoute` returns `string?` / `T?`; the optional constructor parameters of `DistributedCacheManager` and `HybridCacheManager`, `PerRequestCache.Set` data and the unused `IConfiguration` of `AddCaching(IConfiguration, CacheConfiguration)` accept `null`; the protected `PerRequestCache.GetItems` returns `IDictionary<object, object?>?`; `BaseDistributedCacheConfiguration.ConnectionString` / `InstanceName` and the `CacheConfiguration` sections are nullable (`null` when not configured). Runtime behaviour is unchanged; callers with nullable reference types enabled may see new warnings where they dereference these values without a check.
- **Breaking (implementers only):** `ICacheManager` and `ICacheLocker` gained members: an optional `CancellationToken cancellationToken = default` on `GetAsync`, `SetAsync`, `ClearAsync` and `PerformActionWithLockAsync`, and new overloads (see Added). Calling code compiles unchanged; a class that implements one of the interfaces must add the new members, and code compiled against 1.x must be recompiled.
- **Breaking:** `AddCaching` validates its input with specific exceptions instead of `Exception("Cache config error!")`: a missing `Caching:Cache` section, or `DistributedCache:Enabled` without a `ConnectionString`, throws `InvalidOperationException` from `AddCaching(IConfiguration)`; `AddCaching(IConfiguration, CacheConfiguration)` throws `ArgumentNullException` / `ArgumentException`. A missing connection string now fails at registration instead of when the first cache manager is resolved. A literal `services.AddCaching(null)` (which already threw `NullReferenceException`) no longer compiles, because `null` matches both `AddCaching(IConfiguration)` and `AddCaching(Action<CacheOptions>)`.
- `AddCaching(IConfiguration)` no longer requires the `Caching:DistributedCache` section, and `AddCaching(IConfiguration, CacheConfiguration)` no longer requires `baseDistributedCacheConfiguration`: without them the distributed cache is disabled and `MemoryCacheManager` is registered. Both overloads (and the new one) share one registration path.
- A Redis call that throws `OperationCanceledException` for a cancelled token is no longer handled as a connection failure: the exception propagates and no in-memory fallback runs. Other errors still fall back as before.
- `CacheKey.CacheTime` is backed by `CacheKey.CacheDuration`; for a duration that is not a whole number of minutes it reads as the next whole minute. Minute-based keys behave as before.
- The `DistributedCacheManager` constructor gained a trailing optional `BaseCacheConfiguration? cacheConfiguration` parameter. Source calls compile unchanged; binaries that call the constructor directly must be recompiled.

### Added
- `BaseDistributedCacheConfiguration.AllowClearWithoutInstanceName` (`DistributedCache:AllowClearWithoutInstanceName`), default `false`.
- `CancellationToken` support: `ICacheManager.GetAsync(CacheKey, Func<CancellationToken, Task<T>>, CancellationToken)`, `RemoveAsync(CacheKey, CancellationToken, params object[])`, `RemoveByPrefixAsync(string, CancellationToken, params object[])` and `ICacheLocker.PerformActionWithLockAsync(string, TimeSpan, Func<CancellationToken, Task>, CancellationToken)`. The token is passed to the distributed cache and to the acquire function or lock action; Redis `ClearAsync` / `RemoveByPrefixAsync` check it between delete batches. A lock is always released after its action.
- `TimeSpan` expiration: `CacheKey(string, TimeSpan, params string[])`, `CacheKey.Create(string, TimeSpan, params string[])` and the `CacheKey.CacheDuration` property, so entries can live for less than a minute or for any duration.
- Optional sliding expiration: `CacheKey.SlidingExpiration` (`TimeSpan?`, positive), applied by the in-memory and the distributed store and kept by `PrepareKey`.
- Code-first registration: `AddCaching(Action<CacheOptions>)` with `CacheOptions.Cache` and `CacheOptions.DistributedCache`; without settings it registers the in-memory manager with the built-in times.
- Opt-in stampede protection: `BaseCacheConfiguration.PreventConcurrentAcquire` (`Caching:Cache:PreventConcurrentAcquire`, or `options.Cache.PreventConcurrentAcquire`), default `false`. When `true`, concurrent `GetAsync` misses for the same key run the acquire function once per process (memory, distributed and hybrid managers); the other callers wait and read the cached value. An exception from the acquire function is not cached and does not block the waiting callers. New constructor `MemoryCacheManager(IMemoryCache, BaseCacheConfiguration?)` and optional parameter `cacheConfiguration` at the end of the `DistributedCacheManager` constructor carry the setting; existing constructor calls compile unchanged.
- The package now includes XML documentation for the whole public API, shown as IntelliSense in the IDE.

### Removed
- Unused third-party async helper dependency.

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
- `DistributedCacheManager.GetAsync` / `Get` with an acquire function: a key whose `CacheTime` is 0 or less always calls the acquire function; a value cached earlier in the same request under that key is no longer returned. `Get<T>(key)` with such a key returns `default`, like `MemoryCacheManager`.
- `DistributedCacheManager`: a per-request entry stored with another type than the requested one no longer throws `InvalidCastException`; it is treated as a miss and the value is read from the cache or acquired.
- `PerRequestCache.RemoveByPrefix` (also used by `DistributedCacheManager.RemoveByPrefixAsync`) removes the keys that start with the prefix, compared literally and case-sensitively. The prefix was interpreted as a case-insensitive regular expression that matched anywhere in the key, so characters such as `.` matched unrelated keys and `[` or `(` could throw.
- `HybridCacheManager` with a non-nullable value type (`int`, `bool`, …): a miss in local memory returned `default` without reading Redis or calling the acquire function. A missing entry now falls through to Redis and the acquire function, and a cached `default` value (such as `0`) is a hit.

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
