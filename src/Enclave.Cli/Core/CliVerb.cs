using System.CommandLine;

namespace Enclave.Cli.Core;

/// <summary>
/// A command that runs: a noun's verb, such as `system list`, or a top-level command such as
/// `login` or `log`. It takes the options its scope gives it (--org and --org-id, --partner-id),
/// --dry-run when it changes something through the API, and --verbose. Options and arguments are
/// added with Add, the checks between them declared with Exclusive, ExactlyOne, Requires and Check,
/// and the work set with SetHandler.
/// </summary>
internal sealed class CliVerb : Command
{
    private readonly List<Action<CliContext>> _checks = [];

    private readonly List<Option> _commonOptions = [];

    private Func<CliContext, Task> _handler = _ => throw CliErrors.NotImplemented("This command has no handler.");

    public CliVerb(string name, string description, CommandScope scope = CommandScope.None, bool changes = false)
        : base(name, description)
    {
        Scope = scope;

        if (scope.HasFlag(CommandScope.Organisation))
        {
            OrgOption = Common(CliOptions.Text("--org", "The organisation to act in, by name. Overrides ENCLAVE_ORG, ENCLAVE_ORG_ID and the default `org use` saved.", "name"));
            OrgIdOption = Common(CliOptions.Id("--org-id", "The organisation to act in, by ID. Overrides ENCLAVE_ORG, ENCLAVE_ORG_ID and the default `org use` saved.", IdFormats.Guid));
            Exclusive(OrgOption, OrgIdOption);
        }

        if (scope.HasFlag(CommandScope.Partner))
        {
            PartnerIdOption = Common(CliOptions.Id("--partner-id", "The partner to act for, by ID. Overrides ENCLAVE_PARTNER_ID and the default `partner use` saved.", IdFormats.Guid));
        }

        if (changes)
        {
            DryRunOption = Common(CliOptions.Flag("--dry-run", "Print the requests the command would send, and send none of them."));
        }

        VerboseOption = Common(CliOptions.Flag("--verbose", "Write diagnostics to stderr."));
        Action = new CliAction(this);
    }

    public CommandScope Scope { get; }

    /// <summary>
    /// --org, when the command acts within an organisation.
    /// </summary>
    public Option<string?>? OrgOption { get; }

    /// <summary>
    /// --org-id, when the command acts within an organisation.
    /// </summary>
    public Option<Guid?>? OrgIdOption { get; }

    /// <summary>
    /// --partner-id, when the command acts for a partner.
    /// </summary>
    public Option<Guid?>? PartnerIdOption { get; }

    /// <summary>
    /// --dry-run, when the command changes something through the API.
    /// </summary>
    public Option<bool>? DryRunOption { get; }

    public Option<bool> VerboseOption { get; }

    /// <summary>
    /// The options every command of the scope takes, which help and `commands` list after the
    /// command's own.
    /// </summary>
    public IReadOnlyList<Option> CommonOptions => _commonOptions;

    internal Func<CliContext, Task> Handler => _handler;

    internal IReadOnlyList<Action<CliContext>> Checks => _checks;

    /// <summary>
    /// Adds an option and returns it, keeping its type for <see cref="CliContext.Get{T}(Option{T})"/>.
    /// </summary>
    /// <typeparam name="TOption">The option's type.</typeparam>
    public TOption Add<TOption>(TOption option)
        where TOption : Option
    {
        ArgumentNullException.ThrowIfNull(option);

        Options.Add(option);
        return option;
    }

    /// <summary>
    /// Adds a positional argument and returns it.
    /// </summary>
    /// <typeparam name="T">The argument's value type.</typeparam>
    public Argument<T> Add<T>(Argument<T> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        Arguments.Add(argument);
        return argument;
    }

    /// <summary>
    /// The work the command does once its command line has parsed and its checks have passed. A
    /// handler checks its remaining arguments first, then asks the context for the organisation or
    /// partner, then makes its calls, and reports a failure by throwing a <see cref="CliException"/>.
    /// </summary>
    public void SetHandler(Func<CliContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
    }

    /// <summary>
    /// At most one of these options or arguments may be given; two contradict each other and exit 2
    /// (proposed-cli-surface.md "Details": --for with --until, a name with its ID option).
    /// </summary>
    public void Exclusive(params Symbol[] symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        _checks.Add(context =>
        {
            var given = symbols.Where(context.IsGiven).ToArray();

            if (given.Length > 1)
            {
                throw CliErrors.InvalidArgument(KeyOf(given[1]), $"{Describe(given[0])} and {Describe(given[1])} cannot be given together.");
            }
        });
    }

    /// <summary>
    /// Exactly one of these must be given: a name or its --id. None, or more than one, exits 2.
    /// </summary>
    public void ExactlyOne(params Symbol[] symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        Exclusive(symbols);
        _checks.Add(context =>
        {
            if (!symbols.Any(context.IsGiven))
            {
                throw CliErrors.InvalidArgument(KeyOf(symbols[0]), $"Give {string.Join(" or ", symbols.Select(Describe))}.");
            }
        });
    }

    /// <summary>
    /// At least one of these must be given: an update with no change flag exits 2
    /// (proposed-cli-surface.md "Details").
    /// </summary>
    public void AtLeastOne(params Symbol[] symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        _checks.Add(context =>
        {
            if (!symbols.Any(context.IsGiven))
            {
                throw CliErrors.InvalidArgument(KeyOf(symbols[0]), $"Give at least one of: {string.Join(", ", symbols.Select(Describe))}.");
            }
        });
    }

    /// <summary>
    /// <paramref name="dependent"/> may be given only with at least one of
    /// <paramref name="anyOf"/>: --then needs --for or --until.
    /// </summary>
    public void Requires(Symbol dependent, params Symbol[] anyOf)
    {
        ArgumentNullException.ThrowIfNull(dependent);
        ArgumentNullException.ThrowIfNull(anyOf);

        _checks.Add(context =>
        {
            if (context.IsGiven(dependent) && !anyOf.Any(context.IsGiven))
            {
                throw CliErrors.InvalidArgument(KeyOf(dependent), $"{Describe(dependent)} needs {string.Join(" or ", anyOf.Select(Describe))}.");
            }
        });
    }

    /// <summary>
    /// Another check on the arguments alone, run with the declared checks before the handler and
    /// before the token is read. It throws a <see cref="CliException"/> to refuse the command.
    /// </summary>
    public void Check(Action<CliContext> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        _checks.Add(check);
    }

    /// <summary>
    /// How an option or argument is named in messages: --until, or &lt;name&gt;.
    /// </summary>
    public static string Describe(Symbol symbol) => symbol switch
    {
        Argument argument => $"<{argument.Name}>",
        _ => symbol.Name,
    };

    /// <summary>
    /// The key an error about an option or argument goes under in errors.
    /// </summary>
    public static string KeyOf(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        return symbol.Name;
    }

    private TOption Common<TOption>(TOption option)
        where TOption : Option
    {
        _commonOptions.Add(option);
        return Add(option);
    }
}
