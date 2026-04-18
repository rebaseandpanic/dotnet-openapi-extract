using System.Reflection;

namespace DotNetOpenApiExtract.Core.Loading;

/// <summary>
/// Helper methods for reading attribute data from MetadataLoadContext types.
/// In MetadataLoadContext, we cannot use GetCustomAttributes() (which instantiates).
/// We must use GetCustomAttributesData() and compare by FullName.
/// </summary>
public static class AttributeHelper
{
    /// <summary>
    /// Check if a member has an attribute by its full type name.
    /// </summary>
    public static bool HasAttribute(MemberInfo member, string attributeFullName)
    {
        return member.GetCustomAttributesData()
            .Any(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Check if a member has an attribute by its full type name, using a pre-fetched attribute list.
    /// </summary>
    public static bool HasAttribute(IList<CustomAttributeData> attrData, string attributeFullName)
    {
        foreach (var a in attrData)
            if (a.AttributeType.FullName == attributeFullName) return true;
        return false;
    }

    /// <summary>
    /// Get the first attribute data matching the full type name, or null.
    /// </summary>
    public static CustomAttributeData? GetAttribute(MemberInfo member, string attributeFullName)
    {
        return member.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Get the first attribute data matching the full type name, using a pre-fetched attribute list.
    /// </summary>
    public static CustomAttributeData? GetAttribute(IList<CustomAttributeData> attrData, string attributeFullName)
    {
        foreach (var a in attrData)
            if (a.AttributeType.FullName == attributeFullName) return a;
        return null;
    }

    /// <summary>
    /// Get all attribute data matching the full type name.
    /// </summary>
    public static IEnumerable<CustomAttributeData> GetAttributes(MemberInfo member, string attributeFullName)
    {
        return member.GetCustomAttributesData()
            .Where(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Get all attribute data matching the full type name, using a pre-fetched attribute list.
    /// </summary>
    public static IEnumerable<CustomAttributeData> GetAttributes(
        IList<CustomAttributeData> attrData,
        string attributeFullName)
    {
        foreach (var a in attrData)
            if (a.AttributeType.FullName == attributeFullName)
                yield return a;
    }

    /// <summary>
    /// Get the first attribute data matching any of the given full type names.
    /// </summary>
    public static CustomAttributeData? GetAttribute(MemberInfo member, params string[] attributeFullNames)
    {
        var attrs = member.GetCustomAttributesData();
        foreach (var a in attrs)
        {
            if (a.AttributeType.FullName == null) continue;
            foreach (var name in attributeFullNames)
                if (a.AttributeType.FullName == name) return a;
        }
        return null;
    }

    /// <summary>
    /// Check if a member has any attribute with name starting with the given prefix.
    /// </summary>
    internal static bool HasAttributeStartingWith(MemberInfo member, string prefix)
    {
        return member.GetCustomAttributesData()
            .Any(a => a.AttributeType.FullName?.StartsWith(prefix, StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Get a constructor argument value by index, or default.
    /// </summary>
    public static T? GetConstructorArgument<T>(CustomAttributeData attribute, int index)
    {
        if (index >= attribute.ConstructorArguments.Count)
            return default;

        var arg = attribute.ConstructorArguments[index];
        if (arg.Value is T value)
            return value;

        return default;
    }

    /// <summary>
    /// Get a named argument value, or default.
    /// </summary>
    public static T? GetNamedArgument<T>(CustomAttributeData attribute, string name)
    {
        var arg = attribute.NamedArguments
            .FirstOrDefault(a => a.MemberName == name);

        if (arg.TypedValue.Value is T value)
            return value;

        return default;
    }

    /// <summary>
    /// Full type names of attributes used for OpenAPI extraction.
    /// Values correspond to the attribute types as shipped in their respective NuGet packages.
    /// </summary>
    public static class Names
    {
        // ASP.NET Core MVC
        public const string ApiController = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
        public const string NonController = "Microsoft.AspNetCore.Mvc.NonControllerAttribute";
        public const string NonAction = "Microsoft.AspNetCore.Mvc.NonActionAttribute";
        public const string Route = "Microsoft.AspNetCore.Mvc.RouteAttribute";
        public const string HttpGet = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
        public const string HttpPost = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
        public const string HttpPut = "Microsoft.AspNetCore.Mvc.HttpPutAttribute";
        public const string HttpDelete = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";
        public const string HttpPatch = "Microsoft.AspNetCore.Mvc.HttpPatchAttribute";
        public const string HttpHead = "Microsoft.AspNetCore.Mvc.HttpHeadAttribute";
        public const string HttpOptions = "Microsoft.AspNetCore.Mvc.HttpOptionsAttribute";
        public const string ActionName = "Microsoft.AspNetCore.Mvc.ActionNameAttribute";
        public const string ExcludeFromDescription = "Microsoft.AspNetCore.Http.ExcludeFromDescriptionAttribute";
        public const string FromRoute = "Microsoft.AspNetCore.Mvc.FromRouteAttribute";
        public const string FromQuery = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";
        public const string FromBody = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
        public const string FromHeader = "Microsoft.AspNetCore.Mvc.FromHeaderAttribute";
        public const string FromForm = "Microsoft.AspNetCore.Mvc.FromFormAttribute";
        public const string FromServices = "Microsoft.AspNetCore.Mvc.FromServicesAttribute";
        public const string ProducesResponseType = "Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute";
        public const string Produces = "Microsoft.AspNetCore.Mvc.ProducesAttribute";
        public const string Consumes = "Microsoft.AspNetCore.Mvc.ConsumesAttribute";
        public const string ProducesDefaultResponseType = "Microsoft.AspNetCore.Mvc.ProducesDefaultResponseTypeAttribute";
        public const string ApiExplorerSettings = "Microsoft.AspNetCore.Mvc.ApiExplorerSettingsAttribute";

        // Swashbuckle Annotations
        public const string SwaggerOperation = "Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute";
        public const string SwaggerParameter = "Swashbuckle.AspNetCore.Annotations.SwaggerParameterAttribute";
        public const string SwaggerResponse = "Swashbuckle.AspNetCore.Annotations.SwaggerResponseAttribute";
        public const string SwaggerTag = "Swashbuckle.AspNetCore.Annotations.SwaggerTagAttribute";
        public const string SwaggerSchema = "Swashbuckle.AspNetCore.Annotations.SwaggerSchemaAttribute";
        public const string SwaggerRequestBody = "Swashbuckle.AspNetCore.Annotations.SwaggerRequestBodyAttribute";

        // DataAnnotations
        public const string Required = "System.ComponentModel.DataAnnotations.RequiredAttribute";
        public const string StringLength = "System.ComponentModel.DataAnnotations.StringLengthAttribute";
        public const string MaxLength = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
        public const string MinLength = "System.ComponentModel.DataAnnotations.MinLengthAttribute";
        public const string Range = "System.ComponentModel.DataAnnotations.RangeAttribute";
        public const string RegularExpression = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute";

        // JSON
        public const string JsonPropertyName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
        public const string JsonIgnore = "System.Text.Json.Serialization.JsonIgnoreAttribute";
        public const string JsonRequired = "System.Text.Json.Serialization.JsonRequiredAttribute";

        // System
        public const string Obsolete = "System.ObsoleteAttribute";

        // Model binding
        public const string BindNever = "Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute";

        // System.ComponentModel
        public const string Description = "System.ComponentModel.DescriptionAttribute";
        public const string DefaultValue = "System.ComponentModel.DefaultValueAttribute";

        // ASP.NET Core HTTP — .NET 9+ endpoint metadata attributes
        public const string EndpointSummary = "Microsoft.AspNetCore.Http.EndpointSummaryAttribute";
        public const string EndpointDescription = "Microsoft.AspNetCore.Http.EndpointDescriptionAttribute";
        public const string Tags = "Microsoft.AspNetCore.Http.TagsAttribute";

        // System.ComponentModel.DataAnnotations — format-related
        public const string EmailAddress = "System.ComponentModel.DataAnnotations.EmailAddressAttribute";
        public const string Url = "System.ComponentModel.DataAnnotations.UrlAttribute";
        public const string Phone = "System.ComponentModel.DataAnnotations.PhoneAttribute";

        // System.Text.Json.Serialization — advanced
        public const string JsonUnmappedMemberHandling = "System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute";
        public const string JsonConverter = "System.Text.Json.Serialization.JsonConverterAttribute";

        // Asp.Versioning (current namespace)
        public const string ApiVersion = "Asp.Versioning.ApiVersionAttribute";
        public const string MapToApiVersion = "Asp.Versioning.MapToApiVersionAttribute";
        public const string ApiVersionNeutral = "Asp.Versioning.ApiVersionNeutralAttribute";

        // Asp.Versioning (legacy Microsoft.AspNetCore.Mvc.Versioning namespace)
        public const string ApiVersionLegacy = "Microsoft.AspNetCore.Mvc.ApiVersionAttribute";
        public const string MapToApiVersionLegacy = "Microsoft.AspNetCore.Mvc.MapToApiVersionAttribute";
        public const string ApiVersionNeutralLegacy = "Microsoft.AspNetCore.Mvc.ApiVersionNeutralAttribute";

        // ASP.NET Core Authorization
        public const string Authorize = "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";
        public const string AllowAnonymous = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";

        // ASP.NET Core Rate Limiting
        public const string EnableRateLimiting = "Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute";
        public const string DisableRateLimiting = "Microsoft.AspNetCore.RateLimiting.DisableRateLimitingAttribute";

        // ASP.NET Core Response Caching
        public const string ResponseCache = "Microsoft.AspNetCore.Mvc.ResponseCacheAttribute";
        public const string OutputCache = "Microsoft.AspNetCore.OutputCaching.OutputCacheAttribute";
    }

    /// <summary>
    /// Check if a parameter has an attribute by its full type name.
    /// </summary>
    public static bool HasAttribute(ParameterInfo parameter, string attributeFullName)
    {
        return parameter.GetCustomAttributesData()
            .Any(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Get the first attribute data for a parameter matching the full type name.
    /// </summary>
    public static CustomAttributeData? GetAttribute(ParameterInfo parameter, string attributeFullName)
    {
        return parameter.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == attributeFullName);
    }

    /// <summary>
    /// Get all attribute data for a parameter matching the full type name.
    /// </summary>
    public static IEnumerable<CustomAttributeData> GetAttributes(ParameterInfo parameter, string attributeFullName)
    {
        return parameter.GetCustomAttributesData()
            .Where(a => a.AttributeType.FullName == attributeFullName);
    }
}
