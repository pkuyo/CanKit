using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Endpoints;

namespace CanKit.Core.Registry;

/// <summary>
/// Represents a registry for managing CAN model providers and factories. (表示用于管理CAN模型提供者和工厂的注册表)
/// </summary>
public partial class CanRegistry
{

    /// <summary>
    /// Gets the singleton instance of the CAN registry. (获取CAN注册表的单例实例)
    /// </summary>
    public static CanRegistry Registry => _registry.Value;

    /// <summary>
    /// Resolves a CAN model provider for the specified device type. (根据指定的设备类型解析CAN模型提供者)
    /// </summary>
    /// <param name="deviceType">The device type to resolve. (要解析的设备类型)</param>
    /// <returns>The resolved CAN model provider. (解析到的CAN模型提供者)</returns>
    public ICanModelProvider Resolve(DeviceType deviceType)
    {
        if (_providers.TryGetValue(deviceType, out var provider))
            return provider;

        var ex = new NotSupportedException(
            $"Unknown device. DeviceType='{deviceType}");

        CanKitLogger.LogError(
            $"Unknown device requested. DeviceType='{deviceType}'.",
            ex);

        throw ex;
    }

    /// <summary>
    /// Retrieves a CAN factory by its unique identifier. (通过唯一标识符检索CAN工厂)
    /// </summary>
    /// <param name="factoryId">The unique identifier of the factory. (工厂的唯一标识符)</param>
    /// <returns>The CAN factory with the specified ID. (具有指定ID的CAN工厂)</returns>
    public ICanFactory Factory(string factoryId)
    {
        if (string.IsNullOrWhiteSpace(factoryId))
            throw new ArgumentException($"FactoryId is null or empty.", nameof(factoryId));

        if (_factories.TryGetValue(factoryId, out var factory))
            return factory;

        var known = _factories.Count == 0 ? "<none>" : string.Join(", ", _factories.Keys);
        var ex = new NotSupportedException(
            $"Unknown factory. FactoryId='{factoryId}'. Known=[{known}]");

        CanKitLogger.LogError(
            $"Unknown factory requested. FactoryId='{factoryId}'. Registered=[{known}]",
            ex);

        throw ex;
    }

    /// <summary>
    /// Try open bus by endpoint (按 endpoint 尝试打开总线)。
    /// </summary>
    public bool TryOpenEndPoint(string endpoint, Action<IBusInitOptionsConfigurator>? configure, out ICanBus? bus)
    {
        var ep = CanEndpoint.Parse(endpoint);
        if (_handlers.TryGetValue(ep.Scheme, out var h))
        {
            bus = h(ep, configure);
            return true;
        }
        bus = null;
        return false;
    }

    /// <summary>
    /// Registers one or more CAN model providers.(注册设备描述)
    /// </summary>
    /// <param name="providers">The array of CAN model providers to register.(待注册的设备描述)</param>
    /// <exception cref="InvalidOperationException">Thrown when a provider with the same DeviceType is already registered.(设备类型已存在时抛出)</exception>
    internal void RegisterProvider(params ICanModelProvider[] providers)
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
    internal void RegisterFactory(string factoryId, ICanFactory factory)
    {
        if (_factories.ContainsKey(factoryId))
        {
            throw new InvalidOperationException($"A factory with the ID '{factoryId}' is already registered.");
        }
        _factories.Add(factoryId, factory);
    }

    /// <summary>
    /// Register a scheme handler (注册一个 scheme 处理器)。
    /// </summary>
    internal void RegisterEndPoint(string scheme, Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus> openHandler)
    {
        if (string.IsNullOrWhiteSpace(scheme)) throw new ArgumentNullException(nameof(scheme));
        _handlers[scheme] = openHandler ?? throw new ArgumentNullException(nameof(openHandler));
    }
}

public partial class CanRegistry
{

    private const string DefaultPrefix = "CanKit";

