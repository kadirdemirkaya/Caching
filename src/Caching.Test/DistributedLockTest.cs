using Base.Caching;
using Base.Caching.Helper;
using Base.Caching.Key;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Caching.Test
{
    public class Student
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public string Role { get; set; }
    }
    public class DistributedLockTest
    {
        public static IConfiguration _configuration { get; private set; }
        public ServiceCollection _services;
        private IServiceProvider _serviceProvider;
        private IHttpContextAccessor _httpContextAccessor;
        private DefaultHttpContext _httpContext;
        private ICacheLocker _locker;
        List<Task> tasks;

        [OneTimeSetUp]
        public void GlobalSetup()
        {
            tasks = new List<Task>();

            _services = new ServiceCollection();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();

            _services.AddCaching(_configuration);

            _services.AddHttpContextAccessor();

            _serviceProvider = _services.BuildServiceProvider();

            _httpContextAccessor = _serviceProvider.GetRequiredService<IHttpContextAccessor>();

            _httpContext = new DefaultHttpContext();

            _httpContextAccessor.HttpContext = _httpContext;

            _locker = _serviceProvider.GetRequiredService<ICacheLocker>();

            _serviceProvider = _services.BuildServiceProvider();
        }

        private ICacheManager GetCacheManager() => _serviceProvider.GetRequiredService<ICacheManager>();
        private IHttpContextAccessor GetHttpContext() => _serviceProvider.GetRequiredService<IHttpContextAccessor>();

        [Test]
        public async Task Distributed_Caching_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            tasks.Add(Task.Run(async () =>
            {
                await _staticMemoryCacheManager.SetAsync(CacheKey.Create("lock.key1"), "lock_test_data_1");
            }));
            tasks.Add(Task.Run(async () =>
            {
                await _staticMemoryCacheManager.SetAsync(CacheKey.Create("lock.key2"), "lock_test_data_2");
            }));
            tasks.Add(Task.Run(async () =>
            {
                await _staticMemoryCacheManager.SetAsync(CacheKey.Create("lock.key3"), "lock_test_data_3");
            }));

            tasks.Add(Task.Run(async () =>
            {
                var cacheData = await _staticMemoryCacheManager.GetAsync<string>(CacheKey.Create("lock.key1"), () => default(string));
                await Console.Out.WriteLineAsync(cacheData);
            }));
            tasks.Add(Task.Run(async () =>
            {
                var cacheData = await _staticMemoryCacheManager.GetAsync<string>(CacheKey.Create("lock.key2"), () => default(string));
                await Console.Out.WriteLineAsync(cacheData);
            }));
            tasks.Add(Task.Run(async () =>
            {
                var cacheData = await _staticMemoryCacheManager.GetAsync<string>(CacheKey.Create("lock.key3"), () => default(string));
                await Console.Out.WriteLineAsync(cacheData);
            }));

            await Task.WhenAll(tasks);
        }
        [Test]
        public async Task Distributed_Locking_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            using (var scope = _serviceProvider.CreateScope())
            {
                var _locker = scope.ServiceProvider.GetRequiredService<ICacheLocker>();

                bool response = _locker.PerformActionWithLock("lock.key", TimeSpan.FromSeconds(30), async () =>
                {
                    await ProductUpdateMethod(_staticMemoryCacheManager);
                });

                if (response)
                    await Console.Out.WriteLineAsync("REDIS IS [LOCKED] IN RIGHT NOW");
                else
                    await Console.Out.WriteLineAsync("REDIS IS [NOT LOCKED] IN RIGHT NOW");
            }
            Assert.Pass();
        }
        public async Task ProductUpdateMethod(ICacheManager _staticMemoryCacheManager)
        {
            await _staticMemoryCacheManager.SetAsync(CacheKey.Create("lock.key2"), "lock_string_product_update_method");
            await Task.Delay(2000);
        }

        [Test]
        public async Task Distributed_Concurrency_Locking_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();
            List<Task> tasks = new();

            tasks.Add(Cache_Lock_Method1());
            tasks.Add(Cache_Lock_Method2());

            await Task.WhenAll(tasks);

            Assert.Pass();
        }
        public async Task Cache_Lock_Method1()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            using (var scope = _serviceProvider.CreateScope())
            {
                var _locker = scope.ServiceProvider.GetRequiredService<ICacheLocker>();

                bool response = _locker.PerformActionWithLock("lock.key", TimeSpan.FromSeconds(30), async () =>
                {
                    await _staticMemoryCacheManager.SetAsync(CacheKey.Create("lock.key.original"), "111");
                });

                if (response)
                    await Console.Out.WriteLineAsync("Cache_Lock_Method1 DE KILIT YOKTU");
                else
                    await Console.Out.WriteLineAsync("Cache_Lock_Method1 DE KITLI VARDI");
            }
        }
        public async Task Cache_Lock_Method2()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            using (var scope = _serviceProvider.CreateScope())
            {
                var _locker = scope.ServiceProvider.GetRequiredService<ICacheLocker>();

                bool response = _locker.PerformActionWithLock("lock.key", TimeSpan.FromSeconds(30), async () =>
                {
                    await _staticMemoryCacheManager.SetAsync(CacheKey.Create("lock.key.original"), "222");
                });

                if (response)
                    await Console.Out.WriteLineAsync("Cache_Lock_Method1 DE KILIT YOKTU");
                else
                    await Console.Out.WriteLineAsync("Cache_Lock_Method1 DE KITLI VARDI");
            }
        }


        [Test]
        public async Task Cache_Lock_Concurrency_Test()
        {
            var _staticDistributedCacheManager = GetCacheManager();

            Student student = new()
            {
                Age = 0,
                Name = "Ahmet",
                Role = "User"
            };

            var tasks = new List<Task>();
            tasks.Add(Process1(_staticDistributedCacheManager, student));
            tasks.Add(Process2(_staticDistributedCacheManager, student));

            await Task.WhenAll(tasks);

            Assert.Pass();
        }

        public async Task Process1(ICacheManager _staticDistributedCacheManager, Student student)
        {
            for (int i = 0; i < 10000; i++)
            {
                bool response = await _locker.PerformActionWithLockAsync("lock.key", TimeSpan.FromSeconds(30), async () =>
                {
                    student.Age += 1;
                });

                if (response)
                    await Console.Out.WriteLineAsync("REDIS IS [LOCKED] IN RIGHT NOW");
                else
                    await Console.Out.WriteLineAsync("REDIS IS [NOT LOCKED] IN RIGHT NOW");
            }
        }

        public async Task Process2(ICacheManager _staticDistributedCacheManager, Student student)
        {
            for (int i = 0; i < 10000; i++)
            {
                bool response = await _locker.PerformActionWithLockAsync("lock.key", TimeSpan.FromSeconds(30), async () =>
                {
                    Console.WriteLine("Student Age : " + student.Age);
                });

                if (response)
                    await Console.Out.WriteLineAsync("REDIS IS [LOCKED] IN RIGHT NOW");
                else
                    await Console.Out.WriteLineAsync("REDIS IS [NOT LOCKED] IN RIGHT NOW");
            }
        }
    }
}
