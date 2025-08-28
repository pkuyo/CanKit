using System;
using System.Linq;
using System.Threading;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Registry;

namespace ZlgCAN.Net.Core
{
    public static class CanCore
    {
        private static readonly Lazy<CanRegistry> _Registry =
            new(BuildRegistry, LazyThreadSafetyMode.ExecutionAndPublication);

        public static CanRegistry Registry => _Registry.Value;

        private static CanRegistry BuildRegistry()
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(a => a.GetName().Name!.StartsWith("YourCompany.", StringComparison.Ordinal))
                .ToArray();

            var reg = new CanRegistry(asms);

            return reg;
        }

        public static ICanFactory ZLGFactory => Registry.Factory("ZLG");
    }
}