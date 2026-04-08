using UnbsAttention.Config;
using UnbsAttention.Models;
using UnbsAttention.Presentation;
using UnbsAttention.Services;
using System.Text.RegularExpressions;

namespace UnbsAttention;

public sealed class AttentionPluginRuntime
{
 private PluginConfig _config = new();
 private AttentionStore _store = null!;
 private AttentionDatabase _localDatabase = null!;
 private AttentionDatabase _effectiveDatabase = null!;
 private AttentionVisibilityPolicy _visibilityPolicy = null!;
 private IBeatSaverMapClient _beatSaverMapClient = null!;
 private IAttentionSyncProvider _syncProvider = null!;
 private HttpClient _httpClient = null!;
 private SubscriptionRefreshReport _lastRefreshReport = new();
 private readonly Dictionary<string, DescriptionCacheEntry> _descriptionCache = new(StringComparer.OrdinalIgnoreCase);
 private readonly object _descriptionCacheGate = new();
 private DateTime _lastDescriptionCacheCleanupUtc = DateTime.MinValue;
 private long _dataRevision;

 public void Init(PluginConfig config)
 {
  _config = config;
  EnsureConfigDefaults();

  _store = new AttentionStore();
  _localDatabase = _store.LoadOrCreate(_config.AttentionJsonPath);
  _effectiveDatabase = BuildEffectiveDatabase(null);
  var issues = AttentionEntryValidator.ValidateDatabase(_localDatabase);
  if (issues.Count > 0)
  {
   // 起動は止めない。無効エントリは matcher/upsert 側で無視される。
  }

  _httpClient = new HttpClient();
  var liveChecker = new TwitchHelixLiveChecker(_httpClient);
  _beatSaverMapClient = new BeatSaverMapClient(_httpClient);
  _visibilityPolicy = new AttentionVisibilityPolicy(_config, liveChecker);
  _syncProvider = BuildSyncProvider(_httpClient);

  if (_config.AutoRefreshOnInit)
  {
   _ = Task.Run(async () =>
   {
    var delay = Math.Max(0, _config.AutoRefreshDelaySeconds);
    if (delay > 0)
    {
     await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
    }

    await PullAndMergeAsync(CancellationToken.None).ConfigureAwait(false);
   });
  }
 }

 public async Task<int> PullAndMergeAsync(CancellationToken cancellationToken)
 {
  AttentionDatabase? remote;
  if (_syncProvider is GoogleSpreadsheetSyncProvider sheetsProvider)
  {
   var pullResult = await sheetsProvider.PullLatestWithReportAsync(cancellationToken).ConfigureAwait(false);
   _lastRefreshReport = pullResult.Report;
   remote = pullResult.Database;
  }
  else
  {
   remote = await _syncProvider.PullLatestAsync(cancellationToken).ConfigureAwait(false);
   _lastRefreshReport = new SubscriptionRefreshReport
   {
    StartedAtUtc = DateTime.UtcNow,
    FinishedAtUtc = DateTime.UtcNow,
    TotalSources = 1,
    SucceededSources = remote is null ? 0 : 1,
    FailedSources = remote is null ? 1 : 0,
    ImportedRows = remote?.Entries.Count ?? 0,
   };
  }

  var previousSignature = CreateSignature(_effectiveDatabase);
  var next = BuildEffectiveDatabase(remote);
  _effectiveDatabase = next;
  Interlocked.Increment(ref _dataRevision);
  var nextSignature = CreateSignature(_effectiveDatabase);
  return string.Equals(previousSignature, nextSignature, StringComparison.Ordinal) ? 0 : 1;
 }

 public long GetDataRevision()
 {
  return Interlocked.Read(ref _dataRevision);
 }

 public async Task<string?> BuildAttentionTextAsync(SongSelectionSnapshot snapshot, CancellationToken cancellationToken)
 {
  var context = AttentionLookupContextFactory.FromSnapshot(snapshot);
  return await BuildAttentionTextAsync(context, cancellationToken).ConfigureAwait(false);
 }

