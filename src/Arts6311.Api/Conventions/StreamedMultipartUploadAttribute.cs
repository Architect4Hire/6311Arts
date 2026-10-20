using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Arts6311.Api.Conventions;

/// <summary>
/// Marks an action that reads its multipart body itself, as a stream, instead of binding an <c>IFormFile</c>
/// (which would buffer the whole file before the action runs). Model binding then sees no parameters, so the
/// OpenAPI request body is described from this attribute instead (<see cref="Describe"/>).
/// </summary>
/// <remarks>
/// It is also the resource filter that makes streaming possible: MVC's form value providers would otherwise read
/// (and buffer) the whole form while binding the action's other parameters — even just its
/// <see cref="CancellationToken"/> — and leave the action an empty body.
/// </remarks>
/// <param name="fileField">The form field carrying the file.</param>
/// <param name="textFields">Small text fields, which must come before the file in the body.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class StreamedMultipartUploadAttribute(string fileField, params string[] textFields) : Attribute, IResourceFilter
{
    public string FileField { get; } = fileField;

    public IReadOnlyList<string> TextFields { get; } = textFields;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    /// <summary>The operation transformer that writes the <c>multipart/form-data</c> request body.</summary>
    public static Task Describe(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var upload = context.Description.ActionDescriptor.EndpointMetadata.OfType<StreamedMultipartUploadAttribute>().FirstOrDefault();
        if (upload is null)
        {
            return Task.CompletedTask;
        }

        var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (var field in upload.TextFields)
        {
            properties[field] = new OpenApiSchema { Type = JsonSchemaType.String };
        }

        properties[upload.FileField] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Text fields first, then the file. Fields after the file are ignored.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["multipart/form-data"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string>(StringComparer.Ordinal) { upload.FileField },
                        Properties = properties,
                    },
                },
            },
        };

        return Task.CompletedTask;
    }
}
