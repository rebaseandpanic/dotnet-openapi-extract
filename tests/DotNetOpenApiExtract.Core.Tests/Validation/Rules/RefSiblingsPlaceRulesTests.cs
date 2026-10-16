using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;
using ValidationContext = DotNetOpenApiExtract.Core.Validation.ValidationContext;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Which rules apply to which form of a place that references a component: an inline schema, the
/// 3.0 <c>allOf: [$ref]</c> wrapper, a <c>$ref</c> with siblings (3.1/3.2) and a bare <c>$ref</c>.
/// Each case is validated twice — the built model, and the same document written to JSON or YAML and
/// read back the way standalone <c>validate</c> reads a file — and one assertion compares both sets of
/// violation pointers with the expected literal.
/// </summary>
/// <remarks>
/// The referenced component <c>Target</c> carries values that would satisfy or break the rule if the
/// rule read it (a description, <c>maxLength</c>, <c>format</c>, <c>enum</c>, a type); a place gives
/// the same result whatever the component holds, and a violation of the component itself has the
/// component's pointer.
/// </remarks>
public sealed class RefSiblingsPlaceRulesTests
{
    private const string Place = "#/components/schemas/Holder/properties/place";
    private const string Component = "#/components/schemas/Target";
    private const string QueryParameter = "#/paths/~1items/get/parameters/place";

    private static readonly OpenApiSpecVersion V30 = OpenApiSpecVersion.OpenApi3_0;
    private static readonly OpenApiSpecVersion V31 = OpenApiSpecVersion.OpenApi3_1;
    private static readonly OpenApiSpecVersion V32 = OpenApiSpecVersion.OpenApi3_2;
    private static readonly OpenApiSpecVersion[] AllVersions = [V30, V31, V32];
    private static readonly OpenApiSpecVersion[] SiblingVersions = [V31, V32];

    /// <summary>The pointers of an array without items in a property, a parameter, a body and a response.</summary>
    private static readonly string[] InlineArrayPointers =
    [
        Place,
        "#/paths/~1items/post/parameters/0/schema",
        "#/paths/~1items/post/requestBody/content/application~1json/schema",
        "#/paths/~1items/post/responses/200/content/application~1json/schema",
    ];

    /// <summary>A component the property places of the CLR cases reference.</summary>
    private sealed class PlaceTarget;

    private sealed class ConstrainedHolder
    {
        [MaxLength(5)] public PlaceTarget? Place { get; set; }
    }

    private sealed class RequiredHolder
    {
        [Required] public PlaceTarget? Place { get; set; }
    }

    private sealed class FormattedHolder
    {
        [DataType(DataType.Date)] public PlaceTarget? Place { get; set; }
    }

    /// <summary>
    /// One validation case: the document is built by <paramref name="Document"/> (once for the model,
    /// once for the file); the rule's pointers are expected to be <paramref name="Model"/> on the model
    /// and <paramref name="File"/> on the file read back.
    /// </summary>
    /// <param name="WrittenAs">The version the file is written in, when it differs from the version it declares.</param>
    private sealed record Case(
        string Rule,
        OpenApiSpecVersion Version,
        Func<OpenApiDocument> Document,
        string[] Model,
        string[] File,
        Type? Holder = null,
        OpenApiSpecVersion? WrittenAs = null);

    // ── Rules that read only the document and now read a place by its own keywords ──────────