 // 旧互換: levelId だけで引くシンプルAPI。
 public async Task<string?> BuildAttentionTextAsync(string levelId, CancellationToken cancellationToken)
 {
  var context = new AttentionLookupContext
  {
   LevelId = levelId,
  };

  return await BuildAttentionTextAsync(context, cancellationToken).ConfigureAwait(false);
 }

 // 推奨: bsr/info/description を含むコンテキスト照合API。
 public async Task<string?> BuildAttentionTextAsync(AttentionLookupContext context, CancellationToken cancellationToken)
 {
  if (!_config.Enabled)
  {
   return null;
  }

  string? result = null;

  if (ShouldResolveBeatSaverDescription(context))
  {
   var resolvedDescription = await TryResolveBeatSaverDescriptionAsync(context, cancellationToken).ConfigureAwait(false);
   if (!string.IsNullOrWhiteSpace(resolvedDescription))
   {
    context.BeatSaverDescription = resolvedDescription;
   }
  }

  var canShow = await _visibilityPolicy.ShouldShowAsync(cancellationToken).ConfigureAwait(false);
  if (canShow)
  {
   var matches = _effectiveDatabase.FindByContext(context, BuildExcludedCategoriesForMatching());
   if (matches.Count > 0)
   {
    result = string.Join("\n", matches.Select(x => AttentionLineFormatter.Format(x, _config.DisplayMode, _config.CategoryPrefixes)));
   }
  }

  // 判定処理の最後で期限切れを掃除する（アクセス時のTTL延長は維持）。
  CleanupExpiredDescriptionCache();
  return result;
 }

 public IReadOnlyList<AttentionValidationIssue> GetValidationIssues()
 {
  return AttentionEntryValidator.ValidateDatabase(_effectiveDatabase);
 }

 public IReadOnlyList<SpreadsheetSourceItem> GetSpreadsheetSourceItems()
 {
  var result = new List<SpreadsheetSourceItem>();
  for (var i = 0; i < _config.SpreadsheetSources.Count; i++)
  {
   var raw = _config.SpreadsheetSources[i];
   var hasOpen = GoogleSpreadsheetSourceParser.TryBuildOpenUrl(raw, out var openUrl);
   var hasExport = GoogleSpreadsheetSourceParser.TryBuildCsvExportUrl(raw, out var exportUrl);
   result.Add(new SpreadsheetSourceItem
   {
    Index = i,
    RawSource = raw,
    OpenUrl = hasOpen ? openUrl : string.Empty,
    CsvExportUrl = hasExport ? exportUrl : string.Empty,
    IsValid = hasOpen && hasExport,
   });
  }

  return result;
 }

 public bool AddSpreadsheetSource(string source)
 {
  if (string.IsNullOrWhiteSpace(source))
  {
   return false;
  }

  if (!GoogleSpreadsheetSourceParser.TryBuildCsvExportUrl(source, out var newExportUrl))
  {
   return false;
  }

  foreach (var existing in _config.SpreadsheetSources)
  {
   if (!GoogleSpreadsheetSourceParser.TryBuildCsvExportUrl(existing, out var existingExportUrl))
   {
    continue;
   }

   if (string.Equals(existingExportUrl, newExportUrl, StringComparison.OrdinalIgnoreCase))
   {
    return false;
   }
  }

  _config.SpreadsheetSources.Add(source.Trim());
  RebuildSyncProvider();
  return true;
 }

 public bool RemoveSpreadsheetSourceAt(int index)
 {
  if (index < 0 || index >= _config.SpreadsheetSources.Count)
  {
   return false;
  }

  _config.SpreadsheetSources.RemoveAt(index);
  RebuildSyncProvider();
  return true;
 }

 public SubscriptionRefreshReport GetLastRefreshReport()
 {
  return _lastRefreshReport;
 }

