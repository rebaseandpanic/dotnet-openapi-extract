namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>Where the CLR type of a component schema, or of one of its properties, is declared.</summary>
internal static class SchemaOrigin
{
    /// <summary>
    /// The simple name of the assembly that declares the CLR type of <paramref name="schemaId"/> when it
    /// is not one of <see cref="ValidationContext.ProjectAssemblyNames"/> (a NuGet package, the shared
    /// framework); <see langword="null"/> for a project type, a schema without a CLR type, or when the
    /// project assemblies are unknown.
    /// </summary>
    public static string? ExternalAssembly(ValidationContext context, string schemaId)
    {
        if (context.ProjectAssemblyNames is null) return null;
        if (context.TypeBySchemaId?.TryGetValue(schemaId, out var type) != true) return null;
        return External(context, type!);
    }

    /// <summary>
    /// The assembly that declares the property written as <paramref name="propertyName"/> of
    /// <paramref name="schemaId"/> when it is outside the project: the declaring type of the CLR
    /// property, so a property a project type inherits from a package type (<c>class MyProblem :
    /// ProblemDetails</c>) is external, and one it declares itself is not; the component's type when
    /// the property has no CLR property.
    /// </summary>
    public static string? ExternalAssembly(ValidationContext context, string schemaId, string propertyName)
    {
        if (context.ProjectAssemblyNames is null) return null;
        if (context.TypeBySchemaId?.TryGetValue(schemaId, out var type) != true) return null;
        var declaring = ClrProperties.Find(type!, propertyName)?.DeclaringType ?? type!;
        return External(context, declaring);
    }

    private static string? External(ValidationContext context, Type type)
    {
        var name = type.Assembly.GetName().Name;
        return name != null && !context.ProjectAssemblyNames!.Contains(name) ? name : null;
    }

    /// <summary>What to do about a missing description of a type from <paramref name="assembly"/>.</summary>
    public static string Hint(ValidationContext context, string assembly) =>
        context.PackageAssemblyNames?.Contains(assembly) == true
            ? $"The type comes from the NuGet package assembly '{assembly}', so its description can only come from the package's XML documentation. " +
              "If the package ships one, set <CopyDocumentationFilesFromPackages>true</CopyDocumentationFilesFromPackages> in the project file " +
              "so the build copies it next to the assembly, or pass it with --xml."
            : $"The type comes from '{assembly}', outside the project, so its description can only come from that assembly's XML documentation: " +
              "pass it with --xml.";
}
