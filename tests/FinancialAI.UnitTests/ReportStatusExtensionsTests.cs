using FinancialAI.Domain;
using Xunit;

namespace FinancialAI.UnitTests;

public class ReportStatusExtensionsTests
{
    [Theory]
    [InlineData(ReportStatus.Pending, "pending")]
    [InlineData(ReportStatus.Running, "running")]
    [InlineData(ReportStatus.NeedsReview, "needs_review")]
    [InlineData(ReportStatus.Completed, "completed")]
    [InlineData(ReportStatus.Failed, "failed")]
    public void ToWireString_ReturnsExpectedValue(ReportStatus status, string expected)
    {
        Assert.Equal(expected, status.ToWireString());
    }

    [Fact]
    public void EveryEnumMember_HasAWireString_NoThrow()
    {
        // Guards against a new enum member being added without updating
        // ToWireString. Without this test, the throw would surface only at
        // runtime, potentially deep in a workflow. This test forces the
        // update to happen at compile-and-test time.
        foreach (ReportStatus status in Enum.GetValues<ReportStatus>())
        {
            var result = status.ToWireString();
            Assert.False(string.IsNullOrEmpty(result));
            Assert.Equal(result.ToLowerInvariant(), result);
            Assert.DoesNotContain(" ", result);
        }
    }
}