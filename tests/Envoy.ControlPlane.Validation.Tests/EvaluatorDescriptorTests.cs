using System.Text.RegularExpressions;
using System.Text.Json;
using Envoy.Config.Core.V3;
using Envoy.Type.V3;
using Envoy.ControlPlane.Validation.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using ProtoValidate.Exceptions;
using Xunit;
using Pgv = Validate.ValidateExtensions;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class EvaluatorDescriptorTests
{
    [Theory]
    [InlineData("matches('example\\n', '^[^/]*$')", true)]
    [InlineData("'example\\n'.matches('^[a-z0-9_\\\\-]*$')", false)]
    [InlineData("matches('example\\n', '^[a-z0-9_\\\\-]*$')", false)]
    [InlineData("'example'.matches('^[a-z0-9_\\\\-]*$')", true)]
    [InlineData("'a\\v'.matches('^[^\\\\s,]+$')", true)]
    [InlineData("'a\\t'.matches('^[^\\\\s,]+$')", false)]
    [InlineData("'😀'.matches('^[^?#]+[^?#/]$')", false)]
    [InlineData("'/😀'.matches('^[^?#]+[^?#/]$')", true)]
    [InlineData("'/😀/'.matches('^[^?#]+[^?#/]$')", false)]
    public void CelRegex_CatalogPatternsUseRe2Semantics(string expression, bool valid)
    {
        var result = new Validator().Validate(PercentWithCelRule(expression), failFast: false);
        Assert.Equal(valid, result.IsSuccess);
        if (!valid)
        {
            Assert.Equal("test.regex", Assert.Single(result.Violations).RuleId);
        }
    }

    [Theory]
    [InlineData("'sample'.matches('amp')")]
    [InlineData("matches('sample', 'amp')")]
    [InlineData("'sample\\n'.matches('^sample$')")]
    public void CelRegex_CustomPatternsRetainDotNetSemantics(string expression)
    {
        Assert.True(new Validator().Validate(PercentWithCelRule(expression), failFast: false).IsSuccess);
    }

    [Theory]
    [InlineData("header", "value", true)]
    [InlineData("header", "value\n", false)]
    [InlineData("header\n", "value", false)]
    public void CelRegex_GeneratedHeaderRulesCheckTheWholeValue(string key, string value, bool valid)
    {
        var result = new Validator().Validate(new HeaderValue { Key = key, Value = value }, failFast: false);
        Assert.Equal(valid, result.IsSuccess);
        if (!valid)
        {
            string field = key.EndsWith('\n') ? "header_name" : "header_value";
            Assert.Equal("string.well_known_regex." + field, Assert.Single(result.Violations).RuleId);
        }
    }

    [Fact]
    public void CelRegex_CatalogOnlyModeCoversEveryMigratedStringPattern()
    {
        var patterns = ValidationCorpus.Files.SelectMany(file => file.MessageTypes)
            .SelectMany(FindCatalogPatterns).Distinct(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(patterns);
        var validator = new Validator(new ValidatorOptions { RequireCatalogRegexPatterns = true });
        foreach (string pattern in patterns)
        {
            string expression = "'sample'.matches(" + JsonSerializer.Serialize(pattern) + ")";
            var error = Record.Exception(() => validator.Validate(PercentWithCelRule(expression), failFast: false));
            Assert.True(error is null, $"Catalog pattern '{pattern}' failed evaluation: {error}");
        }
    }

    [Fact]
    public void CelRegex_CatalogOnlyModeRejectsOtherPatterns()
    {
        var validator = new Validator(new ValidatorOptions { RequireCatalogRegexPatterns = true });
        var error = Assert.Throws<ExecutionException>(() =>
            validator.Validate(PercentWithCelRule("matches('sample', 'amp')"), failFast: false));
        Assert.Contains("outside the supported Envoy catalog", Assert.IsType<ArgumentException>(error.InnerException).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CelRegex_MatchTimeoutFailsEvaluation()
    {
        // The .NET interpreter rounds the positive tick to a zero-millisecond deadline at its first timeout check.
        var validator = new Validator(new ValidatorOptions { CustomRegexMatchTimeout = TimeSpan.FromTicks(1) });
        var rule = PercentWithCelRule("'sample word'.matches('[sS]ample [a-z]+')");
        var error = Assert.Throws<ExecutionException>(() => validator.Validate(rule, failFast: false));
        Assert.IsType<RegexMatchTimeoutException>(error.InnerException);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void CelRegex_CustomTimeoutMustBePositiveAndAtMostTheDefault(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator(new ValidatorOptions { CustomRegexMatchTimeout = TimeSpan.FromMilliseconds(milliseconds) }));
    }

    [Fact]
    public void CelRegex_OversizePatternFailsEvaluation()
    {
        string expression = "'x'.matches('" + new string('x', (16 * 1024) + 1) + "')";
        var error = Assert.Throws<ExecutionException>(() =>
            new Validator().Validate(PercentWithCelRule(expression), failFast: false));
        Assert.Contains("16 KiB", Assert.IsType<ArgumentException>(error.InnerException).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CelRegex_InvalidPatternFailsEvaluation()
    {
        var error = Assert.Throws<ExecutionException>(() =>
            new Validator().Validate(PercentWithCelRule("'value'.matches('[')"), failFast: false));
        Assert.IsType<RegexParseException>(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_MetadataOnlyDescriptor_ThrowsWithoutChangingGeneratedRules(bool warm)
    {
        var validator = new Validator();
        if (warm)
        {
            AssertGeneratedRules(validator);
        }
        var metadataOnly = MetadataOnlyPercent();
        Assert.Null(metadataOnly.Descriptor.Parser);
        Assert.Equal(Percent.Descriptor.Fields[1].GetOptions(), metadataOnly.Descriptor.Fields[1].GetOptions());

        var error = Assert.Throws<CompilationException>(() => validator.Validate(metadataOnly, failFast: false));

        Assert.Contains("envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
        Assert.Contains("CLR parser", error.Message, StringComparison.Ordinal);
        AssertGeneratedRules(validator);
    }

    [Fact]
    public void Preload_MetadataOnlyDescriptor_ThrowsCompilationException()
    {
        var metadataOnly = MetadataOnlyPercent();

        var error = Assert.Throws<CompilationException>(() => new Validator(new ValidatorOptions
        {
            FileDescriptors = [metadataOnly.Descriptor.File],
            PreLoadDescriptors = true,
            DisableLazy = true,
        }));

        Assert.Contains("envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
        Assert.Contains("CLR parser", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Preload_SameNameDescriptor_DoesNotChangeGeneratedRules(double upperBound)
    {
        var alternate = ClrBackedPercentFile(upperBound);
        Assert.NotSame(Percent.Descriptor, alternate.MessageTypes[0]);
        Assert.Equal(Percent.Descriptor.FullName, alternate.MessageTypes[0].FullName);
        var validator = new Validator(new ValidatorOptions
        {
            FileDescriptors = [alternate],
            PreLoadDescriptors = true,
        });

        AssertGeneratedRules(validator);
    }

    [Fact]
    public void ClosedDescriptorSet_SameNameDescriptor_DoesNotRegisterGeneratedDescriptor()
    {
        var validator = new Validator(new ValidatorOptions
        {
            FileDescriptors = [ClrBackedPercentFile(1)],
            PreLoadDescriptors = true,
            DisableLazy = true,
        });

        var error = Assert.Throws<CompilationException>(() => validator.Validate(new Percent { Value = 50 }, failFast: false));

        Assert.Contains("No evaluator available for envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<string> FindCatalogPatterns(MessageDescriptor message)
    {
        if (message.GetOptions()?.GetExtension(Protovalidate.Message) is { } rules)
        {
            AssertNoCustomRegexRules(rules.Cel);
        }
        return message.Fields.InDeclarationOrder().SelectMany(field => FindFieldPatterns(field.GetOptions()?.GetExtension(Protovalidate.Field)))
            .Concat(message.NestedTypes.SelectMany(FindCatalogPatterns));
    }

    private static IEnumerable<string> FindFieldPatterns(Buf.Validate.FieldRules? rules)
    {
        if (rules is null)
        {
            yield break;
        }
        AssertNoCustomRegexRules(rules.Cel);
        Assert.False(rules.Bytes is { HasPattern: true }, "Review bytes.pattern semantics and extend the catalog regex verification.");
        if (rules.String is { HasPattern: true } text)
        {
            yield return text.Pattern;
        }
        foreach (var nested in new[] { rules.Repeated?.Items, rules.Map?.Keys, rules.Map?.Values })
        {
            if (nested is not null)
            {
                foreach (string pattern in FindFieldPatterns(nested))
                {
                    yield return pattern;
                }
            }
        }
    }

    private static void AssertNoCustomRegexRules(IEnumerable<Buf.Validate.Rule> rules)
    {
        // A new CEL regex expression needs review for computed patterns and argument types.
        Assert.DoesNotContain(rules, rule => rule.Expression.Contains("matches", StringComparison.Ordinal));
    }

    private static void AssertGeneratedRules(Validator validator)
    {
        Assert.True(validator.Validate(new Percent { Value = 50 }, failFast: false).IsSuccess);
        Assert.Contains(validator.Validate(new Percent { Value = 101 }, failFast: false).Violations,
            violation => violation.RuleId == "double.gte_lte" && violation.Field.GetPath() == "value");
    }

    private static IMessage MetadataOnlyPercent()
    {
        var files = new List<FileDescriptorProto>();
        var visited = new HashSet<FileDescriptor>();
        Add(Percent.Descriptor.File);
        var extensions = new ExtensionRegistry
        {
            Pgv.Rules, Pgv.Required, Pgv.Disabled, Pgv.Ignored,
            Protovalidate.Field, Protovalidate.Oneof, Protovalidate.Message,
        };
        var descriptors = FileDescriptor.BuildFromByteStrings(files.Select(file => file.ToByteString()), extensions);
        var descriptor = descriptors.Single(file => file.Name == Percent.Descriptor.File.Name).MessageTypes[0];
        return new DescriptorMessage(new Percent { Value = 101 }, descriptor);

        void Add(FileDescriptor file)
        {
            if (!visited.Add(file))
            {
                return;
            }
            foreach (var dependency in file.Dependencies)
            {
                Add(dependency);
            }
            files.Add(file.ToProto());
        }
    }

    private static FileDescriptor ClrBackedPercentFile(double upperBound)
    {
        // Rebind only test metadata to the existing CLR types; no second message owner is generated.
        return DescriptorFixtures.PercentFile(file =>
            file.MessageType[0].Field[0].Options.GetExtension(Protovalidate.Field).Double.Lte = upperBound);
    }

    private static IMessage PercentWithCelRule(string expression)
    {
        var file = DescriptorFixtures.PercentFile(source =>
        {
            var message = source.MessageType[0];
            message.Field[0].Options.SetExtension(Protovalidate.Field, new Buf.Validate.FieldRules { Ignore = Buf.Validate.Ignore.Always });
            var rules = new Buf.Validate.MessageRules();
            rules.Cel.Add(new Buf.Validate.Rule { Id = "test.regex", Expression = expression, Message = "regex rejected the value" });
            message.Options ??= new MessageOptions();
            message.Options.SetExtension(Protovalidate.Message, rules);
        });
        return new DescriptorMessage(new Percent(), file.MessageTypes[0]);
    }

    private sealed class DescriptorMessage(IMessage message, MessageDescriptor descriptor) : IMessage
    {
        public MessageDescriptor Descriptor => descriptor;
        public int CalculateSize() => message.CalculateSize();
        public void MergeFrom(CodedInputStream input) => message.MergeFrom(input);
        public void WriteTo(CodedOutputStream output) => message.WriteTo(output);
    }
}
