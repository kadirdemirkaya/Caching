namespace Base.Caching.Key
{
    /// <summary>
    /// Cache key: the key name (optionally a format string), its prefixes and its expiration.
    /// </summary>
    public partial class CacheKey
    {
        /// <summary>
        /// Key name; after <c>PrepareKey</c> the formatted name under which the entry is stored.
        /// </summary>
        public string Key { get; protected set; }
        /// <summary>
        /// Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory. Empty prefixes are not added.
        /// </summary>
        public List<string> Prefixes { get; protected set; } = new List<string>();
        private TimeSpan? _cacheDuration;
        private TimeSpan? _slidingExpiration;

        /// <summary>
        /// Cache time in minutes. A key created without an explicit expiration follows
        /// <see cref="CachingDefaults.CacheTime"/> (the configured <c>DefaultCacheTime</c> after <c>AddCaching</c>);
        /// an explicit value always wins and is kept by <see cref="Create(Func{object, object}, object[])"/>.
        /// Setting it sets <see cref="CacheDuration"/>. When the duration is not a whole number of minutes, it is
        /// rounded up (a positive duration below one minute reads as 1), so a positive duration never reads as 0.
        /// </summary>
        public int CacheTime
        {
            get => _cacheDuration is { } duration ? ToWholeMinutes(duration) : CachingDefaults.CacheTime;
            set => _cacheDuration = TimeSpan.FromMinutes(value);
        }

        /// <summary>
        /// Absolute expiration of the entry, relative to the time it is written. A key created without an explicit
        /// expiration follows <see cref="CachingDefaults.CacheTime"/> minutes. A duration of zero or less disables caching
        /// for the key, like a <see cref="CacheTime"/> of zero or less.
        /// </summary>
        public TimeSpan CacheDuration
        {
            get => _cacheDuration ?? TimeSpan.FromMinutes(CachingDefaults.CacheTime);
            set => _cacheDuration = value;
        }

        /// <summary>
        /// Optional sliding expiration: the entry is removed when it has not been read for this long, and at the latest
        /// after <see cref="CacheDuration"/>. <c>null</c> (default) uses the absolute expiration only. Applied by the
        /// in-memory and the distributed store.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
        public TimeSpan? SlidingExpiration
        {
            get => _slidingExpiration;
            set
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The sliding expiration must be positive.");

                _slidingExpiration = value;
            }
        }

        /// <summary>
        /// Creates a key that follows <see cref="CachingDefaults.CacheTime"/>.
        /// </summary>
        /// <param name="key">Key, optionally a format string for the parameters passed to <c>PrepareKey</c>.</param>
        /// <param name="prefixes">Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory.</param>
        public CacheKey(string key, params string[] prefixes)
        {
            Key = key;
            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        /// <summary>
        /// Creates a key whose entry expires <paramref name="expireMinute"/> minutes after it is written.
        /// </summary>
        /// <param name="key">Key, optionally a format string for the parameters passed to <c>PrepareKey</c>.</param>
        /// <param name="expireMinute">Cache time in minutes; zero or less disables caching.</param>
        /// <param name="prefixes">Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory.</param>
        public CacheKey(string key, int expireMinute, params string[] prefixes)
        {
            Key = key;
            CacheTime = expireMinute;
            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        /// <summary>
        /// Creates a key whose entry expires <paramref name="cacheDuration"/> after it is written.
        /// </summary>
        /// <param name="key">Key, optionally a format string for the parameters passed to <c>PrepareKey</c>.</param>
        /// <param name="cacheDuration">Absolute expiration relative to the write; zero or less disables caching.</param>
        /// <param name="prefixes">Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory.</param>
        public CacheKey(string key, TimeSpan cacheDuration, params string[] prefixes)
        {
            Key = key;
            CacheDuration = cacheDuration;
            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        /// <summary>
        /// Creates a key that follows <see cref="CachingDefaults.CacheTime"/>.
        /// </summary>
        /// <param name="key">Key, optionally a format string for the parameters passed to <c>PrepareKey</c>.</param>
        /// <param name="prefixes">Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory.</param>
        /// <returns>The new key.</returns>
        public static CacheKey Create(string key, params string[] prefixes)
            => new CacheKey(key, prefixes);

        /// <summary>
        /// Creates a key whose entry expires <paramref name="expireMinute"/> minutes after it is written.
        /// </summary>
        /// <param name="key">Key, optionally a format string for the parameters passed to <c>PrepareKey</c>.</param>
        /// <param name="expireMinute">Cache time in minutes; zero or less disables caching.</param>
        /// <param name="prefixes">Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory.</param>
        /// <returns>The new key.</returns>
        public static CacheKey Create(string key, int expireMinute, params string[] prefixes)
            => new CacheKey(key, expireMinute, prefixes);

        /// <summary>
        /// Creates a key whose entry expires <paramref name="cacheDuration"/> after it is written.
        /// </summary>
        /// <param name="key">Key, optionally a format string for the parameters passed to <c>PrepareKey</c>.</param>
        /// <param name="cacheDuration">Absolute expiration relative to the write; zero or less disables caching.</param>
        /// <param name="prefixes">Prefixes (tags) used by <c>RemoveByPrefixAsync</c> in memory.</param>
        /// <returns>The new key.</returns>
        public static CacheKey Create(string key, TimeSpan cacheDuration, params string[] prefixes)
            => new CacheKey(key, cacheDuration, prefixes);

        private static int ToWholeMinutes(TimeSpan duration)
        {
            var minutes = Math.Ceiling(duration.TotalMinutes);

            if (minutes >= int.MaxValue)
                return int.MaxValue;

            if (minutes <= int.MinValue)
                return int.MinValue;

            return (int)minutes;
        }

        /// <summary>
        /// Returns a copy of this key with the same expiration whose key name and prefixes are formatted with
        /// <paramref name="keyObjects"/>, each converted by <paramref name="createCacheKeyParameters"/>.
        /// </summary>
        /// <param name="createCacheKeyParameters">Converts each value before it is formatted.</param>
        /// <param name="keyObjects">Values formatted into the key name and the prefixes; when empty, the copy is
        /// returned unformatted.</param>
        /// <returns>The new key.</returns>
        public virtual CacheKey Create(Func<object, object> createCacheKeyParameters, params object[] keyObjects)
        {
            var cacheKey = new CacheKey(Key, Prefixes.ToArray()) { _cacheDuration = _cacheDuration, _slidingExpiration = _slidingExpiration };

            if (!keyObjects.Any())
                return cacheKey;

            cacheKey.Key = string.Format(cacheKey.Key, keyObjects.Select(createCacheKeyParameters).ToArray());

            for (var i = 0; i < cacheKey.Prefixes.Count; i++)
                cacheKey.Prefixes[i] = string.Format(cacheKey.Prefixes[i], keyObjects.Select(createCacheKeyParameters).ToArray());

            return cacheKey;
        }
    }
}
