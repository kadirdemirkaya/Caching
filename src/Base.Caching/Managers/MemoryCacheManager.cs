using Base.Caching.Interfaces;
using Base.Caching.Key;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Base.Caching.Managers
{
    public partial class MemoryCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private bool _disposed;
        private readonly IMemoryCache _memoryCache;
        private readonly CacheInvalidationState _state;
        private readonly ConcurrentDictionary<string, DateTime> _inMemoryLocks;

        public MemoryCacheManager(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
            _state = CacheInvalidationState.ForMemoryManager(memoryCache);
            _inMemoryLocks = _state.Locks;
        }

        private MemoryCacheEntryOptions PrepareEntryOptions(CacheKey key)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(key.CacheTime)
            };

            return _state.AddExpirationTokens(options, key.Prefixes.ToList());
        }

        private void Remove(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            cacheKey = PrepareKey(cacheKey, cacheKeyParameters);
            _memoryCache.Remove(cacheKey.Key);
        }

        internal void RemovePreparedKey(string key)
        {
            _memoryCache.Remove(key);
        }

        internal void RemovePreparedPrefix(string prefix)
        {
            _state.RemoveByPrefix(prefix);
        }

        internal void ClearAll()
        {
            _state.Clear();
        }

        internal void Set(CacheKey key, object data)
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
            var now = DateTime.UtcNow;
            var lockExpiry = now.Add(expirationTime);

            while (true)
            {
                if (_inMemoryLocks.TryAdd(key, lockExpiry))
                {
                    break;
                }

                if (_inMemoryLocks.TryGetValue(key, out var existingExpiry))
                {
                    if (existingExpiry < now)
                    {
                        _inMemoryLocks.TryRemove(KeyValuePair.Create(key, existingExpiry));
                        continue;
                    }
                }

                return false;
            }

            try
            {
                action();
                return true;
            }
            finally
            {
                _inMemoryLocks.TryRemove(KeyValuePair.Create(key, lockExpiry));
            }
        }

        public Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            prefix = PrepareKeyPrefix(prefix, prefixParameters);

            _state.RemoveByPrefix(prefix);

            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _state.Clear();

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

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            var now = DateTime.UtcNow;
            var lockExpiry = now.Add(expirationTime);

            while (true)
            {
                if (_inMemoryLocks.TryAdd(resource, lockExpiry))
                {
                    break;
                }

                if (_inMemoryLocks.TryGetValue(resource, out var existingExpiry))
                {
                    if (existingExpiry < now)
                    {
                        _inMemoryLocks.TryRemove(KeyValuePair.Create(resource, existingExpiry));
                        continue;
                    }
                }

                return false;
            }

            try
            {
                await action();
                return true;
            }
            finally
            {
                _inMemoryLocks.TryRemove(KeyValuePair.Create(resource, lockExpiry));
            }
        }
    }
}
