// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

using ZlgCAN.Net.Core.Attributes;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Gen.Sample
{
    [CanOptions]
    [CanValue("maxConnections", typeof(int), DefaultValue = 10, Access = CanValueAccess.Set)]
    [CanValue("enableCache",    typeof(bool), DefaultValue = true)]
    [CanValue("timeoutMs",      typeof(int), DefaultValue = 5000)]
    public partial class MyOptions
    {

        
    }
}



