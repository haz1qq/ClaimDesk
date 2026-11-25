using ClaimsApi.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Api;

/// <summary>Turns known exceptions into ProblemDetails responses with the right status code.</summary>
public sealed class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException bad)
            log.LogInformation("Rejected malformed request: {Reason}", bad.Message); // full detail stays server-side

        (int Status, string Title, string Detail)? problem = exception switch
        {
            ClaimRuleException { Kind: RuleViolation.Validation } e => (400, "Invalid request", e.Message),
            ClaimRuleException { Kind: RuleViolation.Forbidden } e => (403, "Not allowed", e.Message),
            ClaimRuleException { Kind: RuleViolation.Conflict } e => (409, "Not possible right now", e.Message),
            DbUpdateConcurrencyException => (409, "Claim was changed by someone else", "Reload the claim and try again."),
            // Bad JSON or an unknown enum value. The framework's message names internal types, so don't echo it.
            BadHttpRequestException e => (e.StatusCode, "Malformed request", "The request body or parameters could not be read."),
            _ => null, // anything else is a genuine 500
        };
        if (problem is null)
            return false;

        http.Response.StatusCode = problem.Value.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Status = problem.Value.Status, Title = problem.Value.Title, Detail = problem.Value.Detail },
        });
    }
}
