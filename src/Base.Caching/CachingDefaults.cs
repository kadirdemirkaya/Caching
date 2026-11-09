using Base.Caching.Configurations;

namespace Base.Caching
{
    /// <summary>
    /// Process-wide default cache times, in minutes. <c>AddCaching</c> overwrites them with the configured
    /// <see cref="BaseCacheConfiguration.DefaultCacheTime"/> and <see cref="BaseCacheConfiguration.ShortTermCacheTime"/>
    /// when those are greater than zero; otherwise the built-in values (120 and 1) stay in effect.
    /// </summary>
    public static class CachingDefaults
    {
        /// <summary>
        /// Cache time in minutes used by <c>PrepareKeyForShortTermCache</c>. Defaults to 1.
        /// </summary>
        public static int ShortCacheTime { get; set; } = 1;

        /// <summary>
        /// Cache time in minutes used by <c>PrepareKeyForDefaultCache</c> and by every <see cref="Key.CacheKey"/>
        /// created without an explicit expiration. Defaults to 120.
        /// </summary>
        public static int CacheTime { get; set; } = 120;

        internal static void Apply(BaseCacheConfiguration configuration)
        {
            if (configuration.DefaultCacheTime > 0)
                CacheTime = configuration.DefaultCacheTime;

            if (configuration.ShortTermCacheTime > 0)
                ShortCacheTime = configuration.ShortTermCacheTime;
        }
    }
}
