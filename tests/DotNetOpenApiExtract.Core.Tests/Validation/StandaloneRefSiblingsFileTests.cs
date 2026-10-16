using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// <c>validate --spec</c> of a file written by hand: in OpenAPI 3.0 a Reference Object cannot be
/// extended with other properties (OAS 3.0, Reference Object: "This object cannot be extended with
/// additional properties and any properties added SHALL be ignored"), so every object of the file with
/// <c>$ref</c> and another key is a <c>spec.no-ref-siblings</c> error at its own pointer, whatever the
/// place. A Path Item Object has a <c>$ref</c> field of its own next to its other fields, a
/// <c>properties</c> map may name a property <c>$ref</c>, and example, default, enum and extension
/// values are data: none of them is a reference. In 3.1 and later the siblings of a <c>$ref</c> are
/// allowed.
/// </summary>
public class StandaloneRefSiblingsFileTests
{
    private const string Rule = "spec.no-ref-siblings";

    /// <summary>A 3.0 document with siblings next to <c>$ref</c> in five places.</summary>
    private const string WithSiblings30 = """
        {
          "openapi": "3.0.4",
          "info": { "title": "Siblings", "version": "1", "description": "References with siblings." },
          "paths": {
            "/items": {
              "get": {
                "operationId": "ListItems",
                "parameters": [ { "$ref": "#/components/parameters/Limit", "description": "Local text." } ],
                "responses": {
                  "200": { "$ref": "#/components/responses/Items", "x-note": "local" },
                  "422": {
                    "description": "Rejected.",
                    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Target", "nullable": true } } }
                  }
                }
              }
            }
          },
          "components": {
            "parameters": {
              "Limit": { "name": "limit", "in": "query", "description": "Page size.", "schema": { "type": "integer" } }
            },
            "responses": {
              "Items": { "description": "Items.", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/List" } } } }
            },
            "schemas": {
              "Target": { "type": "object", "description": "Target." },
              "Holder": {
                "type": "object",
                "properties": { "p": { "$ref": "#/components/schemas/Target", "description": "Local text." } }
              },
              "List": {
                "type": "array",
                "items": { "$ref": "#/components/schemas/Target", "maxLength": 3 }
              }
            }
          }
        }
        """;

    /// <summary>The pointers of the five places, each with its own siblings.</summary>
    private static readonly string[] SiblingPlaces =
    [
        "#/paths/~1items/get/parameters/0",
        "#/paths/~1items/get/responses/200",
        "#/paths/~1items/get/responses/422/content/application~1json/schema",
        "#/components/schemas/Holder/properties/p",
        "#/components/schemas/List/items",
    ];

    /// <summary>
    /// The same document as YAML, where the parameter's sibling is in block style and the property's
    /// in flow style.
    /// </summary>
    private const string WithSiblings30Yaml = """
        openapi: 3.0.4
        info:
          title: Siblings
          version: '1'
          description: References with siblings.
        paths:
          /items:
            get:
              operationId: ListItems
              parameters:
                - $ref: '#/components/parameters/Limit'
                  description: Local text.
              responses:
                '200':
                  $ref: '#/components/responses/Items'
                  x-note: local
                '422':
                  description: Rejected.
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Target'
                        nullable: true
        components:
          parameters:
            Limit: { name: limit, in: query, description: Page size., schema: { type: integer } }
          responses:
            Items:
              description: Items.
              content:
                application/json:
                  schema: { $ref: '#/components/schemas/List' }
          schemas:
            Target: { type: object, description: Target. }
            Holder:
              type: object
              properties:
                p: { $ref: '#/components/schemas/Target', description: Local text. }
            List:
              type: array
              items:
                $ref: '#/components/schemas/Target'
                maxLength: 3
        """;

