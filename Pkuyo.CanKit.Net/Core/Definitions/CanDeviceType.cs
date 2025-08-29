using System;
using System.Collections.Generic;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    public abstract record DeviceType
    {
        public string Id { get; }
        public int NativeCode { get; }           
        public IReadOnlyDictionary<string, string> Metadata => _metadata;
        private readonly Dictionary<string, string> _metadata;

       
        private static readonly Dictionary<string, DeviceType> _byId = new (StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, DeviceType> _byCode = new ();
        private static readonly List<DeviceType> _all = new ();
        private static readonly object _gate = new ();

        protected DeviceType(string id, int nativeCode, IDictionary<string, string> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            NativeCode = nativeCode;
            _metadata = metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(metadata);
            
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
        
        public static bool TryFromId(string id, out DeviceType value)
        {
            if (id is null) { value = Unknown; return false; }
            lock (_gate) return _byId.TryGetValue(id, out value!);
        }

        public static bool TryFromCode(int code, out DeviceType value)
        {
            lock (_gate) return _byCode.TryGetValue(code, out value!);
        }

        public static DeviceType FromId(string id) =>
            TryFromId(id, out var v) ? v : throw new KeyNotFoundException($"Unknown DeviceType id: {id}");

        public static DeviceType FromCode(int code) =>
            TryFromCode(code, out var v) ? v : throw new KeyNotFoundException($"Unknown DeviceType code: {code}");

        public static IReadOnlyList<DeviceType> List()
        {
            lock (_gate) return _all.ToArray(); 
        }
        
        public virtual bool Equals(DeviceType other) =>
            other is not null && NativeCode == other.NativeCode;

        public override int GetHashCode() => NativeCode.GetHashCode();

        public override string ToString() => Id;
        
        public static readonly DeviceType Unknown = new Generic("unknown", 0);


        public static DeviceType Register(string id, int nativeCode, IDictionary<string,string> metadata = null) =>
            new Generic(id, nativeCode, metadata);
        
        private sealed record Generic : DeviceType
        {
            public Generic(string id, int code, IDictionary<string,string> meta = null)
                : base(id, code, meta) { }
        }
    }
}