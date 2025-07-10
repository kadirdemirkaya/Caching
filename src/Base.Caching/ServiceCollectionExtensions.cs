using Base.Caching.Configurations;
using Base.Caching.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Base.Caching
{
    public static class ServiceCollectionExtensions
    {
        public static void AddCaching(this IServiceCollection services, IConfiguration configuration)
        {
            var cacheSection = configuration.GetSection("Caching:Cache").Get<BaseCacheConfiguration>();
            var distributedCacheSection = configuration.GetSection("Caching:DistributedCache").Get<BaseDistributedCacheConfiguration>();

            if (cacheSection == null || distributedCacheSection == null)
                throw new Exception("Cache config error!");

            services.AddSingleton(cacheSection);
            services.AddSingleton(distributedCacheSection);

            if (distributedCacheSection.Enabled)
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = distributedCacheSection.ConnectionString;
                    options.InstanceName = distributedCacheSection.InstanceName;
                });

                services.AddScoped<ICacheLocker, DistributedCacheManager>();
                services.AddScoped<ICacheManager, DistributedCacheManager>();
            }
            else
            {
                services.AddMemoryCache();
                services.AddSingleton<ICacheLocker, MemoryCacheManager>();
                services.AddSingleton<ICacheManager, MemoryCacheManager>();
            }
        }

        public static void AddCaching(this IServiceCollection services, IConfiguration configuration, CacheConfiguration cacheConfiguration)
        {
            var cacheSection = cacheConfiguration.baseCacheConfiguration;
            var distributedCacheSection = cacheConfiguration.baseDistributedCacheConfiguration;

            if (cacheSection == null || distributedCacheSection == null)
                throw new Exception("Cache config error!");

            services.AddSingleton(cacheSection);
            services.AddSingleton(distributedCacheSection);

            if (distributedCacheSection.Enabled)
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = distributedCacheSection.ConnectionString;
                    options.InstanceName = distributedCacheSection.InstanceName;
                });

                services.AddScoped<ICacheLocker, DistributedCacheManager>();
                services.AddScoped<ICacheManager, DistributedCacheManager>();
            }
            else
            {
                services.AddMemoryCache();
                services.AddSingleton<ICacheLocker, MemoryCacheManager>();
                services.AddSingleton<ICacheManager, MemoryCacheManager>();
            }
        }
    }
}
