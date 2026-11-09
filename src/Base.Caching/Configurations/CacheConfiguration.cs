namespace Base.Caching.Configurations
{
    public class CacheConfiguration
    {
        /// <summary>
        /// Cache times. Required by <c>AddCaching(IConfiguration, CacheConfiguration)</c>; <c>null</c> when not set.
        /// </summary>
        public BaseCacheConfiguration? baseCacheConfiguration;

        /// <summary>
        /// Distributed cache settings. Optional for <c>AddCaching(IConfiguration, CacheConfiguration)</c>: when <c>null</c>,
        /// the distributed cache is disabled and the in-memory manager is registered.
        /// </summary>
        public BaseDistributedCacheConfiguration? baseDistributedCacheConfiguration;
    }
}
