using System.Collections;
using Buf.Validate;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate.Exceptions;

namespace ProtoValidate.Internal.Evaluator;

internal static class AnnotationGuard
{
    internal static void RequireSupported(IMessage rules, string owner, IList<FieldDescriptor> extensions)
    {
        // Unknown protobuf fields otherwise survive parsing without acquiring any evaluator.
        using var input = new CodedInputStream(rules.ToByteArray());
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int number = WireFormat.GetTagFieldNumber(tag);
            if (rules.Descriptor.FindFieldByNumber(number) is null)
            {
                var extension = extensions.SingleOrDefault(field => field.ExtendeeType == rules.Descriptor && field.FieldNumber == number);
                if (extension?.GetOptions()?.GetExtension(ValidateExtensions.Predefined)?.Cel.Count is not > 0)
                {
                    throw new CompilationException($"Unsupported validation annotation on {owner}: {rules.Descriptor.FullName} field {number}.");
                }
            }
            input.SkipLastField();
        }

        foreach (var field in rules.Descriptor.Fields.InFieldNumberOrder())
        {
            object value = field.Accessor.GetValue(rules);
            if (field.FieldType == FieldType.Enum && (!field.HasPresence || field.Accessor.HasValue(rules)))
            {
                int number = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                if (field.EnumType.FindValueByNumber(number) is null
                    || (field.FullName == "buf.validate.StringRules.well_known_regex" && number == 0))
                {
                    throw new CompilationException($"Unsupported validation enum on {owner}: {field.FullName} = {number}.");
                }
            }
            if (value is IMessage child)
            {
                RequireSupported(child, owner, extensions);
            }
            else if (field.IsRepeated && value is IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is IMessage nested)
                    {
                        RequireSupported(nested, owner, extensions);
                    }
                }
            }
        }
    }
}
