using Arch.Core;
using Arch.Core.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Arch.Scheduled;

/// <inheritdoc cref="QueryDescription"/>
/// <remarks>
///     The <see cref="ScheduledQueryDescription"/> wrapper provides the ability to declare Reads and Writes on the <see cref="QueryDescription"/>,
///     for use in <see cref="ScheduledWorld"/>.
/// </remarks>
public partial struct ScheduledQueryDescription
{
    internal QueryDescription Inner;

    /// <inheritdoc cref="QueryDescription.Null"/>
    public static readonly ScheduledQueryDescription Null = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScheduledQueryDescription"/> struct.
    /// </summary>
    public ScheduledQueryDescription() { }

    /// <inheritdoc cref="QueryDescription.None"/>
    public ComponentType[] None
    {
        readonly get => Inner.None;
        set => Inner.None = value;
    }

    /// <inheritdoc cref="QueryDescription.All"/>
    public ComponentType[] All
    {
        readonly get => Inner.All;
        set => Inner.All = value;
    }

    /// <inheritdoc cref="QueryDescription.Any"/>
    public ComponentType[] Any
    {
        readonly get => Inner.Any;
        set => Inner.Any = value;
    }

    /// <inheritdoc cref="QueryDescription.Exclusive"/>
    public ComponentType[] Exclusive
    {
        readonly get => Inner.Exclusive;
        set => Inner.Exclusive = value;
    }

    /// <summary>
    ///     An array of all components which a query explicitly reads.
    /// </summary>
    /// <remarks>
    ///     Note that explicitly defining <see cref="Reads"/> is rarely needed, as all components referenced in a lambda query or <see cref="IForEach"/> struct
    ///     are automatically registered as <see cref="Reads"/>.
    /// </remarks>
    public ComponentType[] Reads = Array.Empty<ComponentType>();

    /// <summary>
    ///     An array of all components which a query explicitly writes.
    /// </summary>
    public ComponentType[] Writes = Array.Empty<ComponentType>();

    /// <summary>
    ///     Converts a <see cref="ScheduledQueryDescription"/> to a <see cref="QueryDescription"/>. Strips all data from <see cref="Reads"/> and
    ///     <see cref="Writes"/> in the resulting struct, so use with care.
    /// </summary>
    /// <param name="description"></param>
    public static explicit operator QueryDescription(ScheduledQueryDescription description) => description.Inner;

    /// <summary>
    ///     Converts a <see cref="QueryDescription"/> to a <see cref="ScheduledQueryDescription"/>.
    /// </summary>
    /// <param name="description"></param>
    public static implicit operator ScheduledQueryDescription(QueryDescription description) => new() { Inner = description };

    // TODO: generate overrides incl. variadics via sourcegen

    /// <summary>
    ///     Marks components which a query explicitly reads.
    /// </summary>
    /// <inheritdoc cref="Reads" path="/remarks"/>
    /// <typeparam name="T">The generic type.</typeparam>
    /// <returns>The same <see cref="ScheduledQueryDescription"/> instance for chained operations.</returns>
    // [Variadic]
    [UnscopedRef]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ScheduledQueryDescription WithReads<T>()
    {
        Reads = Group<T>.Types;
        return ref this;
    }

    /// <summary>
    ///     Marks components which a query explicitly writes.
    /// </summary>
    /// <typeparam name="T">The generic type.</typeparam>
    /// <returns>The same <see cref="ScheduledQueryDescription"/> instance for chained operations.</returns>
    // [Variadic]
    [UnscopedRef]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ScheduledQueryDescription WithWrites<T>()
    {
        Writes = Group<T>.Types;
        return ref this;
    }
}
