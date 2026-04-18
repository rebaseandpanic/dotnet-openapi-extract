using System.Globalization;
using System.Reflection;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Extracts API versioning information from <c>Asp.Versioning</c> attributes
/// (<c>[ApiVersion]</c>, <c>[MapToApiVersion]</c>, <c>[ApiVersionNeutral]</c>)
/// applied to controllers and actions.
/// <para>
/// Both current namespace (<c>Asp.Versioning.*</c>) and legacy namespace
/// (<c>Microsoft.AspNetCore.Mvc.*</c>) are supported.
/// </para>
/// <para>
/// Attributes are read via <see cref="AttributeHelper"/> from <c>MetadataLoadContext</c>
/// metadata — no code from the target assembly is ever executed.
/// </para>
/// </summary>
public static class ApiVersionExtractor
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> if either the controller or the action carries
    /// <c>[ApiVersionNeutral]</c> (current or legacy namespace).
    /// </summary>
    /// <param name="controller">The owning controller.</param>
    /// <param name="action">The action to check.</param>
    public static bool IsVersionNeutral(ControllerInfo controller, ActionInfo action)
    {
        return IsVersionNeutral(
            action.Method.GetCustomAttributesData(),
            controller.Type.GetCustomAttributesData());
    }

    /// <summary>
    /// Returns <see langword="true"/> if either the pre-fetched action or controller attribute list
    /// carries <c>[ApiVersionNeutral]</c> (current or legacy namespace).
    /// </summary>
    /// <param name="actionAttrs">Pre-fetched attribute data for the action method.</param>
    /// <param name="controllerAttrs">Pre-fetched attribute data for the controller type.</param>
    public static bool IsVersionNeutral(
        IList<CustomAttributeData> actionAttrs,
        IList<CustomAttributeData> controllerAttrs)
    {
        return HasNeutral(actionAttrs) || HasNeutral(controllerAttrs);
    }

    /// <summary>
    /// Returns the list of API version strings that apply to the given action.
    /// </summary>
    /// <remarks>
    /// Resolution rules (in priority order):
    /// <list type="number">
    ///   <item>If <c>[ApiVersionNeutral]</c> is present (controller or action) → returns an empty list.
    ///         Callers should use <see cref="IsVersionNeutral"/> separately to emit the neutral marker.</item>
    ///   <item>If the action has <c>[MapToApiVersion]</c> → returns only that version (action-specific mapping
    ///         overrides all <c>[ApiVersion]</c> attributes).</item>
    ///   <item>Otherwise → union of all <c>[ApiVersion]</c> values from the controller and the action,
    ///         deduplicated and ordered.</item>
    /// </list>
    /// If <c>[ApiVersionNeutral]</c> is present alongside <c>[ApiVersion]</c>, neutral wins.
    /// </remarks>
    /// <param name="controller">The owning controller.</param>
    /// <param name="action">The action to query.</param>
    /// <returns>
    /// A read-only list of version strings (e.g. <c>["1.0", "2.0"]</c>).
    /// Empty when no versioning attributes exist OR when neutral is set.
    /// </returns>
    public static IReadOnlyList<string> GetSupportedVersions(ControllerInfo controller, ActionInfo action)
    {
        return GetSupportedVersions(
            action.Method.GetCustomAttributesData(),
            controller.Type.GetCustomAttributesData());
    }

    /// <summary>
    /// Returns the list of API version strings that apply to the given action,
    /// using pre-fetched attribute lists.
    /// </summary>
    /// <param name="actionAttrs">Pre-fetched attribute data for the action method.</param>
    /// <param name="controllerAttrs">Pre-fetched attribute data for the controller type.</param>
    /// <returns>
    /// A read-only list of version strings (e.g. <c>["1.0", "2.0"]</c>).
    /// Empty when no versioning attributes exist OR when neutral is set.
    /// </returns>
    public static IReadOnlyList<string> GetSupportedVersions(
        IList<CustomAttributeData> actionAttrs,
        IList<CustomAttributeData> controllerAttrs)
    {
        // Neutral → no version list (treated separately by callers).
        if (IsVersionNeutral(actionAttrs, controllerAttrs))
            return Array.Empty<string>();

        // [MapToApiVersion] on the action overrides controller-level [ApiVersion].
        var mapToAttr = GetMapToApiVersionAttr(actionAttrs);
        if (mapToAttr != null)
        {
            var version = ParseVersionFromAttr(mapToAttr);
            return version != null ? new[] { version } : Array.Empty<string>();
        }

        // Collect from controller + action, deduplicate, sort.
        var versions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attr in GetApiVersionAttrs(controllerAttrs))
        {
            var v = ParseVersionFromAttr(attr);
            if (v != null) versions.Add(v);
        }

        foreach (var attr in GetApiVersionAttrs(actionAttrs))
        {
            var v = ParseVersionFromAttr(attr);
            if (v != null) versions.Add(v);
        }

        return versions.OrderBy(v => v, StringComparer.Ordinal).ToArray();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool HasNeutral(IList<CustomAttributeData> attrs)
    {
        return AttributeHelper.HasAttribute(attrs, AttributeHelper.Names.ApiVersionNeutral)
            || AttributeHelper.HasAttribute(attrs, AttributeHelper.Names.ApiVersionNeutralLegacy);
    }

    private static IEnumerable<CustomAttributeData> GetApiVersionAttrs(IList<CustomAttributeData> attrs)
    {
        foreach (var a in attrs)
        {
            var fn = a.AttributeType.FullName;
            if (fn == AttributeHelper.Names.ApiVersion || fn == AttributeHelper.Names.ApiVersionLegacy)
                yield return a;
        }
    }

    private static CustomAttributeData? GetMapToApiVersionAttr(IList<CustomAttributeData> attrs)
    {
        foreach (var a in attrs)
        {
            var fn = a.AttributeType.FullName;
            if (fn == AttributeHelper.Names.MapToApiVersion || fn == AttributeHelper.Names.MapToApiVersionLegacy)
                return a;
        }
        return null;
    }

    /// <summary>
    /// Extracts a version string from an <c>[ApiVersion(...)]</c> or <c>[MapToApiVersion(...)]</c>
    /// attribute data. Returns <see langword="null"/> if the constructor signature is not recognised.
    /// </summary>
    /// <remarks>
    /// Supported constructor overloads:
    /// <list type="bullet">
    ///   <item><c>(string version)</c> — used directly.</item>
    ///   <item><c>(double version)</c> — converted via <see cref="CultureInfo.InvariantCulture"/>.</item>
    ///   <item><c>(double version, string status)</c> — formatted as <c>"{version}-{status}"</c>.</item>
    ///   <item><c>(int major, int minor)</c> — formatted as <c>"{major}.{minor}"</c>.</item>
    ///   <item><c>(int major, int minor, string status)</c> — formatted as <c>"{major}.{minor}-{status}"</c>.</item>
    ///   <item><c>(int year, int month, int day, string? status)</c> — formatted as <c>"{year}-{month:D2}-{day:D2}"</c>
    ///         (date-based versioning), with optional status appended.</item>
    /// </list>
    /// Any other signature is silently skipped (returns null).
    /// </remarks>
    private static string? ParseVersionFromAttr(CustomAttributeData attr)
    {
        if (attr.ConstructorArguments.Count == 0)
            return null;

        var args = attr.ConstructorArguments;
        var first = args[0];

        // (string version)
        if (first.ArgumentType.FullName == "System.String")
            return first.Value as string;

        // (double version, string status) — status-suffix prerelease overload
        if (args.Count == 2
            && first.ArgumentType.FullName == "System.Double"
            && args[1].ArgumentType.FullName == "System.String")
        {
            var d = (double?)first.Value;
            if (!d.HasValue) return null;
            var formatted = d.Value.ToString("0.0###", CultureInfo.InvariantCulture);
            var status = args[1].Value as string;
            return string.IsNullOrEmpty(status) ? formatted : $"{formatted}-{status}";
        }

        // (double version)
        if (first.ArgumentType.FullName == "System.Double")
        {
            var d = (double?)first.Value;
            return d.HasValue ? d.Value.ToString("0.0###", CultureInfo.InvariantCulture) : null;
        }

        // (int ...) — (major, minor), (major, minor, string status), or (year, month, day[, status])
        if (first.ArgumentType.FullName == "System.Int32" && args.Count >= 2)
        {
            var a0 = (int?)args[0].Value;
            var a1 = (int?)args[1].Value;
            if (!a0.HasValue || !a1.HasValue) return null;

            // (int major, int minor, string status) — status-suffix prerelease overload
            if (args.Count == 3 && args[2].ArgumentType.FullName == "System.String")
            {
                var status = args[2].Value as string;
                return string.IsNullOrEmpty(status)
                    ? FormattableString.Invariant($"{a0.Value}.{a1.Value}")
                    : FormattableString.Invariant($"{a0.Value}.{a1.Value}-{status}");
            }

            if (args.Count == 2)
            {
                // (int major, int minor)
                return FormattableString.Invariant($"{a0.Value}.{a1.Value}");
            }

            if (args.Count >= 3 && args[2].ArgumentType.FullName == "System.Int32")
            {
                // (int year, int month, int day[, string? status])
                var a2 = (int?)args[2].Value;
                if (!a2.HasValue) return null;
                var dateStr = FormattableString.Invariant($"{a0.Value}-{a1.Value:D2}-{a2.Value:D2}");
                if (args.Count >= 4 && args[3].ArgumentType.FullName == "System.String"
                    && args[3].Value is string status && !string.IsNullOrEmpty(status))
                    dateStr += "-" + status;
                return dateStr;
            }
        }

        return null;
    }
}
