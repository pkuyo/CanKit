using System;
using System.ComponentModel;
using Pkuyo.CanKit.Net.Core.Abstractions;

namespace Pkuyo.CanKit.Net.Core.Internal;

[Obsolete("Access ICanOptions may caused unexpected error.", error: false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IOptionsAccessor<out T> where T : ICanOptions
{
    T Options { get; }
}