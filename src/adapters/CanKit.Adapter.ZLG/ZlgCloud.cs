using System;
using System.Threading;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Adapter.ZLG.Exceptions;
using CanKit.Adapter.ZLG.Native;

namespace CanKit.Adapter.ZLG;

/// <summary>ZLG cloud server connection session  (ZLG 云服务器连接会话)</summary>
public class ZlgCloud : IDisposable
{

    private bool _disposed;
    private readonly ZlgServerInfo _serverInfo;
    private readonly string _userName;

    /// <summary>Whether this instance has been disposed  (是否已释放)</summary>
    public bool Disposed => _disposed;

    /// <summary>Server info used to establish the connection  (建立连接所用的服务器信息)</summary>
    public ZlgServerInfo ServerInfo
    {
        get
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ZlgCloud));
            return _serverInfo;
        }
    }

    /// <summary>User name associated with this cloud session  (与此云会话关联的用户名)</summary>
    public string UserName
    {
        get
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ZlgCloud));
            return _userName;
        }
    }

    /// <summary>Connects to the ZLG cloud server and returns a new session  (连接到 ZLG 云服务器并返回新会话)</summary>
    /// <param name="serverInfo">Server endpoint info  (服务器端点信息)</param>
    /// <param name="userName">User name  (用户名)</param>
    /// <param name="password">Password  (密码)</param>
    /// <returns>The new <see cref="ZlgCloud"/> session  (新的会话实例)</returns>
    /// <exception cref="ZlgCloudConnectException">Already connected or connection failed  (已连接或连接失败)</exception>
    public static ZlgCloud ConnectServer(ZlgServerInfo serverInfo, string userName, string password)
    {
        if (ZLGCAN.ZCLOUD_IsConnected())
        {
            throw new ZlgCloudConnectException(serverInfo,
                $"already connect to ZLGCloud server. Info:{ZlgCloudStore.Instance!.Cloud!.ServerInfo}, Username:{ZlgCloudStore.Instance.Cloud.UserName}");
        }
        ZLGCAN.ZCLOUD_SetServerInfo(serverInfo.AuthServer, (ushort)serverInfo.AuthPort, serverInfo.Mqtt, (ushort)serverInfo.MqttPort);
        var rst = ZLGCAN.ZCLOUD_ConnectServer(userName, password);

        if (rst != 0)
        {
            var errMsg = rst switch
            {
                1 => "Unknown error",
                2 => "Authentication server connection error",
                3 => "User information verification error",
                4 => "Data server connection error",
                _ => "Unexpected error"
            };
            throw new ZlgCloudConnectException(serverInfo,
                $"failed to connect ZLGCloud server. Info:{serverInfo}, Username:{userName}, ErrorMessage:{errMsg}");
        }

        var cloud = new ZlgCloud(serverInfo, userName);
        ZlgCloudStore.Instance.Replace(cloud);
        return cloud;
    }

    private ZlgCloud(ZlgServerInfo serverInfo, string userName)
    {
        _serverInfo = serverInfo;
        _userName = userName;
    }


    /// <summary>Returns whether the session is connected to the server  (返回会话是否已连接到服务器)</summary>
    public bool IsConnected()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ZlgCloud));
        return ZLGCAN.ZCLOUD_IsConnected();
    }

    /// <summary>Disconnects from the server and releases all resources  (断开服务器连接并释放所有资源)</summary>
    public void Dispose()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ZlgCloud));
        _disposed = true;
        ZlgCloudStore.Instance.Replace(null);
        ZlgErr.ThrowIfError(ZLGCAN.ZCLOUD_DisconnectServer() == 0 ? ZlgErr.StatusOk : 0, nameof(ZLGCAN.ZCLOUD_DisconnectServer));
    }
}

/// <summary>Thread-safe singleton holding the current active <see cref="ZlgCloud"/> instance  (线程安全单例，持有当前活动的 <see cref="ZlgCloud"/> 实例)</summary>
public sealed class ZlgCloudStore
{
    private static readonly Lazy<ZlgCloudStore> _lazy =
        new(() => new ZlgCloudStore());

    /// <summary>The singleton instance  (单例实例)</summary>
    public static ZlgCloudStore Instance => _lazy.Value;

    private ZlgCloud? _current;

    /// <summary>The current active session, or <c>null</c> if none  (当前活动会话，无则为 <c>null</c>)</summary>
    public ZlgCloud? Cloud => this._current;

    private ZlgCloudStore() { }



    /// <summary>Atomically replaces the current session  (以原子方式替换当前会话)</summary>
    /// <param name="next">New session, or <c>null</c> to clear  (新会话，传入 <c>null</c> 可清除)</param>
    public void Replace(ZlgCloud? next)
    {
        Interlocked.Exchange(ref _current, next);
    }
}
