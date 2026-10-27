using System.Text.Json;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Validation.Tests.Support;
using Envoy.Extensions.AccessLoggers.File.V3;
using Envoy.Type.V3;
using Google.Protobuf;
using ProtoValidate;
using ProtoValidate.Exceptions;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Corpus_ValidInvalidAndBoundaryCases_MatchDeclaredOutcomes()
    {
        var results = ValidationCorpus.Evaluate(new Validator());
        Assert.Equal(52, results.Length);
        Assert.Equal(52, results.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var result in results)
        {
            Assert.True(result.ActualValid == result.ExpectedValid,
                $"{result.Id}: expected valid={result.ExpectedValid}, got valid={result.ActualValid}, violations={result.Violations}");
        }

        string output = Path.Combine(AppContext.BaseDirectory, "validation-corpus.json");
        var report = new { Results = results };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    [Fact]
    public void NaN_RangeViolation_IdentifiesFieldAndRule()
    {
        var result = new Validator().Validate(new Percent { Value = double.NaN }, failFast: false);
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("value", violation.Field.GetPath());
        Assert.Equal("double.gte_lte", violation.RuleId);
    }

    [Fact]
    public void Violations_NestedInvalidMessage_IdentifyFieldAndRule()
    {
        var validator = new Validator();
        var result = validator.Validate(new HeaderValueOption { Header = new HeaderValue() }, failFast: false);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Violations,
            violation => violation.Field.GetPath() == "header.key" && violation.RuleId == "string.min_len");
    }

    [Fact]
    public void FailFast_MultipleViolations_ReturnsOneInsteadOfAll()
    {
        var validator = new Validator();
        var message = new SocketAddress();
        var all = validator.Validate(message, failFast: false);
        var first = validator.Validate(message, failFast: true);
        Assert.Contains(all.Violations, violation => violation.Field.GetPath() == "port_specifier" && violation.RuleId == "required");
        Assert.Contains(all.Violations, violation => violation.Field.GetPath() == "address" && violation.RuleId == "string.min_len");
        var single = Assert.Single(first.Violations);
        Assert.Contains(all.Violations, violation => violation.Field.GetPath() == single.Field.GetPath() && violation.RuleId == single.RuleId);
    }

    [Fact]
    public void Validate_NullMessage_ThrowsArgumentNullException()
    {
        var validator = new Validator();
        var error = Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, failFast: false));
        Assert.Equal("message", error.ParamName);
    }

    [Fact]
    public void ClosedDescriptorSet_UnregisteredMessage_IsCompilationFault()
    {
        var validator = new Validator(new ValidatorOptions { DisableLazy = true });
        var error = Assert.Throws<CompilationException>(() => validator.Validate(new Percent { Value = 50 }, failFast: false));
        Assert.Contains("envoy.type.v3.Percent", error.Message);
    }

    [Fact]
    public void PreloadedClosedDescriptorSet_ValidRegisteredMessageSucceeds_UnknownMessageFaults()
    {
        var validator = new Validator(new ValidatorOptions
        {
            FileDescriptors = [Percent.Descriptor.File],
            PreLoadDescriptors = true,
            DisableLazy = true,
        });
        Assert.True(validator.Validate(new Percent { Value = 50 }, failFast: false).IsSuccess);
        Assert.Contains(validator.Validate(new Percent { Value = 101 }, failFast: false).Violations,
            violation => violation.RuleId == "double.gte_lte" && violation.Field.GetPath() == "value");
        Assert.Throws<CompilationException>(() => validator.Validate(new HeaderValue { Key = "x" }, failFast: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedValidator_ColdAndWarmUse_PreservesValidAndInvalidOutcomes(bool warm)
    {
        var validator = new Validator();
        if (warm)
        {
            AssertRepresentativeOutcomes();
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] workers = Enumerable.Range(0, 4).Select(async _ =>
        {
            await start.Task;
            AssertRepresentativeOutcomes();
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        void AssertRepresentativeOutcomes()
        {
            Assert.True(validator.Validate(new Percent { Value = 50 }, failFast: false).IsSuccess);
            Assert.False(validator.Validate(new Percent { Value = 101 }, failFast: false).IsSuccess);
        }
    }

    [Fact]
    public void Validate_ValidResource_PreservesBinaryAndJson()
    {
        var resource = new FileAccessLog { Path = "/tmp/access.log" };
        byte[] binary = resource.ToByteArray();
        string json = JsonFormatter.Default.Format(resource);
        var validator = new Validator();
        Assert.True(validator.Validate(resource, failFast: false).IsSuccess);
        Assert.Equal(binary, resource.ToByteArray());
        Assert.Equal(json, JsonFormatter.Default.Format(resource));
    }
}
