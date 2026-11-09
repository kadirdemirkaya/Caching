using Base.Caching.Lock;
using Microsoft.AspNetCore.Http;

namespace Base.Caching
{
    public class PerRequestCache
    {
        private static readonly object _trackedKeysItemKey = new();

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ReaderWriterLockSlim _lockSlim;

        public PerRequestCache(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;

            _lockSlim = new ReaderWriterLockSlim();
        }

        protected virtual IDictionary<object, object?>? GetItems()
        {
            return _httpContextAccessor.HttpContext?.Items;
        }

        public virtual T Get<T>(string key, Func<T> acquire)
        {
            IDictionary<object, object?>? items;

            using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
            {
                items = GetItems();
                if (items == null)
                    return acquire();

                var existing = items[key];
                if (existing != null)
                    return (T)existing;
            }

            var result = acquire();

            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                items[key] = result;
                TrackKey(items, key);
            }

            return result;
        }

        /// <summary>
        /// Stores <paramref name="data"/> for the current HTTP request under <paramref name="key"/>.
        /// Nothing is stored when <paramref name="data"/> is <c>null</c> or there is no current request.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="data">Value to store; <c>null</c> is ignored.</param>
        public virtual void Set(string key, object? data)
        {
            if (data == null)
                return;

            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                if (items == null)
                    return;

                items[key] = data;
                TrackKey(items, key);
            }
        }

        internal bool TryGet<T>(string key, out T value)
        {
            using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
            {
                var items = GetItems();
                if (items != null && items.TryGetValue(key, out var item) && item is T typed)
                {
                    value = typed;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        internal void Clear()
        {
            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                if (items == null || !items.TryGetValue(_trackedKeysItemKey, out var tracked) || tracked is not HashSet<string> keys)
                    return;

                foreach (var key in keys)
                    items.Remove(key);

                items.Remove(_trackedKeysItemKey);
            }
        }

        private static void TrackKey(IDictionary<object, object?> items, string key)
        {
            if (!items.TryGetValue(_trackedKeysItemKey, out var tracked) || tracked is not HashSet<string> keys)
            {
                keys = new HashSet<string>();
                items[_trackedKeysItemKey] = keys;
            }

            keys.Add(key);
        }

        public virtual bool IsSet(string key)
        {
            using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
            {
                var items = GetItems();
                return items?[key] != null;
            }
        }

        public virtual void Remove(string key)
        {
            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                items?.Remove(key);
            }
        }

        public virtual bool IsRemove(string key)
        {
            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                return items?[key] == null ? true : false;
            }
        }

        public virtual void RemoveByPrefix(string prefix)
        {
            using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.UpgradeableRead))
            {
                var items = GetItems();
                if (items == null)
                    return;

                ArgumentNullException.ThrowIfNull(prefix);

                var matchesKeys = items.Keys.OfType<string>()
                    .Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();

                if (!matchesKeys.Any())
                    return;

                using (new ReaderWriteLockDisposable(_lockSlim))
                    foreach (var key in matchesKeys)
                        items.Remove(key);
            }
        }
    }
}
