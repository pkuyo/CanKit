using System;
using System.Collections.Generic;
using System.Linq;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// Default in-process <see cref="IHaweCodecRegistry"/>: a name-keyed dictionary of codec
    /// factories, protected by a single lock. Not a static singleton -- each application (and
    /// each unit test) owns its own instance so that codec registrations never leak between
    /// tests or across independent HAWE modules.
    /// </summary>
    public sealed class HaweCodecRegistry : IHaweCodecRegistry
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Func<IHaweCodec>> _factories = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public void Register(string name, Func<IHaweCodec> factory)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Codec name must be non-empty.", nameof(name));
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            lock (_gate)
            {
                _factories[name] = factory;
            }
        }

        /// <inheritdoc />
        public bool Unregister(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            lock (_gate)
            {
                return _factories.Remove(name);
            }
        }

        /// <inheritdoc />
        public IHaweCodec Create(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Codec name must be non-empty.", nameof(name));

            Func<IHaweCodec>? factory;
            lock (_gate)
            {
                if (!_factories.TryGetValue(name, out factory))
                    throw new KeyNotFoundException($"No HAWE codec registered under name '{name}'.");
            }

            var codec = factory();
            if (codec is null)
                throw new InvalidOperationException($"HAWE codec factory for '{name}' returned null.");

            return codec;
        }

        /// <inheritdoc />
        public bool IsRegistered(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            lock (_gate)
            {
                return _factories.ContainsKey(name);
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> RegisteredNames
        {
            get
            {
                lock (_gate)
                {
                    return _factories.Keys.ToArray();
                }
            }
        }
    }
}
