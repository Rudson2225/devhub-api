using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DevHub.Api.OpenApi;

/// <summary>
/// Places the Bearer requirement on the document root once the operations have been generated.
/// <para>
/// The generated requirement has to be built from the finished document: a scheme reference created
/// without its host document serialises as an empty object, which would tell clients that "nothing"
/// is required rather than the Bearer token. Operations marked public by
/// <see cref="AuthorizationOperationFilter"/> already carry an empty requirement array and therefore
/// opt out of this one.
/// </para>
/// </summary>
public sealed class AuthorizationDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        if (document.Components?.SecuritySchemes is not { Count: > 0 } schemes
            || !schemes.ContainsKey("Bearer"))
        {
            return;
        }

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Bearer", document), [] }
            }
        ];
    }
}
