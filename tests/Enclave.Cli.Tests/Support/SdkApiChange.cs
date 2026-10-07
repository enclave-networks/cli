namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Items of proposed-cli-surface.md "Needs Enclave.Sdk.Api changes" that a test case needs, for
/// <see cref="TestCategory.PendingOn"/>.
/// </summary>
internal static class SdkApiChange
{
    // IEnrolmentKeysClient in Enclave.Sdk.Api 1.0.5 has no enrolment key delete, single or bulk, so
    // key delete exits not_implemented and sends nothing (src/Enclave.Cli/Commands/Key/KeyBulkCommands.cs).
    public const string KeyDelete = "4. Enrolment key delete, single and bulk, for key delete.";

    // Enclave.Sdk.Api 1.0.5 does not check the status of the invite, cancel-invite and remove-user
    // responses (OrganisationClient.cs:91-133), so a failure the API reports without problem details
    // reaches the CLI as success.
    public const string StatusChecks = "6. Status checks on calls that pass a failure through as success when the response is not problem+json.";
}
