namespace Base.Caching.Configurations
{
    public class BaseCacheConfiguration
    {
        public int DefaultCacheTime { get; set; }
        public int ShortTermCacheTime { get; set; }

        /// <summary>
        /// When <c>true</c>, concurrent <c>GetAsync</c> calls that miss the cache for the same key run the acquire function
        /// once per process: the first call acquires and caches the value, the others wait for it and read it from the
        /// cache. Calls for different keys do not wait for each other. If the acquire function throws, nothing is cached
        /// and the next waiting call runs its own acquire function. Applies to the <c>GetAsync</c> overloads of
        /// <c>MemoryCacheManager</c>, <c>DistributedCacheManager</c> and <c>HybridCacheManager</c>; the synchronous
        /// <c>Get</c> is not affected. Defaults to <c>false</c> (<c>Caching:Cache:PreventConcurrentAcquire</c>).
        /// </summary>
        public bool PreventConcurrentAcquire { get; set; }
    }
}
