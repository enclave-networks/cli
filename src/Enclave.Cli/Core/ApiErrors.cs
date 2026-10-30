using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Enclave.Sdk.Api.Exceptions;

namespace Enclave.Cli.Core;

internal static class ApiErrors
{
    // Returns a CliException unchanged. With notFoundIsUnknownItem, a 404 means the item the
    // command named does not exist (exit 5); without it, a 404 is an API error (exit 1).
    public static CliException ToCliException(Exception exception, bool notFoundIsUnknownItem = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case CliException cli:
                return cli;

            // Enclave.Sdk.Api throws EnclaveApiException only for application/problem+json responses
            // (ProblemDetailsHttpMessageHandler).
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

            // Any other failed response, such as a proxy's 401 or 502, comes from
            // EnsureSuccessStatusCode (PatchClient.ApplyAsync, HttpClient.GetFromJsonAsync), and maps
            // to the same codes as the API's own.
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

            // No status means no response, which a retry can fix. A TLS failure also has no status
            // (HttpRequestError.SecureConnectionError) and gets the same code, though a retry does
            // not fix it (spec/cli-surface.md "Accepted for now").
            case HttpRequestException http:
                return new CliException(ErrorCode.Transient, $"The Enclave API could not be reached: {http.Message}", innerException: http);

            // A failure while the body is read comes after SendAsync has returned
            // (HttpClient.GetFromJsonAsync reads with ResponseHeadersRead), so no HttpRequestException
            // wraps it: .NET throws HttpIOException for a body that ends early, and an IOException
            // around a SocketException for a reset (NetworkStream.ReadAsync; seen with .NET 10.0.1).
            // Another IOException, such as a write to a closed stdout pipe, stays api_error.
            case HttpIOException io:
                return new CliException(ErrorCode.Transient, $"The connection to the Enclave API failed while its response was read: {io.Message}", innerException: io);

            case IOException { InnerException: SocketException } io:
                return new CliException(ErrorCode.Transient, $"The connection to the Enclave API failed while its response was read: {io.Message}", innerException: io);

            // HttpClient reports its timeout as a TaskCanceledException around a TimeoutException
            // (.NET 5 and later).
            case TaskCanceledException { InnerException: TimeoutException } timeout:
                return new CliException(ErrorCode.Transient, "The Enclave API did not answer in time.", innerException: timeout);

            case JsonException json:
                return new CliException(ErrorCode.ApiError, $"The Enclave API sent a response the CLI could not read: {json.Message}", innerException: json);

            default:
                return new CliException(ErrorCode.ApiError, $"{exception.GetType().Name}: {exception.Message}", innerException: exception);
        }
    }

    // True when the API answered 404, with problem details or without.
    public static bool IsNotFound(Exception exception) => exception switch
    {
        EnclaveApiException api => (api.ProblemDetails?.Status ?? (int)api.Response.StatusCode) == (int)HttpStatusCode.NotFound,
        HttpRequestException http => http.StatusCode == HttpStatusCode.NotFound,
        _ => false,
    };

    // spec/cli-surface.md "Errors and exit codes"
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
