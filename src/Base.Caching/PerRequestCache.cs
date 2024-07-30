using Base.Caching.Lock;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;

namespace Base.Caching
{
    public class PerRequestCache
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ReaderWriterLockSlim _lockSlim;

        public PerRequestCache(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;

            _lockSlim = new ReaderWriterLockSlim();
        }

        protected virtual IDictionary<object, object> GetItems()
        {
            return _httpContextAccessor.HttpContext?.Items;
        }

        public virtual T Get<T>(string key, Func<T> acquire)
        {
            IDictionary<object, object> items;

            using (new ReaderWriteLockDisposable(_lockSlim, ReaderWriteLockType.Read))
            {
                items = GetItems();
                if (items == null)
                    return acquire();

                if (items[key] != null)
                    return (T)items[key];
            }

            var result = acquire();

            using (new ReaderWriteLockDisposable(_lockSlim))
                items[key] = result;

            return result;
        }

        public virtual void Set(string key, object data)
        {
            if (data == null)
                return;

            using (new ReaderWriteLockDisposable(_lockSlim))
            {
                var items = GetItems();
                if (items == null)
                    return;

                items[key] = data;
            }
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
                var regex = new Regex(prefix,
                    RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var matchesKeys = items.Keys.Select(p => p.ToString())
                    .Where(key => regex.IsMatch(key ?? string.Empty)).ToList();

                if (!matchesKeys.Any())
                    return;

                using (new ReaderWriteLockDisposable(_lockSlim))
                    foreach (var key in matchesKeys)
                        items.Remove(key);
            }
        }
    }
}
