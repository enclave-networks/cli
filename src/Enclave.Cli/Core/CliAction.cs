using System.CommandLine;
using System.CommandLine.Invocation;

namespace Enclave.Cli.Core;

/// <summary>
/// Runs a command: reports parse errors, runs the declared checks, runs the handler, and turns any
/// failure into the one JSON error on stderr and its exit code.
/// </summary>
internal sealed class CliAction : AsynchronousCommandLineAction
{
    private readonly CliVerb? _verb;

    private readonly Func<CliContext, Task>? _handler;

    public CliAction(CliVerb verb)
    {
        _verb = verb;
    }

    /// <summary>
    /// An action for a command that is not a verb: the root, which prints help, and a noun, which
    /// needs a verb.
    /// </summary>
    public CliAction(Func<CliContext, Task> handler)
    {
        _handler = handler;
    }

    // System.CommandLine runs its own ParseErrorAction, which writes text and help and exits 1,
    // unless the action of the command parsed clears parse errors
    // (ParseOperation.ValidateAndAddDefaultResults, System.CommandLine 2.0.12). This action clears
    // them and reports them itself, as JSON with exit 2 (ParseProblems).
    public override bool ClearsParseErrors => true;

    public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        var verbose = _verb is not null && parseResult.GetResult(_verb.VerboseOption) is { Implicit: false };
        var dryRun = _verb?.DryRunOption is { } dryRunOption && parseResult.GetResult(dryRunOption) is { Implicit: false } ? new DryRun() : null;
        var output = new CliOutput(parseResult.InvocationConfiguration.Output, parseResult.InvocationConfiguration.Error, verbose, dryRun is not null);
        using var context = new CliContext(parseResult, CliRootCommand.HostOf(parseResult), output, _verb, dryRun, cancellationToken);

        try
        {
            if (ParseProblems.Find(parseResult) is { } parseError)
            {
                throw parseError;
            }

            if (_verb is not null)
            {
                foreach (var check in _verb.Checks)
                {
                    check(context);
                }

                await _verb.Handler(context);
            }
            else
            {
                await _handler!(context);
            }

            // Under --dry-run the command's own output would come from the answers the CLI gave its
            // changes (DryRun.Answer), which the API never sent, so the report is the only output
            // (proposed-cli-surface.md "Dry run").
            if (dryRun is not null)
            {
                await output.WriteDryRunAsync(dryRun.Report(), cancellationToken);
            }

            await output.FlushAsync();
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = ApiErrors.ToCliException(exception);

            if (error.InnerException is { } cause)
            {
                output.Verbose($"{cause.GetType().FullName}: {cause.Message}");
            }

            await output.WriteErrorAsync(error);
            return error.Code.ExitCode;
        }
    }
}
