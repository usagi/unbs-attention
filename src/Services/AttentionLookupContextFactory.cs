using UnbsAttention.Models;

namespace UnbsAttention.Services;

public static class AttentionLookupContextFactory
{
 public static AttentionLookupContext FromSnapshot(SongSelectionSnapshot snapshot)
 {
  return new AttentionLookupContext
  {
   LevelId = snapshot.LevelId,
   BsrId = snapshot.BsrId,
   SongName = snapshot.SongName,
   SongSubName = snapshot.SongSubName,
   SongAuthorName = snapshot.SongAuthorName,
   LevelAuthorName = snapshot.LevelAuthorName,
   BeatSaverDescription = snapshot.BeatSaverDescription,
  };
 }
}
