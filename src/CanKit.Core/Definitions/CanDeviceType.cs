using System;
using System.Collections.Generic;

namespace CanKit.Core.Definitions
{
    /// <summary>
    /// Device type registry with unique string Id (轻量设备类型注册，仅保留唯一 Id)
    /// </summary>
    public abstract record DeviceType
    {
        private static readonly Dictionary<string, DeviceType> _byId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<DeviceType> _all = new();
        private static readonly object _gate = new();

        /// <summary>
        /// Placeholder for unknown device (未知设备占位类型)
        /// </summary>
        public static readonly DeviceType Unknown = new Generic("unknown");

        /// <summary>
        /// Construct and register device type (构造并完成注册)
        /// </summary>
        /// <param name="id">Unique identifier, case-insensitive (唯一标识)</param>
        protected DeviceType(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;

            lock (_gate)
            {
                if (_byId.ContainsKey(Id))
                    throw new InvalidOperationException($"DeviceType id '{Id}' already registered.");

                _byId[Id] = this;
                _all.Add(this);
            }
        }

        /// <summary>
        /// Unique device type id (设备类型 Id)
        /// </summary>
        public string Id { get; }

        /// <inheritdoc />
        public virtual bool Equals(DeviceType? other) =>
            other is not null && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Try get registered type by Id (按 Id 获取，失败返回 false)
        /// </summary>
        public static bool TryFromId(string id, out DeviceType value)
        {
            if (id is null) { value = Unknown; return false; }
            lock (_gate) return _byId.TryGetValue(id, out value!);
        }

        /// <summary>
        /// Get type by Id or throw (按 Id 获取，不存在则抛异常)
        /// </summary>
        public static DeviceType FromId(string id) =>
            TryFromId(id, out var v) ? v : throw new KeyNotFoundException($"Unknown DeviceType id: {id}");

        /// <summary>
        /// Get all registered types (列出所有已注册类型)
        /// </summary>
        public static IReadOnlyList<DeviceType> List()
        {
            lock (_gate) return _all.ToArray();
        }

        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);

        /// <inheritdoc />
        public override string ToString() => Id;

        /// <summary>
        /// Register a new device type (注册新设备类型)
        /// </summary>
        public static DeviceType Register(string id) => new Generic(id);

        /// <summary>
        /// Internal generic device type implementation (内部通用实现)
        /// </summary>
        private sealed record Generic : DeviceType
        {
            public Generic(string id) : base(id) { }
        }
    }
}

