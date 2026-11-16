using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Extraction;

namespace DotNetOpenApiExtract.Core.Discovery;

/// <summary>
/// How the API versions of <c>Asp.Versioning</c> shape the paths of one document: which actions it
/// describes (<see cref="OpenApiDocumentOptions.ApiVersion"/>), whether the version is substituted into
/// the URL (<c>SubstituteApiVersionInUrl</c>) and which API version route parameters stay in a path.
/// </summary>
/// <remarks>
/// With the substitution, <c>Asp.Versioning</c> describes an action once per API version, with the
/// <c>{version:apiVersion}</c> parameter replaced by the version written in the substitution format, and
/// without that parameter. A document describes one version: the one chosen with
/// <see cref="OpenApiDocumentOptions.ApiVersion"/>, else the single version the versioned actions declare.
/// When they declare several and none is chosen, or the format cannot be computed, nothing is substituted
/// and a warning says why: the parameter stays a path parameter, and the document stays valid.
/// </remarks>
internal sealed class ApiVersionRouting
{
    private readonly Dictionary<ActionInfo, Plan> _plans;

    /// <summary>How one action's path is written.</summary>
    /// <param name="Path">The OpenAPI path of the action.</param>
    /// <param name="VersionParameters">The API version route parameters that stay in <paramref name="Path"/>, with the spelling of the path.</param>
    /// <param name="Substituted">The API version route parameters replaced by the version.</param>
    private sealed record Plan(string Path, IReadOnlyList<string> VersionParameters, IReadOnlyList<string> Substituted);

    private ApiVersionRouting(IReadOnlyList<ActionInfo> actions, Dictionary<ActionInfo, Plan> plans, string? parameterDescription)
    {
        Actions = actions;
        _plans = plans;
        VersionParameterDescription = parameterDescription;
    }

    /// <summary>The actions the document describes, in discovery order.</summary>
    public IReadOnlyList<ActionInfo> Actions { get; }

    /// <summary>The description of an API version path parameter: what it is and the versions it takes.</summary>
    public string? VersionParameterDescription { get; }

    /// <summary>The OpenAPI path of <paramref name="action"/>.</summary>
    public string PathOf(ActionInfo action) => _plans[action].Path;

    /// <summary>The API version route parameters that stay in the path of <paramref name="action"/>.</summary>
    public IReadOnlyList<string> VersionParametersOf(ActionInfo action) => _plans[action].VersionParameters;

    /// <summary>The API version route parameters the version replaced in the path of <paramref name="action"/>.</summary>
    public IReadOnlyList<string> SubstitutedParametersOf(ActionInfo action) => _plans[action].Substituted;

