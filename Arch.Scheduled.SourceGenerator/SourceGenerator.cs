using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Text;

namespace Arch.System.SourceGenerator;

[Generator]
public class SourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // filter down to the assembly class so we don't re-evaluate
        var world = context.CompilationProvider
            // runs every time the compilation changes (i.e. constantly)
            .Select(static (compilation, _) =>
            {
                // grabs the arch symbol
                return compilation.SourceModule.ReferencedAssemblySymbols.First(q => q.Name == "Arch");
            })
            // runs every time the arch symbol changes (i.e. never, I think? not sure exactly how it checks for equality)
            .Select(static (archSymbol, _) =>
            {
                var world = archSymbol.GlobalNamespace
                    .GetNamespaceMembers().First(q => q.Name == "Arch")
                    .GetNamespaceMembers().First(q => q.Name == "Core")
                    .GetTypeMembers();
                return world;
            });

        context.RegisterSourceOutput(world, (ctx, types) =>
        {
            var world = types.First(q => q.Name == "World");
            var queryDescription = types.First(q => q.Name == "QueryDescription");

            ctx.AddSource("ScheduledWorld.Wrapper.g.cs",
                CSharpSyntaxTree.ParseText(GenerateWorldWrapper(world)).GetRoot().NormalizeWhitespace().ToFullString());
            ctx.AddSource("ScheduledQueryDescription.Variadics.g.cs",
                CSharpSyntaxTree.ParseText(GenerateQueryDescriptionVariadics()).GetRoot().NormalizeWhitespace().ToFullString());
            ctx.AddSource("ScheduledQueryDescription.Wrapper.g.cs",
                CSharpSyntaxTree.ParseText(GenerateQueryDescriptionWrapper(queryDescription)).GetRoot().NormalizeWhitespace().ToFullString());
        });
    }

    private string GenerateQueryDescriptionVariadics()
    {
        // naive variadics for WithRead/WithWrite. A proper variadic implementation would be better.
        StringBuilder methods = new();
        foreach (string method in new string[] { "Reads", "Writes" })
        {
            StringBuilder generics = new();
            for (int i = 0; i < 25; i++)
            {
                if (generics.Length != 0)
                {
                    generics.Append(", ");
                }
                generics.Append($"T{i}");
                // only start at 2 generics
                if (i == 0)
                {
                    continue;
                }
                methods.AppendLine($$"""
                    <inheritdoc cref="With{{method}}<T>"/>
                    [UnscopedRef]
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public ref ScheduledQueryDescription With{{method}}<{{generics}}>()
                    {
                        {{method}} = Arch.Core.Group<{{generics}}>.Types;
                        return ref this;
                    }
                    """);
            }
        }

        return $$"""
            public partial struct ScheduledQueryDescription
            {
                {{methods}}
            }
            """;
    }

    private string GenerateQueryDescriptionWrapper(INamedTypeSymbol queryDescription)
    {
        // much simpler than World.... only override With____ methods
        var methods = queryDescription.GetMembers().OfType<IMethodSymbol>().Where(symbol => symbol.Name.StartsWith("With"));

        StringBuilder wrappedMethods = new();
        foreach (var method in methods)
        {
            string name = method.Name;
            string generics = string.Join(", ", method.TypeParameters.Select(t => t.Name));

            wrappedMethods.AppendLine($$"""
                <inheritdoc cref="Arch.Core.QueryDescription.{{method.Name}}"/>
                [UnscopedRef]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public ref ScheduledQueryDescription {{method.Name}}<{{generics}}>()
                {
                    Inner = Inner.{{method.Name}}<{{generics}}>();
                    return ref this;
                }
                """);
        }

        return $$"""
            public partial struct ScheduledQueryDescription
            {
                {{wrappedMethods}}
            }
            """;
    }

    private enum MemberHandling
    {
        // no modifiers, just wrap
        Wrap,
        // adds query guards
        GenericQuery,
        // adds scheduled query guards
        GenericScheduledQuery,
        // excludes from wrap; forces use of UnsafeWorld
        Exclude,
        StructuralChange
    }

    private struct MemberInfo
    {
        public MemberHandling Handling = MemberHandling.Exclude;
        public string Name = string.Empty;
        public bool Static = false;
        public int? ParamCount = null; // if null, matches any param count
        public MemberInfo() { }
    }

    private string GenerateWorldWrapper(INamedTypeSymbol world)
    {
        // These methods are excluded from wrapping; we either make custom overrides in the actual assembly, or
        // we can't make them safe.

        MemberInfo[] specialMembers =
        {
            new() { Name = "Create", Static = true },
            new() { Name = "Destroy", Static = true },
            new() { Name = "Worlds", Static = true },
            new() { Name = "InlineParallelChunkQuery" },
            // the default 1 param query returns a Query struct, which we don't support. (what if they store the query and use it later?)
            new() { Name = "Query", ParamCount = 1 },
            // it is invalid to use GetEntities because anything you can do with them is invalid. (what if you read a component but haven't declared the read?)
            // same with these others. The user should use UnsafeWorld and declare their reads.
            new() { Name = "GetEntities" },
            new() { Name = "GetChunks" },
            new() { Name = "GetChunk" },

            // Most non-structural accessors are invalid beacause this would be reading undeclared things.
            // TODO: we can definitely enable these with some trickery; we just have to declare the read immediately and define a context if within scheduled query.
            // The issue here is non-generics and thread assurance.
            new() { Name = "Get" },
            new() { Name = "TryGet" },
            new() { Name = "TryGetRef" },
            new() { Name = "GetRange" },
            new() { Name = "GetAllComponents" },
            new() { Name = "GetAllComponents" },

            // CountEntities works, but we have to treat it as a query
            new() { Name = "CountEntities", Handling = MemberHandling.GenericQuery },
            new() { Name = "Query", Handling = MemberHandling.GenericQuery },
            new() { Name = "InlineQuery", Handling = MemberHandling.GenericQuery },
            new() { Name = "InlineEntityQuery", Handling = MemberHandling.GenericQuery },
            new() { Name = "ParallelQuery", Handling = MemberHandling.GenericScheduledQuery },
            new() { Name = "InlineParallelQuery", Handling = MemberHandling.GenericScheduledQuery },
            new() { Name = "InlineParallelEntityQuery", Handling = MemberHandling.GenericScheduledQuery },
        };


        var members = world.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public)
            // Map the members to their handling
            .Select(m =>
            {
                foreach (var info in specialMembers)
                {
                    // If this isn't us, we keep looking
                    if (info.Name != m.Name)
                    {
                        continue;
                    }

                    // If we don't match the static constraint, this isn't us
                    if (info.Static != m.IsStatic)
                    {
                        continue;
                    }

                    // If there's a param constraint and we're a property, this isn't us
                    if (info.ParamCount is not null && m is IPropertySymbol)
                    {
                        continue;
                    }

                    // If we don't match the param constraint, this isn't us
                    if (info.ParamCount is not null && m is IMethodSymbol method && method.Parameters.Length != info.ParamCount)
                    {
                        continue;
                    }

                    // we found a matching handling, so use that
                    return (m, info.Handling);
                }

                // We didn't find a matching handling override, so either wrap or structural change, based on attr.
                if (m.GetAttributes().Any(attrData => attrData.AttributeClass?.Name == "StructuralChangesAttribute"))
                {
                    return (m, MemberHandling.StructuralChange);
                }
                return (m, MemberHandling.Wrap);
            })
            .Where(tu => tu.Item2 != MemberHandling.Exclude);

        // This does ***NOT*** (yet) support: 
        // - readonly (structs)
        // - abstract
        // - virtual
        // - override
        // This is OK because World doesn't have any.


        StringBuilder wrappedMembers = new();

        // getters/setters
        foreach (var (property, handling) in members.OfType<(IPropertySymbol, MemberHandling)>())
        {
            if (property.IsIndexer) throw new NotImplementedException();

            wrappedMembers.AppendLine(WrapProperty(property, handling));
        }

        foreach (var (method, handling) in members.OfType<(IMethodSymbol, MemberHandling)>())
        {
            // skip ToString() because we override that manually
            if (method.Name == "ToString" && method.IsOverride)
            {
                continue;
            }

            // TODO: skip any queries to do those manually

            if (method.MethodKind == MethodKind.Ordinary)
            {
                wrappedMembers.AppendLine(WrapMethod(method, handling));
            }
        }

        return $$"""
            using Arch.Core;
            using System.Runtime.CompilerServices;
            using System.Diagnostics.Contracts;
            partial class ScheduledWorld
            {
                public World UnsafeWorld { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

                // custom override since we don't support other overrides
                public override string ToString()
                {
                    return UnsafeWorld.ToString();
                }

                {{wrappedMembers}}
            }
            """;
    }

    private string WrapProperty(IPropertySymbol property, MemberHandling handling)
    {
        var type = property.Type.ToString();
        var name = property.Name;
        var @static = property.IsStatic ? "static" : string.Empty;
        var symbol = property.IsStatic ? "Arch.Core.World" : "UnsafeWorld";
        var refReturn = property.ReturnsByRef || property.ReturnsByRefReadonly ? "ref" : string.Empty;
        var refReadOnly = string.Empty;

        if (property.ReturnsByRef)
        {
            refReadOnly = "ref";
        }
        else if (property.ReturnsByRefReadonly)
        {
            refReadOnly = "ref readonly";
        }

        if (handling != MemberHandling.Wrap)
        {
            throw new NotImplementedException();
        }

        var get = property.GetMethod is not null ? $$"""
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return {{refReturn}} {{symbol}}.{{name}};
            }
        """ : "";

        var set = property.SetMethod is not null ? $$"""
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                {{symbol}}.{{name}} = value;
            }
        """ : "";

        return $$"""
            public {{@static}} {{refReadOnly}} {{type}} {{name}}
            {
                {{get}}
                {{set}}
            }
            """;
    }

    // only suitable for params, not return types
    private string GetRefKindParamString(RefKind kind)
    {
        return kind switch
        {
            RefKind.Ref => "ref",
            RefKind.In => "in",
            RefKind.Out => "out",
            _ => string.Empty
        };
    }

    private string WrapMethod(IMethodSymbol method, MemberHandling handling)
    {
        var type = method.ReturnType.ToString();
        var name = method.Name;

        var genericsList = method.TypeParameters.Select(s => s.ToString()).ToList();
        var generics = genericsList.Count() > 0 ? $"<{string.Join(", ", genericsList)}>" : string.Empty;
        var typedParamsList = method.Parameters.Select(p => p.ToString()).ToList();
        var typedParams = string.Join(", ", typedParamsList);
        var untypedParamsList = method.Parameters.Select(p => $"{GetRefKindParamString(p.RefKind)} {p.Name}").ToList();
        var untypedParams = string.Join(", ", untypedParamsList);
        var @static = method.IsStatic ? "static" : string.Empty;
        var symbol = method.IsStatic ? "Arch.Core.World" : "UnsafeWorld";
        var constraints = string.Join("\n", method.TypeParameters.Select(GetConstraintString));
        var refReturn = method.ReturnsByRef || method.ReturnsByRefReadonly ? "ref" : string.Empty;
        var refReadOnly = string.Empty;
        var pureAttr = method.GetAttributes().Any(a => a.AttributeClass?.Name == "PureAttribute") ? "[Pure]" : string.Empty;
        var structuralAttr = method.GetAttributes().Any(a => a.AttributeClass?.Name == "StructuralChangeAttribute") ? "[StructuralChange]" : string.Empty;

        var header = string.Empty;
        var footer = string.Empty;

        if (handling == MemberHandling.GenericQuery || handling == MemberHandling.GenericScheduledQuery)
        {
            // unconstrained generics are components in need of write checking
            List<string> unconstrainedGenericsList = new();
            foreach (var tp in method.TypeParameters)
            {
                if (tp.HasConstructorConstraint
                    || tp.HasNotNullConstraint
                    || tp.HasReferenceTypeConstraint
                    || tp.HasUnmanagedTypeConstraint
                    || tp.HasValueTypeConstraint
                    || tp.ConstraintTypes.Length != 0)
                {
                    continue;
                }
                unconstrainedGenericsList.Add(tp.Name);
            }
            string unconstrainedGenerics = string.Join(", ", unconstrainedGenericsList);

            if (handling == MemberHandling.GenericQuery)
            {
                // For a regular generic query, we just wait on the reads and query description
                // If these are run in a thread, it runs validation on the registration.
                // Otherwise, it waits.
                header = $$"""
                    SetupQuery(Arch.Core.Group<{{unconstrainedGenerics}}>.Types, queryDescription.Reads, queryDescription.Writes);
                    """;
            }
            else
            {
                // For a scheduled generic query, we do a whole process to register our thread as within a query.
                // (internal)
                header = $$"""
                    if (dependency is null)
                    {
                        dependency = GetDependency(Arch.Core.Group<{{unconstrainedGenerics}}>.Types, queryDescription.Reads, queryDescription.Writes);
                        var handle = {{refReturn}} {{symbol}}.{{name}}{{generics}}({{untypedParams}});
                    }
                    """;
                footer = $$"""
                    RegisterHandle(Arch.Core.Group<{{unconstrainedGenerics}}>.Types, queryDescription.Reads, queryDescription.Writes);
                    return handle;
                    """;
            }
        }
        else if (handling == MemberHandling.StructuralChange)
        {
            header = "Synchronize();";
        }

        if (method.ReturnsByRef)
        {
            refReadOnly = "ref";
        }
        else if (method.ReturnsByRefReadonly)
        {
            refReadOnly = "ref readonly";
        }

        if (footer == string.Empty)
        {
            footer = $"{(type != "void" ? "return" : "")} {refReturn} {symbol}.{name}{generics}({untypedParams});";
        }

        return $$"""
            /// <inheritdoc cref="Arch.Core.World.{{name}}{{generics}}({{typedParams}})"/>
            {{pureAttr}}
            {{structuralAttr}}
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public {{@static}} {{refReadOnly}} {{type}} {{name}}{{generics}}({{typedParams}}) {{constraints}}
            {
                {{header}}
                {{footer}}
            }
            """;
    }

    private string GetConstraintString(ITypeParameterSymbol t)
    {
        List<string> constraints = new();
        if (t.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        if (t.HasReferenceTypeConstraint)
        {
            constraints.Add("class");
        }
        if (t.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        if (t.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }
        if (t.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }
        foreach (var type in t.ConstraintTypes)
        {
            constraints.Add(type.ToString());
        }
        return constraints.Count > 0 ? $"where {t.Name} : {string.Join(", ", constraints)}" : string.Empty;
    }
}