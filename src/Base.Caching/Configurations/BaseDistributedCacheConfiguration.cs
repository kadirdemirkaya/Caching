namespace Base.Caching.Configurations
{
    /// <summary>
    /// Distributed cache settings bound from the <c>Caching:DistributedCache</c> configuration section.
    /// </summary>
    public class BaseDistributedCacheConfiguration
    {
        /// <summary>
        /// Enables the distributed cache (Redis). Default <c>false</c>: the in-memory manager is registered.
        /// </summary>
        public bool Enabled { get; set; }
        /// <summary>
        /// Redis connection string. Required when <see cref="Enabled"/> is <c>true</c>; <c>null</c> when not configured.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Prefix of every Redis key written by the cache. <c>null</c> when not configured.
        /// </summary>
        public string? InstanceName { get; set; }
        /// <summary>
        /// When <see cref="Enabled"/> is also <c>true</c>, registers the hybrid manager (local memory level plus Redis
        /// level, with invalidation messages between instances) instead of the distributed manager. Default <c>false</c>.
        /// </summary>
        public bool HybridCacheEnabled { get; set; }

        /// <summary>
        /// Allows <c>ClearAsync</c> (and <c>RemoveByPrefixAsync</c> with an empty prefix) to delete every key of the
        /// Redis database when <see cref="InstanceName"/> is empty. Default <c>false</c>: without an instance name such a
        /// call deletes nothing in Redis and logs a warning, so keys of other applications sharing the database are kept.
        /// </summary>
        public bool AllowClearWithoutInstanceName { get; set; }
    }
}
