using System;

namespace Pkuyo.CanKit.Net.Core.Abstractions;

/// <summary>
/// Preferred single-bus interface (首选的单总线接口)。为兼容保留自 ICanChannel 继承。
/// </summary>
public interface ICanBus : ICanChannel { }

/// <summary>
/// Strong-typed bus with RT configurator (带强类型运行时配置器的总线)。
/// </summary>
public interface ICanBus<out TConfigurator> : ICanBus, ICanChannel<TConfigurator>
    where TConfigurator : IChannelRTOptionsConfigurator
{ }
