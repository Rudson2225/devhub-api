using DevHub.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected async Task ValidateAsync<T>(T request, CancellationToken cancellationToken = default)
    {
        var validator = HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null)
        {
            return;
        }

        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => string.IsNullOrEmpty(g.Key) ? "request" : g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        throw new DevHub.Application.Common.Exceptions.ValidationException(errors);
    }
}
