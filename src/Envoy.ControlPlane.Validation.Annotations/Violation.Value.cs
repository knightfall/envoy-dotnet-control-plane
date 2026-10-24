namespace Buf.Validate;

/// <summary>
/// Retains the upstream evaluator's diagnostic value with its generated owner.
/// This property is not a protobuf wire field.
/// </summary>
public sealed partial class Violation
{
    /// <summary>Gets or sets the non-serialized diagnostic value.</summary>
    public object? Value { get; set; }
}
