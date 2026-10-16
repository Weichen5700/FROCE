using System.Text.Json;

namespace RoceTool.Web;

public sealed class ResearchQueueState
{
    public int SchemaVersion { get; set; } = 1;
    public List<ResearchQueueEntry> Entries { get; set; } = [];
    public string? Error { get; set; }
}

public static class ResearchQueueStatuses
{
    public const string ToReview = "待研究";
    public const string InProgress = "研究中";
    public const string Completed = "已完成";
    public const string Paused = "暫緩";
    public static readonly string[] All = [ToReview, InProgress, Completed, Paused];
}

public sealed class ResearchQueueEntry
{
    public string StockNo { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = ResearchQueueStatuses.ToReview;
    public DateOnly? NextReviewOn { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class ResearchQueueStateFile
{
    private const string FileName = "exception-research-queue.json";
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static ResearchQueueState Load(string dataDirectory)
    {
        lock (Sync) return LoadUnsafe(dataDirectory);
    }

    public static bool TryUpdate(string dataDirectory, Func<ResearchQueueState, bool> update, out string? error)
    {
        lock (Sync)
        {
            var state = LoadUnsafe(dataDirectory);
            if (state.Error is not null)
            {
                error = state.Error;
                return false;
            }
            if (!update(state))
            {
                error = "找不到這筆例外研究項目。";
                return false;
            }

            var path = Path.Combine(dataDirectory, FileName);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, Options));
                File.Move(temporaryPath, path, overwrite: true);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "無法保存本機研究佇列，請確認 App_Data 可寫入。";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static ResearchQueueState LoadUnsafe(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return new ResearchQueueState();
        try
        {
            var state = JsonSerializer.Deserialize<ResearchQueueState>(File.ReadAllText(path), Options)
                ?? throw new JsonException("研究佇列檔案為空。");
            if (state.SchemaVersion != 1 || state.Entries is null || state.Entries.Any(x => x is null || string.IsNullOrWhiteSpace(x.StockNo)))
                throw new JsonException("研究佇列資料格式無效。");
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ResearchQueueState { Error = "本機例外研究佇列無法讀取；為避免覆寫原資料，已停用新增與更新。" };
        }
    }
}
