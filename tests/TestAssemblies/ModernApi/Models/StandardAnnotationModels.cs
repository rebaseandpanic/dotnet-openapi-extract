using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Models.Keywords;

/// <summary>[Length] on each schema shape.</summary>
public class LengthModel
{
    /// <summary>A string.</summary>
    [Length(2, 8)]
    public string Code { get; set; } = "ab";

    /// <summary>A list.</summary>
    [Length(1, 3)]
    public List<string> Tags { get; set; } = ["a"];

    /// <summary>An array.</summary>
    [Length(2, 5)]
    public int[] Numbers { get; set; } = [1, 2];

    /// <summary>A dictionary.</summary>
    [Length(1, 4)]
    public Dictionary<string, int> Counts { get; set; } = new() { ["a"] = 1 };

    /// <summary>Only the empty string.</summary>
    [Length(0, 0)]
    public string Empty { get; set; } = string.Empty;
}

/// <summary>[DataType] with every member, on a property whose type has a format of its own (uuid).</summary>
public class DataTypeModel
{
    /// <summary>Custom.</summary>
    [DataType(DataType.Custom)] public Guid AsCustom { get; set; }

    /// <summary>DateTime.</summary>
    [DataType(DataType.DateTime)] public Guid AsDateTime { get; set; }

    /// <summary>Date.</summary>
    [DataType(DataType.Date)] public Guid AsDate { get; set; }

    /// <summary>Time.</summary>
    [DataType(DataType.Time)] public Guid AsTime { get; set; }

    /// <summary>Duration.</summary>
    [DataType(DataType.Duration)] public Guid AsDuration { get; set; }

    /// <summary>PhoneNumber.</summary>
    [DataType(DataType.PhoneNumber)] public Guid AsPhoneNumber { get; set; }

    /// <summary>Currency.</summary>
    [DataType(DataType.Currency)] public Guid AsCurrency { get; set; }

    /// <summary>Text.</summary>
    [DataType(DataType.Text)] public Guid AsText { get; set; }

    /// <summary>Html.</summary>
    [DataType(DataType.Html)] public Guid AsHtml { get; set; }

    /// <summary>MultilineText.</summary>
    [DataType(DataType.MultilineText)] public Guid AsMultilineText { get; set; }

    /// <summary>EmailAddress.</summary>
    [DataType(DataType.EmailAddress)] public Guid AsEmailAddress { get; set; }

    /// <summary>Password.</summary>
    [DataType(DataType.Password)] public Guid AsPassword { get; set; }

    /// <summary>Url.</summary>
    [DataType(DataType.Url)] public Guid AsUrl { get; set; }

    /// <summary>ImageUrl.</summary>
    [DataType(DataType.ImageUrl)] public Guid AsImageUrl { get; set; }

    /// <summary>CreditCard.</summary>
    [DataType(DataType.CreditCard)] public Guid AsCreditCard { get; set; }

    /// <summary>PostalCode.</summary>
    [DataType(DataType.PostalCode)] public Guid AsPostalCode { get; set; }

    /// <summary>Upload.</summary>
    [DataType(DataType.Upload)] public Guid AsUpload { get; set; }

    /// <summary>A custom data type name.</summary>
    [DataType("LicensePlate")] public Guid AsCustomName { get; set; }

    /// <summary>A string, which has no format of its own.</summary>
    [DataType(DataType.Date)] public string DateText { get; set; } = "2024-01-02";
}

/// <summary>Competing format sources on one member.</summary>
public class FormatPriorityModel
{
    /// <summary>[SwaggerSchema(Format)] over [EmailAddress].</summary>
    [SwaggerSchema(Format = "x-code"), EmailAddress]
    public string SchemaOverProfile { get; set; } = string.Empty;

    /// <summary>[SwaggerSchema(Format)] over [DataType].</summary>
    [SwaggerSchema(Format = "x-code"), DataType(DataType.Date)]
    public string SchemaOverDataType { get; set; } = string.Empty;

    /// <summary>[SwaggerSchema(Format)] over the type's format.</summary>
    [SwaggerSchema(Format = "x-code")]
    public DateTime SchemaOverType { get; set; }

    /// <summary>[EmailAddress] over [DataType].</summary>
    [EmailAddress, DataType(DataType.Url)]
    public string EmailOverDataType { get; set; } = string.Empty;

    /// <summary>[Url] over [DataType].</summary>
    [Url, DataType(DataType.Date)]
    public string UrlOverDataType { get; set; } = string.Empty;

