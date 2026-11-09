namespace Base.Caching.Configurations
{
    /// <summary>
    /// Code-first settings for <c>AddCaching(Action&lt;CacheOptions&gt;)</c>. Same settings as the <c>Caching:Cache</c> and
    /// <c>Caching:DistributedCache</c> configuration sections; every value has a default, so an empty configuration
    /// registers the in-memory cache with the built-in cache times.
    /// </summary>
    public class CacheOptions
    {
        /// <summary>
        /// Cache times (<c>Caching:Cache</c>). Zero keeps the built-in times (120 and 1 minutes).
        /// </summary>
        public BaseCacheConfiguration Cache { get; set; } = new BaseCacheConfiguration();

        /// <summary>
        /// Distributed cache settings (<c>Caching:DistributedCache</c>). Disabled by default.
        /// </summary>
        public BaseDistributedCacheConfiguration DistributedCache { get; set; } = new BaseDistributedCacheConfiguration();
    }
}
