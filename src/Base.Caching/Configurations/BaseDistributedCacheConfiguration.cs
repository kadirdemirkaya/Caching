namespace Base.Caching.Configurations
{
    public class BaseDistributedCacheConfiguration
    {
        public bool Enabled { get; set; }
        public string ConnectionString { get; set; }
        public string InstanceName { get; set; }
        public bool HybridCacheEnabled { get; set; }

        /// <summary>
        /// Allows <c>ClearAsync</c> (and <c>RemoveByPrefixAsync</c> with an empty prefix) to delete every key of the
        /// Redis database when <see cref="InstanceName"/> is empty. Default <c>false</c>: without an instance name such a
        /// call deletes nothing in Redis and logs a warning, so keys of other applications sharing the database are kept.
        /// </summary>
        public bool AllowClearWithoutInstanceName { get; set; }
    }
}