    /// <summary>[Phone] over [DataType].</summary>
    [Phone, DataType(DataType.EmailAddress)]
    public string PhoneOverDataType { get; set; } = string.Empty;

    /// <summary>[EmailAddress] over the type's format.</summary>
    [EmailAddress]
    public DateTime ProfileOverType { get; set; }

    /// <summary>[DataType] over the type's format.</summary>
    [DataType(DataType.Date)]
    public DateTime DataTypeOverType { get; set; }

    /// <summary>A [DataType] member without a format next to a profile attribute.</summary>
    [DataType(DataType.Text), Phone]
    public string FormatlessDataTypeWithProfile { get; set; } = string.Empty;

    /// <summary>[SwaggerSchema(Format)] on a number read from strings: the numeric branch.</summary>
    [SwaggerSchema(Format = "x-count"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int SchemaOverNumberBranch { get; set; }

    /// <summary>A [DataType] member without a format on a number read from strings.</summary>
    [DataType(DataType.Currency), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal FormatlessDataTypeOnNumberBranch { get; set; }
}

/// <summary>A nested object for reference-typed annotated properties.</summary>
public class AnnotatedTarget
{
    /// <summary>Label.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>Resources whose keys a [Display] names.</summary>
public static class AnnotationResources
{
    /// <summary>A localized text.</summary>
    public static string LocalizedText => "Localized text";
}

/// <summary>Competing description sources on one member.</summary>
public class DescriptionPriorityModel
{
    /// <summary>Xml summary.</summary>
    [SwaggerSchema(Description = "From SwaggerSchema"), Display(Description = "From Display")]
    public string SchemaOverDisplay { get; set; } = string.Empty;

    /// <summary>Xml summary.</summary>
    [SwaggerSchema("From the SwaggerSchema constructor"), Display(Description = "From Display")]
    public string SchemaConstructorOverDisplay { get; set; } = string.Empty;

    /// <summary>Xml summary.</summary>
    [Description("From Description"), Display(Description = "From Display")]
    public string DescriptionOverDisplay { get; set; } = string.Empty;

    /// <summary>Xml summary.</summary>
    [Display(Description = "From Display")]
    public string DisplayOverXml { get; set; } = string.Empty;

    /// <summary>Xml summary.</summary>
    [Display(Name = "Shown name")]
    public string DisplayWithoutDescription { get; set; } = string.Empty;

    /// <summary>Xml summary.</summary>
    [Display(Description = nameof(AnnotationResources.LocalizedText), ResourceType = typeof(AnnotationResources))]
    public string DisplayResourceKey { get; set; } = string.Empty;

    /// <summary>Xml summary.</summary>
    [Display(Description = "From Display")]
    public AnnotatedTarget DisplayOnReference { get; set; } = new();

    /// <summary>Xml summary.</summary>
    [Display(Description = "From Display")]
    public int? DisplayOnNullableNumber { get; set; }
}

/// <summary>readOnly, writeOnly and title sources.</summary>
public class AccessModel
{
    /// <summary>[SwaggerSchema(ReadOnly)].</summary>
    [SwaggerSchema(ReadOnly = true)]
    public string SchemaReadOnly { get; set; } = string.Empty;

    /// <summary>[ReadOnly(true)].</summary>
    [ReadOnly(true)]
    public string AttributeReadOnly { get; set; } = string.Empty;

    /// <summary>[SwaggerSchema(WriteOnly)].</summary>
    [SwaggerSchema(WriteOnly = true)]
    public string SchemaWriteOnly { get; set; } = string.Empty;

    /// <summary>An explicit false of [SwaggerSchema] over [ReadOnly(true)].</summary>
    [SwaggerSchema(ReadOnly = false), ReadOnly(true)]
    public string ExplicitFalseOverAttribute { get; set; } = string.Empty;

    /// <summary>[ReadOnly(false)].</summary>
    [ReadOnly(false)]
    public string AttributeFalse { get; set; } = string.Empty;

    /// <summary>Write-only, with [ReadOnly(true)] overruled by an explicit false.</summary>
    [SwaggerSchema(ReadOnly = false, WriteOnly = true), ReadOnly(true)]
    public string WriteOnlyOverAttribute { get; set; } = string.Empty;

    /// <summary>[SwaggerSchema(Title)].</summary>
    [SwaggerSchema(Title = "Display title")]
    public string Titled { get; set; } = string.Empty;

    /// <summary>Read-only reference.</summary>
    [SwaggerSchema(ReadOnly = true, Title = "Target")]
    public AnnotatedTarget ReadOnlyReference { get; set; } = new();
}
