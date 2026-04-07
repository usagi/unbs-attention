namespace UnbsAttention.Models;

public sealed class AttentionLookupContext
{
 public string? LevelId { get; set; }

 public string? BsrId { get; set; }

 public string? SongName { get; set; }

 public string? SongSubName { get; set; }

 public string? SongAuthorName { get; set; }

 public string? LevelAuthorName { get; set; }

 public string? BeatSaverDescription { get; set; }

 public string BuildInfoSearchText()
 {
  return string.Join(" ", new[]
  {
            SongName,
            SongSubName,
            SongAuthorName,
            LevelAuthorName,
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
 }
}
