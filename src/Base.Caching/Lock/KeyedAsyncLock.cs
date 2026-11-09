namespace Base.Caching.Lock
{
    internal sealed class KeyedAsyncLock
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        internal int Count
        {
            get
            {
                lock (_entries)
                    return _entries.Count;
            }
        }

        internal async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
        {
            Entry entry;

            lock (_entries)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new Entry();
                    _entries.Add(key, entry);
                }

                entry.References++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Release(key, entry, held: false);
                throw;
            }

            return new Releaser(this, key, entry);
        }

        private void Release(string key, Entry entry, bool held)
        {
            lock (_entries)
            {
                if (held)
                    entry.Semaphore.Release();

                if (--entry.References > 0)
                    return;

                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }

        private sealed class Entry
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);

            public int References { get; set; }
        }

        private sealed class Releaser : IDisposable
        {
            private KeyedAsyncLock? _owner;
            private readonly string _key;
            private readonly Entry _entry;

            public Releaser(KeyedAsyncLock owner, string key, Entry entry)
            {
                _owner = owner;
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Release(_key, _entry, held: true);
            }
        }
    }
}
