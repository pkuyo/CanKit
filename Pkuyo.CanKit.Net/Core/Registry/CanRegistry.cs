using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;

namespace Pkuyo.CanKit.Net.Core.Registry
{

    /// <summary>
    /// Represents a registry for managing CAN model providers and factories. (表示用于管理CAN模型提供者和工厂的注册表)
    /// </summary>
    public partial class CanRegistry
    {

        /// <summary>
        /// Gets the singleton instance of the CAN registry. (获取CAN注册表的单例实例)
        /// </summary>
        public static CanRegistry Registry => _Registry.Value;


        /// <summary>
        /// Registers one or more CAN model providers.(注册设备描述)
        /// </summary>
        /// <param name="providers">The array of CAN model providers to register.(待注册的设备描述)</param>
        /// <exception cref="InvalidOperationException">Thrown when a provider with the same DeviceType is already registered.(设备类型已存在时抛出)</exception>
        public void RegisterProvider(params ICanModelProvider[] providers)
        {
            foreach (var provider in providers)
            {
                if (_providers.ContainsKey(provider.DeviceType))
                {
                    throw new InvalidOperationException($"A provider with the DeviceType '{provider.DeviceType}' is already registered.");
                }
                _providers.Add(provider.DeviceType, provider);
            }
        }

        /// <summary>
        /// Registers a CAN factory with the specified ID.
        /// (注册指定ID的CAN工厂)
        /// </summary>
        /// <param name="factoryId">The unique identifier for the factory.(工厂的唯一标识符)</param>
        /// <param name="factory">The CAN factory to register.(要注册的CAN工厂)</param>
        /// <exception cref="InvalidOperationException">Thrown when a factory with the same ID is already registered.(工厂ID已存在时抛出)</exception>
        public void RegisterFactory(string factoryId, ICanFactory factory)
        {
            if (_factories.ContainsKey(factoryId))
            {
                throw new InvalidOperationException($"A factory with the ID '{factoryId}' is already registered.");
            }
            _factories.Add(factoryId, factory);
        }

        /// <summary>
        /// Resolves a CAN model provider for the specified device type. (根据指定的设备类型解析CAN模型提供者)
        /// </summary>
        /// <param name="deviceType">The device type to resolve. (要解析的设备类型)</param>
        /// <returns>The resolved CAN model provider. (解析到的CAN模型提供者)</returns>
        /// <exception cref="NotSupportedException">Thrown when the device type is unknown. (未知设备类型时抛出)</exception>
        public ICanModelProvider Resolve(DeviceType deviceType)
        {
            if (_providers.TryGetValue(deviceType, out var provider))
                return provider;
            throw new NotSupportedException("Unknown device");
        }

        /// <summary>
        /// Retrieves a CAN factory by its unique identifier. (通过唯一标识符检索CAN工厂)
        /// </summary>
        /// <param name="factoryId">The unique identifier of the factory. (工厂的唯一标识符)</param>
        /// <returns>The CAN factory with the specified ID. (具有指定ID的CAN工厂)</returns>
        public ICanFactory Factory(string factoryId)
        {
            return _factories[factoryId];
        }
    }

    public partial class CanRegistry
    {
        private static CanRegistry BuildRegistry()
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .ToArray();

            var reg = new CanRegistry(asms);

            return reg;
        }

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
            var types = asm.GetTypes().Where(t =>
                typeof(ICanModelProvider).IsAssignableFrom(t) && !t.IsAbstract &&
                t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var t in types)
            {
                var provider = (ICanModelProvider)Activator.CreateInstance(t)!;
                if (_providers.ContainsKey(provider.DeviceType))
                {
                    CanKitLogger.LogError($"A provider with the DeviceType '{provider.DeviceType}' is already registered.");
                }
                else
                {
                    _providers.Add(provider.DeviceType, provider);
                }
            }

            // Scan group providers and expand to concrete providers per device type
            // ZH: 扫描分组 Provider，并按设备类型逐一创建并注册具体 Provider。
            var groupTypes = asm.GetTypes().Where(t =>
                typeof(ICanModelProviderGroup).IsAssignableFrom(t) && !t.IsAbstract &&
                t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var t in groupTypes)
            {
                ICanModelProviderGroup group;
                try
                {
                    group = (ICanModelProviderGroup)Activator.CreateInstance(t)!;
                }
                catch (Exception ex)
                {
                    CanKitLogger.LogWarning($"Failed to create provider group '{t.FullName}'.", ex);
                    continue;
                }

                IEnumerable<DeviceType> deviceTypes;
                try
                {
                    deviceTypes = group.SupportedDeviceTypes ?? Array.Empty<DeviceType>();
                }
                catch (Exception ex)
                {
                    CanKitLogger.LogWarning($"Failed to query SupportedDeviceTypes from group '{t.FullName}'.", ex);
                    continue;
                }

                foreach (var dt in deviceTypes)
                {
                    try
                    {
                        var provider = group.Create(dt);
                        if (provider == null)
                        {
                            CanKitLogger.LogError($"Provider group '{t.FullName}' returned null for DeviceType '{dt}'.");
                            continue;
                        }

                        if (_providers.ContainsKey(dt))
                        {
                            CanKitLogger.LogError($"A provider with the DeviceType '{dt}' is already registered.");
                        }
                        else
                        {
                            _providers.Add(dt, provider);
                        }
                    }
                    catch (Exception ex)
                    {
                        CanKitLogger.LogWarning($"Failed to create provider for DeviceType '{dt}' from group '{t.FullName}'.", ex);
                    }
                }
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
                if (_factories.ContainsKey(attr.FactoryId))
                {
                    CanKitLogger.LogError($"A factory with the ID '{attr.FactoryId}' is already registered.");
                }
                else
                {
                    _factories.Add(attr.FactoryId, factory);
                }

            }
        }

        private static readonly Lazy<CanRegistry> _Registry =
            new(BuildRegistry, LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly Dictionary<DeviceType, ICanModelProvider> _providers = new();

        private readonly Dictionary<string, ICanFactory> _factories = new();
    }
}
