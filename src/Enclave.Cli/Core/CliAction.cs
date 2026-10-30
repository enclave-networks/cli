using System.CommandLine;
using System.CommandLine.Invocation;

namespace Enclave.Cli.Core;

internal sealed class CliAction : AsynchronousCommandLineAction
{
    private readonly CliVerb? _verb;

    private readonly Func<CliContext, Task>? _handler;

    public CliAction(CliVerb verb)
    {
        _verb = verb;
    }

    /// <summary>
    /// For a command that is not a verb: the root or a noun.
    /// </summary>
    public CliAction(Func<CliContext, Task> handler)
    {
        _handler = handler;
    }

    // Otherwise System.CommandLine runs its own ParseErrorAction, which writes text and exits 1
    // (System.CommandLine 2.0.12, ParseOperation.ValidateAndAddDefaultResults). ParseProblems
    // reports them as JSON with exit 2.
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
