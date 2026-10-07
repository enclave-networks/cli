using System.Globalization;
using System.Net;
using System.Text.Json;
using Enclave.Sdk.Api.Exceptions;

namespace Enclave.Cli.Core;

/// <summary>
/// Turns the exceptions an Enclave.Sdk.Api call can throw into the CLI's errors.
/// </summary>
internal static class ApiErrors
{
    /// <summary>
    /// The CLI error for an exception thrown by command code or by an Enclave.Sdk.Api call. A
    /// <see cref="CliException"/> is returned as it is.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="notFoundIsUnknownItem">Whether a 404 means the item the command named does not
    /// exist (a single-ID command, exit 5), or is an API error like any other status (exit 1).</param>
    public static CliException ToCliException(Exception exception, bool notFoundIsUnknownItem = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case CliException cli:
                return cli;

            // Enclave.Sdk.Api throws EnclaveApiException only for application/problem+json responses
            // (Handlers/ProblemDetailsHttpMessageHandler.cs:20, version 1.0.5), so the API's own
            // status, title, detail and field errors are passed through.
            case EnclaveApiException api:
            {
                var problem = api.ProblemDetails;
                var status = problem?.Status ?? (int)api.Response.StatusCode;
                var errors = problem?.Errors is { Count: > 0 } fieldErrors
                    ? fieldErrors.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal)
                    : null;

                return new CliException(
                    ForStatus(status, notFoundIsUnknownItem),
                    problem?.Detail ?? problem?.Title ?? api.Message,
                    status,
                    problem?.Title,
                    errors,
                    api);
            }

            // Any other response that is not a success reaches the CLI as HttpRequestException with
            // the status, from HttpResponseMessage.EnsureSuccessStatusCode, which Enclave.Sdk.Api calls
            // (PatchClient.ApplyAsync, HttpClient.GetFromJsonAsync). A proxy in front of the API
            // answers this way, so its 401 or 502 maps to the same codes as the API's own.
            case HttpRequestException { StatusCode: { } statusCode } http:
            {
                var status = (int)statusCode;

                return new CliException(
                    ForStatus(status, notFoundIsUnknownItem),
                    http.Message,
                    status,
                    string.Create(CultureInfo.InvariantCulture, $"HTTP {status} {statusCode}"),
                    innerException: http);
            }

            // No status means no response: the connection was refused or dropped, or name
            // resolution failed. Retrying can succeed.
            case HttpRequestException http:
                return new CliException(ErrorCode.Transient, $"The Enclave API could not be reached: {http.Message}", innerException: http);

            // HttpClient reports its timeout (HttpClient.Timeout, 100 seconds by default) as a
            // TaskCanceledException whose inner exception is a TimeoutException (.NET 5 and later).
            case TaskCanceledException { InnerException: TimeoutException } timeout:
                return new CliException(ErrorCode.Transient, "The Enclave API did not answer in time.", innerException: timeout);

            case JsonException json:
                return new CliException(ErrorCode.ApiError, $"The Enclave API sent a response the CLI could not read: {json.Message}", innerException: json);

            default:
                return new CliException(ErrorCode.ApiError, $"{exception.GetType().Name}: {exception.Message}", innerException: exception);
        }
    }

    /// <summary>
    /// Whether an Enclave.Sdk.Api call failed because the API answered 404, with problem details or
    /// without: for a read whose 404 decides what to do next, such as `tag set` choosing between
    /// the API's update and create calls.
    /// </summary>
    public static bool IsNotFound(Exception exception) => exception switch
    {
        EnclaveApiException api => (api.ProblemDetails?.Status ?? (int)api.Response.StatusCode) == (int)HttpStatusCode.NotFound,
        HttpRequestException http => http.StatusCode == HttpStatusCode.NotFound,
        _ => false,
    };

    /// <summary>
    /// The error code for an HTTP status the API answered with (proposed-cli-surface.md "Errors and
    /// exit codes").
    /// </summary>
    public static ErrorCode ForStatus(int status, bool notFoundIsUnknownItem = false) => status switch
    {
        (int)HttpStatusCode.Unauthorized => ErrorCode.TokenInvalid,
        (int)HttpStatusCode.Forbidden => ErrorCode.Forbidden,
        (int)HttpStatusCode.NotFound when notFoundIsUnknownItem => ErrorCode.NotFound,
        (int)HttpStatusCode.TooManyRequests => ErrorCode.Transient,
        >= 500 and < 600 => ErrorCode.Transient,
        _ => ErrorCode.ApiError,
    };
}
