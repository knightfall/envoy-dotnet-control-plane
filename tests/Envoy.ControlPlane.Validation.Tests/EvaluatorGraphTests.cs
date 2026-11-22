using Buf.Validate;
using Envoy.Config.Cluster.V3;
using Envoy.Type.Matcher.V3;
using Envoy.ControlPlane.Validation.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using ProtoValidate.Exceptions;
using Xunit;
using RetryBudget = Envoy.Config.Cluster.V3.CircuitBreakers.Types.Thresholds.Types.RetryBudget;
using Thresholds = Envoy.Config.Cluster.V3.CircuitBreakers.Types.Thresholds;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class EvaluatorGraphTests
{
    [Fact]
    public void FailedCyclicBuild_DoesNotCacheChildReferencingIncompleteParent()
    {
        var original = ValueMatcher.Descriptor.File;
        var file = original.ToProto();
        var field = file.MessageType[0].Field.Single(item => item.Name == "or_match");
        field.Options ??= new FieldOptions();
        var rules = field.Options.GetExtension(Protovalidate.Field) ?? new FieldRules();
        rules.Cel.Add(new Rule { Id = "test.invalid", Expression = "this >" });
        field.Options.SetExtension(Protovalidate.Field, rules);

        var types = new GeneratedClrTypeInfo(null, null,
        [
            new(typeof(ValueMatcher), ValueMatcher.Parser,
                ["NullMatch", "DoubleMatch", "StringMatch", "BoolMatch", "PresentMatch", "ListMatch", "OrMatch"],
                ["MatchPattern"], null, null,
                [new(typeof(ValueMatcher.Types.NullMatch), ValueMatcher.Types.NullMatch.Parser, null, null, null, null, null)]),
            new(typeof(ListMatcher), ListMatcher.Parser, ["OneOf"], ["MatchPattern"], null, null, null),
            new(typeof(OrMatcher), OrMatcher.Parser, ["ValueMatchers"], null, null, null, null),
        ]);
        var descriptors = FileDescriptor.FromGeneratedCode(file.ToByteArray(), original.Dependencies.ToArray(), types);
        var root = new DescriptorMessage(new ValueMatcher(), descriptors.MessageTypes[0]);
        var child = new DescriptorMessage(new ListMatcher { OneOf = new ValueMatcher() }, descriptors.MessageTypes[1]);
        var validator = new Validator();

        Assert.Throws<CompilationException>(() => validator.GetEvaluatorDebugString(root));
        Assert.Throws<CompilationException>(() => validator.GetEvaluatorDebugString(child));
        Assert.True(validator.Validate(new ValueMatcher { BoolMatch = true }, failFast: false).IsSuccess);
    }

    [Fact]
    public async Task ConcurrentColdCyclicValidation_PreservesNestedRequiredRules()
    {
        var validator = new Validator();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await release.Task;
            bool valid = index % 2 == 0;
            var child = new ValueMatcher();
            if (valid)
            {
                child.BoolMatch = true;
            }
            var list = new ListMatcher { OneOf = child };
            IMessage input = index % 4 < 2 ? list : new ValueMatcher { ListMatch = list };
            var result = validator.Validate(input, failFast: false);
            Assert.Equal(valid, result.IsSuccess);
            if (!valid)
            {
                Assert.Single(result.Violations);
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        release.SetResult();

        await Task.WhenAll(jobs).WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Preload_PgvOnlyField_FailsDuringConstruction()
    {
        var file = DescriptorFixtures.PercentFile(source =>
            source.MessageType[0].Field[0].Options.ClearExtension(Protovalidate.Field));

        var error = Assert.Throws<CompilationException>(() => Preload(file));

        Assert.Contains("PGV-only validation metadata on envoy.type.v3.Percent.value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preload_CompilesDeepDeclarationEvenWhenItsOwningFieldIsIgnored()
    {
        var original = CircuitBreakers.Descriptor.File;
        var file = original.ToProto();
        var thresholds = file.MessageType[0].NestedType[0];
        var field = thresholds.Field.Single(item => item.Name == "retry_budget");
        field.Options ??= new FieldOptions();
        field.Options.SetExtension(Protovalidate.Field, new FieldRules { Ignore = Ignore.Always });
        var types = new GeneratedClrTypeInfo(null, null,
        [
            new(typeof(CircuitBreakers), CircuitBreakers.Parser, ["Thresholds", "PerHostThresholds"], null, null, null,
            [
                new(typeof(Thresholds), Thresholds.Parser,
                    ["Priority", "MaxConnections", "MaxPendingRequests", "MaxRequests", "MaxRetries", "RetryBudget", "TrackRemaining", "MaxConnectionPools"],
                    null, null, null, [new(typeof(RetryBudget), RetryBudget.Parser, ["BudgetPercent", "BudgetInterval", "MinRetryConcurrency"],
                        null, null, null, null)]),
            ]),
        ]);
        var descriptors = FileDescriptor.FromGeneratedCode(file.ToByteArray(), original.Dependencies.ToArray(), types);
        var retryBudget = descriptors.MessageTypes[0].NestedTypes[0].NestedTypes[0];

        string evaluator = Preload(descriptors).GetEvaluatorDebugString(new DescriptorMessage(new RetryBudget(), retryBudget));

        Assert.Contains("Message Evaluator: envoy.config.cluster.v3.CircuitBreakers.Thresholds.RetryBudget", evaluator, StringComparison.Ordinal);
    }

    [Fact]
    public void Preload_HandlesFileWithSyntheticMapEntries()
    {
        var validator = Preload(Cluster.Descriptor.File);

        Assert.True(validator.Validate(new Cluster.Types.LbSubsetConfig.Types.LbSubsetSelector
        {
            Keys = { "stage" },
        }, failFast: false).IsSuccess);
    }

    private static Validator Preload(FileDescriptor file) => new(new ValidatorOptions
    {
        FileDescriptors = [file],
        PreLoadDescriptors = true,
        DisableLazy = true,
    });

    private sealed class DescriptorMessage(IMessage message, MessageDescriptor descriptor) : IMessage
    {
        public MessageDescriptor Descriptor => descriptor;
        public int CalculateSize() => message.CalculateSize();
        public void MergeFrom(CodedInputStream input) => message.MergeFrom(input);
        public void WriteTo(CodedOutputStream output) => message.WriteTo(output);
    }
}
