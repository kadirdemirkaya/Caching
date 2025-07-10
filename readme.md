# Base.Caching

Base.Caching is a modular and extensible caching library for .NET 8, providing unified interfaces and implementations for both in-memory and distributed caching scenarios. It supports advanced cache key management, per-request caching, and flexible cache invalidation strategies.

## Features

- **Unified Cache Manager Interface**: Common interface (`ICacheManager`) for all cache operations.
- **Memory and Distributed Cache Support**: Easily switch between in-memory and distributed (e.g., Redis) caching.
- **Cache Key Management**: Strongly-typed `CacheKey` class for consistent and safe cache key generation.
- **Per-Request Cache**: Optional per-request caching for web applications.
- **Asynchronous and Synchronous APIs**: All cache operations are available in both async and sync forms.
- **Cache Invalidation**: Remove by key, by prefix, or clear all.
- **Cache Locking**: Distributed locking support for critical sections.

## Getting Started

### Installation

Add the project to your solution and reference it from your application.

### Usage

#### 1. Register Dependencies

``` bash
services.AddDistributedMemoryCache(); 
services.AddHttpContextAccessor(); 
services.AddScoped<ICacheManager, DistributedCacheManager>();
```

#### 2. Using the Cache Manager

``` bash
public class MyService { private readonly ICacheManager _cacheManager;

    public MyService(ICacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    public async Task<MyData> GetDataAsync(int id)
    {
        var cacheKey = new CacheKey($"mydata-{id}", 30); // 30 minutes
        return await _cacheManager.GetAsync(cacheKey, async () =>
        {
            return await FetchDataFromSourceAsync(id);
        });
    }
}
```


#### 3. Removing or Clearing Cache


``` bash
await _cacheManager.RemoveAsync(cacheKey); await _cacheManager.RemoveByPrefixAsync("mydata-"); await _cacheManager.ClearAsync();
```


## Project Structure

- `ICacheManager`: Main cache manager interface.
- `DistributedCacheManager`: Implementation using `IDistributedCache`.
- `MemoryCacheManager`: Implementation using in-memory cache.
- `CacheKey`: Strongly-typed cache key representation.
- `PerRequestCache`: Per-request cache for web scenarios.
- `CacheKeyService`: Base class for cache key operations.

## Extending

You can implement your own cache manager by inheriting from `ICacheManager` and following the existing patterns.

## License

This project is licensed under the MIT License.

---

**Note:** For more advanced scenarios, such as distributed locking or custom cache key strategies, refer to the source code and XML documentation in the interfaces and classes.



