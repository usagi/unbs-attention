using Newtonsoft.Json;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public sealed class AttentionStore
{
 private static readonly JsonSerializerSettings JsonSettings = new()
 {
  Formatting = Formatting.Indented,
  NullValueHandling = NullValueHandling.Ignore,
 };

 public AttentionDatabase LoadOrCreate(string path)
 {
  if (string.IsNullOrWhiteSpace(path))
  {
   throw new ArgumentException("Path is empty.", nameof(path));
  }

  if (!File.Exists(path))
  {
   var fresh = new AttentionDatabase();
   Save(path, fresh);
   return fresh;
  }

  var json = File.ReadAllText(path);
  var model = JsonConvert.DeserializeObject<AttentionDatabase>(json);
  return model ?? new AttentionDatabase();
 }

 public void Save(string path, AttentionDatabase database)
 {
  var directory = Path.GetDirectoryName(path);
  if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
  {
   Directory.CreateDirectory(directory);
  }

  var json = JsonConvert.SerializeObject(database, JsonSettings);
  File.WriteAllText(path, json);
 }
}
