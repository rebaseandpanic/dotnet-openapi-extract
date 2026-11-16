using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// An API version as <c>Asp.Versioning</c> writes it in <c>[ApiVersion]</c>: <c>major[.minor][-status]</c>, the status
/// letters and digits with dots after the first character (<c>beta</c>, <c>rc.1</c>, <c>preview.1</c>), as
/// <c>ApiVersionParser.Default</c> of Asp.Versioning 8.1.0 accepts it (it refuses <c>-</c> and <c>_</c> in the status).
/// A group (date) version (<c>2017-05-01</c>, <c>2017-05-01.1-RC</c>) is not represented.
/// </summary>
/// <param name="Major">The major version.</param>
/// <param name="Minor">The minor version; <see langword="null"/> when the text has none (<c>1</c>).</param>
/// <param name="Status">The status (<c>beta</c> of <c>1.0-beta</c>); <see langword="null"/> when there is none.</param>
internal sealed record ApiVersionValue(int Major, int? Minor, string? Status)
{
    private static readonly Regex Numeric = new(
        @"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:-(?<status>[A-Za-z0-9][A-Za-z0-9.]*))?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses <paramref name="text"/> as <c>major[.minor][-status]</c>; <see langword="false"/> for a group
    /// (date) version and for any other text.
    /// </summary>
    public static bool TryParse(string? text, out ApiVersionValue version)
    {
        version = null!;
        if (text == null)
            return false;

        var match = Numeric.Match(text.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
            return false;

        int? minor = null;
        if (match.Groups["minor"].Success)
        {
            if (!int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMinor))
                return false;
            minor = parsedMinor;
        }

        var status = match.Groups["status"].Success ? match.Groups["status"].Value : null;
        version = new ApiVersionValue(major, minor, status);
        return true;
    }

    /// <summary>
    /// Whether two versions are the same API version, as <c>Asp.Versioning</c> compares them: an absent
    /// minor version equals 0 (<c>1</c> = <c>1.0</c>), the status is compared ignoring case.
    /// </summary>
    public bool SameVersion(ApiVersionValue other) =>
        Major == other.Major
        && (Minor ?? 0) == (other.Minor ?? 0)
        && string.Equals(Status ?? string.Empty, other.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Formats an <see cref="ApiVersionValue"/> with a format string of <c>Asp.Versioning</c>
/// (<c>ApiExplorerOptions.SubstitutionFormat</c>, default <c>VVV</c>) the way
/// <c>ApiVersion.ToString(format)</c> does, for the specifiers of a version without a group date.
/// </summary>
/// <remarks>
/// Supported: <c>V</c> (major), <c>VV</c> (major.minor), <c>VVV</c> (major, the minor when it is not 0,
/// status), <c>VVVV</c> (major.minor-status), <c>v</c> (minor), <c>S</c> (status), <c>P</c>/<c>P</c><i>n</i>
/// (major padded to <i>n</i> digits, 2 by default), <c>p</c>/<c>p</c><i>n</i> (minor padded),
/// <c>PP</c>, <c>PPP</c>, <c>PPPP</c> (the padded forms of <c>VV</c>, <c>VVV</c>, <c>VVVV</c>), literals
/// in <c>'…'</c> or <c>"…"</c>, <c>\</c> escapes, <c>%</c> before a single specifier, and any other
/// character as itself. The group version specifiers (<c>F</c>, <c>G</c>, <c>M</c>, <c>d</c>,
/// <c>y</c>) and other runs of a letter are not: formatting fails, it does not guess.
/// </remarks>
internal static class ApiVersionFormat
{
    /// <summary>The default of <c>ApiExplorerOptions.SubstitutionFormat</c>.</summary>
    public const string DefaultSubstitutionFormat = "VVV";

    private const int DefaultPadding = 2;
    private const int MaxPadding = 99;

    /// <summary>
    /// Formats <paramref name="version"/> with <paramref name="format"/>; <see langword="false"/> when the
    /// format is malformed (an unclosed quote) or uses a specifier that is not supported.
    /// </summary>
    public static bool TryFormat(ApiVersionValue version, string format, out string result)
    {
        result = string.Empty;
        if (string.IsNullOrEmpty(format))
            return false;

        var text = new StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            var ch = format[i];

            if (ch is '\'' or '"')
            {
                var close = format.IndexOf(ch, i + 1);
                if (close < 0)
                    return false;
                text.Append(format, i + 1, close - i - 1);
                i = close;
                continue;
            }

            if (ch == '\\' && i + 1 < format.Length && IsEscapable(format[i + 1]))
            {
                text.Append(format[++i]);
                continue;
            }

            if (ch == '%' && i + 1 < format.Length && IsSpecifier(format[i + 1]))
            {
                // A single specifier: one letter, the digits after it are literal text.
                if (!TryAppend(version, format[++i], 1, padding: null, text))
                    return false;
                continue;
            }

            if (IsSpecifier(ch))
            {
                var length = 1;
                while (i + length < format.Length && format[i + length] == ch)
                    length++;
                var digitsStart = i + length;
                var digitsEnd = digitsStart;
                while (digitsEnd < format.Length && char.IsAsciiDigit(format[digitsEnd]))
                    digitsEnd++;

                int? padding = null;
                if (digitsEnd > digitsStart)
                {
                    if (!int.TryParse(format.AsSpan(digitsStart, digitsEnd - digitsStart), NumberStyles.None,
                            CultureInfo.InvariantCulture, out var parsed) || parsed > MaxPadding)
                        return false;
                    padding = parsed;
                }

                if (!TryAppend(version, ch, length, padding, text))
                    return false;
                i = digitsEnd - 1;
                continue;
            }

            text.Append(ch);
        }

        result = text.ToString();
        return true;
    }

    private static bool IsSpecifier(char ch) => ch is 'F' or 'G' or 'M' or 'P' or 'S' or 'V' or 'd' or 'p' or 'v' or 'y';

    private static bool IsEscapable(char ch) => ch is '\'' or '\\' || IsSpecifier(ch);

    /// <summary>Appends one specifier token: <paramref name="length"/> times <paramref name="letter"/>, then <paramref name="padding"/> digits.</summary>
    private static bool TryAppend(ApiVersionValue version, char letter, int length, int? padding, StringBuilder text)
    {
        var invariant = CultureInfo.InvariantCulture;
        switch (letter, length, padding)
        {
            case ('V', 1, null):
                text.Append(version.Major.ToString(invariant));
                return true;
            case ('V', 2, null):
                text.Append(version.Major.ToString(invariant)).Append('.').Append((version.Minor ?? 0).ToString(invariant));
                return true;
            case ('V', 3, null):
                text.Append(version.Major.ToString(invariant));
                if (version.Minor is > 0)
                    text.Append('.').Append(version.Minor.Value.ToString(invariant));
                AppendStatus(version, text);
                return true;
            case ('V', 4, null):
                text.Append(version.Major.ToString(invariant)).Append('.').Append((version.Minor ?? 0).ToString(invariant));
                AppendStatus(version, text);
                return true;
            case ('v', 1, null):
                if (version.Minor is { } minor)
                    text.Append(minor.ToString(invariant));
                return true;
            case ('S', 1, null):
                text.Append(version.Status);
                return true;
            case ('P', 1, _):
                text.Append(Pad(version.Major, padding ?? DefaultPadding));
                return true;
            case ('p', 1, _):
                if (version.Minor is { } paddedMinor)
                    text.Append(Pad(paddedMinor, padding ?? DefaultPadding));
                return true;
            case ('P', 2, null):
                text.Append(Pad(version.Major, DefaultPadding)).Append('.').Append(Pad(version.Minor ?? 0, DefaultPadding));
                return true;
            case ('P', 3, null):
                text.Append(Pad(version.Major, DefaultPadding));
                if (version.Minor is > 0)
                    text.Append('.').Append(Pad(version.Minor.Value, DefaultPadding));
                AppendStatus(version, text);
                return true;
            case ('P', 4, null):
                text.Append(Pad(version.Major, DefaultPadding)).Append('.').Append(Pad(version.Minor ?? 0, DefaultPadding));
                AppendStatus(version, text);
                return true;
            default:
                return false;
        }
    }

    private static string Pad(int value, int digits) =>
        value.ToString(digits == 0 ? "0" : new string('0', digits), CultureInfo.InvariantCulture);

    private static void AppendStatus(ApiVersionValue version, StringBuilder text)
    {
        if (!string.IsNullOrEmpty(version.Status))
            text.Append('-').Append(version.Status);
    }
}
