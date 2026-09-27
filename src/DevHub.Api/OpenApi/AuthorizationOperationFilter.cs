using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DevHub.Api.OpenApi;

/// <summary>
/// Advertise the credentials contract on every endpoint that actually needs credentials.
/// <para>
/// The document carries a Bearer requirement for every operation; this filter takes it away again
/// from the endpoints that are deliberately public, so clients stop sending tokens to /api/auth/login
/// and /health. On the endpoints that keep the requirement it records the 401 for "who are you" and
/// the 403 for "you are not allowed here" that permission checks in the application layer return.
/// </para>
/// </summary>
public sealed class AuthorizationOperationFilter : IOperationFilter
{
    private const string UnauthorizedDescription =
        "Missing, malformed, expired or revoked credentials. The body uses the application/problem+json " +
        "media type with errorCode \"unauthorized\".";

    private const string ForbiddenDescription =
        "Authenticated, but the account lacks the permission this endpoint requires. The body uses the " +
        "application/problem+json media type with errorCode \"forbidden\".";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata is null)
        {
            return;
        }

        // [AllowAnonymous] wins over a controller level [Authorize], so those operations stay public
        // and must not advertise a credential requirement they do not enforce. An empty requirement
        // array is how OpenAPI opts an operation out of the document wide one.
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [];
            return;
        }

        if (!metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Responses ??= [];

        if (!operation.Responses.ContainsKey("401"))
        {
            operation.Responses["401"] = new OpenApiResponse { Description = UnauthorizedDescription };
        }

        if (!operation.Responses.ContainsKey("403"))
        {
            operation.Responses["403"] = new OpenApiResponse { Description = ForbiddenDescription };
        }
    }
}
