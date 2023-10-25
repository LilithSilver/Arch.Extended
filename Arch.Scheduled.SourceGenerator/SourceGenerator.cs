using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;

namespace Arch.System.SourceGenerator;

[Generator]
public class SourceGenerator : IIncrementalGenerator
{    public void Initialize(IncrementalGeneratorInitializationContext context)
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
                    .GetTypeMembers().First(q => q.Name == "World");
                return world;
            });

        context.RegisterSourceOutput(world, (ctx, world) =>
        {
            var wrapper = GenerateWorldWrapper(world);

            ctx.AddSource("ScheduledWorld.Wrapper.g.cs", CSharpSyntaxTree.ParseText(wrapper).GetRoot().NormalizeWhitespace().ToFullString());
        });
    }

    private string GenerateWorldWrapper(INamedTypeSymbol world)
    {
        var members = world.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public);


        // This does ***NOT*** (yet) support: 
        // - readonly (structs)
        // - abstract
        // - virtual
        // - override
        // This is OK because World doesn't have any.


        StringBuilder wrappedMembers = new();

        // getters/setters
        foreach (var property in members.OfType<IPropertySymbol>())
        {
            if (property.IsIndexer) throw new NotImplementedException();

            wrappedMembers.AppendLine(WrapProperty(property));
        }

        foreach (var method in members.OfType<IMethodSymbol>())
        {
            // skip ToString() because we override that manually
            if (method.Name == "ToString" && method.IsOverride)
            {
                continue;
            }

            // TODO: skip any queries to do those manually

            if (method.MethodKind == MethodKind.Ordinary)
            {
                wrappedMembers.AppendLine(WrapMethod(method));
            }
        }

        return $$"""
            using Arch.Core;
            partial class ScheduledWorld
            {
                public World UnsafeWorld { get; }

                // TODO: use ScheduledWorld.Create();
                public ScheduledWorld()
                {
                    UnsafeWorld = World.Create();
                }

                // custom override since we don't support other overrides
                public override string ToString()
                {
                    return UnsafeWorld.ToString();
                }

                {{wrappedMembers}}
            }
            """;
    }

    private string WrapProperty(IPropertySymbol property)
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


        var get = property.GetMethod is not null ? $$"""
            get
            {
                return {{refReturn}} {{symbol}}.{{name}};
            }
        """ : "";

        var set = property.SetMethod is not null ? $$"""
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

    private string WrapMethod(IMethodSymbol method)
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

        if (method.ReturnsByRef)
        {
            refReadOnly = "ref";
        }
        else if (method.ReturnsByRefReadonly)
        {
            refReadOnly = "ref readonly";
        }

        return $$"""
            public {{@static}} {{refReadOnly}} {{type}} {{name}}{{generics}}({{typedParams}}) {{constraints}}
            {
                {{(type != "void" ? "return" : "")}} {{refReturn}} {{symbol}}.{{name}}{{generics}}({{untypedParams}});
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