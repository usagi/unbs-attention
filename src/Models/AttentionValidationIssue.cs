namespace UnbsAttention.Models;

public sealed class AttentionValidationIssue
{
 public int EntryIndex { get; set; } = -1;

 public string Code { get; set; } = string.Empty;

 public string Message { get; set; } = string.Empty;
}