 public bool GetEnabled()
 {
  return _config.Enabled;
 }

 public bool SetEnabled(bool enabled)
 {
  if (_config.Enabled == enabled)
  {
   return false;
  }

  _config.Enabled = enabled;
  return true;
 }

 public PluginConfig GetConfigSnapshot()
 {
  NormalizeAttentionCategories();
  NormalizeCategoryPrefixes();
  NormalizeSpreadsheetSources();

  return new PluginConfig
  {
   Enabled = _config.Enabled,
   DisplayMode = _config.DisplayMode,
   AttentionCategories = new List<string>(_config.AttentionCategories ?? new List<string>()),
   CategoryPrefixes = new Dictionary<string, string>(_config.CategoryPrefixes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
   EnableOnlyWhenTwitchStreamerLive = _config.EnableOnlyWhenTwitchStreamerLive,
   TwitchBroadcasterId = _config.TwitchBroadcasterId,
   TwitchClientId = _config.TwitchClientId,
   TwitchAppAccessToken = _config.TwitchAppAccessToken,
   TwitchCheckFailOpen = _config.TwitchCheckFailOpen,
   TwitchCheckIntervalSeconds = _config.TwitchCheckIntervalSeconds,
   AttentionJsonPath = _config.AttentionJsonPath,
   SyncStrategy = _config.SyncStrategy,
   SpreadsheetSources = new List<string>(_config.SpreadsheetSources ?? new List<string>()),
   AutoRefreshOnInit = _config.AutoRefreshOnInit,
   AutoRefreshDelaySeconds = _config.AutoRefreshDelaySeconds,
   BeatSaverDescriptionCacheTtlSeconds = _config.BeatSaverDescriptionCacheTtlSeconds,
   AttentionPositionOffsetX = _config.AttentionPositionOffsetX,
   AttentionPositionOffsetY = _config.AttentionPositionOffsetY,
   AttentionDisplayColorHex = _config.AttentionDisplayColorHex,
   PlayButtonAttentionColorHex = _config.PlayButtonAttentionColorHex,
   PlayButtonAttentionTextColorHex = _config.PlayButtonAttentionTextColorHex,
   PlayButtonConfirmColorHex = _config.PlayButtonConfirmColorHex,
   PlayButtonConfirmText = _config.PlayButtonConfirmText,
   PlayButtonConfirmDurationSeconds = _config.PlayButtonConfirmDurationSeconds,
   DiscordChannelUrl = _config.DiscordChannelUrl,
  };
 }

 public IReadOnlyList<string> GetEnabledCategories()
 {
  NormalizeAttentionCategories();
  return _config.AttentionCategories;
 }

 public bool SetCategoryEnabled(AttentionCategory category, bool enabled)
 {
  NormalizeAttentionCategories();

  var name = category.ToString();
  var exists = _config.AttentionCategories.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
  if (enabled && !exists)
  {
   _config.AttentionCategories.Add(name);
   return true;
  }

  if (!enabled && exists)
  {
   _config.AttentionCategories = _config.AttentionCategories
    .Where(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase))
    .ToList();
   return true;
  }

  return false;
 }

 public string GetCategoryPrefix(AttentionCategory category)
 {
  NormalizeCategoryPrefixes();
  return _config.CategoryPrefixes.TryGetValue(category.ToString(), out var prefix)
   && !string.IsNullOrWhiteSpace(prefix)
   ? prefix
   : AttentionLineFormatter.GetDefaultPrefix(category);
 }

 public bool SetCategoryPrefix(AttentionCategory category, string? prefix)
 {
  NormalizeCategoryPrefixes();

  var key = category.ToString();
  var next = string.IsNullOrWhiteSpace(prefix)
   ? AttentionLineFormatter.GetDefaultPrefix(category)
   : prefix!.Trim();

  var current = GetCategoryPrefix(category);
  if (string.Equals(current, next, StringComparison.Ordinal))
  {
   return false;
  }

  _config.CategoryPrefixes[key] = next;
  return true;
 }

