using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Reflection;

namespace Zamay.Core;

/// <summary>Reusable inspector. Caches are shared; path, budgets and cancellation belong to one call.</summary>
public sealed class ObjectInspector
{
    internal const string ReflectionWarning = "Arbitrary runtime members require preservation when trimming. Native AOT needs explicit type preservation or custom inspection.";
    public TypeInspectorResolver Resolver { get; }
    public ReflectionMetadataCache Metadata { get; } = new();
    internal static readonly ConcurrentDictionary<Type, Lazy<Type?>> AsyncTypes = new();
    public ObjectInspector(IEnumerable<IValueInspector>? inspectors = null) => Resolver = new(inspectors ?? []);
    /// <summary>Creates a bounded snapshot. Public getters may run arbitrary code; cancellation is checked between traversal steps.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An inspection budget is invalid.</exception>
    [RequiresUnreferencedCode(ReflectionWarning)]
    public DisplayDocument Inspect(object? value, InspectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); options.Validate();
        return new(new InspectionSession(this, options, cancellationToken).Visit(value, 0, "$"));
    }
    /// <summary>Only the root async enumerable is consumed, when enabled. Tasks, streams and readers are never consumed.</summary>
    [RequiresUnreferencedCode(ReflectionWarning)]
    public async Task<DisplayDocument> InspectAsync(object? value, InspectionOptions? options = null,
        AsyncInspectionOptions? asyncOptions = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); options.Validate(); asyncOptions ??= new(); asyncOptions.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var asyncType = value is null ? null : GetAsyncType(value.GetType());
        if (!asyncOptions.EnumerateAsyncEnumerable || asyncType is null) return Inspect(value, options, cancellationToken);
        var method = typeof(ObjectInspector).GetMethod(nameof(ReadAsync), BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(asyncType.GetGenericArguments()[0]);
        return await (Task<DisplayDocument>)method.Invoke(this, [value, options, asyncOptions, cancellationToken])!;
    }
    internal static Type? GetAsyncType(Type t) => AsyncTypes.GetOrAdd(t, type => new(() => type.GetInterfaces()
        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)), true)).Value;
    private async Task<DisplayDocument> ReadAsync<T>(IAsyncEnumerable<T> source, InspectionOptions options, AsyncInspectionOptions asyncOptions, CancellationToken token)
    {
        var session = new InspectionSession(this, options, token);
        session.ReserveRoot(source);
        var nodes = ImmutableArray.CreateBuilder<DisplayNode>();
        var limit = Math.Min(options.MaxCollectionItems, asyncOptions.MaxItems);
        if (!session.CanVisit(1)) nodes.Add(session.Limit(1));
        else if (limit == 0) nodes.Add(new PlaceholderNode(PlaceholderReason.MoreItemsMayExist));
        else
        {
            try
            {
                await using var enumerator = source.GetAsyncEnumerator(token);
                while (nodes.Count < limit && session.CanVisit(1))
                {
                    token.ThrowIfCancellationRequested();
                    if (!await enumerator.MoveNextAsync()) return new(new CollectionNode("IAsyncEnumerable<" + TypeDisplayName.Of(typeof(T)) + ">", nodes.ToImmutable()));
                    nodes.Add(session.Visit(enumerator.Current, 1, "$[" + nodes.Count + "]"));
                }
                nodes.Add(session.CanVisit(1) ? new PlaceholderNode(PlaceholderReason.MoreItemsMayExist) : session.Limit(1));
            }
            catch (Exception ex) when (InspectionErrors.Recoverable(ex)) { nodes.Add(session.Error(ex)); }
        }
        return new(new CollectionNode("IAsyncEnumerable<" + TypeDisplayName.Of(typeof(T)) + ">", nodes.ToImmutable()));
    }
}

