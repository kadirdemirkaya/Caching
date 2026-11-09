using Base.Caching.Lock;
using Microsoft.AspNetCore.Http;

namespace Base.Caching
{
    /// <summary>
    /// Caches values for the duration of the current HTTP request in <c>HttpContext.Items</c>.
    /// </summary>
    public class PerRequestCache
    {
        private static readonly object _trackedKeysItemKey = new();

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ReaderWriterLockSlim _lockSlim;

        /// <summary>
        /// Creates the per-request cache.
        /// </summary>
        /// <param name="httpContextAccessor">Gives access to the current HTTP request.</param>
        public PerRequestCache(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;

            _lockSlim = new ReaderWriterLockSlim();
        }

        /// <summary>
        /// Returns the item collection of the current HTTP request.
        /// </summary>
        /// <returns>The items, or <c>null</c> when there is no current request.</returns>
        protected virtual IDictionary<object, object?>? GetItems()
        {
            return _httpContextAccessor.HttpContext?.Items;
        }

        /// <summary>
        /// Returns the value stored for the current request under <paramref name="key"/>, or loads it with
        /// <paramref name="acquire"/> and stores it. Without a current request <paramref name="acquire"/> is called and
        /// nothing is stored.
        /// </summary>
        /// <typeparam name="T">Type of the value.</typeparam>
        /// <param name="key">Cache key.</param>
        /// <param name="acquire">Loads the value when nothing is stored.</param>
        /// <returns>The stored or loaded value.</returns>
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

        /// <summary>
        /// Checks whether a value is stored for the current request under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <returns><c>true</c> when a non-null value is stored; <c>false</c> otherwise or without a current request.</returns>
        public virtual bool IsSet(string key)
        {
            using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
            {
                var items = GetItems();
                return items?[key] != null;
            }
        }

        /// <summary>
        /// Removes the value stored for the current request under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Cache key.</param>
        public virtual void Remove(string key)
        {
            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                items?.Remove(key);
            }
        }

        /// <summary>
        /// Checks whether no value is stored for the current request under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <returns><c>true</c> when no non-null value is stored or there is no current request; otherwise <c>false</c>.</returns>
        public virtual bool IsRemove(string key)
        {
            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                return items?[key] == null ? true : false;
            }
        }

        /// <summary>
        /// Removes the values stored for the current request whose string key starts with <paramref name="prefix"/>
        /// (ordinal comparison).
        /// </summary>
        /// <param name="prefix">Key prefix.</param>
        /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <c>null</c> and there is a current
        /// request.</exception>
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
