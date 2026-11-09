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
                AbsoluteExpirationRelativeToNow = key.CacheDuration,
                SlidingExpiration = key.SlidingExpiration
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
            if (key is null || key.CacheTime <= 0 || data == null)
                return;

            _memoryCache.Set(key.Key, data, PrepareEntryOptions(key));
        }

        internal bool TryGet<T>(CacheKey key, out T value)
        {
            var found = _memoryCache.TryGetValue(key.Key, out T? cached);
            value = cached!;
            return found;
        }

        public Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            return RemoveAsync(cacheKey, CancellationToken.None, cacheKeyParameters);
        }

        public Task RemoveAsync(CacheKey cacheKey, CancellationToken cancellationToken, params object[] cacheKeyParameters)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            Remove(cacheKey, cacheKeyParameters);

            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire, CancellationToken cancellationToken = default)
        {
            return GetAsync(key, _ => acquire(), cancellationToken);
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> acquire, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key is null || key.CacheTime <= 0)
                return await acquire(cancellationToken);

            if (TryGet(key, out T result))
                return result;

            result = await acquire(cancellationToken);

            if (result != null)
                await SetAsync(key, result, cancellationToken);

            return result;
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key is null || key.CacheTime <= 0)
                return acquire();

            var result = _memoryCache.GetOrCreate(key.Key, entry =>
            {
                entry.SetOptions(PrepareEntryOptions(key));

                return acquire();
            });

            if (result == null)
                await RemoveAsync(key);

            return result!;
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if (key is null || key.CacheTime <= 0)
                return acquire();

            if (TryGet(key, out T result))
                return result;

            result = acquire();

            if (result != null)
                Set(key, result);

            return result;
        }

        public T? Get<T>(CacheKey key)
        {
            if (key is null || key.CacheTime <= 0)
                return default;

            if (TryGet(key, out T result))
                return result;

            return default;
        }

        public Task SetAsync(CacheKey key, object data, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

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
            return RemoveByPrefixAsync(prefix, CancellationToken.None, prefixParameters);
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken, params object[] prefixParameters)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            prefix = PrepareKeyPrefix(prefix, prefixParameters);

            _state.RemoveByPrefix(prefix);

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

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

        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action, CancellationToken cancellationToken = default)
        {
            return PerformActionWithLockAsync(resource, expirationTime, _ => action(), cancellationToken);
        }

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                await action(cancellationToken);
                return true;
            }
            finally
            {
                _inMemoryLocks.TryRemove(KeyValuePair.Create(resource, lockExpiry));
            }
        }
    }
}
