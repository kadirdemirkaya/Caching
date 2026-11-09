# Changelog

All notable changes to `Base.Caching` are documented in this file.

## Unreleased

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
