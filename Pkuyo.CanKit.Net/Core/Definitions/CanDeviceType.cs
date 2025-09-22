using System;
using System.Collections.Generic;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    /// <summary>
    /// Device type registry with unique string Id and native code (设备类型注册，提供唯一 Id 与内部编码)。
    /// </summary>
    public abstract record DeviceType
    {
        /// <summary>
        /// Global native code, auto-incremented during registration (全局内部编码，自增)。
        /// </summary>
        public static int GlobalNativeId { get; private set; } = 0;

        /// <summary>
        /// Unique device type id (设备类型 Id)。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Internal native code for fast compare/lookup (内部编码)。
        /// </summary>
        public int NativeCode { get; }

        /// <summary>
        /// Custom metadata for extension (自定义元数据)。
        /// </summary>
        public object Metadata => _metadata;

        private object _metadata;

        private static readonly Dictionary<string, DeviceType> _byId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, DeviceType> _byCode = new();
        private static readonly List<DeviceType> _all = new();
        private static readonly object _gate = new();

        /// <summary>
        /// Construct and register device type (构造并完成注册)。
        /// </summary>
        /// <param name="id">Unique identifier, case-insensitive (唯一标识)。</param>
        /// <param name="metaData">Attached metadata (附加元数据)。</param>
        protected DeviceType(string id, object metaData)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            NativeCode = GlobalNativeId++;
            _metadata = metaData;

            lock (_gate)
            {
                if (_byId.ContainsKey(Id))
                    throw new InvalidOperationException($"DeviceType id '{Id}' already registered.");
                if (_byCode.ContainsKey(NativeCode))
                    throw new InvalidOperationException($"DeviceType code '{NativeCode}' already registered.");

                _byId[Id] = this;
                _byCode[NativeCode] = this;
                _all.Add(this);
            }
        }

        /// <summary>
        /// Try get registered type by Id (按 Id 获取，失败返回 false)。
        /// </summary>
        public static bool TryFromId(string id, out DeviceType value)
        {
            if (id is null) { value = Unknown; return false; }
            lock (_gate) return _byId.TryGetValue(id, out value!);
        }

        /// <summary>
        /// Get type by Id or throw (按 Id 获取，不存在则抛异常)。
        /// </summary>
        public static DeviceType FromId(string id) =>
            TryFromId(id, out var v) ? v : throw new KeyNotFoundException($"Unknown DeviceType id: {id}");

        /// <summary>
        /// Get all registered types (列出所有已注册类型)。
        /// </summary>
        public static IReadOnlyList<DeviceType> List()
        {
            lock (_gate) return _all.ToArray();
        }

        /// <inheritdoc />
        public virtual bool Equals(DeviceType? other) =>
            other is not null && NativeCode == other.NativeCode;

        /// <inheritdoc />
        public override int GetHashCode() => NativeCode.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => Id;

        /// <summary>
        /// Placeholder for unknown device (未知设备占位类型)。
        /// </summary>
        public static readonly DeviceType Unknown = new Generic("unknown", 0);

        /// <summary>
        /// Register a new device type (注册新设备类型)。
        /// </summary>
        public static DeviceType Register(string id, object meta) =>
            new Generic(id, meta);

        /// <summary>
        /// Internal generic device type implementation (内部通用实现)。
        /// </summary>
        private sealed record Generic : DeviceType
        {
            public Generic(string id, object code)
                : base(id, code) { }
        }
    }
}

