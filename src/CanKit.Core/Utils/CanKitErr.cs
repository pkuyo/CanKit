using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Core.Utils
{
    public static class CanKitErr
    {
        public static void ThrowIfNotSupport(CanFeature deviceFeatures, CanFeature checkFeature)
        {
            if ((deviceFeatures & checkFeature) == checkFeature)
            {
                return;
            }

            throw new CanFeatureNotSupportedException(checkFeature, deviceFeatures);
        }
    }
}
