using Base.Caching.Key;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Nito.AsyncEx;
using StackExchange.Redis;
using System.Text.Json;

namespace Base.Caching.Managers
{
    public class DistributedCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private readonly IDistributedCache _distributedCache;
        private readonly PerRequestCache _perRequestCache;
        private static readonly List<string> _keys;
        private static readonly AsyncLock _locker;

        static DistributedCacheManager()
        {
            _locker = new AsyncLock();
            _keys = new List<string>();
        }

        public DistributedCacheManager(IDistributedCache distributedCache, IHttpContextAccessor httpContextAccessor)
        {
            _distributedCache = distributedCache;
            _perRequestCache = new PerRequestCache(httpContextAccessor);
        }

        private DistributedCacheEntryOptions PrepareEntryOptions(CacheKey key)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(key.CacheTime)
            };

            return options;
        }

        private async Task<(bool isSet, T item)> TryGetItemAsync<T>(CacheKey key)
        {
            var json = await _distributedCache.GetStringAsync(key.Key);

            if (string.IsNullOrEmpty(json))
                return (false, default);

            var item = JsonSerializer.Deserialize<T>(json);
            _perRequestCache.Set(key.Key, item);

            using var _ = await _locker.LockAsync();
            _keys.Add(key.Key);

            return (true, item);
        }

        private (bool isSet, T item) TryGetItem<T>(CacheKey key)
        {
            var json = _distributedCache.GetString(key.Key);

            if (string.IsNullOrEmpty(json))
                return (false, default);

            var item = JsonSerializer.Deserialize<T>(json);
            _perRequestCache.Set(key.Key, item);

            using var _ = _locker.Lock();
            _keys.Add(key.Key);

            return (true, item);
        }

        private void Set(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
                return;

            _distributedCache.SetString(key.Key, JsonSerializer.Serialize(data), PrepareEntryOptions(key));
            _perRequestCache.Set(key.Key, data);

            using var _ = _locker.Lock();
            _keys.Add(key.Key);
        }

        public void Dispose()
        {
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire)
        {
            if (_perRequestCache.IsSet(key.Key))
                return _perRequestCache.Get(key.Key, () => default(T));

            if (key.CacheTime <= 0)
                return await acquire();

            var (isSet, item) = await TryGetItemAsync<T>(key);

            if (isSet)
                return item;

            var result = await acquire();

            if (result != null)
                await SetAsync(key, result);

            return result;
        }

        public T Get<T>(CacheKey key)
        {
            var json = _distributedCache.GetString(key.Key);

            if (string.IsNullOrEmpty(json))
                return default;

            var item = JsonSerializer.Deserialize<T>(json);
            _perRequestCache.Set(key.Key, item);

            using var _ = _locker.Lock();
            _keys.Add(key.Key);

            return item;
        }

        public async Task SetAsync(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
                return;

            await _distributedCache.SetStringAsync(key.Key, JsonSerializer.Serialize(data), PrepareEntryOptions(key));
            _perRequestCache.Set(key.Key, data);

            using var _ = await _locker.LockAsync();
            _keys.Add(key.Key);
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire)
        {
            if (_perRequestCache.IsSet(key.Key))
                return _perRequestCache.Get(key.Key, () => default(T));

            if (key.CacheTime <= 0)
                return acquire();

            var (isSet, item) = await TryGetItemAsync<T>(key);

            if (isSet)
                return item;

            var result = acquire();

            if (result != null)
                await SetAsync(key, result);

            return result;
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if (_perRequestCache.IsSet(key.Key))
                return _perRequestCache.Get(key.Key, () => default(T));

            if (key.CacheTime <= 0)
                return acquire();

            var (isSet, item) = TryGetItem<T>(key);

            if (isSet)
                return item;

            var result = acquire();

            if (result != null)
                Set(key, result);

            return result;
        }

        public async Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            cacheKey = PrepareKey(cacheKey, cacheKeyParameters);

            await _distributedCache.RemoveAsync(cacheKey.Key);
            _perRequestCache.Remove(cacheKey.Key);

            using var _ = await _locker.LockAsync();
            _keys.Remove(cacheKey.Key);
        }

        public async Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            prefix = PrepareKeyPrefix(prefix, prefixParameters);
            _perRequestCache.RemoveByPrefix(prefix);

            using var _ = await _locker.LockAsync();

            foreach (var key in _keys.Where(key => key.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase)).ToList())
            {
                await _distributedCache.RemoveAsync(key);
                _keys.Remove(key);
            }
        }

        public async Task ClearAsync()
        {
            foreach (var redisKey in _keys)
                _perRequestCache.Remove(redisKey);

            using var _ = await _locker.LockAsync();

            foreach (var key in _keys)
                await _distributedCache.RemoveAsync(key);

            _keys.Clear();
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            // Kaynak zaten kilitli
            if (!string.IsNullOrEmpty(_distributedCache.GetString(resource)))
                return false;

            try
            {
                // Kilidi koyma (Cache'e ekleme)
                _distributedCache.SetString(resource, resource, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expirationTime
                });

                // İşlemi gerçekleştirme
                action();

                return true;
            }
            finally
            {
                // Kilidi kaldırma (Cache'ten çıkarma)
                _distributedCache.Remove(resource);
            }
        }

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            // Kilit olup olmadığını kontrol et
            var existingLock = await _distributedCache.GetStringAsync(resource);
            if (!string.IsNullOrEmpty(existingLock))
            {
                return false; // Kaynak zaten kilitli
            }

            // Kilidi koyma (Cache'e ekleme)
            await _distributedCache.SetStringAsync(resource, resource, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expirationTime
            });

            try
            {
                // İşlemi gerçekleştir
                await action();
                return true;
            }
            finally
            {
                // Kilidi kaldır (Cache'ten çıkar)
                await _distributedCache.RemoveAsync(resource);
            }
        }

        //protected class PerRequestCache
        //{
        //    private readonly IHttpContextAccessor _httpContextAccessor;
        //    private readonly ReaderWriterLockSlim _lockSlim;


        //    public PerRequestCache(IHttpContextAccessor httpContextAccessor)
        //    {
        //        _httpContextAccessor = httpContextAccessor;

        //        _lockSlim = new ReaderWriterLockSlim();
        //    }

        //    protected virtual IDictionary<object, object> GetItems()
        //    {
        //        return _httpContextAccessor.HttpContext?.Items;
        //    }

        //    public virtual T Get<T>(string key, Func<T> acquire)
        //    {
        //        IDictionary<object, object> items;

        //        using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
        //        {
        //            items = GetItems();
        //            if (items == null)
        //                return acquire();

        //            if (items[key] != null)
        //                return (T)items[key];
        //        }

        //        var result = acquire();

        //        using (new ReaderWriteLockDisposable(_lockSlim))
        //            items[key] = result;

        //        return result;
        //    }

        //    public virtual void Set(string key, object data)
        //    {
        //        if (data == null)
        //            return;

        //        using (new ReaderWriteLockDisposable(_lockSlim))
        //        {
        //            var items = GetItems();
        //            if (items == null)
        //                return;

        //            items[key] = data;
        //        }
        //    }

        //    public virtual bool IsSet(string key)
        //    {
        //        using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
        //        {
        //            var items = GetItems();
        //            return items?[key] != null;
        //        }
        //    }

        //    public virtual void Remove(string key)
        //    {
        //        using (new ReaderWriteLockDisposable(_lockSlim))
        //        {
        //            var items = GetItems();
        //            items?.Remove(key);
        //        }
        //    }

        //    public virtual bool IsRemove(string key)
        //    {
        //        using (new ReaderWriteLockDisposable(_lockSlim))
        //        {
        //            var items = GetItems();
        //            return items?[key] == null ? true : false;
        //        }
        //    }

        //    public virtual void RemoveByPrefix(string prefix)
        //    {
        //        using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.UpgradeableRead))
        //        {
        //            var items = GetItems();
        //            if (items == null)
        //                return;
        //            var regex = new Regex(prefix,
        //                RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

        //            var matchesKeys = items.Keys.Select(p => p.ToString())
        //                .Where(key => regex.IsMatch(key ?? string.Empty)).ToList();

        //            if (!matchesKeys.Any())
        //                return;

        //            using (new ReaderWriteLockDisposable(_lockSlim))
        //                foreach (var key in matchesKeys)
        //                    items.Remove(key);
        //        }
        //    }
        //}
    }
}
