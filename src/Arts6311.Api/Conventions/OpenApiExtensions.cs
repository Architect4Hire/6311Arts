using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Arts6311.Api.Conventions;

internal static class OpenApiExtensions
{
    /// <summary>The document name, and so the route: <c>/openapi/v1.json</c>. It matches the <c>/api/v1</c> prefix.</summary>
    public const string DocumentName = "v1";

    /// <summary>The component every error response points at.</summary>
    private const string ProblemSchemaId = "ProblemDetails";

    private const string ProblemMediaType = "application/problem+json";

    /// <summary>
    /// Registers the <c>v1</c> OpenAPI document. The document is the API contract: it is snapshotted at
    /// <c>src/Arts6311.Tests/Api/Snapshots/openapi-v1.json</c> and <c>OpenApiContractTests</c> fails on any shape
    /// change until the snapshot is regenerated in the same change (<c>api-contract.md</c> "Compatibility").
    /// </summary>
    public static IServiceCollection AddArtsOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options => options
            .AddOperationTransformer(AddStandardErrorResponses)
            .AddOperationTransformer(StreamedMultipartUploadAttribute.Describe)
            .AddSchemaTransformer(NumbersAreNumbers)
            .AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "6311arts API";
                document.Info.Version = DocumentName;

                // The servers list is whatever host answered the request, which would make the snapshot depend on
                // the port the test server happened to get. The contract is the paths and schemas, not where it is
                // hosted — and the real base URL is the gateway's, not the API's (ADR 0001).
                document.Servers?.Clear();

                DefineProblemSchema(document);

                return Task.CompletedTask;
            }));

        return services;
    }

    /// <summary>
    /// Takes <c>string</c> off every numeric schema, and with it the digits-only pattern that comes along.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host serializes with <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>, which <em>reads</em>
    /// quoted numbers as a leniency, so the generator described every integer as
    /// <c>["null", "integer", "string"]</c> — money included. Nothing we write ever emits a quoted number, so the
    /// document was promising a shape the API does not produce, and a generated client would have typed
    /// <c>startingPriceCents</c> as <c>number | string</c> (api-contract review).
    /// </para>
    /// <para>
    /// This corrects what is <em>promised</em>, not what is accepted: a client that sends <c>"100"</c> is still
    /// read, because narrowing accepted values would be a breaking change (api-contract.md "Compatibility"). The
    /// promise has to be corrected before any client is generated from this document, since taking a type away
    /// afterwards is the breaking change.
    /// </para>
    /// </remarks>
    private static Task NumbersAreNumbers(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var numeric = JsonSchemaType.Integer | JsonSchemaType.Number;
        if (schema.Type is not { } type || (type & numeric) == 0 || (type & JsonSchemaType.String) == 0)
        {
            return Task.CompletedTask;
        }

        schema.Type = type & ~JsonSchemaType.String;

        // The pattern only existed to constrain the string form, and a number cannot match a pattern anyway.
        schema.Pattern = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Documents the error responses every route can return, once, instead of each controller repeating them.
    /// </summary>
    /// <remarks>
    /// Without this the document claims each route only ever succeeds: the failures come from the shared pipeline
    /// (<c>UseStatusCodePages</c>, <c>UseExceptionHandler</c>, the authorization middleware), not from
    /// <c>[ProducesResponseType]</c> attributes. Doing it here also keeps a per-action variant from drifting as
    /// routes are added.
    /// </remarks>
    private static Task AddStandardErrorResponses(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= [];

        // Anything can fault, so every operation can answer 500.
        Add("500", "An unexpected fault. The cause is in the logs under the correlationId, never in the response.");

        // An anonymous route cannot answer 401 or 403, and saying it could would be misleading.
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            Add("401", "No internal token, or one the API does not accept.");
            Add("403", "The token is valid but not allowed to make this request.");
        }

        return Task.CompletedTask;

        void Add(string status, string description)
        {
            if (operation.Responses!.ContainsKey(status))
            {
                return;
            }

            operation.Responses[status] = new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    // A reference, not an inline schema: one shape in one place, and the component below is where
                    // `code` and `correlationId` get described.
                    [ProblemMediaType] = new() { Schema = new OpenApiSchemaReference(ProblemSchemaId) },
                },
            };
        }
    }

    /// <summary>
    /// Writes the <c>ProblemDetails</c> component, including <c>code</c> and <c>correlationId</c>.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than generated from the type, because both fields live in
    /// <c>ProblemDetails.Extensions</c> and a generator can only see the dictionary property, not what we put in
    /// it. They are the one pair of fields every client branches on, so the document has to name them — including
    /// for the Angular models that <c>api-contract-checker</c> compares against this document.
    /// </remarks>
    private static void DefineProblemSchema(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        document.Components.Schemas[ProblemSchemaId] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "RFC 9457 problem details, with the 6311arts error code and correlation id.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "status", "code", "correlationId" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description =
                        "Stable, machine-readable error code (api-contract.md). Branch on this, not on the status.",
                },
                ["correlationId"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description =
                        "The request's W3C trace id as 32 lower-case hex digits, for support and log lookup.",
                },
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object | JsonSchemaType.Null,
                    Description = "Present on validation.failed: messages keyed by camelCase field name.",
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                },
            },
        };
    }
}
