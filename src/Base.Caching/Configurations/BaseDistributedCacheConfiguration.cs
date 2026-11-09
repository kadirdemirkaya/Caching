namespace Base.Caching.Configurations
{
    public class BaseDistributedCacheConfiguration
    {
        public bool Enabled { get; set; }
        /// <summary>
        /// Redis connection string. Required when <see cref="Enabled"/> is <c>true</c>; <c>null</c> when not configured.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Prefix of every Redis key written by the cache. <c>null</c> when not configured.
        /// </summary>
        public string? InstanceName { get; set; }
        public bool HybridCacheEnabled { get; set; }

        /// <summary>
        /// Allows <c>ClearAsync</c> (and <c>RemoveByPrefixAsync</c> with an empty prefix) to delete every key of the
        /// Redis database when <see cref="InstanceName"/> is empty. Default <c>false</c>: without an instance name such a
        /// call deletes nothing in Redis and logs a warning, so keys of other applications sharing the database are kept.
        /// </summary>
        public bool AllowClearWithoutInstanceName { get; set; }
    }
}
