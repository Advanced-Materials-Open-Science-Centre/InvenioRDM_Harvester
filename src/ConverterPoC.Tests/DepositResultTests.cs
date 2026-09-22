namespace ConverterPoC.Tests;

public class DepositResultTests
{
    // The deposit log Crossref returned for the original ej8xg-c9628 book deposit
    private const string FailureLog = """
        <?xml version="1.0" encoding="UTF-8"?>
        <doi_batch_diagnostic status="completed" sp="ip-10-4-0-83.ec2.internal">
           <submission_id>1769102028</submission_id>
           <batch_id>20260922204151-13d94b0d86b9651c76a001b3a23a17c7d8ff80d6</batch_id>
           <record_diagnostic status="Failure">
              <doi />
              <msg>Error validating schema crossref5.3.1.xsd : Error: cvc-complex-type.2.4.a: Invalid content was found starting with element '{"http://www.crossref.org/schema/5.3.1":publisher}'. One of '{"http://www.crossref.org/schema/5.3.1":publication_date, "http://www.crossref.org/schema/5.3.1":acceptance_date, "http://www.crossref.org/schema/5.3.1":isbn, "http://www.crossref.org/schema/5.3.1":noisbn}' is expected.
        </msg>
           </record_diagnostic>
           <batch_data>
              <record_count>1</record_count>
              <success_count>0</success_count>
              <warning_count>0</warning_count>
              <failure_count>1</failure_count>
           </batch_data>
        </doi_batch_diagnostic>
        """;

    private const string SuccessLog = """
        <?xml version="1.0" encoding="UTF-8"?>
        <doi_batch_diagnostic status="completed" sp="test">
           <submission_id>1</submission_id>
           <batch_id>20260923000000-abc</batch_id>
           <record_diagnostic status="Success">
              <doi>10.15330/dataset.26.09.01</doi>
              <msg>Successfully added</msg>
           </record_diagnostic>
           <batch_data>
              <record_count>1</record_count>
              <success_count>1</success_count>
              <warning_count>0</warning_count>
              <failure_count>0</failure_count>
           </batch_data>
        </doi_batch_diagnostic>
        """;

    [Fact]
    public void FailureLog_IsCompletedAndFailed()
    {
        var result = DepositResult.Parse(FailureLog);

        Assert.True(result.IsCompleted);
        Assert.False(result.Succeeded);
        Assert.Equal(1, result.FailureCount);

        var record = Assert.Single(result.Records);
        Assert.Equal("Failure", record.Status);
        Assert.Equal("", record.Doi);
        Assert.Contains("noisbn", record.Message);
        Assert.EndsWith("is expected.", record.Message);
    }

    [Fact]
    public void SuccessLog_IsCompletedAndSucceeded()
    {
        var result = DepositResult.Parse(SuccessLog);

        Assert.True(result.Succeeded);
        Assert.Equal(new RecordDiagnostic("Success", "10.15330/dataset.26.09.01", "Successfully added"), Assert.Single(result.Records));
    }

    [Theory]
    [InlineData("""<doi_batch_diagnostic status="queued"><batch_id>x</batch_id></doi_batch_diagnostic>""")]
    [InlineData("""<doi_batch_diagnostic status="in_process" />""")]
    public void UnprocessedLog_IsNotCompleted(string response)
    {
        var result = DepositResult.Parse(response);

        Assert.False(result.IsCompleted);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Wrong credentials. Incorrect username or password.")]
    [InlineData("<html><body>Error</body></html>")]
    [InlineData("")]
    public void OtherResponses_AreRejected(string response)
    {
        Assert.Throws<FormatException>(() => DepositResult.Parse(response));
    }
}