    /// <summary>
    /// A 3.0 document whose <c>$ref</c> keys are all either alone in a Reference Object or not a
    /// reference at all: a Path Item with <c>$ref</c> and a summary, a property named <c>$ref</c>
    /// beside another property, and objects with <c>$ref</c> and other keys inside an example, a
    /// default, an enum, an extension and an Example Object's value.
    /// </summary>
    private const string WithoutSiblings30 = """
        {
          "openapi": "3.0.4",
          "info": { "title": "Clean", "version": "1", "description": "No reference with siblings." },
          "paths": {
            "/shared": { "$ref": "#/paths/~1items", "summary": "Same as items." },
            "/items": {
              "get": {
                "operationId": "ListItems",
                "x-sample": { "$ref": "#/x", "note": "extension data" },
                "responses": {
                  "200": {
                    "description": "Items.",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Holder" },
                        "examples": { "one": { "summary": "One.", "value": { "$ref": "literal", "other": 1 } } }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Target": { "type": "object", "description": "Target." },
              "Holder": {
                "type": "object",
                "example": { "$ref": "literal", "other": 1 },
                "properties": {
                  "$ref": { "type": "string", "description": "A property named $ref." },
                  "plain": { "type": "string", "description": "Another property." },
                  "target": { "$ref": "#/components/schemas/Target" },
                  "data": {
                    "type": "object",
                    "default": { "$ref": "literal", "other": 1 },
                    "enum": [ { "$ref": "literal", "other": 1 } ]
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// A 3.0 document whose extensible maps — Paths, Responses and a Callback (OAS 3.0: "This object MAY
    /// be extended with Specification Extensions") — carry an extension whose value is an object with
    /// <c>$ref</c> and other keys, also deeper inside: extension values are data. Entries of plain maps
    /// whose name only starts with <c>x-</c> — a component schema, a component response, a property — are
    /// real objects, and their siblings next to <c>$ref</c> are errors.
    /// </summary>
    private const string ExtensionsAndXNames30 = """
        {
          "openapi": "3.0.4",
          "info": { "title": "Extensions", "version": "1", "description": "Extensions of maps." },
          "paths": {
            "x-meta": { "$ref": "literal", "note": "extension data", "inner": { "$ref": "literal", "other": 1 } },
            "/items": {
              "get": {
                "operationId": "ListItems",
                "responses": {
                  "200": { "description": "Items." },
                  "x-payload": { "$ref": "literal", "note": "extension data" }
                },
                "callbacks": {
                  "onEvent": {
                    "{$request.body#/url}": { "post": { "responses": { "200": { "description": "Received." } } } },
                    "x-hook": { "$ref": "literal", "note": "extension data" }
                  }
                }
              }
            }
          },
          "components": {
            "responses": {
              "x-shared": { "$ref": "#/components/responses/Plain", "description": "Local text." },
              "Plain": { "description": "Plain." }
            },
            "schemas": {
              "Target": { "type": "object", "description": "Target." },
              "x-alias": { "$ref": "#/components/schemas/Target", "description": "Local text." },
              "Holder": {
                "type": "object",
                "properties": { "x-prop": { "$ref": "#/components/schemas/Target", "description": "Local text." } }
              }
            }
          }
        }
        """;

    private const string ExtensionsAndXNames30Yaml = """
        openapi: 3.0.4
        info:
          title: Extensions
          version: '1'
          description: Extensions of maps.
        paths:
          x-meta:
            $ref: literal
            note: extension data
            inner: { $ref: literal, other: 1 }
          /items:
            get:
              operationId: ListItems
              responses:
                '200':
                  description: Items.
                x-payload:
                  $ref: literal
                  note: extension data
              callbacks:
                onEvent:
                  '{$request.body#/url}':
                    post:
                      responses:
                        '200': { description: Received. }
                  x-hook: { $ref: literal, note: extension data }
        components:
          responses:
            x-shared:
              $ref: '#/components/responses/Plain'
              description: Local text.
            Plain: { description: Plain. }
          schemas:
            Target: { type: object, description: Target. }
            x-alias:
              $ref: '#/components/schemas/Target'
              description: Local text.
            Holder:
              type: object
              properties:
                x-prop: { $ref: '#/components/schemas/Target', description: Local text. }
        """;

    /// <summary>The entries named <c>x-…</c> of plain maps, each a reference with a sibling.</summary>
    private static readonly string[] XNamedPlaces =
    [
        "#/components/responses/x-shared",
        "#/components/schemas/x-alias",
        "#/components/schemas/Holder/properties/x-prop",
    ];

    public static TheoryData<string, string> ExtensionFiles => new()
    {
        { "extensions.json", ExtensionsAndXNames30 },
        { "extensions.yaml", ExtensionsAndXNames30Yaml },
    };

    [Theory]
    [MemberData(nameof(ExtensionFiles))]
    public async Task File30_ExtensionsOfExtensibleMapsAreData_AndXNamedEntriesOfPlainMapsAreChecked(string fileName, string content)
    {
        var (_, violations) = await ValidateAsync(fileName, content);

        violations.Select(v => v["jsonPointer"]!.GetValue<string>()).Should().BeEquivalentTo(XNamedPlaces);
    }

    private const string WithSiblings31 = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Siblings", "version": "1", "description": "References with siblings." },
          "paths": {},
          "components": {
            "schemas": {
              "Target": { "type": "object", "description": "Target." },
              "Holder": {
                "type": "object",
                "properties": { "p": { "$ref": "#/components/schemas/Target", "description": "Local text." } }
              }
            }
          }
        }
        """;

    private static async Task<(int ExitCode, List<JsonNode> Violations)> ValidateAsync(string fileName, string content)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDirectory();
        var spec = Path.Combine(directory.Path, fileName);
        await File.WriteAllTextAsync(spec, content, ct);

        var result = await CliRunner.RunAsync(["validate", "--spec", spec], directory.Path, ct);
        result.ExitCode.Should().NotBe(2, because: result.StdErr);

        var violations = JsonNode.Parse(result.StdOut)!["violations"]!.AsArray()
            .Where(v => (string?)v!["rule"] == Rule)
            .Select(v => v!)
            .ToList();
        return (result.ExitCode, violations);
    }

    public static TheoryData<string, string> SiblingFiles => new()
    {
        { "siblings.json", WithSiblings30 },
        { "siblings.yaml", WithSiblings30Yaml },
    };

    [Theory]
    [MemberData(nameof(SiblingFiles))]
    public async Task File30_WithSiblingsNextToRef_IsAnErrorAtEachPlace(string fileName, string content)
    {
        var (exitCode, violations) = await ValidateAsync(fileName, content);

        violations.Select(v => v["jsonPointer"]!.GetValue<string>()).Should().BeEquivalentTo(SiblingPlaces);
        violations.Should().AllSatisfy(v => v["severity"]!.GetValue<string>().Should().BeEquivalentTo("error"));
        exitCode.Should().Be(1, because: "an error-severity violation fails validation");
    }

    [Fact]
    public async Task File30_WithoutSiblingsNextToRef_HasNoViolation()
    {
        var (_, violations) = await ValidateAsync("clean.json", WithoutSiblings30);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task File31_WithSiblingsNextToRef_HasNoViolation()
    {
        var (_, violations) = await ValidateAsync("siblings31.json", WithSiblings31);

        violations.Should().BeEmpty();
    }
}
