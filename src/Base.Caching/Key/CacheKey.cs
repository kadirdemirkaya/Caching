namespace Base.Caching.Key
{
    public partial class CacheKey
    {
        public string Key { get; protected set; }
        public List<string> Prefixes { get; protected set; } = new List<string>();
        public int CacheTime { get; set; } = CachingDefaults.CacheTime;

        public CacheKey(string key, params string[] prefixes)
        {
            Key = key;
            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        public CacheKey(string key, int expireMinute, params string[] prefixes)
        {
            Key = key;
            CacheTime = expireMinute;
            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        public static CacheKey Create(string key, params string[] prefixes)
            => new CacheKey(key, prefixes);

        public static CacheKey Create(string key, int expireMinute, params string[] prefixes)
            => new CacheKey(key, expireMinute, prefixes);

        public virtual CacheKey Create(Func<object, object> createCacheKeyParameters, params object[] keyObjects)
        {
            var cacheKey = new CacheKey(Key, Prefixes.ToArray());

            if (!keyObjects.Any())
                return cacheKey;

            cacheKey.Key = string.Format(cacheKey.Key, keyObjects.Select(createCacheKeyParameters).ToArray());

            for (var i = 0; i < cacheKey.Prefixes.Count; i++)
                cacheKey.Prefixes[i] = string.Format(cacheKey.Prefixes[i], keyObjects.Select(createCacheKeyParameters).ToArray());

            return cacheKey;
        }
    }
}