 public int GetAttentionPositionOffsetX()
 {
  return _config.AttentionPositionOffsetX;
 }

 public int GetAttentionPositionOffsetY()
 {
  return _config.AttentionPositionOffsetY;
 }

 public string GetAttentionDisplayColorHex()
 {
  return _config.AttentionDisplayColorHex;
 }

 public string GetPlayButtonAttentionColorHex()
 {
  return _config.PlayButtonAttentionColorHex;
 }

 public string GetPlayButtonAttentionTextColorHex()
 {
  return _config.PlayButtonAttentionTextColorHex;
 }

 public bool SetAttentionDisplayColorHex(string? value)
 {
  var next = NormalizeColorHex(value, "#FFFF00FF");
  if (string.Equals(_config.AttentionDisplayColorHex, next, StringComparison.Ordinal))
  {
   return false;
  }

  _config.AttentionDisplayColorHex = next;
  return true;
 }

 public bool SetPlayButtonAttentionColorHex(string? value)
 {
  var next = NormalizeColorHex(value, "#FFFF00FF");
  if (string.Equals(_config.PlayButtonAttentionColorHex, next, StringComparison.Ordinal))
  {
   return false;
  }

  _config.PlayButtonAttentionColorHex = next;
  return true;
 }

 public bool SetPlayButtonAttentionTextColorHex(string? value)
 {
  var next = NormalizeColorHex(value, "#880000FF");
  if (string.Equals(_config.PlayButtonAttentionTextColorHex, next, StringComparison.Ordinal))
  {
   return false;
  }

  _config.PlayButtonAttentionTextColorHex = next;
  return true;
 }

 public string GetPlayButtonConfirmColorHex()
 {
  return _config.PlayButtonConfirmColorHex;
 }

 public string GetPlayButtonConfirmText()
 {
  return _config.PlayButtonConfirmText;
 }

 public int GetPlayButtonConfirmDurationSeconds()
 {
  return Math.Max(0, _config.PlayButtonConfirmDurationSeconds);
 }

 public bool SetPlayButtonConfirmColorHex(string? value)
 {
  var next = NormalizeColorHex(value, "#FF5933FF");
  if (string.Equals(_config.PlayButtonConfirmColorHex, next, StringComparison.Ordinal))
  {
   return false;
  }

  _config.PlayButtonConfirmColorHex = next;
  return true;
 }

 public bool SetPlayButtonConfirmText(string? value)
 {
  var next = NormalizePlayConfirmText(value);
  if (string.Equals(_config.PlayButtonConfirmText, next, StringComparison.Ordinal))
  {
   return false;
  }

  _config.PlayButtonConfirmText = next;
  return true;
 }

 public bool SetPlayButtonConfirmDurationSeconds(int value)
 {
  var next = Math.Max(0, value);
  if (_config.PlayButtonConfirmDurationSeconds == next)
  {
   return false;
  }

  _config.PlayButtonConfirmDurationSeconds = next;
  return true;
 }

 public bool AdjustAttentionPositionOffset(int deltaX, int deltaY)
 {
  var nextX = Math.Max(-400, Math.Min(400, _config.AttentionPositionOffsetX + deltaX));
  var nextY = Math.Max(-400, Math.Min(400, _config.AttentionPositionOffsetY + deltaY));
  if (nextX == _config.AttentionPositionOffsetX && nextY == _config.AttentionPositionOffsetY)
  {
   return false;
  }

  _config.AttentionPositionOffsetX = nextX;
  _config.AttentionPositionOffsetY = nextY;
  return true;
 }

 public bool TryGetOpenableSpreadsheetSourceUrl(int index, out string url)
 {
  url = string.Empty;
  if (index < 0 || index >= _config.SpreadsheetSources.Count)
  {
   return false;
  }

  return GoogleSpreadsheetSourceParser.TryBuildOpenUrl(_config.SpreadsheetSources[index], out url);
 }

