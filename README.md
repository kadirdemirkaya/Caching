# Caching Library for .NET

This project is a caching library for .NET applications.
It supports both in-memory and distributed caching (like Redis).
It helps manage cache easily in high-performance scenarios.


## ⚙️ Features

- 🧠 In‑memory cache (memory‑based)
- 🌐 Distributed cache (for example Redis)
- 🔄 Custom cache duration (per key or global)
- 🔁 Automatic cache key handling
- 🔧 Easy setup using services.AddCaching()


## 🚀 Start / Usage Example

```csharp
// In Startup.cs or Program.cs:
services.AddCaching(configuration);

// Usage in a service:
public class MyService
{
    private readonly ICacheManager _cacheManager;

    public MyService(ICacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    public async Task CacheTestExample()
    {
        var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "default");
        var preparedKey = _cacheManager.PrepareKey(cacheKey, "def", "de");

        var cachedUser = await _cacheManager.GetAsync<User>(preparedKey, async () =>
        {
            return new User
            {
                Age = 12,
                Name = "Kadir",
                Role = "Admin"
            };
        });

        Console.WriteLine($"Cached User: {cachedUser.Name}, Role: {cachedUser.Role}");
    }
}

```

## 🧩 Project Structure

- **/Shared**   : Contains shared helper classes and models.

- **/Library**  : Contains core cache services, configuration, and extensions.

- **/Test**     : Includes unit tests written with NUnit.


## 🛠️ Installation and Operation

```
git clone https://github.com/kadirdemirkaya/Caching.git
cd Caching

# Build library
dotnet build

# Pack 
dotnet pack -c Release

# This package will be extracted to the output folder as .nupkg and used as a home library.

```
