using UnbsAttention.Config;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public sealed class GoogleSpreadsheetSyncProvider : IAttentionSyncProvider
{
 private readonly HttpClient _httpClient;
 private readonly IReadOnlyList<string> _sources;

 public GoogleSpreadsheetSyncProvider(HttpClient httpClient, IReadOnlyList<string> sources)
 {
  _httpClient = httpClient;
  _sources = sources;
 }

 public string Name => "google-sheets";

 public async Task<SubscriptionPullResult> PullLatestWithReportAsync(CancellationToken cancellationToken)
 {
  var report = new SubscriptionRefreshReport
  {
   StartedAtUtc = DateTime.UtcNow,
   TotalSources = _sources.Count,
  };

  var aggregate = new AttentionDatabase();
  var hadAny = false;

  foreach (var source in _sources)
  {
   var item = new SubscriptionSourceRefreshResult
   {
    RawSource = source,
   };

   if (!GoogleSpreadsheetSourceParser.TryBuildCsvExportUrl(source, out var exportUrl))
   {
    item.IsSuccess = false;
    item.Message = "Invalid source format";
    report.FailedSources++;
    report.Sources.Add(item);
    continue;
   }

   item.CsvExportUrl = exportUrl;

   try
   {
    using var response = await _httpClient.GetAsync(exportUrl, cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
     if (GoogleSpreadsheetSourceParser.TryBuildLegacyCsvExportUrl(source, out var legacyExportUrl))
     {
      using var legacyResponse = await _httpClient.GetAsync(legacyExportUrl, cancellationToken).ConfigureAwait(false);
      if (legacyResponse.IsSuccessStatusCode)
      {
       var legacyCsv = await legacyResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
       var legacyParsed = GoogleSpreadsheetCsvAttentionParser.Parse(legacyCsv);
       item.CsvExportUrl = legacyExportUrl;
       item.ImportedRows = legacyParsed.Entries.Count;
       item.IsSuccess = true;
       item.Message = legacyParsed.Entries.Count == 0
        ? "OK (legacy endpoint, 0 rows parsed; check header names and matching rules)"
        : "OK (legacy endpoint)";
       report.SucceededSources++;
       report.ImportedRows += item.ImportedRows;
       report.Sources.Add(item);

       aggregate.MergeFrom(legacyParsed);
       hadAny = true;
       continue;
      }
     }

     var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
     var snippet = string.IsNullOrWhiteSpace(body)
      ? string.Empty
      : " body=" + body.Replace("\r", " ").Replace("\n", " ").Trim().Substring(0, Math.Min(120, body.Trim().Length));
     item.IsSuccess = false;
     item.Message = "HTTP " + (int)response.StatusCode + snippet + " (sheet may require public access)";
     report.FailedSources++;
     report.Sources.Add(item);
     continue;
    }

    var csv = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    var parsed = GoogleSpreadsheetCsvAttentionParser.Parse(csv);
    item.ImportedRows = parsed.Entries.Count;
    item.IsSuccess = true;
    item.Message = parsed.Entries.Count == 0
    ? "OK (0 rows parsed; check header names and matching rules)"
    : "OK";
    report.SucceededSources++;
    report.ImportedRows += item.ImportedRows;
    report.Sources.Add(item);

    aggregate.MergeFrom(parsed);
    hadAny = true;
   }
   catch (Exception ex)
   {
    item.IsSuccess = false;
    item.Message = ex.GetType().Name + ": " + ex.Message;
    report.FailedSources++;
    report.Sources.Add(item);
   }
  }

  report.FinishedAtUtc = DateTime.UtcNow;
  return new SubscriptionPullResult
  {
   Database = hadAny ? aggregate : null,
   Report = report,
  };
 }

 public async Task<AttentionDatabase?> PullLatestAsync(CancellationToken cancellationToken)
 {
  var result = await PullLatestWithReportAsync(cancellationToken).ConfigureAwait(false);
  return result.Database;
 }

 public Task PushAsync(AttentionDatabase database, CancellationToken cancellationToken)
 {
  // スプレッドシート編集は、このランタイムの責務外として扱う。
  return Task.CompletedTask;
 }
}
