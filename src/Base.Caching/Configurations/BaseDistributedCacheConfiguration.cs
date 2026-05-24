namespace Base.Caching.Configurations
{
    public class BaseDistributedCacheConfiguration
    {
        public bool Enabled { get; set; }
        public string ConnectionString { get; set; }
        public string InstanceName { get; set; }
        public bool HybridCacheEnabled { get; set; }
    }
}
