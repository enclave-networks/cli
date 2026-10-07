using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Core;

/// <summary>
/// What a command's handler works with: its parsed command line, the host, the output, and the
/// token, organisation and partner, each resolved when the handler first asks for it.
/// </summary>
internal sealed class CliContext
{
    private ApiAccess? _access;

    private EnclaveClient? _client;

    public CliContext(ParseResult parseResult, CliHost host, CliOutput output, CliVerb? verb, CancellationToken cancellationToken)
    {
        ParseResult = parseResult;
        Host = host;
        Output = output;
        Verb = verb;
        CancellationToken = cancellationToken;
    }

    public ParseResult ParseResult { get; }

    public CliHost Host { get; }

    public CliOutput Output { get; }

    /// <summary>
    /// The command being run, or null for the root and the nouns.
    /// </summary>
    public CliVerb? Verb { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Whether --dry-run was given.
    /// </summary>
    public bool IsDryRun => Verb?.DryRunOption is { } option && Get(option);

    /// <summary>
    /// The option's value, or its default (null, or false for a flag) when not given.
    /// </summary>
    /// <typeparam name="T">The option's value type.</typeparam>
    public T? Get<T>(Option<T> option) => ParseResult.GetValue(option);

    /// <summary>
    /// The argument's value, or its default (null) when not given.
    /// </summary>
    /// <typeparam name="T">The argument's value type.</typeparam>
    public T? Get<T>(Argument<T> argument) => ParseResult.GetValue(argument);

    /// <summary>
    /// Whether the command line gives this option, or at least one value for this argument.
    /// </summary>
    public bool IsGiven(Symbol symbol) => ParseResult.GetResult(symbol) switch
    {
        System.CommandLine.Parsing.OptionResult option => !option.Implicit,
        System.CommandLine.Parsing.ArgumentResult argument => argument.Tokens.Count > 0,
        System.CommandLine.Parsing.CommandResult => true,
        _ => false,
    };

    /// <summary>
    /// Writes a diagnostic line on stderr when --verbose is given. Never pass the token.
    /// </summary>
    public void Verbose(string message) => Output.Verbose(message);

    /// <summary>
    /// The token and API address. Exits 3 with token_missing when there is no token.
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
    /// The Enclave.Sdk.Api client for calls outside an organisation, such as GetOrganisationsAsync.
    /// Exits 3 with token_missing when there is no token. It makes no call.
    /// </summary>
    public EnclaveClient GetClient() => _client ??= Access.CreateClient();

    /// <summary>
    /// The organisation the command acts in, with its client (proposed-cli-surface.md "Context").
    /// Call it after checking the command's own arguments: checks run in the order arguments, the
    /// token, the organisation, then the call ("Errors and exit codes"). It makes one lookup call
    /// when the organisation is named or not chosen, and none when it is given by ID.
    /// </summary>
    public async Task<OrganisationInUse> GetOrganisationAsync()
    {
        RefuseDryRun();

        var client = GetClient();
        return await OrganisationResolver.ResolveAsync(this, client);
    }

    /// <summary>
    /// The partner a partner command acts for. Like <see cref="GetOrganisationAsync"/>, call it
    /// after checking the command's own arguments; it checks the token first, then the partner,
    /// and makes no call.
    /// </summary>
    public Task<PartnerInUse> GetPartnerAsync()
    {
        RefuseDryRun();

        _ = Access;
        var partner = PartnerResolver.Resolve(this);
        Verbose($"Partner {partner.Id} from {partner.Source}.");
        return Task.FromResult(partner);
    }

    /// <summary>
    /// The instant a time given to <paramref name="option"/> names, read forwards from now (a
    /// duration counts from now, a clock time is its next occurrence), as UTC. A time that has
    /// passed, or does not exist in the local time zone, exits 2 (proposed-cli-surface.md "Details").
    /// </summary>
    public DateTimeOffset FutureInstant(TimeInput input, Option option)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(option);

        var instant = input.Forwards(Host.Time)
            ?? throw CliErrors.InvalidArgument(option.Name, $"The time given to {option.Name} does not exist in the local time zone.");

        return instant > Host.Time.GetUtcNow()
            ? instant
            : throw CliErrors.InvalidArgument(option.Name, $"The time given to {option.Name} has passed.");
    }

    /// <summary>
    /// The expiry --for or --until gives, as a UTC instant, or null when neither is given. --for
    /// counts from now; --until is read as <see cref="FutureInstant"/> reads it, so a time that has
    /// passed exits 2. Declare the two options exclusive, and --then as requiring one of them.
    /// </summary>
    public DateTimeOffset? ExpiryFrom(Option<TimeSpan?> forOption, Option<TimeInput?> untilOption)
    {
        ArgumentNullException.ThrowIfNull(forOption);
        ArgumentNullException.ThrowIfNull(untilOption);

        if (Get(forOption) is { } duration)
        {
            return Host.Time.GetUtcNow() + duration;
        }

        return Get(untilOption) is { } until ? FutureInstant(until, untilOption) : null;
    }

    /// <summary>
    /// The instant a time given to <paramref name="option"/> names, read backwards from now (a
    /// duration counts back from now, a clock time is its latest occurrence), as UTC: log's --since.
    /// </summary>
    public DateTimeOffset PastInstant(TimeInput input, Option option)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(option);

        return input.Backwards(Host.Time)
            ?? throw CliErrors.InvalidArgument(option.Name, $"The time given to {option.Name} does not exist in the local time zone.");
    }

    // Capturing the request a change would send needs an HTTP handler option on
    // EnclaveClientOptions, which Enclave.Sdk.Api 1.0.5 lacks (proposed-cli-surface.md "Needs
    // Enclave.Sdk.Api changes", item 2). Until then --dry-run sends nothing and reports
    // not_implemented, after the command's own argument checks and before the token is read.
    private void RefuseDryRun()
    {
        if (IsDryRun)
        {
            throw CliErrors.NotImplemented("--dry-run needs an Enclave.Sdk.Api change: an HTTP handler option on EnclaveClientOptions, so the CLI can capture the requests a change would send without sending them. Nothing was sent.");
        }
    }
}