    /// <summary>
    /// Plans the paths of <paramref name="actions"/> by <paramref name="options"/> and the API explorer
    /// options read from the source (<paramref name="source"/>).
    /// </summary>
    /// <exception cref="OpenApiConfigurationException">
    /// <see cref="OpenApiDocumentOptions.ApiVersion"/> is not a version, or no action declares it.
    /// </exception>
    public static ApiVersionRouting Create(
        IReadOnlyList<ActionInfo> actions,
        OpenApiDocumentOptions options,
        ApiExplorerOptionsExtractionResult source,
        DiagnosticBag diagnostics)
    {
        var declared = actions.ToDictionary(a => a, Declared);
        var allVersions = DistinctVersions(declared.Values.SelectMany(d => d.Versions));

        // The version the document describes.
        ApiVersionValue? selected = null;
        if (options.ApiVersion != null)
        {
            if (!ApiVersionValue.TryParse(options.ApiVersion, out var parsed))
                throw new OpenApiConfigurationException(
                    $"The API version '{options.ApiVersion}' (--api-version, OpenApiDocumentOptions.ApiVersion) is not a " +
                    "version of the form major[.minor][-status], such as 1, 1.0 or 2.0-beta.");
            selected = parsed;
            if (!allVersions.Any(v => v.Value?.SameVersion(parsed) == true))
                throw new OpenApiConfigurationException(
                    $"No operation declares the API version '{options.ApiVersion}' (--api-version, OpenApiDocumentOptions.ApiVersion). " +
                    (allVersions.Count == 0
                        ? "No operation declares [ApiVersion] or [MapToApiVersion]."
                        : $"Declared versions: {string.Join(", ", allVersions.Select(v => v.Text))}."));
        }

        var described = selected == null
            ? actions.ToList()
            : actions.Where(a => declared[a].Neutral || declared[a].Versions.Count == 0
                                 || declared[a].Versions.Any(v => v.Value?.SameVersion(selected) == true)).ToList();

        var routes = described.ToDictionary(a => a, a => RouteBuilder.ApiVersionParameterNames(
            a.Controller.RouteTemplate, a.RouteTemplate, a.Controller.Type.Name, a.Name));
        var routed = described.Where(a => routes[a].Count > 0).ToList();

        // The versions the routes that carry a version parameter take.
        var routedVersions = DistinctVersions(routed.SelectMany(a => declared[a].Versions));
        var substitute = options.SubstituteApiVersionInUrl ?? source.SubstituteApiVersionInUrl ?? false;
        var format = options.ApiVersionSubstitutionFormat ?? source.SubstitutionFormat ?? ApiVersionFormat.DefaultSubstitutionFormat;

        string? value = null;
        if (substitute && routed.Count > 0)
            value = SubstitutedValue(selected, routedVersions, format, diagnostics);

        var plans = new Dictionary<ActionInfo, Plan>();
        foreach (var action in described)
        {
            var path = RouteBuilder.BuildPath(action.Controller.RouteTemplate, action.RouteTemplate, action.Controller.Type.Name, action.Name);
            var names = routes[action];
            if (names.Count == 0)
            {
                plans[action] = new Plan(path, [], []);
                continue;
            }

            var versioned = !declared[action].Neutral && declared[action].Versions.Count > 0;
            if (substitute && !versioned && !declared[action].Neutral)
                RecordUnversioned(action, path, diagnostics);

            if (value != null && versioned)
            {
                foreach (var name in names)
                    path = path.Replace("{" + name + "}", value, StringComparison.OrdinalIgnoreCase);
                plans[action] = new Plan(path, [], names);
            }
            else
            {
                plans[action] = new Plan(path, names.Select(n => SpellingIn(path, n)).Distinct(StringComparer.Ordinal).ToList(), []);
            }
        }

        var takes = selected != null
            ? routedVersions.Where(v => v.Value?.SameVersion(selected) == true).Select(v => v.Text).ToList()
            : routedVersions.Select(v => v.Text).ToList();
        var description = takes.Count == 0
            ? "The requested API version."
            : $"The requested API version: {string.Join(", ", takes)}.";

        return new ApiVersionRouting(described, plans, description);
    }

