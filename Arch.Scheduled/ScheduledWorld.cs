using Arch.Core;
using Arch.Core.Utils;
using JobScheduler;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Arch.Scheduled;

public partial class ScheduledWorld
{

    private struct ComponentRegistration
    {
        public List<JobHandle> Readers = new();
        public JobHandle? Writer = new();

        public ComponentRegistration()
        { }
    }

    public Queue<List<JobHandle>> _listPool = new(32);

    private Dictionary<ComponentType, ComponentRegistration> _registrar = new();

    private readonly List<JobHandle> _dependencyCache = new(32);


    /// <summary>
    ///     Returns a <see cref="JobHandle"/> dependency handle with dependencies according to the provided <see cref="ScheduledQueryDescription"/>.
    /// </summary>
    /// <param name="queryDescription">
    ///     The query description, with <see cref="ScheduledQueryDescription.WithReads"/> and <see cref="ScheduledQueryDescription.WithWrites{T}"/>
    ///     according to the desired dependency.
    /// </param>
    /// <returns>A handle that can be either awaited directly or used as a dependency for further jobs.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JobHandle GetDependency(in ScheduledQueryDescription queryDescription)
    {
        if (!Scheduler.IsMainThread)
        {
            throw new NotOnMainThreadException();
        }

        return GetDependency(queryDescription.Reads, Array.Empty<ComponentType>(), queryDescription.Writes);
    }

    /// <summary>
    ///     Register an externally-scheduled <see cref="JobHandle"/> with the dependency system.
    ///     Note that the handle must have been recently scheduled with a dependency from <see cref="GetDependency(in ScheduledQueryDescription)"/>, with the same
    ///     query description, otherwise the behavior is undefined. Additionally, <see cref="JobScheduler.Flush()"/> must not have been called between the scheduling method
    ///     and the registration.
    /// </summary>
    /// <remarks>
    ///     This method is only for advanced custom job scheduling. If you just want to run a query using the <see cref="ScheduledWorld"/> scheduled query methods, registration
    ///     is done automatically.
    /// </remarks>
    /// <param name="queryDescription"><inheritdoc cref="GetDependency(in ScheduledQueryDescription)" path="/param[@name='queryDescription']"/></param>
    /// <param name="handle">The scheduled handle.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterDependency(in ScheduledQueryDescription queryDescription, in JobHandle handle)
    {
        if (!Scheduler.IsMainThread)
        {
            throw new NotOnMainThreadException();
        }

        RegisterDependency(queryDescription.Reads, Array.Empty<ComponentType>(), queryDescription.Writes, handle);
    }

    // We need 2 read arrays because there are two options for declaring reads which may exist simultaneously: query params (like in a lambda), or ScheduledQueryDescription
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JobHandle GetDependency(ComponentType[] reads, ComponentType[] reads2, ComponentType[] writes)
    {
        _dependencyCache.Clear();
        foreach (var write in writes)
        {
            var registration = _registrar[write];
            // We have to wait for all readers to finish first...
            if (registration.Readers.Count != 0)
            {
                foreach (var reader in _registrar[write].Readers)
                {
                    _dependencyCache.Add(reader);
                }
            }
            // ... or if there's only a writer, we wait for them.
            // This is enough because we know that if a reader is in the list, it must already depend on the current writer.
            else if (registration.Writer is not null)
            {
                _dependencyCache.Add(registration.Writer.Value);
            }
        }

        foreach (var read in reads)
        {
            var registration = _registrar[read];
            if (registration.Writer is not null)
            {
                _dependencyCache.Add(registration.Writer.Value);
            }
        }

        return UnsafeWorld.CombineDependencies(_dependencyCache.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RegisterDependency(ComponentType[] reads, ComponentType[] reads2, ComponentType[] writes, in JobHandle handle)
    {
        foreach (var read in reads)
        {
            RegisterRead(read, handle);
        }

        foreach (var read in reads2)
        {
            RegisterRead(read, handle);
        }

        foreach (var write in writes)
        {
            RegisterWrite(write, handle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RegisterRead(ComponentType read, JobHandle handle)
    {
        if (!_registrar.TryGetValue(read, out var registration))
        {
            registration = new();
            _registrar[read] = registration;
        }
        _registrar[read].Readers.Add(handle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RegisterWrite(ComponentType write, JobHandle handle)
    {
        if (!_registrar.TryGetValue(write, out var registration))
        {
            registration = new();
            _registrar[write] = registration;
        }
        registration.Writer = handle;
        registration.Readers.Clear();
        _registrar[write] = registration;
    }
}