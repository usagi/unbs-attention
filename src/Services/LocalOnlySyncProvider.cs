using UnbsAttention.Models;

namespace UnbsAttention.Services;

public sealed class LocalOnlySyncProvider : IAttentionSyncProvider
{
 private readonly AttentionStore _store;
 private readonly string _path;

 public LocalOnlySyncProvider(AttentionStore store, string path)
 {
  _store = store;
  _path = path;
 }

 public string Name => "local";

 public Task<AttentionDatabase?> PullLatestAsync(CancellationToken cancellationToken)
 {
  var db = _store.LoadOrCreate(_path);
  return Task.FromResult<AttentionDatabase?>(db);
 }

 public Task PushAsync(AttentionDatabase database, CancellationToken cancellationToken)
 {
  _store.Save(_path, database);
  return Task.CompletedTask;
 }
}
