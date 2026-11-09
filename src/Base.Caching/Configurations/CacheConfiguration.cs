namespace Base.Caching.Configurations
{
    public class CacheConfiguration
    {
        /// <summary>
        /// Cache times. Required by <c>AddCaching(IConfiguration, CacheConfiguration)</c>; <c>null</c> when not set.
        /// </summary>
        public BaseCacheConfiguration? baseCacheConfiguration;

        /// <summary>
        /// Distributed cache settings. Required by <c>AddCaching(IConfiguration, CacheConfiguration)</c>; <c>null</c> when not set.
        /// </summary>
        public BaseDistributedCacheConfiguration? baseDistributedCacheConfiguration;
    }
}
