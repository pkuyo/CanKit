using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Attributes;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Registry
{

    public class CanRegistry
    {
     

        internal CanRegistry(params Assembly[] assembliesToScan)
        {
            var assemblies = assembliesToScan.Length == 0 ? [Assembly.GetExecutingAssembly()] : assembliesToScan;

            foreach (var asm in assemblies)
            {
                RegisterFactory(asm);
                RegisterProvider(asm);
            }
        }

        private void RegisterProvider(Assembly asm)
        {
            _ = ZlgDeviceType.ZCAN_CANFDDTU_400_TCP;
            var types = asm.GetTypes().Where(t =>
                typeof(ICanModelProvider).IsAssignableFrom(t) && !t.IsAbstract &&
                t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var t in types)
            {
                var attr = t.GetCustomAttribute<CanModelAttribute>();
                if (attr == null) continue;
                var provider = (ICanModelProvider)Activator.CreateInstance(t)!;
                _providers.Add(DeviceType.FromId(attr.DeviceType), provider);
            }
        }

        private void RegisterFactory(Assembly asm)
        {
            var types = asm.GetTypes().Where(t =>
                typeof(ICanFactory).IsAssignableFrom(t) && !t.IsAbstract &&
                t.GetConstructor(Type.EmptyTypes) != null);
            foreach (var t in types)
            {
                var attr = t.GetCustomAttribute<CanFactoryAttribute>();
                if (attr == null) continue;
                var factory = (ICanFactory)Activator.CreateInstance(t)!;
                _factories.Add(attr.FactoryId, factory);
            }
        }
        

        public ICanModelProvider Resolve(DeviceType deviceType)
        {
            if (_providers.TryGetValue(deviceType, out var provider))
                return provider;
            throw new NotSupportedException("Unknown device");
        }
        public ICanFactory Factory(string factoryId)
        {
            return _factories[factoryId];
        }
        private readonly Dictionary<DeviceType, ICanModelProvider> _providers = new();
        
        private readonly Dictionary<string, ICanFactory> _factories = new ();
    }
}
