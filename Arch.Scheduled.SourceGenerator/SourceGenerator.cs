using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Arch.System.SourceGenerator;

// using https://stackoverflow.com/questions/68055210/generate-source-based-on-other-assembly-classes-c-source-generator
// dunno how to make incremental :(
[Generator]
public class SourceGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (!Debugger.IsAttached)
        {
           // Debugger.Launch();
        }
        var archSymbol = context.Compilation.SourceModule.ReferencedAssemblySymbols.First(q => q.Name == "Arch");

        var world = archSymbol.GlobalNamespace
            .GetNamespaceMembers().First(q => q.Name == "Arch")
            .GetNamespaceMembers().First(q => q.Name == "Core")
            .GetTypeMembers().First(q => q.Name == "World");

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
            if (method.MethodKind == MethodKind.Ordinary)
            {
                wrappedMembers.AppendLine(WrapMethod(method));
            }
        }

        var wrapper = $$"""
            using Arch.Core;
            partial class ScheduledWorld
            {
                public World UnsafeWorld { get; }

                // TODO: use ScheduledWorld.Create();
                public ScheduledWorld()
                {
                    UnsafeWorld = World.Create();
                }

                {{wrappedMembers}}
            }
            """;

        context.AddSource("ScheduledWorld.Wrapper.g.cs", CSharpSyntaxTree.ParseText(wrapper).GetRoot().NormalizeWhitespace().ToFullString());
    }

    private string WrapProperty(IPropertySymbol property)
    {
        var type = property.Type.ToString();
        var name = property.Name;
        var @static = property.IsStatic ? "static" : string.Empty;
        var symbol = property.IsStatic ? "Arch.Core.World" : "UnsafeWorld";

        var get = property.GetMethod is not null ? $$"""
            get
            {
                return {{symbol}}.{{name}};
            }
        """ : "";

        var set = property.SetMethod is not null ? $$"""
            set
            {
                {{symbol}}.{{name}} = value;
            }
        """ : "";

        return $$"""
            public {{@static}} {{type}} {{name}}
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

        return $$"""
            public {{@static}} {{type}} {{name}}{{generics}}({{typedParams}}) {{constraints}}
            {
                {{(type != "void" ? "return" : "")}}
                {{symbol}}.{{name}}{{generics}}({{untypedParams}});
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

    public void Initialize(GeneratorInitializationContext context)
    {
    }
}