    private static CanRegistry BuildRegistry()
    {
        //TODO:白名单
        var pre = DefaultPrefix;
        var baseDir = AppContext.BaseDirectory;
        foreach (var path in Directory.EnumerateFiles(baseDir, "*.dll"))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith(pre, StringComparison.OrdinalIgnoreCase))
            {
                SafeLoadFromPath(path);
            }
        }

        var asms = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .ToArray();

        var reg = new CanRegistry(asms);

        return reg;
    }

    private static void SafeLoadFromPath(string path)
    {
        try
        {
#if NET5_0_OR_GREATER
            var asm = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
#else
            var asm = Assembly.LoadFrom(path); // .NET Framework
#endif

        }
        catch { /* 忽略非托管/不兼容/重复加载等 */ }
    }

    internal CanRegistry(params Assembly[] assembliesToScan)
    {
        var assemblies = assembliesToScan.Length == 0 ? [Assembly.GetExecutingAssembly()] : assembliesToScan;

        foreach (var asm in assemblies)
        {
            RegisterFactory(asm);
            RegisterProvider(asm);
            RegisterEndPoint(asm);
        }
    }

    private void RegisterEndPoint(Assembly asm)
    {
        const string expectedSig = "public static ICanBus Open(CanEndpoint, Action<IBusInitOptionsConfigurator>?)";

        var types = asm.GetTypes()
            .Select(type => (type, type.GetCustomAttribute<CanEndPointAttribute>()))
            .Where(t => t.Item2 != null);

        foreach (var (type, attr) in types)
        {
            if (attr is null)
            {
                continue;
            }

            try
            {
                var method = type.GetMethod("Open",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(CanEndpoint), typeof(Action<IBusInitOptionsConfigurator>)],
                    null);

                if (method == null)
                {
                    CanKitLogger.LogError(
                        $"RegisterEndpoint skipped: method not found. Type={type.AssemblyQualifiedName}, Scheme={attr.Scheme}, ExpectedSignature={expectedSig}");
                    continue;
                }

                if (!typeof(ICanBus).IsAssignableFrom(method.ReturnType))
                {
                    CanKitLogger.LogError(
                        $"RegisterEndpoint skipped: return type mismatch. Type={type.AssemblyQualifiedName}, Scheme={attr.Scheme}, ExpectedReturn={typeof(ICanBus).FullName}, ActualReturn={method.ReturnType.FullName}");
                    continue;
                }

                RegisterEndPoint(attr.Scheme,
                    (Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus>)
                    method.CreateDelegate(
                        typeof(Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus>)));
                CanKitLogger.LogInformation(
                    $"Registered endpoint. Scheme='{attr.Scheme}', Type='{type.AssemblyQualifiedName}'");
            }
            catch (AmbiguousMatchException ex)
            {
                CanKitLogger.LogError(
                    $"RegisterEndpoint skipped: multiple overloads matched. Type={type.AssemblyQualifiedName}, Scheme={attr.Scheme}",
                    ex);
            }
            catch (ArgumentException ex)
            {
                CanKitLogger.LogError(
                    $"RegisterEndpoint skipped: delegate creation failed due to signature incompatibility. Type={type.AssemblyQualifiedName}, Scheme={attr.Scheme}, ExpectedSignature={expectedSig}",
                    ex);
            }
            catch (Exception ex)
            {
                CanKitLogger.LogError(
                    $"RegisterEndpoint skipped: unexpected error. Type={type.AssemblyQualifiedName}, Scheme={attr.Scheme}",
                    ex);
            }
        }

    }

    private void RegisterProvider(Assembly asm)
    {
        var types = asm.GetTypes().Where(t =>
            typeof(ICanModelProvider).IsAssignableFrom(t) && !t.IsAbstract &&
            t.GetConstructor(Type.EmptyTypes) != null);

        foreach (var t in types)
        {
            try
            {
                var provider = (ICanModelProvider)Activator.CreateInstance(t)!;

                if (string.IsNullOrWhiteSpace(provider.DeviceType.Id))
                {
                    CanKitLogger.LogError(
                        $"Provider '{t.AssemblyQualifiedName}' returned an empty DeviceType. Skipped.");
                    return;
                }

                if (_providers.TryGetValue(provider.DeviceType, out var existing))
                {
                    CanKitLogger.LogError(
                        $"A provider with DeviceType '{provider.DeviceType}' is already registered. " +
                        $"Existing={existing.GetType().AssemblyQualifiedName}, Incoming={t.AssemblyQualifiedName}");
                    return;
                }

                _providers.Add(provider.DeviceType, provider);
                CanKitLogger.LogInformation(
                    $"Registered provider. DeviceType='{provider.DeviceType}', Type='{t.AssemblyQualifiedName}'");
            }
            catch (Exception ex)
            {
                CanKitLogger.LogError(
                    $"Failed to register provider. Type={t.AssemblyQualifiedName}",
                    ex);
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
                deviceTypes = group.SupportedDeviceTypes;
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
            if (attr == null)
            {
                continue;
            }


            try
            {
                var factory = Activator.CreateInstance(t) as ICanFactory;

                if (factory is null)
                {
                    CanKitLogger.LogError(
                        $"CanFactory create instance failed. Type='{t.AssemblyQualifiedName}'");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(attr.FactoryId))
                {
                    CanKitLogger.LogError(
                        $"CanFactoryAttribute.FactoryId is null/empty. Type='{t.AssemblyQualifiedName}'");
                    continue;
                }

                if (_factories.TryGetValue(attr.FactoryId, out var existing))
                {
                    CanKitLogger.LogError(
                        $"A factory with the ID '{attr.FactoryId}' is already registered. Existing='{existing.GetType().AssemblyQualifiedName}', Incoming='{t.AssemblyQualifiedName}'");
                    continue;
                }

                _factories.Add(attr.FactoryId, factory);

                CanKitLogger.LogInformation(
                    $"Registered factory. FactoryId='{attr.FactoryId}', Type='{t.AssemblyQualifiedName}'");
            }
            catch (Exception ex)
            {
                CanKitLogger.LogError(
                    $"Failed to register factory. Type='{t.AssemblyQualifiedName}', FactoryId='{attr.FactoryId}'",
                    ex);
            }
        }
    }

    private static readonly Lazy<CanRegistry> _registry =
        new(BuildRegistry, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<DeviceType, ICanModelProvider> _providers = new();

    private readonly Dictionary<string, ICanFactory> _factories = new();

    private readonly Dictionary<string, Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
}

