using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

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

            CachingDefaults.Apply(cacheSection);
            services.AddSingleton(cacheSection);
            services.AddSingleton(distributedCacheSection);
            services.AddMemoryCache();
            services.AddHttpContextAccessor();

            services.AddSingleton<MemoryCacheManager>();
            services.AddScoped<DistributedCacheManager>();

            if (distributedCacheSection.Enabled)
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = distributedCacheSection.ConnectionString;
                    options.InstanceName = distributedCacheSection.InstanceName;
                });

                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var config = ConfigurationOptions.Parse(distributedCacheSection.ConnectionString!);
                    config.AbortOnConnectFail = false;
                    return ConnectionMultiplexer.Connect(config);
                });

                if (distributedCacheSection.HybridCacheEnabled)
                {
                    services.AddScoped<ICacheLocker, HybridCacheManager>();
                    services.AddScoped<ICacheManager, HybridCacheManager>();
                }
                else
                {
                    services.AddScoped<ICacheLocker>(sp => sp.GetRequiredService<DistributedCacheManager>());
                    services.AddScoped<ICacheManager>(sp => sp.GetRequiredService<DistributedCacheManager>());
                }
            }
            else
            {
                services.AddSingleton<ICacheLocker>(sp => sp.GetRequiredService<MemoryCacheManager>());
                services.AddSingleton<ICacheManager>(sp => sp.GetRequiredService<MemoryCacheManager>());
            }
        }

        /// <summary>
        /// Registers the cache managers from <paramref name="cacheConfiguration"/>.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Not read by this overload; may be <c>null</c>.</param>
        /// <param name="cacheConfiguration">Cache and distributed cache settings; both must be set.</param>
        public static void AddCaching(this IServiceCollection services, IConfiguration? configuration, CacheConfiguration cacheConfiguration)
        {
            var cacheSection = cacheConfiguration.baseCacheConfiguration;
            var distributedCacheSection = cacheConfiguration.baseDistributedCacheConfiguration;

            if (cacheSection == null || distributedCacheSection == null)
                throw new Exception("Cache config error!");

            CachingDefaults.Apply(cacheSection);
            services.AddSingleton(cacheSection);
            services.AddSingleton(distributedCacheSection);
            services.AddMemoryCache();
            services.AddHttpContextAccessor();

            services.AddSingleton<MemoryCacheManager>();
            services.AddScoped<DistributedCacheManager>();

            if (distributedCacheSection.Enabled)
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = distributedCacheSection.ConnectionString;
                    options.InstanceName = distributedCacheSection.InstanceName;
                });

                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var config = ConfigurationOptions.Parse(distributedCacheSection.ConnectionString!);
                    config.AbortOnConnectFail = false;
                    return ConnectionMultiplexer.Connect(config);
                });

                if (distributedCacheSection.HybridCacheEnabled)
                {
                    services.AddScoped<ICacheLocker, HybridCacheManager>();
                    services.AddScoped<ICacheManager, HybridCacheManager>();
                }
                else
                {
                    services.AddScoped<ICacheLocker>(sp => sp.GetRequiredService<DistributedCacheManager>());
                    services.AddScoped<ICacheManager>(sp => sp.GetRequiredService<DistributedCacheManager>());
                }
            }
            else
            {
                services.AddSingleton<ICacheLocker>(sp => sp.GetRequiredService<MemoryCacheManager>());
                services.AddSingleton<ICacheManager>(sp => sp.GetRequiredService<MemoryCacheManager>());
            }
        }
    }
}
