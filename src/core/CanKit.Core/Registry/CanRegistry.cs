using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
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
    /// Try prepare endpoint to construct provider + device/channel config without opening.
    /// ZH: 尝试仅构造Provider与设备/通道配置，不执行打开。
    /// </summary>
    public bool TryPrepareEndPoint(string endpoint, Action<IBusInitOptionsConfigurator>? configure, out PreparedBusContext? prepared)
    {
        var ep = CanEndpoint.Parse(endpoint);
        if (_prepareHandlers.TryGetValue(ep.Scheme, out var p))
        {
            prepared = p(ep, configure);
            return true;
        }
        prepared = null;
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
    /// Register an endpoint by descriptor (open/prepare/enumerate) without reflection.
    /// </summary>
    internal void RegisterEndPoint(EndpointRegistration e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));
        if (string.IsNullOrWhiteSpace(e.Scheme)) throw new ArgumentNullException(nameof(e.Scheme));

        _handlers[e.Scheme] = e.Open;
        _prepareHandlers[e.Scheme] = e.Prepare;

        if (e.Enumerate != null)
            _enumerators[e.Scheme] = e.Enumerate;

        foreach (var alias in e.Alias.Append(e.Scheme))
        {
            if (!_enumeratorAlias.ContainsKey(alias))
                _enumeratorAlias[alias] = e.Scheme;
        }


    }
}

public partial class CanRegistry
{

    private static readonly Lazy<CanRegistry> _registry =
        new(BuildRegistry, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, Func<IEnumerable<BusEndpointInfo>>> _enumerators =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _enumeratorAlias = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ICanFactory> _factories = new();

    private readonly Dictionary<string, Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, ICanBus>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Func<CanEndpoint, Action<IBusInitOptionsConfigurator>?, PreparedBusContext>> _prepareHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<DeviceType, ICanModelProvider> _providers = new();

    internal static CanRegistry? Instance;

    internal CanRegistry(params Assembly[] assembliesToScan)
    {
        Instance = this;
        var assemblies = assembliesToScan.Length == 0 ? [Assembly.GetExecutingAssembly()] : assembliesToScan;
        ExecuteRegistrationPipeline(assemblies);
    }

    private static Assembly Entry()
    {

        var entry = Assembly.GetEntryAssembly();
        if (entry != null) return entry;

        var withEntry = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.EntryPoint != null);
        if (withEntry != null) return withEntry;

        return Assembly.GetExecutingAssembly();
    }
    private static CanRegistry BuildRegistry()
    {
        try
        {
            var genType = Entry().GetType("CanKit.Core.Internal.AdapterPreloadList", false);

            if (genType?
                    .GetField("Assemblies", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                    .GetValue(null) is string[] names)
            {
                foreach (var n in names.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    try { SafeLoad(new AssemblyName(n)); }
                    catch { /* ignore one-off load errors */ }
                }
            }
        }
        catch { /* ignore any preload reflection errors */ }
        var asms = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .ToArray();

        var reg = new CanRegistry(asms);

        return reg;
    }

    private static void SafeLoad(AssemblyName path)
    {
        try
        {
#if NET5_0_OR_GREATER
            var asm = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(path);
#else
            var asm = Assembly.Load(path); // .NET Framework
#endif

        }
        catch {/* ignore any preload reflection errors */ }
    }
}

public partial class CanRegistry
{
    /// <summary>
    /// Enumerate discoverable endpoints. When schemes is null/empty, include all registered schemes.
    /// ZH: 枚举可发现的端点；若未指定 scheme，则合并所有 scheme 的结果。
    /// </summary>
    public IEnumerable<BusEndpointInfo> EnumerateEndPoints(IEnumerable<string>? vendorsOrSchemes)
    {
        if (vendorsOrSchemes is null)
        {
            foreach (var e in _enumerators.Values)
            {
                IEnumerable<BusEndpointInfo> items;
                try { items = e() ?? []; }
                catch { items = []; }
                foreach (var it in items) yield return it;
            }
            yield break;
        }
        var schemes = vendorsOrSchemes.Select(i =>
        {
            if (_enumeratorAlias.TryGetValue(i, out var aliased))
                return aliased;
            return i;
        });
        var set = new HashSet<string>(schemes, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _enumerators)
        {
            if (!set.Contains(kv.Key)) continue;
            IEnumerable<BusEndpointInfo> items;
            try { items = kv.Value() ?? []; }
            catch { items = []; }
            foreach (var it in items) yield return it;
        }
    }
}
