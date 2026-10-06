namespace Enclave.Cli.Tests.Support;

/// <summary>
/// NUnit categories the CI workflow filters on.
/// </summary>
internal static class TestCategory
{
    // Tests in this category state behaviour from proposed-cli-surface.md that the CLI does not
    // meet. CI runs every test outside it (.github/workflows/ci.yml, --filter "TestCategory!=Pending"),
    // so a pull request shows whether behaviour that is built still works. A fixture or test leaves
    // the category once the CLI meets it, and from then on CI guards it.
    public const string Pending = "Pending";
}
