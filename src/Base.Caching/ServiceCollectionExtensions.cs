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
        private const string CacheSectionName = "Caching:Cache";
        private const string DistributedCacheSectionName = "Caching:DistributedCache";

        /// <summary>
        /// Registers the cache managers from the <c>Caching:Cache</c> and <c>Caching:DistributedCache</c> sections of
        /// <paramref name="configuration"/>. The <c>Caching:DistributedCache</c> section is optional; without it the
        /// distributed cache is disabled and the in-memory manager is registered.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Application configuration.</param>
        /// <remarks>When the distributed cache is enabled, the Redis connection is opened when
        /// <c>IConnectionMultiplexer</c> is first resolved (with the first distributed or hybrid cache manager); that call
        /// blocks until the connection succeeds or its connect timeout elapses. A failed connection does not throw: the
        /// managers fall back to the in-memory cache until Redis is available.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is
        /// <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The <c>Caching:Cache</c> section is missing, or the distributed cache
        /// is enabled without a <c>ConnectionString</c>.</exception>
        public static void AddCaching(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            var cacheSection = configuration.GetSection(CacheSectionName).Get<BaseCacheConfiguration>()
                ?? throw new InvalidOperationException(
                    $"The '{CacheSectionName}' configuration section is missing. Add it (for example with DefaultCacheTime and ShortTermCacheTime) or register the cache with AddCaching(Action<CacheOptions>).");
            var distributedCacheSection = configuration.GetSection(DistributedCacheSectionName).Get<BaseDistributedCacheConfiguration>()
                ?? new BaseDistributedCacheConfiguration();

            if (IsConnectionStringMissing(distributedCacheSection))
                throw new InvalidOperationException(
                    $"'{DistributedCacheSectionName}:ConnectionString' is required when '{DistributedCacheSectionName}:Enabled' is true.");

            AddCachingCore(services, cacheSection, distributedCacheSection);
        }

        /// <summary>
        /// Registers the cache managers from <paramref name="cacheConfiguration"/>.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Not read by this overload; may be <c>null</c>.</param>
        /// <param name="cacheConfiguration">Cache and distributed cache settings. <c>baseCacheConfiguration</c> is required;
        /// without <c>baseDistributedCacheConfiguration</c> the distributed cache is disabled.</param>
        /// <remarks>The Redis connection is opened as described for
        /// <see cref="AddCaching(IServiceCollection, IConfiguration)"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="cacheConfiguration"/> is
        /// <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><c>baseCacheConfiguration</c> is <c>null</c>, or the distributed cache is
        /// enabled without a <c>ConnectionString</c>.</exception>
        public static void AddCaching(this IServiceCollection services, IConfiguration? configuration, CacheConfiguration cacheConfiguration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(cacheConfiguration);

            var cacheSection = cacheConfiguration.baseCacheConfiguration
                ?? throw new ArgumentException(
                    $"{nameof(CacheConfiguration)}.{nameof(CacheConfiguration.baseCacheConfiguration)} must be set.", nameof(cacheConfiguration));
            var distributedCacheSection = cacheConfiguration.baseDistributedCacheConfiguration ?? new BaseDistributedCacheConfiguration();

            if (IsConnectionStringMissing(distributedCacheSection))
                throw new ArgumentException(
                    $"{nameof(BaseDistributedCacheConfiguration)}.{nameof(BaseDistributedCacheConfiguration.ConnectionString)} is required when {nameof(BaseDistributedCacheConfiguration.Enabled)} is true.",
                    nameof(cacheConfiguration));

            AddCachingCore(services, cacheSection, distributedCacheSection);
        }

        /// <summary>
        /// Registers the cache managers from settings made in code. Without any setting the in-memory manager is registered
        /// with the built-in cache times (120 and 1 minutes).
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configure">Sets the <see cref="CacheOptions"/>.</param>
        /// <remarks>The Redis connection is opened as described for
        /// <see cref="AddCaching(IServiceCollection, IConfiguration)"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is
        /// <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><see cref="CacheOptions.Cache"/> or <see cref="CacheOptions.DistributedCache"/>
        /// was set to <c>null</c>, or the distributed cache is enabled without a <c>ConnectionString</c>.</exception>
        public static void AddCaching(this IServiceCollection services, Action<CacheOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new CacheOptions();
            configure(options);

            var cacheSection = options.Cache
                ?? throw new ArgumentException($"{nameof(CacheOptions)}.{nameof(CacheOptions.Cache)} must not be null.", nameof(configure));
            var distributedCacheSection = options.DistributedCache
                ?? throw new ArgumentException($"{nameof(CacheOptions)}.{nameof(CacheOptions.DistributedCache)} must not be null.", nameof(configure));

            if (IsConnectionStringMissing(distributedCacheSection))
                throw new ArgumentException(
                    $"{nameof(CacheOptions)}.{nameof(CacheOptions.DistributedCache)}.{nameof(BaseDistributedCacheConfiguration.ConnectionString)} is required when {nameof(BaseDistributedCacheConfiguration.Enabled)} is true.",
                    nameof(configure));

            AddCachingCore(services, cacheSection, distributedCacheSection);
        }

        private static bool IsConnectionStringMissing(BaseDistributedCacheConfiguration distributedCacheSection)
            => distributedCacheSection.Enabled && string.IsNullOrWhiteSpace(distributedCacheSection.ConnectionString);

        private static void AddCachingCore(IServiceCollection services, BaseCacheConfiguration cacheSection, BaseDistributedCacheConfiguration distributedCacheSection)
        {
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