    /// <summary>
    /// The text that replaces the version parameter, or <see langword="null"/> when nothing is
    /// substituted (reported).
    /// </summary>
    private static string? SubstitutedValue(
        ApiVersionValue? selected,
        IReadOnlyList<(string Text, ApiVersionValue? Value)> routedVersions,
        string format,
        DiagnosticBag diagnostics)
    {
        (string Text, ApiVersionValue? Value)? version = null;
        if (selected != null)
        {
            // When no route with a version parameter declares the chosen version, only version-neutral or
            // unversioned routes carry the parameter: nothing is substituted.
            var chosen = routedVersions.Where(v => v.Value?.SameVersion(selected) == true).ToList();
            if (chosen.Count == 0)
                return null;
            version = chosen[0];
        }
        else if (routedVersions.Count == 1)
        {
            version = routedVersions[0];
        }
        else if (routedVersions.Count > 1)
        {
            var listed = routedVersions.Select(v => v.Text).ToList();
            diagnostics.Report(new ExtractionDiagnostic
            {
                Code     = ExtractionDiagnosticCodes.ApiVersionSeveralVersions,
                Message  = "the API version is substituted into the URL (SubstituteApiVersionInUrl), but the operations declare " +
                           $"several API versions ({string.Join(", ", listed)}): a document describes one of them. The version " +
                           "stays a path parameter; choose the version with --api-version (OpenApiDocumentOptions.ApiVersion).",
                Feature  = "paths",
                Subjects = listed,
            });
            return null;
        }

        if (version == null)
            return null;

        var formatted = "";
        if (version.Value.Value is not { } computable || !ApiVersionFormat.TryFormat(computable, format, out formatted))
        {
            diagnostics.Report(new ExtractionDiagnostic
            {
                Code     = ExtractionDiagnosticCodes.ApiVersionSubstitutionNotComputed,
                Message  = $"the API version {version.Value.Text} cannot be written in the substitution format '{format}': the tool " +
                           "computes the specifiers V, VV, VVV, VVVV, v, S, P, PP, PPP, PPPP, p (with padding digits) and " +
                           "quoted literals, not the group version ones (F, G, M, d, y). The version stays a path parameter; " +
                           "set a format with --api-version-format (OpenApiDocumentOptions.ApiVersionSubstitutionFormat).",
                Feature  = "paths",
                Subjects = [format, version.Value.Text],
            });
            return null;
        }

        return formatted;
    }

    private static void RecordUnversioned(ActionInfo action, string path, DiagnosticBag diagnostics)
    {
        var where = $"{action.HttpMethod.ToUpperInvariant()} {path}";
        diagnostics.Report(new ExtractionDiagnostic
        {
            Code     = ExtractionDiagnosticCodes.ApiVersionOperationUnversioned,
            Message  = $"{where} ({OperationConflicts.DisplayName(action)}) has an API version route parameter but declares no " +
                       "[ApiVersion] or [MapToApiVersion]: its version is the application's default API version, which the " +
                       "tool does not read. The version stays a path parameter; declare [ApiVersion] on the controller.",
            Location = where,
            Feature  = "paths",
            Subjects = [OperationConflicts.DisplayName(action)],
        });
    }

    /// <summary>The parameter <paramref name="name"/> as the path spells it (static segments are lowercased, a parameter inside one too).</summary>
    private static string SpellingIn(string path, string name)
    {
        var token = "{" + name + "}";
        var index = path.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? name : path.Substring(index + 1, name.Length);
    }

    /// <summary>The versioning an action declares: neutral, or its versions with their parsed values.</summary>
    private sealed record Declaration(bool Neutral, IReadOnlyList<(string Text, ApiVersionValue? Value)> Versions);

    private static Declaration Declared(ActionInfo action)
    {
        var actionAttrs = action.Method.GetCustomAttributesData();
        var controllerAttrs = action.Controller.Type.GetCustomAttributesData();
        if (ApiVersionExtractor.IsVersionNeutral(actionAttrs, controllerAttrs))
            return new Declaration(true, []);

        // A version the tool cannot compute with (a group, date, version) has no value: it equals no
        // other version, so it is never chosen, and it is not substituted.
        var versions = new List<(string, ApiVersionValue?)>();
        foreach (var text in ApiVersionExtractor.GetSupportedVersions(actionAttrs, controllerAttrs))
            versions.Add((text, ApiVersionValue.TryParse(text, out var value) ? value : null));

        return new Declaration(false, versions);
    }

    /// <summary>The versions, one per API version (<c>1</c> and <c>1.0</c> are one), in the order of their text.</summary>
    private static IReadOnlyList<(string Text, ApiVersionValue? Value)> DistinctVersions(
        IEnumerable<(string Text, ApiVersionValue? Value)> versions)
    {
        var distinct = new List<(string Text, ApiVersionValue? Value)>();
        foreach (var version in versions.OrderBy(v => v.Text, StringComparer.Ordinal))
        {
            if (version.Value is not { } value
                    ? !distinct.Any(d => d.Text == version.Text)
                    : !distinct.Any(d => d.Value?.SameVersion(value) == true))
                distinct.Add(version);
        }

        return distinct;
    }
}
