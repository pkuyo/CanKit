using System.ComponentModel;

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // 仅供编译器识别，避免出现在 IntelliSense 中
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit {}
}
#endif
