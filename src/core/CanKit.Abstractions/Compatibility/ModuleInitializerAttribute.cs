#if NETSTANDARD2_0
using System;

namespace System.Runtime.CompilerServices;

/// <summary>Provides the module initializer marker for legacy target frameworks.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ModuleInitializerAttribute : Attribute;
#endif
