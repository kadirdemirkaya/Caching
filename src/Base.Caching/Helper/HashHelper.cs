using System.Security.Cryptography;

namespace Base.Caching.Helper
{
    /// <summary>
    /// Hash helper used to build cache key parameters.
    /// </summary>
    public partial class HashHelper
    {
        /// <summary>
        /// Computes the hash of <paramref name="data"/> with the named algorithm.
        /// </summary>
        /// <param name="data">Data to hash.</param>
        /// <param name="hashAlgorithm">Algorithm name, for example <c>SHA1</c>.</param>
        /// <param name="trimByteCount">When positive and smaller than the data length, only the first
        /// <paramref name="trimByteCount"/> bytes are hashed.</param>
        /// <returns>The hash as an upper-case hexadecimal string without separators.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="hashAlgorithm"/> is <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentException"><paramref name="hashAlgorithm"/> is not a known algorithm name.</exception>
        public static string CreateHash(byte[] data, string hashAlgorithm, int trimByteCount = 0)
        {
            if (string.IsNullOrEmpty(hashAlgorithm))
                throw new ArgumentNullException(nameof(hashAlgorithm));

            var algorithm = (HashAlgorithm?)CryptoConfig.CreateFromName(hashAlgorithm);
            if (algorithm == null)
                throw new ArgumentException("Unrecognized hash name");

            if (trimByteCount > 0 && data.Length > trimByteCount)
            {
                var newData = new byte[trimByteCount];
                Array.Copy(data, newData, trimByteCount);

                return BitConverter.ToString(algorithm.ComputeHash(newData)).Replace("-", string.Empty);
            }

            return BitConverter.ToString(algorithm.ComputeHash(data)).Replace("-", string.Empty);
        }
    }
}
