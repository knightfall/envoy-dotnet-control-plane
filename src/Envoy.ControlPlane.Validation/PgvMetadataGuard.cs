using System.Runtime.CompilerServices;
using Buf.Validate;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate.Exceptions;

namespace ProtoValidate.Internal.Evaluator;

internal static class PgvMetadataGuard
{
    private const int PgvRulesOrRequiredOrDisabled = 1071;
    private const int PgvIgnored = 1072;
    private static readonly ConditionalWeakTable<MessageDescriptor, object> Checked = new();

    internal static void RequireMigrated(MessageDescriptor descriptor) => Checked.GetValue(descriptor, Check);

    private static object Check(MessageDescriptor root)
    {
        var visited = new HashSet<MessageDescriptor>();
        Visit(root);
        return new object();

        void Visit(MessageDescriptor message)
        {
            if (!visited.Add(message))
            {
                return;
            }

            var messageOptions = message.GetOptions();
            if ((HasLegacyOption(messageOptions, PgvRulesOrRequiredOrDisabled) || HasLegacyOption(messageOptions, PgvIgnored))
                && messageOptions?.HasExtension(ValidateExtensions.Message) != true)
            {
                ThrowUnmigrated(message.FullName);
            }

            foreach (var oneof in message.Oneofs)
            {
                var options = oneof.GetOptions();
                if (HasLegacyOption(options, PgvRulesOrRequiredOrDisabled) && options?.HasExtension(ValidateExtensions.Oneof) != true)
                {
                    ThrowUnmigrated(oneof.FullName);
                }
            }

            foreach (var field in message.Fields.InFieldNumberOrder())
            {
                var options = field.GetOptions();
                if (HasLegacyOption(options, PgvRulesOrRequiredOrDisabled) && options?.HasExtension(ValidateExtensions.Field) != true)
                {
                    ThrowUnmigrated(field.FullName);
                }

                if (field.FieldType == FieldType.Message
                    && options?.GetExtension(ValidateExtensions.Field)?.Ignore != Ignore.Always)
                {
                    Visit(field.MessageType);
                }
            }
        }
    }

    private static bool HasLegacyOption(IMessage? options, int fieldNumber)
    {
        if (options is null)
        {
            return false;
        }

        // Read PGV's option numbers without introducing another annotation owner
        // or making the optional evaluator depend on the Protocol assembly.
        using var input = new CodedInputStream(options.ToByteArray());
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == fieldNumber)
            {
                return WireFormat.GetTagWireType(tag) switch
                {
                    WireFormat.WireType.LengthDelimited => input.ReadBytes().Length != 0,
                    WireFormat.WireType.Varint => input.ReadBool(),
                    _ => throw new CompilationException("Unsupported PGV validation option encoding."),
                };
            }
            input.SkipLastField();
        }
        return false;
    }

    private static void ThrowUnmigrated(string name) => throw new CompilationException(
        $"PGV-only validation metadata on {name}. Regenerate these bindings with Protovalidate annotations before validation.");
}
