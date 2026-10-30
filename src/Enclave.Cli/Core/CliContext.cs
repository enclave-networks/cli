using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Core;

/// <summary>
/// Resolves the token, organisation and partner when the handler first asks for each.
/// </summary>
internal sealed class CliContext : IDisposable
{
    private ApiAccess? _access;

    private EnclaveClient? _client;

    private ApiRequestHandler? _handler;

    public CliContext(ParseResult parseResult, CliHost host, CliOutput output, CliVerb? verb, DryRun? dryRun, CancellationToken cancellationToken)
    {
        ParseResult = parseResult;
        Host = host;
        Output = output;
        Verb = verb;
        DryRun = dryRun;
        CancellationToken = cancellationToken;
    }

    public ParseResult ParseResult { get; }

    public CliHost Host { get; }

    public CliOutput Output { get; }

    /// <summary>
    /// The command being run, or null for the root and the nouns.
    /// </summary>
    public CliVerb? Verb { get; }

    /// <summary>
    /// Null without --dry-run.
    /// </summary>
    public DryRun? DryRun { get; }

    public CancellationToken CancellationToken { get; }

    public bool IsDryRun => DryRun is not null;

    public T? Get<T>(Option<T> option) => ParseResult.GetValue(option);

    public T? Get<T>(Argument<T> argument) => ParseResult.GetValue(argument);

    public bool IsGiven(Symbol symbol) => ParseResult.GetResult(symbol) switch
    {
        System.CommandLine.Parsing.OptionResult option => !option.Implicit,
        System.CommandLine.Parsing.ArgumentResult argument => argument.Tokens.Count > 0,
        System.CommandLine.Parsing.CommandResult => true,
        _ => false,
    };

    /// <summary>
    /// Never pass the token.
    /// </summary>
    public void Verbose(string message) => Output.Verbose(message);

    /// <summary>
    /// Exits 3 with token_missing when there is no token.
    /// </summary>
    public ApiAccess Access
    {
        get
        {
            if (_access is null)
            {
                _access = ApiAccess.Resolve(Host);
                Verbose($"Token from {_access.TokenSource}; API at {_access.BaseUrl}.");
            }

            return _access;
        }
    }

    /// <summary>
    /// For calls outside an organisation. Exits 3 with token_missing when there is no token; makes no
    /// call.
    /// </summary>
    public EnclaveClient GetClient() => _client ??= CreateClient(Access);

    /// <summary>
    /// For login, which checks a token of its own; every other command uses <see cref="GetClient"/>.
    /// Makes no call.
    /// </summary>
    public EnclaveClient CreateClient(ApiAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);

        return access.CreateClient(_handler ??= new ApiRequestHandler(Output, DryRun, Host.CreateNetworkHandler?.Invoke()));
    }

    /// <summary>
    /// Call it after checking the command's own arguments, which come before the token
    /// (spec/cli-surface.md "Errors and exit codes"). Makes one lookup call unless the organisation is
    /// given by ID.
    /// </summary>
    public async Task<OrganisationInUse> GetOrganisationAsync()
    {
        var organisation = await OrganisationResolver.ResolveAsync(this, GetClient());

        if (DryRun is { } dryRun)
        {
            dryRun.Organisation = organisation;
        }

        return organisation;
    }

    /// <summary>
    /// Call it after checking the command's own arguments, as <see cref="GetOrganisationAsync"/>.
    /// Makes no call.
    /// </summary>
    public Task<PartnerInUse> GetPartnerAsync()
    {
        var client = GetClient();
        var choice = PartnerResolver.Resolve(this);
        Verbose($"Partner {choice.Id} from {choice.Source}.");

        var partner = new PartnerInUse(choice.Id, choice.Source, client.CreatePartnerClient(PartnerId.FromGuid(choice.Id)));

        if (DryRun is { } dryRun)
        {
            dryRun.Partner = partner;
        }

        return Task.FromResult(partner);
    }

    /// <summary>
    /// As <see cref="TimeInput.Forwards"/>, and exits 2 for a time that has passed
    /// (spec/cli-surface.md "Details").
    /// </summary>
    public DateTimeOffset FutureInstant(TimeInput input, Option option)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(option);

        var instant = input.Forwards(Host.Time, option.Name);

        return instant > Host.Time.GetUtcNow()
            ? instant
            : throw CliErrors.InvalidArgument(option.Name, $"The time given to {option.Name} has passed.");
    }

    /// <summary>
    /// Exits 2 naming <paramref name="option"/> when the result passes the end of year 9999.
    /// </summary>
    public DateTimeOffset FutureInstant(TimeSpan duration, Option option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return TimeInput.After(Host.Time.GetUtcNow(), duration, option.Name);
    }

    /// <summary>
    /// Null when neither option is given. Declare the two options exclusive, and --then as requiring
    /// one of them.
    /// </summary>
    public DateTimeOffset? ExpiryFrom(Option<TimeSpan?> forOption, Option<TimeInput?> untilOption)
    {
        ArgumentNullException.ThrowIfNull(forOption);
        ArgumentNullException.ThrowIfNull(untilOption);

        if (Get(forOption) is { } duration)
        {
            return FutureInstant(duration, forOption);
        }

        return Get(untilOption) is { } until ? FutureInstant(until, untilOption) : null;
    }

    public DateTimeOffset PastInstant(TimeInput input, Option option)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(option);

        return input.Backwards(Host.Time, option.Name);
    }

    /// <summary>
    /// Exits 2 naming <paramref name="option"/> when the result is before year 1. Call it before
    /// GetOrganisationAsync, as a check of the arguments (spec/cli-surface.md "Errors and exit codes").
    /// </summary>
    public DateTimeOffset PastInstant(TimeSpan duration, Option option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return TimeInput.Before(Host.Time.GetUtcNow(), duration, option.Name);
    }

    // Enclave.Sdk.Api never disposes a handler given in EnclaveClientOptions.HttpMessageHandler, and
    // the clients built here live for one command.
    public void Dispose() => _handler?.Dispose();
}
