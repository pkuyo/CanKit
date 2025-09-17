using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;

namespace Pkuyo.CanKit.Net.Core.Utils
{
    public static class CanExtension
    {
        public static void CheckFeature(this CanFeature deviceFeatures, CanFeature checkFeature)
        {
            if ((deviceFeatures & checkFeature) == checkFeature)
            {
                return;
            }

            throw new CanFeatureNotSupportedException(checkFeature, deviceFeatures);
        }
    }
}
