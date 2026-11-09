using Base.Caching.Key;
using Microsoft.AspNetCore.Http;

namespace Base.Caching.Helper
{
    /// <summary>
    /// Stores and reads values in <see cref="HttpContext.Items"/> of the current request.
    /// </summary>
    public static class RouteHelper
    {
        /// <summary>
        /// Stores <paramref name="value"/> in <see cref="HttpContext.Items"/> under <paramref name="key"/>, replacing an
        /// existing value.
        /// </summary>
        /// <param name="httpContext">Current HTTP context.</param>
        /// <param name="key">Item key.</param>
        /// <param name="value">Value to store.</param>
        public static void SetDataInRoute(this HttpContext httpContext, string key, string value)
        {
            httpContext.Items[$"{key}"] = value;
        }

        /// <summary>
        /// Stores <paramref name="value"/> in <see cref="HttpContext.Items"/> under <paramref name="key"/>, replacing an
        /// existing value.
        /// </summary>
        /// <typeparam name="T">Type of the value.</typeparam>
        /// <param name="httpContext">Current HTTP context.</param>
        /// <param name="key">Item key.</param>
        /// <param name="value">Value to store.</param>
        public static void SetDataInRoute<T>(this HttpContext httpContext, string key, T value)
        {
            httpContext.Items[$"{key}"] = value;
        }

        /// <summary>
        /// Returns the string stored in <see cref="HttpContext.Items"/> under <paramref name="key"/>.
        /// </summary>
        /// <returns>The value, or <c>null</c> when nothing is stored or the value is not a string.</returns>
        public static string? GetDataInRoute(this HttpContext httpContext, string key)
            => httpContext.Items[$"{key}"] as string;


        /// <summary>
        /// Returns the value of type <typeparamref name="T"/> stored in <see cref="HttpContext.Items"/> under <paramref name="key"/>.
        /// </summary>
        /// <returns>The value, or <c>null</c> when nothing is stored or the value is not a <typeparamref name="T"/>.</returns>
        public static T? GetDataInRoute<T>(this HttpContext httpContext, string key)
            where T : class
            => httpContext.Items[$"{key}"] as T;

    }
}
