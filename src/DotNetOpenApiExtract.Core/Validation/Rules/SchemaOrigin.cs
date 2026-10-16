namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>Where the CLR type of a component schema is declared: in the project or outside it.</summary>
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
        if (context.ProjectAssemblyNames is not { } project) return null;
        if (context.TypeBySchemaId?.TryGetValue(schemaId, out var type) != true) return null;
        var name = type!.Assembly.GetName().Name;
        return name != null && !project.Contains(name) ? name : null;
    }

    /// <summary>What to do about a missing description of a type from <paramref name="assembly"/>.</summary>
    public static string Hint(string assembly) =>
        $"The type comes from '{assembly}', outside the project, so its description can only come from that assembly's XML documentation. " +
        "If the package ships one, set <CopyDocumentationFilesFromPackages>true</CopyDocumentationFilesFromPackages> in the project file " +
        "so the build copies it next to the assembly, or pass it with --xml.";
}
