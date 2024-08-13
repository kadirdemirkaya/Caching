using Base.Caching.Key;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace Base.Caching.Managers
{
    public partial class MemoryCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private bool _disposed;
        private readonly IMemoryCache _memoryCache;
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _prefixes = new();
        private static CancellationTokenSource _clearToken = new();

        public MemoryCacheManager(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        private MemoryCacheEntryOptions PrepareEntryOptions(CacheKey key)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(key.CacheTime)
            };

            options.AddExpirationToken(new CancellationChangeToken(_clearToken.Token));
            foreach (var keyPrefix in key.Prefixes.ToList())
            {
                var tokenSource = _prefixes.GetOrAdd(keyPrefix, new CancellationTokenSource());
                options.AddExpirationToken(new CancellationChangeToken(tokenSource.Token));
            }

            return options;
        }

        private void Remove(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            cacheKey = PrepareKey(cacheKey, cacheKeyParameters);
            _memoryCache.Remove(cacheKey.Key);
        }

        private void Set(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
                return;

            _memoryCache.Set(key.Key, data, PrepareEntryOptions(key));
        }

        public Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            Remove(cacheKey, cacheKeyParameters);

            return Task.CompletedTask;
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire)
        {
            if ((key?.CacheTime ?? 0) <= 0)
                return await acquire();

            if (_memoryCache.TryGetValue(key.Key, out T result))
                return result;

            result = await acquire();

            if (result != null)
                await SetAsync(key, result);

            return result;
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire)
        {
            if ((key?.CacheTime ?? 0) <= 0)
                return acquire();

            var result = _memoryCache.GetOrCreate(key.Key, entry =>
            {
                entry.SetOptions(PrepareEntryOptions(key));

                return acquire();
            });

            if (result == null)
                await RemoveAsync(key);

            return result;
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if ((key?.CacheTime ?? 0) <= 0)
                return acquire();

            if (_memoryCache.TryGetValue(key.Key, out T result))
                return result;

            result = acquire();

            if (result != null)
                Set(key, result);

            return result;
        }

        public T Get<T>(CacheKey key)
        {
            if ((key?.CacheTime ?? 0) <= 0)
                return default;

            if (_memoryCache.TryGetValue(key.Key, out T result))
                return result;

            return default;
        }

        public Task SetAsync(CacheKey key, object data)
        {
            Set(key, data);

            return Task.CompletedTask;
        }

        public bool PerformActionWithLock(string key, TimeSpan expirationTime, Action action)
        {
            if (_memoryCache.TryGetValue(key, out _))
                return false;

            try
            {
                _memoryCache.Set(key, key, expirationTime);

                action();

                return true;
            }
            finally
            {
                _memoryCache.Remove(key);
            }
        }

        public Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            prefix = PrepareKeyPrefix(prefix, prefixParameters);

            _prefixes.TryRemove(prefix, out var tokenSource);
            tokenSource?.Cancel();
            tokenSource?.Dispose();

            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _clearToken.Cancel();
            _clearToken.Dispose();

            _clearToken = new CancellationTokenSource();

            foreach (var prefix in _prefixes.Keys.ToList())
            {
                _prefixes.TryRemove(prefix, out var tokenSource);
                tokenSource?.Dispose();
            }

            return Task.CompletedTask;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                _memoryCache.Dispose();

            _disposed = true;
        }

        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Action action)
        {
            throw new NotImplementedException();
        }

        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            throw new NotImplementedException();
        }
    }
}
