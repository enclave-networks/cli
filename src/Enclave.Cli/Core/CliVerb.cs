using System.CommandLine;
using Enclave.Cli.Context;

namespace Enclave.Cli.Core;

// A command that runs: a noun's verb, or a top-level command such as `login`.
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
            RefuseBlank(OrgOption, "an organisation name");
        }

        if (scope.HasFlag(CommandScope.Partner))
        {
            PartnerIdOption = Common(CliOptions.Id("--partner-id", "The partner to act for, by ID. Overrides ENCLAVE_PARTNER_ID and the default `partner use` saved.", IdFormats.Guid));
        }

        if (changes)
        {
            DryRunOption = Common(CliOptions.Flag("--dry-run", "Print the requests the command would send, and send none of them."));
        }

        Action = new CliAction(this);
    }

    public CommandScope Scope { get; }

    public Option<string?>? OrgOption { get; }

    public Option<Guid?>? OrgIdOption { get; }

    public Option<Guid?>? PartnerIdOption { get; }

    public Option<bool>? DryRunOption { get; }

    // Help and `commands` list these after the command's own options.
    public IReadOnlyList<Option> CommonOptions => _commonOptions;

    internal Func<CliContext, Task> Handler => _handler;

    internal IReadOnlyList<Action<CliContext>> Checks => _checks;

    public TOption Add<TOption>(TOption option)
        where TOption : Option
    {
        ArgumentNullException.ThrowIfNull(option);

        Options.Add(option);
        return option;
    }

    public Argument<T> Add<T>(Argument<T> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        Arguments.Add(argument);
        return argument;
    }

    // The handler runs after the checks pass. It checks its remaining arguments before asking for
    // the organisation or partner, and reports a failure by throwing a CliException.
    public void SetHandler(Func<CliContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
    }

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

    // Runs before the handler and before the token is read; throws a CliException to refuse the
    // command.
    public void Check(Action<CliContext> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        _checks.Add(check);
    }

    // A blank value names nothing. Checked with the arguments, it exits 2 before the token is read, so
    // the command line fails the same way with a token and without one ("Details"). takes is what the
    // value names, such as "an email address".
    public void RefuseBlank(Option<string?> option, string takes) => RefuseBlank(option, context => context.Get(option), takes);

    public void RefuseBlank(Symbol symbol, Func<CliContext, string?> value, string takes) =>
        Check(context =>
        {
            if (value(context) is { } given && string.IsNullOrWhiteSpace(given))
            {
                throw CliErrors.InvalidArgument(KeyOf(symbol), $"{Describe(symbol)} takes {takes}, and the value given is blank.");
            }
        });

    public static string Describe(Symbol symbol) => symbol switch
    {
        Argument argument => $"<{argument.Name}>",
        _ => symbol.Name,
    };

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