    private static readonly Dictionary<string, Case> DocumentRules = new[]
    {
        // schema.property-description: the place's own description counts, the component's never does.
        Each("description: inline without a description", AllVersions, v => new Case("schema.property-description", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String }, DescribedTarget()), [Place], [Place])),
        Each("description: wrapper without a description, described component", [V30], v => new Case("schema.property-description", v,
            () => PropertyDocument(Wrapper(s => s.MaxLength = 5), DescribedTarget()), [Place], [Place])),
        Each("description: wrapper with its own description", [V30], v => new Case("schema.property-description", v,
            () => PropertyDocument(Wrapper(s => s.Description = "Place description."), StringTarget()), [], [])),
        Each("description: $ref with maxLength and no description, described component", SiblingVersions, v => new Case("schema.property-description", v,
            () => PropertyDocument(Siblings(r => r.MaxLength = 5), DescribedTarget()), [Place], [Place])),
        Each("description: $ref with only an allOf of its own, described component", SiblingVersions, v => new Case("schema.property-description", v,
            () => PropertyDocument(Siblings(r => r.AllOf = [new OpenApiSchema { MaxLength = 5 }]), DescribedTarget()), [Place], [Place])),
        Each("description: $ref with its own description, undescribed component", SiblingVersions, v => new Case("schema.property-description", v,
            () => PropertyDocument(Siblings(r => r.Description = "Place description."), StringTarget()), [], [])),
        Each("description: bare $ref, undescribed component", AllVersions, v => new Case("schema.property-description", v,
            () => PropertyDocument(Bare(), StringTarget()), [], [])),

        // schema.enum-filled. Microsoft.OpenApi does not write an empty enum at all — next to $ref or
        // in an inline schema, in any version — so a file read back has no empty enum to report, and
        // the negative side of the rule is checked on the model only.
        Each("enum-filled: inline empty enum", AllVersions, v => new Case("schema.enum-filled", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String, Enum = [] }, StringTarget()), [Place], [])),
        Each("enum-filled: wrapper with an empty enum of allowed values", [V30], v => new Case("schema.enum-filled", v,
            () => PropertyDocument(AllowedWrapper([]), StringTarget()), [Place], [])),
        Each("enum-filled: $ref with an empty enum", SiblingVersions, v => new Case("schema.enum-filled", v,
            () => PropertyDocument(Siblings(r => r.Enum = []), StringTarget()), [Place], [])),
        Each("enum-filled: $ref with its own enum, component with an empty enum", SiblingVersions, v => new Case("schema.enum-filled", v,
            () => PropertyDocument(Siblings(r => r.Enum = [Json("a")]), EnumTarget()), [Component], [])),
        Each("enum-filled: bare $ref, component with an empty enum", AllVersions, v => new Case("schema.enum-filled", v,
            () => PropertyDocument(Bare(), EnumTarget()), [Component], [])),

        // schema.no-duplicate-enum: duplicates of the place and of the component are reported apart.
        Each("no-duplicate-enum: inline duplicates", AllVersions, v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String, Enum = [Json("a"), Json("a")] }, StringTarget()), [Place + "/enum"], [Place + "/enum"])),
        Each("no-duplicate-enum: wrapper with duplicated allowed values", [V30], v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(AllowedWrapper([Json("a"), Json("a")]), StringTarget()), [Place + "/enum"], [Place + "/enum"])),
        Each("no-duplicate-enum: $ref with duplicates", SiblingVersions, v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(Siblings(r => r.Enum = [Json("a"), Json("a")]), StringTarget()), [Place + "/enum"], [Place + "/enum"])),
        Each("no-duplicate-enum: $ref with its own distinct enum, component with duplicates", SiblingVersions, v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(Siblings(r => r.Enum = [Json("a"), Json("b")]), DuplicateEnumTarget()), [Component + "/enum"], [Component + "/enum"])),
        Each("no-duplicate-enum: bare $ref, component with duplicates", AllVersions, v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(Bare(), DuplicateEnumTarget()), [Component + "/enum"], [Component + "/enum"])),

        // A nullable enum, anyOf of the enum schema and the null branch: the values of the place are
        // those of its enum branch, as the values of a reference place are its own.
        Each("no-duplicate-enum: nullable enum with duplicates", AllVersions, v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(NullableEnum(v, Json("a"), Json("a")), StringTarget()), [Place + "/enum"], [Place + "/enum"])),
        Each("no-duplicate-enum: nullable enum with distinct values", AllVersions, v => new Case("schema.no-duplicate-enum", v,
            () => PropertyDocument(NullableEnum(v, Json("a"), Json("b")), StringTarget()), [], [])),
        Each("enum-filled: nullable enum with an empty enum", AllVersions, v => new Case("schema.enum-filled", v,
            () => PropertyDocument(NullableEnum(v), StringTarget()), [Place], [])),
        Each("enum-filled: nullable enum with values", AllVersions, v => new Case("schema.enum-filled", v,
            () => PropertyDocument(NullableEnum(v, Json("a")), StringTarget()), [], [])),
    }.SelectMany(c => c).ToDictionary(c => c.Key, c => c.Value);

    // ── Rules that compare a place with its CLR property (TypeBySchemaId) ────────────────────

    private static readonly Dictionary<string, Case> ClrRules = new[]
    {
        // schema.property-constraints: [MaxLength(5)] needs maxLength of at most 5 on the place.
        Each("constraints: inline maxLength weaker than the attribute", AllVersions, v => new Case("schema.property-constraints", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 10 }, ConstrainedTarget()), [Place], [Place], typeof(ConstrainedHolder))),
        Each("constraints: wrapper maxLength weaker than the attribute", [V30], v => new Case("schema.property-constraints", v,
            () => PropertyDocument(Wrapper(s => s.MaxLength = 10), ConstrainedTarget()), [Place], [Place], typeof(ConstrainedHolder))),
        Each("constraints: wrapper maxLength of the attribute", [V30], v => new Case("schema.property-constraints", v,
            () => PropertyDocument(Wrapper(s => s.MaxLength = 5), WeakTarget()), [], [], typeof(ConstrainedHolder))),
        Each("constraints: $ref sibling weaker than the attribute, component right", SiblingVersions, v => new Case("schema.property-constraints", v,
            () => PropertyDocument(Siblings(r => r.MaxLength = 10), ConstrainedTarget()), [Place], [Place], typeof(ConstrainedHolder))),
        Each("constraints: $ref sibling of the attribute, component weaker", SiblingVersions, v => new Case("schema.property-constraints", v,
            () => PropertyDocument(Siblings(r => r.MaxLength = 5), WeakTarget()), [], [], typeof(ConstrainedHolder))),
        Each("constraints: bare $ref is skipped", AllVersions, v => new Case("schema.property-constraints", v,
            () => PropertyDocument(Bare(), WeakTarget()), [], [], typeof(ConstrainedHolder))),

        // schema.required-consistency: [Required] needs the place in the holder's required, in any form of
        // the place; a place without a declaration needs nothing, whatever its type.
        Each("required-consistency: inline place declared required, not listed", AllVersions, v => new Case("schema.required-consistency", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String, Description = "Place description." }, StringTarget()), [Place], [Place], typeof(RequiredHolder))),
        Each("required-consistency: wrapper declared required, not listed", [V30], v => new Case("schema.required-consistency", v,
            () => PropertyDocument(Wrapper(s => s.Description = "Place description."), StringTarget()), [Place], [Place], typeof(RequiredHolder))),
        Each("required-consistency: $ref with siblings declared required, not listed", SiblingVersions, v => new Case("schema.required-consistency", v,
            () => PropertyDocument(Siblings(r => r.Description = "Place description."), StringTarget()), [Place], [Place], typeof(RequiredHolder))),
        Each("required-consistency: bare $ref declared required, not listed", AllVersions, v => new Case("schema.required-consistency", v,
            () => PropertyDocument(Bare(), StringTarget()), [Place], [Place], typeof(RequiredHolder))),
        Each("required-consistency: inline string without a declaration", AllVersions, v => new Case("schema.required-consistency", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String, Description = "Place description." }, StringTarget()), [], [], typeof(FormattedHolder))),

        // schema.property-format: [DataType(DataType.Date)] needs format date on the place.
        Each("format: inline format other than the attribute's", AllVersions, v => new Case("schema.property-format", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }, DateTarget()), [Place], [Place], typeof(FormattedHolder))),
        Each("format: wrapper format other than the attribute's", [V30], v => new Case("schema.property-format", v,
            () => PropertyDocument(Wrapper(s => s.Format = "date-time"), DateTarget()), [Place], [Place], typeof(FormattedHolder))),
        Each("format: wrapper format of the attribute", [V30], v => new Case("schema.property-format", v,
            () => PropertyDocument(Wrapper(s => s.Format = "date"), DateTimeTarget()), [], [], typeof(FormattedHolder))),
        Each("format: $ref sibling format wrong, component right", SiblingVersions, v => new Case("schema.property-format", v,
            () => PropertyDocument(Siblings(r => r.Format = "date-time"), DateTarget()), [Place], [Place], typeof(FormattedHolder))),
        Each("format: $ref sibling format right, component wrong", SiblingVersions, v => new Case("schema.property-format", v,
            () => PropertyDocument(Siblings(r => r.Format = "date"), DateTimeTarget()), [], [], typeof(FormattedHolder))),
        Each("format: bare $ref is skipped", AllVersions, v => new Case("schema.property-format", v,
            () => PropertyDocument(Bare(), DateTimeTarget()), [], [], typeof(FormattedHolder))),
    }.SelectMany(c => c).ToDictionary(c => c.Key, c => c.Value);

    // ── Rules that need a type of the place: a reference in either form has none ─────────────

    private static readonly Dictionary<string, Case> TypedRules = new[]
    {
        // schema.typed-enum: an integer component does not make the place's string enum mistyped.
        Each("typed-enum: inline enum of another type", AllVersions, v => new Case("schema.typed-enum", v,
            () => PropertyDocument(new OpenApiSchema { Type = JsonSchemaType.Integer, Enum = [Json("x")] }, IntegerTarget()), [Place + "/enum/0"], [Place + "/enum/0"])),
        Each("typed-enum: wrapper enum, integer component", [V30], v => new Case("schema.typed-enum", v,
            () => PropertyDocument(Wrapper(s => s.Enum = [Json("x")]), IntegerTarget()), [], [])),
        Each("typed-enum: $ref sibling enum, integer component", SiblingVersions, v => new Case("schema.typed-enum", v,
            () => PropertyDocument(Siblings(r => r.Enum = [Json("x")]), IntegerTarget()), [], [])),
        Each("typed-enum: bare $ref", AllVersions, v => new Case("schema.typed-enum", v,
            () => PropertyDocument(Bare(), IntegerTarget()), [], [])),

        // parameter.optional-has-default: an integer component does not make the parameter a value type.
        Each("optional-has-default: inline optional integer without a default", AllVersions, v => new Case("parameter.optional-has-default", v,
            () => QueryDocument(new OpenApiSchema { Type = JsonSchemaType.Integer }, IntegerTarget()), [QueryParameter], [QueryParameter])),
        Each("optional-has-default: wrapper, integer component", [V30], v => new Case("parameter.optional-has-default", v,
            () => QueryDocument(Wrapper(s => s.Description = "Place description."), IntegerTarget()), [], [])),
        Each("optional-has-default: $ref with siblings, integer component", SiblingVersions, v => new Case("parameter.optional-has-default", v,
            () => QueryDocument(Siblings(r => r.Description = "Place description."), IntegerTarget()), [], [])),
        Each("optional-has-default: bare $ref, integer component", AllVersions, v => new Case("parameter.optional-has-default", v,
            () => QueryDocument(Bare(), IntegerTarget()), [], [])),

        // parameter.schema-type: a parameter without a schema is reported; any reference has its type elsewhere.
        Each("schema-type: inline parameter without a schema", AllVersions, v => new Case("parameter.schema-type", v,
            () => QueryDocument(null, IntegerTarget()), [QueryParameter], [QueryParameter])),
        Each("schema-type: wrapper", [V30], v => new Case("parameter.schema-type", v,
            () => QueryDocument(Wrapper(s => s.Description = "Place description."), IntegerTarget()), [], [])),
        Each("schema-type: $ref with siblings", SiblingVersions, v => new Case("parameter.schema-type", v,
            () => QueryDocument(Siblings(r => r.Description = "Place description."), IntegerTarget()), [], [])),
        Each("schema-type: bare $ref", AllVersions, v => new Case("parameter.schema-type", v,
            () => QueryDocument(Bare(), IntegerTarget()), [], [])),

        // schema.array-items: an array without items is reported where it stands, in a parameter, a
        // body, a response and a property; a component array without items only at the component.
        Each("array-items: inline arrays without items in every place", AllVersions, v => new Case("schema.array-items", v,
            () => ArrayPlacesDocument(() => new OpenApiSchema { Type = JsonSchemaType.Array }, StringTarget()), InlineArrayPointers, InlineArrayPointers)),
        Each("array-items: wrappers in every place, component array without items", [V30], v => new Case("schema.array-items", v,
            () => ArrayPlacesDocument(() => Wrapper(s => s.Description = "Place description."), ItemlessArrayTarget()), [Component], [Component])),
        Each("array-items: $ref with siblings in every place, component array without items", SiblingVersions, v => new Case("schema.array-items", v,
            () => ArrayPlacesDocument(() => Siblings(r => r.Description = "Place description."), ItemlessArrayTarget()), [Component], [Component])),
        Each("array-items: bare $ref in every place, component array without items", AllVersions, v => new Case("schema.array-items", v,
            () => ArrayPlacesDocument(Bare, ItemlessArrayTarget()), [Component], [Component])),
    }.SelectMany(c => c).ToDictionary(c => c.Key, c => c.Value);

    // ── spec.no-ref-siblings: a sibling of $ref is an error in 3.0 only ──────────────────────

    private static readonly Dictionary<string, Case> NoRefSiblings = new[]
    {
        Each("no-ref-siblings: wrapper with a description", [V30], v => new Case("spec.no-ref-siblings", v,
            () => PropertyDocument(Wrapper(s => s.Description = "Place description."), StringTarget()), [], [])),
        // The 3.0 writer drops the siblings of $ref, so the 3.0 file is the 3.1 text declared as 3.0:
        // the foreign document the rule exists for. The Microsoft.OpenApi 3.0 reader drops the
        // siblings of $ref when it reads that file, so the negative side of the rule for 3.0 is
        // checked on the model only.
        Each("no-ref-siblings: $ref with a description in a 3.0 document", [V30], v => new Case("spec.no-ref-siblings", v,
            () => PropertyDocument(Siblings(r => r.Description = "Place description."), StringTarget()), [Place], [], WrittenAs: V31)),
        Each("no-ref-siblings: $ref with a description", SiblingVersions, v => new Case("spec.no-ref-siblings", v,
            () => PropertyDocument(Siblings(r => r.Description = "Place description."), StringTarget()), [], [])),
        Each("no-ref-siblings: bare $ref", AllVersions, v => new Case("spec.no-ref-siblings", v,
            () => PropertyDocument(Bare(), DescribedTarget()), [], [])),
    }.SelectMany(c => c).ToDictionary(c => c.Key, c => c.Value);

    public static TheoryData<string, DocumentFormat> DocumentRuleCases => Cases(DocumentRules);
    public static TheoryData<string, DocumentFormat> ClrRuleCases => Cases(ClrRules);
    public static TheoryData<string, DocumentFormat> TypedRuleCases => Cases(TypedRules);
    public static TheoryData<string, DocumentFormat> NoRefSiblingsCases => Cases(NoRefSiblings);

    [Theory]
    [MemberData(nameof(DocumentRuleCases))]
    public async Task DocumentRules_ReadAPlaceByItsOwnKeywords_InTheModelAndInTheFileReadBack(string name, DocumentFormat format) =>
        await Verify(DocumentRules[name], format);

    [Theory]
    [MemberData(nameof(ClrRuleCases))]
    public async Task ClrRules_CompareThePlacesOwnKeywordsWithTheAttribute_NotTheComponent(string name, DocumentFormat format) =>
        await Verify(ClrRules[name], format);

    [Theory]
    [MemberData(nameof(TypedRuleCases))]
    public async Task RulesThatNeedAType_SkipAReferenceInEitherForm_AndNeverReadTheComponentsType(string name, DocumentFormat format) =>
        await Verify(TypedRules[name], format);

    [Theory]
    [MemberData(nameof(NoRefSiblingsCases))]
    public async Task NoRefSiblings_ReportsASiblingOfRefIn30Only_AndNeverTheWrapper(string name, DocumentFormat format) =>
        await Verify(NoRefSiblings[name], format);

    private static async Task Verify(Case @case, DocumentFormat format)
    {
        var model = Pointers(@case.Document(), @case.Version, @case);

        var written = @case.WrittenAs ?? @case.Version;
        var text = format == DocumentFormat.Json
            ? await @case.Document().SerializeAsJsonAsync(written, TestContext.Current.CancellationToken)
            : await @case.Document().SerializeAsYamlAsync(written, TestContext.Current.CancellationToken);
        if (written != @case.Version)
            text = DeclareVersion(text, @case.Version);
        var (read, readVersion) = await ValidateReader.ReadAsync(text, format, TestContext.Current.CancellationToken);
        var file = Pointers(read, readVersion, @case);

        new { Model = model, File = file }.Should().BeEquivalentTo(
            new { @case.Model, @case.File },
            options => options.WithStrictOrdering(),
            because: $"{@case.Rule} reports these pointers on the model and on the {format} file read back");
    }

    private static string[] Pointers(OpenApiDocument document, OpenApiSpecVersion? version, Case @case)
    {
        var context = new ValidationContext
        {
            OpenApiSpecVersion = version,
            TypeBySchemaId = @case.Holder is null ? null : new Dictionary<string, Type> { ["Holder"] = @case.Holder },
        };
        return CoreValidator.Validate(document, context).Violations
            .Where(v => v.RuleId == @case.Rule)
            .Select(v => v.JsonPointer)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The written 3.1 text with its <c>openapi</c> field set to 3.0.4.</summary>
    private static string DeclareVersion(string text, OpenApiSpecVersion version)
    {
        if (version != OpenApiSpecVersion.OpenApi3_0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Only a 3.0 declaration is rewritten.");
        var field = new Regex(@"3\.1\.\d+");
        if (!field.IsMatch(text))
            throw new InvalidOperationException("The written text has no 3.1 version field.");
        return field.Replace(text, "3.0.4", 1);
    }

    // ── Documents ─────────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument PropertyDocument(IOpenApiSchema place, OpenApiSchema target) => new()
    {
        Info = new OpenApiInfo { Title = "T", Version = "1" },
        Paths = new OpenApiPaths(),
        Components = Components(target, place),
    };

    private static OpenApiDocument QueryDocument(IOpenApiSchema? place, OpenApiSchema target) => new()
    {
        Info = new OpenApiInfo { Title = "T", Version = "1" },
        Paths = new OpenApiPaths
        {
            ["/items"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [HttpMethod.Get] = new OpenApiOperation
                    {
                        Parameters =
                        [
                            new OpenApiParameter
                            {
                                Name = "place",
                                In = ParameterLocation.Query,
                                Required = false,
                                Description = "Place parameter.",
                                Schema = place,
                            },
                        ],
                        Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "Done." } },
                    },
                },
            },
        },
        Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema> { ["Target"] = target } },
    };

    private static OpenApiDocument ArrayPlacesDocument(Func<IOpenApiSchema> place, OpenApiSchema target) => new()
    {
        Info = new OpenApiInfo { Title = "T", Version = "1" },
        Paths = new OpenApiPaths
        {
            ["/items"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [HttpMethod.Post] = new OpenApiOperation
                    {
                        Parameters =
                        [
                            new OpenApiParameter { Name = "place", In = ParameterLocation.Query, Schema = place() },
                        ],
                        RequestBody = new OpenApiRequestBody
                        {
                            Content = new Dictionary<string, IOpenApiMediaType> { ["application/json"] = new OpenApiMediaType { Schema = place() } },
                        },
                        Responses = new OpenApiResponses
                        {
                            ["200"] = new OpenApiResponse
                            {
                                Description = "Done.",
                                Content = new Dictionary<string, IOpenApiMediaType> { ["application/json"] = new OpenApiMediaType { Schema = place() } },
                            },
                        },
                    },
                },
            },
        },
        Components = Components(target, place()),
    };

    private static OpenApiComponents Components(OpenApiSchema target, IOpenApiSchema place) => new()
    {
        Schemas = new Dictionary<string, IOpenApiSchema>
        {
            ["Holder"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Holder of the place.",
                Properties = new Dictionary<string, IOpenApiSchema> { ["place"] = place },
            },
            ["Target"] = target,
        },
    };

    // ── Places ────────────────────────────────────────────────────────────────────────────────

    private static OpenApiSchemaReference Reference() => new("Target", null);

    /// <summary>The 3.0 form: the reference wrapped in <c>allOf</c>, the keywords on the wrapper.</summary>
    private static OpenApiSchema Wrapper(Action<OpenApiSchema> keywords)
    {
        var wrapper = new OpenApiSchema { AllOf = [Reference()] };
        keywords(wrapper);
        return wrapper;
    }

    /// <summary>
    /// The 3.0 form the generator writes for <c>[AllowedValues]</c>: the values as a second element of
    /// the wrapper's <c>allOf</c>, <c>{allOf: [$ref, {enum}]}</c>.
    /// </summary>
    private static OpenApiSchema AllowedWrapper(List<JsonNode> values) =>
        new() { AllOf = [Reference(), new OpenApiSchema { Enum = values }] };

    /// <summary>The 3.1+ form: the keywords in the reference's own fields, next to <c>$ref</c>.</summary>
    private static OpenApiSchemaReference Siblings(Action<JsonSchemaReference> keywords)
    {
        var reference = Reference();
        keywords(reference.Reference);
        return reference;
    }

    private static IOpenApiSchema Bare() => Reference();

    // ── Components ────────────────────────────────────────────────────────────────────────────

    private static OpenApiSchema StringTarget() => new() { Type = JsonSchemaType.String };

    private static OpenApiSchema DescribedTarget() => new() { Type = JsonSchemaType.String, Description = "Target description." };

    private static OpenApiSchema EnumTarget() => new() { Type = JsonSchemaType.String, Enum = [] };

    private static OpenApiSchema DuplicateEnumTarget() => new() { Type = JsonSchemaType.String, Enum = [Json("a"), Json("a")] };

    private static OpenApiSchema ConstrainedTarget() => new() { Type = JsonSchemaType.String, MaxLength = 5 };

    private static OpenApiSchema WeakTarget() => new() { Type = JsonSchemaType.String, MaxLength = 10 };

    private static OpenApiSchema DateTarget() => new() { Type = JsonSchemaType.String, Format = "date" };

    private static OpenApiSchema DateTimeTarget() => new() { Type = JsonSchemaType.String, Format = "date-time" };

    private static OpenApiSchema IntegerTarget() => new() { Type = JsonSchemaType.Integer };

    private static OpenApiSchema ItemlessArrayTarget() => new() { Type = JsonSchemaType.Array };

    // ── Case tables ───────────────────────────────────────────────────────────────────────────

    private static JsonNode Json(string value) => JsonValue.Create(value);

    /// <summary>The generator's nullable form of a string enum with <paramref name="values"/>, for <paramref name="version"/>.</summary>
    private static OpenApiSchema NullableEnum(OpenApiSpecVersion version, params JsonNode[] values) =>
        Core.Schema.VersionedSchemaForms.NullableEnum(
            new OpenApiSchema { Type = JsonSchemaType.String, Enum = [.. values], Description = "A tint." }, version);

    private static IEnumerable<KeyValuePair<string, Case>> Each(
        string name, OpenApiSpecVersion[] versions, Func<OpenApiSpecVersion, Case> create) =>
        versions.Select(v => KeyValuePair.Create($"{name} ({Label(v)})", create(v)));

    private static string Label(OpenApiSpecVersion version) => version switch
    {
        OpenApiSpecVersion.OpenApi3_0 => "3.0",
        OpenApiSpecVersion.OpenApi3_1 => "3.1",
        OpenApiSpecVersion.OpenApi3_2 => "3.2",
        _ => version.ToString(),
    };

    private static TheoryData<string, DocumentFormat> Cases(Dictionary<string, Case> table)
    {
        var data = new TheoryData<string, DocumentFormat>();
        foreach (var name in table.Keys)
        foreach (var format in new[] { DocumentFormat.Json, DocumentFormat.Yaml })
            data.Add(name, format);
        return data;
    }
}

/// <summary>Reads a document from its text the way standalone <c>validate</c> reads a file.</summary>
internal static class ValidateReader
{
    /// <summary>The document and the version its <c>openapi</c> field declares.</summary>
    public static async Task<(OpenApiDocument Document, OpenApiSpecVersion? Version)> ReadAsync(
        string text, DocumentFormat format, CancellationToken cancellationToken)
    {
        var settings = new OpenApiReaderSettings();
        settings.TryAddReader("yaml", new OpenApiYamlReader());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
        var result = await OpenApiDocument.LoadAsync(
            stream, format == DocumentFormat.Json ? "json" : "yaml", settings, cancellationToken);

        if (result.Document is null || result.Diagnostic?.Errors is { Count: > 0 })
            throw new InvalidOperationException(
                "The document did not read back: "
                + string.Join("; ", result.Diagnostic?.Errors.Select(e => e.Message) ?? []));
        return (result.Document, result.Diagnostic?.SpecificationVersion);
    }
}