 private IAttentionSyncProvider BuildSyncProvider(HttpClient httpClient)
 {
  var strategy = _config.SyncStrategy?.Trim().ToLowerInvariant();
  if (strategy == "google-sheets" || strategy == "google" || strategy == "sheets")
  {
   return new GoogleSpreadsheetSyncProvider(httpClient, _config.SpreadsheetSources);
  }

  if (strategy == "discord")
  {
   return new DiscordChannelAttachmentSyncProvider(httpClient, _config.DiscordChannelUrl);
  }

  return new LocalOnlySyncProvider(_store, _config.AttentionJsonPath);
 }

 private void EnsureConfigDefaults()
 {
  _config.AttentionCategories ??= new List<string>();
  _config.CategoryPrefixes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
  _config.SpreadsheetSources ??= new List<string>();
  _config.SyncStrategy ??= "google-sheets";
  _config.AttentionJsonPath ??= "UserData/unbs-attention.json";
  _config.DiscordChannelUrl ??= string.Empty;
  _config.TwitchBroadcasterId ??= string.Empty;
  _config.TwitchClientId ??= string.Empty;
  _config.TwitchAppAccessToken ??= string.Empty;
  _config.BeatSaverDescriptionCacheTtlSeconds = Math.Max(0, _config.BeatSaverDescriptionCacheTtlSeconds);
  _config.AttentionDisplayColorHex = NormalizeColorHex(_config.AttentionDisplayColorHex, "#FFFF00FF");
  _config.PlayButtonAttentionColorHex = NormalizeColorHex(_config.PlayButtonAttentionColorHex, "#FFFF00FF");
  _config.PlayButtonAttentionTextColorHex = NormalizeColorHex(_config.PlayButtonAttentionTextColorHex, "#880000FF");
  _config.PlayButtonConfirmColorHex = NormalizeColorHex(_config.PlayButtonConfirmColorHex, "#FF5933FF");
  _config.PlayButtonConfirmText = NormalizePlayConfirmText(_config.PlayButtonConfirmText);
  _config.PlayButtonConfirmDurationSeconds = Math.Max(0, _config.PlayButtonConfirmDurationSeconds);

  NormalizeAttentionCategories();
  NormalizeCategoryPrefixes();
  NormalizeSpreadsheetSources();
 }

 private void NormalizeAttentionCategories()
 {
  var normalized = _config.AttentionCategories
   .Where(x => !string.IsNullOrWhiteSpace(x))
   .Select(x => x!.Trim())
   .Select(ParseCategoryName)
   .Where(x => x.HasValue)
   .Select(x => x!.Value.ToString())
   .Distinct(StringComparer.OrdinalIgnoreCase)
   .ToList();

  if (normalized.Count == 0)
  {
   normalized = Enum
    .GetValues(typeof(AttentionCategory))
    .Cast<AttentionCategory>()
    .Select(x => x.ToString())
    .ToList();
  }

  _config.AttentionCategories = normalized;
 }

 private static AttentionCategory? ParseCategoryName(string name)
 {
  return Enum.TryParse<AttentionCategory>(name, true, out var parsed)
   ? parsed
   : null;
 }

