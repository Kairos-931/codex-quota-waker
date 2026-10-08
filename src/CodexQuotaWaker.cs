using System;
using System.Collections;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("Codex Quota Waker")]
[assembly: AssemblyProduct("Codex Quota Waker")]
[assembly: AssemblyDescription("Wake Windows and run a verifiable local Codex request on schedule.")]
[assembly: AssemblyVersion("0.19.0.0")]
[assembly: AssemblyFileVersion("0.19.0.0")]
[assembly: AssemblyInformationalVersion("0.19.0")]

namespace CodexQuotaWaker
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                AppPaths.EnsureDirectories();

                if (args.Length > 0 && string.Equals(args[0], "--run-scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    string planVersion = args.Length == 3 && args[1] == "--plan-version" ? args[2] : string.Empty;
                    RunRecord record = BackgroundRunner.Execute("scheduled", true, null, planVersion);
                    return record.Success ? 0 : 1;
                }

                if (args.Length > 0 && string.Equals(args[0], "--self-check", StringComparison.OrdinalIgnoreCase))
                {
                    TimelineHistoryStore.ApplicationAccessEnabled = false;
                    string reportPath = args.Length > 1 ? args[1] : Path.Combine(AppPaths.BaseDirectory, "self-check.txt");
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    return SelfCheck.Run(reportPath);
                }

                if (args.Length > 1 && string.Equals(args[0], "--export-task-xml", StringComparison.OrdinalIgnoreCase))
                {
                    AppConfig exportConfig = new AppConfig();
                    exportConfig.FiniteSequenceEnabled = true;
                    exportConfig.SequenceStart = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    exportConfig.SequenceCount = 3;
                    exportConfig.SequenceIntervalMinutes = 125;
                    exportConfig.ExtraWakeTimes.Add(DateTime.Now.AddDays(4).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    string exportSid = WindowsIdentity.GetCurrent().User.Value;
                    File.WriteAllText(
                        args[1],
                        TaskXml.Build(exportConfig, Assembly.GetExecutingAssembly().Location, exportSid),
                        Encoding.Unicode);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Write("FATAL", ex.ToString());
                if (Environment.UserInteractive)
                {
                    MessageBox.Show(
                        "程序启动失败：" + ex.Message + "\r\n\r\n详细信息已写入：\r\n" + AppPaths.LogPath,
                        "Codex 额度唤醒器",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return 2;
            }
        }
    }

    internal static class AppVersion
    {
        public static string Display
        {
            get
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                AssemblyInformationalVersionAttribute version = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .Cast<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                return "v" + (version == null ? assembly.GetName().Version.ToString(3) : version.InformationalVersion);
            }
        }
    }

    internal static class AppPaths
    {
        public static readonly string BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaWaker");

        public static readonly string ConfigPath = Path.Combine(BaseDirectory, "config.json");
        public static readonly string LastRunPath = Path.Combine(BaseDirectory, "last-run.json");
        public static readonly string LastSuccessPath = Path.Combine(BaseDirectory, "last-success.json");
        public static readonly string LogPath = Path.Combine(BaseDirectory, "activity.log");
        public static readonly string ModelCatalogPath = Path.Combine(BaseDirectory, "model-catalog.json");
        public static readonly string ExecutionHistoryPath = Path.Combine(BaseDirectory, "execution-history.json");
        public static readonly string InstalledExePath = Path.Combine(BaseDirectory, "CodexQuotaWaker.exe");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(BaseDirectory);
        }
    }

    internal static class TargetModes
    {
        public const string Official = "official";
        public const string FollowCurrent = "follow-current";

        public static string Normalize(string value)
        {
            return string.Equals(value, FollowCurrent, StringComparison.OrdinalIgnoreCase)
                ? FollowCurrent
                : Official;
        }

        public static string DisplayName(string value)
        {
            return string.Equals(Normalize(value), FollowCurrent, StringComparison.OrdinalIgnoreCase)
                ? "跟随当前 Codex 配置"
                : "官方 Codex 额度";
        }
    }
    internal sealed class AppConfig
    {
        public const string DefaultRequestModel = "gpt-6-luna";
        public const string LegacyDefaultRequestModel = "gpt-5.6-luna";

        public static string[] ListedRequestModels()
        {
            return new[] { "gpt-6-luna", "gpt-6-sol", "gpt-6.1-sol", "gpt-6-astra" };
        }

        public static bool MigrateLegacyDefaultModelToDraft(AppConfig config)
        {
            if (config == null || config.RequestModelSelectionConfirmed || !string.Equals(
                (config.Model ?? string.Empty).Trim(),
                LegacyDefaultRequestModel,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            config.Model = DefaultRequestModel;
            return true;
        }

        // Kept only so older JSON files can still be read. The unified scheduler
        // intentionally never uses the legacy daily-time or mode fields.
        public List<string> Times { get; set; }
        public string Model { get; set; }
        public bool RequestModelSelectionConfirmed { get; set; }
        public string TargetMode { get; set; }
        public string Prompt { get; set; }
        public int RetryCount { get; set; }
        public bool Enabled { get; set; }
        public bool FiniteSequenceEnabled { get; set; }
        public string SequenceStart { get; set; }
        public int SequenceCount { get; set; }
        public int SequenceIntervalMinutes { get; set; }
        public List<string> ExtraWakeTimes { get; set; }
        public bool SleepAfterScheduledWake { get; set; }
        public bool RunWithoutLogon { get; set; }
        public bool LegacyDailyScheduleDetected { get; set; }
        public string ScheduleVersion { get; set; }

        public AppConfig()
        {
            Times = new List<string>();
            Model = DefaultRequestModel;
            TargetMode = TargetModes.Official;
            Prompt = "只回复：请求链路测试成功。不要调用工具。";
            RetryCount = 3;
            Enabled = false;
            FiniteSequenceEnabled = true;
            SequenceStart = string.Empty;
            SequenceCount = 0;
            SequenceIntervalMinutes = 310;
            ExtraWakeTimes = new List<string>();
            SleepAfterScheduledWake = false;
            RunWithoutLogon = false;
            LegacyDailyScheduleDetected = false;
            ScheduleVersion = string.Empty;
        }

        public static AppConfig Normalize(AppConfig config)
        {
            if (config == null)
            {
                return new AppConfig();
            }

            if (config.Times == null)
            {
                config.Times = new List<string>();
            }
            config.Times = ScheduleTime.Normalize(config.Times);
            if (!config.FiniteSequenceEnabled && config.Times.Count > 0)
            {
                // The old daily mode cannot be converted to a dated one-shot
                // schedule without guessing. Keep the old values for recovery,
                // but never execute them in the unified scheduler.
                config.LegacyDailyScheduleDetected = true;
            }
            config.FiniteSequenceEnabled = true;
            if (string.IsNullOrWhiteSpace(config.Model))
            {
                config.Model = DefaultRequestModel;
            }
            config.TargetMode = TargetModes.Normalize(config.TargetMode);
            if (string.IsNullOrWhiteSpace(config.Prompt))
            {
                config.Prompt = "只回复：请求链路测试成功。不要调用工具。";
            }
            if (config.RetryCount < 1 || config.RetryCount > 5)
            {
                config.RetryCount = 3;
            }
            DateTime sequenceStart;
            if (!TryParseSequenceStart(config.SequenceStart, out sequenceStart))
            {
                config.SequenceStart = string.Empty;
            }
            else
            {
                config.SequenceStart = sequenceStart.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            if (config.SequenceCount < 0)
            {
                config.SequenceCount = 0;
            }
            if (config.SequenceIntervalMinutes < 1 || config.SequenceIntervalMinutes > 10080)
            {
                config.SequenceIntervalMinutes = 310;
            }
            if (config.ExtraWakeTimes == null)
            {
                config.ExtraWakeTimes = new List<string>();
            }
            config.ExtraWakeTimes = NormalizeDateTimes(config.ExtraWakeTimes);
            return config;
        }

        public static bool TryParseSequenceStart(string value, out DateTime result)
        {
            return TryParseDateTime(value, out result);
        }

        public static bool TryParseDateTime(string value, out DateTime result)
        {
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result);
        }

        public static List<string> NormalizeDateTimes(IEnumerable<string> values)
        {
            SortedSet<string> result = new SortedSet<string>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (string value in values)
                {
                    DateTime parsed;
                    if (TryParseDateTime(value, out parsed))
                    {
                        result.Add(parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    }
                }
            }
            return result.ToList();
        }
    }

    internal sealed class RunRecord
    {
        public bool Success { get; set; }
        public string Mode { get; set; }
        public string StartedAt { get; set; }
        public string FinishedAt { get; set; }
        public string Model { get; set; }
        public string TargetMode { get; set; }
        public string RouteType { get; set; }
        public string RouteSummary { get; set; }
        public string Provider { get; set; }
        public string BaseUrlKind { get; set; }
        public string SessionId { get; set; }
        public bool OfficialQuotaConfirmed { get; set; }
        public bool TargetSatisfied { get; set; }
        public string QuotaStatus { get; set; }
        public string LimitId { get; set; }
        public int WindowMinutes { get; set; }
        public double? UsedPercent { get; set; }
        public long? ResetsAtUnix { get; set; }
        public string ResetAtLocal { get; set; }
        public bool HasReliableResetTime { get; set; }
        public string ResetTimeDiagnostic { get; set; }
        public string PlanType { get; set; }
        public string QuotaDiagnostic { get; set; }
        public string FinalModel { get; set; }
        public string UpstreamProvider { get; set; }
        public string UpstreamTarget { get; set; }
        public string UpstreamEvidence { get; set; }
        public string RemoteError { get; set; }
        public bool RecommendRouteSwitch { get; set; }
        public int AttemptCount { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public string WakeEvidence { get; set; }
        public string SleepStatus { get; set; }
        public bool SleepRequested { get; set; }
        public string TimelineRunId { get; set; }

        public RunRecord()
        {
            ExitCode = -1;
            TargetMode = TargetModes.Official;
            RouteType = string.Empty;
            RouteSummary = string.Empty;
            Provider = string.Empty;
            BaseUrlKind = string.Empty;
            SessionId = string.Empty;
            QuotaStatus = string.Empty;
            LimitId = string.Empty;
            ResetAtLocal = string.Empty;
            ResetTimeDiagnostic = string.Empty;
            PlanType = string.Empty;
            QuotaDiagnostic = string.Empty;
            FinalModel = string.Empty;
            UpstreamProvider = string.Empty;
            UpstreamTarget = string.Empty;
            UpstreamEvidence = string.Empty;
            RemoteError = string.Empty;
            Output = string.Empty;
            Error = string.Empty;
            WakeEvidence = string.Empty;
            SleepStatus = string.Empty;
        }
    }

    internal sealed class SuccessSnapshot
    {
        public string FinishedAtUtc { get; set; }
        public string Source { get; set; }
        public string FinalModel { get; set; }
        public string Route { get; set; }
        public string EvidenceId { get; set; }
    }

    internal static class RecentSuccessStore
    {
        internal static bool Time(string value, out DateTimeOffset time)
        {
            time = default(DateTimeOffset);
            return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"(?:Z|[+-]\d{2}:\d{2})$")
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        private static string SafeModel(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 80
                && Regex.IsMatch(value, @"^[a-zA-Z0-9][a-zA-Z0-9._/-]*$")
                && !Regex.IsMatch(value, @"^(?:sk-|sess-|eyJ)", RegexOptions.IgnoreCase) ? value : "";
        }

        internal static SuccessSnapshot FromRun(RunRecord record)
        {
            DateTimeOffset started, finished;
            if (record == null || !TimelineExecutionPolicy.BusinessSuccess(record)
                || (record.Mode != "manual" && record.Mode != "scheduled")
                || !Time(record.StartedAt, out started) || !Time(record.FinishedAt, out finished)
                || finished < started || finished > DateTimeOffset.UtcNow) return null;
            string route = "";
            if (!string.IsNullOrWhiteSpace(record.RouteSummary))
            {
                // Store categories from this run only, never endpoints or free-form diagnostic text.
                switch (record.RouteType)
                {
                    case "direct": route = "直接链路"; break;
                    case "cc-switch-local": route = "CC Switch 本地路由"; break;
                    case "local-router": route = "本地路由"; break;
                    case "custom": route = "自定义链路"; break;
                }
            }
            return new SuccessSnapshot { FinishedAtUtc = finished.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Source = record.Mode, FinalModel = SafeModel(record.FinalModel), Route = route,
                EvidenceId = TimelineExecutionPolicy.Hash(record.Mode + "|" + record.StartedAt + "|" + record.FinishedAt) };
        }

        private static SuccessSnapshot FromTimeline(TimelineExecution entry)
        {
            DateTimeOffset started, finished;
            if (entry == null || entry.State != "success" || entry.ExitCode != 0
                || string.IsNullOrWhiteSpace(entry.RunId) || string.IsNullOrWhiteSpace(entry.PlanKey)
                || string.IsNullOrWhiteSpace(entry.PlannedTime) || !Time(entry.StartedAt, out started)
                || !Time(entry.FinishedAt, out finished) || finished < started || finished > DateTimeOffset.UtcNow) return null;
            return new SuccessSnapshot { FinishedAtUtc = finished.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Source = "scheduled", FinalModel = "", Route = "", EvidenceId = TimelineExecutionPolicy.Hash(entry.RunId) };
        }

        internal static string ReadText(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("Record too large.");
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }

        internal static SuccessSnapshot Read(string path, out bool unavailable)
        {
            unavailable = false;
            try
            {
                if (!File.Exists(path)) return null;
                SuccessSnapshot value = new JavaScriptSerializer().Deserialize<SuccessSnapshot>(ReadText(path));
                DateTimeOffset finished;
                if (value == null || !Time(value.FinishedAtUtc, out finished) || finished > DateTimeOffset.UtcNow
                    || (value.Source != "manual" && value.Source != "scheduled")
                    || !Regex.IsMatch(value.EvidenceId ?? "", @"^[A-F0-9]{64}$")) throw new InvalidDataException();
                value.FinalModel = SafeModel(value.FinalModel);
                if (!new[] { "直接链路", "CC Switch 本地路由", "本地路由", "自定义链路" }.Contains(value.Route)) value.Route = "";
                return value;
            }
            catch { unavailable = true; return null; }
        }

        internal static void AtomicWrite(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                for (int attempt = 0; ; attempt++)
                {
                    try { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); break; }
                    catch (IOException) { if (attempt >= 4) throw; Thread.Sleep(50 * (1 << attempt)); }
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static void Locked(string path, Action action)
        {
            using (Mutex mutex = new Mutex(false, "Global\\CodexQuotaWaker-Results-" + TimelineExecutionPolicy.Hash(Path.GetFullPath(path).ToUpperInvariant())))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(5000); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Result store is busy.");
                    action();
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }

        private static void PublishLocked(string path, SuccessSnapshot candidate)
        {
            if (candidate == null) return;
            bool unavailable;
            SuccessSnapshot current = Read(path, out unavailable);
            DateTimeOffset candidateTime, currentTime;
            if (!Time(candidate.FinishedAtUtc, out candidateTime)) return;
            if (current != null && Time(current.FinishedAtUtc, out currentTime) && currentTime >= candidateTime) return;
            AtomicWrite(path, candidate);
        }

        internal static void SaveAttempt(string attemptPath, string successPath, RunRecord record)
        {
            try
            {
                Locked(successPath, delegate
                {
                    // Persist business success before last-attempt metadata (including later sleep updates).
                    try { PublishLocked(successPath, FromRun(record)); }
                    catch { Logger.Write("WARN", "最近成功记录暂未保存；请检查日志目录权限后重试。"); }
                    RunRecord previous = null;
                    try { if (File.Exists(attemptPath)) previous = new JavaScriptSerializer().Deserialize<RunRecord>(ReadText(attemptPath)); } catch { }
                    DateTimeOffset oldTime, newTime;
                    if (previous != null && Time(previous.FinishedAt, out oldTime)
                        && Time(record.FinishedAt, out newTime) && oldTime > newTime) return;
                    AtomicWrite(attemptPath, record);
                });
            }
            catch { Logger.Write("WARN", "最近尝试记录暂未保存；请检查日志目录权限后重试。"); }
        }

        internal static SuccessSnapshot Recover(string path, RunRecord previous, IEnumerable<TimelineExecution> history, out bool unavailable)
        {
            try
            {
                Locked(path, delegate
                {
                    PublishLocked(path, FromRun(previous));
                    foreach (TimelineExecution entry in history ?? Enumerable.Empty<TimelineExecution>()) PublishLocked(path, FromTimeline(entry));
                });
            }
            catch { unavailable = true; return ReadFallback(path); }
            return Read(path, out unavailable);
        }

        private static SuccessSnapshot ReadFallback(string path)
        {
            bool unavailable;
            return Read(path, out unavailable);
        }

        public static SuccessSnapshot LoadForApplication(RunRecord previous, out bool unavailable)
        {
            if (!TimelineHistoryStore.ApplicationAccessEnabled) { unavailable = false; return null; }
            return Recover(AppPaths.LastSuccessPath, previous, TimelineHistoryStore.Load(AppPaths.ExecutionHistoryPath), out unavailable);
        }

        internal static string Summary(SuccessSnapshot success, bool unavailable, RunRecord last, bool attemptUnavailable)
        {
            string text = success == null ? (unavailable ? "最近成功记录暂不可用，请检查日志目录权限。" : "尚无成功联通记录。")
                : "最近一次成功联通：" + LocalTime(success.FinishedAtUtc) + " · " + (success.Source == "manual" ? "手动测试" : "计划执行")
                    + "\r\n" + (string.IsNullOrWhiteSpace(success.FinalModel) ? "未确认实际响应模型" : "实际响应模型：" + success.FinalModel)
                    + "\r\n实际链路：" + (string.IsNullOrWhiteSpace(success.Route) ? "未确认" : success.Route);
            if (attemptUnavailable) text += "\r\n最近尝试记录暂不可用，请查看日志。";
            else if (last != null && last.Success && !TimelineExecutionPolicy.BusinessSuccess(last))
                text += "\r\n最近尝试结果未确认，请查看执行详情。";
            else if (last != null && !last.Success)
                text += "\r\n最近尝试失败：" + LocalTime(last.FinishedAt) + "\r\n原因：" + FailureReason(last)
                    + "\r\n下一步：" + RouteTestPresentation.FailureNextStep(last);
            return text;
        }

        private static string LocalTime(string value)
        {
            DateTimeOffset time;
            return Time(value, out time) ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "时间未确认";
        }

        private static string FailureReason(RunRecord last)
        {
            // A bounded vocabulary avoids exposing credentials embedded in remote errors.
            string error = last.Error ?? "";
            if (error.Contains("超时")) return "请求超时";
            if (error.Contains("网络")) return "网络未恢复";
            if (error.Contains("找不到 Codex CLI")) return "找不到 Codex CLI";
            if (error.Contains("最终模型回复")) return "未收到有效模型回复";
            if (error.Contains("正在运行")) return "已有请求正在运行，本次跳过";
            if (error.Contains("暂停")) return "计划已暂停，本次跳过";
            return "请求未成功（退出码 " + last.ExitCode.ToString(CultureInfo.InvariantCulture) + "），请查看执行详情";
        }
    }

    internal static class RecentSuccessChecks
    {
        internal static RunRecord Fixture(string mode, DateTimeOffset finished)
        {
            return new RunRecord { Mode = mode, StartedAt = finished.AddSeconds(-20).ToString("o"), FinishedAt = finished.ToString("o"),
                Success = true, ExitCode = 0, AttemptCount = 1, Output = "fixture reply", FinalModel = "gpt-6-luna",
                RouteType = "direct", RouteSummary = "fixture", Error = "Bearer secret-cookie", Model = "requested-model" };
        }

        // Invoked by isolated Windows PowerShell children to test the actual cross-process mutex.
        internal static void WriteFixture(string directory, int index)
        {
            string root = Path.GetFullPath(Path.GetTempPath());
            string full = Path.GetFullPath(directory);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("CodexQuotaWaker-Results-", StringComparison.Ordinal)) throw new InvalidOperationException();
            DateTimeOffset time = DateTimeOffset.Parse("2026-01-01T01:00:00Z", CultureInfo.InvariantCulture).AddMinutes(index);
            for (int i = 0; i < 12; i++) RecentSuccessStore.SaveAttempt(Path.Combine(full, "attempt.json"),
                Path.Combine(full, "concurrent.json"), Fixture(index == 0 ? "manual" : "scheduled", time));
        }

        internal static bool Verify(out string evidence)
        {
            List<string> checks = new List<string>();
            bool ok = true;
            Action<string, bool> check = delegate(string name, bool value) { checks.Add(name + "=" + value); ok &= value; };
            string directory = Path.Combine(Path.GetTempPath(), "CodexQuotaWaker-Results-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string successPath = Path.Combine(directory, "success.json"), attemptPath = Path.Combine(directory, "last.json");
            bool unavailable;
            DateTimeOffset now = DateTimeOffset.UtcNow.AddHours(-1);
            try
            {
                RunRecord manual = Fixture("manual", now), scheduled = Fixture("scheduled", now.AddMinutes(1));
                RecentSuccessStore.SaveAttempt(attemptPath, successPath, manual);
                RecentSuccessStore.SaveAttempt(attemptPath, successPath, scheduled);
                SuccessSnapshot read = RecentSuccessStore.Read(successPath, out unavailable);
                check("manualThenScheduled", read.Source == "scheduled" && read.FinishedAtUtc == scheduled.FinishedAt);
                manual.FinishedAt = now.AddMinutes(2).ToString("o");
                RecentSuccessStore.SaveAttempt(attemptPath, successPath, manual);
                check("scheduledThenManual", RecentSuccessStore.Read(successPath, out unavailable).Source == "manual");
                RunRecord failure = Fixture("scheduled", now.AddMinutes(3)); failure.Success = false; failure.ExitCode = 1;
                failure.FinalModel = "gpt-other-failure"; failure.RouteType = "custom"; failure.Error = "网络超时 Bearer secret-cookie";
                RecentSuccessStore.SaveAttempt(attemptPath, successPath, failure);
                read = RecentSuccessStore.Read(successPath, out unavailable);
                string summary = RecentSuccessStore.Summary(read, false, failure, false);
                check("failurePreservesSuccessAndSeparatesAttempt", read.Source == "manual" && summary.Contains("手动测试")
                    && summary.Contains("最近尝试失败") && summary.Contains("下一步") && !summary.Contains("gpt-other-failure") && !summary.Contains("secret-cookie"));
                RecentSuccessStore.SaveAttempt(attemptPath, successPath, scheduled);
                check("olderDoesNotOverwriteEitherRecord", RecentSuccessStore.Read(successPath, out unavailable).Source == "manual"
                    && JsonStore.Deserialize<RunRecord>(RecentSuccessStore.ReadText(attemptPath)).ExitCode == 1);
                RunRecord unknownModel = Fixture("scheduled", now.AddMinutes(4)); unknownModel.FinalModel = "";
                RecentSuccessStore.SaveAttempt(attemptPath, successPath, unknownModel);
                read = RecentSuccessStore.Read(successPath, out unavailable);
                check("unknownModelStillSucceeds", RecentSuccessStore.Summary(read, false, unknownModel, false).Contains("未确认实际响应模型"));
                string persisted = File.ReadAllText(successPath);
                check("minimalSafeSnapshot", !persisted.Contains("fixture reply") && !persisted.Contains("secret-cookie") && !persisted.Contains("requested-model"));
                foreach (int invalid in new[] { 0, 1, 2, 3 })
                {
                    RunRecord invalidRun = Fixture("manual", now.AddMinutes(5));
                    if (invalid == 0) invalidRun.ExitCode = 1;
                    if (invalid == 1) invalidRun.Output = "";
                    if (invalid == 2) invalidRun.Output = "已收到 Codex 结构化响应";
                    if (invalid == 3) invalidRun.AttemptCount = 0;
                    RecentSuccessStore.SaveAttempt(attemptPath, successPath, invalidRun);
                }
                check("invalidBusinessEvidenceRejected", File.ReadAllText(successPath) == persisted);
                string migration = Path.Combine(directory, "migration.json");
                read = RecentSuccessStore.Recover(migration, scheduled, null, out unavailable);
                string migrationBefore = File.ReadAllText(migration);
                RecentSuccessStore.Recover(migration, scheduled, null, out unavailable);
                check("legacySuccessMigratesIdempotently", read.Source == "scheduled" && migrationBefore == File.ReadAllText(migration));
                string empty = Path.Combine(directory, "empty.json");
                check("failureCannotInventSuccess", RecentSuccessStore.Recover(empty, failure, null, out unavailable) == null && !File.Exists(empty));
                List<TimelineExecution> history = new List<TimelineExecution> { new TimelineExecution { State = "success", ExitCode = 0,
                    RunId = "trusted", PlanKey = "same-plan", PlannedTime = "2026-01-01 00:00", StartedAt = now.ToString("o"), FinishedAt = now.AddMinutes(6).ToString("o") } };
                read = RecentSuccessStore.Recover(empty, failure, history, out unavailable);
                check("timelineMigrationDoesNotBorrowFields", read.Source == "scheduled" && read.FinalModel == "" && read.Route == ""
                    && RecentSuccessStore.Summary(read, false, failure, false).Contains("实际链路：未确认"));
                File.WriteAllText(empty, "{broken");
                check("corruptionIsUnavailable", RecentSuccessStore.Recover(empty, failure, null, out unavailable) == null && unavailable);
                check("unknownNotFailure", RecentSuccessStore.Summary(null, true, null, true).Contains("暂不可用")
                    && !RecentSuccessStore.Summary(null, true, null, true).Contains("失败"));
                File.WriteAllText(empty, JsonStore.Serialize(new SuccessSnapshot { FinishedAtUtc = now.ToString("o"), Source = "scheduled", EvidenceId = "unproven" }));
                check("invalidSnapshotRejected", RecentSuccessStore.Read(empty, out unavailable) == null && unavailable);

                string executable = Assembly.GetExecutingAssembly().Location;
                List<Process> workers = new List<Process>();
                try
                {
                    for (int index = 1; index >= 0; index--)
                    {
                        string script = "$ErrorActionPreference='Stop'; $a=[Reflection.Assembly]::LoadFrom('" + executable.Replace("'", "''")
                            + "'); $t=$a.GetType('CodexQuotaWaker.RecentSuccessChecks'); $m=$t.GetMethod('WriteFixture',[Reflection.BindingFlags]'Static,NonPublic');"
                            + " $m.Invoke($null,[object[]]@('" + directory.Replace("'", "''") + "',[int]" + index + ")) | Out-Null";
                        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                            "WindowsPowerShell\\v1.0\\powershell.exe"), "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
                        info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
                        workers.Add(Process.Start(info));
                    }
                    bool readersOk = true;
                    Stopwatch waiting = Stopwatch.StartNew();
                    while (workers.Any(delegate(Process worker) { return !worker.HasExited; }) && waiting.ElapsedMilliseconds < 20000)
                    {
                        bool readUnavailable;
                        RecentSuccessStore.Read(Path.Combine(directory, "concurrent.json"), out readUnavailable);
                        readersOk &= !readUnavailable;
                        Thread.Sleep(10);
                    }
                    bool workersOk = true;
                    foreach (Process worker in workers) workersOk &= worker.WaitForExit(20000) && worker.ExitCode == 0;
                    read = RecentSuccessStore.Read(Path.Combine(directory, "concurrent.json"), out unavailable);
                    check("crossProcessAtomicLatestWinsAndRestart", readersOk && workersOk && read != null && read.Source == "scheduled"
                        && DateTimeOffset.Parse(read.FinishedAtUtc).Minute == 1
                        && JsonStore.Deserialize<RunRecord>(RecentSuccessStore.ReadText(Path.Combine(directory, "attempt.json"))).Mode == "scheduled");
                }
                finally { foreach (Process worker in workers) { if (!worker.HasExited) worker.Kill(); worker.Dispose(); } }
            }
            catch (Exception ex) { check("exception:" + ex.GetType().Name, false); }
            finally
            {
                string full = Path.GetFullPath(directory);
                if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith("CodexQuotaWaker-Results-", StringComparison.Ordinal)) Directory.Delete(full, true);
            }
            evidence = string.Join("; ", checks);
            return ok;
        }
    }

    internal static class JsonStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static AppConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigPath))
                {
                    return new AppConfig();
                }
                return AppConfig.Normalize(Serializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigPath, Encoding.UTF8)));
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "读取配置失败，已使用默认配置：" + ex.Message);
                return new AppConfig();
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(AppPaths.ConfigPath, Serializer.Serialize(AppConfig.Normalize(config)), new UTF8Encoding(false));
        }

        public static RunRecord LoadLastRun()
        {
            try
            {
                if (!File.Exists(AppPaths.LastRunPath))
                {
                    return null;
                }
                return new JavaScriptSerializer().Deserialize<RunRecord>(RecentSuccessStore.ReadText(AppPaths.LastRunPath));
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "读取最近运行结果失败：" + ex.Message);
                return null;
            }
        }

        public static void SaveLastRun(RunRecord record)
        {
            RecentSuccessStore.SaveAttempt(AppPaths.LastRunPath, AppPaths.LastSuccessPath, record);
        }

        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }

        public static T Deserialize<T>(string value)
        {
            return Serializer.Deserialize<T>(value);
        }
    }

    internal static class Logger
    {
        private static readonly object Sync = new object();

        public static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    AppPaths.EnsureDirectories();
                    string safe = (message ?? string.Empty).Replace("\r\n", " | ").Replace("\n", " | ");
                    File.AppendAllText(
                        AppPaths.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" + level + "] " + safe + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never hide the original failure.
            }
        }
    }

    internal static class ScheduleTime
    {
        public static bool TryParse(string text, out DateTime time)
        {
            return DateTime.TryParseExact(
                text,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time);
        }

        public static List<string> Normalize(IEnumerable<string> values)
        {
            SortedSet<string> result = new SortedSet<string>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (string value in values)
                {
                    DateTime parsed;
                    if (TryParse(value, out parsed))
                    {
                        result.Add(parsed.ToString("HH:mm", CultureInfo.InvariantCulture));
                    }
                }
            }
            return result.ToList();
        }

        public static DateTime? NextRun(IEnumerable<string> values, DateTime now)
        {
            List<DateTime> candidates = new List<DateTime>();
            foreach (string value in Normalize(values))
            {
                DateTime parsed;
                if (!TryParse(value, out parsed))
                {
                    continue;
                }
                DateTime candidate = now.Date.AddHours(parsed.Hour).AddMinutes(parsed.Minute);
                if (candidate <= now)
                {
                    candidate = candidate.AddDays(1);
                }
                candidates.Add(candidate);
            }
            return candidates.Count == 0 ? (DateTime?)null : candidates.Min();
        }
    }

    internal static class SequenceSchedule
    {
        public const int DefaultIntervalMinutes = 310;

        public static int GetIntervalMinutes(AppConfig config)
        {
            return config != null && config.SequenceIntervalMinutes >= 1 && config.SequenceIntervalMinutes <= 10080
                ? config.SequenceIntervalMinutes
                : DefaultIntervalMinutes;
        }

        public static DateTime? NextRun(AppConfig config, DateTime now)
        {
            DateTime start;
            if (config == null
                || config.SequenceCount <= 0
                || !AppConfig.TryParseSequenceStart(config.SequenceStart, out start))
            {
                return null;
            }

            if (start > now)
            {
                return start;
            }

            int intervalMinutes = GetIntervalMinutes(config);
            long nextIndex = ((long)Math.Floor((now - start).TotalMinutes / intervalMinutes)) + 1L;
            if (nextIndex >= config.SequenceCount)
            {
                return null;
            }
            try
            {
                return start.AddMinutes(nextIndex * intervalMinutes);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        public static bool ContainsRun(AppConfig config, DateTime value)
        {
            DateTime start;
            if (config == null
                || config.SequenceCount <= 0
                || !AppConfig.TryParseSequenceStart(config.SequenceStart, out start))
            {
                return false;
            }

            int intervalMinutes = GetIntervalMinutes(config);
            TimeSpan delta = value - start;
            if (delta.Ticks < 0 || delta.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                return false;
            }

            long index = delta.Ticks / TimeSpan.TicksPerMinute / intervalMinutes;
            if (index < 0 || index >= config.SequenceCount)
            {
                return false;
            }

            try
            {
                return start.AddMinutes(index * (long)intervalMinutes) == value;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public static DateTime? LastRun(AppConfig config)
        {
            DateTime start;
            if (config == null
                || config.SequenceCount <= 0
                || !AppConfig.TryParseSequenceStart(config.SequenceStart, out start))
            {
                return null;
            }
            try
            {
                return start.AddMinutes((long)(config.SequenceCount - 1) * GetIntervalMinutes(config));
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        public static string RepetitionDuration(int count, int intervalMinutes)
        {
            if (count <= 1)
            {
                return string.Empty;
            }

            long totalMinutes = ((long)(count - 1) * intervalMinutes) + 1L;
            long days = totalMinutes / 1440L;
            long remainder = totalMinutes % 1440L;
            long hours = remainder / 60L;
            long minutes = remainder % 60L;
            StringBuilder duration = new StringBuilder("P");
            if (days > 0)
            {
                duration.Append(days).Append("D");
            }
            duration.Append("T");
            if (hours > 0)
            {
                duration.Append(hours).Append("H");
            }
            if (minutes > 0 || hours == 0)
            {
                duration.Append(minutes).Append("M");
            }
            return duration.ToString();
        }
    }

    internal sealed class RequestModelCatalogEntry
    {
        public string Slug { get; set; }
        public string DisplayName { get; set; }
    }

    internal sealed class RequestModelCatalogSnapshot
    {
        public string RefreshedAtUtc { get; set; }
        public List<RequestModelCatalogEntry> Models { get; set; }

        public RequestModelCatalogSnapshot()
        {
            RefreshedAtUtc = string.Empty;
            Models = new List<RequestModelCatalogEntry>();
        }
    }

    internal sealed class RequestModelCatalogRefreshResult
    {
        public bool Success { get; set; }
        public bool TimedOut { get; set; }
        public bool CacheSaved { get; set; }
        public RequestModelCatalogSnapshot Snapshot { get; set; }
    }

    internal static class RequestModelCatalogService
    {
        public const int TimeoutMilliseconds = 10000;
        private const int MaxModels = 256;
        private static readonly Regex SlugPattern = new Regex(
            "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z",
            RegexOptions.CultureInvariant);

        public static RequestModelCatalogSnapshot CreateBuiltIn()
        {
            RequestModelCatalogSnapshot snapshot = new RequestModelCatalogSnapshot();
            string[] slugs = AppConfig.ListedRequestModels();
            foreach (string slug in slugs)
            {
                snapshot.Models.Add(new RequestModelCatalogEntry
                {
                    Slug = slug,
                    DisplayName = slug
                });
            }
            return snapshot;
        }

        public static RequestModelCatalogSnapshot LoadOrFallback(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    RequestModelCatalogSnapshot cached = ParseCache(File.ReadAllText(path));
                    if (cached != null)
                    {
                        return cached;
                    }
                }
            }
            catch
            {
            }
            return CreateBuiltIn();
        }

        public static RequestModelCatalogSnapshot ParseCacheOrFallback(string json)
        {
            RequestModelCatalogSnapshot cached = ParseCache(json);
            return cached ?? CreateBuiltIn();
        }

        public static RequestModelCatalogSnapshot ParseCache(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                IDictionary<string, object> root = DeserializeDictionary(json);
                if (root == null)
                {
                    return null;
                }

                string refreshedAt = GetString(root, "RefreshedAtUtc");
                DateTimeOffset parsedTime;
                if (!DateTimeOffset.TryParse(
                    refreshedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsedTime))
                {
                    return null;
                }

                object rawModels;
                if (!root.TryGetValue("Models", out rawModels))
                {
                    return null;
                }
                IEnumerable modelArray = rawModels as IEnumerable;
                if (modelArray == null || rawModels is string)
                {
                    return null;
                }

                List<RequestModelCatalogEntry> entries = new List<RequestModelCatalogEntry>();
                foreach (object rawEntry in modelArray)
                {
                    IDictionary<string, object> entry = rawEntry as IDictionary<string, object>;
                    if (entry == null)
                    {
                        continue;
                    }
                    entries.Add(new RequestModelCatalogEntry
                    {
                        Slug = GetString(entry, "Slug"),
                        DisplayName = GetString(entry, "DisplayName")
                    });
                }

                List<RequestModelCatalogEntry> normalized = NormalizeEntries(entries);
                if (normalized.Count == 0)
                {
                    return null;
                }

                return new RequestModelCatalogSnapshot
                {
                    RefreshedAtUtc = parsedTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    Models = normalized
                };
            }
            catch
            {
                return null;
            }
        }

        public static RequestModelCatalogRefreshResult Evaluate(ProcessResult process)
        {
            RequestModelCatalogRefreshResult result = new RequestModelCatalogRefreshResult();
            if (process == null)
            {
                return result;
            }
            if (process.TimedOut)
            {
                result.TimedOut = true;
                return result;
            }
            if (process.ExitCode != 0)
            {
                return result;
            }

            RequestModelCatalogSnapshot parsed = ParseCliCatalog(process.StandardOutput);
            if (parsed == null)
            {
                return result;
            }

            parsed.RefreshedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            result.Success = true;
            result.Snapshot = parsed;
            return result;
        }

        public static RequestModelCatalogRefreshResult Refresh(string codexPath, string cachePath)
        {
            if (string.IsNullOrWhiteSpace(codexPath) || !File.Exists(codexPath))
            {
                return new RequestModelCatalogRefreshResult();
            }

            try
            {
                ProcessResult process = ProcessRunner.Run(
                    codexPath,
                    "debug models",
                    AppPaths.BaseDirectory,
                    TimeoutMilliseconds);
                RequestModelCatalogRefreshResult result = Evaluate(process);
                if (!result.Success)
                {
                    return result;
                }
                result.CacheSaved = TrySaveCache(cachePath, result.Snapshot);
                return result;
            }
            catch
            {
                return new RequestModelCatalogRefreshResult();
            }
        }

        public static RequestModelCatalogSnapshot ParseCliCatalog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                IDictionary<string, object> root = DeserializeDictionary(json);
                if (root == null)
                {
                    return null;
                }

                object rawModels;
                if (!root.TryGetValue("models", out rawModels))
                {
                    return null;
                }
                IEnumerable modelArray = rawModels as IEnumerable;
                if (modelArray == null || rawModels is string)
                {
                    return null;
                }

                List<RequestModelCatalogEntry> entries = new List<RequestModelCatalogEntry>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object rawEntry in modelArray)
                {
                    IDictionary<string, object> entry = rawEntry as IDictionary<string, object>;
                    if (entry == null
                        || !string.Equals(GetString(entry, "visibility"), "list", StringComparison.OrdinalIgnoreCase)
                        || !GetBoolean(entry, "supported_in_api"))
                    {
                        continue;
                    }

                    string slug = (GetString(entry, "slug") ?? string.Empty).Trim();
                    if (!IsSafeSlug(slug) || !seen.Add(slug))
                    {
                        continue;
                    }

                    entries.Add(new RequestModelCatalogEntry
                    {
                        Slug = slug,
                        DisplayName = NormalizeDisplayName(GetString(entry, "display_name"), slug)
                    });
                    if (entries.Count >= MaxModels)
                    {
                        break;
                    }
                }

                if (entries.Count == 0)
                {
                    return null;
                }
                return new RequestModelCatalogSnapshot { Models = entries };
            }
            catch
            {
                return null;
            }
        }

        public static string SerializeCache(RequestModelCatalogSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            RequestModelCatalogSnapshot safe = new RequestModelCatalogSnapshot
            {
                RefreshedAtUtc = snapshot.RefreshedAtUtc ?? string.Empty,
                Models = NormalizeEntries(snapshot.Models)
            };
            return CreateSerializer().Serialize(safe);
        }

        public static bool TrySaveCache(string path, RequestModelCatalogSnapshot snapshot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || snapshot == null || snapshot.Models == null || snapshot.Models.Count == 0)
                {
                    return false;
                }
                AppPaths.EnsureDirectories();
                File.WriteAllText(path, SerializeCache(snapshot), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IDictionary<string, object> DeserializeDictionary(string json)
        {
            JavaScriptSerializer serializer = CreateSerializer();
            return serializer.DeserializeObject(json) as IDictionary<string, object>;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = 8 * 1024 * 1024,
                RecursionLimit = 100
            };
        }

        private static List<RequestModelCatalogEntry> NormalizeEntries(IEnumerable<RequestModelCatalogEntry> entries)
        {
            List<RequestModelCatalogEntry> normalized = new List<RequestModelCatalogEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
            {
                return normalized;
            }

            foreach (RequestModelCatalogEntry entry in entries)
            {
                string slug = entry == null ? string.Empty : (entry.Slug ?? string.Empty).Trim();
                if (!IsSafeSlug(slug) || !seen.Add(slug))
                {
                    continue;
                }
                normalized.Add(new RequestModelCatalogEntry
                {
                    Slug = slug,
                    DisplayName = NormalizeDisplayName(entry.DisplayName, slug)
                });
                if (normalized.Count >= MaxModels)
                {
                    break;
                }
            }
            return normalized;
        }

        private static bool IsSafeSlug(string slug)
        {
            return !string.IsNullOrWhiteSpace(slug) && SlugPattern.IsMatch(slug);
        }

        private static string NormalizeDisplayName(string value, string slug)
        {
            string name = (value ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 100 || name.Any(char.IsControl))
            {
                return slug;
            }
            return name;
        }

        private static string GetString(IDictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) ? value as string : null;
        }

        private static bool GetBoolean(IDictionary<string, object> values, string key)
        {
            object value;
            return values != null
                && values.TryGetValue(key, out value)
                && value is bool
                && (bool)value;
        }
    }

    internal static class SequenceCountInput
    {
        public static bool TryParse(string text, out int count)
        {
            return int.TryParse(
                (text ?? string.Empty).Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count)
                && count >= 0;
        }
    }

    internal static class SequenceStartShortcut
    {
        public static bool IsEnabled()
        {
            return true;
        }

        public static DateTime NextMinute(DateTime now)
        {
            DateTime minute = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                now.Minute,
                0,
                now.Kind);
            return minute.AddMinutes(1);
        }
    }

    internal sealed class TimelineExecution
    {
        public string RunId { get; set; }
        public string PlanKey { get; set; }
        public string PlannedTime { get; set; }
        public string StartedAt { get; set; }
        public string FinishedAt { get; set; }
        public string State { get; set; }
        public int OwnerPid { get; set; }
        public string OwnerStartedAt { get; set; }
        public string ExpiresAt { get; set; }
        public int ExitCode { get; set; }
        public string Diagnostic { get; set; }
    }

    internal static class TimelineExecutionPolicy
    {
        public const string Unknown = "已到期·结果未确认";

        public static string Arguments(AppConfig config)
        {
            Guid version;
            return config != null && Guid.TryParse(config.ScheduleVersion, out version)
                ? "--run-scheduled --plan-version " + version.ToString("N") : "--run-scheduled";
        }

        public static string Hash(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "");
        }

        public static string PlanKey(AppConfig config, DateTime savedUtc)
        {
            string signature = TriggerSignature(TaskXml.Build(config, AppPaths.InstalledExePath, "fixture"));
            return (string.IsNullOrWhiteSpace(config.ScheduleVersion)
                ? "legacy-" + savedUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
                : config.ScheduleVersion) + ":" + Hash(signature);
        }

        private static string NodeText(XmlNode parent, string path, string fallback)
        {
            XmlNode node = parent.SelectSingleNode(path);
            return node == null ? fallback : node.InnerText.Trim();
        }

        private static string Duration(string value)
        {
            return XmlConvert.ToTimeSpan(value).Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private static string TriggerSignature(string xml)
        {
            XmlDocument document = new XmlDocument();
            document.LoadXml(xml);
            List<string> signatures = new List<string>();
            foreach (XmlNode trigger in document.SelectNodes("//*[local-name()='Triggers']/*"))
            {
                if (trigger.LocalName != "TimeTrigger") throw new InvalidOperationException("Unexpected trigger");
                string startText = NodeText(trigger, "./*[local-name()='StartBoundary']", "");
                DateTime start = DateTime.Parse(startText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (start.Kind == DateTimeKind.Utc) start = start.ToLocalTime();
                string repetition = "";
                XmlNode repeat = trigger.SelectSingleNode("./*[local-name()='Repetition']");
                if (repeat != null)
                    repetition = Duration(NodeText(repeat, "./*[local-name()='Interval']", "PT0S")) + "/"
                        + Duration(NodeText(repeat, "./*[local-name()='Duration']", "PT0S")) + "/"
                        + NodeText(repeat, "./*[local-name()='StopAtDurationEnd']", "false");
                signatures.Add(start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "|" + repetition + "|"
                    + NodeText(trigger, "./*[local-name()='Enabled']", "true") + "|"
                    + NodeText(trigger, "./*[local-name()='EndBoundary']", "") + "|"
                    + Duration(NodeText(trigger, "./*[local-name()='RandomDelay']", "PT0S")));
            }
            signatures.Sort(StringComparer.Ordinal);
            return string.Join(";", signatures);
        }

        public static bool InstalledMatches(AppConfig config, WindowsTaskStatus status)
        {
            try
            {
                if (!status.Exists || string.IsNullOrWhiteSpace(status.DefinitionXml)) return false;
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(status.DefinitionXml);
                XmlNode actions = xml.SelectSingleNode("//*[local-name()='Actions']");
                if (actions == null || actions.ChildNodes.Count != 1 || actions.FirstChild.LocalName != "Exec") return false;
                string args = NodeText(actions.FirstChild, "./*[local-name()='Arguments']", "");
                string command = NodeText(actions.FirstChild, "./*[local-name()='Command']", "");
                if (!string.Equals(Path.GetFullPath(command), Path.GetFullPath(AppPaths.InstalledExePath), StringComparison.OrdinalIgnoreCase)) return false;
                Guid version;
                if (!string.IsNullOrWhiteSpace(config.ScheduleVersion) && !Guid.TryParse(config.ScheduleVersion, out version)) return false;
                return args == Arguments(config) && TriggerSignature(status.DefinitionXml)
                    == TriggerSignature(TaskXml.Build(config, AppPaths.InstalledExePath, "fixture"));
            }
            catch { return false; }
        }

        public static PlannedRun Associate(AppConfig config, WindowsTaskStatus status, DateTime started,
            DateTime savedUtc, IEnumerable<TimelineExecution> history, string invokedVersion)
        {
            if (!InstalledMatches(config, status) || !status.LastRunTime.HasValue
                || !string.Equals(config.ScheduleVersion ?? "", invokedVersion ?? "", StringComparison.Ordinal)) return null;
            DateTime launch = status.LastRunTime.Value;
            // The Windows launch corroborates this request. The short startup window
            // never selects an entry: there must be exactly one unclaimed due entry.
            if (started < launch || (started - launch).TotalSeconds > 90 || savedUtc.ToUniversalTime() > launch.ToUniversalTime()) return null;
            return UniqueDue(config, launch, savedUtc, history);
        }

        internal static PlannedRun UniqueDue(AppConfig config, DateTime through, DateTime savedUtc, IEnumerable<TimelineExecution> history)
        {
            string key = PlanKey(config, savedUtc);
            HashSet<string> claimed = new HashSet<string>(history.Where(delegate(TimelineExecution entry) { return entry.PlanKey == key; })
                .Select(delegate(TimelineExecution entry) { return entry.PlannedTime; }), StringComparer.Ordinal);
            List<PlannedRun> due = SchedulePlanner.Build(config, through, 14).Where(delegate(PlannedRun item)
            {
                return item.Time <= through && !claimed.Contains(item.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }).Take(2).ToList();
            return due.Count == 1 ? due[0] : null;
        }

        public static bool BusinessSuccess(RunRecord record)
        {
            return record.Success && record.ExitCode == 0 && record.AttemptCount > 0
                && !string.IsNullOrWhiteSpace(record.Output)
                && !record.Output.StartsWith("已收到 Codex 结构化响应", StringComparison.Ordinal);
        }

        public static bool ProcessAlive(TimelineExecution entry)
        {
            try
            {
                using (Process owner = Process.GetProcessById(entry.OwnerPid))
                    return !owner.HasExited && owner.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) == entry.OwnerStartedAt;
            }
            catch { return false; }
        }

        public static string State(PlannedRun item, string key, IEnumerable<TimelineExecution> history, DateTime now, bool draft)
        {
            if (!draft)
            {
                string time = item.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                TimelineExecution entry = history.Where(delegate(TimelineExecution run) { return run.PlanKey == key && run.PlannedTime == time; })
                    .OrderByDescending(delegate(TimelineExecution run) { return run.StartedAt; }).FirstOrDefault();
                if (entry != null)
                {
                    if (entry.State == "success") return "执行成功";
                    if (entry.State == "failure") return "执行失败";
                    DateTimeOffset expires;
                    if (entry.State == "running" && DateTimeOffset.TryParse(entry.ExpiresAt, out expires)
                        && expires > new DateTimeOffset(now) && ProcessAlive(entry)) return "执行中";
                }
            }
            return item.Time <= now ? Unknown : "待执行";
        }
    }

    internal sealed class LegacyLogExecution
    {
        public DateTime Started { get; set; }
        public DateTime Finished { get; set; }
        public string Model { get; set; }
        public string State { get; set; }
    }

    internal static class LegacyTimelineEvidence
    {
        private static readonly Regex Line = new Regex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[(?<level>[A-Z]+)\] (?<message>.*)$");
        private static readonly Regex Begin = new Regex(@"^开始 (?<mode>[a-z-]+) 触发；model=(?<model>[^；;]+)");

        internal static List<LegacyLogExecution> Parse(IEnumerable<string> lines)
        {
            List<LegacyLogExecution> completed = new List<LegacyLogExecution>();
            LegacyLogExecution current = null;
            bool acceptedResponse = false;
            bool blocked = false;
            DateTime previous = DateTime.MinValue;
            foreach (string line in lines)
            {
                Match parsed = Line.Match(line ?? "");
                DateTime time;
                if (!parsed.Success || !DateTime.TryParseExact(parsed.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;
                time = DateTime.SpecifyKind(time, DateTimeKind.Local);
                if (time < previous || TimeZoneInfo.Local.IsAmbiguousTime(time) || TimeZoneInfo.Local.IsInvalidTime(time))
                {
                    current = null;
                    blocked = true;
                    continue;
                }
                previous = time;
                string message = parsed.Groups["message"].Value;
                string level = parsed.Groups["level"].Value;
                Match begin = Begin.Match(message);
                if (level == "INFO" && begin.Success)
                {
                    blocked = current != null;
                    acceptedResponse = false;
                    current = new LegacyLogExecution { Started = time, Model = begin.Groups["model"].Value,
                        State = begin.Groups["mode"].Value == "scheduled" ? "scheduled" : "excluded" };
                    continue;
                }
                if (current == null) continue;
                if (level == "INFO" && message.StartsWith("Codex 请求成功并获得有效模型响应；", StringComparison.Ordinal)) acceptedResponse = true;
                bool success = level == "SUCCESS" && message.Trim() == "触发完成。";
                bool failure = level == "FAIL";
                if (!success && !failure) continue;
                double seconds = (time - current.Started).TotalSeconds;
                if (!blocked && current.State == "scheduled" && seconds >= 0
                    && seconds <= ExecutionBudget.TotalSeconds(5) + 90 && (success ? acceptedResponse : !acceptedResponse))
                {
                    current.Finished = time;
                    current.State = success ? "success" : "failure";
                    completed.Add(current);
                }
                current = null;
                acceptedResponse = false;
                blocked = false;
            }
            return completed;
        }

        internal static void Recover(AppConfig config, WindowsTaskStatus status, RunRecord last, DateTime savedUtc,
            IEnumerable<string> lines, List<TimelineExecution> history, DateTimeOffset now)
        {
            DateTimeOffset latestStarted, latestFinished;
            if (!string.IsNullOrWhiteSpace(config.ScheduleVersion) || !TimelineExecutionPolicy.InstalledMatches(config, status)
                || !status.DefinitionSavedUtc.HasValue || last == null
                || !DateTimeOffset.TryParse(last.StartedAt, out latestStarted) || !DateTimeOffset.TryParse(last.FinishedAt, out latestFinished)) return;
            foreach (LegacyLogExecution run in Parse(lines))
            {
                if (run.Model != config.Model || run.Started.ToUniversalTime() < savedUtc.ToUniversalTime()
                    || run.Started.ToUniversalTime() < status.DefinitionSavedUtc.Value.ToUniversalTime()
                    || new DateTimeOffset(run.Finished) > now
                    || TimeZoneInfo.Local.GetUtcOffset(run.Started) != latestStarted.Offset) continue;
                // Corroborate the most recent log block against its structured result.
                // Earlier blocks require their own accepted-response/terminal evidence.
                bool isLatest = Math.Abs((run.Started - latestStarted.LocalDateTime).TotalSeconds) <= 90;
                if (isLatest)
                {
                    if (last.Mode != "scheduled" || latestFinished < latestStarted
                        || Math.Abs((run.Finished - latestFinished.LocalDateTime).TotalSeconds) > 2
                        || (run.State == "success") != TimelineExecutionPolicy.BusinessSuccess(last)) continue;
                }
                PlannedRun match = TimelineExecutionPolicy.UniqueDue(config, run.Started, savedUtc, history);
                if (match == null) continue;
                string key = TimelineExecutionPolicy.PlanKey(config, savedUtc);
                TimelineExecution unassigned = isLatest && !string.IsNullOrWhiteSpace(last.TimelineRunId)
                    ? history.FirstOrDefault(delegate(TimelineExecution entry)
                    {
                        DateTimeOffset recordedStart, recordedFinish;
                        return entry.RunId == last.TimelineRunId && entry.PlanKey == key && string.IsNullOrWhiteSpace(entry.PlannedTime)
                            && entry.State == run.State && DateTimeOffset.TryParse(entry.StartedAt, out recordedStart)
                            && DateTimeOffset.TryParse(entry.FinishedAt, out recordedFinish)
                            && recordedStart == latestStarted && recordedFinish == latestFinished;
                    }) : null;
                if (unassigned != null)
                {
                    unassigned.PlannedTime = match.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    unassigned.Diagnostic = "依据逐次业务日志及完整运行标识确认条目归属";
                    continue;
                }
                history.Add(new TimelineExecution
                {
                    RunId = "legacy-log-" + TimelineExecutionPolicy.Hash(run.Started.ToString("o") + run.Finished.ToString("o") + run.State),
                    PlanKey = key, PlannedTime = match.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    StartedAt = new DateTimeOffset(run.Started).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    FinishedAt = new DateTimeOffset(run.Finished).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    State = run.State, ExitCode = run.State == "success" ? 0 : -1,
                    Diagnostic = "依据逐次业务日志、执行前保存且一致的计划定义及唯一条目恢复"
                });
            }
        }

        internal static string[] ReadLogTail(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, stream.Length - 512 * 1024);
                    stream.Seek(start, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        if (start > 0) reader.ReadLine();
                        return reader.ReadToEnd().Split(new[] { '\n' }).Select(delegate(string value) { return value.TrimEnd('\r'); }).ToArray();
                    }
                }
            }
            catch { return new string[0]; }
        }
    }

    internal static class TimelineHistoryStore
    {
        public static bool ApplicationAccessEnabled = true;
        private const int HistoryLimit = 500;

        internal static List<TimelineExecution> Load(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return new List<TimelineExecution>();
                List<TimelineExecution> entries;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    entries = new JavaScriptSerializer().Deserialize<List<TimelineExecution>>(reader.ReadToEnd());
                return (entries ?? new List<TimelineExecution>()).Where(delegate(TimelineExecution entry)
                    { return entry != null && !string.IsNullOrWhiteSpace(entry.RunId) && !string.IsNullOrWhiteSpace(entry.PlanKey); })
                    .OrderByDescending(delegate(TimelineExecution entry) { return entry.StartedAt; }).Take(HistoryLimit).ToList();
            }
            catch { return new List<TimelineExecution>(); }
        }

        internal static void Mutate(string path, Action<List<TimelineExecution>> change)
        {
            using (Mutex mutex = new Mutex(false, "Global\\CodexQuotaWaker-History-" + TimelineExecutionPolicy.Hash(Path.GetFullPath(path))))
            {
                bool acquired = false;
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    try { acquired = mutex.WaitOne(2000); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return;
                    List<TimelineExecution> entries = Load(path);
                    change(entries);
                    entries = entries.OrderByDescending(delegate(TimelineExecution entry) { return entry.StartedAt; }).Take(HistoryLimit).ToList();
                    File.WriteAllText(temp, new JavaScriptSerializer().Serialize(entries), new UTF8Encoding(false));
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                            break;
                        }
                        catch (IOException)
                        {
                            // Readers/scanners can briefly hold Windows file metadata.
                            // Keep the existing history intact and retry the atomic replace.
                            if (attempt >= 4) throw;
                            Thread.Sleep(50 * (1 << attempt));
                        }
                    }
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    if (acquired) mutex.ReleaseMutex();
                }
            }
        }

        public static void Begin(AppConfig config, RunRecord record, string invokedVersion)
        {
            if (!ApplicationAccessEnabled || record.Mode != "scheduled") return;
            try
            {
                WindowsTaskStatus status = WindowsTaskService.GetStatus();
                DateTime savedUtc = File.GetLastWriteTimeUtc(AppPaths.ConfigPath);
                RunRecord previous = JsonStore.LoadLastRun();
                string[] logEvidence = LegacyTimelineEvidence.ReadLogTail(AppPaths.LogPath);
                Mutate(AppPaths.ExecutionHistoryPath, delegate(List<TimelineExecution> entries)
                {
                    BeginEvidence(config, status, record, invokedVersion, savedUtc, previous, logEvidence, entries, DateTimeOffset.Now);
                });
            }
            catch { Logger.Write("WARN", "无法可靠记录计划执行关联；时间线将保留结果未确认。"); }
        }

        internal static void BeginEvidence(AppConfig config, WindowsTaskStatus status, RunRecord record, string invokedVersion,
            DateTime savedUtc, RunRecord previous, IEnumerable<string> logs, List<TimelineExecution> history, DateTimeOffset now)
        {
            if (record.Mode != "scheduled") return;
            RecoverBeforeRun(config, status, previous, savedUtc, logs, history, now);
            DateTime started = DateTimeOffset.Parse(record.StartedAt, CultureInfo.InvariantCulture).LocalDateTime;
            PlannedRun match = TimelineExecutionPolicy.Associate(config, status, started, savedUtc, history, invokedVersion);
            using (Process current = Process.GetCurrentProcess())
            {
                TimelineExecution entry = new TimelineExecution
                {
                    RunId = Guid.NewGuid().ToString("N"), PlanKey = TimelineExecutionPolicy.PlanKey(config, savedUtc),
                    PlannedTime = match == null ? string.Empty : match.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    StartedAt = new DateTimeOffset(started).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), State = "running",
                    OwnerPid = current.Id, OwnerStartedAt = current.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    ExpiresAt = now.AddSeconds(ExecutionBudget.TotalSeconds(config.RetryCount) + 90).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    ExitCode = -1, Diagnostic = match == null ? "运行已记录，条目归属未确认" : "请求正在执行"
                };
                history.Add(entry);
                record.TimelineRunId = entry.RunId;
            }
        }

        internal static void RecoverBeforeRun(AppConfig config, WindowsTaskStatus status, RunRecord previous, DateTime savedUtc,
            IEnumerable<string> logs, List<TimelineExecution> history, DateTimeOffset now)
        {
            LegacyTimelineEvidence.Recover(config, status, previous, savedUtc, logs, history, now);
            TimelineExecution fallback = LegacyEntry(config, status, previous, savedUtc, history, now);
            if (fallback != null) history.Add(fallback);
        }

        public static void Finish(RunRecord record)
        {
            if (!ApplicationAccessEnabled || record.Mode != "scheduled" || string.IsNullOrWhiteSpace(record.TimelineRunId)) return;
            try
            {
                Mutate(AppPaths.ExecutionHistoryPath, delegate(List<TimelineExecution> entries)
                {
                    ApplyResult(entries, record);
                });
            }
            catch { Logger.Write("WARN", "计划执行结果未能写入历史；时间线将保留结果未确认。"); }
        }

        internal static void ApplyResult(List<TimelineExecution> entries, RunRecord record)
        {
            if (record.Mode != "scheduled" || string.IsNullOrWhiteSpace(record.TimelineRunId)) return;
            TimelineExecution entry = entries.FirstOrDefault(delegate(TimelineExecution run) { return run.RunId == record.TimelineRunId; });
            if (entry == null) return;
            entry.State = TimelineExecutionPolicy.BusinessSuccess(record) ? "success" : "failure";
            entry.FinishedAt = DateTimeOffset.Parse(record.FinishedAt, CultureInfo.InvariantCulture).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            entry.ExitCode = record.ExitCode;
            entry.Diagnostic = entry.State == "success" ? "收到有效模型响应" : "请求未成功，请查看最近结果或本地日志";
        }

        internal static TimelineExecution LegacyEntry(AppConfig config, WindowsTaskStatus status, RunRecord last,
            DateTime savedUtc, List<TimelineExecution> history, DateTimeOffset now)
        {
            DateTimeOffset started, finished;
            if (last == null || last.Mode != "scheduled" || !string.IsNullOrWhiteSpace(last.TimelineRunId)
                || !string.IsNullOrWhiteSpace(config.ScheduleVersion)
                || !DateTimeOffset.TryParse(last.StartedAt, out started) || !DateTimeOffset.TryParse(last.FinishedAt, out finished)
                || finished < started || finished > now) return null;
            PlannedRun match = TimelineExecutionPolicy.Associate(config, status, started.LocalDateTime, savedUtc, history, "");
            if (match == null) return null;
            return new TimelineExecution
            {
                RunId = "legacy-" + TimelineExecutionPolicy.Hash(last.StartedAt + last.FinishedAt),
                PlanKey = TimelineExecutionPolicy.PlanKey(config, savedUtc), PlannedTime = match.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                StartedAt = started.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), FinishedAt = finished.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                State = TimelineExecutionPolicy.BusinessSuccess(last) ? "success" : "failure", ExitCode = last.ExitCode,
                Diagnostic = "依据一致的 Windows 计划、唯一到期条目和既有业务结果恢复"
            };
        }

        public static List<TimelineExecution> ReadForTimeline(AppConfig config, WindowsTaskStatus status, RunRecord last)
        {
            if (!ApplicationAccessEnabled) return new List<TimelineExecution>();
            try
            {
                List<TimelineExecution> history = Load(AppPaths.ExecutionHistoryPath);
                DateTime savedUtc = File.GetLastWriteTimeUtc(AppPaths.ConfigPath);
                string[] logs = LegacyTimelineEvidence.ReadLogTail(AppPaths.LogPath);
                Mutate(AppPaths.ExecutionHistoryPath, delegate(List<TimelineExecution> entries)
                {
                    RecoverBeforeRun(config, status, last, savedUtc, logs, entries, DateTimeOffset.Now);
                });
                return Load(AppPaths.ExecutionHistoryPath);
            }
            catch { return new List<TimelineExecution>(); }
        }
    }

    internal static class TimelineExecutionChecks
    {
        internal static bool Verify(out string evidence)
        {
            List<string> checks = new List<string>();
            bool ok = true;
            Action<string, bool> check = delegate(string name, bool passed) { checks.Add(name + "=" + passed); ok = ok && passed; };
            DateTime start = new DateTime(2026, 10, 7, 17, 27, 0, DateTimeKind.Local);
            DateTime now = start.AddMinutes(2);
            DateTime savedUtc = start.AddHours(-1).ToUniversalTime();
            AppConfig config = new AppConfig { Enabled = true, SequenceStart = "2026-10-07 17:27", SequenceCount = 3, SequenceIntervalMinutes = 303 };
            WindowsTaskStatus status = new WindowsTaskStatus { Exists = true, Enabled = true,
                DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture"), LastRunTime = start.AddSeconds(28) };
            List<TimelineExecution> history = new List<TimelineExecution>();
            string key = TimelineExecutionPolicy.PlanKey(config, savedUtc);
            PlannedRun first = SchedulePlanner.Build(config, now, 14)[0];
            RunRecord legacy = new RunRecord { Mode = "scheduled", StartedAt = start.AddSeconds(33).ToString("o"),
                FinishedAt = start.AddMinutes(1).AddSeconds(5).ToString("o"), Success = true, ExitCode = 0, AttemptCount = 1, Output = "有效模型回复" };
            TimelineExecution recovered = TimelineHistoryStore.LegacyEntry(config, status, legacy, savedUtc, history, new DateTimeOffset(now));
            check("legacyUniqueSuccess", recovered != null && recovered.State == "success");
            if (recovered != null) history.Add(recovered);
            check("restartSuccess", TimelineExecutionPolicy.State(first, key, history, now, false) == "执行成功");
            check("draftIsPreview", TimelineExecutionPolicy.State(first, key, history, now, true) == TimelineExecutionPolicy.Unknown);
            check("noBroadcast", TimelineExecutionPolicy.State(SchedulePlanner.Build(config, now, 14)[1], key, history, start.AddHours(6), false) == TimelineExecutionPolicy.Unknown);
            check("duplicateNotReassigned", TimelineExecutionPolicy.Associate(config, status, start.AddSeconds(33), savedUtc, history, "") == null);
            legacy.Mode = "manual";
            check("manualNotImported", TimelineHistoryStore.LegacyEntry(config, status, legacy, savedUtc, new List<TimelineExecution>(), new DateTimeOffset(now)) == null);
            legacy.Mode = "scheduled";
            check("configEditedAfterRun", TimelineHistoryStore.LegacyEntry(config, status, legacy, now.ToUniversalTime(), new List<TimelineExecution>(), new DateTimeOffset(now)) == null);
            status.LastRunTime = start.AddHours(2);
            check("uniqueDelayed", TimelineExecutionPolicy.Associate(config, status, start.AddHours(2).AddSeconds(5), savedUtc, new List<TimelineExecution>(), "") != null);
            status.LastRunTime = start.AddHours(6);
            check("ambiguousDelayed", TimelineExecutionPolicy.Associate(config, status, start.AddHours(6).AddSeconds(5), savedUtc, new List<TimelineExecution>(), "") == null);
            check("provenNextSlot", TimelineExecutionPolicy.Associate(config, status, start.AddHours(6).AddSeconds(5), savedUtc, history, "") != null);
            check("launchNotCorroborated", TimelineExecutionPolicy.Associate(config, status, start.AddHours(6).AddMinutes(2), savedUtc, history, "") == null);

            config.ScheduleVersion = Guid.NewGuid().ToString("N");
            string versionKey = TimelineExecutionPolicy.PlanKey(config, savedUtc);
            check("sameTimeNewVersion", versionKey != key && TimelineExecutionPolicy.State(first, versionKey, history, now, false) == TimelineExecutionPolicy.Unknown);
            check("oldTaskRejected", !TimelineExecutionPolicy.InstalledMatches(config, status));
            status.DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture");
            status.LastRunTime = start.AddSeconds(28);
            check("newVersionArguments", TimelineExecutionPolicy.InstalledMatches(config, status)
                && TimelineExecutionPolicy.Associate(config, status, start.AddSeconds(33), savedUtc, new List<TimelineExecution>(), "old-version") == null
                && TimelineExecutionPolicy.Associate(config, status, start.AddSeconds(33), savedUtc, new List<TimelineExecution>(), config.ScheduleVersion) != null);
            AppConfig changed = JsonStore.Deserialize<AppConfig>(JsonStore.Serialize(config));
            changed.SequenceIntervalMinutes++;
            check("definitionMismatch", !TimelineExecutionPolicy.InstalledMatches(changed, status)
                && TimelineExecutionPolicy.PlanKey(changed, savedUtc) != versionKey);
            config.SequenceCount = 0;
            config.ExtraWakeTimes.Add("2026-10-07 17:27");
            config.ExtraWakeTimes.Add("2026-10-07 17:27");
            status.DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture");
            check("zeroAndExtraDedup", TimelineExecutionPolicy.Associate(config, status, start.AddSeconds(33), savedUtc, new List<TimelineExecution>(), config.ScheduleVersion) != null
                && SchedulePlanner.Build(config, now, 14).Count == 1);

            TimelineExecution running;
            using (Process owner = Process.GetCurrentProcess())
                running = new TimelineExecution { RunId = "fixture-running", PlanKey = key, PlannedTime = "2026-10-07 17:27", State = "running",
                    StartedAt = new DateTimeOffset(start).ToUniversalTime().ToString("o"), OwnerPid = owner.Id,
                    OwnerStartedAt = owner.StartTime.ToUniversalTime().ToString("o"), ExpiresAt = new DateTimeOffset(now.AddMinutes(10)).ToString("o") };
            List<TimelineExecution> active = new List<TimelineExecution> { running };
            check("actualRunning", TimelineExecutionPolicy.State(first, key, active, now, false) == "执行中");
            running.OwnerStartedAt = "wrong-process-generation";
            check("pidReuseOrCrash", TimelineExecutionPolicy.State(first, key, active, now, false) == TimelineExecutionPolicy.Unknown);
            using (Process owner = Process.GetCurrentProcess()) running.OwnerStartedAt = owner.StartTime.ToUniversalTime().ToString("o");
            running.ExpiresAt = new DateTimeOffset(now.AddSeconds(-1)).ToString("o");
            check("boundedRunning", TimelineExecutionPolicy.State(first, key, active, now, false) == TimelineExecutionPolicy.Unknown);
            RunRecord result = new RunRecord { Mode = "manual", TimelineRunId = running.RunId, FinishedAt = new DateTimeOffset(now).ToString("o"),
                Success = true, ExitCode = 0, AttemptCount = 1, Output = "有效模型回复", Error = "token=do-not-store" };
            TimelineHistoryStore.ApplyResult(active, result);
            check("manualDoesNotFinish", running.State == "running");
            result.Mode = "scheduled";
            result.Output = "";
            TimelineHistoryStore.ApplyResult(active, result);
            check("zeroExitNotBusinessSuccess", running.State == "failure" && TimelineExecutionPolicy.State(first, key, active, now, false) == "执行失败");
            result.Output = "有效模型回复";
            TimelineHistoryStore.ApplyResult(active, result);
            check("businessSuccess", running.State == "success" && TimelineExecutionPolicy.State(first, key, active, now, false) == "执行成功");

            string path = Path.Combine(Path.GetTempPath(), "CodexQuotaWaker-TimelineCheck-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries) { entries.Add(running); });
                List<TimelineExecution> loaded = TimelineHistoryStore.Load(path);
                check("persistentRestart", loaded.Count == 1 && TimelineExecutionPolicy.State(first, key, loaded, now, false) == "执行成功");
                check("noRawOrCredentials", File.ReadAllText(path).IndexOf("token=", StringComparison.Ordinal) < 0
                    && File.ReadAllText(path).IndexOf("有效模型回复", StringComparison.Ordinal) < 0);
                Task[] writers = new[] { "writer-a", "writer-b" }.Select(delegate(string id)
                    { return Task.Run(delegate { TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries)
                        { entries.Add(new TimelineExecution { RunId = id, PlanKey = key, StartedAt = new DateTimeOffset(now).ToString("o") }); }); }); }).ToArray();
                Task.WaitAll(writers);
                check("concurrentAtomicWrites", TimelineHistoryStore.Load(path).Count == 3);
                TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries)
                {
                    for (int index = 0; index < 510; index++)
                        entries.Add(new TimelineExecution { RunId = "bounded-" + index, PlanKey = key, StartedAt = new DateTimeOffset(now.AddMinutes(index)).ToString("o") });
                });
                check("boundedHistory", TimelineHistoryStore.Load(path).Count == 500);
                File.WriteAllText(path, "invalid-json");
                check("badHistorySafe", TimelineHistoryStore.Load(path).Count == 0);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            evidence = string.Join("; ", checks);
            return ok;
        }
    }

    internal static class TimelineContinuityChecks
    {
        private static RunRecord Result(DateTime started, bool success)
        {
            return new RunRecord { Mode = "scheduled", StartedAt = new DateTimeOffset(started).ToString("o"),
                FinishedAt = new DateTimeOffset(started.AddSeconds(30)).ToString("o"), Success = success,
                ExitCode = success ? 0 : 1, AttemptCount = 1, Output = success ? "有效模型回复" : "", Model = AppConfig.DefaultRequestModel };
        }

        private static string[] Logs(RunRecord record)
        {
            DateTime start = DateTimeOffset.Parse(record.StartedAt).LocalDateTime;
            DateTime finish = DateTimeOffset.Parse(record.FinishedAt).LocalDateTime;
            List<string> lines = new List<string> { start.ToString("yyyy-MM-dd HH:mm:ss") + " [INFO] 开始 " + record.Mode + " 触发；model=" + record.Model + "; route=fixture" };
            if (record.Success)
            {
                lines.Add(finish.ToString("yyyy-MM-dd HH:mm:ss") + " [INFO] Codex 请求成功并获得有效模型响应；route=fixture");
                lines.Add(finish.ToString("yyyy-MM-dd HH:mm:ss") + " [SUCCESS] 触发完成。");
            }
            else lines.Add(finish.ToString("yyyy-MM-dd HH:mm:ss") + " [FAIL] 请求未成功");
            return lines.ToArray();
        }

        internal static bool Verify(out string evidence)
        {
            bool ok = true;
            List<string> checks = new List<string>();
            Action<string, bool> check = delegate(string label, bool passed) { ok = ok && passed; checks.Add(label + "=" + passed); };
            DateTime start = new DateTime(2026, 10, 7, 17, 27, 0, DateTimeKind.Local);
            DateTime secondTime = start.AddMinutes(303);
            DateTime savedUtc = start.AddHours(-1).ToUniversalTime();
            AppConfig config = new AppConfig { SequenceStart = "2026-10-07 17:27", SequenceCount = 10, SequenceIntervalMinutes = 303, Enabled = true };
            WindowsTaskStatus status = new WindowsTaskStatus { Exists = true, Enabled = true, DefinitionSavedUtc = savedUtc.AddSeconds(-1) };
            string path = Path.Combine(Path.GetTempPath(), "CodexQuotaWaker-Continuity-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                foreach (bool versioned in new[] { false, true })
                foreach (bool success in new[] { true, false })
                {
                    if (File.Exists(path)) File.Delete(path);
                    config.ScheduleVersion = versioned ? Guid.NewGuid().ToString("N") : "";
                    status.DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture");
                    status.LastRunTime = start.AddSeconds(1);
                    RunRecord first = Result(start.AddSeconds(2), success);
                    TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries)
                    { TimelineHistoryStore.BeginEvidence(config, status, first, config.ScheduleVersion, savedUtc, null, new string[0], entries, new DateTimeOffset(start.AddSeconds(2))); });
                    TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries) { TimelineHistoryStore.ApplyResult(entries, first); });
                    // Reload on a separate background invocation; no MainForm is constructed.
                    status.LastRunTime = secondTime.AddSeconds(1);
                    RunRecord second = Result(secondTime.AddSeconds(2), success);
                    TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries)
                    { TimelineHistoryStore.BeginEvidence(config, status, second, config.ScheduleVersion, savedUtc, first, Logs(first), entries, new DateTimeOffset(secondTime.AddSeconds(2))); });
                    TimelineHistoryStore.Mutate(path, delegate(List<TimelineExecution> entries) { TimelineHistoryStore.ApplyResult(entries, second); });
                    List<TimelineExecution> loaded = TimelineHistoryStore.Load(path);
                    string expected = success ? "执行成功" : "执行失败";
                    string key = TimelineExecutionPolicy.PlanKey(config, savedUtc);
                    List<PlannedRun> plan = SchedulePlanner.Build(config, secondTime, 14);
                    check("noGuiTwoRuns/" + versioned + "/" + success, loaded.Count == 2 && !string.IsNullOrWhiteSpace(first.TimelineRunId)
                        && !string.IsNullOrWhiteSpace(second.TimelineRunId)
                        && TimelineExecutionPolicy.State(plan[0], key, loaded, secondTime.AddMinutes(1), false) == expected
                        && TimelineExecutionPolicy.State(plan[1], key, loaded, secondTime.AddMinutes(1), false) == expected);
                }

                config.ScheduleVersion = "";
                status.DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture");
                status.LastRunTime = secondTime.AddSeconds(1);
                RunRecord oldFirst = Result(start.AddSeconds(33), true);
                RunRecord oldSecond = Result(secondTime.AddSeconds(2), true);
                DateTimeOffset now = new DateTimeOffset(secondTime.AddMinutes(1));
                List<TimelineExecution> bootstrap = new List<TimelineExecution>();
                TimelineHistoryStore.BeginEvidence(config, status, oldSecond, "", savedUtc, oldFirst, Logs(oldFirst), bootstrap, new DateTimeOffset(secondTime.AddSeconds(2)));
                TimelineHistoryStore.ApplyResult(bootstrap, oldSecond);
                check("oldFirstRecoveredBeforeSecond", bootstrap.Count == 2 && bootstrap.All(delegate(TimelineExecution run) { return run.State == "success" && !string.IsNullOrWhiteSpace(run.PlannedTime); }));
                oldSecond.TimelineRunId = null;
                List<TimelineExecution> backfill = new List<TimelineExecution>();
                string[] bothLogs = Logs(oldFirst).Concat(Logs(oldSecond)).ToArray();
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, bothLogs, backfill, now);
                check("twoIndependentLegacyResults", backfill.Count == 2 && backfill.Select(delegate(TimelineExecution run) { return run.PlannedTime; })
                    .SequenceEqual(new[] { "2026-10-07 17:27", "2026-10-07 22:30" }));
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, bothLogs, backfill, now);
                check("legacyRecoveryIdempotent", backfill.Count == 2);

                List<TimelineExecution> missing = new List<TimelineExecution>();
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, Logs(oldSecond), missing, now);
                check("missedFirstNotGuessed", missing.Count == 0);
                string[] noResponseEvidence = Logs(oldFirst).Where(delegate(string line) { return !line.Contains("有效模型响应"); }).Concat(Logs(oldSecond)).ToArray();
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, noResponseEvidence, missing, now);
                check("terminalAloneNotSuccess", missing.Count == 0);
                RunRecord delayed = Result(secondTime.AddSeconds(1), true);
                TimelineHistoryStore.RecoverBeforeRun(config, status, delayed, savedUtc, Logs(delayed), missing, now);
                check("delayedMultipleCandidatesSafe", missing.Count == 0);
                RunRecord manual = Result(start.AddSeconds(33), true);
                manual.Mode = "manual";
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, Logs(manual).Concat(Logs(oldSecond)), missing, now);
                check("manualHistoryExcluded", missing.Count == 0);
                string[] overlap = new[] { Logs(oldFirst)[0], start.AddSeconds(34).ToString("yyyy-MM-dd HH:mm:ss") + " [INFO] 开始 manual 触发；model=gpt-6-luna" }
                    .Concat(Logs(oldFirst).Skip(1)).Concat(Logs(oldSecond)).ToArray();
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, overlap, missing, now);
                check("overlappingLegacyEvidenceSafe", missing.Count == 0);
                status.DefinitionSavedUtc = null;
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, bothLogs, missing, now);
                check("definitionTimestampRequired", missing.Count == 0);
                status.DefinitionSavedUtc = secondTime.AddMinutes(1).ToUniversalTime();
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, bothLogs, missing, now);
                check("definitionChangedAfterRuns", missing.Count == 0);
                status.DefinitionSavedUtc = savedUtc.AddSeconds(-1);
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, now.UtcDateTime, bothLogs, missing, now);
                check("configChangedAfterRuns", missing.Count == 0);
                config.ScheduleVersion = Guid.NewGuid().ToString("N");
                status.DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture");
                TimelineHistoryStore.RecoverBeforeRun(config, status, oldSecond, savedUtc, bothLogs, missing, now);
                check("legacyNotInNewVersion", missing.Count == 0);
                config.ScheduleVersion = "";
                status.DefinitionXml = TaskXml.Build(config, AppPaths.InstalledExePath, "fixture");

                RunRecord unmatched = Result(secondTime.AddSeconds(2), true);
                TimelineHistoryStore.BeginEvidence(config, status, unmatched, "", savedUtc, null, new string[0], missing, now);
                TimelineHistoryStore.ApplyResult(missing, unmatched);
                check("unmatchedResultPersistedWithoutBroadcast", missing.Count == 1 && missing[0].State == "success"
                    && missing[0].PlannedTime == "" && !string.IsNullOrWhiteSpace(unmatched.TimelineRunId)
                    && TimelineExecutionPolicy.State(SchedulePlanner.Build(config, secondTime, 14)[1], TimelineExecutionPolicy.PlanKey(config, savedUtc), missing, now.LocalDateTime, false) == TimelineExecutionPolicy.Unknown);
                TimelineHistoryStore.RecoverBeforeRun(config, status, unmatched, savedUtc, Logs(oldFirst).Concat(Logs(unmatched)), missing, now);
                check("recoveryPreservesRunIdentity", missing.Count == 2 && missing.Any(delegate(TimelineExecution entry)
                    { return entry.RunId == unmatched.TimelineRunId && entry.PlannedTime == "2026-10-07 22:30"; }));
                int countBefore = missing.Count;
                TimelineHistoryStore.BeginEvidence(config, status, manual, "", savedUtc, null, bothLogs, missing, now);
                check("manualDoesNotJournal", missing.Count == countBefore);
                check("safeLegacyDiagnostic", JsonStore.Serialize(backfill).IndexOf("有效模型回复", StringComparison.Ordinal) < 0);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            evidence = string.Join("; ", checks);
            return ok;
        }
    }

    internal sealed class PlannedRun
    {
        public DateTime Time { get; set; }
        public string Source { get; set; }
    }

    internal static class SchedulePlanner
    {
        public static List<PlannedRun> Build(AppConfig config, DateTime now, int dailyDays)
        {
            List<PlannedRun> runs = new List<PlannedRun>();
            if (config == null)
            {
                return runs;
            }

            DateTime start;
            if (config.SequenceCount > 0 && AppConfig.TryParseSequenceStart(config.SequenceStart, out start))
            {
                int intervalMinutes = SequenceSchedule.GetIntervalMinutes(config);
                for (int index = 0; index < config.SequenceCount; index++)
                {
                    try
                    {
                        runs.Add(new PlannedRun
                        {
                            Time = start.AddMinutes((long)index * intervalMinutes),
                            Source = "序列第 " + (index + 1) + " / " + config.SequenceCount + " 次"
                        });
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        break;
                    }
                }
            }

            foreach (string value in AppConfig.NormalizeDateTimes(config.ExtraWakeTimes))
            {
                DateTime extra;
                if (AppConfig.TryParseDateTime(value, out extra))
                {
                    PlannedRun duplicate = runs.FirstOrDefault(delegate(PlannedRun item) { return item.Time == extra; });
                    if (duplicate == null)
                    {
                        runs.Add(new PlannedRun { Time = extra, Source = "插入唤醒" });
                    }
                    else if (duplicate.Source.IndexOf("插入唤醒", StringComparison.Ordinal) < 0)
                    {
                        duplicate.Source += "；插入唤醒（同一时间已合并）";
                    }
                }
            }

            return runs
                .OrderBy(delegate(PlannedRun item) { return item.Time; })
                .ThenBy(delegate(PlannedRun item) { return item.Source; })
                .ToList();
        }

        public static DateTime? NextRun(AppConfig config, DateTime now)
        {
            PlannedRun next = Build(config, now, 8)
                .Where(delegate(PlannedRun item) { return item.Time > now; })
                .FirstOrDefault();
            return next == null ? (DateTime?)null : next.Time;
        }
    }

    internal static class NativePower
    {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint flags);

        public static bool KeepAwake()
        {
            return SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED) != 0;
        }

        public static void Restore()
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        public static bool RequestSleep()
        {
            return Application.SetSuspendState(PowerState.Suspend, false, false);
        }
    }

    internal static class ExecutionBudget
    {
        public const int NetworkWaitSeconds = 90;
        public const int AttemptTimeoutSeconds = 240;
        public const int RetryDelaySeconds = 15;
        public const int CleanupReserveSeconds = 30;

        public static int TotalSeconds(int attempts)
        {
            int safeAttempts = Math.Max(1, Math.Min(5, attempts));
            long total = NetworkWaitSeconds
                + ((long)safeAttempts * AttemptTimeoutSeconds)
                + ((long)(safeAttempts - 1) * RetryDelaySeconds)
                + CleanupReserveSeconds;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public static int TaskExecutionLimitMinutes(int attempts)
        {
            long seconds = TotalSeconds(attempts) + 60L;
            return (int)Math.Max(5L, (seconds + 59L) / 60L);
        }
    }

    internal sealed class WakeEvidence
    {
        public bool Confirmed { get; set; }
        public DateTime WakeTime { get; set; }
        public string Description { get; set; }

        public WakeEvidence()
        {
            Description = string.Empty;
        }
    }

    internal static class WakeDetector
    {
        private const int MaximumWakeAgeMinutes = 10;

        public static WakeEvidence Capture(string mode)
        {
            WakeEvidence result = new WakeEvidence();
            if (!string.Equals(mode, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                result.Description = "非计划任务模式";
                return result;
            }

            DateTime now = DateTime.Now;
            try
            {
                string queryText = "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]";
                EventLogQuery query = new EventLogQuery("System", PathType.LogName, queryText);
                query.ReverseDirection = true;
                using (EventLogReader reader = new EventLogReader(query))
                {
                    for (int index = 0; index < 8; index++)
                    {
                        using (EventRecord wake = reader.ReadEvent())
                        {
                            if (wake == null)
                            {
                                break;
                            }
                            DateTime wakeTime = wake.TimeCreated ?? DateTime.MinValue;
                            if (wakeTime < now.AddMinutes(-MaximumWakeAgeMinutes))
                            {
                                break;
                            }

                            string sourceText = ReadEventData(wake.ToXml(), "WakeSourceText");
                            if (sourceText.IndexOf(TaskXml.TaskName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                result.Confirmed = true;
                                result.WakeTime = wakeTime;
                                result.Description = "Power-Troubleshooter 已确认由 " + TaskXml.TaskName + " 唤醒；时间=" + wakeTime.ToString("yyyy-MM-dd HH:mm:ss");
                                return result;
                            }
                        }
                    }
                }
                result.Description = "最近 10 分钟的唤醒事件未明确包含任务名 " + TaskXml.TaskName;
            }
            catch (Exception ex)
            {
                result.Description = "无法读取 Power-Troubleshooter 唤醒证据：" + ex.Message;
            }
            return result;
        }

        internal static string ReadEventData(string xml, string dataName)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return string.Empty;
            }
            XmlDocument document = new XmlDocument();
            document.LoadXml(xml);
            XmlNodeList nodes = document.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']");
            foreach (XmlNode node in nodes)
            {
                XmlAttribute name = node.Attributes == null ? null : node.Attributes["Name"];
                if (name != null && string.Equals(name.Value, dataName, StringComparison.OrdinalIgnoreCase))
                {
                    return node.InnerText ?? string.Empty;
                }
            }
            return string.Empty;
        }
    }

    internal static class NativeInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO value);

        public static bool TryGetLastInputTime(out DateTime result)
        {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info))
            {
                result = DateTime.MinValue;
                return false;
            }
            uint nowTick = unchecked((uint)Environment.TickCount);
            uint idleMilliseconds = unchecked(nowTick - info.dwTime);
            result = DateTime.Now.AddMilliseconds(-idleMilliseconds);
            return true;
        }

        public static bool WasUsedAfter(DateTime wakeTime)
        {
            DateTime lastInput;
            return !TryGetLastInputTime(out lastInput) || lastInput >= wakeTime.AddSeconds(-1);
        }
    }

    internal static class AutoSleepPolicy
    {
        public const int CountdownSeconds = 60;

        public static string Evaluate(bool enabled, string mode, bool requestSucceeded, bool wakeConfirmed, bool userActive)
        {
            if (!enabled) return "disabled";
            if (!string.Equals(mode, "scheduled", StringComparison.OrdinalIgnoreCase)) return "not-scheduled";
            if (!requestSucceeded) return "request-failed";
            if (!wakeConfirmed) return "wake-unconfirmed";
            if (userActive) return "user-active";
            return "ready";
        }

        public static string CountdownStatus(bool interactive)
        {
            return interactive
                ? "等待 " + CountdownSeconds + " 秒后进入普通睡眠，可取消"
                : "后台运行，无法取消：等待 " + CountdownSeconds + " 秒后请求普通睡眠";
        }
    }

    internal enum SleepCountdownOutcome
    {
        Elapsed,
        Cancelled,
        UserActivity
    }

    internal sealed class SleepCountdownForm : Form
    {
        private readonly Label message;
        private readonly System.Windows.Forms.Timer timer;
        private readonly DateTime wakeTime;
        private int remainingSeconds;

        public SleepCountdownOutcome Outcome { get; private set; }

        public SleepCountdownForm(DateTime wakeTime, int seconds)
        {
            this.wakeTime = wakeTime;
            remainingSeconds = seconds;
            Outcome = SleepCountdownOutcome.Cancelled;
            Text = "Codex 任务已完成";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(440, 145);
            Font = new Font("Microsoft YaHei UI", 9F);

            message = new Label();
            message.Dock = DockStyle.Top;
            message.Height = 78;
            message.Padding = new Padding(18, 18, 18, 4);
            message.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(message);

            Button cancel = new Button();
            cancel.Text = "取消本次睡眠";
            cancel.Size = new Size(140, 34);
            cancel.Location = new Point((ClientSize.Width - cancel.Width) / 2, 92);
            cancel.Click += delegate
            {
                Outcome = SleepCountdownOutcome.Cancelled;
                Close();
            };
            Controls.Add(cancel);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                if (NativeInput.WasUsedAfter(this.wakeTime))
                {
                    Outcome = SleepCountdownOutcome.UserActivity;
                    Close();
                    return;
                }
                remainingSeconds--;
                UpdateMessage();
                if (remainingSeconds <= 0)
                {
                    Outcome = SleepCountdownOutcome.Elapsed;
                    Close();
                }
            };
            Shown += delegate
            {
                UpdateMessage();
                timer.Start();
            };
            FormClosed += delegate { timer.Stop(); };
        }

        private void UpdateMessage()
        {
            message.Text = "本次请求已成功完成。\r\n" + Math.Max(0, remainingSeconds) + " 秒后电脑将进入睡眠。";
        }
    }

    internal static class AutoSleepCoordinator
    {
        public static void Handle(AppConfig config, RunRecord record, WakeEvidence wake)
        {
            bool userActive = wake.Confirmed && NativeInput.WasUsedAfter(wake.WakeTime);
            string decision = AutoSleepPolicy.Evaluate(config.SleepAfterScheduledWake, record.Mode, record.Success, wake.Confirmed, userActive);

            if (decision != "ready")
            {
                record.SleepStatus = ExplainSkip(decision);
                Logger.Write("INFO", "自动睡眠已跳过：" + record.SleepStatus);
                JsonStore.SaveLastRun(record);
                return;
            }

            if (!Environment.UserInteractive)
            {
                record.SleepStatus = AutoSleepPolicy.CountdownStatus(false);
                JsonStore.SaveLastRun(record);
                Logger.Write("INFO", record.SleepStatus);
                Thread.Sleep(AutoSleepPolicy.CountdownSeconds * 1000);
                RequestSleep(record);
                return;
            }

            record.SleepStatus = AutoSleepPolicy.CountdownStatus(true);
            JsonStore.SaveLastRun(record);
            Logger.Write("INFO", record.SleepStatus);

            SleepCountdownOutcome outcome;
            using (SleepCountdownForm dialog = new SleepCountdownForm(wake.WakeTime, AutoSleepPolicy.CountdownSeconds))
            {
                dialog.ShowDialog();
                outcome = dialog.Outcome;
            }

            if (outcome != SleepCountdownOutcome.Elapsed)
            {
                record.SleepStatus = outcome == SleepCountdownOutcome.UserActivity
                    ? "自动睡眠已取消：检测到用户操作"
                    : "自动睡眠已由用户取消";
                Logger.Write("INFO", record.SleepStatus);
                JsonStore.SaveLastRun(record);
                return;
            }

            RequestSleep(record);
        }

        private static void RequestSleep(RunRecord record)
        {
            record.SleepRequested = true;
            record.SleepStatus = "已向 Windows 请求普通睡眠";
            JsonStore.SaveLastRun(record);
            Logger.Write("INFO", record.SleepStatus);
            if (!NativePower.RequestSleep())
            {
                record.SleepRequested = false;
                record.SleepStatus = "Windows 拒绝进入睡眠，请检查电源策略或活动程序";
                Logger.Write("WARN", record.SleepStatus);
                JsonStore.SaveLastRun(record);
            }
        }

        private static string ExplainSkip(string decision)
        {
            if (decision == "disabled") return "功能未勾选";
            if (decision == "not-scheduled") return "本次是手动测试或非计划模式";
            if (decision == "request-failed") return "Codex 请求未成功";
            if (decision == "wake-unconfirmed") return "无法确认电脑由本次计划唤醒";
            if (decision == "user-active") return "唤醒后检测到用户操作";
            return "条件不满足";
        }
    }

    internal static class CodexLocator
    {
        public static string Find()
        {
            string fromPath = FindFromWhere();
            if (!string.IsNullOrWhiteSpace(fromPath) && File.Exists(fromPath))
            {
                return fromPath;
            }

            string binRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "Codex",
                "bin");

            if (Directory.Exists(binRoot))
            {
                try
                {
                    FileInfo newest = Directory.GetFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
                        .Select(delegate(string path) { return new FileInfo(path); })
                        .OrderByDescending(delegate(FileInfo file) { return file.LastWriteTimeUtc; })
                        .FirstOrDefault();
                    if (newest != null)
                    {
                        return newest.FullName;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write("WARN", "扫描 Codex 安装目录失败：" + ex.Message);
                }
            }

            return null;
        }

        private static string FindFromWhere()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("where.exe", "codex.exe");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    if (process.ExitCode == 0)
                    {
                        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    }
                }
            }
            catch
            {
                return null;
            }
            return null;
        }
    }

    internal sealed class RouteSnapshot
    {
        public string TargetMode { get; set; }
        public string RouteType { get; set; }
        public string DisplayName { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string BaseUrlKind { get; set; }
        public string Summary { get; set; }
        public string RiskMessage { get; set; }
        public string Diagnostic { get; set; }
        public bool IsOfficialDirect { get; set; }
        public bool IsLocalRouter { get; set; }

        public RouteSnapshot()
        {
            TargetMode = TargetModes.FollowCurrent;
            RouteType = "unknown";
            DisplayName = "未识别链路";
            Provider = string.Empty;
            Model = string.Empty;
            BaseUrlKind = string.Empty;
            Summary = "尚未检测";
            RiskMessage = string.Empty;
            Diagnostic = string.Empty;
        }
    }

    internal static class CodexRouteDetector
    {
        public static RouteSnapshot Detect()
        {
            RouteSnapshot snapshot = new RouteSnapshot();
            snapshot.TargetMode = TargetModes.FollowCurrent;
            try
            {
                string configPath = GetConfigPath();
                Dictionary<string, string> values = ReadSimpleToml(configPath);
                snapshot.Provider = GetValue(values, "model_provider");
                snapshot.Model = GetValue(values, "model");
                string baseUrl = GetValue(values, "base_url");
                if (string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(snapshot.Provider))
                {
                    baseUrl = GetValue(values, "model_providers." + snapshot.Provider + ".base_url");
                }

                Uri uri;
                bool hasUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out uri);
                bool officialHost = hasUri && IsOfficialHost(uri.Host);
                bool localHost = hasUri && uri.IsLoopback;
                bool isOpenAiProvider = string.Equals(snapshot.Provider, "openai", StringComparison.OrdinalIgnoreCase);

                if ((!hasUri && string.IsNullOrWhiteSpace(baseUrl) && (isOpenAiProvider || string.IsNullOrWhiteSpace(snapshot.Provider)))
                    || officialHost)
                {
                    snapshot.RouteType = "direct";
                    snapshot.DisplayName = "直接链路";
                    snapshot.IsOfficialDirect = true;
                    snapshot.BaseUrlKind = hasUri ? DescribeBaseUrl(uri) : "默认端点";
                    snapshot.Summary = "直接链路；provider=" + ProviderForDisplay(snapshot.Provider);
                }
                else if (localHost)
                {
                    snapshot.IsLocalRouter = true;
                    snapshot.RouteType = LooksLikeCcSwitch(uri) ? "cc-switch-local" : "local-router";
                    snapshot.DisplayName = snapshot.RouteType == "cc-switch-local" ? "CC Switch 本地路由" : "本地路由";
                    snapshot.BaseUrlKind = DescribeBaseUrl(uri);
                    snapshot.Summary = snapshot.DisplayName + "；provider=" + ProviderForDisplay(snapshot.Provider) + "；base_url=" + snapshot.BaseUrlKind;
                }
                else if (hasUri || !string.IsNullOrWhiteSpace(baseUrl))
                {
                    snapshot.RouteType = "custom";
                    snapshot.DisplayName = "自定义链路";
                    snapshot.BaseUrlKind = hasUri ? DescribeBaseUrl(uri) : "无法识别的 base_url";
                    snapshot.Summary = "自定义链路；provider=" + ProviderForDisplay(snapshot.Provider) + "；base_url=" + snapshot.BaseUrlKind;
                }
                else
                {
                    snapshot.RouteType = "unknown";
                    snapshot.DisplayName = "未识别链路";
                    snapshot.BaseUrlKind = "未配置";
                    snapshot.Summary = "未识别的 provider=" + ProviderForDisplay(snapshot.Provider);
                }

                snapshot.RiskMessage = "测试请求会沿当前 Codex 配置执行，不会自动切换 provider、模型或路由。";
            }
            catch (Exception ex)
            {
                snapshot.Diagnostic = ex.Message;
                snapshot.Summary = "链路检测失败；将按当前 Codex 配置执行并在运行后验证。";
                snapshot.RiskMessage = "无法预先识别当前配置摘要，测试后请查看运行结果和日志。";
            }
            return snapshot;
        }

        // Kept for older self-check fixtures and persisted compatibility paths.
        // The active product flow always follows the current Codex configuration.
        public static RouteSnapshot Detect(string ignoredTargetMode)
        {
            return Detect();
        }

        public static string GetConfigPath()
        {
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(codexHome))
            {
                return Path.Combine(codexHome, "config.toml");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "config.toml");
        }

        private static Dictionary<string, string> ReadSimpleToml(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                return values;
            }

            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = StripTomlComment(raw).Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, equals).Trim();
                string value = UnquoteTomlValue(line.Substring(equals + 1).Trim());
                string fullKey = section.Length == 0 ? key : section + "." + key;
                values[fullKey] = value;
            }
            return values;
        }

        private static string StripTomlComment(string value)
        {
            bool quoted = false;
            bool escaped = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' && !escaped)
                {
                    quoted = !quoted;
                }
                if (c == '#' && !quoted)
                {
                    return value.Substring(0, i);
                }
                escaped = c == '\\' && !escaped;
                if (c != '\\')
                {
                    escaped = false;
                }
            }
            return value;
        }

        private static string UnquoteTomlValue(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            {
                return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        private static string GetValue(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static bool IsOfficialHost(string host)
        {
            return string.Equals(host, "api.openai.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeCcSwitch(Uri uri)
        {
            if (uri != null && uri.Port == 15721)
            {
                return true;
            }
            string[] names = new[] { "cc-switch", "cc-switch-desktop", "ccswitch" };
            foreach (string name in names)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    bool found = processes.Length > 0;
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                    if (found)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }
            return false;
        }

        private static string DescribeBaseUrl(Uri uri)
        {
            if (uri == null)
            {
                return "未配置";
            }
            string host = uri.IsLoopback ? "本机" : uri.Host;
            int port = uri.IsDefaultPort ? -1 : uri.Port;
            return port > 0 ? host + ":" + port.ToString(CultureInfo.InvariantCulture) : host;
        }

        private static string ProviderForDisplay(string provider)
        {
            return string.IsNullOrWhiteSpace(provider) ? "默认" : provider;
        }
    }
    internal sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool TimedOut { get; set; }

        public ProcessResult()
        {
            ExitCode = -1;
            StandardOutput = string.Empty;
            StandardError = string.Empty;
        }
    }

    internal static class ProcessRunner
    {
        public static ProcessResult Run(string executable, string arguments, string workingDirectory, int timeoutMilliseconds)
        {
            ProcessResult result = new ProcessResult();
            ProcessStartInfo info = new ProcessStartInfo(executable, arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
            info.WorkingDirectory = workingDirectory;

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    result.TimedOut = true;
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    process.WaitForExit(5000);
                }
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                result.StandardOutput = stdout.IsCompleted ? stdout.Result : string.Empty;
                result.StandardError = stderr.IsCompleted ? stderr.Result : string.Empty;
                if (!result.TimedOut)
                {
                    result.ExitCode = process.ExitCode;
                }
            }
            return result;
        }

        public static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '\"' }) < 0)
            {
                return value;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append('\"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '\"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('\"');
                    backslashes = 0;
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(c);
                    backslashes = 0;
                }
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('\"');
            return builder.ToString();
        }
    }

    internal sealed class CcSwitchLogEvidence
    {
        public bool Available { get; set; }
        public bool Matched { get; set; }
        public bool MatchedModel { get; set; }
        public string TargetUrl { get; set; }
        public string TargetHost { get; set; }
        public string Model { get; set; }
        public string ProviderName { get; set; }
        public bool IsOfficialTarget { get; set; }
        public string Confidence { get; set; }
        public string Diagnostic { get; set; }

        public CcSwitchLogEvidence()
        {
            TargetUrl = string.Empty;
            TargetHost = string.Empty;
            Model = string.Empty;
            ProviderName = string.Empty;
            Confidence = "unavailable";
            Diagnostic = string.Empty;
        }

        public string Summary()
        {
            if (!Matched)
            {
                return string.Empty;
            }
            return ProviderName + "（" + TargetHost + "）";
        }
    }

    internal static class CcSwitchLogProbe
    {
        private static readonly Regex TargetLinePattern = new Regex(
            @"^\[(?<date>\d{4}-\d{2}-\d{2})\]\[(?<time>\d{2}:\d{2}:\d{2})\].*?\[Codex\]\s*>>>\s*请求目标:\s*(?<url>\S+)\s*\(model=(?<model>[^)]+)\)",
            RegexOptions.Compiled);

        public static CcSwitchLogEvidence TryRead(string model, string startedAt, string finishedAt)
        {
            CcSwitchLogEvidence evidence = new CcSwitchLogEvidence();
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cc-switch",
                    "logs",
                    "cc-switch.log");
                if (!File.Exists(logPath))
                {
                    evidence.Diagnostic = "未找到 CC Switch 日志。";
                    return evidence;
                }

                evidence.Available = true;
                string text = ReadTail(logPath, 2 * 1024 * 1024);
                DateTime start = ParseLocalTime(startedAt);
                DateTime finish = ParseLocalTime(finishedAt);
                if (start == DateTime.MinValue)
                {
                    start = DateTime.Now.AddMinutes(-10);
                }
                if (finish == DateTime.MinValue || finish < start)
                {
                    finish = DateTime.Now.AddMinutes(2);
                }

                CcSwitchLogEvidence parsed = ParseEvidence(text, model, start, finish);
                parsed.Available = true;
                return parsed;
            }
            catch (Exception ex)
            {
                evidence.Diagnostic = "读取 CC Switch 日志失败：" + ex.Message;
                return evidence;
            }
        }

        public static CcSwitchLogEvidence ParseEvidence(string text, string model, DateTime startedAt, DateTime finishedAt)
        {
            CcSwitchLogEvidence evidence = new CcSwitchLogEvidence();
            if (string.IsNullOrWhiteSpace(text))
            {
                evidence.Diagnostic = "CC Switch 日志为空。";
                return evidence;
            }

            DateTime windowStart = startedAt.AddMinutes(-2);
            DateTime windowEnd = finishedAt.AddMinutes(2);
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Match latestInWindow = null;
            DateTime latestInWindowTime = DateTime.MinValue;
            Match latestModelMatch = null;
            DateTime latestModelMatchTime = DateTime.MinValue;

            foreach (string line in lines)
            {
                Match match = TargetLinePattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }
                DateTime timestamp;
                if (!DateTime.TryParseExact(
                    match.Groups["date"].Value + " " + match.Groups["time"].Value,
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out timestamp))
                {
                    continue;
                }
                if (timestamp < windowStart || timestamp > windowEnd)
                {
                    continue;
                }

                latestInWindow = match;
                latestInWindowTime = timestamp;
                if (string.Equals(match.Groups["model"].Value.Trim(), model, StringComparison.OrdinalIgnoreCase))
                {
                    latestModelMatch = match;
                    latestModelMatchTime = timestamp;
                }
            }

            Match selected = latestModelMatch ?? latestInWindow;
            DateTime selectedTime = latestModelMatch != null ? latestModelMatchTime : latestInWindowTime;
            if (selected == null)
            {
                evidence.Diagnostic = "CC Switch 日志中没有匹配本次时间窗的 Codex 请求目标。";
                return evidence;
            }

            string targetUrl = selected.Groups["url"].Value.Trim();
            Uri uri;
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out uri))
            {
                evidence.Diagnostic = "CC Switch 请求目标不是有效 URL。";
                return evidence;
            }

            evidence.Matched = true;
            evidence.MatchedModel = latestModelMatch != null;
            evidence.TargetUrl = targetUrl;
            evidence.TargetHost = uri.Host;
            evidence.Model = selected.Groups["model"].Value.Trim();
            evidence.IsOfficialTarget = IsOfficialHost(uri.Host);
            evidence.ProviderName = DescribeProvider(uri.Host);
            evidence.Confidence = evidence.MatchedModel ? "model-and-time" : "time-window";
            evidence.Diagnostic = evidence.MatchedModel
                ? "CC Switch 日志按本次模型和时间窗匹配到请求目标。"
                : "CC Switch 日志只按时间窗匹配到请求目标，未匹配模型。";
            return evidence;
        }

        private static string ReadTail(string path, int maxBytes)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0L, stream.Length - maxBytes);
                stream.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[stream.Length - start];
                int read = stream.Read(buffer, 0, buffer.Length);
                string text = Encoding.UTF8.GetString(buffer, 0, read);
                if (start > 0)
                {
                    int newline = text.IndexOf('\n');
                    if (newline >= 0)
                    {
                        text = text.Substring(newline + 1);
                    }
                }
                return text;
            }
        }

        private static DateTime ParseLocalTime(string value)
        {
            DateTime result;
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out result) ? result : DateTime.MinValue;
        }

        private static bool IsOfficialHost(string host)
        {
            return string.Equals(host, "api.openai.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeProvider(string host)
        {
            if (string.Equals(host, "api.deepseek.com", StringComparison.OrdinalIgnoreCase))
            {
                return "DeepSeek";
            }
            if (string.Equals(host, "api.openai.com", StringComparison.OrdinalIgnoreCase))
            {
                return "OpenAI";
            }
            if (string.Equals(host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase))
            {
                return "ChatGPT / Codex";
            }
            return host;
        }
    }

    internal sealed class CodexRunAnalysis
    {
        public string SessionId { get; set; }
        public string Provider { get; set; }
        public string FinalModel { get; set; }
        public string RemoteError { get; set; }
        public string FinalText { get; set; }
        public bool OfficialQuotaConfirmed { get; set; }
        public string QuotaStatus { get; set; }
        public string LimitId { get; set; }
        public int WindowMinutes { get; set; }
        public double? UsedPercent { get; set; }
        public long? ResetsAtUnix { get; set; }
        public string ResetAtLocal { get; set; }
        public bool HasReliableResetTime { get; set; }
        public string ResetTimeDiagnostic { get; set; }
        public List<long> ResetCandidatesUnix { get; private set; }
        public bool HasInvalidResetEvidence { get; set; }
        public string PlanType { get; set; }
        public string Diagnostic { get; set; }
        public int ParsedEventCount { get; set; }

        public CodexRunAnalysis()
        {
            SessionId = string.Empty;
            Provider = string.Empty;
            FinalModel = string.Empty;
            RemoteError = string.Empty;
            FinalText = string.Empty;
            QuotaStatus = "unconfirmed";
            LimitId = string.Empty;
            ResetAtLocal = string.Empty;
            ResetTimeDiagnostic = string.Empty;
            ResetCandidatesUnix = new List<long>();
            PlanType = string.Empty;
            Diagnostic = string.Empty;
        }
    }

    internal static class CodexRunAnalyzer
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static void Apply(ProcessResult process, AppConfig config, RouteSnapshot route, RunRecord record, DateTime startedAt)
        {
            record.TargetMode = TargetModes.Normalize(config.TargetMode);
            record.RouteType = route == null ? "unknown" : route.RouteType;
            record.RouteSummary = route == null ? "未检测" : route.Summary;
            record.Provider = route == null ? string.Empty : route.Provider;
            record.BaseUrlKind = route == null ? string.Empty : route.BaseUrlKind;
            record.QuotaStatus = "unconfirmed";

            CodexRunAnalysis analysis = new CodexRunAnalysis();
            try
            {
                string stdout = CleanForJson(process == null ? string.Empty : process.StandardOutput);
                analysis = AnalyzeText(stdout, startedAt);
                if (!string.IsNullOrWhiteSpace(analysis.SessionId))
                {
                    string sessionPath = FindSessionFile(analysis.SessionId);
                    if (!string.IsNullOrWhiteSpace(sessionPath))
                    {
                        CodexRunAnalysis fromSession = AnalyzeText(File.ReadAllText(sessionPath, Encoding.UTF8), startedAt);
                        analysis = Merge(analysis, fromSession);
                    }
                    else if (string.IsNullOrWhiteSpace(analysis.Diagnostic))
                    {
                        analysis.Diagnostic = "未找到与本次 session id 精确匹配的 Codex 会话文件。";
                    }
                }
                else if (string.IsNullOrWhiteSpace(analysis.Diagnostic))
                {
                    analysis.Diagnostic = "Codex JSONL 输出中没有本次 session/thread id，无法精确读取会话补充证据。";
                }

                FinalizeResetEvidence(analysis);

                record.SessionId = analysis.SessionId;
                bool providerFromRemoteError = !string.IsNullOrWhiteSpace(analysis.Provider)
                    && route != null
                    && !string.Equals(analysis.Provider, route.Provider, StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(analysis.Provider))
                {
                    record.Provider = analysis.Provider;
                }
                if (providerFromRemoteError)
                {
                    record.UpstreamProvider = analysis.Provider;
                    record.UpstreamEvidence = "Codex JSON 错误信息报告了实际上游 provider。";
                }
                // Legacy quota fields remain readable in old JSON. The active request
                // flow only reads narrow structured reset evidence for schedule preview;
                // it never uses that evidence to judge request success or quota status.
                record.OfficialQuotaConfirmed = false;
                record.QuotaStatus = "unconfirmed";
                record.LimitId = analysis.LimitId;
                record.WindowMinutes = analysis.WindowMinutes;
                record.UsedPercent = analysis.UsedPercent;
                record.ResetsAtUnix = analysis.ResetsAtUnix;
                record.ResetAtLocal = analysis.ResetAtLocal;
                record.HasReliableResetTime = analysis.HasReliableResetTime;
                record.ResetTimeDiagnostic = analysis.ResetTimeDiagnostic;
                record.PlanType = analysis.PlanType;
                record.QuotaDiagnostic = analysis.Diagnostic;
                record.FinalModel = analysis.FinalModel;
                record.RemoteError = analysis.RemoteError;

                if (!string.IsNullOrWhiteSpace(analysis.FinalText))
                {
                    record.Output = analysis.FinalText;
                }
                else if (string.IsNullOrWhiteSpace(record.Output))
                {
                    record.Output = "已收到 Codex 结构化响应，但未找到最终文本。";
                }
            }
            catch (Exception ex)
            {
                record.QuotaStatus = "unconfirmed";
                record.QuotaDiagnostic = "解析 Codex 结构化响应失败：" + ex.Message;
            }

            if (route != null && route.IsLocalRouter)
            {
                CcSwitchLogEvidence upstream = CcSwitchLogProbe.TryRead(
                    config.Model,
                    record.StartedAt,
                    DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                if (upstream.Matched)
                {
                    record.UpstreamProvider = upstream.ProviderName;
                    record.UpstreamTarget = upstream.TargetUrl;
                    record.UpstreamEvidence = upstream.Diagnostic;
                    if (string.IsNullOrWhiteSpace(record.FinalModel))
                    {
                        record.FinalModel = upstream.Model;
                    }
                }
                else
                {
                    record.UpstreamEvidence = upstream.Diagnostic;
                }
            }

            bool requestSucceeded = process != null && !process.TimedOut && process.ExitCode == 0 && record.Success;
            // TargetSatisfied is retained as a compatibility alias for old result
            // files. New success, scheduling, UI, and sleep decisions use Success.
            record.TargetSatisfied = requestSucceeded;
            record.RouteSummary = BuildActualRouteSummary(route, record.Provider);
            if (!string.IsNullOrWhiteSpace(record.UpstreamProvider))
            {
                record.RouteSummary += "；CC Switch 上游=" + record.UpstreamProvider;
            }

            if (string.IsNullOrWhiteSpace(record.QuotaDiagnostic))
            {
                record.QuotaDiagnostic = "本次运行未提供额外的结构化路由证据。";
            }
        }

        private static CodexRunAnalysis AnalyzeText(string text, DateTime requestFinishedAt)
        {
            CodexRunAnalysis result = new CodexRunAnalysis();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] != '{')
                {
                    continue;
                }

                object raw;
                try
                {
                    raw = Serializer.DeserializeObject(line);
                }
                catch
                {
                    continue;
                }
                Dictionary<string, object> root = AsObject(raw);
                if (root == null)
                {
                    continue;
                }
                result.ParsedEventCount++;

                string type = GetString(root, "type");
                Dictionary<string, object> payload = GetObject(root, "payload");
                string payloadType = GetString(payload, "type");

                if (string.Equals(type, "session_meta", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "thread.started", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "session.started", StringComparison.OrdinalIgnoreCase))
                {
                    string id = FirstNonEmpty(
                        GetString(root, "session_id"),
                        GetString(root, "thread_id"),
                        GetString(payload, "session_id"),
                        GetString(payload, "id"),
                        GetString(payload, "thread_id"));
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.SessionId = id;
                    }
                    string provider = FirstNonEmpty(GetString(payload, "model_provider"), GetString(root, "model_provider"));
                    if (!string.IsNullOrWhiteSpace(provider))
                    {
                        result.Provider = provider;
                    }
                }

                string directProvider = FirstNonEmpty(GetString(payload, "model_provider"), GetString(root, "model_provider"));
                if (!string.IsNullOrWhiteSpace(directProvider))
                {
                    result.Provider = directProvider;
                }

                if (string.Equals(type, "turn_context", StringComparison.OrdinalIgnoreCase))
                {
                    string turnModel = GetString(payload, "model");
                    if (!string.IsNullOrWhiteSpace(turnModel))
                    {
                        result.FinalModel = turnModel;
                    }
                }

                if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "turn.failed", StringComparison.OrdinalIgnoreCase))
                {
                    string remoteError = ExtractRemoteError(root);
                    if (!string.IsNullOrWhiteSpace(remoteError))
                    {
                        result.RemoteError = remoteError;
                        string errorProvider = ExtractJsonStringField(remoteError, "provider");
                        if (!string.IsNullOrWhiteSpace(errorProvider))
                        {
                            result.Provider = errorProvider;
                        }
                        string errorModel = ExtractJsonStringField(remoteError, "model");
                        if (!string.IsNullOrWhiteSpace(errorModel))
                        {
                            result.FinalModel = errorModel;
                        }
                    }
                }

                Dictionary<string, object> payloadRateLimits = GetObject(payload, "rate_limits");
                if (payloadRateLimits != null)
                {
                    ApplyRateLimitResetEvidence(result, payloadRateLimits);
                }
                Dictionary<string, object> rootRateLimits = GetObject(root, "rate_limits");
                if (rootRateLimits != null)
                {
                    ApplyRateLimitResetEvidence(result, rootRateLimits);
                }

                string finalText = ExtractAssistantText(root, payload, payloadType);
                if (!string.IsNullOrWhiteSpace(finalText))
                {
                    result.FinalText = finalText.Trim();
                }
            }
            FinalizeResetEvidence(result);
            return result;
        }

        private static void ApplyRateLimitResetEvidence(CodexRunAnalysis result, Dictionary<string, object> rateLimits)
        {
            if (!string.Equals(GetString(rateLimits, "limit_id"), "codex", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Dictionary<string, object> primary = GetObject(rateLimits, "primary");
            if (primary == null || GetInt(primary, "window_minutes") != 300)
            {
                return;
            }

            string rawReset = GetString(primary, "resets_at");
            long? resetUnix = ParseResetUnix(rawReset);
            if (!resetUnix.HasValue)
            {
                result.HasInvalidResetEvidence = true;
                result.ResetTimeDiagnostic = "本次响应的额度重置字段缺少有效且带时区的时间。";
                return;
            }

            if (!result.ResetCandidatesUnix.Contains(resetUnix.Value))
            {
                result.ResetCandidatesUnix.Add(resetUnix.Value);
            }
        }

        private static void FinalizeResetEvidence(CodexRunAnalysis result)
        {
            if (result.ResetCandidatesUnix.Count > 1)
            {
                result.ResetsAtUnix = null;
                result.ResetAtLocal = string.Empty;
                result.HasReliableResetTime = false;
                result.ResetTimeDiagnostic = "本次响应包含冲突的额度重置时间。";
                return;
            }

            if (result.HasInvalidResetEvidence)
            {
                result.ResetsAtUnix = null;
                result.ResetAtLocal = string.Empty;
                result.HasReliableResetTime = false;
                result.ResetTimeDiagnostic = result.ResetCandidatesUnix.Count == 0
                    ? "本次响应的额度重置字段缺少有效且带时区的时间。"
                    : "本次响应同时包含有效和无效的额度重置时间，无法确认唯一可靠值。";
                return;
            }

            if (result.ResetCandidatesUnix.Count == 1)
            {
                result.ResetsAtUnix = result.ResetCandidatesUnix[0];
                try
                {
                    result.ResetAtLocal = DateTimeOffset.FromUnixTimeSeconds(result.ResetsAtUnix.Value)
                        .ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                    result.HasReliableResetTime = true;
                    result.ResetTimeDiagnostic = "已获得唯一且可靠的额度重置时间。";
                }
                catch
                {
                    result.ResetsAtUnix = null;
                    result.ResetAtLocal = string.Empty;
                    result.HasReliableResetTime = false;
                    result.ResetTimeDiagnostic = "本次响应的额度重置时间超出 Windows 可表示范围。";
                }
                return;
            }

            result.ResetsAtUnix = null;
            result.ResetAtLocal = string.Empty;
            result.HasReliableResetTime = false;
            result.ResetTimeDiagnostic = string.IsNullOrWhiteSpace(result.ResetTimeDiagnostic)
                ? "本次响应未提供唯一且可靠的额度重置时间。"
                : result.ResetTimeDiagnostic;
        }

        private static long? ParseResetUnix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            long unix;
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out unix))
            {
                return unix > 0 ? (long?)unix : null;
            }

            DateTimeOffset parsed;
            bool hasExplicitOffset = Regex.IsMatch(raw.Trim(), @"(?:Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase);
            if (hasExplicitOffset
                && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
            {
                long value = parsed.ToUnixTimeSeconds();
                return value > 0 ? (long?)value : null;
            }
            return null;
        }

        private static string ExtractAssistantText(Dictionary<string, object> root, Dictionary<string, object> payload, string payloadType)
        {
            Dictionary<string, object> message = null;
            if (string.Equals(GetString(payload, "type"), "message", StringComparison.OrdinalIgnoreCase))
            {
                message = payload;
            }
            else if (string.Equals(GetString(root, "type"), "response_item", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetString(payload, "type"), "message", StringComparison.OrdinalIgnoreCase))
            {
                message = payload;
            }
            else if (string.Equals(GetString(root, "type"), "item.completed", StringComparison.OrdinalIgnoreCase))
            {
                message = GetObject(root, "item");
            }
            else if (string.Equals(GetString(root, "type"), "item_completed", StringComparison.OrdinalIgnoreCase))
            {
                message = GetObject(payload, "item");
            }
            else if (string.Equals(payloadType, "item_completed", StringComparison.OrdinalIgnoreCase))
            {
                message = GetObject(payload, "item");
            }

            if (message == null)
            {
                return string.Empty;
            }
            string role = GetString(message, "role");
            string messageType = GetString(message, "type");
            bool assistant = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(messageType, "agent_message", StringComparison.OrdinalIgnoreCase);
            if (!assistant)
            {
                return string.Empty;
            }

            string direct = FirstNonEmpty(GetString(message, "text"), GetString(message, "message"));
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }
            object[] content = GetArray(message, "content");
            if (content == null)
            {
                return string.Empty;
            }
            StringBuilder builder = new StringBuilder();
            foreach (object item in content)
            {
                Dictionary<string, object> part = AsObject(item);
                string value = GetString(part, "text");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }
                    builder.Append(value);
                }
            }
            return builder.ToString();
        }

        private static string ExtractRemoteError(Dictionary<string, object> root)
        {
            if (root == null)
            {
                return string.Empty;
            }
            string direct = GetString(root, "message");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }
            Dictionary<string, object> error = GetObject(root, "error");
            return error == null ? string.Empty : GetString(error, "message");
        }

        private static string ExtractJsonStringField(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }
            Match match = Regex.Match(
                value,
                "\\\"" + Regex.Escape(fieldName) + "\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : string.Empty;
        }

        private static CodexRunAnalysis Merge(CodexRunAnalysis first, CodexRunAnalysis second)
        {
            if (string.IsNullOrWhiteSpace(first.SessionId)) first.SessionId = second.SessionId;
            if (string.IsNullOrWhiteSpace(first.Provider)) first.Provider = second.Provider;
            if (string.IsNullOrWhiteSpace(first.FinalText)) first.FinalText = second.FinalText;
            if (string.IsNullOrWhiteSpace(first.LimitId)) first.LimitId = second.LimitId;
            foreach (long candidate in second.ResetCandidatesUnix)
            {
                if (!first.ResetCandidatesUnix.Contains(candidate))
                {
                    first.ResetCandidatesUnix.Add(candidate);
                }
            }
            first.HasInvalidResetEvidence = first.HasInvalidResetEvidence || second.HasInvalidResetEvidence;
            if (!first.OfficialQuotaConfirmed) first.OfficialQuotaConfirmed = second.OfficialQuotaConfirmed;
            if (string.IsNullOrWhiteSpace(first.QuotaStatus) || first.QuotaStatus == "unconfirmed") first.QuotaStatus = second.QuotaStatus;
            if (first.WindowMinutes <= 0) first.WindowMinutes = second.WindowMinutes;
            if (!first.UsedPercent.HasValue) first.UsedPercent = second.UsedPercent;
            if (!first.ResetsAtUnix.HasValue) first.ResetsAtUnix = second.ResetsAtUnix;
            if (string.IsNullOrWhiteSpace(first.ResetAtLocal)) first.ResetAtLocal = second.ResetAtLocal;
            if (string.IsNullOrWhiteSpace(first.PlanType)) first.PlanType = second.PlanType;
            if (second.OfficialQuotaConfirmed || string.IsNullOrWhiteSpace(first.Diagnostic)) first.Diagnostic = second.Diagnostic;
            first.ParsedEventCount += second.ParsedEventCount;
            return first;
        }

        private static string FindSessionFile(string sessionId)
        {
            Guid id;
            if (!Guid.TryParse(sessionId, out id))
            {
                return null;
            }
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            string sessionsRoot = Path.Combine(codexHome, "sessions");
            if (!Directory.Exists(sessionsRoot))
            {
                return null;
            }
            string[] files = Directory.GetFiles(sessionsRoot, "*" + id.ToString("D") + "*.jsonl", SearchOption.AllDirectories);
            return files.Length == 0 ? null : files[0];
        }

        private static string BuildActualRouteSummary(RouteSnapshot route, string provider)
        {
            string routeName = route == null ? "未识别链路" : route.DisplayName;
            if (route != null && route.IsLocalRouter && string.Equals(provider, "custom", StringComparison.OrdinalIgnoreCase))
            {
                routeName = route.DisplayName;
            }
            return routeName + "；provider=" + (string.IsNullOrWhiteSpace(provider) ? "未识别" : provider);
        }

        private static string CleanForJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            return Regex.Replace(value, "\\x1B\\[[0-?]*[ -/]*[@-~]", string.Empty).Trim();
        }

        private static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> parent, string key)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key) || !parent.ContainsKey(key))
            {
                return null;
            }
            return AsObject(parent[key]);
        }

        private static object[] GetArray(Dictionary<string, object> parent, string key)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key) || !parent.ContainsKey(key))
            {
                return null;
            }
            return parent[key] as object[];
        }

        private static string GetString(Dictionary<string, object> parent, string key)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key) || !parent.ContainsKey(key) || parent[key] == null)
            {
                return string.Empty;
            }
            return Convert.ToString(parent[key], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static int GetInt(Dictionary<string, object> parent, string key)
        {
            int value;
            return int.TryParse(GetString(parent, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static long? GetLong(Dictionary<string, object> parent, string key)
        {
            long value;
            return long.TryParse(GetString(parent, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? (long?)value : null;
        }

        private static double? GetDouble(Dictionary<string, object> parent, string key)
        {
            double value;
            return double.TryParse(GetString(parent, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? (double?)value : null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return string.Empty;
        }
    }

    internal static class RouteTestPresentation
    {
        public static string LinkStatus(RunRecord record)
        {
            if (record == null)
            {
                return "尚未测试";
            }
            return record.Success ? "链路已联通" : "链路未联通";
        }

        public static string FinalModelLabel(RunRecord record)
        {
            if (record == null)
            {
                return "最终模型尚未识别";
            }
            return "最终模型：" + FinalModelValue(record);
        }

        public static string FinalModelValue(RunRecord record)
        {
            return record == null || string.IsNullOrWhiteSpace(record.FinalModel)
                ? "未识别"
                : record.FinalModel.Trim();
        }

        public static string RouteSummary(RunRecord record)
        {
            if (record == null)
            {
                return "未记录";
            }

            string summary = record.RouteSummary ?? string.Empty;
            summary = summary.Replace("官方直连", "直接链路");
            summary = summary.Replace("官方 GPT", "GPT");
            summary = Regex.Replace(summary, @"(?:^|；)官方额度[^；\r\n]*", string.Empty, RegexOptions.IgnoreCase);
            summary = Regex.Replace(summary, @"(?:^|；)目标(?:模式|结果)[^；\r\n]*", string.Empty, RegexOptions.IgnoreCase);
            summary = Regex.Replace(summary, @"(?:^|；)final_model=[^；\r\n]*", string.Empty, RegexOptions.IgnoreCase);
            summary = summary.Trim('；', ' ', '\r', '\n');
            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = string.IsNullOrWhiteSpace(record.Provider)
                    ? "未识别链路"
                    : "provider=" + record.Provider;
            }
            return summary;
        }

        public static string FailureNextStep(RunRecord record)
        {
            string error = record == null ? string.Empty : record.Error ?? string.Empty;
            if (error.IndexOf("找不到 Codex CLI", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "请先安装或登录 Codex CLI，然后重新测试。";
            }
            if (error.IndexOf("网络", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("超时", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("不可达", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "请检查唤醒后的网络、代理和当前 Codex 配置，然后重新测试。";
            }
            if (error.IndexOf("没有找到最终模型回复", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "请确认当前 Codex / CC Switch 配置能够返回模型回复，再重新测试。";
            }
            return "请打开日志查看脱敏诊断，确认当前 Codex / CC Switch 配置后重新测试。";
        }
    }

    internal sealed class TestResultDialog : Form
    {
        private readonly Panel viewport;
        private readonly Panel footer;
        private readonly List<Control> rows = new List<Control>();
        private readonly Label headline;
        private readonly Label model;
        private readonly Label catalog;
        private readonly TextBox details;
        private readonly Button detailsButton;
        private readonly Button planButton;
        private readonly Button closeButton;
        private readonly Button logButton;
        private readonly bool planApplied;
        private bool detailsExpanded;
        private bool layingOut;
        private bool hiddenVerification;

        protected override bool ShowWithoutActivation { get { return hiddenVerification || base.ShowWithoutActivation; } }

        public TestResultDialog(RunRecord record, ResetScheduleAdjustment adjustment, string catalogFeedback, Action openLog)
        {
            record = record ?? new RunRecord();
            adjustment = adjustment ?? new ResetScheduleAdjustment();
            planApplied = adjustment.Applied;
            Text = "测试请求链路";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(248, 248, 252);
            ForeColor = Color.FromArgb(42, 44, 52);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(380, 400);
            Size area = Screen.FromControl(this).WorkingArea.Size;
            ClientSize = new Size(Math.Min(620, Math.Max(360, area.Width - 80)), Math.Min(620, Math.Max(360, area.Height - 100)));
            MinimizeBox = false;
            ShowInTaskbar = false;
            viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.White };
            Controls.Add(viewport);
            Controls.Add(footer);
            headline = AddText(record.Success ? "请求成功" : "请求失败", 20F);
            headline.ForeColor = record.Success ? Color.FromArgb(33, 126, 86) : Color.FromArgb(178, 54, 66);
            AddText(FormatTime(record), 9F).ForeColor = Color.FromArgb(104, 108, 120);
            model = AddText(string.IsNullOrWhiteSpace(record.FinalModel)
                ? "未确认实际响应模型"
                : "最终模型：" + record.FinalModel.Trim(), 12F);
            if (record.Success && string.IsNullOrWhiteSpace(record.FinalModel))
                AddText("已收到有效回复，本次请求成功。模型信息未被链路返回。", 9F);
            if (!record.Success)
            {
                AddText("失败原因", 10F);
                AddScrollable(string.IsNullOrWhiteSpace(record.Error) ? "未记录具体原因，请查看日志。" : record.Error, 110);
                AddText("下一步：" + RouteTestPresentation.FailureNextStep(record), 10F);
            }
            AddText(planApplied ? "计划已预填 · 尚未保存" : "计划预填", 10F);
            if (planApplied)
            {
                Label start = AddText(adjustment.LocalStart.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), 16F);
                start.ForeColor = Color.FromArgb(103, 73, 184);
                AddText("首次执行时间已填入计划草稿。查看后点击“保存并启用”才会生效。", 9F);
            }
            else
                AddText(string.IsNullOrWhiteSpace(adjustment.Message) ? "本次未调整计划。" : adjustment.Message, 9F);
            catalog = AddText(catalogFeedback ?? "模型列表正在刷新，稍后会更新候选。", 9F);
            catalog.ForeColor = Color.FromArgb(104, 108, 120);
            detailsButton = new Button { Text = "查看技术详情", FlatStyle = FlatStyle.Flat, TabIndex = 0 };
            detailsButton.FlatAppearance.BorderSize = 0;
            rows.Add(detailsButton);
            viewport.Controls.Add(detailsButton);
            details = AddScrollable(BuildDetails(record), 180);
            details.Visible = false;
            detailsButton.Click += delegate { ToggleDetails(); };
            closeButton = NewButton("关闭", 110);
            closeButton.DialogResult = DialogResult.OK;
            closeButton.BackColor = Color.FromArgb(103, 73, 184);
            closeButton.ForeColor = Color.White;
            planButton = NewButton("查看计划", 110);
            planButton.Visible = planApplied;
            planButton.DialogResult = DialogResult.Yes;
            logButton = NewButton("打开日志", 100);
            logButton.Click += delegate { if (openLog != null) openLog(); };
            AcceptButton = closeButton;
            CancelButton = closeButton;
            viewport.SizeChanged += delegate { LayoutContent(); };
            footer.SizeChanged += delegate { LayoutContent(); };
            LayoutContent();
        }

        private Label AddText(string text, float size)
        {
            Label label = new Label { Text = text, Font = new Font("Microsoft YaHei UI", size), AutoSize = false, UseCompatibleTextRendering = false };
            rows.Add(label);
            viewport.Controls.Add(label);
            return label;
        }

        private TextBox AddScrollable(string text, int height)
        {
            TextBox box = new TextBox { Text = text, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = true, BorderStyle = BorderStyle.None,
                BackColor = Color.White, Tag = height };
            rows.Add(box);
            viewport.Controls.Add(box);
            return box;
        }

        private Button NewButton(string text, int width)
        {
            Button button = new Button { Text = text, FlatStyle = FlatStyle.Flat, Tag = width, TabIndex = footer.Controls.Count };
            button.FlatAppearance.BorderColor = Color.FromArgb(219, 216, 228);
            footer.Controls.Add(button);
            return button;
        }

        private void ToggleDetails()
        {
            detailsExpanded = !detailsExpanded;
            details.Visible = detailsExpanded;
            detailsButton.Text = detailsExpanded ? "收起技术详情" : "查看技术详情";
            LayoutContent();
            ScrollRowIntoView(detailsExpanded ? (Control)details : detailsButton);
        }

        private void ScrollRowIntoView(Control row)
        {
            int top = row.Top - viewport.AutoScrollPosition.Y;
            viewport.AutoScrollPosition = new Point(0, Math.Max(0, top - (int)(24 * Font.Size / 9F)));
        }

        public void UpdateCatalogFeedback(string text)
        {
            if (IsDisposed || Disposing) return;
            catalog.Text = text ?? string.Empty;
            LayoutContent();
        }

        private void LayoutContent()
        {
            if (layingOut || viewport == null || closeButton == null) return;
            layingOut = true;
            try
            {
                float scale = Font.Size / 9F;
                int padding = (int)(24 * scale);
                int gap = (int)(12 * scale);
                int width = Math.Max(100, viewport.ClientSize.Width - padding * 2 - SystemInformation.VerticalScrollBarWidth);
                int y = padding;
                Point scroll = viewport.AutoScrollPosition;
                foreach (Control control in rows)
                {
                    if (control == details && !detailsExpanded) continue;
                    int height;
                    Label label = control as Label;
                    if (label != null)
                        height = Math.Max(label.Font.Height + 4, TextRenderer.MeasureText(label.Text, label.Font,
                            new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4);
                    else if (control is TextBox)
                        height = (int)((int)control.Tag * scale);
                    else
                        height = (int)(36 * scale);
                    control.Bounds = new Rectangle(padding + scroll.X, y + scroll.Y, width, height);
                    y += height + gap;
                }
                viewport.AutoScrollMinSize = new Size(0, y + padding);
                footer.Height = (int)(62 * scale);
                int buttonHeight = (int)(36 * scale);
                int buttonGap = (int)(8 * scale);
                int x = footer.ClientSize.Width - (int)(16 * scale);
                foreach (Button button in new[] { closeButton, planButton, logButton })
                {
                    if (button == planButton && !planApplied) continue;
                    int buttonWidth = (int)((int)button.Tag * scale);
                    x -= buttonWidth;
                    button.Bounds = new Rectangle(x, (footer.Height - buttonHeight) / 2, buttonWidth, buttonHeight);
                    x -= buttonGap;
                }
            }
            finally { layingOut = false; }
        }

        internal static string BuildDetails(RunRecord record)
        {
            List<string> lines = new List<string>();
            AddUnique(lines, "实际链路", RouteTestPresentation.RouteSummary(record));
            AddUnique(lines, "provider", record.Provider);
            AddUnique(lines, "实际上游", record.UpstreamProvider);
            AddUnique(lines, "目标", record.UpstreamTarget);
            AddUnique(lines, "链路证据", record.UpstreamEvidence);
            AddUnique(lines, "远端诊断", record.RemoteError);
            AddUnique(lines, "重置时间说明", record.ResetTimeDiagnostic);
            lines.Add("退出码：" + record.ExitCode + "；尝试次数：" + record.AttemptCount);
            return string.Join("\r\n", lines);
        }

        private static void AddUnique(List<string> lines, string label, string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0 || lines.Any(delegate(string line) { return line.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0; })) return;
            lines.Add(label + "：" + value);
        }

        private static string FormatTime(RunRecord record)
        {
            DateTimeOffset time;
            string raw = string.IsNullOrWhiteSpace(record.FinishedAt) ? record.StartedAt : record.FinishedAt;
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                ? "完成时间：" + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "完成时间未记录";
        }

        internal static bool Verify(out string evidence)
        {
            bool ok = true;
            List<string> checks = new List<string>();
            foreach (bool success in new[] { true, false })
            foreach (int width in new[] { 620, 380 })
            foreach (float scale in new[] { 1F, 1.5F })
            {
                RunRecord record = new RunRecord { Success = success, FinalModel = success && width == 620 ? "gpt-6.1-sol" : "",
                    FinishedAt = "2026-10-07T10:00:00+08:00", Provider = "openai", UpstreamProvider = "openai",
                    RouteSummary = "CC Switch → openai", Error = string.Concat(Enumerable.Repeat("网络错误，连接超时。", 100)),
                    UpstreamEvidence = string.Concat(Enumerable.Repeat("脱敏链路信息。", 100)) };
                ResetScheduleAdjustment adjustment = new ResetScheduleAdjustment { Applied = success,
                    LocalStart = new DateTime(2026, 10, 8, 9, 10, 0), Message = "草稿已改变，未覆盖当前草稿。" };
                using (TestResultDialog dialog = new TestResultDialog(record, adjustment, "模型列表暂未刷新，仍可测试。", null))
                {
                    dialog.Font = new Font("Microsoft YaHei UI", 9F * scale);
                    dialog.ClientSize = new Size((int)(width * scale), (int)(430 * scale));
                    // Native scrolling needs a visible-state host. Keep this fixture transparent,
                    // off screen and non-activating so it never occupies the user's desktop.
                    dialog.hiddenVerification = true;
                    dialog.Opacity = 0;
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-32000, -32000);
                    dialog.Show();
                    IntPtr handle = dialog.Handle;
                    IntPtr viewportHandle = dialog.viewport.Handle;
                    IntPtr footerHandle = dialog.footer.Handle;
                    foreach (Control c in dialog.rows) { IntPtr child = c.Handle; }
                    dialog.PerformLayout();
                    dialog.LayoutContent();
                    bool state = dialog.headline.Text == (success ? "请求成功" : "请求失败")
                        && (record.FinalModel.Length > 0 || dialog.model.Text == "未确认实际响应模型")
                        && !dialog.detailsExpanded
                        && dialog.AcceptButton == dialog.closeButton && dialog.CancelButton == dialog.closeButton
                        && (!success || dialog.rows.OfType<Label>().Any(delegate(Label label) { return label.Text == "2026-10-08 09:10"; }));
                    string diagnostic = "initial=" + state;
                    dialog.ToggleDetails();
                    dialog.LayoutContent();
                    int bottom = int.MinValue;
                    foreach (Control c in dialog.rows)
                    {
                        state = state && c.Width > 0 && c.Height > 0 && c.Top >= bottom
                            && c.Left >= 0 && c.Right <= dialog.viewport.ClientSize.Width;
                        bottom = c.Bottom;
                    }
                    diagnostic += ",rows=" + state;
                    foreach (Button b in new[] { dialog.closeButton, dialog.logButton, dialog.planButton })
                        if (b != dialog.planButton || success) state = state && dialog.footer.ClientRectangle.Contains(b.Bounds);
                    diagnostic += ",footer=" + state;
                    dialog.ScrollRowIntoView(dialog.details);
                    state = state && dialog.viewport.ClientRectangle.IntersectsWith(dialog.details.Bounds)
                        && dialog.viewport.AutoScrollMinSize.Height > dialog.viewport.ClientSize.Height;
                    diagnostic += ",scroll=" + state + ":" + dialog.details.Bounds + "/" + dialog.viewport.ClientRectangle
                        + "/min=" + dialog.viewport.AutoScrollMinSize + "/offset=" + dialog.viewport.AutoScrollPosition + "/v=" + dialog.viewport.VerticalScroll.Visible;
                    dialog.ToggleDetails();
                    state = state && !dialog.detailsExpanded;
                    dialog.ToggleDetails();
                    dialog.UpdateCatalogFeedback("目录刷新失败，不影响本次请求。");
                    state = state && dialog.headline.Text == (success ? "请求成功" : "请求失败");
                    if (success)
                    {
                        MethodInfo click = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                        click.Invoke(dialog.planButton, new object[] { EventArgs.Empty });
                        state = state && dialog.DialogResult == DialogResult.Yes;
                    }
                    state = state && BuildDetails(record).IndexOf("provider：openai", StringComparison.Ordinal) < 0;
                    ok = ok && state;
                    checks.Add(success + "/" + width + "/" + scale + "=" + state + "[" + diagnostic + "]");
                }
            }
            foreach (string reason in new[] { "序列次数为 0，未应用预填。", "序列次数为空或无效，未应用预填。", "草稿已修改，未应用预填。", "未获得可靠重置时间，未应用预填。" })
            using (TestResultDialog dialog = new TestResultDialog(new RunRecord { Success = true, Model = "requested-only" },
                new ResetScheduleAdjustment { Message = reason }, "模型列表已刷新。", null))
            {
                bool state = dialog.headline.Text == "请求成功" && dialog.model.Text == "未确认实际响应模型"
                    && !dialog.planApplied && dialog.rows.OfType<Label>().Any(delegate(Label label) { return label.Text == reason; });
                checks.Add("skip:" + reason + "=" + state);
                ok = ok && state;
            }
            evidence = string.Join("; ", checks);
            return ok;
        }
    }

    internal sealed class ResetScheduleAdjustment
    {
        public bool Applied { get; set; }
        public DateTime LocalStart { get; set; }
        public string Message { get; set; }

        public ResetScheduleAdjustment()
        {
            Message = string.Empty;
        }
    }

    internal sealed class ManualTestScheduleSnapshot
    {
        public string SequenceCountText { get; set; }
        public DateTime SequenceStart { get; set; }
        public int SequenceIntervalMinutes { get; set; }
        public List<string> ExtraWakeTimes { get; set; }
    }

    internal static class ManualTestSchedulePolicy
    {
        public static ResetScheduleAdjustment EvaluateReset(
            RunRecord record,
            ManualTestScheduleSnapshot before,
            ManualTestScheduleSnapshot after,
            DateTime now)
        {
            if (!DraftMatches(before, after))
            {
                return Skipped("测试期间计划输入已修改，已保留当前草稿，未应用额度重置预填。");
            }

            int count;
            if (!SequenceCountInput.TryParse(before.SequenceCountText, out count))
            {
                return Skipped("序列次数为空或无效，未应用额度重置预填；计划草稿保持不变。");
            }
            if (count == 0)
            {
                return Skipped("序列次数为 0，未应用额度重置预填；计划草稿保持不变。");
            }

            return ResetSchedulePlanner.Evaluate(record, count, now);
        }

        private static bool DraftMatches(ManualTestScheduleSnapshot first, ManualTestScheduleSnapshot second)
        {
            if (first == null || second == null
                || !string.Equals(first.SequenceCountText, second.SequenceCountText, StringComparison.Ordinal)
                || first.SequenceStart != second.SequenceStart
                || first.SequenceIntervalMinutes != second.SequenceIntervalMinutes)
            {
                return false;
            }

            List<string> firstExtras = first.ExtraWakeTimes ?? new List<string>();
            List<string> secondExtras = second.ExtraWakeTimes ?? new List<string>();
            return firstExtras.SequenceEqual(secondExtras, StringComparer.OrdinalIgnoreCase);
        }

        private static ResetScheduleAdjustment Skipped(string message)
        {
            return new ResetScheduleAdjustment { Message = message };
        }
    }

    internal static class ResetSchedulePlanner
    {
        public static ResetScheduleAdjustment Evaluate(RunRecord record, int sequenceCount, DateTime now)
        {
            ResetScheduleAdjustment result = new ResetScheduleAdjustment();
            if (sequenceCount <= 0)
            {
                result.Message = "当前未启用执行序列，计划未调整。";
                return result;
            }
            if (record == null || !record.Success)
            {
                result.Message = "本次请求未成功，计划未调整。";
                return result;
            }
            if (!record.HasReliableResetTime || !record.ResetsAtUnix.HasValue)
            {
                result.Message = string.IsNullOrWhiteSpace(record.ResetTimeDiagnostic)
                    ? "本次未获得可靠额度重置时间，计划未调整。"
                    : record.ResetTimeDiagnostic + "计划未调整。";
                return result;
            }

            DateTimeOffset reset;
            try
            {
                reset = DateTimeOffset.FromUnixTimeSeconds(record.ResetsAtUnix.Value).ToLocalTime();
            }
            catch
            {
                result.Message = "额度重置时间无法转换为 Windows 本地时间，计划未调整。";
                return result;
            }

            if (reset.DateTime <= now)
            {
                result.Message = "额度重置时间已过去，计划未调整。";
                return result;
            }

            DateTime nextMinute = new DateTime(
                reset.Year,
                reset.Month,
                reset.Day,
                reset.Hour,
                reset.Minute,
                0,
                DateTimeKind.Unspecified).AddMinutes(1);
            if (nextMinute <= now)
            {
                result.Message = "额度重置后的可调度时间已过去，计划未调整。";
                return result;
            }

            result.Applied = true;
            result.LocalStart = nextMinute;
            result.Message = "已根据额度重置时间调整为 "
                + nextMinute.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + "，尚未保存。";
            return result;
        }
    }

    internal static class BackgroundRunner
    {
        private const string MutexName = "Local\\CodexQuotaWaker-Runner";

        public static RunRecord Execute(string mode, bool requireEnabled, AppConfig requestedConfig = null, string invokedPlanVersion = "")
        {
            RunRecord record = new RunRecord();
            record.Mode = mode;
            record.StartedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            AppConfig config = requestedConfig == null
                ? JsonStore.LoadConfig()
                : AppConfig.Normalize(requestedConfig);
            record.Model = config.Model;
            record.TargetMode = TargetModes.Normalize(config.TargetMode);
            RouteSnapshot initialRoute = CodexRouteDetector.Detect();
            record.RouteType = initialRoute.RouteType;
            record.RouteSummary = initialRoute.Summary;
            record.Provider = initialRoute.Provider;
            record.BaseUrlKind = initialRoute.BaseUrlKind;
            WakeEvidence wake = config.SleepAfterScheduledWake
                ? WakeDetector.Capture(mode)
                : new WakeEvidence { Description = "自动睡眠功能未启用" };
            record.WakeEvidence = wake.Description;

            using (Mutex mutex = new Mutex(false, MutexName))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = mutex.WaitOne(0, false);
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    if (!acquired)
                    {
                        record.Error = "已有一条触发任务正在运行，本次已跳过。";
                        Logger.Write("WARN", record.Error);
                        return Finish(record, config, wake);
                    }

                    if (requireEnabled && !config.Enabled)
                    {
                        record.Error = "计划已暂停，本次后台触发已跳过。";
                        Logger.Write("INFO", record.Error);
                        return Finish(record, config, wake);
                    }

                    if (mode == "scheduled")
                        TimelineHistoryStore.Begin(config, record, invokedPlanVersion);

                    string codexPath = CodexLocator.Find();
                    if (string.IsNullOrWhiteSpace(codexPath))
                    {
                        record.Error = "找不到 Codex CLI。请先打开 Codex，或重新安装 Codex 桌面应用。";
                        Logger.Write("ERROR", record.Error);
                        return Finish(record, config, wake);
                    }

                    Logger.Write("INFO", "开始 " + mode + " 触发；model=" + config.Model + "; route=" + record.RouteSummary + "; codex=" + codexPath);
                    if (!NativePower.KeepAwake())
                    {
                        Logger.Write("WARN", "Windows 未接受临时防睡眠请求，任务仍将继续。Win32Error=" + Marshal.GetLastWin32Error());
                    }

                    try
                    {
                        DateTime deadline = DateTime.Now.AddSeconds(ExecutionBudget.TotalSeconds(config.RetryCount));
                        if (!WaitForNetwork(deadline))
                        {
                            record.Error = "等待网络恢复超时。请检查睡眠唤醒后的 Wi-Fi 或代理连接。";
                            Logger.Write("ERROR", record.Error);
                            return Finish(record, config, wake);
                        }

                        for (int attempt = 1; attempt <= config.RetryCount; attempt++)
                        {
                            record.AttemptCount = attempt;
                            int remainingSeconds = (int)(deadline - DateTime.Now).TotalSeconds;
                            if (remainingSeconds < 20)
                            {
                                record.Error = "内部执行时间预算即将结束，停止继续重试。";
                                break;
                            }

                            int timeoutSeconds = Math.Min(remainingSeconds, 240);
                            string arguments = "exec --skip-git-repo-check --sandbox read-only --json -m "
                                + ProcessRunner.Quote(config.Model)
                                + " "
                                + ProcessRunner.Quote(config.Prompt);

                            Logger.Write("INFO", "执行 Codex 请求，第 " + attempt + " 次尝试。");
                            ProcessResult process = ProcessRunner.Run(
                                codexPath,
                                arguments,
                                AppPaths.BaseDirectory,
                                timeoutSeconds * 1000);

                            string stdout = CleanOutput(process.StandardOutput);
                            string stderr = CleanOutput(process.StandardError);
                            record.ExitCode = process.ExitCode;
                            record.Output = string.Empty;
                            record.Success = !process.TimedOut && process.ExitCode == 0;
                            CodexRunAnalyzer.Apply(process, config, initialRoute, record, DateTime.Now);
                            record.Output = Truncate(record.Output, 6000);

                            if (process.TimedOut)
                            {
                                record.Success = false;
                                record.Error = "Codex 请求超时。";
                            }
                            else if (process.ExitCode != 0)
                            {
                                record.Success = false;
                                record.Error = !string.IsNullOrWhiteSpace(record.RemoteError)
                                    ? Truncate(record.RemoteError, 6000)
                                    : string.IsNullOrWhiteSpace(stderr)
                                        ? "Codex 返回非零退出码：" + process.ExitCode
                                        : Truncate(stderr, 6000);
                            }
                            else if (string.IsNullOrWhiteSpace(record.Output)
                                || record.Output.StartsWith("已收到 Codex 结构化响应", StringComparison.Ordinal))
                            {
                                record.Success = false;
                                record.Error = "Codex 退出码为 0，但没有找到最终模型回复。";
                            }
                            else
                            {
                                record.Success = true;
                                record.Error = string.Empty;
                                record.TargetSatisfied = true;
                                Logger.Write("INFO", "Codex 请求成功并获得有效模型响应；route=" + record.RouteSummary + "; final_model=" + RouteTestPresentation.FinalModelValue(record));
                                break;
                            }

                            record.TargetSatisfied = false;
                            Logger.Write("WARN", "第 " + attempt + " 次尝试失败：" + record.Error);
                            if (attempt < config.RetryCount
                                && DateTime.Now.AddSeconds(ExecutionBudget.RetryDelaySeconds) < deadline)
                            {
                                Thread.Sleep(ExecutionBudget.RetryDelaySeconds * 1000);
                            }
                        }
                    }
                    finally
                    {
                        NativePower.Restore();
                    }

                    return Finish(record, config, wake);
                }
                catch (Exception ex)
                {
                    record.Error = ex.Message;
                    Logger.Write("ERROR", ex.ToString());
                    return Finish(record, config, wake);
                }
                finally
                {
                    if (acquired)
                    {
                        try
                        {
                            mutex.ReleaseMutex();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static RunRecord Finish(RunRecord record, AppConfig config, WakeEvidence wake)
        {
            record.FinishedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            TimelineHistoryStore.Finish(record);
            JsonStore.SaveLastRun(record);
            if (!record.Success)
            {
                Logger.Write("FAIL", record.Error);
            }
            else
            {
                Logger.Write("SUCCESS", "触发完成。" );
            }
            AutoSleepCoordinator.Handle(config, record, wake);
            return record;
        }

        private static bool WaitForNetwork(DateTime deadline)
        {
            DateTime networkDeadline = DateTime.Now.AddSeconds(ExecutionBudget.NetworkWaitSeconds);
            if (networkDeadline > deadline)
            {
                networkDeadline = deadline;
            }
            while (DateTime.Now < networkDeadline)
            {
                if (NetworkInterface.GetIsNetworkAvailable())
                {
                    return true;
                }
                Thread.Sleep(5000);
            }
            return NetworkInterface.GetIsNetworkAvailable();
        }

        private static string CleanOutput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            return Regex.Replace(value, "\\x1B\\[[0-?]*[ -/]*[@-~]", string.Empty).Trim();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }
            return value.Substring(0, maxLength) + "…";
        }
    }

    internal static class TaskXml
    {
        public const string TaskName = "CodexQuotaWaker-Schedule";

        public static string Build(AppConfig config, string executablePath, string userSid)
        {
            StringBuilder xml = new StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            xml.AppendLine("<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            xml.AppendLine("  <RegistrationInfo>");
            xml.AppendLine("    <Description>按计划唤醒电脑并执行一条可验证的 Codex 本地请求。</Description>");
            xml.AppendLine("    <URI>\\" + TaskName + "</URI>");
            xml.AppendLine("  </RegistrationInfo>");
            xml.AppendLine("  <Triggers>");
            DateTime sequenceStart;
            if (config.SequenceCount > 0 && AppConfig.TryParseSequenceStart(config.SequenceStart, out sequenceStart))
            {
                xml.AppendLine("    <TimeTrigger>");
                if (config.SequenceCount > 1)
                {
                    int intervalMinutes = SequenceSchedule.GetIntervalMinutes(config);
                    xml.AppendLine("      <Repetition>");
                    xml.AppendLine("        <Interval>" + ToDuration(intervalMinutes) + "</Interval>");
                    xml.AppendLine("        <Duration>" + SequenceSchedule.RepetitionDuration(config.SequenceCount, intervalMinutes) + "</Duration>");
                    xml.AppendLine("        <StopAtDurationEnd>false</StopAtDurationEnd>");
                    xml.AppendLine("      </Repetition>");
                }
                xml.AppendLine("      <StartBoundary>" + sequenceStart.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "</StartBoundary>");
                xml.AppendLine("      <Enabled>true</Enabled>");
                xml.AppendLine("    </TimeTrigger>");
            }
            foreach (string value in AppConfig.NormalizeDateTimes(config.ExtraWakeTimes))
            {
                DateTime extra;
                if (!AppConfig.TryParseDateTime(value, out extra))
                {
                    continue;
                }
                if (SequenceSchedule.ContainsRun(config, extra))
                {
                    continue;
                }
                xml.AppendLine("    <TimeTrigger>");
                xml.AppendLine("      <StartBoundary>" + extra.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "</StartBoundary>");
                xml.AppendLine("      <Enabled>true</Enabled>");
                xml.AppendLine("    </TimeTrigger>");
            }
            xml.AppendLine("  </Triggers>");
            xml.AppendLine("  <Principals>");
            xml.AppendLine("    <Principal id=\"Author\">");
            xml.AppendLine("      <UserId>" + Escape(userSid) + "</UserId>");
            xml.AppendLine("      <LogonType>" + (config.RunWithoutLogon ? "Password" : "InteractiveToken") + "</LogonType>");
            xml.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
            xml.AppendLine("    </Principal>");
            xml.AppendLine("  </Principals>");
            xml.AppendLine("  <Settings>");
            xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            xml.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            xml.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            xml.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            xml.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            xml.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            xml.AppendLine("    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>");
            xml.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            xml.AppendLine("    <Enabled>true</Enabled>");
            xml.AppendLine("    <Hidden>false</Hidden>");
            xml.AppendLine("    <WakeToRun>true</WakeToRun>");
            xml.AppendLine("    <ExecutionTimeLimit>PT" + ExecutionBudget.TaskExecutionLimitMinutes(config.RetryCount) + "M</ExecutionTimeLimit>");
            xml.AppendLine("    <Priority>7</Priority>");
            xml.AppendLine("  </Settings>");
            xml.AppendLine("  <Actions Context=\"Author\">");
            xml.AppendLine("    <Exec>");
            xml.AppendLine("      <Command>" + Escape(executablePath) + "</Command>");
            xml.AppendLine("      <Arguments>" + Escape(TimelineExecutionPolicy.Arguments(config)) + "</Arguments>");
            xml.AppendLine("      <WorkingDirectory>" + Escape(Path.GetDirectoryName(executablePath)) + "</WorkingDirectory>");
            xml.AppendLine("    </Exec>");
            xml.AppendLine("  </Actions>");
            xml.AppendLine("</Task>");
            return xml.ToString();
        }

        private static string Escape(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? string.Empty);
        }

        private static string ToDuration(int totalMinutes)
        {
            long minutes = Math.Max(1, totalMinutes);
            long hours = minutes / 60L;
            long remainder = minutes % 60L;
            StringBuilder duration = new StringBuilder("PT");
            if (hours > 0)
            {
                duration.Append(hours).Append("H");
            }
            if (remainder > 0 || hours == 0)
            {
                duration.Append(remainder).Append("M");
            }
            return duration.ToString();
        }
    }

    internal sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    internal sealed class WindowsTaskStatus
    {
        public bool Exists { get; set; }
        public bool Enabled { get; set; }
        public bool WakeToRun { get; set; }
        public string LogonType { get; set; }
        public string UserId { get; set; }
        public string Error { get; set; }
        public DateTime? NextRunTime { get; set; }
        public DateTime? LastRunTime { get; set; }
        public string RuntimeDetailsError { get; set; }
        public string DefinitionXml { get; set; }
        public DateTime? DefinitionSavedUtc { get; set; }

        public bool CanRunWithoutLogon
        {
            get
            {
                return string.Equals(LogonType, "Password", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(LogonType, "S4U", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(LogonType, "ServiceAccount", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    internal sealed class WakeTaskInspection
    {
        public bool Missing { get; set; }
        public bool? Exists { get; set; }
        public bool? Enabled { get; set; }
        public bool? WakeToRun { get; set; }
        public string LogonType { get; set; }
        public string Error { get; set; }
    }

    internal enum CurrentPowerSource
    {
        Unknown,
        PluggedIn,
        Battery
    }

    internal sealed class WakeSystemSnapshot
    {
        public DateTimeOffset CheckedAt { get; set; }
        public CurrentPowerSource PowerSource { get; set; }
        public string PowerSourceError { get; set; }
        public Guid? ActiveSchemeId { get; set; }
        public string ActiveSchemeName { get; set; }
        public string SchemeError { get; set; }
        public int? WakeTimerAc { get; set; }
        public int? WakeTimerDc { get; set; }
        public string WakeTimerAcError { get; set; }
        public string WakeTimerDcError { get; set; }
        public bool? WakeTimerVisible { get; set; }
        public string WakeTimerVisibilityError { get; set; }
        public bool? LidPresent { get; set; }
        public int? LidActionAc { get; set; }
        public int? LidActionDc { get; set; }
        public bool? LidActionVisible { get; set; }
        public string LidVisibilityError { get; set; }
        public string LidError { get; set; }
        public bool? SupportsSleep { get; set; }
        public bool? SupportsHibernate { get; set; }
        public bool? SupportsModernStandby { get; set; }
        public string SleepStates { get; set; }
        public int? RtcWakeState { get; set; }
        public string CapabilityError { get; set; }
    }

    internal sealed class WakeReadinessView
    {
        public string MainSummary { get; set; }
        public string Details { get; set; }
        public bool ShowPowerSettings { get; set; }
        public string CheckedAt { get; set; }
    }

    internal static class WakeSystemReader
    {
        private static readonly Guid SleepSubgroup = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid WakeTimerSetting = new Guid("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d");
        private static readonly Guid ButtonsSubgroup = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
        private static readonly Guid LidActionSetting = new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte AcLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadSettingAttributes(ref Guid subgroupGuid, ref Guid settingGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid,
            IntPtr subgroupGuid, IntPtr settingGuid, IntPtr buffer, ref uint bufferSize);

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool GetPwrCapabilities(IntPtr capabilities);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static WakeSystemSnapshot Read()
        {
            WakeSystemSnapshot snapshot = new WakeSystemSnapshot { CheckedAt = DateTimeOffset.Now, PowerSource = CurrentPowerSource.Unknown };
            try
            {
                SystemPowerStatus status;
                if (!GetSystemPowerStatus(out status)) snapshot.PowerSourceError = Win32Error(Marshal.GetLastWin32Error());
                else if (status.AcLineStatus == 1) snapshot.PowerSource = CurrentPowerSource.PluggedIn;
                else if (status.AcLineStatus == 0 && status.BatteryFlag != 128) snapshot.PowerSource = CurrentPowerSource.Battery;
                else snapshot.PowerSourceError = "Windows 未报告明确的插电或电池状态。";
            }
            catch (Exception ex) { snapshot.PowerSourceError = ReadError(ex); }

            try
            {
                IntPtr schemePointer;
                uint result = PowerGetActiveScheme(IntPtr.Zero, out schemePointer);
                if (result != 0) snapshot.SchemeError = Win32Error((int)result);
                else
                {
                    try
                    {
                        Guid scheme = (Guid)Marshal.PtrToStructure(schemePointer, typeof(Guid));
                        snapshot.ActiveSchemeId = scheme;
                        snapshot.ActiveSchemeName = ReadSchemeName(scheme);
                        string settingError;
                        snapshot.WakeTimerVisible = ReadSettingVisible(SleepSubgroup, WakeTimerSetting, out settingError);
                        snapshot.WakeTimerVisibilityError = settingError;
                        snapshot.LidActionVisible = ReadSettingVisible(ButtonsSubgroup, LidActionSetting, out settingError);
                        snapshot.LidVisibilityError = settingError;
                        snapshot.WakeTimerAc = ReadSetting(scheme, SleepSubgroup, WakeTimerSetting, true, out settingError);
                        snapshot.WakeTimerAcError = settingError;
                        snapshot.WakeTimerDc = ReadSetting(scheme, SleepSubgroup, WakeTimerSetting, false, out settingError);
                        snapshot.WakeTimerDcError = settingError;
                        snapshot.LidActionAc = ReadSetting(scheme, ButtonsSubgroup, LidActionSetting, true, out settingError);
                        snapshot.LidError = settingError;
                        string dcError;
                        snapshot.LidActionDc = ReadSetting(scheme, ButtonsSubgroup, LidActionSetting, false, out dcError);
                        if (string.IsNullOrWhiteSpace(snapshot.LidError)) snapshot.LidError = dcError;
                    }
                    finally { LocalFree(schemePointer); }
                }
            }
            catch (Exception ex) { snapshot.SchemeError = ReadError(ex); }

            ReadCapabilities(snapshot);
            return snapshot;
        }

        private static int? ReadSetting(Guid scheme, Guid subgroup, Guid setting, bool ac, out string error)
        {
            uint value;
            uint status = ac
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
            error = status != 0 ? Win32Error((int)status) : value > 100 ? "Windows 返回了无法识别的设置值。" : "";
            return status == 0 && value <= 100 ? (int?)value : null;
        }

        private static bool? ReadSettingVisible(Guid subgroup, Guid setting, out string error)
        {
            try
            {
                const uint PowerAttributeHide = 1;
                uint attributes = PowerReadSettingAttributes(ref subgroup, ref setting);
                error = "";
                return (attributes & PowerAttributeHide) == 0;
            }
            catch (Exception ex)
            {
                error = ReadError(ex);
                return null;
            }
        }

        private static string ReadSchemeName(Guid scheme)
        {
            IntPtr buffer = Marshal.AllocHGlobal(1024);
            try
            {
                uint size = 1024;
                uint status = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
                return status == 0 ? Marshal.PtrToStringUni(buffer) : "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static void ReadCapabilities(WakeSystemSnapshot snapshot)
        {
            IntPtr buffer = Marshal.AllocHGlobal(256);
            try
            {
                Marshal.Copy(new byte[256], 0, buffer, 256);
                if (!GetPwrCapabilities(buffer)) { snapshot.CapabilityError = Win32Error(Marshal.GetLastWin32Error()); return; }
                bool s1 = Marshal.ReadByte(buffer, 3) != 0;
                bool s2 = Marshal.ReadByte(buffer, 4) != 0;
                bool s3 = Marshal.ReadByte(buffer, 5) != 0;
                bool s4 = Marshal.ReadByte(buffer, 6) != 0;
                snapshot.LidPresent = Marshal.ReadByte(buffer, 2) != 0;
                // SYSTEM_POWER_CAPABILITIES marks AoAc after 22 bytes of fixed BOOLEAN/BYTE fields.
                snapshot.SupportsModernStandby = Marshal.ReadByte(buffer, 20) != 0;
                snapshot.SupportsSleep = s1 || s2 || s3 || snapshot.SupportsModernStandby.Value;
                snapshot.SupportsHibernate = s4;
                List<string> states = new List<string>();
                if (snapshot.SupportsModernStandby.Value) states.Add("S0 低功耗待机（现代待机）");
                if (s1) states.Add("S1"); if (s2) states.Add("S2"); if (s3) states.Add("S3"); if (s4) states.Add("S4 休眠");
                snapshot.SleepStates = states.Count == 0 ? "Windows 未报告可用睡眠/休眠状态" : string.Join("、", states);
                int rtcWake = Marshal.ReadInt32(buffer, 64);
                if (rtcWake >= 1 && rtcWake <= 6) snapshot.RtcWakeState = rtcWake;
            }
            catch (Exception ex) { snapshot.CapabilityError = ReadError(ex); }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        internal static string Win32Error(int code)
        {
            string detail = code == 0 ? "Windows 未提供错误码" : new Win32Exception(code).Message;
            return "Windows 未能读取此项（" + detail + "）。";
        }

        private static string ReadError(Exception ex)
        {
            return ex is DllNotFoundException || ex is EntryPointNotFoundException
                ? "此 Windows 版本未提供所需的电源查询接口。"
                : "读取失败（" + ex.GetType().Name + "）。";
        }
    }

    internal static class WakeReadinessPolicy
    {
        private static string Timer(int? value, bool? visible, string error)
        {
            if (visible == false) return "未能确认（此选项在高级电源设置中隐藏）";
            if (visible != true) return "未能确认" + (string.IsNullOrWhiteSpace(error) ? "" : "（" + error + "）");
            if (!value.HasValue) return "未能确认";
            if (value == 0) return "已关闭";
            if (value == 1) return "已开启";
            if (value == 2) return "仅重要唤醒（受限）";
            return "未知设置值（未能确认）";
        }

        private static string Lid(int? value, bool? present, bool? visible, string error)
        {
            if (present == false) return "设备未报告合盖开关（不适用）";
            if (present != true) return "未能确认" + (string.IsNullOrWhiteSpace(error) ? "" : "：" + error);
            if (visible == false) return "未能确认（合盖动作选项在高级电源设置中隐藏）";
            if (visible != true) return "未能确认" + (string.IsNullOrWhiteSpace(error) ? "" : "：" + error);
            if (!value.HasValue) return "未能确认" + (string.IsNullOrWhiteSpace(error) ? "" : "：" + error);
            switch (value.Value)
            {
                case 0: return "合盖后不操作（电脑保持运行）";
                case 1: return "合盖后睡眠";
                case 2: return "合盖后休眠";
                case 3: return "合盖后关机";
                default: return "未知设置值（未能确认）";
            }
        }

        public static string TaskLogon(string logonType)
        {
            if (string.Equals(logonType, "InteractiveToken", StringComparison.OrdinalIgnoreCase)) return "交互登录：锁屏仍登录时可运行；注销/重启后未登录时不能运行";
            if (string.Equals(logonType, "Password", StringComparison.OrdinalIgnoreCase)) return "密码模式：任务可在用户未登录时运行";
            if (string.Equals(logonType, "S4U", StringComparison.OrdinalIgnoreCase)) return "S4U 模式：可无交互登录运行，访问网络/加密资源受限";
            if (string.Equals(logonType, "ServiceAccount", StringComparison.OrdinalIgnoreCase)) return "服务账户模式：以任务中指定的服务账户运行";
            return "登录模式未能确认";
        }

        private static bool IsKnownLogonType(string logonType)
        {
            return string.Equals(logonType, "InteractiveToken", StringComparison.OrdinalIgnoreCase)
                || string.Equals(logonType, "Password", StringComparison.OrdinalIgnoreCase)
                || string.Equals(logonType, "S4U", StringComparison.OrdinalIgnoreCase)
                || string.Equals(logonType, "ServiceAccount", StringComparison.OrdinalIgnoreCase);
        }

        private static string XmlText(XmlNode node, string localName)
        {
            XmlNode child = node == null ? null : node.SelectSingleNode("./*[local-name()='" + localName + "']");
            return child == null ? "" : child.InnerText.Trim();
        }

        public static WakeTaskInspection InspectTask(WindowsTaskStatus task)
        {
            if (task == null) return new WakeTaskInspection { Error = "未能取得任务状态；请重新检查。" };
            if (!task.Exists)
                return IsMissingTaskError(task.Error)
                    ? new WakeTaskInspection { Exists = false, Missing = true }
                    : new WakeTaskInspection { Error = "Windows 未能确认本程序的计划是否存在；请检查任务计划程序后重试。" };
            if (!string.IsNullOrWhiteSpace(task.Error) || string.IsNullOrWhiteSpace(task.DefinitionXml))
                return new WakeTaskInspection { Exists = true, Error = "无法读取计划任务定义；请在任务计划程序核对，随后重新检查。" };
            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(task.DefinitionXml);
                XmlNode settings = document.SelectSingleNode("//*[local-name()='Settings']");
                XmlNode principal = document.SelectSingleNode("//*[local-name()='Principal']");
                bool parsed;
                string enabledText = XmlText(settings, "Enabled"), wakeText = XmlText(settings, "WakeToRun");
                return new WakeTaskInspection
                {
                    Exists = true,
                    Enabled = bool.TryParse(enabledText, out parsed) ? (bool?)parsed : null,
                    WakeToRun = bool.TryParse(wakeText, out parsed) ? (bool?)parsed : null,
                    LogonType = XmlText(principal, "LogonType"),
                    Error = ""
                };
            }
            catch { return new WakeTaskInspection { Exists = true, Error = "无法解析计划任务定义；请重新检查任务计划程序。" }; }
        }

        private static bool IsMissingTaskError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            return error.IndexOf("cannot find the file", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("系统找不到指定的文件", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("找不到指定的文件", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("找不到指定的路径", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("指定されたファイルが見つかりません", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static WakeReadinessView Evaluate(WakeSystemSnapshot power, WindowsTaskStatus task)
        {
            power = power ?? new WakeSystemSnapshot { PowerSource = CurrentPowerSource.Unknown, CheckedAt = DateTimeOffset.Now };
            WakeTaskInspection taskState = InspectTask(task);
            string source = power.PowerSource == CurrentPowerSource.PluggedIn ? "插电" : power.PowerSource == CurrentPowerSource.Battery ? "电池" : "供电状态未能确认";
            int? currentTimer = power.WakeTimerVisible != true ? null
                : power.PowerSource == CurrentPowerSource.PluggedIn ? power.WakeTimerAc
                : power.PowerSource == CurrentPowerSource.Battery ? power.WakeTimerDc : null;
            bool settingsProblem = !IsEnabledTimer(power.WakeTimerAc, power.WakeTimerVisible)
                || !IsEnabledTimer(power.WakeTimerDc, power.WakeTimerVisible);
            string lead;
            if (taskState.Missing)
                lead = "尚未安装计划；保存并启用后，重新检查本程序唯一计划。";
            else if (!taskState.Exists.HasValue || !string.IsNullOrWhiteSpace(taskState.Error))
                lead = "实际计划状态未能确认；请检查 Windows 任务计划程序后重新检查。";
            else if (taskState.Enabled != true)
                lead = taskState.Enabled == false ? "计划已暂停；先在本程序中启用计划，再重新检查。" : "计划启用状态未能确认；重新检查任务定义。";
            else if (taskState.WakeToRun != true)
                lead = taskState.WakeToRun == false ? "该计划未开启 WakeToRun；请在本程序中更新计划。" : "任务是否允许唤醒未能确认；重新检查任务定义。";
            else if (power.PowerSource == CurrentPowerSource.Unknown)
                lead = "当前供电状态未能确认；插电或使用电池后重新检查。";
            else if (power.WakeTimerVisible == false)
                lead = source + "唤醒定时器选项在此电源方案中隐藏，当前值未能确认；请按设备和电源模式核对。";
            else if (!currentTimer.HasValue)
                lead = source + "唤醒定时器未能确认；打开电源设置检查后重试。";
            else if (currentTimer.Value == 0)
                lead = source + "唤醒定时器已关闭；需在电源设置中打开。";
            else if (currentTimer.Value == 2)
                lead = source + "只允许重要唤醒；本计划是否被唤醒未能确认。";
            else if (currentTimer.Value != 1)
                lead = source + "唤醒定时器值不受识别；请在电源设置中核对。";
            else if (power.SupportsSleep != true && power.SupportsHibernate != true)
                lead = "Windows 未确认可用的睡眠/休眠状态；不能据此预期睡眠唤醒。";
            else if (!IsKnownLogonType(taskState.LogonType))
                lead = "任务登录模式不受识别；能否在当前 Windows 会话下运行未能确认。";
            else if (string.Equals(taskState.LogonType, "InteractiveToken", StringComparison.OrdinalIgnoreCase))
                lead = source + "设置允许尝试；运行时须保持 Windows 已登录（锁屏可以）。";
            else lead = source + "设置允许尝试计划唤醒；合盖/睡眠唤醒尚未经本机验证。";

            StringBuilder details = new StringBuilder();
            details.Append("检查时间：").Append(power.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            details.Append("\r\n当前供电：").Append(source);
            details.Append("\r\n当前电源方案：").Append(string.IsNullOrWhiteSpace(power.ActiveSchemeName) ? (power.ActiveSchemeId.HasValue ? power.ActiveSchemeId.Value.ToString("B") : "未能确认") : power.ActiveSchemeName);
            details.Append("\r\n系统睡眠能力：").Append(power.SleepStates ?? "未能确认");
            if (!string.IsNullOrWhiteSpace(power.CapabilityError)) details.Append("（").Append(power.CapabilityError).Append("）");
            details.Append("\r\n硬件 RTC 可唤醒的最深状态：").Append(FormatRtc(power.RtcWakeState, power.SupportsModernStandby));
            details.Append("\r\n合盖动作：插电 ").Append(Lid(power.LidActionAc, power.LidPresent, power.LidActionVisible, power.LidError));
            details.Append("；电池 ").Append(Lid(power.LidActionDc, power.LidPresent, power.LidActionVisible, power.LidError));
            details.Append("\r\n允许唤醒定时器：插电 ").Append(Timer(power.WakeTimerAc, power.WakeTimerVisible, power.WakeTimerVisibilityError));
            if ((power.WakeTimerVisible != true || !power.WakeTimerAc.HasValue || power.WakeTimerAc.Value != 1) && !string.IsNullOrWhiteSpace(power.WakeTimerAcError)) details.Append("（").Append(power.WakeTimerAcError).Append("）");
            details.Append("；电池 ").Append(Timer(power.WakeTimerDc, power.WakeTimerVisible, power.WakeTimerVisibilityError));
            if ((power.WakeTimerVisible != true || !power.WakeTimerDc.HasValue || power.WakeTimerDc.Value != 1) && !string.IsNullOrWhiteSpace(power.WakeTimerDcError)) details.Append("（").Append(power.WakeTimerDcError).Append("）");
            if (!string.IsNullOrWhiteSpace(power.SchemeError)) details.Append("\r\n电源方案查询：").Append(power.SchemeError);
            if (!string.IsNullOrWhiteSpace(power.WakeTimerAcError)) details.Append("\r\n插电定时器查询：").Append(power.WakeTimerAcError);
            if (!string.IsNullOrWhiteSpace(power.WakeTimerDcError)) details.Append("\r\n电池定时器查询：").Append(power.WakeTimerDcError);
            if (!string.IsNullOrWhiteSpace(power.LidError)) details.Append("\r\n合盖设置查询：").Append(power.LidError);
            if (!string.IsNullOrWhiteSpace(power.LidVisibilityError)) details.Append("\r\n合盖设置可见性查询：").Append(power.LidVisibilityError);
            if (!string.IsNullOrWhiteSpace(power.WakeTimerVisibilityError)) details.Append("\r\n唤醒定时器可见性查询：").Append(power.WakeTimerVisibilityError);
            if (taskState.Missing) details.Append("\r\n本程序的计划任务：不存在；请先保存并启用。");
            else if (!taskState.Exists.HasValue || !string.IsNullOrWhiteSpace(taskState.Error)) details.Append("\r\n本程序的计划任务：未能确认；").Append(taskState.Error);
            else
            {
                details.Append("\r\n实际计划：").Append(taskState.Enabled.HasValue ? (taskState.Enabled.Value ? "已启用" : "已暂停") : "未能确认是否启用");
                details.Append("；WakeToRun：").Append(taskState.WakeToRun.HasValue ? (taskState.WakeToRun.Value ? "已开启" : "已关闭") : "未能确认");
                details.Append("\r\n任务登录：").Append(TaskLogon(taskState.LogonType));
            }
            if (!IsEnabledTimer(power.WakeTimerAc, power.WakeTimerVisible) || !IsEnabledTimer(power.WakeTimerDc, power.WakeTimerVisible))
                details.Append("\r\n下一步：在 Windows“电源选项 → 更改计划设置 → 高级电源设置 → 睡眠 → 允许使用唤醒定时器”检查插电与电池；若此项未显示，请按设备和电源模式核对。");
            details.Append("\r\n上述检查只反映当前设置；真正的合盖/睡眠唤醒尚未实测。");
            bool openSettings = settingsProblem || (power.LidPresent == true && (power.LidActionVisible != true || !power.LidActionAc.HasValue || !power.LidActionDc.HasValue))
                || (power.LidActionAc == 3) || (power.LidActionDc == 3);
            return new WakeReadinessView { MainSummary = lead, Details = details.ToString(), ShowPowerSettings = openSettings,
                CheckedAt = power.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
        }

        private static string FormatRtc(int? state, bool? modernStandby)
        {
            if (!state.HasValue) return "未能确认";
            switch (state.Value)
            {
                case 1: return modernStandby == true ? "S0 低功耗待机（现代待机）" : "S0 工作态";
                case 2: return "S1";
                case 3: return "S2";
                case 4: return "S3";
                case 5: return "S4 休眠";
                case 6: return "S5 关机（本程序不支持关机启动）";
            }
            return "未能确认";
        }

        private static bool IsEnabledTimer(int? value, bool? visible)
        {
            return visible == true && value.HasValue && value.Value == 1;
        }
    }

    internal static class WakeReadinessChecks
    {
        private static WakeSystemSnapshot Power(CurrentPowerSource source, int? acTimer, int? dcTimer)
        {
            return new WakeSystemSnapshot
            {
                CheckedAt = new DateTimeOffset(2026, 10, 8, 11, 30, 0, TimeSpan.FromHours(8)),
                PowerSource = source,
                WakeTimerVisible = true,
                ActiveSchemeId = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
                ActiveSchemeName = "fixture balanced",
                WakeTimerAc = acTimer,
                WakeTimerDc = dcTimer,
                LidPresent = true,
                LidActionAc = 1,
                LidActionDc = 1,
                LidActionVisible = true,
                SupportsSleep = true,
                SupportsHibernate = true,
                SupportsModernStandby = false,
                SleepStates = "S3 睡眠、S4 休眠",
                RtcWakeState = 4
            };
        }

        private static WindowsTaskStatus Task(string enabled, string wake, string logon)
        {
            return new WindowsTaskStatus
            {
                Exists = true,
                DefinitionXml = "<Task><Principals><Principal><LogonType>" + (logon ?? "")
                    + "</LogonType></Principal></Principals><Settings>"
                    + (enabled == null ? "" : "<Enabled>" + enabled + "</Enabled>")
                    + (wake == null ? "" : "<WakeToRun>" + wake + "</WakeToRun>")
                    + "</Settings></Task>"
            };
        }

        internal static WakeSystemSnapshot PowerForLayout()
        {
            return Power(CurrentPowerSource.PluggedIn, 1, 0);
        }

        internal static WindowsTaskStatus TaskForLayout()
        {
            WindowsTaskStatus task = Task("true", "true", "InteractiveToken");
            task.NextRunTime = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Local);
            task.LastRunTime = new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Local);
            return task;
        }

        public static bool Verify(out string evidence)
        {
            List<string> checks = new List<string>();
            bool ok = true;
            WakeSystemSnapshot both = Power(CurrentPowerSource.PluggedIn, 1, 0);
            WindowsTaskStatus interactive = Task("true", "true", "InteractiveToken");
            WakeReadinessView ac = WakeReadinessPolicy.Evaluate(both, interactive);
            bool acBatterySplit = ac.MainSummary.Contains("插电设置允许尝试")
                && ac.Details.Contains("插电 已开启；电池 已关闭")
                && ac.Details.Contains("允许使用唤醒定时器") && ac.ShowPowerSettings;
            checks.Add("acBatterySplit=" + acBatterySplit);
            ok &= acBatterySplit;

            WakeReadinessView battery = WakeReadinessPolicy.Evaluate(Power(CurrentPowerSource.Battery, 1, 0), interactive);
            bool batteryAction = battery.MainSummary.Contains("电池唤醒定时器已关闭")
                && battery.Details.Contains("检查插电与电池") && battery.Details.Contains("合盖/睡眠唤醒尚未实测");
            checks.Add("batteryTimerAction=" + batteryAction);
            ok &= batteryAction;

            WakeReadinessView disabled = WakeReadinessPolicy.Evaluate(both, Task("false", "true", "InteractiveToken"));
            WakeReadinessView wakeDisabled = WakeReadinessPolicy.Evaluate(both, Task("true", "false", "InteractiveToken"));
            bool taskStates = disabled.MainSummary.Contains("计划已暂停") && wakeDisabled.MainSummary.Contains("未开启 WakeToRun");
            checks.Add("taskEnabledAndWakeToRun=" + taskStates);
            ok &= taskStates;

            bool interactiveRule = ac.Details.Contains("锁屏仍登录时可运行") && ac.Details.Contains("注销/重启后未登录时不能运行");
            WakeReadinessView s4u = WakeReadinessPolicy.Evaluate(both, Task("true", "true", "S4U"));
            bool s4uRule = s4u.Details.Contains("可无交互登录运行") && s4u.Details.Contains("网络/加密资源受限");
            checks.Add("logonModes=" + (interactiveRule && s4uRule));
            ok &= interactiveRule && s4uRule;

            WindowsTaskStatus missing = new WindowsTaskStatus { Exists = false, Error = "ERROR: The system cannot find the file specified." };
            WakeReadinessView missingView = WakeReadinessPolicy.Evaluate(both, missing);
            WindowsTaskStatus denied = new WindowsTaskStatus { Exists = false, Error = "Access is denied." };
            WakeReadinessView deniedView = WakeReadinessPolicy.Evaluate(both, denied);
            bool missingDistinct = missingView.MainSummary.Contains("尚未安装计划")
                && !missingView.MainSummary.Contains("未能确认") && deniedView.MainSummary.Contains("状态未能确认")
                && !WakeReadinessPolicy.InspectTask(denied).Missing;
            checks.Add("missingDistinctFromQueryError=" + missingDistinct);
            ok &= missingDistinct;

            WakeReadinessView malformed = WakeReadinessPolicy.Evaluate(both, new WindowsTaskStatus { Exists = true, DefinitionXml = "<Task" });
            WakeReadinessView hiddenWake = WakeReadinessPolicy.Evaluate(both, Task("true", null, "InteractiveToken"));
            WakeReadinessView unknownLogon = WakeReadinessPolicy.Evaluate(both, Task("true", "true", "UnknownLogon"));
            WakeSystemSnapshot hiddenTimerPower = Power(CurrentPowerSource.PluggedIn, 1, 1);
            hiddenTimerPower.WakeTimerVisible = false;
            WakeReadinessView hiddenTimer = WakeReadinessPolicy.Evaluate(hiddenTimerPower, interactive);
            bool unknownNotPassing = malformed.MainSummary.Contains("未能确认")
                && hiddenWake.MainSummary.Contains("任务是否允许唤醒未能确认")
                && unknownLogon.MainSummary.Contains("任务登录模式不受识别")
                && hiddenTimer.MainSummary.Contains("唤醒定时器选项在此电源方案中隐藏")
                && hiddenTimer.Details.Contains("此选项在高级电源设置中隐藏")
                && !malformed.MainSummary.Contains("允许尝试") && !hiddenWake.MainSummary.Contains("允许尝试")
                && !unknownLogon.MainSummary.Contains("允许尝试") && !hiddenTimer.MainSummary.Contains("允许尝试");
            checks.Add("malformedAndHiddenFieldsUnknown=" + unknownNotPassing);
            ok &= unknownNotPassing;

            WakeSystemSnapshot unsupported = Power(CurrentPowerSource.PluggedIn, 1, 1);
            unsupported.SupportsSleep = null;
            unsupported.SupportsHibernate = null;
            unsupported.SleepStates = "未能确认";
            unsupported.CapabilityError = "fixture unavailable";
            WakeReadinessView unsupportedView = WakeReadinessPolicy.Evaluate(unsupported, interactive);
            bool capabilityUnknown = unsupportedView.MainSummary.Contains("未确认可用")
                && !unsupportedView.MainSummary.Contains("允许尝试");
            checks.Add("unsupportedCapabilityUnknown=" + capabilityUnknown);
            ok &= capabilityUnknown;

            WakeSystemSnapshot modern = Power(CurrentPowerSource.PluggedIn, 1, 1);
            modern.SupportsModernStandby = true;
            modern.SupportsSleep = true;
            modern.SleepStates = "S0 低功耗待机（现代待机）";
            WakeReadinessView modernView = WakeReadinessPolicy.Evaluate(modern, Task("true", "true", "Password"));
            bool modernStandby = modernView.Details.Contains("现代待机") && modernView.Details.Contains("密码模式");
            checks.Add("modernStandbyAndPassword=" + modernStandby);
            ok &= modernStandby;

            WakeSystemSnapshot unsupportedLid = Power(CurrentPowerSource.PluggedIn, 1, 1);
            unsupportedLid.LidPresent = false;
            unsupportedLid.LidActionAc = null;
            unsupportedLid.LidActionDc = null;
            WakeReadinessView noLid = WakeReadinessPolicy.Evaluate(unsupportedLid, interactive);
            WakeSystemSnapshot unknownLid = Power(CurrentPowerSource.PluggedIn, 1, 1);
            unknownLid.LidActionAc = null;
            unknownLid.LidActionDc = null;
            WakeReadinessView hiddenLid = WakeReadinessPolicy.Evaluate(unknownLid, interactive);
            WakeSystemSnapshot hiddenLidPower = Power(CurrentPowerSource.PluggedIn, 1, 1);
            hiddenLidPower.LidActionVisible = false;
            WakeReadinessView hiddenLidValue = WakeReadinessPolicy.Evaluate(hiddenLidPower, interactive);
            bool lidUnknowns = noLid.Details.Contains("不适用") && hiddenLid.Details.Contains("合盖动作：插电 未能确认")
                && hiddenLidValue.Details.Contains("合盖动作选项在高级电源设置中隐藏");
            checks.Add("lidUnsupportedAndHidden=" + lidUnknowns);
            ok &= lidUnknowns;

            WakeSystemSnapshot restricted = Power(CurrentPowerSource.Battery, 1, 2);
            WakeReadinessView restrictedView = WakeReadinessPolicy.Evaluate(restricted, interactive);
            bool restrictedTimer = restrictedView.MainSummary.Contains("只允许重要唤醒") && restrictedView.ShowPowerSettings;
            checks.Add("restrictedTimer=" + restrictedTimer);
            ok &= restrictedTimer;

            WakeSystemSnapshot rtc = Power(CurrentPowerSource.PluggedIn, 1, 1);
            rtc.RtcWakeState = 6;
            WakeReadinessView rtcView = WakeReadinessPolicy.Evaluate(rtc, interactive);
            bool rtcDeepestState = !rtcView.MainSummary.Contains("未能确认")
                && rtcView.Details.Contains("S5 关机（本程序不支持关机启动）");
            checks.Add("rtcDeepestState=" + rtcDeepestState);
            ok &= rtcDeepestState;

            evidence = string.Join("; ", checks);
            return ok;
        }
    }

    internal static class WindowsCredentialPrompt
    {
        private const uint ErrorCancelled = 1223;
        private const uint CredUiFlagsGenericCredentials = 0x00040000;
        private const uint CredUiFlagsAlwaysShowUi = 0x00000080;
        private const uint CredUiFlagsDoNotPersist = 0x00000002;
        private const uint CredUiFlagsExcludeCertificates = 0x00000008;
        private const int UserNameCapacity = 512;
        private const int PasswordCapacity = 512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CredUiInfo
        {
            public int CbSize;
            public IntPtr Parent;
            public string MessageText;
            public string CaptionText;
            public IntPtr Banner;
        }

        [DllImport("credui.dll", CharSet = CharSet.Unicode)]
        private static extern uint CredUIPromptForCredentials(
            ref CredUiInfo uiInfo,
            string targetName,
            IntPtr reserved,
            uint authError,
            StringBuilder userName,
            int userNameMaxChars,
            StringBuilder password,
            int passwordMaxChars,
            ref bool save,
            uint flags);

        public static bool TryGetCurrentUserPassword(out string userName, out string password, out string error)
        {
            userName = string.Empty;
            password = string.Empty;
            error = string.Empty;

            string currentUser = WindowsIdentity.GetCurrent().Name;
            StringBuilder userBuffer = new StringBuilder(currentUser, UserNameCapacity);
            StringBuilder passwordBuffer = new StringBuilder(PasswordCapacity);
            bool save = false;
            CredUiInfo info = new CredUiInfo
            {
                CbSize = Marshal.SizeOf(typeof(CredUiInfo)),
                MessageText = BuildPromptMessage(currentUser),
                CaptionText = "Codex 额度唤醒器 - 后台运行凭据"
            };

            try
            {
                uint result = CredUIPromptForCredentials(
                    ref info,
                    TaskXml.TaskName,
                    IntPtr.Zero,
                    0,
                    userBuffer,
                    UserNameCapacity,
                    passwordBuffer,
                    PasswordCapacity,
                    ref save,
                    CredUiFlagsGenericCredentials
                        | CredUiFlagsAlwaysShowUi
                        | CredUiFlagsDoNotPersist
                        | CredUiFlagsExcludeCertificates);
                if (result == ErrorCancelled)
                {
                    error = "已取消输入 Windows 密码，原有计划未修改。";
                    return false;
                }
                if (result != 0)
                {
                    error = "Windows 凭据验证窗口返回错误（代码 " + result + "），原有计划未修改。";
                    return false;
                }

                userName = userBuffer.ToString().Trim();
                password = passwordBuffer.ToString();
                if (string.IsNullOrEmpty(password))
                {
                    error = "未输入 Windows 帐户密码，原有计划未修改。";
                    return false;
                }
                if (!IsCurrentUser(userName, currentUser))
                {
                    error = "请输入当前登录的 Windows 帐户密码，原有计划未修改。";
                    return false;
                }
                return true;
            }
            catch (DllNotFoundException)
            {
                error = "Windows 凭据组件不可用，原有计划未修改。";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                error = "Windows 凭据组件版本不受支持，原有计划未修改。";
                return false;
            }
            finally
            {
                for (int index = 0; index < passwordBuffer.Length; index++)
                {
                    passwordBuffer[index] = '\0';
                }
            }
        }

        internal static string BuildPromptMessage(string currentUser)
        {
            return "当前账户：" + (currentUser ?? string.Empty)
                + "\r\n请输入该账户的 Windows 密码；不能使用 PIN、指纹或人脸。密码只用于本次注册，由 Windows 任务计划程序加密保管。";
        }

        private static bool IsCurrentUser(string entered, string current)
        {
            if (string.Equals(entered, current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return string.Equals(UserPart(entered), UserPart(current), StringComparison.OrdinalIgnoreCase)
                && string.Equals(UserPart(entered), Environment.UserName, StringComparison.OrdinalIgnoreCase);
        }

        private static string UserPart(string value)
        {
            string result = (value ?? string.Empty).Trim();
            int slash = result.LastIndexOf('\\');
            if (slash >= 0 && slash + 1 < result.Length)
            {
                result = result.Substring(slash + 1);
            }
            int at = result.IndexOf('@');
            if (at > 0)
            {
                result = result.Substring(0, at);
            }
            return result;
        }
    }

    internal sealed class TaskRegistrationDiagnosis
    {
        public string ExceptionType { get; set; }
        public string ErrorCode { get; set; }
        public string UserMessage { get; set; }
        public string LogMessage { get; set; }
    }

    internal static class TaskRegistrationErrors
    {
        public static TaskRegistrationDiagnosis Diagnose(Exception exception, string stage)
        {
            Exception codeSource;
            int? code = FindCode(exception, 0, out codeSource);
            string exceptionType = codeSource == null
                ? (exception == null ? "UnknownException" : exception.GetType().Name)
                : codeSource.GetType().Name;
            string codeText = code.HasValue ? FormatCode(code.Value) : "UNKNOWN";
            uint unsignedCode = code.HasValue ? unchecked((uint)code.Value) : 0U;
            string userMessage;

            if (unsignedCode == 0x8007052EU)
            {
                userMessage = "账户密码不正确，或输入了 Windows Hello PIN。错误码：" + codeText + "（可复制）。原计划未修改。下一步：使用当前 Windows 帐户的完整密码重试；普通睡眠唤醒不需要开启“重启或注销后仍运行”，也可以取消勾选后继续使用。";
            }
            else if (unsignedCode == 0x80070569U)
            {
                userMessage = "Windows 未授予该账户“作为批处理作业登录”的权限。错误码：" + codeText + "（可复制）。软件未修改本地安全策略，原计划未修改。下一步：请联系管理员按组织策略授予该权限，或取消勾选“重启或注销后仍运行”继续使用。";
            }
            else if (unsignedCode == 0x80070005U)
            {
                userMessage = "Windows 拒绝访问。错误码：" + codeText + "（可复制）。原计划未修改。下一步：请右键以管理员身份启动程序后重试；如果仍失败，请检查任务计划程序权限。";
            }
            else
            {
                userMessage = "Windows 未能更新“重启或注销后仍运行”计划。错误码：" + codeText + "（可复制）。原计划未修改。下一步：保持该选项未勾选可继续使用普通睡眠唤醒；如需后台运行，请记录错误码并查看日志后再重试。";
            }

            return new TaskRegistrationDiagnosis
            {
                ExceptionType = exceptionType,
                ErrorCode = codeText,
                UserMessage = userMessage,
                LogMessage = "阶段=" + SafeStage(stage) + ";异常类型=" + SafeType(exceptionType) + ";错误码=" + codeText
            };
        }

        private static int? FindCode(Exception exception, int depth, out Exception source)
        {
            source = null;
            if (exception == null || depth > 32)
            {
                return null;
            }

            AggregateException aggregate = exception as AggregateException;
            if (aggregate != null)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    Exception innerSource;
                    int? innerCode = FindCode(inner, depth + 1, out innerSource);
                    if (innerCode.HasValue)
                    {
                        source = innerSource;
                        return innerCode;
                    }
                }
            }
            else if (exception.InnerException != null)
            {
                Exception innerSource;
                int? innerCode = FindCode(exception.InnerException, depth + 1, out innerSource);
                if (innerCode.HasValue)
                {
                    source = innerSource;
                    return innerCode;
                }
            }

            COMException com = exception as COMException;
            if (com != null && com.ErrorCode != 0)
            {
                source = exception;
                return NormalizeCode(com.ErrorCode);
            }

            Win32Exception win32 = exception as Win32Exception;
            if (win32 != null && win32.NativeErrorCode != 0)
            {
                source = exception;
                return NormalizeCode(win32.NativeErrorCode);
            }

            if (exception.HResult != 0)
            {
                source = exception;
                return NormalizeCode(exception.HResult);
            }
            return null;
        }

        private static int NormalizeCode(int code)
        {
            uint unsignedCode = unchecked((uint)code);
            if (unsignedCode <= 0xFFFFU)
            {
                unsignedCode = 0x80070000U | unsignedCode;
            }
            return unchecked((int)unsignedCode);
        }

        private static string FormatCode(int code)
        {
            return "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture);
        }

        private static string SafeStage(string stage)
        {
            return string.IsNullOrWhiteSpace(stage) ? "unknown" : stage.Replace(";", "_").Replace("\r", "_").Replace("\n", "_");
        }

        private static string SafeType(string type)
        {
            return string.IsNullOrWhiteSpace(type) ? "UnknownException" : type.Replace(";", "_").Replace("\r", "_").Replace("\n", "_");
        }
    }

    internal static class WindowsTaskService
    {
        public static OperationResult InstallOrUpdate(AppConfig config)
        {
            string password = string.Empty;
            try
            {
                config = AppConfig.Normalize(config);
                bool existed = Exists();
                bool hasSequence = config.SequenceCount > 0;
                if (hasSequence)
                {
                    DateTime sequenceStart;
                    if (!AppConfig.TryParseSequenceStart(config.SequenceStart, out sequenceStart))
                    {
                        return Fail("请设置有效的首次执行日期和时间。");
                    }
                    if (config.SequenceIntervalMinutes < 1 || config.SequenceIntervalMinutes > 10080)
                    {
                        return Fail("执行间隔必须介于 1 分钟到 7 天之间。");
                    }
                    if (sequenceStart <= DateTime.Now)
                    {
                        return Fail("首次执行时间必须晚于当前时间，未来日期没有上限。");
                    }
                }
                bool hasExtraWake = AppConfig.NormalizeDateTimes(config.ExtraWakeTimes).Count > 0;
                if (!hasSequence && !hasExtraWake)
                {
                    return Fail("请至少设置 1 次序列执行或添加一个额外唤醒时间。");
                }

                string accountName = WindowsIdentity.GetCurrent().Name;
                if (config.RunWithoutLogon)
                {
                    string credentialError;
                    if (!WindowsCredentialPrompt.TryGetCurrentUserPassword(out accountName, out password, out credentialError))
                    {
                        return Fail(credentialError);
                    }
                }

                InstallExecutable();
                string sid = WindowsIdentity.GetCurrent().User.Value;
                string xml = TaskXml.Build(config, AppPaths.InstalledExePath, sid);
                if (config.RunWithoutLogon)
                {
                    return RegisterWithPassword(xml, accountName, password);
                }

                string tempPath = Path.Combine(Path.GetTempPath(), "CodexQuotaWaker-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(tempPath, xml, Encoding.Unicode);
                    ProcessResult result = ProcessRunner.Run(
                        "schtasks.exe",
                        "/Create /TN " + ProcessRunner.Quote(TaskXml.TaskName) + " /XML " + ProcessRunner.Quote(tempPath) + " /F",
                        AppPaths.BaseDirectory,
                        30000);
                    if (result.ExitCode != 0)
                    {
                        return Fail(ExplainTaskError(result.StandardError, result.StandardOutput));
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }
                Logger.Write("INFO", "Windows 计划任务已创建或更新：" + TaskXml.TaskName);
                return Ok(existed
                    ? "已更新并启用同一条 Windows 计划任务，不会新增重复计划。"
                    : "已创建并启用 Windows 计划任务。以后修改后再次保存会更新这一条，不会新增重复计划。");
            }
            catch (Exception ex)
            {
                TaskRegistrationDiagnosis diagnosis = TaskRegistrationErrors.Diagnose(ex, "InstallOrUpdate");
                Logger.Write("ERROR", diagnosis.LogMessage);
                return config != null && config.RunWithoutLogon
                    ? Fail(diagnosis.UserMessage + "\r\n日志入口：打开“打开日志”查看该安全阶段记录。")
                    : Fail("创建计划任务失败，原有计划未修改。请检查 Windows 任务计划程序权限后重试。");
            }
            finally
            {
                password = string.Empty;
            }
        }

        private static OperationResult RegisterWithPassword(string xml, string accountName, string password)
        {
            object service = null;
            object rootFolder = null;
            object taskDefinition = null;
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null)
                {
                    return Fail("Windows 任务计划程序组件不可用，原有计划未修改。");
                }

                service = Activator.CreateInstance(serviceType);
                InvokeCom(service, "Connect");
                rootFolder = InvokeCom(service, "GetFolder", "\\");
                taskDefinition = InvokeCom(service, "NewTask", 0);
                SetComProperty(taskDefinition, "XmlText", xml);

                // TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_PASSWORD = 1.
                // The password is passed only to this in-memory COM call; it is
                // intentionally absent from XML, command lines, and log output.
                InvokeCom(
                    rootFolder,
                    "RegisterTaskDefinition",
                    TaskXml.TaskName,
                    taskDefinition,
                    6,
                    accountName,
                    password,
                    1,
                    null);
                return Ok("已更新并启用同一条 Windows 计划任务；无需登录也可运行。Windows 会加密保管凭据，不会新增重复计划。");
            }
            catch (Exception ex)
            {
                TaskRegistrationDiagnosis diagnosis = TaskRegistrationErrors.Diagnose(ex, "RegisterTaskDefinition");
                Logger.Write("ERROR", diagnosis.LogMessage);
                return Fail(diagnosis.UserMessage + "\r\n日志入口：打开“打开日志”查看该安全阶段记录。");
            }
            finally
            {
                ReleaseComObject(taskDefinition);
                ReleaseComObject(rootFolder);
                ReleaseComObject(service);
            }
        }

        private static object InvokeCom(object target, string member, params object[] arguments)
        {
            return target.GetType().InvokeMember(
                member,
                BindingFlags.InvokeMethod,
                null,
                target,
                arguments,
                CultureInfo.InvariantCulture);
        }

        private static void SetComProperty(object target, string member, object value)
        {
            target.GetType().InvokeMember(
                member,
                BindingFlags.SetProperty,
                null,
                target,
                new[] { value },
                CultureInfo.InvariantCulture);
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try
                {
                    Marshal.FinalReleaseComObject(value);
                }
                catch
                {
                }
            }
        }

        public static OperationResult Pause()
        {
            if (!Exists())
            {
                return Ok("没有已安装的计划任务。");
            }
            ProcessResult result = ProcessRunner.Run(
                "schtasks.exe",
                "/Change /TN " + ProcessRunner.Quote(TaskXml.TaskName) + " /Disable",
                AppPaths.BaseDirectory,
                15000);
            return result.ExitCode == 0
                ? Ok("计划已暂停。")
                : Fail(ExplainTaskError(result.StandardError, result.StandardOutput));
        }

        public static OperationResult Delete()
        {
            if (!Exists())
            {
                return Ok("没有已安装的计划任务。");
            }
            ProcessResult result = ProcessRunner.Run(
                "schtasks.exe",
                "/Delete /TN " + ProcessRunner.Quote(TaskXml.TaskName) + " /F",
                AppPaths.BaseDirectory,
                15000);
            return result.ExitCode == 0
                ? Ok("计划任务已删除；配置和历史日志仍保留。")
                : Fail(ExplainTaskError(result.StandardError, result.StandardOutput));
        }

        public static bool Exists()
        {
            ProcessResult result = ProcessRunner.Run(
                "schtasks.exe",
                "/Query /TN " + ProcessRunner.Quote(TaskXml.TaskName),
                AppPaths.BaseDirectory,
                10000);
            return result.ExitCode == 0;
        }

        public static WindowsTaskStatus GetStatus()
        {
            ProcessResult result = ProcessRunner.Run(
                "schtasks.exe",
                "/Query /TN " + ProcessRunner.Quote(TaskXml.TaskName) + " /XML",
                AppPaths.BaseDirectory,
                10000);
            if (result.ExitCode != 0)
            {
                return new WindowsTaskStatus { Exists = false, Error = ExplainTaskError(result.StandardError, result.StandardOutput) };
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(result.StandardOutput);
                XmlNode settings = document.SelectSingleNode("//*[local-name()='Settings']");
                XmlNode principal = document.SelectSingleNode("//*[local-name()='Principal']");
                WindowsTaskStatus status = new WindowsTaskStatus
                {
                    Exists = true,
                    DefinitionXml = result.StandardOutput,
                    Enabled = ReadBoolean(settings, "Enabled", true),
                    WakeToRun = ReadBoolean(settings, "WakeToRun", false),
                    LogonType = ReadText(principal, "LogonType"),
                    UserId = ReadText(principal, "UserId")
                };
                try { ReadScheduledTaskTimes(status); }
                catch (Exception ex) { status.RuntimeDetailsError = "Windows 已安装计划，但无法读取实际运行时间：" + ex.GetType().Name + "。请重试刷新，或检查任务计划程序服务。"; }
                try
                {
                    string taskPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks", TaskXml.TaskName);
                    if (File.Exists(taskPath)) status.DefinitionSavedUtc = File.GetLastWriteTimeUtc(taskPath);
                }
                catch { }
                return status;
            }
            catch (Exception ex)
            {
                return new WindowsTaskStatus { Exists = true, Error = "无法读取 Windows 计划状态：" + ex.Message };
            }
        }

        private static void ReadScheduledTaskTimes(WindowsTaskStatus status)
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null) throw new InvalidOperationException("Task Scheduler is unavailable");
            object service = null;
            object folder = null;
            object task = null;
            try
            {
                service = Activator.CreateInstance(serviceType);
                serviceType.InvokeMember("Connect", BindingFlags.InvokeMethod, null, service,
                    new object[] { Type.Missing, Type.Missing, Type.Missing, Type.Missing }, CultureInfo.InvariantCulture);
                folder = serviceType.InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, service,
                    new object[] { "\\" }, CultureInfo.InvariantCulture);
                task = folder.GetType().InvokeMember("GetTask", BindingFlags.InvokeMethod, null, folder,
                    new object[] { TaskXml.TaskName }, CultureInfo.InvariantCulture);
                status.NextRunTime = ReadScheduledDate(task, "NextRunTime");
                status.LastRunTime = ReadScheduledDate(task, "LastRunTime");
            }
            finally
            {
                ReleaseCom(task);
                ReleaseCom(folder);
                ReleaseCom(service);
            }
        }

        private static DateTime? ReadScheduledDate(object task, string propertyName)
        {
            object value = task.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, task, null, CultureInfo.InvariantCulture);
            if (value == null) return null;
            DateTime parsed;
            if (value is DateTime) parsed = (DateTime)value;
            else if (!DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)) return null;
            return parsed.Year <= 1601 ? (DateTime?)null : DateTime.SpecifyKind(parsed, DateTimeKind.Local);
        }

        private static void ReleaseCom(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }

        private static bool ReadBoolean(XmlNode parent, string localName, bool defaultValue)
        {
            if (parent == null)
            {
                return defaultValue;
            }
            XmlNode node = parent.SelectSingleNode("./*[local-name()='" + localName + "']");
            bool value;
            return node != null && bool.TryParse(node.InnerText, out value) ? value : defaultValue;
        }

        private static string ReadText(XmlNode parent, string localName)
        {
            if (parent == null)
            {
                return string.Empty;
            }
            XmlNode node = parent.SelectSingleNode("./*[local-name()='" + localName + "']");
            return node == null ? string.Empty : (node.InnerText ?? string.Empty).Trim();
        }

        private static void InstallExecutable()
        {
            string current = Assembly.GetExecutingAssembly().Location;
            if (string.Equals(
                Path.GetFullPath(current),
                Path.GetFullPath(AppPaths.InstalledExePath),
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            File.Copy(current, AppPaths.InstalledExePath, true);
        }

        private static string ExplainTaskError(string stderr, string stdout)
        {
            string combined = ((stderr ?? string.Empty) + " " + (stdout ?? string.Empty)).Trim();
            if (combined.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("拒绝访问", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Windows 拒绝创建计划任务。请右键程序选择“以管理员身份运行”，再点击保存。";
            }
            return string.IsNullOrWhiteSpace(combined) ? "Windows 计划任务命令执行失败。" : combined;
        }

        private static OperationResult Ok(string message)
        {
            return new OperationResult { Success = true, Message = message };
        }

        private static OperationResult Fail(string message)
        {
            return new OperationResult { Success = false, Message = message };
        }
    }

    internal static class SelfCheck
    {
        public static int Run(string reportPath)
        {
            List<string> lines = new List<string>();
            bool success = true;
            try
            {
                lines.Add("CodexQuotaWaker self-check");
                lines.Add("Time=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                lines.Add("Framework=" + Environment.Version);
                lines.Add("OS=" + Environment.OSVersion);

                string codex = CodexLocator.Find();
                lines.Add("Codex=" + (codex ?? "NOT_FOUND"));
                if (string.IsNullOrWhiteSpace(codex))
                {
                    success = false;
                }

                string schtasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                lines.Add("TaskScheduler=" + schtasks + ";Exists=" + File.Exists(schtasks));
                if (!File.Exists(schtasks))
                {
                    success = false;
                }

                AppConfig sample = new AppConfig();
                sample.SequenceStart = "2026-09-06 02:15";
                sample.SequenceCount = 0;
                sample.ExtraWakeTimes.Add("2026-09-06 09:00");
                sample.SleepAfterScheduledWake = true;
                sample.RunWithoutLogon = true;
                string json = JsonStore.Serialize(sample);
                AppConfig roundTrip = JsonStore.Deserialize<AppConfig>(json);
                sample.TargetMode = TargetModes.FollowCurrent;
                json = JsonStore.Serialize(sample);
                roundTrip = JsonStore.Deserialize<AppConfig>(json);
                bool jsonOk = roundTrip != null
                    && roundTrip.SequenceCount == 0
                    && roundTrip.ExtraWakeTimes.Count == 1
                    && roundTrip.SleepAfterScheduledWake
                    && roundTrip.RunWithoutLogon
                    && roundTrip.TargetMode == TargetModes.FollowCurrent
                    && json.IndexOf("KeepAwakeMinutes", StringComparison.Ordinal) < 0;
                lines.Add("JsonRoundTrip=" + jsonOk);
                success = success && jsonOk;

                AppConfig blankModelConfig = new AppConfig();
                blankModelConfig.Model = " ";
                AppConfig.Normalize(blankModelConfig);
                AppConfig legacyDefaultConfig = new AppConfig();
                legacyDefaultConfig.Model = AppConfig.LegacyDefaultRequestModel;
                bool migratedLegacyDefault = AppConfig.MigrateLegacyDefaultModelToDraft(legacyDefaultConfig);
                AppConfig explicitlySelectedLegacyModel = new AppConfig();
                explicitlySelectedLegacyModel.Model = AppConfig.LegacyDefaultRequestModel;
                explicitlySelectedLegacyModel.RequestModelSelectionConfirmed = true;
                AppConfig restoredExplicitLegacyModel = JsonStore.Deserialize<AppConfig>(JsonStore.Serialize(explicitlySelectedLegacyModel));
                bool preservedExplicitLegacyModel = restoredExplicitLegacyModel != null
                    && restoredExplicitLegacyModel.RequestModelSelectionConfirmed
                    && !AppConfig.MigrateLegacyDefaultModelToDraft(restoredExplicitLegacyModel)
                    && restoredExplicitLegacyModel.Model == AppConfig.LegacyDefaultRequestModel;
                AppConfig customModelConfig = new AppConfig();
                customModelConfig.Model = "custom-model-slug";
                bool preservedCustomModel = !AppConfig.MigrateLegacyDefaultModelToDraft(customModelConfig)
                    && customModelConfig.Model == "custom-model-slug";
                bool requestModelsOk = new AppConfig().Model == AppConfig.DefaultRequestModel
                    && AppConfig.Normalize(null).Model == AppConfig.DefaultRequestModel
                    && blankModelConfig.Model == AppConfig.DefaultRequestModel
                    && migratedLegacyDefault
                    && legacyDefaultConfig.Model == AppConfig.DefaultRequestModel
                    && preservedExplicitLegacyModel
                    && preservedCustomModel
                    && string.Join("|", AppConfig.ListedRequestModels()) == "gpt-6-luna|gpt-6-sol|gpt-6.1-sol|gpt-6-astra";
                lines.Add("RequestModelDefaultsAndMigration=" + requestModelsOk);
                success = success && requestModelsOk;

                string catalogFixture = "{\"models\":["
                    + "{\"slug\":\"gpt-6.2-sol\",\"display_name\":\"GPT-6.2-Sol\",\"visibility\":\"list\",\"supported_in_api\":true,\"model_messages\":{\"persistent_instructions\":\"untrusted catalog text\"}},"
                    + "{\"slug\":\"GPT-6.2-Sol\",\"display_name\":\"Duplicate\",\"visibility\":\"list\",\"supported_in_api\":true},"
                    + "{\"slug\":\"gpt-hidden\",\"display_name\":\"Hidden\",\"visibility\":\"unlisted\",\"supported_in_api\":true},"
                    + "{\"slug\":\"gpt-disabled\",\"display_name\":\"Disabled\",\"visibility\":\"list\",\"supported_in_api\":false},"
                    + "{\"slug\":\"bad/model\",\"display_name\":\"Bad\",\"visibility\":\"list\",\"supported_in_api\":true},"
                    + "{\"slug\":\"gpt-6.1-sol\",\"display_name\":\"GPT-6.1-Sol\",\"visibility\":\"list\",\"supported_in_api\":true}] }";
                RequestModelCatalogSnapshot parsedCatalog = RequestModelCatalogService.ParseCliCatalog(catalogFixture);
                bool catalogParsingOk = parsedCatalog != null
                    && parsedCatalog.Models.Count == 2
                    && parsedCatalog.Models[0].Slug == "gpt-6.2-sol"
                    && parsedCatalog.Models[1].Slug == "gpt-6.1-sol"
                    && parsedCatalog.Models[0].DisplayName == "GPT-6.2-Sol"
                    && RequestModelCatalogService.ParseCliCatalog("{\"models\":{\"unexpected\":true}}") == null;
                lines.Add("ModelCatalogParseFilterOrderAndDedup=" + catalogParsingOk);
                success = success && catalogParsingOk;

                if (parsedCatalog != null)
                {
                    parsedCatalog.RefreshedAtUtc = "2026-10-04T00:00:00.0000000Z";
                }
                string safeCatalogCache = RequestModelCatalogService.SerializeCache(parsedCatalog);
                RequestModelCatalogSnapshot loadedCatalogCache = RequestModelCatalogService.ParseCache(safeCatalogCache);
                RequestModelCatalogSnapshot missingCatalogCache = RequestModelCatalogService.ParseCacheOrFallback(string.Empty);
                RequestModelCatalogSnapshot invalidCatalogCache = RequestModelCatalogService.ParseCacheOrFallback("not-json");
                bool cacheWhitelistOk = loadedCatalogCache != null
                    && loadedCatalogCache.Models.Count == 2
                    && safeCatalogCache.IndexOf("model_messages", StringComparison.OrdinalIgnoreCase) < 0
                    && safeCatalogCache.IndexOf("persistent_instructions", StringComparison.OrdinalIgnoreCase) < 0
                    && safeCatalogCache.IndexOf("visibility", StringComparison.OrdinalIgnoreCase) < 0
                    && safeCatalogCache.IndexOf("supported_in_api", StringComparison.OrdinalIgnoreCase) < 0
                    && safeCatalogCache.IndexOf("untrusted catalog text", StringComparison.OrdinalIgnoreCase) < 0;
                bool cacheFallbackOk = missingCatalogCache.Models.Count == AppConfig.ListedRequestModels().Length
                    && invalidCatalogCache.Models.Count == AppConfig.ListedRequestModels().Length;
                lines.Add("ModelCatalogCacheWhitelist=" + cacheWhitelistOk);
                lines.Add("ModelCatalogMissingOrBadCacheFallback=" + cacheFallbackOk);
                success = success && cacheWhitelistOk && cacheFallbackOk;

                ProcessResult timedOutCatalogProcess = new ProcessResult { TimedOut = true };
                ProcessResult failedCatalogProcess = new ProcessResult { ExitCode = 1, StandardError = "untrusted-secret-output" };
                RequestModelCatalogRefreshResult timedOutCatalog = RequestModelCatalogService.Evaluate(timedOutCatalogProcess);
                RequestModelCatalogRefreshResult failedCatalog = RequestModelCatalogService.Evaluate(failedCatalogProcess);
                bool catalogFailurePolicyOk = RequestModelCatalogService.TimeoutMilliseconds == 10000
                    && !timedOutCatalog.Success && timedOutCatalog.TimedOut
                    && !failedCatalog.Success && !failedCatalog.TimedOut;
                lines.Add("ModelCatalogTimeoutAndCommandFailure=" + catalogFailurePolicyOk);
                success = success && catalogFailurePolicyOk;

                string modelCatalogInteractionEvidence;
                bool modelCatalogInteractionOk = MainForm.VerifyRequestModelCatalogInteraction(out modelCatalogInteractionEvidence);
                lines.Add("RequestModelCatalogInteractionEvidence=" + modelCatalogInteractionEvidence);
                lines.Add("RequestModelCatalogSelectionAndSnapshot=" + modelCatalogInteractionOk);
                success = success && modelCatalogInteractionOk;

                bool autoSleepPolicyOk =
                    AutoSleepPolicy.Evaluate(true, "scheduled", true, true, false) == "ready"
                    && AutoSleepPolicy.Evaluate(false, "scheduled", true, true, false) == "disabled"
                    && AutoSleepPolicy.Evaluate(true, "manual", true, true, false) == "not-scheduled"
                    && AutoSleepPolicy.Evaluate(true, "scheduled", false, true, false) == "request-failed"
                    && AutoSleepPolicy.Evaluate(true, "scheduled", true, false, false) == "wake-unconfirmed"
                    && AutoSleepPolicy.Evaluate(true, "scheduled", true, true, true) == "user-active";
                lines.Add("AutoSleepPolicy=" + autoSleepPolicyOk);
                success = success && autoSleepPolicyOk;

                bool backgroundSleepOk = AutoSleepPolicy.CountdownSeconds == 60
                    && AutoSleepPolicy.CountdownStatus(false).IndexOf("后台运行", StringComparison.Ordinal) >= 0
                    && AutoSleepPolicy.CountdownStatus(false).IndexOf("无法取消", StringComparison.Ordinal) >= 0;
                lines.Add("BackgroundAutoSleepPolicy=" + backgroundSleepOk);
                success = success && backgroundSleepOk;

                string wakeXml = "<Event><EventData><Data Name='WakeSourceText'>Windows will execute NT TASK\\"
                    + TaskXml.TaskName
                    + "</Data></EventData></Event>";
                bool wakeParsingOk = WakeDetector.ReadEventData(wakeXml, "WakeSourceText").IndexOf(
                    TaskXml.TaskName,
                    StringComparison.OrdinalIgnoreCase) >= 0;
                lines.Add("WakeEvidenceParsing=" + wakeParsingOk);
                success = success && wakeParsingOk;

                string sid = WindowsIdentity.GetCurrent().User.Value;
                string taskXml = TaskXml.Build(sample, Assembly.GetExecutingAssembly().Location, sid);
                XmlDocument document = new XmlDocument();
                document.LoadXml(taskXml);
                bool xmlOk = document.DocumentElement != null && document.DocumentElement.LocalName == "Task";
                lines.Add("TaskXml=" + xmlOk);
                success = success && xmlOk;

                AppConfig interactiveConfig = new AppConfig();
                interactiveConfig.SequenceStart = "2026-09-06 02:15";
                interactiveConfig.SequenceCount = 1;
                string interactiveXml = TaskXml.Build(interactiveConfig, Assembly.GetExecutingAssembly().Location, sid);
                AppConfig backgroundConfig = new AppConfig();
                backgroundConfig.SequenceStart = interactiveConfig.SequenceStart;
                backgroundConfig.SequenceCount = interactiveConfig.SequenceCount;
                backgroundConfig.RunWithoutLogon = true;
                string backgroundXml = TaskXml.Build(backgroundConfig, Assembly.GetExecutingAssembly().Location, sid);
                bool taskLogonModeOk = interactiveXml.IndexOf("<LogonType>InteractiveToken</LogonType>", StringComparison.Ordinal) >= 0
                    && backgroundXml.IndexOf("<LogonType>Password</LogonType>", StringComparison.Ordinal) >= 0
                    && backgroundXml.IndexOf("<WakeToRun>true</WakeToRun>", StringComparison.Ordinal) >= 0
                    && backgroundXml.IndexOf("<StartWhenAvailable>true</StartWhenAvailable>", StringComparison.Ordinal) >= 0
                    && backgroundXml.IndexOf("self-check-password", StringComparison.Ordinal) < 0
                    && backgroundXml.IndexOf("<Password>", StringComparison.OrdinalIgnoreCase) < 0;
                lines.Add("TaskLogonModesAndNoSecret=" + taskLogonModeOk);
                success = success && taskLogonModeOk;

                Exception nestedCredentialError = new TargetInvocationException(
                    "test-wrapper",
                    new TargetInvocationException(
                        "test-wrapper-2",
                        new COMException("test-password", unchecked((int)0x8007052EU))));
                TaskRegistrationDiagnosis credentialDiagnosis = TaskRegistrationErrors.Diagnose(
                    nestedCredentialError,
                    "self-check");
                TaskRegistrationDiagnosis batchLogonDiagnosis = TaskRegistrationErrors.Diagnose(
                    new COMException("test-pin", unchecked((int)0x80070569U)),
                    "self-check");
                TaskRegistrationDiagnosis accessDiagnosis = TaskRegistrationErrors.Diagnose(
                    new Win32Exception(5, "test-secret"),
                    "self-check");
                TaskRegistrationDiagnosis unknownDiagnosis = TaskRegistrationErrors.Diagnose(
                    new COMException("test-cookie", unchecked((int)0x81234567U)),
                    "self-check");
                string promptMessage = WindowsCredentialPrompt.BuildPromptMessage("DOMAIN\\current-user");
                bool diagnosisOk = credentialDiagnosis.ErrorCode == "0x8007052E"
                    && credentialDiagnosis.UserMessage.IndexOf("PIN", StringComparison.OrdinalIgnoreCase) >= 0
                    && batchLogonDiagnosis.ErrorCode == "0x80070569"
                    && batchLogonDiagnosis.UserMessage.IndexOf("批处理作业登录", StringComparison.Ordinal) >= 0
                    && accessDiagnosis.ErrorCode == "0x80070005"
                    && accessDiagnosis.UserMessage.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0
                    && unknownDiagnosis.ErrorCode == "0x81234567"
                    && unknownDiagnosis.UserMessage.IndexOf("0x81234567", StringComparison.Ordinal) >= 0
                    && promptMessage.IndexOf("DOMAIN\\current-user", StringComparison.Ordinal) >= 0
                    && promptMessage.IndexOf("PIN", StringComparison.OrdinalIgnoreCase) >= 0
                    && promptMessage.IndexOf("指纹", StringComparison.Ordinal) >= 0
                    && promptMessage.IndexOf("人脸", StringComparison.Ordinal) >= 0
                    && credentialDiagnosis.LogMessage.IndexOf("test-password", StringComparison.OrdinalIgnoreCase) < 0
                    && credentialDiagnosis.UserMessage.IndexOf("test-password", StringComparison.OrdinalIgnoreCase) < 0
                    && batchLogonDiagnosis.LogMessage.IndexOf("test-pin", StringComparison.OrdinalIgnoreCase) < 0
                    && accessDiagnosis.LogMessage.IndexOf("test-secret", StringComparison.OrdinalIgnoreCase) < 0
                    && unknownDiagnosis.LogMessage.IndexOf("test-cookie", StringComparison.OrdinalIgnoreCase) < 0;
                lines.Add("TaskRegistrationDiagnosisAndPrompt=" + diagnosisOk);
                success = success && diagnosisOk;

                AppConfig sequence = new AppConfig();
                sequence.SequenceStart = "2026-09-06 02:15";
                sequence.SequenceCount = 3;
                sequence.SequenceIntervalMinutes = 125;
                sequence.ExtraWakeTimes.Add("2026-09-06 09:00");
                DateTime? sequenceLast = SequenceSchedule.LastRun(sequence);
                bool sequenceTimesOk = sequenceLast.HasValue
                    && sequenceLast.Value == new DateTime(2026, 9, 6, 6, 25, 0);
                lines.Add("FiniteSequenceTimes=" + sequenceTimesOk);
                success = success && sequenceTimesOk;

                string sequenceXml = TaskXml.Build(sequence, Assembly.GetExecutingAssembly().Location, sid);
                bool sequenceXmlOk = sequenceXml.IndexOf("<Interval>PT2H5M</Interval>", StringComparison.Ordinal) >= 0
                    && sequenceXml.IndexOf("<Duration>PT4H11M</Duration>", StringComparison.Ordinal) >= 0
                    && sequenceXml.IndexOf("2026-09-06T09:00:00", StringComparison.Ordinal) >= 0;
                lines.Add("CustomSequenceXml=" + sequenceXmlOk);
                success = success && sequenceXmlOk;

                List<PlannedRun> planned = SchedulePlanner.Build(sequence, new DateTime(2026, 9, 6, 0, 0, 0), 1);
                bool timelineOk = planned.Count == 4
                    && planned[0].Time == new DateTime(2026, 9, 6, 2, 15, 0)
                    && planned[3].Source == "插入唤醒";
                lines.Add("MergedTimeline=" + timelineOk);
                success = success && timelineOk;

                AppConfig duplicate = new AppConfig();
                duplicate.SequenceStart = sequence.SequenceStart;
                duplicate.SequenceCount = sequence.SequenceCount;
                duplicate.SequenceIntervalMinutes = sequence.SequenceIntervalMinutes;
                duplicate.ExtraWakeTimes.Add("2026-09-06 04:20");
                List<PlannedRun> deduped = SchedulePlanner.Build(duplicate, new DateTime(2026, 9, 6, 0, 0, 0), 1);
                string duplicateXml = TaskXml.Build(duplicate, Assembly.GetExecutingAssembly().Location, sid);
                bool dedupeOk = deduped.Count == 3
                    && deduped[1].Source.IndexOf("插入唤醒", StringComparison.Ordinal) >= 0
                    && CountOccurrences(duplicateXml, "<TimeTrigger>") == 1;
                lines.Add("DuplicateWakeDeduplication=" + dedupeOk);
                success = success && dedupeOk;

                AppConfig legacy = new AppConfig();
                legacy.FiniteSequenceEnabled = false;
                legacy.Times.Add("02:15");
                AppConfig.Normalize(legacy);
                List<PlannedRun> legacyPlan = SchedulePlanner.Build(legacy, new DateTime(2026, 9, 6, 0, 0, 0), 3);
                bool legacyOk = legacy.LegacyDailyScheduleDetected
                    && legacy.FiniteSequenceEnabled
                    && legacyPlan.Count == 0;
                lines.Add("LegacyDailyModeSafe=" + legacyOk);
                success = success && legacyOk;

                bool zeroExtraOk = SchedulePlanner.Build(sample, new DateTime(2026, 9, 1), 1).Count == 1
                    && CountOccurrences(taskXml, "<TimeTrigger>") == 1
                    && SchedulePlanner.Build(new AppConfig(), new DateTime(2026, 9, 1), 1).Count == 0;
                lines.Add("ZeroSequenceExtraWake=" + zeroExtraOk);
                success = success && zeroExtraOk;

                DateTime nextMinute = SequenceStartShortcut.NextMinute(new DateTime(2026, 9, 9, 14, 26, 37));
                DateTime midnight = SequenceStartShortcut.NextMinute(new DateTime(2026, 9, 9, 23, 59, 30));
                bool shortcutTimeOk = nextMinute == new DateTime(2026, 9, 9, 14, 27, 0)
                    && midnight == new DateTime(2026, 9, 10, 0, 0, 0);
                lines.Add("SequenceStartNextMinute=" + shortcutTimeOk);
                success = success && shortcutTimeOk;

                string inputOrderEvidence;
                bool inputOrderOk = MainForm.VerifyFlexiblePlanInputOrder(out inputOrderEvidence);
                lines.Add("FlexiblePlanInputOrderEvidence=" + inputOrderEvidence);
                lines.Add("FlexiblePlanInputOrder=" + inputOrderOk);
                success = success && inputOrderOk;

                string manualTestEvidence;
                bool manualTestValidationOk = MainForm.VerifyManualTestValidationIsolation(out manualTestEvidence);
                lines.Add("ManualTestValidationIsolationEvidence=" + manualTestEvidence);
                lines.Add("ManualTestValidationIsolation=" + manualTestValidationOk);
                success = success && manualTestValidationOk;

                string resultDialogEvidence;
                bool resultDialogOk = TestResultDialog.Verify(out resultDialogEvidence);
                lines.Add("TestResultDialogEvidence=" + resultDialogEvidence);
                lines.Add("TestResultDialog=" + resultDialogOk);
                success = success && resultDialogOk;
                string planNavigationEvidence;
                bool planNavigationOk = MainForm.VerifyResultPlanNavigation(out planNavigationEvidence);
                lines.Add("ResultPlanNavigationEvidence=" + planNavigationEvidence);
                lines.Add("ResultPlanNavigation=" + planNavigationOk);
                success = success && planNavigationOk;

                string timelineExecutionEvidence;
                bool timelineExecutionOk = TimelineExecutionChecks.Verify(out timelineExecutionEvidence);
                lines.Add("TimelineExecutionEvidence=" + timelineExecutionEvidence);
                lines.Add("TimelineExecution=" + timelineExecutionOk);
                success = success && timelineExecutionOk;
                string timelineRenderingEvidence;
                bool timelineRenderingOk = MainForm.VerifyTimelineRendering(out timelineRenderingEvidence);
                lines.Add("TimelineRenderingEvidence=" + timelineRenderingEvidence);
                lines.Add("TimelineRendering=" + timelineRenderingOk);
                success = success && timelineRenderingOk;
                string visibleVersionEvidence;
                bool visibleVersionOk = MainForm.VerifyVisibleVersion(out visibleVersionEvidence);
                lines.Add("VisibleAppVersionEvidence=" + visibleVersionEvidence);
                lines.Add("VisibleAppVersion=" + visibleVersionOk);
                success = success && visibleVersionOk;
                string recentSuccessEvidence;
                bool recentSuccessOk = RecentSuccessChecks.Verify(out recentSuccessEvidence);
                lines.Add("RecentSuccessEvidence=" + recentSuccessEvidence);
                lines.Add("RecentSuccessPersistence=" + recentSuccessOk);
                success = success && recentSuccessOk;
                string recentSuccessLayoutEvidence;
                bool recentSuccessLayoutOk = MainForm.VerifyRecentSuccessLayout(out recentSuccessLayoutEvidence);
                lines.Add("RecentSuccessLayoutEvidence=" + recentSuccessLayoutEvidence);
                lines.Add("RecentSuccessLayout=" + recentSuccessLayoutOk);
                success = success && recentSuccessLayoutOk;
                string timelineContinuityEvidence;
                bool timelineContinuityOk = TimelineContinuityChecks.Verify(out timelineContinuityEvidence);
                lines.Add("TimelineContinuityEvidence=" + timelineContinuityEvidence);
                lines.Add("TimelineContinuity=" + timelineContinuityOk);
                success = success && timelineContinuityOk;

                string wakeReadinessEvidence;
                bool wakeReadinessOk = WakeReadinessChecks.Verify(out wakeReadinessEvidence);
                lines.Add("WakeReadinessEvidence=" + wakeReadinessEvidence);
                lines.Add("WakeReadiness=" + wakeReadinessOk);
                success = success && wakeReadinessOk;
                string wakeReadinessLayoutEvidence;
                bool wakeReadinessLayoutOk = MainForm.VerifyWakeReadinessLayout(out wakeReadinessLayoutEvidence);
                lines.Add("WakeReadinessLayoutEvidence=" + wakeReadinessLayoutEvidence);
                lines.Add("WakeReadinessLayout=" + wakeReadinessLayoutOk);
                success = success && wakeReadinessLayoutOk;

                bool budgetOk = ExecutionBudget.TotalSeconds(3) == 870
                    && ExecutionBudget.TaskExecutionLimitMinutes(3) == 16;
                lines.Add("ExecutionBudget=" + budgetOk);
                success = success && budgetOk;

                DateTime fixtureNow = DateTime.Now;
                long newWindowReset = new DateTimeOffset(fixtureNow.AddMinutes(300)).ToUnixTimeSeconds();
                string recognizedJson = "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-5.6-luna\"}}\n"
                    + BuildAnalyzerFixtureJson(newWindowReset, "custom", "请求链路测试成功。");
                RunRecord recognized = AnalyzeFixture(recognizedJson, TargetModes.Official, fixtureNow);
                bool recognizedOk = recognized.Success
                    && recognized.FinalModel == "gpt-5.6-luna"
                    && recognized.Provider == "custom"
                    && recognized.Output.IndexOf("请求链路测试成功", StringComparison.Ordinal) >= 0
                    && RouteTestPresentation.LinkStatus(recognized) == "链路已联通"
                    && RouteTestPresentation.FinalModelLabel(recognized) == "最终模型：gpt-5.6-luna";
                lines.Add("RouteTestRecognizesFinalModel=" + recognizedOk);
                success = success && recognizedOk;

                DateTimeOffset expectedResetLocal = DateTimeOffset.FromUnixTimeSeconds(newWindowReset).ToLocalTime();
                DateTime expectedAdjusted = new DateTime(
                    expectedResetLocal.Year,
                    expectedResetLocal.Month,
                    expectedResetLocal.Day,
                    expectedResetLocal.Hour,
                    expectedResetLocal.Minute,
                    0,
                    DateTimeKind.Unspecified).AddMinutes(1);
                ResetScheduleAdjustment resetAdjustment = ResetSchedulePlanner.Evaluate(
                    recognized,
                    1,
                    fixtureNow);
                bool resetEvidenceOk = recognized.HasReliableResetTime
                    && recognized.ResetsAtUnix == newWindowReset
                    && resetAdjustment.Applied
                    && resetAdjustment.LocalStart == expectedAdjusted
                    && resetAdjustment.Message.IndexOf("尚未保存", StringComparison.Ordinal) >= 0;
                lines.Add("QuotaResetEvidenceAndNextMinute=" + resetEvidenceOk);
                success = success && resetEvidenceOk;

                string isoResetJson = @"{""type"":""event_msg"",""payload"":{""rate_limits"":{""limit_id"":""codex"",""primary"":{""window_minutes"":300,""resets_at"":""2026-09-20T23:08:42+08:00""}}}}
{""type"":""item.completed"",""item"":{""type"":""agent_message"",""text"":""ok""}}";
                RunRecord isoReset = AnalyzeFixture(isoResetJson, TargetModes.FollowCurrent, fixtureNow);
                long isoExpected = new DateTimeOffset(
                    2026,
                    9,
                    20,
                    23,
                    8,
                    42,
                    TimeSpan.FromHours(8)).ToUnixTimeSeconds();
                bool isoResetOk = isoReset.HasReliableResetTime && isoReset.ResetsAtUnix == isoExpected;
                lines.Add("QuotaResetTimezoneConversion=" + isoResetOk);
                success = success && isoResetOk;

                string invalidResetJson = @"{""type"":""event_msg"",""payload"":{""rate_limits"":{""limit_id"":""codex"",""primary"":{""window_minutes"":300,""resets_at"":""2026-09-20 23:08:42""}}}}
{""type"":""item.completed"",""item"":{""type"":""agent_message"",""text"":""ok""}}";
                RunRecord invalidReset = AnalyzeFixture(invalidResetJson, TargetModes.FollowCurrent, fixtureNow);
                bool invalidResetOk = !invalidReset.HasReliableResetTime
                    && invalidReset.ResetTimeDiagnostic.IndexOf("带时区", StringComparison.Ordinal) >= 0;
                lines.Add("QuotaResetMissingTimezoneSafe=" + invalidResetOk);
                success = success && invalidResetOk;

                string conflictJson = BuildAnalyzerFixtureJson(newWindowReset, "custom", "请求链路测试成功。")
                    + "\n{\"type\":\"event_msg\",\"payload\":{\"rate_limits\":{\"limit_id\":\"codex\",\"primary\":{\"window_minutes\":300,\"resets_at\":"
                    + (newWindowReset + 60).ToString(CultureInfo.InvariantCulture)
                    + "}}}}";
                RunRecord conflict = AnalyzeFixture(conflictJson, TargetModes.FollowCurrent, fixtureNow);
                ResetScheduleAdjustment conflictAdjustment = ResetSchedulePlanner.Evaluate(conflict, 1, fixtureNow);
                bool conflictOk = !conflict.HasReliableResetTime
                    && !conflictAdjustment.Applied
                    && conflictAdjustment.Message.IndexOf("冲突", StringComparison.Ordinal) >= 0;
                lines.Add("QuotaResetConflictSafe=" + conflictOk);
                success = success && conflictOk;

                ResetScheduleAdjustment zeroSequenceAdjustment = ResetSchedulePlanner.Evaluate(
                    recognized,
                    0,
                    fixtureNow);
                bool zeroSequenceResetOk = !zeroSequenceAdjustment.Applied
                    && zeroSequenceAdjustment.Message == "当前未启用执行序列，计划未调整。";
                lines.Add("QuotaResetZeroSequenceSafe=" + zeroSequenceResetOk);
                success = success && zeroSequenceResetOk;

                RunRecord pastReset = new RunRecord();
                pastReset.Success = true;
                pastReset.HasReliableResetTime = true;
                pastReset.ResetsAtUnix = new DateTimeOffset(fixtureNow.AddMinutes(-1)).ToUnixTimeSeconds();
                ResetScheduleAdjustment pastAdjustment = ResetSchedulePlanner.Evaluate(pastReset, 1, fixtureNow);
                bool pastResetOk = !pastAdjustment.Applied
                    && pastAdjustment.Message.IndexOf("已过去", StringComparison.Ordinal) >= 0;
                lines.Add("QuotaResetPastTimeSafe=" + pastResetOk);
                success = success && pastResetOk;

                string unknownJson = @"{""type"":""session_meta"",""payload"":{""model_provider"":""custom""}}
{""type"":""item.completed"",""item"":{""type"":""agent_message"",""text"":""ok""}}";
                RunRecord unknown = AnalyzeFixture(unknownJson, TargetModes.FollowCurrent, fixtureNow);
                bool unknownOk = unknown.Success
                    && string.IsNullOrWhiteSpace(unknown.FinalModel)
                    && RouteTestPresentation.LinkStatus(unknown) == "链路已联通"
                    && RouteTestPresentation.FinalModelLabel(unknown) == "最终模型：未识别"
                    && unknown.Model != RouteTestPresentation.FinalModelValue(unknown);
                lines.Add("RouteTestDoesNotFallbackToRequestModel=" + unknownOk);
                success = success && unknownOk;

                string malformedJson = "{bad json\n" + recognizedJson;
                RunRecord malformed = AnalyzeFixture(malformedJson, TargetModes.Official, fixtureNow);
                bool malformedOk = malformed.Success
                    && malformed.TargetSatisfied
                    && !malformed.OfficialQuotaConfirmed
                    && malformed.QuotaStatus == "unconfirmed";
                lines.Add("RouteTestIgnoresQuotaFields=" + malformedOk);
                success = success && malformedOk;

                DateTime logTime = new DateTime(2026, 9, 18, 0, 40, 4);
                string ccSwitchLog =
                    "[2026-09-18][00:40:04][INFO][cc_switch_lib::proxy::forwarder] [Codex] >>> 请求目标: https://api.deepseek.com/v1/responses (model=deepseek-flash)";
                CcSwitchLogEvidence upstreamEvidence = CcSwitchLogProbe.ParseEvidence(
                    ccSwitchLog,
                    "deepseek-flash",
                    logTime.AddMinutes(-1),
                    logTime.AddMinutes(1));
                bool upstreamEvidenceOk = upstreamEvidence.Matched
                    && upstreamEvidence.MatchedModel
                    && upstreamEvidence.ProviderName == "DeepSeek"
                    && upstreamEvidence.Model == "deepseek-flash"
                    && !upstreamEvidence.IsOfficialTarget;
                lines.Add("CcSwitchUpstreamEvidence=" + upstreamEvidenceOk);
                success = success && upstreamEvidenceOk;

                string errorMessage = "{\"error\":{\"provider\":\"DeepSeek\",\"model\":\"gpt-5.6-terra\"}}";
                string errorJson = JsonStore.Serialize(new Dictionary<string, object>
                {
                    { "type", "error" },
                    { "message", errorMessage }
                });
                RunRecord remoteError = AnalyzeFixture(errorJson, TargetModes.Official, fixtureNow);
                bool remoteErrorOk = remoteError.RemoteError.IndexOf("DeepSeek", StringComparison.Ordinal) >= 0
                    && remoteError.FinalModel == "gpt-5.6-terra"
                    && !string.IsNullOrWhiteSpace(remoteError.Provider);
                lines.Add("CcSwitchRemoteError=" + remoteErrorOk);
                success = success && remoteErrorOk;

                RouteSnapshot detectedRoute = CodexRouteDetector.Detect(TargetModes.Official);
                bool routeDetectionOk = detectedRoute != null
                    && !string.IsNullOrWhiteSpace(detectedRoute.DisplayName)
                    && !string.IsNullOrWhiteSpace(detectedRoute.Summary);
                lines.Add("RouteDetection=" + routeDetectionOk);
                success = success && routeDetectionOk;
                string presentationGeometry;
                bool presentationOk = MainForm.VerifyPresentation(out presentationGeometry);
                lines.Add("PresentationGeometryEvidence=" + presentationGeometry);
                lines.Add("PresentationGeometryRegression=" + presentationOk);
                lines.Add("ModernUiConstructionAndDetails=" + presentationOk);
                success = success && presentationOk;
            }
            catch (Exception ex)
            {
                success = false;
                lines.Add("Exception=" + ex);
            }

            lines.Add("Result=" + (success ? "PASS" : "FAIL"));
            string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(false));
            return success ? 0 : 1;
        }

        private static RunRecord AnalyzeFixture(string stdout, string targetMode, DateTime finishedAt)
        {
            AppConfig config = new AppConfig();
            config.TargetMode = targetMode;
            RouteSnapshot route = new RouteSnapshot();
            route.RouteType = "fixture";
            route.DisplayName = "测试链路";
            route.Provider = "custom";
            route.Summary = "测试链路；provider=custom";
            ProcessResult process = new ProcessResult();
            process.ExitCode = 0;
            process.StandardOutput = stdout;
            RunRecord record = new RunRecord();
            record.Success = true;
            CodexRunAnalyzer.Apply(process, config, route, record, finishedAt);
            return record;
        }

        private static string BuildAnalyzerFixtureJson(long resetAtUnix, string provider, string finalText)
        {
            string safeText = (finalText ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"type\":\"session_meta\",\"payload\":{\"id\":\"01a0a114-6588-7d03-a79c-7182cab318e0\",\"model_provider\":\"" + provider + "\"}}\n"
                + "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\",\"primary\":{\"used_percent\":0.0,\"window_minutes\":300,\"resets_at\":" + resetAtUnix.ToString(CultureInfo.InvariantCulture) + "},\"plan_type\":\"plus\"}}}\n"
                + "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"" + safeText + "\"}}";
        }

        private static int CountOccurrences(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
            {
                return 0;
            }
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }
    }

    internal sealed class RoundedCardPanel : Panel
    {
        public RoundedCardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 2 || Height < 2) return;
            int radius = Math.Max(8, 14 * Math.Max(96, DeviceDpi) / 96);
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius))
                Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = Math.Max(8, 14 * Math.Max(96, DeviceDpi) / 96);
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            using (Pen border = new Pen(Color.FromArgb(238, 235, 243)))
                e.Graphics.DrawPath(border, path);
        }

        private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Panel viewport;
        private readonly TableLayoutPanel pageRoot;
        private readonly TableLayoutPanel mainGrid;
        private readonly Panel heroCard;
        private readonly Label brandVersionLabel;
        private readonly Label brandSubtitleLabel;
        private readonly SplitContainer resultsSplit;
        private bool adjustingResultsSplit;
        private readonly FlowLayoutPanel leftStack;
        private readonly FlowLayoutPanel rightStack;
        private readonly Label nextRunLabel;
        private readonly Label nextRunDateLabel;
        private readonly Label heroHintLabel;
        private readonly Label installedSummaryLabel;
        private readonly Label wakeReadinessLabel;
        private readonly FlowLayoutPanel wakeReadinessActions;
        private readonly Button wakeRefreshButton;
        private readonly Button openPowerSettingsButton;
        private readonly Button installedDetailsButton;
        private readonly Button routeDetailsButton;
        private readonly FlowLayoutPanel advancedBody;
        private readonly Button advancedToggleButton;
        private readonly Panel advancedCard;
        private readonly Panel routeCard;
        private readonly Panel installedCard;
        private readonly Panel installedBody;
        private readonly FlowLayoutPanel installedContent;
        private DateTime? installedNextRunTime;
        private bool installedTaskExists;
        private bool installedTaskEnabled;
        private List<TimelineExecution> timelineHistory = new List<TimelineExecution>();
        private DateTime timelineSavedUtc;
        private readonly System.Windows.Forms.Timer timelineRefreshTimer = new System.Windows.Forms.Timer();
        private bool installedDetailsExpanded;
        private bool routeDetailsExpanded;
        private bool advancedExpanded;
        private bool hasUnsavedChanges;
        private bool legacyDefaultModelMigrationPending;
        private readonly Label resultSummaryLabel;
        private readonly Button resultDetailsButton;
        private readonly Label statusLabel;
        private readonly Label planDetailLabel;
        private readonly Label routeStatusLabel;
        private readonly Label routeModelLabel;
        private readonly Label routeDetailLabel;
        private readonly Label modelCatalogFeedbackLabel;
        private readonly Button routeRefreshButton;
        private readonly Button routeTestButton;
        private readonly Panel sequenceSchedulePanel;
        private readonly DateTimePicker sequenceDatePicker;
        private readonly DateTimePicker sequenceTimePicker;
        private readonly Button sequenceNowButton;
        private readonly TextBox sequenceCountBox;
        private NumericUpDown sequenceIntervalHoursBox;
        private NumericUpDown sequenceIntervalMinutesBox;
        private readonly Label sequenceSummaryLabel;
        private readonly DateTimePicker extraWakeDatePicker;
        private readonly DateTimePicker extraWakeTimePicker;
        private readonly Button addExtraWakeButton;
        private readonly Button removeExtraWakeButton;
        private readonly ListBox extraWakeList;
        private readonly ListView timelineList;
        private readonly Label timelineSummaryLabel;
        private ComboBox modelBox;
        private RequestModelCatalogSnapshot modelCatalogSnapshot;
        private bool updatingModelCatalogChoices;
        private readonly TextBox promptBox;
        private NumericUpDown retryBox;
        private readonly CheckBox runWithoutLogonCheck;
        private readonly CheckBox autoSleepCheck;
        private readonly TextBox resultBox;
        private readonly Button saveButton;
        private readonly Button pauseButton;
        private readonly Button deleteTaskButton;
        private readonly Button openLogButton;
        private bool resultDetailsExpanded;
        private AppConfig config;

        public MainForm()
        {
            Text = "Codex 额度唤醒器  " + AppVersion.Display;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 620);
            Size = new Size(1180, 940);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(247, 247, 250);

            viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BackColor };
            Controls.Add(viewport);
            pageRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(30, 24, 30, 28),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = BackColor
            };
            pageRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int row = 0; row < 4; row++) pageRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewport.Controls.Add(pageRoot);

            FlowLayoutPanel brand = NewFlow(false);
            brand.Margin = new Padding(0, 0, 0, 20);
            Panel mark = new Panel { Size = new Size(40, 40), BackColor = Color.FromArgb(234, 229, 252), Margin = new Padding(0, 0, 12, 0) };
            Label markText = new Label { Text = "◷", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Symbol", 20F), ForeColor = Purple };
            mark.Controls.Add(markText);
            brand.Controls.Add(mark);
            FlowLayoutPanel brandText = NewFlow(true);
            brandText.Controls.Add(new Label { Text = "额度唤醒器", AutoSize = true, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold), ForeColor = Ink, Margin = new Padding(0) });
            FlowLayoutPanel brandCaption = NewFlow(false);
            brandCaption.WrapContents = false;
            brandSubtitleLabel = new Label { Text = "安排好时间，剩下的交给电脑。", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = Muted, Margin = new Padding(0, 2, 0, 0) };
            brandVersionLabel = new Label { Text = AppVersion.Display, AutoSize = true, Font = brandSubtitleLabel.Font,
                ForeColor = Muted, Margin = new Padding(10, 2, 0, 0), AccessibleName = "软件版本" };
            brandCaption.Controls.Add(brandSubtitleLabel);
            brandCaption.Controls.Add(brandVersionLabel);
            brandText.Controls.Add(brandCaption);
            brand.Controls.Add(brandText);
            pageRoot.Controls.Add(brand, 0, 0);

            Panel heroBody;
            heroCard = NewCard("", out heroBody, 172);
            heroCard.Dock = DockStyle.Top;
            TableLayoutPanel heroLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(2, 4, 2, 4) };
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            heroBody.Controls.Add(heroLayout);
            FlowLayoutPanel heroLeft = NewFlow(true);
            heroLeft.Controls.Add(new Label { Text = "下一次唤醒", AutoSize = true, ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 10F), Margin = new Padding(0, 0, 0, 2) });
            nextRunLabel = new Label { Text = "尚未安排", AutoSize = true, ForeColor = Purple, Font = new Font("Microsoft YaHei UI", 36F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 0) };
            heroLeft.Controls.Add(nextRunLabel);
            nextRunDateLabel = new Label { Text = "", AutoSize = true, ForeColor = Color.FromArgb(89, 82, 105), Font = new Font("Microsoft YaHei UI", 12F), Margin = new Padding(0, 0, 0, 4) };
            heroLeft.Controls.Add(nextRunDateLabel);
            heroHintLabel = new Label { Text = "正在读取计划…", AutoSize = true, ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F), Margin = new Padding(0, 2, 0, 0) };
            heroLeft.Controls.Add(heroHintLabel);
            heroLayout.Controls.Add(heroLeft, 0, 0);
            FlowLayoutPanel heroRight = NewFlow(true);
            heroRight.FlowDirection = FlowDirection.TopDown;
            heroRight.WrapContents = false;
            heroRight.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            statusLabel = new Label { AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = Color.FromArgb(239, 236, 245), ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Margin = new Padding(0, 4, 0, 8) };
            heroRight.Controls.Add(statusLabel);
            heroRight.Controls.Add(new Label { Text = "无需保持此窗口打开", AutoSize = true, ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F), Margin = new Padding(0) });
            heroLayout.Controls.Add(heroRight, 1, 0);
            pageRoot.Controls.Add(heroCard, 0, 1);

            mainGrid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = false, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 22, 0, 0), BackColor = BackColor };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51F));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftStack = NewFlow(true);
            rightStack = NewFlow(true);
            leftStack.AutoSize = false;
            rightStack.AutoSize = false;
            leftStack.Margin = new Padding(0, 0, 12, 0);
            rightStack.Margin = new Padding(12, 0, 0, 0);

            Panel scheduleBody;
            Panel scheduleCard = NewCard("安排唤醒", out scheduleBody, 570);
            sequenceSchedulePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            scheduleBody.Controls.Add(sequenceSchedulePanel);
            TableLayoutPanel scheduleLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 7, Padding = new Padding(0, 2, 0, 0) };
            scheduleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            scheduleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sequenceSchedulePanel.AutoScroll = true;
            sequenceSchedulePanel.Controls.Add(scheduleLayout);

            FlowLayoutPanel startBlock = NewFlow(true);
            startBlock.Margin = new Padding(0, 0, 0, 10);
            startBlock.Controls.Add(FieldCaption("首次日期与时间"));
            FlowLayoutPanel startInputs = NewFlow(false);
            startInputs.WrapContents = true;
            startInputs.Margin = new Padding(0);
            sequenceDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 146, Margin = new Padding(0, 0, 8, 0) };
            sequenceDatePicker.ValueChanged += delegate { UpdateSequenceSummary(); UpdateDraftTimeline(DateTime.Now); MarkDraftChanged(); };
            sequenceTimePicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 104, Margin = new Padding(0, 0, 8, 0) };
            sequenceTimePicker.ValueChanged += delegate { UpdateSequenceSummary(); UpdateDraftTimeline(DateTime.Now); MarkDraftChanged(); };
            sequenceNowButton = NewButton("现在", false);
            sequenceNowButton.MinimumSize = new Size(66, 36);
            sequenceNowButton.Width = 66;
            sequenceNowButton.Click += delegate { SetSequenceStartToNow(); };
            startInputs.Controls.Add(sequenceDatePicker);
            startInputs.Controls.Add(sequenceTimePicker);
            startInputs.Controls.Add(sequenceNowButton);
            startBlock.Controls.Add(startInputs);
            scheduleLayout.Controls.Add(startBlock, 0, 0);

            FlowLayoutPanel planFields = NewFlow(false);
            planFields.WrapContents = true;
            planFields.Margin = new Padding(0, 0, 0, 8);
            planFields.Controls.Add(BuildIntervalField());
            Panel countSlot;
            Panel countField = NewField("执行次数（含首次）", out countSlot, 206, 78);
            sequenceCountBox = new TextBox { Dock = DockStyle.Top, Font = new Font("Microsoft YaHei UI", 10F), Margin = new Padding(0, 6, 0, 0) };
            sequenceCountBox.TextChanged += delegate { UpdateSequenceSummary(); UpdateDraftTimeline(DateTime.Now); MarkDraftChanged(); };
            countSlot.Controls.Add(sequenceCountBox);
            planFields.Controls.Add(countField);
            scheduleLayout.Controls.Add(planFields, 0, 1);

            sequenceSummaryLabel = new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Purple, BackColor = Color.FromArgb(245, 242, 253), Padding = new Padding(11, 8, 11, 8), Margin = new Padding(0, 2, 0, 12), Font = new Font("Microsoft YaHei UI", 9F) };
            scheduleLayout.Controls.Add(sequenceSummaryLabel, 0, 2);
            scheduleLayout.Controls.Add(new Label { Text = "额外唤醒 · 可选", AutoSize = true, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = Ink, Margin = new Padding(0, 3, 0, 4) }, 0, 3);
            FlowLayoutPanel extraWakeActions = NewFlow(false);
            extraWakeActions.WrapContents = true;
            extraWakeActions.Margin = new Padding(0, 0, 0, 5);
            extraWakeDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 138, Margin = new Padding(0, 0, 7, 0) };
            extraWakeTimePicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 100, Margin = new Padding(0, 0, 7, 0) };
            addExtraWakeButton = NewButton("＋ 添加", false);
            addExtraWakeButton.Click += delegate { AddExtraWake(); };
            removeExtraWakeButton = NewButton("删除选中", false);
            removeExtraWakeButton.Click += delegate { RemoveExtraWake(); };
            extraWakeActions.Controls.Add(extraWakeDatePicker);
            extraWakeActions.Controls.Add(extraWakeTimePicker);
            extraWakeActions.Controls.Add(addExtraWakeButton);
            extraWakeActions.Controls.Add(removeExtraWakeButton);
            scheduleLayout.Controls.Add(extraWakeActions, 0, 4);
            extraWakeList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Microsoft YaHei UI", 9F), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 10) };
            scheduleLayout.Controls.Add(extraWakeList, 0, 5);

            FlowLayoutPanel planActions = NewFlow(false);
            planActions.WrapContents = true;
            planActions.Margin = new Padding(0, 5, 0, 0);
            saveButton = NewButton("保存并启用计划  ↗", true);
            saveButton.MinimumSize = new Size(190, 42);
            saveButton.Click += delegate { SaveAndEnable(); };
            pauseButton = NewButton("暂停计划", false);
            pauseButton.Click += delegate { PauseSchedule(); };
            planActions.Controls.Add(saveButton);
            planActions.Controls.Add(pauseButton);
            scheduleLayout.Controls.Add(planActions, 0, 6);

            Panel advancedCardBody;
            advancedCard = NewCard("更多设置", out advancedCardBody, 120);
            advancedToggleButton = NewButton("展开高级设置  ＋", false);
            advancedToggleButton.Dock = DockStyle.Top;
            advancedToggleButton.Click += delegate { ToggleAdvancedSettings(); };
            advancedCardBody.Controls.Add(advancedToggleButton);
            advancedBody = NewFlow(true);
            advancedBody.Visible = false;
            advancedBody.Location = new Point(0, 46);
            advancedBody.WrapContents = false;
            advancedBody.Margin = new Padding(0, 10, 0, 0);
            advancedCardBody.Controls.Add(advancedBody);
            FlowLayoutPanel options = new FlowLayoutPanel();
            options.FlowDirection = FlowDirection.TopDown;
            options.WrapContents = false;
            options.AutoSize = true;
            options.Margin = new Padding(0, 0, 0, 8);
            options.Controls.Add(BuildModelField());
            options.Controls.Add(BuildRetryField());
            runWithoutLogonCheck = new CheckBox { Text = "重启或注销后仍运行", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            runWithoutLogonCheck.CheckedChanged += delegate
            {
                runWithoutLogonCheck.AccessibleName = runWithoutLogonCheck.Checked
                    ? "重启或注销后仍运行；保存时需要输入当前 Windows 帐户密码，不能使用 PIN、指纹或人脸"
                    : "未选择；普通睡眠唤醒不受影响，Windows 更新重启后可能漏跑";
                MarkDraftChanged();
            };
            options.Controls.Add(runWithoutLogonCheck);
            Label runWithoutLogonHint = new Label { Text = "普通睡眠唤醒不需要；开启后保存时需输入 Windows 帐户密码，开机 PIN、指纹和人脸不可用。", AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Muted, Margin = new Padding(0, 3, 0, 4) };
            options.Controls.Add(runWithoutLogonHint);
            autoSleepCheck = new CheckBox { Text = "任务唤醒电脑后，成功完成时自动睡眠", AutoSize = true, Margin = new Padding(0, 7, 0, 2) };
            autoSleepCheck.CheckedChanged += delegate { MarkDraftChanged(); };
            options.Controls.Add(autoSleepCheck);
            options.Controls.Add(new Label { Text = "执行期间防睡眠与超时由程序自动管理。", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 2, 0, 6) });
            advancedBody.Controls.Add(options);
            Panel promptSlot;
            Panel promptField = NewField("触发消息", out promptSlot, 420, 84);
            promptBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font("Microsoft YaHei UI", 9F), BorderStyle = BorderStyle.FixedSingle };
            promptBox.TextChanged += delegate { MarkDraftChanged(); };
            promptSlot.Controls.Add(promptBox);
            advancedBody.Controls.Add(promptField);

            Panel routeBody;
            routeCard = NewCard("请求链路", out routeBody, 250);
            routeStatusLabel = new Label { AutoSize = true, Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), ForeColor = Purple, Margin = new Padding(0, 0, 0, 5) };
            routeModelLabel = new Label { AutoSize = true, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = Ink, Margin = new Padding(0, 0, 0, 6) };
            modelCatalogFeedbackLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F), Visible = false, Margin = new Padding(0, 0, 0, 5) };
            routeDetailLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F), Visible = false, Margin = new Padding(0, 4, 0, 4) };
            FlowLayoutPanel routeContent = NewFlow(true);
            routeContent.WrapContents = false;
            routeBody.Controls.Add(routeContent);
            routeContent.Controls.Add(routeStatusLabel);
            routeContent.Controls.Add(routeModelLabel);
            routeContent.Controls.Add(modelCatalogFeedbackLabel);
            routeContent.Controls.Add(routeDetailLabel);
            FlowLayoutPanel routeActions = NewFlow(false);
            routeActions.WrapContents = true;
            routeActions.Margin = new Padding(0, 12, 0, 0);
            routeRefreshButton = NewButton("刷新配置", false);
            routeRefreshButton.Click += delegate { RefreshRouteStatus(); };
            routeTestButton = NewButton("▷ 测试请求", true);
            routeTestButton.Click += async delegate { await TestNow(); };
            routeDetailsButton = NewButton("查看链路详情", false);
            routeDetailsButton.Click += delegate { ToggleRouteDetails(); };
            routeActions.Controls.Add(routeTestButton);
            routeActions.Controls.Add(routeRefreshButton);
            routeActions.Controls.Add(routeDetailsButton);
            routeContent.Controls.Add(routeActions);

            leftStack.Controls.Add(scheduleCard);
            leftStack.Controls.Add(advancedCard);
            leftStack.Controls.Add(routeCard);
            mainGrid.Controls.Add(leftStack, 0, 0);

            Panel timelineBody;
            Panel timelineCard = NewCard("执行时间线", out timelineBody, 330);
            timelineSummaryLabel = new Label { Dock = DockStyle.Top, Height = 28, ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F) };
            timelineList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F) };
            timelineList.Columns.Add("状态", 160);
            timelineList.Columns.Add("触发时间", 148);
            timelineList.Columns.Add("来源", 220);
            timelineList.Resize += delegate { ResizeTimelineColumns(); };
            timelineBody.Controls.Add(timelineList);
            timelineBody.Controls.Add(timelineSummaryLabel);

            Panel resultBody;
            Panel resultCard = NewCard("最近一次结果", out resultBody, 260);
            resultSummaryLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 10F), Margin = new Padding(0, 0, 0, 8) };
            resultBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 247, 250), Font = new Font("Microsoft YaHei UI", 9F), BorderStyle = BorderStyle.FixedSingle, Visible = false };
            resultDetailsButton = NewButton("查看最近尝试详情", false);
            resultDetailsButton.Dock = DockStyle.Bottom;
            resultDetailsButton.Click += delegate { ToggleResultDetails(); };
            resultBody.Controls.Add(resultBox);
            Panel resultSummaryViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            resultSummaryViewport.Controls.Add(resultSummaryLabel);
            resultSummaryLabel.SizeChanged += delegate { resultSummaryViewport.AutoScrollMinSize = new Size(0, resultSummaryLabel.Height); };
            resultBody.Controls.Add(resultSummaryViewport);
            resultBody.Controls.Add(resultDetailsButton);

            SplitContainer dataSplit = new SplitContainer
            {
                Dock = DockStyle.Top,
                Orientation = Orientation.Horizontal,
                Size = new Size(600, 630),
                Height = 610,
                SplitterWidth = 10,
                IsSplitterFixed = false,
                BackColor = Color.FromArgb(226, 222, 234),
                SplitterDistance = 340,
                Panel1MinSize = 180,
                Panel2MinSize = 190,
                Margin = new Padding(0, 0, 0, 16)
            };
            dataSplit.Panel1.BackColor = BackColor;
            dataSplit.Panel2.BackColor = BackColor;
            timelineCard.Dock = DockStyle.Fill;
            resultCard.Dock = DockStyle.Fill;
            dataSplit.Panel1.Controls.Add(timelineCard);
            dataSplit.Panel2.Controls.Add(resultCard);
            resultsSplit = dataSplit;
            resultSummaryLabel.SizeChanged += delegate { UpdateResultPanelMinimum(); };
            dataSplit.Resize += delegate { UpdateResultPanelMinimum(); };
            rightStack.Controls.Add(dataSplit);

            installedCard = NewCard("Windows 计划状态 · 唤醒条件", out installedBody, 260);
            installedBody.AutoScroll = true;
            installedContent = NewFlow(false);
            installedContent.FlowDirection = FlowDirection.TopDown;
            installedContent.WrapContents = false;
            installedContent.AutoSize = true;
            installedContent.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            installedContent.Dock = DockStyle.Top;
            installedContent.Margin = Padding.Empty;
            installedContent.Padding = Padding.Empty;
            installedSummaryLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 10F), Margin = new Padding(0, 0, 0, 8) };
            wakeReadinessLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 9.5F), Margin = new Padding(0, 5, 0, 6) };
            wakeReadinessActions = NewFlow(false);
            wakeReadinessActions.WrapContents = true;
            wakeReadinessActions.AutoSize = true;
            wakeReadinessActions.Margin = Padding.Empty;
            wakeRefreshButton = NewButton("重新检查唤醒条件", false);
            wakeRefreshButton.Click += delegate { RefreshWakeReadiness(); };
            openPowerSettingsButton = NewButton("打开电源设置", false);
            openPowerSettingsButton.Visible = false;
            openPowerSettingsButton.Click += delegate { OpenPowerSettings(); };
            wakeReadinessActions.Controls.Add(wakeRefreshButton);
            wakeReadinessActions.Controls.Add(openPowerSettingsButton);
            planDetailLabel = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 9F), Visible = false, Margin = new Padding(0, 5, 0, 4) };
            installedDetailsButton = NewButton("查看计划与唤醒详情", false);
            installedDetailsButton.Margin = new Padding(0, 8, 0, 6);
            installedDetailsButton.Click += delegate { ToggleInstalledDetails(); };
            installedContent.Controls.Add(installedSummaryLabel);
            installedContent.Controls.Add(wakeReadinessLabel);
            installedContent.Controls.Add(wakeReadinessActions);
            installedContent.Controls.Add(installedDetailsButton);
            installedContent.Controls.Add(planDetailLabel);
            installedBody.Controls.Add(installedContent);
            installedBody.ClientSizeChanged += delegate { FitInstalledContent(); };
            rightStack.Controls.Add(installedCard);
            mainGrid.Controls.Add(rightStack, 1, 0);
            pageRoot.Controls.Add(mainGrid, 0, 2);

            FlowLayoutPanel footer = NewFlow(false);
            footer.WrapContents = true;
            footer.Margin = new Padding(0, 12, 0, 0);
            footer.Controls.Add(new Label { Text = "后台使用 read-only 沙盒 · 程序不会读取或保存 ChatGPT 凭据", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 10, 20, 0) });
            openLogButton = NewButton("打开日志", false);
            openLogButton.Click += delegate { OpenLog(); };
            deleteTaskButton = NewButton("删除计划", false);
            deleteTaskButton.ForeColor = Color.FromArgb(161, 91, 109);
            deleteTaskButton.Click += delegate { DeleteSchedule(); };
            footer.Controls.Add(openLogButton);
            footer.Controls.Add(deleteTaskButton);
            pageRoot.Controls.Add(footer, 0, 3);

            StyleInputs(pageRoot);
            viewport.Resize += delegate { LayoutResponsive(); };
            SizeChanged += delegate { LayoutResponsive(); };
            Load += delegate
            {
                Rectangle workingArea = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, workingArea.Width), Math.Min(Height, workingArea.Height - 24));
                CenterToScreen();
                LayoutResponsive();
                LoadState();
            };
            timelineRefreshTimer.Interval = 5000;
            timelineRefreshTimer.Tick += delegate
            {
                if (!hasUnsavedChanges && TimelineHistoryStore.ApplicationAccessEnabled)
                {
                    timelineHistory = TimelineHistoryStore.Load(AppPaths.ExecutionHistoryPath);
                    UpdateTimeline(DateTime.Now);
                }
            };
            Shown += delegate { if (TimelineHistoryStore.ApplicationAccessEnabled) timelineRefreshTimer.Start(); };
            FormClosed += delegate { timelineRefreshTimer.Stop(); timelineRefreshTimer.Dispose(); };
        }

        private static readonly Color Purple = Color.FromArgb(118, 96, 219);
        private static readonly Color Ink = Color.FromArgb(41, 38, 49);
        private static readonly Color Muted = Color.FromArgb(116, 107, 130);

        private void LayoutResponsive()
        {
            if (viewport == null || pageRoot == null || mainGrid == null || leftStack == null || rightStack == null) return;
            int available = Math.Max(300, viewport.ClientSize.Width - pageRoot.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            pageRoot.Width = available + pageRoot.Padding.Horizontal;
            mainGrid.Width = available;
            bool twoColumns = CanUseTwoColumns(available);
            int leftContentWidth = available;
            int rightContentWidth = available;
            if (twoColumns)
            {
                mainGrid.SuspendLayout();
                mainGrid.ColumnCount = 2;
                mainGrid.RowCount = 1;
                mainGrid.ColumnStyles.Clear();
                mainGrid.RowStyles.Clear();
                mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51F));
                mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49F));
                mainGrid.SetCellPosition(leftStack, new TableLayoutPanelCellPosition(0, 0));
                mainGrid.SetCellPosition(rightStack, new TableLayoutPanelCellPosition(1, 0));
                leftContentWidth = (available - 24) * 51 / 100;
                rightContentWidth = available - leftContentWidth - 24;
                leftStack.Width = leftContentWidth;
                rightStack.Width = rightContentWidth;
                leftStack.Margin = new Padding(0, 0, 12, 0);
                rightStack.Margin = new Padding(12, 0, 0, 0);
                mainGrid.ResumeLayout(false);
            }
            else
            {
                mainGrid.SuspendLayout();
                mainGrid.ColumnCount = 1;
                mainGrid.RowCount = 2;
                mainGrid.ColumnStyles.Clear();
                mainGrid.RowStyles.Clear();
                mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                mainGrid.SetCellPosition(leftStack, new TableLayoutPanelCellPosition(0, 0));
                mainGrid.SetCellPosition(rightStack, new TableLayoutPanelCellPosition(0, 1));
                leftStack.Width = leftContentWidth;
                rightStack.Width = rightContentWidth;
                leftStack.Margin = new Padding(0);
                rightStack.Margin = new Padding(0);
                mainGrid.ResumeLayout(false);
            }

            // The TableLayoutPanel has not applied its new percentage-column bounds yet here.
            // Use the widths calculated above instead of reading a stale ClientSize (often zero).
            foreach (Control control in leftStack.Controls) control.Width = leftContentWidth;
            foreach (Control control in rightStack.Controls) control.Width = rightContentWidth;
            leftStack.Height = StackHeight(leftStack);
            rightStack.Height = StackHeight(rightStack);
            routeDetailLabel.MaximumSize = new Size(Math.Max(180, leftContentWidth - 64), 0);
            modelCatalogFeedbackLabel.MaximumSize = new Size(Math.Max(180, leftContentWidth - 64), 0);
            resultSummaryLabel.MaximumSize = new Size(Math.Max(120, rightContentWidth - 64 - SystemInformation.VerticalScrollBarWidth), 0);
            installedSummaryLabel.MaximumSize = new Size(Math.Max(180, rightContentWidth - 64 - SystemInformation.VerticalScrollBarWidth), 0);
            wakeReadinessLabel.MaximumSize = new Size(Math.Max(180, rightContentWidth - 64 - SystemInformation.VerticalScrollBarWidth), 0);
            FitInstalledContent();
            planDetailLabel.MaximumSize = new Size(Math.Max(180, rightContentWidth - 64 - SystemInformation.VerticalScrollBarWidth), 0);
            sequenceSummaryLabel.MaximumSize = new Size(Math.Max(180, leftContentWidth - 64), 0);

            int leftHeight = leftStack.Height + leftStack.Margin.Vertical;
            int rightHeight = rightStack.Height + rightStack.Margin.Vertical;
            mainGrid.SuspendLayout();
            mainGrid.RowStyles.Clear();
            if (twoColumns)
            {
                mainGrid.RowCount = 1;
                mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(leftHeight, rightHeight)));
                mainGrid.Height = Math.Max(leftHeight, rightHeight);
            }
            else
            {
                mainGrid.RowCount = 2;
                mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, leftHeight));
                mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, rightHeight));
                mainGrid.Height = leftHeight + rightHeight;
            }
            mainGrid.ResumeLayout(true);
            pageRoot.PerformLayout();
            mainGrid.PerformLayout();
            leftStack.PerformLayout();
            rightStack.PerformLayout();
            pageRoot.PerformLayout();
            ResizeTimelineColumns();
        }

        private static bool CanUseTwoColumns(int availableWidth)
        {
            return availableWidth >= 1040;
        }

        private int ScaleLayoutHeight(int logicalHeight)
        {
            float scale = 1F;
            if (heroCard != null && heroCard.Height > 0)
            {
                scale = heroCard.Height / 172F;
            }
            return Math.Max(1, (int)Math.Round(logicalHeight * scale));
        }

        private void FitInstalledContent()
        {
            if (wakeReadinessActions == null || installedContent == null || installedBody == null || installedBody.ClientSize.Width <= 0) return;
            int width = Math.Max(180, installedBody.ClientSize.Width);
            if (installedContent.MaximumSize.Width != width || installedContent.Width != width)
            {
                installedContent.MaximumSize = new Size(width, 0);
                installedContent.Width = width;
            }
            installedSummaryLabel.MaximumSize = new Size(width, 0);
            wakeReadinessLabel.MaximumSize = new Size(width, 0);
            planDetailLabel.MaximumSize = new Size(width, 0);
            if (wakeReadinessActions.MaximumSize.Width != width)
                wakeReadinessActions.MaximumSize = new Size(width, 0);
        }

        private static int StackHeight(FlowLayoutPanel stack)
        {
            int height = 0;
            foreach (Control control in stack.Controls) height += control.Height + control.Margin.Vertical;
            return height;
        }

        private static FlowLayoutPanel NewFlow(bool vertical)
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight,
                WrapContents = !vertical,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
        }

        private static Panel NewCard(string title, out Panel body, int height)
        {
            Panel card = new RoundedCardPanel { Height = height, BackColor = Color.White, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 16) };
            int headingHeight = string.IsNullOrWhiteSpace(title) ? 0 : 34;
            TableLayoutPanel contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, headingHeight));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label heading = new Label
            {
                Text = title,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0), Padding = new Padding(0) };
            contentLayout.Controls.Add(heading, 0, 0);
            contentLayout.Controls.Add(body, 0, 1);
            card.Controls.Add(contentLayout);
            return card;
        }

        private static Label FieldCaption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(113, 106, 128), Font = new Font("Microsoft YaHei UI", 9F), Margin = new Padding(0, 0, 0, 6) };
        }

        private Panel BuildIntervalField()
        {
            Panel slot;
            Panel field = NewField("每次间隔", out slot, 206, 78);
            FlowLayoutPanel interval = NewFlow(false);
            interval.WrapContents = false;
            interval.Dock = DockStyle.Top;
            sequenceIntervalHoursBox = new NumericUpDown { Minimum = 0, Maximum = 8760, Value = 5, Width = 66, Margin = new Padding(0, 6, 4, 0) };
            sequenceIntervalHoursBox.ValueChanged += delegate { UpdateSequenceSummary(); UpdateDraftTimeline(DateTime.Now); MarkDraftChanged(); };
            sequenceIntervalMinutesBox = new NumericUpDown { Minimum = 0, Maximum = 59, Value = 10, Width = 66, Margin = new Padding(8, 6, 4, 0) };
            sequenceIntervalMinutesBox.ValueChanged += delegate { UpdateSequenceSummary(); UpdateDraftTimeline(DateTime.Now); MarkDraftChanged(); };
            interval.Controls.Add(sequenceIntervalHoursBox);
            interval.Controls.Add(new Label { Text = "时", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 12, 0, 0) });
            interval.Controls.Add(sequenceIntervalMinutesBox);
            interval.Controls.Add(new Label { Text = "分", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 12, 0, 0) });
            slot.Controls.Add(interval);
            return field;
        }

        private static Panel NewField(string title, out Panel slot, int width, int height)
        {
            Panel field = new Panel { Width = width, Height = height, Margin = new Padding(0, 0, 12, 4), BackColor = Color.White };
            Label caption = FieldCaption(title);
            caption.Dock = DockStyle.Top;
            caption.Height = 24;
            slot = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            field.Controls.Add(slot);
            field.Controls.Add(caption);
            return field;
        }

        private Panel BuildModelField()
        {
            Panel slot;
            Panel field = NewField("请求模型", out slot, 380, 66);
            modelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 320, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0) };
            modelBox.Items.AddRange(AppConfig.ListedRequestModels());
            modelCatalogSnapshot = RequestModelCatalogService.CreateBuiltIn();
            modelBox.TextChanged += delegate { if (!updatingModelCatalogChoices) MarkDraftChanged(); };
            modelBox.SelectedIndexChanged += delegate { if (!updatingModelCatalogChoices) MarkDraftChanged(); };
            slot.Controls.Add(modelBox);
            return field;
        }

        private void ReplaceModelCatalogChoices(IEnumerable<RequestModelCatalogEntry> entries)
        {
            if (modelBox == null)
            {
                return;
            }

            string currentText = modelBox.Text ?? string.Empty;
            bool wasUpdating = updatingModelCatalogChoices;
            bool updateStarted = false;
            updatingModelCatalogChoices = true;
            try
            {
                modelBox.BeginUpdate();
                updateStarted = true;
                modelBox.Items.Clear();
                if (entries != null)
                {
                    foreach (RequestModelCatalogEntry entry in entries)
                    {
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.Slug))
                        {
                            modelBox.Items.Add(entry.Slug);
                        }
                    }
                }
                modelBox.SelectedIndex = -1;
                modelBox.Text = currentText;
            }
            finally
            {
                if (updateStarted)
                {
                    modelBox.EndUpdate();
                }
                updatingModelCatalogChoices = wasUpdating;
            }
        }

        private string ApplyModelCatalogRefreshResult(RequestModelCatalogRefreshResult result, string requestModelSnapshot)
        {
            if (result == null || !result.Success || result.Snapshot == null || result.Snapshot.Models == null)
            {
                string failure = result != null && result.TimedOut
                    ? "模型列表暂未刷新（超过 10 秒），仍可测试或手动输入。"
                    : "模型列表暂未刷新，仍可测试或手动输入。";
                SetModelCatalogFeedback(failure, true);
                return failure;
            }

            string currentSelection = modelBox.Text ?? string.Empty;
            RequestModelCatalogSnapshot previous = modelCatalogSnapshot ?? RequestModelCatalogService.CreateBuiltIn();
            HashSet<string> previousSlugs = new HashSet<string>(
                previous.Models.Select(delegate(RequestModelCatalogEntry item) { return item.Slug; }),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> refreshedSlugs = new HashSet<string>(
                result.Snapshot.Models.Select(delegate(RequestModelCatalogEntry item) { return item.Slug; }),
                StringComparer.OrdinalIgnoreCase);
            List<RequestModelCatalogEntry> added = result.Snapshot.Models
                .Where(delegate(RequestModelCatalogEntry item) { return !previousSlugs.Contains(item.Slug); })
                .ToList();
            bool selectionWasListed = previousSlugs.Contains(currentSelection.Trim());
            bool selectionMissing = selectionWasListed && !refreshedSlugs.Contains(currentSelection.Trim());

            ReplaceModelCatalogChoices(result.Snapshot.Models);
            modelCatalogSnapshot = result.Snapshot;

            StringBuilder feedback = new StringBuilder();
            if (added.Count == 0)
            {
                feedback.Append("模型列表已刷新。");
            }
            else
            {
                feedback.Append("模型列表已刷新，新增 ").Append(added.Count).Append(" 个选项");
                string[] examples = added.Take(2)
                    .Select(delegate(RequestModelCatalogEntry item) { return item.DisplayName; })
                    .ToArray();
                if (examples.Length > 0)
                {
                    feedback.Append("（").Append(string.Join("、", examples));
                    if (added.Count > examples.Length)
                    {
                        feedback.Append("等");
                    }
                    feedback.Append("）");
                }
                feedback.Append("。");
            }

            if (selectionMissing)
            {
                feedback.Append("当前选择“").Append(ShortModelLabel(currentSelection))
                    .Append("”不在最新目录中，已保留；本次请求仍使用“")
                    .Append(ShortModelLabel(requestModelSnapshot)).Append("”。");
            }
            else
            {
                feedback.Append("当前选择保持不变。");
            }
            if (!result.CacheSaved)
            {
                feedback.Append("本次列表可用，但缓存未写入；下次启动可能回退。");
            }

            string message = feedback.ToString();
            SetModelCatalogFeedback(message, selectionMissing || !result.CacheSaved);
            return message;
        }

        private void SetModelCatalogFeedback(string message, bool warning)
        {
            modelCatalogFeedbackLabel.Text = message ?? string.Empty;
            modelCatalogFeedbackLabel.ForeColor = warning ? Color.FromArgb(160, 100, 20) : Muted;
            modelCatalogFeedbackLabel.Visible = !string.IsNullOrWhiteSpace(message);
            routeCard.Height = RouteCardLayoutHeight();
            LayoutResponsive();
        }

        private int RouteCardLayoutHeight()
        {
            int height = routeDetailsExpanded ? 370 : 250;
            if (modelCatalogFeedbackLabel != null && !string.IsNullOrWhiteSpace(modelCatalogFeedbackLabel.Text))
            {
                height += 44;
            }
            return ScaleLayoutHeight(height);
        }

        private static string ShortModelLabel(string value)
        {
            string safe = new string((value ?? string.Empty).Where(delegate(char c) { return !char.IsControl(c); }).ToArray()).Trim();
            return safe.Length > 48 ? safe.Substring(0, 45) + "…" : safe;
        }

        private void QueueModelCatalogFeedback(Task<RequestModelCatalogRefreshResult> refreshTask, string requestModelSnapshot, TestResultDialog dialog)
        {
            refreshTask.ContinueWith(delegate(Task<RequestModelCatalogRefreshResult> completed)
            {
                RequestModelCatalogRefreshResult result = completed.IsFaulted || completed.IsCanceled
                    ? new RequestModelCatalogRefreshResult()
                    : completed.Result;
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    return;
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (!IsDisposed && !Disposing)
                        {
                            string feedback = ApplyModelCatalogRefreshResult(result, requestModelSnapshot);
                            if (dialog != null && !dialog.IsDisposed && !dialog.Disposing)
                                dialog.UpdateCatalogFeedback(feedback);
                        }
                    }));
                }
                catch
                {
                }
            });
        }

        private Panel BuildRetryField()
        {
            Panel slot;
            Panel field = NewField("最多尝试次数（含首次）", out slot, 380, 66);
            retryBox = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 3, Width = 160, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0) };
            retryBox.ValueChanged += delegate { MarkDraftChanged(); };
            slot.Controls.Add(retryBox);
            return field;
        }

        private void MarkDraftChanged()
        {
            hasUnsavedChanges = true;
            if (heroHintLabel != null && !IsDisposed) heroHintLabel.Text = DraftHint();
        }

        private string DraftHint()
        {
            if (legacyDefaultModelMigrationPending
                && modelBox != null
                && string.Equals((modelBox.Text ?? string.Empty).Trim(), AppConfig.DefaultRequestModel, StringComparison.OrdinalIgnoreCase))
            {
                return "旧默认请求模型已预填为 GPT-6 Luna；保存并启用后才写入配置与计划。";
            }
            return "正在预览未保存的修改 · 保存后才会更新 Windows 计划";
        }

        private void ToggleAdvancedSettings()
        {
            advancedExpanded = !advancedExpanded;
            advancedBody.Visible = advancedExpanded;
            advancedToggleButton.Text = advancedExpanded ? "收起高级设置  −" : "展开高级设置  ＋";
            advancedCard.Height = advancedExpanded
                ? Math.Max(ScaleLayoutHeight(360), advancedBody.PreferredSize.Height + ScaleLayoutHeight(128))
                : ScaleLayoutHeight(120);
            LayoutResponsive();
        }

        private void ToggleRouteDetails()
        {
            routeDetailsExpanded = !routeDetailsExpanded;
            routeDetailLabel.Visible = routeDetailsExpanded;
            routeDetailsButton.Text = routeDetailsExpanded ? "收起链路详情" : "查看链路详情";
            routeCard.Height = RouteCardLayoutHeight();
            LayoutResponsive();
        }

        private void ToggleInstalledDetails()
        {
            installedDetailsExpanded = !installedDetailsExpanded;
            planDetailLabel.Visible = installedDetailsExpanded;
            installedDetailsButton.Text = installedDetailsExpanded ? "收起计划与唤醒详情" : "查看计划与唤醒详情";
            installedCard.Height = ScaleLayoutHeight(installedDetailsExpanded ? 360 : 260);
            LayoutResponsive();
        }

        private void ToggleResultDetails()
        {
            resultDetailsExpanded = !resultDetailsExpanded;
            resultBox.Visible = resultDetailsExpanded;
            resultSummaryLabel.Visible = !resultDetailsExpanded;
            resultSummaryLabel.Parent.Visible = !resultDetailsExpanded;
            resultDetailsButton.Text = resultDetailsExpanded ? "收起最近尝试详情" : "查看最近尝试详情";
        }

        internal static bool VerifyPresentation(out string geometryEvidence)
        {
            string wideEvidence;
            string narrowEvidence;
            string scaledEvidence;
            bool wide = VerifyLayoutScenario(1420, 880, 1F, true, out wideEvidence);
            bool narrow = VerifyLayoutScenario(760, 620, 1F, false, out narrowEvidence);
            bool scaled = VerifyLayoutScenario(1420, 880, 1.25F, true, out scaledEvidence);
            geometryEvidence = "wide{" + wideEvidence + "}; narrow{" + narrowEvidence + "}; scaled{" + scaledEvidence + "}";
            return wide && narrow && scaled;
        }

        internal static bool VerifyRequestModelCatalogInteraction(out string evidence)
        {
            try
            {
                using (MainForm form = new MainForm())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.ShowInTaskbar = false;
                    form.ClientSize = new Size(960, 800);
                    IntPtr formHandle = form.Handle;
                    EnsureControlHandles(form);
                    MethodInfo onLoad = typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (onLoad == null) throw new InvalidOperationException("无法触发 WinForms Load 模型目录路径。");
                    onLoad.Invoke(form, new object[] { EventArgs.Empty });

                    RequestModelCatalogSnapshot previous = CreateModelCatalogFixture("gpt-6-luna", "gpt-6-retired");
                    RequestModelCatalogSnapshot refreshed = CreateModelCatalogFixture("gpt-6-luna", "gpt-6.1-sol", "gpt-6.2-sol");
                    form.modelCatalogSnapshot = previous;
                    form.ReplaceModelCatalogChoices(previous.Models);
                    form.modelBox.Text = "gpt-6-retired";
                    AppConfig requestSnapshot = form.ReadForm(false);
                    bool dirtyBeforeUpdate = form.hasUnsavedChanges;
                    RequestModelCatalogRefreshResult success = new RequestModelCatalogRefreshResult
                    {
                        Success = true,
                        CacheSaved = true,
                        Snapshot = refreshed
                    };
                    string firstFeedback = form.ApplyModelCatalogRefreshResult(success, requestSnapshot.Model);
                    bool removedSelectionPreserved = form.modelBox.Text == "gpt-6-retired"
                        && form.modelBox.Items.Contains("gpt-6.2-sol")
                        && firstFeedback.IndexOf("不在最新目录中", StringComparison.Ordinal) >= 0;
                    bool newModelReported = firstFeedback.IndexOf("新增 2 个选项", StringComparison.Ordinal) >= 0;
                    bool requestSnapshotStable = requestSnapshot.Model == "gpt-6-retired"
                        && firstFeedback.IndexOf("本次请求仍使用“gpt-6-retired”", StringComparison.Ordinal) >= 0;
                    bool updateDidNotMarkDraft = form.hasUnsavedChanges == dirtyBeforeUpdate;
                    PerformLayoutTree(form);
                    form.LayoutResponsive();
                    PerformLayoutTree(form);
                    Rectangle feedbackBounds = form.modelCatalogFeedbackLabel.Bounds;
                    Rectangle feedbackParentClient = form.modelCatalogFeedbackLabel.Parent.ClientRectangle;
                    int expectedRouteHeight = form.RouteCardLayoutHeight();
                    bool feedbackLayoutValid = !string.IsNullOrWhiteSpace(form.modelCatalogFeedbackLabel.Text)
                        && feedbackParentClient.Contains(feedbackBounds)
                        && form.routeCard.Height >= expectedRouteHeight;

                    form.modelBox.Text = "vendor/custom-model";
                    AppConfig customRequestSnapshot = form.ReadForm(false);
                    string customFeedback = form.ApplyModelCatalogRefreshResult(success, customRequestSnapshot.Model);
                    bool customSelectionPreserved = form.modelBox.Text == "vendor/custom-model"
                        && customRequestSnapshot.Model == "vendor/custom-model"
                        && customFeedback.IndexOf("当前选择保持不变", StringComparison.Ordinal) >= 0;

                    string itemsBeforeFailure = string.Join("|", form.modelBox.Items.Cast<string>());
                    RequestModelCatalogRefreshResult timeout = new RequestModelCatalogRefreshResult { TimedOut = true };
                    string failureFeedback = form.ApplyModelCatalogRefreshResult(timeout, customRequestSnapshot.Model);
                    string itemsAfterFailure = string.Join("|", form.modelBox.Items.Cast<string>());
                    bool failurePreservesListAndSelection = itemsBeforeFailure == itemsAfterFailure
                        && form.modelBox.Text == "vendor/custom-model"
                        && failureFeedback.IndexOf("超过 10 秒", StringComparison.Ordinal) >= 0
                        && failureFeedback.IndexOf("仍可测试或手动输入", StringComparison.Ordinal) >= 0;

                    evidence = "removedSelectionPreserved=" + removedSelectionPreserved
                        + "; newModelReported=" + newModelReported
                        + "; requestSnapshotStable=" + requestSnapshotStable
                        + "; customSelectionPreserved=" + customSelectionPreserved
                        + "; updateDidNotMarkDraft=" + updateDidNotMarkDraft
                        + "; failurePreservesListAndSelection=" + failurePreservesListAndSelection
                        + "; feedbackLayoutValid=" + feedbackLayoutValid
                        + "[label=" + feedbackBounds + "; parent=" + feedbackParentClient
                        + "; route=" + form.routeCard.Height + "; expected=" + expectedRouteHeight + "]";
                    return removedSelectionPreserved && newModelReported && requestSnapshotStable
                        && customSelectionPreserved && updateDidNotMarkDraft
                        && failurePreservesListAndSelection && feedbackLayoutValid;
                }
            }
            catch (Exception ex)
            {
                evidence = "exception=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static RequestModelCatalogSnapshot CreateModelCatalogFixture(params string[] slugs)
        {
            RequestModelCatalogSnapshot snapshot = new RequestModelCatalogSnapshot
            {
                RefreshedAtUtc = "2026-10-04T00:00:00.0000000Z"
            };
            foreach (string slug in slugs)
            {
                snapshot.Models.Add(new RequestModelCatalogEntry { Slug = slug, DisplayName = slug });
            }
            return snapshot;
        }

        internal static bool VerifyFlexiblePlanInputOrder(out string evidence)
        {
            try
            {
                using (MainForm form = new MainForm())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.ShowInTaskbar = false;
                    form.ClientSize = new Size(960, 800);
                    IntPtr formHandle = form.Handle;
                    EnsureControlHandles(form);
                    MethodInfo onLoad = typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (onLoad == null) throw new InvalidOperationException("无法触发 WinForms Load 输入路径。");
                    onLoad.Invoke(form, new object[] { EventArgs.Empty });
                    form.extraWakeList.Items.Clear();

                    int parsedCount;
                    bool blankRejected = !SequenceCountInput.TryParse(string.Empty, out parsedCount)
                        && !SequenceCountInput.TryParse("   ", out parsedCount)
                        && !SequenceCountInput.TryParse("abc", out parsedCount);
                    bool zeroAccepted = SequenceCountInput.TryParse("0", out parsedCount) && parsedCount == 0;

                    form.sequenceCountBox.Text = string.Empty;
                    bool blankInputsEnabled = form.sequenceDatePicker.Enabled
                        && form.sequenceTimePicker.Enabled
                        && form.sequenceNowButton.Enabled
                        && form.sequenceIntervalHoursBox.Enabled
                        && form.sequenceIntervalMinutesBox.Enabled;
                    DateTime chosenStart = new DateTime(2099, 7, 8, 9, 10, 0);
                    form.sequenceDatePicker.Value = chosenStart.Date;
                    form.sequenceTimePicker.Value = DateTime.Today.AddHours(chosenStart.Hour).AddMinutes(chosenStart.Minute);
                    form.sequenceIntervalHoursBox.Value = 5;
                    form.sequenceIntervalMinutesBox.Value = 10;
                    form.sequenceCountBox.Text = "3";
                    AppConfig threeRunDraft = form.ReadForm(false);
                    List<PlannedRun> threeRunPlan = SchedulePlanner.Build(threeRunDraft, DateTime.MinValue, 14);
                    bool retainedBlankDraft = blankInputsEnabled
                        && threeRunDraft.SequenceCount == 3
                        && threeRunPlan.Count == 3
                        && threeRunPlan[0].Time == chosenStart
                        && threeRunPlan[1].Time == chosenStart.AddMinutes(310)
                        && threeRunPlan[2].Time == chosenStart.AddMinutes(620);

                    form.sequenceCountBox.Text = "temporary-invalid";
                    bool invalidInputsEnabled = form.sequenceDatePicker.Enabled
                        && form.sequenceTimePicker.Enabled
                        && form.sequenceNowButton.Enabled
                        && form.sequenceIntervalHoursBox.Enabled
                        && form.sequenceIntervalMinutesBox.Enabled;
                    bool invalidSaveRejected = !form.SaveMaintenanceDraft();
                    DateTime invalidDraftStart = new DateTime(2099, 8, 9, 11, 12, 0);
                    form.sequenceDatePicker.Value = invalidDraftStart.Date;
                    form.sequenceTimePicker.Value = DateTime.Today.AddHours(invalidDraftStart.Hour).AddMinutes(invalidDraftStart.Minute);
                    form.sequenceIntervalHoursBox.Value = 3;
                    form.sequenceIntervalMinutesBox.Value = 15;
                    form.sequenceCountBox.Text = "2";
                    AppConfig recoveredDraft = form.ReadForm(false);
                    List<PlannedRun> recoveredPlan = SchedulePlanner.Build(recoveredDraft, DateTime.MinValue, 14);
                    bool retainedInvalidDraft = invalidInputsEnabled
                        && recoveredPlan.Count == 2
                        && recoveredPlan[0].Time == invalidDraftStart
                        && recoveredPlan[1].Time == invalidDraftStart.AddMinutes(195);

                    form.sequenceCountBox.Text = "0";
                    bool zeroInputsEnabled = form.sequenceDatePicker.Enabled
                        && form.sequenceTimePicker.Enabled
                        && form.sequenceNowButton.Enabled
                        && form.sequenceIntervalHoursBox.Enabled
                        && form.sequenceIntervalMinutesBox.Enabled;
                    DateTime nowBefore = DateTime.Now;
                    InvokeButtonClick(form.sequenceNowButton);
                    DateTime nowAfter = DateTime.Now;
                    DateTime pickedNow = form.ReadSequenceStart();
                    bool nowUsesNextMinute = SequenceStartShortcut.IsEnabled()
                        && pickedNow >= SequenceStartShortcut.NextMinute(nowBefore)
                        && pickedNow <= SequenceStartShortcut.NextMinute(nowAfter);

                    DateTime extraWake = new DateTime(2099, 9, 10, 10, 11, 0);
                    form.extraWakeDatePicker.Value = extraWake.Date;
                    form.extraWakeTimePicker.Value = DateTime.Today.AddHours(extraWake.Hour).AddMinutes(extraWake.Minute);
                    InvokeButtonClick(form.addExtraWakeButton);
                    AppConfig zeroWithExtra = form.ReadForm(false);
                    List<PlannedRun> extraOnlyPlan = SchedulePlanner.Build(zeroWithExtra, DateTime.MinValue, 14);
                    bool zeroWithExtraWorks = zeroWithExtra.SequenceCount == 0
                        && extraOnlyPlan.Count == 1
                        && extraOnlyPlan[0].Time == extraWake
                        && extraOnlyPlan[0].Source == "插入唤醒";

                    evidence = "blankRejected=" + blankRejected
                        + "; zeroAccepted=" + zeroAccepted
                        + "; blankInputsEnabled=" + blankInputsEnabled
                        + "; blankDraftUsesEarlierValues=" + retainedBlankDraft
                        + "; invalidInputsEnabled=" + invalidInputsEnabled
                        + "; invalidSaveRejected=" + invalidSaveRejected
                        + "; invalidDraftUsesEarlierValues=" + retainedInvalidDraft
                        + "; zeroInputsEnabled=" + zeroInputsEnabled
                        + "; nowUsesNextMinute=" + nowUsesNextMinute
                        + "; zeroWithExtraWorks=" + zeroWithExtraWorks;
                    return blankRejected && zeroAccepted && retainedBlankDraft
                        && invalidSaveRejected && retainedInvalidDraft && zeroInputsEnabled
                        && nowUsesNextMinute && zeroWithExtraWorks;
                }
            }
            catch (Exception ex)
            {
                evidence = "exception=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        internal static bool VerifyManualTestValidationIsolation(out string evidence)
        {
            try
            {
                using (MainForm form = new MainForm())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.ShowInTaskbar = false;
                    form.ClientSize = new Size(960, 800);
                    IntPtr formHandle = form.Handle;
                    EnsureControlHandles(form);
                    MethodInfo onLoad = typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (onLoad == null) throw new InvalidOperationException("无法触发 WinForms Load 请求测试路径。");
                    onLoad.Invoke(form, new object[] { EventArgs.Empty });

                    form.modelBox.Text = "gpt-6-luna";
                    form.promptBox.Text = "Respond with OK.";
                    form.extraWakeList.Items.Clear();
                    form.sequenceIntervalHoursBox.Value = 0;
                    form.sequenceIntervalMinutesBox.Value = 1;
                    DateTime futureStart = DateTime.Today.AddDays(2).AddHours(9);
                    form.sequenceDatePicker.Value = futureStart.Date;
                    form.sequenceTimePicker.Value = DateTime.Today.AddHours(futureStart.Hour).AddMinutes(futureStart.Minute);

                    bool invalidPlanDoesNotBlockTest = true;
                    bool invalidPlanDoesNotMutateDraft = true;
                    string[] invalidCounts = new[] { string.Empty, "   ", "temporary-invalid", "-1", "0" };
                    foreach (string invalidCount in invalidCounts)
                    {
                        form.extraWakeList.Items.Clear();
                        form.sequenceCountBox.Text = invalidCount;
                        DateTime startBefore = form.ReadSequenceStart();
                        int intervalBefore = form.ReadSequenceIntervalMinutes();
                        bool requestAccepted = form.ValidateRequestInputs(false);
                        bool planRejected = !form.ValidateForm(true, false);
                        bool draftUnchanged = form.sequenceCountBox.Text == invalidCount
                            && form.ReadSequenceStart() == startBefore
                            && form.ReadSequenceIntervalMinutes() == intervalBefore
                            && form.extraWakeList.Items.Count == 0;
                        invalidPlanDoesNotBlockTest = invalidPlanDoesNotBlockTest && requestAccepted && planRejected;
                        invalidPlanDoesNotMutateDraft = invalidPlanDoesNotMutateDraft && draftUnchanged;
                    }

                    form.sequenceCountBox.Text = "2";
                    form.sequenceDatePicker.Value = DateTime.Today.AddDays(-2);
                    form.sequenceTimePicker.Value = DateTime.Today.AddHours(8);
                    bool expiredStartDoesNotBlockTest = form.ValidateRequestInputs(false)
                        && !form.ValidateForm(true, false);

                    form.sequenceDatePicker.Value = futureStart.Date;
                    form.sequenceTimePicker.Value = DateTime.Today.AddHours(futureStart.Hour).AddMinutes(futureStart.Minute);
                    form.sequenceIntervalHoursBox.Value = 0;
                    form.sequenceIntervalMinutesBox.Value = 0;
                    bool invalidIntervalDoesNotBlockTest = form.ValidateRequestInputs(false)
                        && !form.ValidateForm(true, false);

                    form.sequenceCountBox.Text = "0";
                    form.extraWakeList.Items.Add("2000-01-01 00:00");
                    bool pastExtraDoesNotBlockTest = form.ValidateRequestInputs(false)
                        && !form.ValidateForm(true, false);

                    form.extraWakeList.Items.Clear();
                    form.sequenceCountBox.Text = "1";
                    form.sequenceIntervalMinutesBox.Value = 0;
                    bool completePlanAccepted = form.ValidateForm(true, false);
                    form.modelBox.Text = " ";
                    bool emptyModelRejected = !form.ValidateRequestInputs(false);
                    form.modelBox.Text = "gpt-6-luna";
                    form.promptBox.Text = " ";
                    bool emptyPromptRejected = !form.ValidateRequestInputs(false);

                    DateTime fixtureNow = DateTime.Now;
                    RunRecord resetEvidence = new RunRecord
                    {
                        Success = true,
                        HasReliableResetTime = true,
                        ResetsAtUnix = new DateTimeOffset(fixtureNow.AddHours(3)).ToUnixTimeSeconds()
                    };
                    bool resetPrefillGatesInvalidCounts = true;
                    foreach (string invalidCount in new[] { string.Empty, "invalid", "0" })
                    {
                        form.sequenceCountBox.Text = invalidCount;
                        ManualTestScheduleSnapshot before = form.CaptureManualTestScheduleSnapshot();
                        ResetScheduleAdjustment adjustment = ManualTestSchedulePolicy.EvaluateReset(
                            resetEvidence,
                            before,
                            form.CaptureManualTestScheduleSnapshot(),
                            fixtureNow);
                        resetPrefillGatesInvalidCounts = resetPrefillGatesInvalidCounts
                            && !adjustment.Applied
                            && form.sequenceCountBox.Text == invalidCount
                            && form.ReadSequenceStart() == before.SequenceStart;
                    }

                    form.sequenceCountBox.Text = "1";
                    ManualTestScheduleSnapshot validBefore = form.CaptureManualTestScheduleSnapshot();
                    ResetScheduleAdjustment validAdjustment = ManualTestSchedulePolicy.EvaluateReset(
                        resetEvidence,
                        validBefore,
                        form.CaptureManualTestScheduleSnapshot(),
                        fixtureNow);
                    bool positiveCountKeepsPrefill = validAdjustment.Applied;

                    ManualTestScheduleSnapshot changedBefore = form.CaptureManualTestScheduleSnapshot();
                    DateTime changedStart = futureStart.AddDays(1);
                    form.sequenceDatePicker.Value = changedStart.Date;
                    form.sequenceTimePicker.Value = DateTime.Today.AddHours(changedStart.Hour).AddMinutes(changedStart.Minute);
                    DateTime preservedStart = form.ReadSequenceStart();
                    ResetScheduleAdjustment changedAdjustment = ManualTestSchedulePolicy.EvaluateReset(
                        resetEvidence,
                        changedBefore,
                        form.CaptureManualTestScheduleSnapshot(),
                        fixtureNow);
                    bool concurrentDraftIsPreserved = !changedAdjustment.Applied
                        && form.ReadSequenceStart() == preservedStart;

                    evidence = "invalidPlanTestAllowed=" + invalidPlanDoesNotBlockTest
                        + "; invalidPlanDraftUnchanged=" + invalidPlanDoesNotMutateDraft
                        + "; expiredStartTestAllowed=" + expiredStartDoesNotBlockTest
                        + "; invalidIntervalTestAllowed=" + invalidIntervalDoesNotBlockTest
                        + "; pastExtraTestAllowed=" + pastExtraDoesNotBlockTest
                        + "; completePlanAccepted=" + completePlanAccepted
                        + "; emptyModelRejected=" + emptyModelRejected
                        + "; emptyPromptRejected=" + emptyPromptRejected
                        + "; invalidCountPrefillBlocked=" + resetPrefillGatesInvalidCounts
                        + "; positiveCountPrefillPreserved=" + positiveCountKeepsPrefill
                        + "; concurrentDraftPreserved=" + concurrentDraftIsPreserved;
                    return invalidPlanDoesNotBlockTest && invalidPlanDoesNotMutateDraft
                        && expiredStartDoesNotBlockTest && invalidIntervalDoesNotBlockTest
                        && pastExtraDoesNotBlockTest && completePlanAccepted
                        && emptyModelRejected && emptyPromptRejected
                        && resetPrefillGatesInvalidCounts && positiveCountKeepsPrefill
                        && concurrentDraftIsPreserved;
                }
            }
            catch (Exception ex)
            {
                evidence = "exception=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        internal static bool VerifyRecentSuccessLayout(out string evidence)
        {
            bool ok = true;
            List<string> checks = new List<string>();
            foreach (int width in new[] { 1420, 760 })
            foreach (float scale in new[] { 1F, 1.5F })
            using (MainForm form = new MainForm())
            {
                if (scale != 1F)
                {
                    form.Scale(new SizeF(scale, scale));
                    form.Font = new Font("Microsoft YaHei UI", 9F * scale);
                    form.resultSummaryLabel.Font = new Font("Microsoft YaHei UI", 10F * scale);
                }
                form.ClientSize = new Size(width, 880);
                IntPtr handle = form.Handle;
                EnsureControlHandles(form);
                typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { EventArgs.Empty });
                form.LayoutResponsive();
                DateTimeOffset now = DateTimeOffset.UtcNow.AddHours(-1);
                RunRecord success = RecentSuccessChecks.Fixture("manual", now);
                RunRecord failure = RecentSuccessChecks.Fixture("scheduled", now.AddMinutes(1));
                failure.Success = false; failure.ExitCode = 1; failure.Error = "网络超时"; failure.FinalModel = "gpt-failure-model";
                foreach (int scenario in new[] { 0, 1, 2, 3 })
                {
                    RunRecord latest = scenario == 0 || scenario == 3 ? null : scenario == 1 ? success : failure;
                    form.SetRecentResultSummary(scenario == 1 || scenario == 2 ? RecentSuccessStore.FromRun(success) : null,
                        scenario == 3, latest, scenario == 3);
                    form.SetLatestAttemptDetails(latest);
                    form.UpdateResultPanelMinimum();
                    PerformLayoutTree(form);
                    Panel panel = (Panel)form.resultSummaryLabel.Parent;
                    Size preferred = form.resultSummaryLabel.GetPreferredSize(new Size(form.resultSummaryLabel.MaximumSize.Width, 0));
                    Rectangle panelBounds = BoundsInAncestor(panel, form.resultDetailsButton.Parent);
                    bool readable = form.resultSummaryLabel.Height >= preferred.Height
                        && form.resultSummaryLabel.Width <= panel.ClientSize.Width
                        && panelBounds.Bottom <= form.resultDetailsButton.Top && panel.AutoScroll;
                    if (form.resultSummaryLabel.Height > panel.ClientSize.Height)
                    {
                        panel.AutoScrollPosition = new Point(0, form.resultSummaryLabel.Height);
                        checks.Add("scroll:" + width + "/" + scale + ",offset=" + panel.AutoScrollPosition
                            + ",label=" + FormatBounds(form.resultSummaryLabel.Bounds) + ",min=" + panel.AutoScrollMinSize
                            + ",vertical=" + panel.VerticalScroll.Visible);
                        readable &= panel.ClientRectangle.IntersectsWith(form.resultSummaryLabel.Bounds)
                            && form.resultSummaryLabel.Bottom <= panel.ClientSize.Height;
                        panel.AutoScrollPosition = Point.Empty;
                    }
                    bool detail = form.resultDetailsButton.Enabled == (latest != null);
                    if (latest != null)
                    {
                        form.ToggleResultDetails();
                        PerformLayoutTree(form);
                        detail &= form.resultDetailsExpanded && form.resultDetailsButton.Text.Contains("最近尝试")
                            && form.resultBox.Bottom <= form.resultDetailsButton.Top;
                        if (scenario == 2) detail &= form.resultBox.Text.Contains("gpt-failure-model") && form.resultBox.Text.Contains("网络超时")
                            && form.resultSummaryLabel.Text.Contains("gpt-6-luna") && !form.resultSummaryLabel.Text.Contains("gpt-failure-model");
                        form.ToggleResultDetails();
                    }
                    checks.Add(width + "/" + scale + "/" + scenario + ":readable=" + readable + ",latestDetails=" + detail
                        + ",label=" + FormatBounds(form.resultSummaryLabel.Bounds) + ",panel=" + FormatBounds(panel.ClientRectangle)
                        + ",split=" + form.resultsSplit.Height + "/" + form.resultsSplit.SplitterDistance + "/" + form.resultsSplit.Panel2MinSize);
                    ok &= readable && detail;
                }
            }
            evidence = string.Join("; ", checks);
            return ok;
        }

        internal static bool VerifyWakeReadinessLayout(out string evidence)
        {
            bool ok = true;
            List<string> checks = new List<string>();
            foreach (int width in new[] { 1420, 760 })
            foreach (float scale in new[] { 1F, 1.5F })
            using (MainForm form = new MainForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.ShowInTaskbar = false;
                if (Math.Abs(scale - 1F) > 0.001F)
                {
                    form.Scale(new SizeF(scale, scale));
                    form.Font = new Font("Microsoft YaHei UI", 9F * scale);
                }
                form.ClientSize = new Size(width, 620);
                IntPtr handle = form.Handle;
                EnsureControlHandles(form);
                form.config = new AppConfig { SequenceCount = 1, SequenceStart = DateTime.Now.AddHours(2).ToString("yyyy-MM-dd HH:mm") };
                form.LayoutResponsive();
                WakeSystemSnapshot power = WakeReadinessChecks.PowerForLayout();
                WindowsTaskStatus task = WakeReadinessChecks.TaskForLayout();
                WakeReadinessView view = WakeReadinessPolicy.Evaluate(power, task);
                form.installedSummaryLabel.Text = BuildInstalledTaskSummary(task, WakeReadinessPolicy.InspectTask(task));
                form.UpdateWakeReadiness(power, task);
                PerformLayoutTree(form);

                Panel body = form.installedBody;
                Size leadPreferred = form.wakeReadinessLabel.GetPreferredSize(new Size(form.wakeReadinessLabel.MaximumSize.Width, 0));
                Rectangle summary = BoundsInAncestor(form.installedSummaryLabel, body);
                Rectangle lead = BoundsInAncestor(form.wakeReadinessLabel, body);
                Rectangle actions = BoundsInAncestor(form.wakeReadinessActions, body);
                Rectangle detailToggle = BoundsInAncestor(form.installedDetailsButton, body);
                bool collapsedReadable = body.AutoScroll && view.ShowPowerSettings
                    && form.wakeReadinessActions.Controls.Contains(form.openPowerSettingsButton)
                    && form.wakeRefreshButton.Enabled && form.wakeReadinessLabel.Height >= leadPreferred.Height
                    && lead.Width <= body.ClientSize.Width && actions.Width <= body.ClientSize.Width && !summary.IntersectsWith(lead)
                    && !lead.IntersectsWith(actions) && form.installedDetailsButton.Parent == form.installedContent;

                form.installedDetailsExpanded = true;
                form.planDetailLabel.Visible = true;
                form.installedDetailsButton.Text = "收起计划与唤醒详情";
                form.installedCard.Height = form.ScaleLayoutHeight(360);
                form.SetPlanDetails(task, null, view);
                form.LayoutResponsive();
                PerformLayoutTree(form);
                Size detailPreferred = form.planDetailLabel.GetPreferredSize(new Size(form.planDetailLabel.MaximumSize.Width, 0));
                Rectangle details = BoundsInAncestor(form.planDetailLabel, body);
                Rectangle expandedActions = BoundsInAncestor(form.wakeReadinessActions, body);
                Rectangle expandedToggle = BoundsInAncestor(form.installedDetailsButton, body);
                bool expandedReadable = form.planDetailLabel.Height >= detailPreferred.Height
                    && form.planDetailLabel.Width <= body.ClientSize.Width
                    && expandedActions.Width <= body.ClientSize.Width
                    && !summary.IntersectsWith(lead) && !details.IntersectsWith(expandedActions)
                    && !details.IntersectsWith(expandedToggle)
                    && body.ClientRectangle.IntersectsWith(expandedToggle);
                body.ScrollControlIntoView(form.planDetailLabel);
                PerformLayoutTree(form);
                Rectangle scrolledDetails = BoundsInAncestor(form.planDetailLabel, body);
                expandedReadable &= body.ClientRectangle.IntersectsWith(scrolledDetails);
                body.AutoScrollPosition = Point.Empty;
                body.ScrollControlIntoView(form.wakeRefreshButton);
                PerformLayoutTree(form);
                Rectangle scrolledRefresh = BoundsInAncestor(form.wakeRefreshButton, body);
                bool refreshReachable = body.ClientRectangle.IntersectsWith(scrolledRefresh);
                checks.Add(width + "/" + scale + ":collapsed=" + collapsedReadable + "[auto=" + body.AutoScroll
                    + ",settingsExpected=" + view.ShowPowerSettings + ",refresh=" + form.wakeRefreshButton.Enabled
                    + ",lead=" + form.wakeReadinessLabel.Height + "/" + leadPreferred.Height
                    + ",width=" + lead.Width + "/" + body.ClientSize.Width
                    + ",actionWidth=" + actions.Width + "/" + body.ClientSize.Width
                    + ",summaryOverlap=" + summary.IntersectsWith(lead) + ",actionOverlap=" + lead.IntersectsWith(actions)
                    + ",toggleAttached=" + (form.installedDetailsButton.Parent == form.installedContent) + "] ,expanded=" + expandedReadable
                    + ",refreshReachable=" + refreshReachable + ",body=" + FormatBounds(body.ClientRectangle)
                    + ",lead=" + FormatBounds(lead) + ",actions=" + FormatBounds(expandedActions)
                    + ",details=" + FormatBounds(details) + ",toggle=" + FormatBounds(detailToggle));
                ok &= collapsedReadable && expandedReadable && refreshReachable;
            }
            evidence = string.Join("; ", checks);
            return ok;
        }

        internal static bool VerifyVisibleVersion(out string evidence)
        {
            bool ok = true;
            List<string> checks = new List<string>();
            string expected = "v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
            foreach (int width in new[] { 1420, 760 })
            foreach (float scale in new[] { 1F, 1.5F })
            using (MainForm form = new MainForm())
            {
                if (scale != 1F)
                {
                    form.Scale(new SizeF(scale, scale));
                    form.Font = new Font("Microsoft YaHei UI", 9F * scale);
                    // Simulate the native font metrics as well as the scaled bounds.
                    form.brandVersionLabel.Font = new Font("Microsoft YaHei UI", 9F * scale);
                    form.brandSubtitleLabel.Font = form.brandVersionLabel.Font;
                }
                form.ClientSize = new Size(width, 880);
                IntPtr handle = form.Handle;
                EnsureControlHandles(form);
                typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { EventArgs.Empty });
                form.LayoutResponsive();
                PerformLayoutTree(form);
                form.viewport.AutoScrollPosition = Point.Empty;
                Rectangle versionBounds = BoundsInAncestor(form.brandVersionLabel, form.viewport);
                Rectangle subtitleBounds = BoundsInAncestor(form.brandSubtitleLabel, form.viewport);
                Rectangle heroBounds = BoundsInAncestor(form.heroCard, form.viewport);
                Size text = TextRenderer.MeasureText(form.brandVersionLabel.Text, form.brandVersionLabel.Font);
                bool matches = form.brandVersionLabel.Text == expected && form.Text.EndsWith(expected, StringComparison.Ordinal);
                bool readable = form.brandVersionLabel.AutoSize && form.brandVersionLabel.Width >= text.Width
                    && form.brandVersionLabel.Height >= text.Height && form.viewport.ClientRectangle.Contains(versionBounds)
                    && form.brandVersionLabel.Parent.ClientRectangle.Contains(form.brandVersionLabel.Bounds)
                    && !versionBounds.IntersectsWith(subtitleBounds) && versionBounds.Bottom <= heroBounds.Top;
                checks.Add(width + "/" + scale + ":metadata=" + matches + ",firstScreenReadable=" + readable + ",version=" + FormatBounds(versionBounds));
                ok = ok && matches && readable;
            }
            evidence = string.Join("; ", checks);
            return ok;
        }

        internal static bool VerifyTimelineRendering(out string evidence)
        {
            bool ok = true;
            List<string> checks = new List<string>();
            foreach (float scale in new[] { 1F, 1.5F })
            using (MainForm form = new MainForm())
            {
                IntPtr handle = form.Handle;
                EnsureControlHandles(form);
                typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { EventArgs.Empty });
                form.ClientSize = new Size(760, 620);
                form.timelineList.Font = new Font("Microsoft YaHei UI", 9F * scale);
                DateTime now = DateTime.Now;
                DateTime start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddHours(-4);
                AppConfig fixture = new AppConfig { SequenceStart = start.ToString("yyyy-MM-dd HH:mm"), SequenceCount = 6,
                    SequenceIntervalMinutes = 60, ScheduleVersion = Guid.NewGuid().ToString("N") };
                fixture.ExtraWakeTimes.Add(start.AddHours(6).ToString("yyyy-MM-dd HH:mm"));
                form.config = fixture;
                form.timelineSavedUtc = now.AddDays(-1).ToUniversalTime();
                string key = TimelineExecutionPolicy.PlanKey(fixture, form.timelineSavedUtc);
                form.timelineHistory.Clear();
                using (Process owner = Process.GetCurrentProcess())
                for (int index = 0; index < 3; index++)
                    form.timelineHistory.Add(new TimelineExecution { RunId = "render-" + index, PlanKey = key,
                        PlannedTime = start.AddHours(index).ToString("yyyy-MM-dd HH:mm"), StartedAt = now.ToString("o"),
                        State = index == 0 ? "success" : index == 1 ? "failure" : "running", OwnerPid = owner.Id,
                        OwnerStartedAt = owner.StartTime.ToUniversalTime().ToString("o"), ExpiresAt = new DateTimeOffset(now.AddMinutes(5)).ToString("o") });
                string before = JsonStore.Serialize(fixture);
                form.UpdateTimeline(now, fixture, false);
                string[] actual = form.timelineList.Items.Cast<ListViewItem>().Select(delegate(ListViewItem row) { return row.Text; }).ToArray();
                bool states = actual.SequenceEqual(new[] { "执行成功", "执行失败", "执行中", TimelineExecutionPolicy.Unknown, TimelineExecutionPolicy.Unknown, "下一次", "待执行" });
                bool textFits = form.timelineList.Columns[0].Width >= TextRenderer.MeasureText(TimelineExecutionPolicy.Unknown, form.timelineList.Font).Width;
                form.UpdateTimeline(now, fixture, true);
                bool draft = form.timelineList.Items.Cast<ListViewItem>().Take(5).All(delegate(ListViewItem row) { return row.Text == TimelineExecutionPolicy.Unknown; })
                    && form.timelineSummaryLabel.Text.Contains("预览") && JsonStore.Serialize(fixture) == before;
                checks.Add(scale + ":states=" + states + ",statusFits=" + textFits + ",draftSeparated=" + draft);
                ok = ok && states && textFits && draft;
            }
            evidence = string.Join("; ", checks);
            return ok;
        }

        internal static bool VerifyResultPlanNavigation(out string evidence)
        {
            using (MainForm form = new MainForm())
            {
                IntPtr handle = form.Handle;
                EnsureControlHandles(form);
                MethodInfo load = typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic);
                load.Invoke(form, new object[] { EventArgs.Empty });
                ManualTestScheduleSnapshot before = form.CaptureManualTestScheduleSnapshot();
                string modelBefore = form.modelBox.Text;
                string promptBefore = form.promptBox.Text;
                string configBefore = JsonStore.Serialize(form.config);
                bool dirtyBefore = form.hasUnsavedChanges;
                form.NavigateToPlan();
                ManualTestScheduleSnapshot after = form.CaptureManualTestScheduleSnapshot();
                bool unchanged = JsonStore.Serialize(before) == JsonStore.Serialize(after) && modelBefore == form.modelBox.Text
                    && promptBefore == form.promptBox.Text && configBefore == JsonStore.Serialize(form.config)
                    && dirtyBefore == form.hasUnsavedChanges;
                bool reachable = form.viewport.ClientRectangle.IntersectsWith(BoundsInAncestor(form.sequenceDatePicker, form.viewport));
                evidence = "draftAndConfigUnchanged=" + unchanged + "; planReachable=" + reachable;
                return unchanged && reachable;
            }
        }

        private static void InvokeButtonClick(Button button)
        {
            MethodInfo onClick = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onClick == null) throw new InvalidOperationException("无法触发 WinForms Button.Click 事件。");
            onClick.Invoke(button, new object[] { EventArgs.Empty });
        }

        private static bool VerifyLayoutScenario(int clientWidth, int clientHeight, float scale, bool expectTwoColumns, out string evidence)
        {
            using (MainForm form = new MainForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.ShowInTaskbar = false;
                if (Math.Abs(scale - 1F) > 0.001F)
                {
                    form.Scale(new SizeF(scale, scale));
                    form.Font = new Font("Microsoft YaHei UI", 9F * scale);
                }
                form.ClientSize = new Size(clientWidth, clientHeight);
                IntPtr formHandle = form.Handle;
                EnsureControlHandles(form);
                MethodInfo onLoad = typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic);
                if (onLoad == null) throw new InvalidOperationException("无法触发 WinForms Load 布局路径。");
                onLoad.Invoke(form, new object[] { EventArgs.Empty });
                form.LayoutResponsive();
                PerformLayoutTree(form);
                form.LayoutResponsive();
                PerformLayoutTree(form);

                int available = Math.Max(300, form.viewport.ClientSize.Width - form.pageRoot.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                Rectangle inputInViewport = BoundsInAncestor(form.sequenceDatePicker, form.viewport);
                Rectangle saveInSchedule = BoundsInAncestor(form.saveButton, form.sequenceSchedulePanel);
                Rectangle timelineInViewport = BoundsInAncestor(form.timelineList, form.viewport);
                string cardRegionEvidence;
                bool cardRegionsSeparated = VerifyCardRegions(form, out cardRegionEvidence);
                Rectangle initialViewport = form.viewport.ClientRectangle;
                Rectangle heroInViewport = BoundsInAncestor(form.pageRoot.Controls[1], form.viewport);
                Rectangle scheduleInViewport = BoundsInAncestor(form.leftStack.Controls[0], form.viewport);
                Panel scheduleCard = form.leftStack.Controls.Count > 0 ? form.leftStack.Controls[0] as Panel : null;
                SplitContainer dataSplit = form.rightStack.Controls.Count > 0 ? form.rightStack.Controls[0] as SplitContainer : null;

                bool mode = form.mainGrid.ColumnCount == (expectTwoColumns ? 2 : 1)
                    && form.leftStack.Width > 0 && form.rightStack.Width > 0;
                bool contentGeometry = form.pageRoot.Width > 0 && form.pageRoot.Height > 0
                    && form.mainGrid.Width > 0 && form.mainGrid.Height > 0
                    && form.leftStack.Height > 0 && form.rightStack.Height > 0
                    && scheduleCard != null && scheduleCard.Width > 0 && scheduleCard.Height > 0
                    && form.sequenceDatePicker.Width > 0 && form.sequenceDatePicker.Height > 0
                    && form.saveButton.Width > 0 && form.saveButton.Height > 0
                    && form.timelineList.Width > 0 && form.timelineList.Height > 0;
                bool inputInParent = form.sequenceDatePicker.Parent.ClientRectangle.Contains(form.sequenceDatePicker.Bounds);
                bool saveInParent = form.saveButton.Parent.ClientRectangle.Contains(form.saveButton.Bounds);
                bool scheduleInParent = scheduleCard != null && form.leftStack.ClientRectangle.Contains(scheduleCard.Bounds);
                bool timelineInParent = form.timelineList.Parent.ClientRectangle.Contains(form.timelineList.Bounds);
                bool routeInParent = form.routeTestButton.Parent.ClientRectangle.Contains(form.routeTestButton.Bounds);
                bool scheduleCardOnFirstScreen = initialViewport.IntersectsWith(scheduleInViewport)
                    && scheduleInViewport.Top >= heroInViewport.Bottom;
                bool inputOnFirstScreen = initialViewport.IntersectsWith(inputInViewport)
                    && inputInViewport.Top >= heroInViewport.Bottom;
                bool splitGeometry = dataSplit != null
                    && dataSplit.Panel1.ClientSize.Width > 0 && dataSplit.Panel1.ClientSize.Height >= dataSplit.Panel1MinSize
                    && dataSplit.Panel2.ClientSize.Width > 0 && dataSplit.Panel2.ClientSize.Height >= dataSplit.Panel2MinSize;

                int advancedHeight = form.leftStack.Controls[1].Height;
                form.ToggleAdvancedSettings();
                PerformLayoutTree(form);
                bool advancedExpands = form.advancedExpanded && form.leftStack.Controls[1].Height > advancedHeight
                    && form.advancedBody.Height > 0;
                bool advancedFieldsReachable = advancedExpands
                    && ScrollIntoView(form.modelBox, form.viewport)
                    && ScrollIntoView(form.retryBox, form.viewport)
                    && ScrollIntoView(form.runWithoutLogonCheck, form.viewport)
                    && ScrollIntoView(form.autoSleepCheck, form.viewport)
                    && ScrollIntoView(form.promptBox, form.viewport);
                form.ToggleAdvancedSettings();
                PerformLayoutTree(form);
                bool advancedCollapses = !form.advancedExpanded && form.leftStack.Controls[1].Height == advancedHeight;

                int routeHeight = form.leftStack.Controls[2].Height;
                form.ToggleRouteDetails();
                PerformLayoutTree(form);
                bool routeExpands = form.routeDetailsExpanded && form.leftStack.Controls[2].Height > routeHeight;
                form.ToggleRouteDetails();
                PerformLayoutTree(form);
                bool routeCollapses = !form.routeDetailsExpanded && form.leftStack.Controls[2].Height == routeHeight;

                int installedHeight = form.rightStack.Controls[1].Height;
                form.ToggleInstalledDetails();
                PerformLayoutTree(form);
                bool installedExpands = form.installedDetailsExpanded && form.rightStack.Controls[1].Height > installedHeight;
                form.ToggleInstalledDetails();
                PerformLayoutTree(form);
                bool installedCollapses = !form.installedDetailsExpanded && form.rightStack.Controls[1].Height == installedHeight;

                form.ToggleResultDetails();
                bool resultExpands = form.resultDetailsExpanded && form.resultDetailsButton.Text == "收起最近尝试详情";
                form.ToggleResultDetails();
                bool resultCollapses = !form.resultDetailsExpanded && form.resultDetailsButton.Text == "查看最近尝试详情";

                form.sequenceSchedulePanel.ScrollControlIntoView(form.saveButton);
                PerformLayoutTree(form);
                Rectangle saveAfterScroll = BoundsInAncestor(form.saveButton, form.sequenceSchedulePanel);
                bool saveReachable = form.sequenceSchedulePanel.AutoScroll
                    && form.sequenceSchedulePanel.ClientRectangle.IntersectsWith(saveAfterScroll);

                form.viewport.ScrollControlIntoView(form.timelineList);
                PerformLayoutTree(form);
                Rectangle timelineAfterScroll = BoundsInAncestor(form.timelineList, form.viewport);
                bool timelineReachable = form.viewport.AutoScroll && form.viewport.ClientRectangle.IntersectsWith(timelineAfterScroll);
                form.viewport.ScrollControlIntoView(form.routeTestButton);
                PerformLayoutTree(form);
                Rectangle routeAfterScroll = BoundsInAncestor(form.routeTestButton, form.viewport);
                bool routeReachable = form.viewport.AutoScroll && form.viewport.ClientRectangle.IntersectsWith(routeAfterScroll);

                Control[] actions = new Control[]
                {
                    form.sequenceDatePicker,
                    form.sequenceTimePicker,
                    form.sequenceNowButton,
                    form.sequenceIntervalHoursBox,
                    form.sequenceIntervalMinutesBox,
                    form.sequenceCountBox,
                    form.extraWakeDatePicker,
                    form.extraWakeTimePicker,
                    form.addExtraWakeButton,
                    form.removeExtraWakeButton,
                    form.saveButton,
                    form.pauseButton,
                    form.routeTestButton,
                    form.routeRefreshButton,
                    form.routeDetailsButton,
                    form.advancedToggleButton,
                    form.installedDetailsButton,
                    form.resultDetailsButton,
                    form.openLogButton,
                    form.deleteTaskButton
                };
                int reachableActions = 0;
                string unreachableActions = "";
                foreach (Control action in actions)
                {
                    string actionEvidence;
                    if (ScrollIntoView(action, form.viewport, out actionEvidence))
                    {
                        reachableActions++;
                    }
                    else
                    {
                        if (unreachableActions.Length > 0) unreachableActions += ",";
                        unreachableActions += actionEvidence;
                    }
                }
                bool allActionsReachable = reachableActions == actions.Length;

                evidence = "client=" + clientWidth + "x" + clientHeight + ",scale=" + scale.ToString("0.00", CultureInfo.InvariantCulture)
                    + ",available=" + available + ",columns=" + form.mainGrid.ColumnCount
                    + ",viewport=" + FormatBounds(form.viewport.ClientRectangle)
                    + ",pageRoot=" + FormatBounds(form.pageRoot.Bounds)
                    + ",mainGrid=" + FormatBounds(form.mainGrid.Bounds)
                    + ",leftStack=" + FormatBounds(form.leftStack.Bounds)
                    + ",rightStack=" + FormatBounds(form.rightStack.Bounds)
                    + ",hero=" + FormatBounds(heroInViewport)
                    + ",scheduleCard=" + FormatBounds(scheduleInViewport)
                    + ",input=" + FormatBounds(inputInViewport)
                    + ",save=" + FormatBounds(saveInSchedule)
                    + ",timeline=" + FormatBounds(timelineInViewport)
                    + ",cardRegions=" + cardRegionsSeparated + "[" + cardRegionEvidence + "]"
                    + ",scheduleCardFirstScreen=" + scheduleCardOnFirstScreen
                    + ",inputFirstScreen=" + inputOnFirstScreen
                    + ",saveReachable=" + saveReachable
                    + ",timelineAfterScroll=" + FormatBounds(timelineAfterScroll)
                    + ",timelineReachable=" + timelineReachable
                    + ",routeAfterScroll=" + FormatBounds(routeAfterScroll)
                    + ",routeReachable=" + routeReachable
                    + ",actions=" + reachableActions + "/" + actions.Length
                    + (unreachableActions.Length == 0 ? "" : ",unreachable=" + unreachableActions)
                    + ",viewportScroll=" + FormatBounds(form.viewport.AutoScrollPosition == Point.Empty
                        ? Rectangle.Empty
                        : new Rectangle(form.viewport.AutoScrollPosition, Size.Empty))
                    + ",checks=" + mode + "/" + contentGeometry + "/" + scheduleInParent + "/" + inputInParent + "/" + saveInParent + "/" + timelineInParent + "/" + routeInParent + "/" + splitGeometry
                    + "/advanced=" + advancedExpands + ":" + advancedCollapses
                    + ":fields=" + advancedFieldsReachable
                    + "/route=" + routeExpands + ":" + routeCollapses
                    + "/installed=" + installedExpands + ":" + installedCollapses
                    + "/result=" + resultExpands + ":" + resultCollapses;

                return mode && contentGeometry && cardRegionsSeparated && scheduleInParent && inputInParent && saveInParent && timelineInParent && routeInParent
                    && scheduleCardOnFirstScreen && inputOnFirstScreen && splitGeometry
                    && advancedExpands && advancedFieldsReachable && advancedCollapses && routeExpands && routeCollapses
                    && installedExpands && installedCollapses && resultExpands && resultCollapses
                    && saveReachable && timelineReachable && routeReachable && allActionsReachable;
            }
        }

        private static bool VerifyCardRegions(MainForm form, out string evidence)
        {
            List<Control> cards = new List<Control>();
            foreach (Control card in form.leftStack.Controls) cards.Add(card);
            SplitContainer split = form.rightStack.Controls.Count > 0 ? form.rightStack.Controls[0] as SplitContainer : null;
            if (split != null)
            {
                if (split.Panel1.Controls.Count > 0) cards.Add(split.Panel1.Controls[0]);
                if (split.Panel2.Controls.Count > 0) cards.Add(split.Panel2.Controls[0]);
            }
            if (form.rightStack.Controls.Count > 1) cards.Add(form.rightStack.Controls[1]);

            bool allSeparated = cards.Count == 6;
            List<string> regions = new List<string>();
            foreach (Control card in cards)
            {
                TableLayoutPanel explicitLayout = card.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
                Control header;
                Control body;
                if (explicitLayout != null)
                {
                    header = explicitLayout.GetControlFromPosition(0, 0);
                    body = explicitLayout.GetControlFromPosition(0, 1);
                }
                else
                {
                    header = card.Controls.OfType<Label>().FirstOrDefault();
                    body = card.Controls.OfType<Panel>().FirstOrDefault();
                }

                Control firstContent = body == null ? null : body.Controls.Cast<Control>()
                    .Where(child => child.Width > 0 && child.Height > 0)
                    .OrderBy(child => child.Top)
                    .FirstOrDefault();
                Rectangle headerInCard = header == null ? Rectangle.Empty : BoundsInAncestor(header, card);
                Rectangle bodyInCard = body == null ? Rectangle.Empty : BoundsInAncestor(body, card);
                Rectangle firstContentInCard = firstContent == null ? Rectangle.Empty : BoundsInAncestor(firstContent, card);
                bool separated = header != null && body != null && firstContent != null
                    && header.Width > 0 && body.Width > 0 && firstContent.Width > 0 && firstContent.Height > 0
                    && !headerInCard.IntersectsWith(bodyInCard)
                    && bodyInCard.Top >= headerInCard.Bottom
                    && firstContentInCard.Top >= headerInCard.Bottom;
                allSeparated = allSeparated && separated;
                string title = header is Label ? ((Label)header).Text : "?";
                regions.Add(title + ":h=" + (header == null ? "missing" : FormatBounds(headerInCard))
                    + ",b=" + (body == null ? "missing" : FormatBounds(bodyInCard))
                    + ",first=" + (firstContent == null ? "missing" : FormatBounds(firstContentInCard))
                    + ",ok=" + separated);
            }
            evidence = string.Join("|", regions.ToArray());
            return allSeparated;
        }

        private static void EnsureControlHandles(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                IntPtr childHandle = child.Handle;
                EnsureControlHandles(child);
            }
        }

        private static void PerformLayoutTree(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                PerformLayoutTree(child);
            }
            parent.PerformLayout();
        }

        private static bool ScrollIntoView(Control control, Panel viewport)
        {
            string evidence;
            return ScrollIntoView(control, viewport, out evidence);
        }

        private static bool ScrollIntoView(Control control, Panel viewport, out string evidence)
        {
            Control parent = control.Parent;
            while (parent != null && parent != viewport)
            {
                ScrollableControl scrollable = parent as ScrollableControl;
                if (scrollable != null && scrollable.AutoScroll) scrollable.ScrollControlIntoView(control);
                parent = parent.Parent;
            }
            if (parent == null) throw new InvalidOperationException("操作控件不在主视口内：" + control.GetType().Name);
            viewport.ScrollControlIntoView(control);
            PerformLayoutTree(viewport);
            Rectangle bounds = BoundsInAncestor(control, viewport);
            bool parentContains = control.Width > 0 && control.Height > 0
                && control.Parent.ClientRectangle.Contains(control.Bounds);
            evidence = (control is Button ? ((Button)control).Text : control.GetType().Name) + "=" + FormatBounds(bounds);
            return parentContains && viewport.ClientRectangle.IntersectsWith(bounds);
        }

        private static Rectangle BoundsInAncestor(Control control, Control ancestor)
        {
            Rectangle bounds = control.Bounds;
            Control parent = control.Parent;
            while (parent != null && parent != ancestor)
            {
                // WinForms updates child Bounds when an AutoScrollPosition changes; applying
                // the scroll offset again would double-count it after ScrollControlIntoView.
                bounds.Offset(parent.Left, parent.Top);
                parent = parent.Parent;
            }
            if (parent == null)
            {
                throw new InvalidOperationException("控件不在目标父容器内：" + control.Name);
            }
            return bounds;
        }

        private static string FormatBounds(Rectangle bounds)
        {
            return bounds.X + "," + bounds.Y + "," + bounds.Width + "x" + bounds.Height;
        }

        private static void StyleInputs(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is TextBox) ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
                if (control is NumericUpDown) ((NumericUpDown)control).BorderStyle = BorderStyle.FixedSingle;
                if (control is ListBox) ((ListBox)control).BorderStyle = BorderStyle.FixedSingle;
                if (control is TextBox || control is NumericUpDown || control is ComboBox || control is DateTimePicker)
                    control.Margin = new Padding(0, 5, 12, 7);
                StyleInputs(control);
            }
        }

        private static Label NewLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 6, 8, 0);
            return label;
        }

        private void ResizeTimelineColumns()
        {
            if (timelineList == null || timelineList.Columns.Count < 3)
            {
                return;
            }
            int available = timelineList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8;
            int statusWidth = Math.Max(132, TextRenderer.MeasureText(TimelineExecutionPolicy.Unknown, timelineList.Font).Width + 18);
            int timeWidth = Math.Min(152, Math.Max(112, available / 3));
            timelineList.Columns[0].Width = statusWidth;
            timelineList.Columns[1].Width = timeWidth;
            timelineList.Columns[2].Width = Math.Max(72, available - statusWidth - timeWidth);
        }

        private static Button NewButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(105, 38);
            button.Padding = new Padding(12, 3, 12, 3);
            button.Cursor = Cursors.Hand;
            button.FlatStyle = FlatStyle.Flat;
            button.Margin = new Padding(0, 0, 8, 0);
            if (primary)
            {
                button.BackColor = Purple;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Purple;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(101, 80, 197);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(87, 69, 174);
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Color.FromArgb(35, 38, 44);
                button.FlatAppearance.BorderColor = Color.FromArgb(220, 225, 233);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 244, 255);
            }
            return button;
        }

        private void LoadState()
        {
            config = JsonStore.LoadConfig();
            legacyDefaultModelMigrationPending = AppConfig.MigrateLegacyDefaultModelToDraft(config);
            modelCatalogSnapshot = RequestModelCatalogService.LoadOrFallback(AppPaths.ModelCatalogPath);
            ReplaceModelCatalogChoices(modelCatalogSnapshot.Models);
            modelBox.Text = config.Model;
            promptBox.Text = config.Prompt;
            retryBox.Value = Math.Min(retryBox.Maximum, Math.Max(retryBox.Minimum, config.RetryCount));
            runWithoutLogonCheck.Checked = config.RunWithoutLogon;
            autoSleepCheck.Checked = config.SleepAfterScheduledWake;
            DateTime sequenceStart;
            if (!AppConfig.TryParseSequenceStart(config.SequenceStart, out sequenceStart))
            {
                sequenceStart = DateTime.Now.AddHours(1);
                sequenceStart = new DateTime(
                    sequenceStart.Year,
                    sequenceStart.Month,
                    sequenceStart.Day,
                    sequenceStart.Hour,
                    sequenceStart.Minute,
                    0);
            }
            sequenceDatePicker.Value = sequenceStart.Date;
            sequenceTimePicker.Value = DateTime.Today.AddHours(sequenceStart.Hour).AddMinutes(sequenceStart.Minute);
            sequenceCountBox.Text = config.SequenceCount.ToString(CultureInfo.InvariantCulture);
            sequenceIntervalHoursBox.Value = Math.Min(
                sequenceIntervalHoursBox.Maximum,
                Math.Max(sequenceIntervalHoursBox.Minimum, config.SequenceIntervalMinutes / 60));
            sequenceIntervalMinutesBox.Value = config.SequenceIntervalMinutes % 60;
            extraWakeList.Items.Clear();
            foreach (string value in AppConfig.NormalizeDateTimes(config.ExtraWakeTimes))
            {
                extraWakeList.Items.Add(value);
            }
            UpdateSequenceSummary();
            hasUnsavedChanges = legacyDefaultModelMigrationPending;
            RefreshStatus();
        }

        private void RefreshRouteStatus()
        {
            if (routeStatusLabel == null || routeModelLabel == null || routeDetailLabel == null)
            {
                return;
            }
            RouteSnapshot route = CodexRouteDetector.Detect();
            RunRecord last = JsonStore.LoadLastRun();
            routeStatusLabel.Text = RouteTestPresentation.LinkStatus(last);
            routeStatusLabel.ForeColor = last == null
                ? Color.FromArgb(160, 100, 20)
                : last.Success
                    ? Color.FromArgb(28, 125, 80)
                    : Color.FromArgb(180, 55, 55);
            routeModelLabel.Text = RouteTestPresentation.FinalModelLabel(last);

            StringBuilder detail = new StringBuilder();
            detail.Append("当前配置：").Append(route.Summary);
            if (!string.IsNullOrWhiteSpace(route.RiskMessage))
            {
                detail.Append("\r\n").Append(route.RiskMessage);
            }
            if (!string.IsNullOrWhiteSpace(route.Diagnostic))
            {
                detail.Append("\r\n检测提示：").Append(route.Diagnostic);
            }

            if (last == null)
            {
                detail.Append("\r\n尚未发送真实请求；刷新配置不会调用模型。");
            }
            else
            {
                detail.Append("\r\n最近测试：").Append(FormatIso(last.FinishedAt));
                detail.Append("；退出码=").Append(last.ExitCode);
                detail.Append("；实际链路=").Append(RouteTestPresentation.RouteSummary(last));
                detail.Append("；provider=").Append(string.IsNullOrWhiteSpace(last.Provider) ? "未识别" : last.Provider);
                if (!string.IsNullOrWhiteSpace(last.UpstreamProvider))
                {
                    detail.Append("；上游=").Append(last.UpstreamProvider);
                }
                if (!last.Success && !string.IsNullOrWhiteSpace(last.Error))
                {
                    detail.Append("；失败点=").Append(last.Error);
                }
            }
            routeDetailLabel.Text = detail.ToString();
        }

        private void RefreshStatus()
        {
            RefreshRouteStatus();
            WindowsTaskStatus taskStatus = WindowsTaskService.GetStatus();
            DateTime now = DateTime.Now;
            RunRecord last = JsonStore.LoadLastRun();
            timelineSavedUtc = File.GetLastWriteTimeUtc(AppPaths.ConfigPath);
            timelineHistory = TimelineExecutionPolicy.InstalledMatches(config ?? new AppConfig(), taskStatus)
                ? TimelineHistoryStore.ReadForTimeline(config, taskStatus, last) : new List<TimelineExecution>();
            UpdateInstalledTaskPresentation(taskStatus);
            SetPlanDetails(taskStatus, last, null);
            UpdateWakeReadiness(WakeSystemReader.Read(), taskStatus);
            if (hasUnsavedChanges) UpdateDraftTimeline(now);
            else UpdateTimeline(now);
            bool successUnavailable;
            SuccessSnapshot recentSuccess = RecentSuccessStore.LoadForApplication(last, out successUnavailable);
            SetRecentResultSummary(recentSuccess, successUnavailable, last, last == null && File.Exists(AppPaths.LastRunPath));
            SetLatestAttemptDetails(last);
        }

        private void UpdateInstalledTaskPresentation(WindowsTaskStatus taskStatus)
        {
            WakeTaskInspection inspection = WakeReadinessPolicy.InspectTask(taskStatus);
            installedTaskExists = inspection.Exists == true;
            installedTaskEnabled = inspection.Enabled == true;
            installedNextRunTime = inspection.Exists == true ? taskStatus.NextRunTime : null;
            if (inspection.Exists == true && inspection.Enabled == true)
            {
                statusLabel.Text = "●  Windows 计划已启用";
                statusLabel.ForeColor = inspection.WakeToRun == true ? Color.FromArgb(51, 149, 119) : Color.FromArgb(174, 119, 35);
                statusLabel.BackColor = inspection.WakeToRun == true ? Color.FromArgb(237, 248, 242) : Color.FromArgb(250, 244, 230);
            }
            else if (inspection.Exists == true && inspection.Enabled == false)
            {
                statusLabel.Text = "●  Windows 计划已暂停";
                statusLabel.ForeColor = Color.FromArgb(174, 119, 35);
                statusLabel.BackColor = Color.FromArgb(250, 244, 230);
            }
            else if (inspection.Missing)
            {
                statusLabel.Text = "●  尚未安装计划";
                statusLabel.ForeColor = Muted;
                statusLabel.BackColor = Color.FromArgb(239, 236, 245);
            }
            else
            {
                statusLabel.Text = "●  Windows 计划状态未能确认";
                statusLabel.ForeColor = Muted;
                statusLabel.BackColor = Color.FromArgb(239, 236, 245);
            }
            installedSummaryLabel.Text = BuildInstalledTaskSummary(taskStatus, inspection);
            installedDetailsButton.Enabled = true;
            saveButton.Text = inspection.Exists == true ? "更新并启用计划  ↗" : "保存并启用计划  ↗";
        }

        private void SetPlanDetails(WindowsTaskStatus taskStatus, RunRecord last, WakeReadinessView wake)
        {
            string plan = taskStatus.Exists
                ? DescribeInstalledPlan(taskStatus.NextRunTime, taskStatus, last)
                    + (string.IsNullOrWhiteSpace(taskStatus.RuntimeDetailsError) ? string.Empty : "\r\n" + taskStatus.RuntimeDetailsError)
                : "任务名：" + TaskXml.TaskName + "\r\n" + (string.IsNullOrWhiteSpace(taskStatus.Error)
                    ? "修改后点击“保存并启用计划”会安装此计划。"
                    : taskStatus.Error + "\r\n请检查 Windows 任务计划程序权限后重试。");
            planDetailLabel.Text = plan + "\r\n\r\n唤醒条件详情\r\n"
                + (wake == null ? "请重新检查以读取当前系统状态。" : wake.Details);
        }

        private static string BuildInstalledTaskSummary(WindowsTaskStatus taskStatus, WakeTaskInspection inspection)
        {
            if (inspection.Missing)
                return "Windows 中尚无本程序的计划任务。保存后只会管理一条 " + TaskXml.TaskName + "。";
            if (inspection.Exists != true)
                return "Windows 计划状态未能确认；请重新检查任务计划程序后再保存。\r\n"
                    + (string.IsNullOrWhiteSpace(inspection.Error) ? "" : inspection.Error);

            string summary = "计划已存在 · " + (inspection.Enabled.HasValue
                ? (inspection.Enabled.Value ? "已启用" : "已暂停") : "启用状态未能确认")
                + "\r\n" + (inspection.WakeToRun.HasValue
                    ? (inspection.WakeToRun.Value ? "允许唤醒电脑" : "未开启 WakeToRun") : "WakeToRun 状态未能确认")
                + " · " + WakeReadinessPolicy.TaskLogon(inspection.LogonType)
                + "\r\nWindows 下次运行：" + FormatTaskTime(taskStatus.NextRunTime)
                + "\r\nWindows 上次运行：" + FormatTaskTime(taskStatus.LastRunTime);
            if (!string.IsNullOrWhiteSpace(inspection.Error)) summary += "\r\n" + inspection.Error;
            return summary;
        }

        private void UpdateWakeReadiness(WakeSystemSnapshot power, WindowsTaskStatus taskStatus)
        {
            WakeReadinessView view = WakeReadinessPolicy.Evaluate(power, taskStatus);
            wakeReadinessLabel.Text = "唤醒条件：" + view.MainSummary + "\r\n检查时间：" + view.CheckedAt;
            openPowerSettingsButton.Visible = view.ShowPowerSettings;
            wakeRefreshButton.Enabled = true;
            if (installedDetailsExpanded) SetPlanDetails(taskStatus, JsonStore.LoadLastRun(), view);
        }

        private void RefreshWakeReadiness()
        {
            if (!wakeRefreshButton.Enabled) return;
            wakeRefreshButton.Enabled = false;
            wakeReadinessLabel.Text = "正在读取 Windows 当前供电、电源方案和计划状态…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                WindowsTaskStatus taskStatus = null;
                WakeSystemSnapshot power = null;
                string error = "";
                try
                {
                    taskStatus = WindowsTaskService.GetStatus();
                    power = WakeSystemReader.Read();
                }
                catch { error = "Windows 唤醒条件暂不可确认；请重试或在任务计划程序核对。"; }

                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            wakeReadinessLabel.Text = error;
                            openPowerSettingsButton.Visible = true;
                        }
                        else
                        {
                            UpdateInstalledTaskPresentation(taskStatus);
                            UpdateWakeReadiness(power, taskStatus);
                            if (hasUnsavedChanges) UpdateDraftTimeline(DateTime.Now);
                            else UpdateTimeline(DateTime.Now);
                        }
                        wakeRefreshButton.Enabled = true;
                        LayoutResponsive();
                    });
                }
                catch { }
            });
        }

        private void OpenPowerSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.PowerOptions") { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("无法打开电源设置。请在 Windows 设置中搜索“电源和睡眠”，或从控制面板打开“电源选项”。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SetLatestAttemptDetails(RunRecord last)
        {
            if (last == null)
            {
                resultBox.Text = "暂无运行记录。";
                return;
            }
            StringBuilder result = new StringBuilder();
            result.AppendLine("请求状态：" + RouteTestPresentation.LinkStatus(last));
            result.AppendLine("模式：" + last.Mode);
            result.AppendLine("开始：" + FormatIso(last.StartedAt));
            result.AppendLine("结束：" + FormatIso(last.FinishedAt));
            result.AppendLine("请求模型：" + last.Model);
            result.AppendLine(RouteTestPresentation.FinalModelLabel(last));
            result.AppendLine("实际链路：" + RouteTestPresentation.RouteSummary(last));
            result.AppendLine("实际 provider：" + (string.IsNullOrWhiteSpace(last.Provider) ? "未识别" : last.Provider));
            if (last.HasReliableResetTime && !string.IsNullOrWhiteSpace(last.ResetAtLocal))
            {
                result.AppendLine("额度重置时间（仅用于未保存预填）：" + last.ResetAtLocal);
            }
            else if (!string.IsNullOrWhiteSpace(last.ResetTimeDiagnostic))
            {
                result.AppendLine("额度重置时间：" + last.ResetTimeDiagnostic);
            }
            if (!string.IsNullOrWhiteSpace(last.UpstreamProvider))
            {
                result.AppendLine("CC Switch 上游：" + last.UpstreamProvider + (string.IsNullOrWhiteSpace(last.UpstreamTarget) ? string.Empty : "（" + last.UpstreamTarget + "）"));
            }
            else if (!string.IsNullOrWhiteSpace(last.UpstreamEvidence))
            {
                result.AppendLine("CC Switch 上游：未确认");
            }
            if (!last.Success)
            {
                result.AppendLine("失败点：" + (string.IsNullOrWhiteSpace(last.Error) ? "未记录" : last.Error));
                result.AppendLine("下一步：" + RouteTestPresentation.FailureNextStep(last));
            }
            result.AppendLine("尝试：" + last.AttemptCount);
            result.AppendLine("退出码：" + last.ExitCode);
            if (!string.IsNullOrWhiteSpace(last.SleepStatus))
            {
                result.AppendLine("自动睡眠：" + last.SleepStatus);
            }
            if (!string.IsNullOrWhiteSpace(last.WakeEvidence))
            {
                result.AppendLine("唤醒证据：" + last.WakeEvidence);
            }
            if (!string.IsNullOrWhiteSpace(last.Output))
            {
                result.AppendLine();
                result.AppendLine("输出：");
                result.AppendLine(last.Output);
            }
            if (!string.IsNullOrWhiteSpace(last.Error))
            {
                result.AppendLine();
                result.AppendLine("错误：");
                result.AppendLine(last.Error);
            }
            resultBox.Text = result.ToString();
        }

        private void SetRecentResultSummary(SuccessSnapshot success, bool unavailable, RunRecord last, bool attemptUnavailable)
        {
            string summary = RecentSuccessStore.Summary(success, unavailable, last, attemptUnavailable);
            if (resultSummaryLabel.Text != summary)
            {
                ((Panel)resultSummaryLabel.Parent).AutoScrollPosition = Point.Empty;
                resultSummaryLabel.Text = summary;
            }
            resultSummaryLabel.ForeColor = Ink;
            resultDetailsButton.Enabled = last != null;
            resultDetailsButton.Text = resultDetailsExpanded ? "收起最近尝试详情" : "查看最近尝试详情";
            UpdateResultPanelMinimum();
        }

        private void UpdateResultPanelMinimum()
        {
            if (resultsSplit == null || adjustingResultsSplit) return;
            adjustingResultsSplit = true;
            try
            {
                Control body = resultDetailsButton.Parent;
                int chrome = Math.Max(0, resultsSplit.Panel2.ClientSize.Height - body.Height);
                int minimum = Math.Max((int)(resultsSplit.Panel1MinSize * 190.0 / 180.0),
                    resultSummaryLabel.Height + resultDetailsButton.Height + chrome + 8);
                int total = resultsSplit.Panel1MinSize + resultsSplit.SplitterWidth + minimum;
                if (resultsSplit.Height < total) resultsSplit.Height = total;
                if (resultsSplit.Height - resultsSplit.SplitterDistance - resultsSplit.SplitterWidth < minimum)
                    resultsSplit.SplitterDistance = Math.Max(resultsSplit.Panel1MinSize, resultsSplit.Height - minimum - resultsSplit.SplitterWidth);
                resultsSplit.Panel2MinSize = minimum;
            }
            finally { adjustingResultsSplit = false; }
        }

        private string DescribeInstalledPlan(DateTime? next, WindowsTaskStatus taskStatus, RunRecord last)
        {
            WakeTaskInspection inspection = WakeReadinessPolicy.InspectTask(taskStatus);
            StringBuilder detail = new StringBuilder();
            detail.Append("任务名：").Append(TaskXml.TaskName).Append("（唯一一条）");
            detail.Append("\r\n任务计划程序实际状态：").Append(inspection.Enabled.HasValue ? (inspection.Enabled.Value ? "已启用" : "已暂停") : "未能确认");
            detail.Append("；允许唤醒：").Append(inspection.WakeToRun.HasValue ? (inspection.WakeToRun.Value ? "是" : "否") : "未能确认");
            detail.Append("；登录：").Append(WakeReadinessPolicy.TaskLogon(inspection.LogonType));
            detail.Append("；Windows 下次运行：").Append(FormatTaskTime(taskStatus.NextRunTime));
            detail.Append("；Windows 上次运行：").Append(FormatTaskTime(taskStatus.LastRunTime));
            detail.Append("\r\n计划：");
            detail.Append("序列 ").Append(config.SequenceCount).Append(" 次");
            if (config.SequenceCount > 1)
            {
                detail.Append("，间隔 ").Append(FormatInterval(SequenceSchedule.GetIntervalMinutes(config)));
            }
            else if (config.SequenceCount == 0)
            {
                detail.Append("（不生成序列，仅执行额外唤醒）");
            }
            if (next.HasValue)
            {
                detail.Append("；下次：").Append(next.Value.ToString("yyyy-MM-dd HH:mm"));
            }
            DateTime? lastPlanned = SequenceSchedule.LastRun(config);
            if (lastPlanned.HasValue)
            {
                detail.Append("；序列最后一次：").Append(lastPlanned.Value.ToString("yyyy-MM-dd HH:mm"));
            }
            int extraCount = AppConfig.NormalizeDateTimes(config.ExtraWakeTimes).Count;
            if (extraCount > 0)
            {
                detail.Append("；额外唤醒 ").Append(extraCount).Append(" 条（每条一次）");
            }
            int mergedCount = SchedulePlanner.Build(config, DateTime.MinValue, 1).Count;
            detail.Append("；合并去重后共 ").Append(mergedCount).Append(" 条");
            if (config.LegacyDailyScheduleDetected && config.Times.Count > 0)
            {
                detail.Append("\r\n警告：检测到旧版每日时间 ").Append(string.Join("、", config.Times.ToArray()))
                    .Append("，新版不会执行这些时间；请用日期时间重新添加。");
            }
            detail.Append("\r\n唤醒电脑：").Append(taskStatus.WakeToRun ? "已开启" : "未确认");
            detail.Append("；登录要求：").Append(taskStatus.CanRunWithoutLogon ? "无需登录也可运行" : "需要登录 Windows");
            if (!taskStatus.CanRunWithoutLogon)
            {
                detail.Append("（Windows 更新重启后可能漏跑）");
            }
            detail.Append("；成功后自动睡眠：").Append(config.SleepAfterScheduledWake ? "已开启" : "未开启");
            if (last != null && !string.IsNullOrWhiteSpace(last.FinishedAt))
            {
                detail.Append("；最近请求：").Append(FormatIso(last.FinishedAt))
                    .Append(last.Success ? "（成功）" : "（失败）");
            }
            detail.Append("\r\n修改后点击“更新并启用”只会覆盖更新这一条任务，不会新增第二条。");
            return detail.ToString();
        }

        private void UpdateTimeline(DateTime now)
        {
            UpdateTimeline(now, config ?? new AppConfig(), false);
        }

        private void UpdateDraftTimeline(DateTime now)
        {
            hasUnsavedChanges = true;
            UpdateTimeline(now, ReadForm(config != null && config.Enabled), true);
        }

        private void UpdateTimeline(DateTime now, AppConfig preview, bool draft)
        {
            if (timelineList == null || timelineSummaryLabel == null)
            {
                return;
            }

            List<PlannedRun> plan = SchedulePlanner.Build(preview, now, 14);
            string timelineKey = TimelineExecutionPolicy.PlanKey(preview, timelineSavedUtc);
            PlannedRun heroNext = plan.FirstOrDefault(delegate(PlannedRun item) { return item.Time > now; });
            DateTime? displayNext = draft ? (heroNext == null ? (DateTime?)null : heroNext.Time)
                : installedTaskExists && installedTaskEnabled ? installedNextRunTime
                : heroNext == null ? (DateTime?)null : heroNext.Time;
            nextRunLabel.Text = displayNext.HasValue ? displayNext.Value.ToString("HH:mm")
                : installedTaskExists && installedTaskEnabled ? "待 Windows 确认" : "尚未安排";
            nextRunDateLabel.Text = displayNext.HasValue ? displayNext.Value.ToString("yyyy年MM月dd日 · dddd") : string.Empty;
            heroHintLabel.Text = draft
                ? DraftHint()
                : installedTaskExists && installedTaskEnabled
                    ? (installedNextRunTime.HasValue ? "这是 Windows 已安装计划的实际下次运行时间。" : "Windows 尚未提供实际时间，请展开计划信息查看状态。")
                    : installedTaskExists ? "当前 Windows 计划已暂停；显示的是保存配置中的下一次时间。"
                    : "Windows 中没有已安装计划；显示的是本地配置预览。";
            timelineList.BeginUpdate();
            try
            {
                timelineList.Items.Clear();
                PlannedRun next = plan.FirstOrDefault(delegate(PlannedRun item) { return item.Time > now; });
                int visibleLimit = 200;
                foreach (PlannedRun item in plan.Take(visibleLimit))
                {
                    string state;
                    Color color;
                    state = TimelineExecutionPolicy.State(item, timelineKey, timelineHistory, now, draft);
                    if (state == "执行成功")
                        color = Color.FromArgb(28, 125, 80);
                    else if (state == "执行失败")
                        color = Color.FromArgb(180, 55, 55);
                    else if (state == "执行中")
                        color = Purple;
                    else if (item.Time <= now)
                    {
                        color = Color.FromArgb(120, 124, 132);
                    }
                    else if (next != null && item.Time == next.Time && item.Source == next.Source)
                    {
                        state = "下一次";
                        color = Purple;
                    }
                    else
                    {
                        state = "待执行";
                        color = Color.FromArgb(113, 106, 128);
                    }
                    ListViewItem row = new ListViewItem(state);
                    row.ForeColor = color;
                    row.SubItems.Add(item.Time.ToString("yyyy-MM-dd HH:mm"));
                    row.SubItems.Add(item.Source);
                    timelineList.Items.Add(row);
                }
                if (plan.Count == 0)
                {
                    timelineSummaryLabel.Text = draft
                        ? "尚未保存的计划预览为空。"
                        : "本地配置中没有未来触发时间；添加序列或额外唤醒后可预览。";
                }
                else if (plan.Count > visibleLimit)
                {
                    timelineSummaryLabel.Text = (draft ? "当前未保存的计划预览共 " : "本地配置预览共 ")
                        + plan.Count + " 条；为保持清晰，仅显示前 " + visibleLimit + " 条。";
                }
                else
                {
                    int futureCount = plan.Count(delegate(PlannedRun item) { return item.Time > now; });
                    timelineSummaryLabel.Text = (draft ? "尚未保存的预览（不显示执行结果）共 " : "当前保存计划共 ")
                        + plan.Count + " 条；未来 " + futureCount + " 条。";
                }
                timelineSummaryLabel.Height = Math.Max(28, TextRenderer.MeasureText(timelineSummaryLabel.Text, timelineSummaryLabel.Font,
                    new Size(Math.Max(100, timelineSummaryLabel.Width), int.MaxValue), TextFormatFlags.WordBreak).Height + 6);
            }
            finally
            {
                timelineList.EndUpdate();
            }
            ResizeTimelineColumns();
        }

        private static string FormatTaskTime(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "—";
        }

        private static string FormatIso(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : value;
        }

        private DateTime ReadSequenceStart()
        {
            return sequenceDatePicker.Value.Date
                .AddHours(sequenceTimePicker.Value.Hour)
                .AddMinutes(sequenceTimePicker.Value.Minute);
        }

        private int ReadSequenceIntervalMinutes()
        {
            return ((int)sequenceIntervalHoursBox.Value * 60) + (int)sequenceIntervalMinutesBox.Value;
        }

        private static string FormatInterval(int totalMinutes)
        {
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            if (hours > 0 && minutes > 0)
            {
                return hours + " 小时 " + minutes + " 分钟";
            }
            if (hours > 0)
            {
                return hours + " 小时";
            }
            return minutes + " 分钟";
        }

        private void AddExtraWake()
        {
            DateTime value = extraWakeDatePicker.Value.Date
                .AddHours(extraWakeTimePicker.Value.Hour)
                .AddMinutes(extraWakeTimePicker.Value.Minute);
            if (value <= DateTime.Now)
            {
                MessageBox.Show("插入唤醒时间必须晚于当前时间。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string formatted = value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            List<string> values = AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>().Concat(new[] { formatted }));
            extraWakeList.Items.Clear();
            foreach (string item in values)
            {
                extraWakeList.Items.Add(item);
            }
            extraWakeList.SelectedItem = formatted;
            UpdateSequenceSummary();
            UpdateDraftTimeline(DateTime.Now);
        }

        private void RemoveExtraWake()
        {
            if (extraWakeList.SelectedIndex >= 0)
            {
                extraWakeList.Items.RemoveAt(extraWakeList.SelectedIndex);
                UpdateSequenceSummary();
                UpdateDraftTimeline(DateTime.Now);
            }
        }

        private void SetSequenceStartToNow()
        {
            if (!SequenceStartShortcut.IsEnabled())
            {
                return;
            }

            DateTime nextMinute;
            try
            {
                nextMinute = SequenceStartShortcut.NextMinute(DateTime.Now);
                sequenceDatePicker.Value = nextMinute.Date;
                sequenceTimePicker.Value = DateTime.Today.AddHours(nextMinute.Hour).AddMinutes(nextMinute.Minute);
                UpdateDraftTimeline(DateTime.Now);
            }
            catch (ArgumentOutOfRangeException)
            {
                MessageBox.Show(
                    "无法设置当前时间，请检查 Windows 日期和时间设置。",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void UpdateSequenceSummary()
        {
            int count;
            bool validCount = SequenceCountInput.TryParse(sequenceCountBox.Text, out count);
            if (!validCount)
            {
                sequenceDatePicker.Enabled = true;
                sequenceTimePicker.Enabled = true;
                sequenceSummaryLabel.Text = "请输入大于等于 0 的整数；次数包含首次执行。";
                sequenceSummaryLabel.ForeColor = Color.FromArgb(160, 100, 20);
                sequenceNowButton.Enabled = SequenceStartShortcut.IsEnabled();
                sequenceIntervalHoursBox.Enabled = true;
                sequenceIntervalMinutesBox.Enabled = true;
                return;
            }

            int extraCount = extraWakeList == null ? 0 : AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>()).Count;
            AppConfig preview = new AppConfig();
            preview.SequenceStart = ReadSequenceStart().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            preview.SequenceCount = count;
            preview.SequenceIntervalMinutes = ReadSequenceIntervalMinutes();
            preview.ExtraWakeTimes = extraWakeList == null
                ? new List<string>()
                : AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>());
            int totalCount = SchedulePlanner.Build(preview, DateTime.MinValue, 1).Count;
            if (count == 0)
            {
                sequenceDatePicker.Enabled = true;
                sequenceTimePicker.Enabled = true;
                sequenceNowButton.Enabled = SequenceStartShortcut.IsEnabled();
                sequenceIntervalHoursBox.Enabled = true;
                sequenceIntervalMinutesBox.Enabled = true;
                sequenceSummaryLabel.Text = "序列执行 0 次；额外唤醒 " + extraCount + " 条，去重后共 " + totalCount + " 次，每条只执行一次。";
                sequenceSummaryLabel.ForeColor = extraCount > 0
                    ? Color.FromArgb(80, 84, 92)
                    : Color.FromArgb(160, 100, 20);
                return;
            }

            sequenceDatePicker.Enabled = true;
            sequenceTimePicker.Enabled = true;
            sequenceIntervalHoursBox.Enabled = count > 1;
            sequenceIntervalMinutesBox.Enabled = count > 1;
            sequenceNowButton.Enabled = true;
            DateTime? last = SequenceSchedule.LastRun(preview);
            if (!last.HasValue)
            {
                sequenceSummaryLabel.Text = "执行次数过大，无法计算最后一次时间。";
                sequenceSummaryLabel.ForeColor = Color.FromArgb(180, 55, 55);
                return;
            }

            sequenceSummaryLabel.Text = count == 1
                ? "序列执行 1 次（首次时间）；额外唤醒 " + extraCount + " 条，去重后共 " + totalCount + " 次。"
                : "每隔 " + FormatInterval(preview.SequenceIntervalMinutes) + "，序列执行 " + count + " 次（含首次），最后一次：" + last.Value.ToString("yyyy-MM-dd HH:mm") + "；额外唤醒 " + extraCount + " 条，去重后共 " + totalCount + " 次。";
            sequenceSummaryLabel.ForeColor = Color.FromArgb(80, 84, 92);
        }

        private AppConfig ReadForm(bool enabled)
        {
            AppConfig value = new AppConfig();
            value.ScheduleVersion = config == null ? string.Empty : config.ScheduleVersion;
            value.Times = new List<string>();
            value.Model = modelBox.Text.Trim();
            // Keep the legacy field only for JSON compatibility. The active
            // request path always follows the current Codex configuration.
            value.TargetMode = TargetModes.FollowCurrent;
            value.Prompt = promptBox.Text.Trim();
            value.RetryCount = (int)retryBox.Value;
            value.SleepAfterScheduledWake = autoSleepCheck.Checked;
            value.RunWithoutLogon = runWithoutLogonCheck.Checked;
            value.Enabled = enabled;
            value.FiniteSequenceEnabled = true;
            value.SequenceStart = ReadSequenceStart().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            int sequenceCount;
            value.SequenceCount = SequenceCountInput.TryParse(sequenceCountBox.Text, out sequenceCount)
                ? sequenceCount
                : 0;
            value.SequenceIntervalMinutes = ReadSequenceIntervalMinutes();
            value.ExtraWakeTimes = AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>());
            return AppConfig.Normalize(value);
        }

        private bool ValidateForm(bool requireTime)
        {
            return ValidateForm(requireTime, true);
        }

        private bool ValidateForm(bool requireTime, bool showMessages)
        {
            int count;
            if (!SequenceCountInput.TryParse(sequenceCountBox.Text, out count))
            {
                return RejectValidation("序列执行次数必须是大于等于 0 的整数。次数包含首次执行。", sequenceCountBox, showMessages);
            }

            if (count > 1 && ReadSequenceIntervalMinutes() <= 0)
            {
                return RejectValidation("执行间隔至少需要 1 分钟。", sequenceIntervalMinutesBox, showMessages);
            }

            if (count > 0)
            {
                AppConfig preview = new AppConfig();
                preview.SequenceStart = ReadSequenceStart().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                preview.SequenceCount = count;
                preview.SequenceIntervalMinutes = ReadSequenceIntervalMinutes();
                if (!SequenceSchedule.LastRun(preview).HasValue)
                {
                    return RejectValidation("执行次数过大，最后一次时间超出了 Windows 可表示范围。", sequenceCountBox, showMessages);
                }
                if (requireTime && ReadSequenceStart() <= DateTime.Now)
                {
                    return RejectValidation("首次执行时间必须晚于当前时间。未来日期没有天数上限。", sequenceDatePicker, showMessages);
                }
            }

            List<string> extras = AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>());
            if (requireTime)
            {
                bool hasSequence = count > 0;
                if (!hasSequence && extras.Count == 0)
                {
                    return RejectValidation("请至少设置 1 次序列执行或添加一个额外唤醒时间。", sequenceCountBox, showMessages);
                }
                foreach (string value in extras)
                {
                    DateTime extra;
                    if (AppConfig.TryParseDateTime(value, out extra) && extra <= DateTime.Now)
                    {
                        return RejectValidation("额外唤醒时间必须晚于当前时间：" + value, extraWakeList, showMessages);
                    }
                }
            }
            return ValidateRequestInputs(showMessages);
        }

        private bool ValidateRequestInputs()
        {
            return ValidateRequestInputs(true);
        }

        private bool ValidateRequestInputs(bool showMessages)
        {
            if (string.IsNullOrWhiteSpace(modelBox.Text))
            {
                return RejectValidation("请选择或填写一个 Codex 模型。", modelBox, showMessages);
            }
            if (string.IsNullOrWhiteSpace(promptBox.Text))
            {
                return RejectValidation("触发消息不能为空。", promptBox, showMessages);
            }
            return true;
        }

        private bool RejectValidation(string message, Control target, bool showMessages)
        {
            if (showMessages)
            {
                MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (target != null) target.Focus();
            }
            return false;
        }

        private ManualTestScheduleSnapshot CaptureManualTestScheduleSnapshot()
        {
            return new ManualTestScheduleSnapshot
            {
                SequenceCountText = sequenceCountBox.Text,
                SequenceStart = ReadSequenceStart(),
                SequenceIntervalMinutes = ReadSequenceIntervalMinutes(),
                ExtraWakeTimes = AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>())
            };
        }

        private async Task TestNow()
        {
            if (!ValidateRequestInputs())
            {
                return;
            }
            AppConfig testConfig = ReadForm(config != null && config.Enabled);
            ManualTestScheduleSnapshot scheduleDraftAtConfirmation = CaptureManualTestScheduleSnapshot();
            RouteSnapshot route = CodexRouteDetector.Detect();
            string confirmMessage = "测试会沿当前 Codex / CC Switch 配置立即发送一条真实请求，并消耗相应用量。\r\n\r\n"
                + "当前配置摘要：" + route.Summary + "\r\n"
                + "程序不会修改 provider、模型或路由。"
                + "\r\n\r\n是否继续？";
            DialogResult confirm = MessageBox.Show(
                confirmMessage,
                "测试请求链路",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true, "正在发送真实 Codex 请求，并刷新可用模型…");
            Task<RequestModelCatalogRefreshResult> modelCatalogTask;
            try
            {
                modelCatalogTask = Task.Run(delegate
                {
                    return RequestModelCatalogService.Refresh(CodexLocator.Find(), AppPaths.ModelCatalogPath);
                });
            }
            catch
            {
                modelCatalogTask = Task.FromResult(new RequestModelCatalogRefreshResult());
            }
            RunRecord result = await Task.Run(delegate { return BackgroundRunner.Execute("manual", false, testConfig); });
            RefreshStatus();
            ResetScheduleAdjustment scheduleAdjustment = ManualTestSchedulePolicy.EvaluateReset(
                result,
                scheduleDraftAtConfirmation,
                CaptureManualTestScheduleSnapshot(),
                DateTime.Now);
            if (scheduleAdjustment.Applied)
            {
                sequenceDatePicker.Value = scheduleAdjustment.LocalStart.Date;
                sequenceTimePicker.Value = DateTime.Today
                    .AddHours(scheduleAdjustment.LocalStart.Hour)
                    .AddMinutes(scheduleAdjustment.LocalStart.Minute);
                UpdateSequenceSummary();
                UpdateDraftTimeline(DateTime.Now);
                sequenceSummaryLabel.Text += "\r\n" + scheduleAdjustment.Message;
                sequenceSummaryLabel.ForeColor = Color.FromArgb(160, 100, 20);
            }

            SetBusy(false, result.Success ? "链路已联通。"
                : "链路未联通：" + (string.IsNullOrWhiteSpace(result.Error) ? "请查看日志" : result.Error));
            string catalogFeedback = null;
            if (modelCatalogTask.IsCompleted)
            {
                RequestModelCatalogRefreshResult catalogResult;
                try
                {
                    catalogResult = modelCatalogTask.Result;
                }
                catch
                {
                    catalogResult = new RequestModelCatalogRefreshResult();
                }
                catalogFeedback = ApplyModelCatalogRefreshResult(catalogResult, testConfig.Model);
            }
            using (TestResultDialog dialog = new TestResultDialog(result, scheduleAdjustment, catalogFeedback, OpenLog))
            {
                if (!modelCatalogTask.IsCompleted)
                    QueueModelCatalogFeedback(modelCatalogTask, testConfig.Model, dialog);
                else if (catalogFeedback == null)
                    QueueModelCatalogFeedback(modelCatalogTask, testConfig.Model, dialog);
                if (dialog.ShowDialog(this) == DialogResult.Yes)
                    NavigateToPlan();
            }
        }

        private void NavigateToPlan()
        {
            ScrollIntoView(sequenceDatePicker, viewport);
            sequenceDatePicker.Focus();
        }

        private void SaveAndEnable()
        {
            if (!ValidateForm(true))
            {
                return;
            }
            AppConfig candidate = ReadForm(true);
            candidate.ScheduleVersion = Guid.NewGuid().ToString("N");
            OperationResult result = WindowsTaskService.InstallOrUpdate(candidate);
            if (result.Success)
            {
                candidate.RequestModelSelectionConfirmed = true;
                config = candidate;
                JsonStore.SaveConfig(config);
                hasUnsavedChanges = false;
                legacyDefaultModelMigrationPending = false;
            }
            MessageBox.Show(result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void PauseSchedule()
        {
            OperationResult result = WindowsTaskService.Pause();
            bool draftSaved = SaveMaintenanceDraft();
            MessageBox.Show(MaintenanceResultMessage(result.Message, draftSaved), Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void DeleteSchedule()
        {
            DialogResult confirm = MessageBox.Show(
                "只删除本程序创建的 Windows 计划任务。配置和日志会保留，是否继续？",
                "删除计划",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
            OperationResult result = WindowsTaskService.Delete();
            bool draftSaved = SaveMaintenanceDraft();
            MessageBox.Show(MaintenanceResultMessage(result.Message, draftSaved), Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
        }

        private static string MaintenanceResultMessage(string resultMessage, bool draftSaved)
        {
            return draftSaved
                ? resultMessage
                : resultMessage + "\r\n\r\n当前执行次数为空或无效，因此没有保存表单草稿。请填写大于等于 0 的整数后再保存。";
        }

        private bool SaveMaintenanceDraft()
        {
            int sequenceCount;
            if (!SequenceCountInput.TryParse(sequenceCountBox.Text, out sequenceCount))
            {
                hasUnsavedChanges = true;
                return false;
            }

            AppConfig candidate = ReadForm(false);
            candidate.SequenceCount = sequenceCount;
            if (legacyDefaultModelMigrationPending)
            {
                candidate.Model = AppConfig.LegacyDefaultRequestModel;
                candidate.RequestModelSelectionConfirmed = false;
            }
            else
            {
                candidate.RequestModelSelectionConfirmed = true;
            }
            config = candidate;
            JsonStore.SaveConfig(config);
            hasUnsavedChanges = legacyDefaultModelMigrationPending;
            return true;
        }

        private void OpenLog()
        {
            if (!File.Exists(AppPaths.LogPath))
            {
                File.WriteAllText(AppPaths.LogPath, string.Empty, new UTF8Encoding(false));
            }
            Process.Start("notepad.exe", ProcessRunner.Quote(AppPaths.LogPath));
        }

        private void SetBusy(bool busy, string message)
        {
            saveButton.Enabled = !busy;
            pauseButton.Enabled = !busy;
            deleteTaskButton.Enabled = !busy;
            if (routeRefreshButton != null) routeRefreshButton.Enabled = !busy;
            if (routeTestButton != null) routeTestButton.Enabled = !busy;
            UseWaitCursor = busy;
            statusLabel.Text = message;
            statusLabel.ForeColor = busy ? Color.FromArgb(42, 105, 210) : Color.FromArgb(80, 84, 92);
        }
    }
}
