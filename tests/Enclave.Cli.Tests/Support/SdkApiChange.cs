namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Items of proposed-cli-surface.md "Enclave.Sdk.Api changes" that a test case needs, for
/// <see cref="TestCategory.PendingOn"/>.
/// </summary>
internal static class SdkApiChange
{
    // key delete exits not_implemented and sends nothing (src/Enclave.Cli/Commands/Key/KeyBulkCommands.cs);
    // the enrolment key delete calls it needs are in Enclave.Sdk.Api 1.1.0.
    public const string KeyDelete = "4. Enrolment key delete, single and bulk, for key delete.";
}