 private void NormalizeCategoryPrefixes()
 {
  _config.CategoryPrefixes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

  foreach (var category in Enum.GetValues(typeof(AttentionCategory)).Cast<AttentionCategory>())
  {
   var key = category.ToString();
   if (!_config.CategoryPrefixes.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
   {
    _config.CategoryPrefixes[key] = AttentionLineFormatter.GetDefaultPrefix(category);
   }
   else
   {
    _config.CategoryPrefixes[key] = value.Trim();
   }
  }
 }

 private void NormalizeSpreadsheetSources()
 {
  _config.SpreadsheetSources ??= new List<string>();

  var normalized = new List<string>();
  var seenValidExports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var seenRawInvalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

  foreach (var source in _config.SpreadsheetSources)
  {
   if (string.IsNullOrWhiteSpace(source))
   {
    continue;
   }

   var raw = source.Trim();
   if (GoogleSpreadsheetSourceParser.TryBuildCsvExportUrl(raw, out var exportUrl))
   {
    if (seenValidExports.Add(exportUrl))
    {
     normalized.Add(raw);
    }

    continue;
   }

   if (seenRawInvalid.Add(raw))
   {
    normalized.Add(raw);
   }
  }

  _config.SpreadsheetSources = normalized;
 }

 private static string NormalizeColorHex(string? value, string fallback)
 {
  var trimmed = value?.Trim();
  return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed!;
 }

 private static string NormalizePlayConfirmText(string? value)
 {
  var trimmed = value?.Trim();
  return string.IsNullOrWhiteSpace(trimmed) ? "本当に？" : trimmed!;
 }

 private IReadOnlyList<string> BuildExcludedCategoriesForMatching()
 {
  var enabled = new HashSet<string>(_config.AttentionCategories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
  return Enum
   .GetValues(typeof(AttentionCategory))
   .Cast<AttentionCategory>()
   .Select(x => x.ToString())
   .Where(x => !enabled.Contains(x))
   .ToList();
 }

 private void RebuildSyncProvider()
 {
  if (_httpClient is null)
  {
   return;
  }

  _syncProvider = BuildSyncProvider(_httpClient);
 }

 private AttentionDatabase BuildEffectiveDatabase(AttentionDatabase? remote)
 {
  var effective = new AttentionDatabase();
  if (remote is not null)
  {
   effective.MergeFrom(remote);
  }

  // ローカル定義を最後に重ねて、購読データを上書きできるようにする。
  effective.MergeFrom(_localDatabase);
  return effective;
 }

 private bool ShouldResolveBeatSaverDescription(AttentionLookupContext context)
 {
  if (!string.IsNullOrWhiteSpace(context.BeatSaverDescription))
  {
   return false;
  }

  if (!HasEnabledDescriptionMatchers())
  {
   return false;
  }

  return !string.IsNullOrWhiteSpace(context.BsrId)
   || TryExtractCustomLevelHash(context.LevelId) is { Length: > 0 };
 }

 private bool HasEnabledDescriptionMatchers()
 {
  if (_effectiveDatabase is null)
  {
   return false;
  }

  var enabledCategories = new HashSet<string>(_config.AttentionCategories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
  return _effectiveDatabase.Entries.Any(entry =>
   enabledCategories.Contains(entry.Category.ToString())
  && entry.Target is AttentionTarget target
   && (
   (target.DescIncludes?.Any(x => !string.IsNullOrWhiteSpace(x)) ?? false)
   || !string.IsNullOrWhiteSpace(target.DescRegex)
   ));
 }

 private async Task<string?> TryResolveBeatSaverDescriptionAsync(AttentionLookupContext context, CancellationToken cancellationToken)
 {
  var bsr = context.BsrId?.Trim();
  if (!string.IsNullOrWhiteSpace(bsr))
  {
   var bsrValue = bsr!;
   var bsrCacheKey = "bsr:" + bsrValue;
   if (TryGetCachedDescription(bsrCacheKey, out var cachedByBsr))
   {
    if (!string.IsNullOrWhiteSpace(cachedByBsr))
    {
     return cachedByBsr;
    }
   }
   else
   {
    try
    {
     var byBsr = await _beatSaverMapClient.GetDescriptionByBsrIdAsync(bsrValue, cancellationToken).ConfigureAwait(false);
     SetCachedDescription(bsrCacheKey, byBsr);
     if (!string.IsNullOrWhiteSpace(byBsr))
     {
      return byBsr;
     }
    }
    catch
    {
     // BeatSaver 取得失敗は握りつぶして、手元の情報だけで判定を続ける。
    }
   }
  }

  var resolvedHash = TryExtractCustomLevelHash(context.LevelId)?.Trim();
  if (string.IsNullOrWhiteSpace(resolvedHash))
  {
   return null;
  }

  var hashValue = resolvedHash!;

  var hashCacheKey = "hash:" + hashValue;
  if (TryGetCachedDescription(hashCacheKey, out var cachedByHash))
  {
   return cachedByHash;
  }

  try
  {
   var byHash = await _beatSaverMapClient.GetDescriptionByHashAsync(hashValue, cancellationToken).ConfigureAwait(false);
   SetCachedDescription(hashCacheKey, byHash);
   return byHash;
  }
  catch
  {
   // BeatSaver 取得失敗は握りつぶして、手元の情報だけで判定を続ける。
   return null;
  }
 }

 private int GetDescriptionCacheTtlSeconds()
 {
  return Math.Max(0, _config.BeatSaverDescriptionCacheTtlSeconds);
 }

 private bool TryGetCachedDescription(string cacheKey, out string? description)
 {
  description = null;
  var ttlSeconds = GetDescriptionCacheTtlSeconds();
  if (ttlSeconds <= 0)
  {
   return false;
  }

  var now = DateTime.UtcNow;
  lock (_descriptionCacheGate)
  {
   if (!_descriptionCache.TryGetValue(cacheKey, out var entry))
   {
    return false;
   }

   if (entry.ExpiresAtUtc <= now)
   {
    _descriptionCache.Remove(cacheKey);
    return false;
   }

   entry.ExpiresAtUtc = now.AddSeconds(ttlSeconds);
   description = entry.Description;
   return true;
  }
 }

 private void SetCachedDescription(string cacheKey, string? description)
 {
  var ttlSeconds = GetDescriptionCacheTtlSeconds();
  if (ttlSeconds <= 0)
  {
   return;
  }

  var now = DateTime.UtcNow;
  lock (_descriptionCacheGate)
  {
   _descriptionCache[cacheKey] = new DescriptionCacheEntry
   {
    Description = description,
    ExpiresAtUtc = now.AddSeconds(ttlSeconds),
   };
  }
 }

 private void CleanupExpiredDescriptionCache()
 {
  var ttlSeconds = GetDescriptionCacheTtlSeconds();
  var now = DateTime.UtcNow;

  lock (_descriptionCacheGate)
  {
   if (ttlSeconds <= 0)
   {
    if (_descriptionCache.Count > 0)
    {
     _descriptionCache.Clear();
    }

    _lastDescriptionCacheCleanupUtc = now;
    return;
   }

   var cleanupIntervalSeconds = Math.Min(60, ttlSeconds);
   if ((now - _lastDescriptionCacheCleanupUtc).TotalSeconds < cleanupIntervalSeconds)
   {
    return;
   }

   var expiredKeys = _descriptionCache
    .Where(x => x.Value.ExpiresAtUtc <= now)
    .Select(x => x.Key)
    .ToList();

   foreach (var expiredKey in expiredKeys)
   {
    _descriptionCache.Remove(expiredKey);
   }

   _lastDescriptionCacheCleanupUtc = now;
  }
 }

 private static string? TryExtractCustomLevelHash(string? levelId)
 {
  if (string.IsNullOrWhiteSpace(levelId))
  {
   return null;
  }

  var match = Regex.Match(levelId, "custom_level_([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
  return match.Success ? match.Groups[1].Value : null;
 }

 private sealed class DescriptionCacheEntry
 {
  public string? Description { get; set; }

  public DateTime ExpiresAtUtc { get; set; }
 }

 private static string CreateSignature(AttentionDatabase database)
 {
  return string.Join("\n", database.Entries.Select(AttentionEntryIdentity.BuildKey));
 }
}
