using UnbsAttention.Config;
using UnbsAttention.Models;
using UnbsAttention.Presentation;
using UnbsAttention.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace UnbsAttention;

public sealed class AttentionPluginRuntime
{
 private static readonly Regex CustomLevelHashRegex = new(
  "custom_level_([a-fA-F0-9]{40})",
  RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
 private readonly Dictionary<string, BsrByHashCacheEntry> _bsrByHashCache = new(StringComparer.OrdinalIgnoreCase);
 private readonly object _descriptionCacheGate = new();
 private DateTime _lastDescriptionCacheCleanupUtc = DateTime.MinValue;
 private AttentionMatcherIndex _matcherIndex = AttentionMatcherIndex.Empty;
 private readonly HashSet<AttentionCategory> _excludedCategoriesForMatching = new();
 private bool _excludedCategoriesForMatchingDirty = true;
 private long _dataRevision;

 public event Action<long>? DataRevisionChanged;

 public void Init(PluginConfig config)
 {
  _config = config;
  EnsureConfigDefaults();

  _store = new AttentionStore();
  _localDatabase = _store.LoadOrCreate(_config.AttentionJsonPath);
  _effectiveDatabase = BuildEffectiveDatabase(null);
  RebuildMatcherIndex();
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
  RebuildMatcherIndex();
  var dataRevision = Interlocked.Increment(ref _dataRevision);
  NotifyDataRevisionChanged(dataRevision);
  InvalidateMatchingCaches();
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
  var normalizedLevelId = levelId?.Trim();
  var bsrId = AttentionMatcherIndex.TryParseBsrHex(normalizedLevelId, out _)
   ? normalizedLevelId
   : null;

  var context = new AttentionLookupContext
  {
   LevelId = normalizedLevelId,
   BsrId = bsrId,
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

  var canShow = await _visibilityPolicy.ShouldShowAsync(cancellationToken).ConfigureAwait(false);
  if (!canShow)
  {
   CleanupExpiredDescriptionCache();
   return null;
  }

  string? result = null;
  var excludedCategories = GetExcludedCategoriesForMatching();

  await ResolveBeatSaverMetadataIfNeededAsync(context, excludedCategories, cancellationToken).ConfigureAwait(false);

  var matches = _matcherIndex.FindMatches(context, excludedCategories);
  if (matches.Count > 0)
  {
   var builder = new StringBuilder(matches.Count * 24);
   for (var i = 0; i < matches.Count; i++)
   {
    if (i > 0)
    {
     builder.Append('\n');
    }

    var match = matches[i];
    builder.Append(AttentionLineFormatter.Format(match.Category, match.Reason, _config.DisplayMode, _config.CategoryPrefixes));
   }

   result = builder.ToString();
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

 public bool GetDebugLoggingEnabled()
 {
  return _config.Debug;
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
   Debug = _config.Debug,
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
   InvalidateMatchingCaches();
   return true;
  }

  if (!enabled && exists)
  {
   _config.AttentionCategories = _config.AttentionCategories
    .Where(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase))
    .ToList();
   InvalidateMatchingCaches();
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
  InvalidateMatchingCaches();
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
  InvalidateMatchingCaches();
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

 private ISet<AttentionCategory> GetExcludedCategoriesForMatching()
 {
  if (!_excludedCategoriesForMatchingDirty)
  {
   return _excludedCategoriesForMatching;
  }

  _excludedCategoriesForMatching.Clear();
  var enabled = new HashSet<string>(_config.AttentionCategories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
  foreach (var category in Enum.GetValues(typeof(AttentionCategory)).Cast<AttentionCategory>())
  {
   if (!enabled.Contains(category.ToString()))
   {
    _excludedCategoriesForMatching.Add(category);
   }
  }

  _excludedCategoriesForMatchingDirty = false;
  return _excludedCategoriesForMatching;
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

 private void RebuildMatcherIndex()
 {
  _matcherIndex = AttentionMatcherIndex.Build(_effectiveDatabase);
 }

 private async Task ResolveBeatSaverMetadataIfNeededAsync(
  AttentionLookupContext context,
  ISet<AttentionCategory> excludedCategories,
  CancellationToken cancellationToken)
 {
  var needsDescription = string.IsNullOrWhiteSpace(context.BeatSaverDescription)
   && _matcherIndex.HasDescriptionRules(excludedCategories);
  var needsBsr = string.IsNullOrWhiteSpace(context.BsrId)
   && _matcherIndex.HasBsrRules(excludedCategories);

  if (!needsDescription && !needsBsr)
  {
   return;
  }

  var bsr = context.BsrId?.Trim();
  if (!string.IsNullOrWhiteSpace(bsr))
  {
   if (!needsDescription)
   {
    return;
   }

   var bsrValue = bsr!;
   var bsrCacheKey = "bsr:" + bsrValue;
   if (TryGetCachedDescription(bsrCacheKey, out var cachedByBsr))
   {
    if (!string.IsNullOrWhiteSpace(cachedByBsr))
    {
     context.BeatSaverDescription = cachedByBsr;
    }

    return;
   }

   try
   {
    var byBsr = await _beatSaverMapClient.GetDescriptionByBsrIdAsync(bsrValue, cancellationToken).ConfigureAwait(false);
    SetCachedDescription(bsrCacheKey, byBsr);
    if (!string.IsNullOrWhiteSpace(byBsr))
    {
     context.BeatSaverDescription = byBsr;
    }
   }
   catch
   {
    // BeatSaver 取得失敗は握りつぶして、手元の情報だけで判定を続ける。
   }

   return;
  }

  var resolvedHash = TryExtractCustomLevelHash(context.LevelId)?.Trim();
  if (string.IsNullOrWhiteSpace(resolvedHash))
  {
   return;
  }

  var hashValue = resolvedHash!;

  if (needsBsr && TryGetCachedBsrByHash(hashValue, out var cachedBsr) && !string.IsNullOrWhiteSpace(cachedBsr))
  {
   context.BsrId = cachedBsr;
   needsBsr = false;
  }

  var hashCacheKey = "hash:" + hashValue;
  if (needsDescription && TryGetCachedDescription(hashCacheKey, out var cachedByHash))
  {
   if (!string.IsNullOrWhiteSpace(cachedByHash))
   {
    context.BeatSaverDescription = cachedByHash;
    needsDescription = false;
   }
  }

  if (!needsDescription && !needsBsr)
  {
   return;
  }

  try
  {
   var details = await _beatSaverMapClient.GetMapDetailsByHashAsync(hashValue, cancellationToken).ConfigureAwait(false);

   var resolvedBsrId = details?.BsrId?.Trim();
   SetCachedBsrByHash(hashValue, resolvedBsrId);
   if (!string.IsNullOrWhiteSpace(resolvedBsrId))
   {
    context.BsrId = resolvedBsrId;
   }

   if (needsDescription)
   {
    var resolvedDescription = details?.Description;
    SetCachedDescription(hashCacheKey, resolvedDescription);
    if (!string.IsNullOrWhiteSpace(resolvedDescription))
    {
     context.BeatSaverDescription = resolvedDescription;
    }
   }
  }
  catch
  {
   if (needsDescription)
   {
    SetCachedDescription(hashCacheKey, null);
   }

   if (needsBsr)
   {
    SetCachedBsrByHash(hashValue, null);
   }
  }
 }

 private void InvalidateMatchingCaches()
 {
  _excludedCategoriesForMatchingDirty = true;
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

 private bool TryGetCachedBsrByHash(string hash, out string? bsrId)
 {
  bsrId = null;
  var ttlSeconds = GetDescriptionCacheTtlSeconds();
  if (ttlSeconds <= 0)
  {
   return false;
  }

  var now = DateTime.UtcNow;
  lock (_descriptionCacheGate)
  {
   if (!_bsrByHashCache.TryGetValue(hash, out var entry))
   {
    return false;
   }

   if (entry.ExpiresAtUtc <= now)
   {
    _bsrByHashCache.Remove(hash);
    return false;
   }

   entry.ExpiresAtUtc = now.AddSeconds(ttlSeconds);
   bsrId = entry.BsrId;
   return true;
  }
 }

 private void SetCachedBsrByHash(string hash, string? bsrId)
 {
  var ttlSeconds = GetDescriptionCacheTtlSeconds();
  if (ttlSeconds <= 0)
  {
   return;
  }

  var now = DateTime.UtcNow;
  lock (_descriptionCacheGate)
  {
   _bsrByHashCache[hash] = new BsrByHashCacheEntry
   {
    BsrId = bsrId,
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

    if (_bsrByHashCache.Count > 0)
    {
     _bsrByHashCache.Clear();
    }

    _lastDescriptionCacheCleanupUtc = now;
    return;
   }

   var cleanupIntervalSeconds = Math.Min(60, ttlSeconds);
   if ((now - _lastDescriptionCacheCleanupUtc).TotalSeconds < cleanupIntervalSeconds)
   {
    return;
   }

   List<string>? expiredKeys = null;
   foreach (var pair in _descriptionCache)
   {
    if (pair.Value.ExpiresAtUtc > now)
    {
     continue;
    }

    expiredKeys ??= new List<string>();
    expiredKeys.Add(pair.Key);
   }

   if (expiredKeys is not null)
   {
    foreach (var expiredKey in expiredKeys)
    {
     _descriptionCache.Remove(expiredKey);
    }
   }

   List<string>? expiredBsrKeys = null;
   foreach (var pair in _bsrByHashCache)
   {
    if (pair.Value.ExpiresAtUtc > now)
    {
     continue;
    }

    expiredBsrKeys ??= new List<string>();
    expiredBsrKeys.Add(pair.Key);
   }

   if (expiredBsrKeys is not null)
   {
    foreach (var expiredBsrKey in expiredBsrKeys)
    {
     _bsrByHashCache.Remove(expiredBsrKey);
    }
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

  var match = CustomLevelHashRegex.Match(levelId);
  return match.Success ? match.Groups[1].Value : null;
 }

 private void NotifyDataRevisionChanged(long revision)
 {
  try
  {
   DataRevisionChanged?.Invoke(revision);
  }
  catch
  {
   // Notification handlers should not break runtime merge/update flow.
  }
 }

 private sealed class DescriptionCacheEntry
 {
  public string? Description { get; set; }

  public DateTime ExpiresAtUtc { get; set; }
 }

 private sealed class BsrByHashCacheEntry
 {
  public string? BsrId { get; set; }

  public DateTime ExpiresAtUtc { get; set; }
 }

 private static string CreateSignature(AttentionDatabase database)
 {
  return string.Join("\n", database.Entries.Select(AttentionEntryIdentity.BuildKey));
 }
}
