using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
[assembly: AssemblyVersion("0.12.0.0")]
[assembly: AssemblyFileVersion("0.12.0.0")]
[assembly: AssemblyInformationalVersion("0.12.0")]

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
                    RunRecord record = BackgroundRunner.Execute("scheduled", true);
                    return record.Success ? 0 : 1;
                }

                if (args.Length > 0 && string.Equals(args[0], "--self-check", StringComparison.OrdinalIgnoreCase))
                {
                    string reportPath = args.Length > 1 ? args[1] : Path.Combine(AppPaths.BaseDirectory, "self-check.txt");
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

    internal static class AppPaths
    {
        public static readonly string BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaWaker");

        public static readonly string ConfigPath = Path.Combine(BaseDirectory, "config.json");
        public static readonly string LastRunPath = Path.Combine(BaseDirectory, "last-run.json");
        public static readonly string LogPath = Path.Combine(BaseDirectory, "activity.log");
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
        // Kept only so older JSON files can still be read. The unified scheduler
        // intentionally never uses the legacy daily-time or mode fields.
        public List<string> Times { get; set; }
        public string Model { get; set; }
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

        public AppConfig()
        {
            Times = new List<string>();
            Model = "gpt-5.6-luna";
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
                config.Model = "gpt-5.6-luna";
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
                return Serializer.Deserialize<RunRecord>(File.ReadAllText(AppPaths.LastRunPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "读取最近运行结果失败：" + ex.Message);
                return null;
            }
        }

        public static void SaveLastRun(RunRecord record)
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(AppPaths.LastRunPath, Serializer.Serialize(record), new UTF8Encoding(false));
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

    internal static class SequenceStartShortcut
    {
        public static bool IsEnabled(string sequenceCountText)
        {
            int count;
            return int.TryParse(
                (sequenceCountText ?? string.Empty).Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count)
                && count > 0;
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

        public static RunRecord Execute(string mode, bool requireEnabled, AppConfig requestedConfig = null)
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
            xml.AppendLine("      <Arguments>--run-scheduled</Arguments>");
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

        public bool CanRunWithoutLogon
        {
            get { return string.Equals(LogonType, "Password", StringComparison.OrdinalIgnoreCase); }
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
                return new WindowsTaskStatus
                {
                    Exists = true,
                    Enabled = ReadBoolean(settings, "Enabled", true),
                    WakeToRun = ReadBoolean(settings, "WakeToRun", false),
                    LogonType = ReadText(principal, "LogonType"),
                    UserId = ReadText(principal, "UserId")
                };
            }
            catch (Exception ex)
            {
                return new WindowsTaskStatus { Exists = true, Error = "无法读取 Windows 计划状态：" + ex.Message };
            }
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

                bool shortcutStateOk = !SequenceStartShortcut.IsEnabled("0")
                    && !SequenceStartShortcut.IsEnabled(" ")
                    && !SequenceStartShortcut.IsEnabled("abc")
                    && SequenceStartShortcut.IsEnabled("1")
                    && SequenceStartShortcut.IsEnabled(" 2 ");
                lines.Add("SequenceStartShortcutState=" + shortcutStateOk);
                success = success && shortcutStateOk;

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

    internal sealed class MainForm : Form
    {
        private readonly Label statusLabel;
        private readonly Label planDetailLabel;
        private readonly Label routeStatusLabel;
        private readonly Label routeModelLabel;
        private readonly Label routeDetailLabel;
        private readonly Button routeRefreshButton;
        private readonly Button routeTestButton;
        private readonly Panel sequenceSchedulePanel;
        private readonly DateTimePicker sequenceDatePicker;
        private readonly DateTimePicker sequenceTimePicker;
        private readonly Button sequenceNowButton;
        private readonly TextBox sequenceCountBox;
        private readonly NumericUpDown sequenceIntervalHoursBox;
        private readonly NumericUpDown sequenceIntervalMinutesBox;
        private readonly Label sequenceSummaryLabel;
        private readonly DateTimePicker extraWakeDatePicker;
        private readonly DateTimePicker extraWakeTimePicker;
        private readonly ListBox extraWakeList;
        private readonly ListView timelineList;
        private readonly Label timelineSummaryLabel;
        private readonly ComboBox modelBox;
        private readonly TextBox promptBox;
        private readonly NumericUpDown retryBox;
        private readonly CheckBox runWithoutLogonCheck;
        private readonly CheckBox autoSleepCheck;
        private readonly TextBox resultBox;
        private readonly Button saveButton;
        private readonly Button pauseButton;
        private readonly Button deleteTaskButton;
        private AppConfig config;

        public MainForm()
        {
            Text = "Codex 额度唤醒器";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 680);
            Size = new Size(920, 900);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(247, 248, 250);

            Panel viewport = new Panel();
            viewport.Dock = DockStyle.Fill;
            viewport.AutoScroll = true;
            Controls.Add(viewport);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Top;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Padding = new Padding(24);
            root.ColumnCount = 1;
            root.RowCount = 9;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 136F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 285F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewport.Controls.Add(root);

            Label title = new Label();
            title.Text = "Codex 额度唤醒器";
            title.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            title.AutoSize = true;
            title.Margin = new Padding(0, 0, 0, 4);
            root.Controls.Add(title, 0, 0);

            Panel planCard = new Panel();
            planCard.Dock = DockStyle.Fill;
            planCard.BackColor = Color.FromArgb(238, 246, 240);
            planCard.BorderStyle = BorderStyle.FixedSingle;
            planCard.Padding = new Padding(14, 10, 14, 10);
            planCard.Margin = new Padding(0, 0, 0, 14);
            root.Controls.Add(planCard, 0, 1);

            TableLayoutPanel planLayout = new TableLayoutPanel();
            planLayout.Dock = DockStyle.Fill;
            planLayout.ColumnCount = 1;
            planLayout.RowCount = 3;
            planLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            planLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            planLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            planCard.Controls.Add(planLayout);

            Label planCaption = new Label();
            planCaption.Text = "当前启用计划（Windows 实际状态）";
            planCaption.AutoSize = true;
            planCaption.ForeColor = Color.FromArgb(70, 74, 82);
            planCaption.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            planLayout.Controls.Add(planCaption, 0, 0);

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            statusLabel.ForeColor = Color.FromArgb(28, 125, 80);
            planLayout.Controls.Add(statusLabel, 0, 1);

            planDetailLabel = new Label();
            planDetailLabel.Dock = DockStyle.Fill;
            planDetailLabel.ForeColor = Color.FromArgb(70, 74, 82);
            planDetailLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            planDetailLabel.Padding = new Padding(0, 4, 0, 0);
            planLayout.Controls.Add(planDetailLabel, 0, 2);

            Panel routeCard = new Panel();
            routeCard.Dock = DockStyle.Fill;
            routeCard.BackColor = Color.FromArgb(244, 247, 252);
            routeCard.BorderStyle = BorderStyle.FixedSingle;
            routeCard.Padding = new Padding(14, 10, 14, 10);
            routeCard.Margin = new Padding(0, 0, 0, 14);
            root.Controls.Add(routeCard, 0, 2);

            TableLayoutPanel routeLayout = new TableLayoutPanel();
            routeLayout.Dock = DockStyle.Fill;
            routeLayout.ColumnCount = 1;
            routeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            routeLayout.RowCount = 5;
            routeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routeCard.Controls.Add(routeLayout);

            Label routeCaption = NewLabel("请求链路测试");
            routeCaption.ForeColor = Color.FromArgb(70, 74, 82);
            routeLayout.Controls.Add(routeCaption, 0, 0);

            routeStatusLabel = new Label();
            routeStatusLabel.AutoSize = true;
            routeStatusLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            routeStatusLabel.Margin = new Padding(0, 2, 0, 2);
            routeLayout.Controls.Add(routeStatusLabel, 0, 1);

            routeModelLabel = new Label();
            routeModelLabel.AutoSize = true;
            routeModelLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            routeModelLabel.Margin = new Padding(0, 0, 0, 2);
            routeModelLabel.ForeColor = Color.FromArgb(35, 38, 44);
            routeLayout.Controls.Add(routeModelLabel, 0, 2);

            routeDetailLabel = new Label();
            routeDetailLabel.AutoSize = true;
            routeDetailLabel.MaximumSize = new Size(700, 0);
            routeDetailLabel.Margin = new Padding(0, 2, 0, 4);
            routeDetailLabel.ForeColor = Color.FromArgb(70, 74, 82);
            routeDetailLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            routeLayout.Controls.Add(routeDetailLabel, 0, 3);

            FlowLayoutPanel routeActions = new FlowLayoutPanel();
            routeActions.AutoSize = true;
            routeActions.WrapContents = false;
            routeActions.Margin = new Padding(0, 4, 0, 0);
            routeRefreshButton = NewButton("刷新配置", false);
            routeRefreshButton.Click += delegate { RefreshRouteStatus(); };
            routeActions.Controls.Add(routeRefreshButton);
            routeTestButton = NewButton("测试请求链路", true);
            routeTestButton.Click += async delegate { await TestNow(); };
            routeActions.Controls.Add(routeTestButton);
            routeLayout.Controls.Add(routeActions, 0, 4);

            GroupBox scheduleGroup = NewGroup("执行计划");
            root.Controls.Add(scheduleGroup, 0, 3);
            TableLayoutPanel scheduleRoot = new TableLayoutPanel();
            scheduleRoot.Dock = DockStyle.Fill;
            scheduleRoot.Padding = new Padding(12, 8, 12, 10);
            scheduleRoot.ColumnCount = 1;
            scheduleRoot.RowCount = 1;
            scheduleRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            scheduleGroup.Controls.Add(scheduleRoot);

            sequenceSchedulePanel = new Panel();
            sequenceSchedulePanel.Dock = DockStyle.Fill;
            scheduleRoot.Controls.Add(sequenceSchedulePanel, 0, 0);

            TableLayoutPanel sequenceLayout = new TableLayoutPanel();
            sequenceLayout.Dock = DockStyle.Fill;
            sequenceLayout.ColumnCount = 5;
            sequenceLayout.RowCount = 5;
            sequenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sequenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            sequenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sequenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            sequenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sequenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sequenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sequenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sequenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sequenceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            sequenceSchedulePanel.Controls.Add(sequenceLayout);

            sequenceLayout.Controls.Add(NewLabel("首次日期"), 0, 0);
            sequenceDatePicker = new DateTimePicker();
            sequenceDatePicker.Format = DateTimePickerFormat.Custom;
            sequenceDatePicker.CustomFormat = "yyyy-MM-dd";
            sequenceDatePicker.Dock = DockStyle.Fill;
            sequenceDatePicker.ValueChanged += delegate { UpdateSequenceSummary(); };
            sequenceLayout.Controls.Add(sequenceDatePicker, 1, 0);

            sequenceLayout.Controls.Add(NewLabel("时间"), 2, 0);
            sequenceTimePicker = new DateTimePicker();
            sequenceTimePicker.Format = DateTimePickerFormat.Custom;
            sequenceTimePicker.CustomFormat = "HH:mm";
            sequenceTimePicker.ShowUpDown = true;
            sequenceTimePicker.Dock = DockStyle.Fill;
            sequenceTimePicker.ValueChanged += delegate { UpdateSequenceSummary(); };
            sequenceLayout.Controls.Add(sequenceTimePicker, 3, 0);

            sequenceNowButton = NewButton("现在", false);
            sequenceNowButton.MinimumSize = new Size(72, 34);
            sequenceNowButton.Width = 72;
            sequenceNowButton.Click += delegate { SetSequenceStartToNow(); };
            sequenceLayout.Controls.Add(sequenceNowButton, 4, 0);

            sequenceLayout.Controls.Add(NewLabel("执行间隔"), 0, 1);
            FlowLayoutPanel intervalPanel = new FlowLayoutPanel();
            intervalPanel.AutoSize = true;
            intervalPanel.WrapContents = false;
            intervalPanel.Dock = DockStyle.Fill;
            sequenceIntervalHoursBox = new NumericUpDown();
            sequenceIntervalHoursBox.Minimum = 0;
            sequenceIntervalHoursBox.Maximum = 168;
            sequenceIntervalHoursBox.Value = 5;
            sequenceIntervalHoursBox.Width = 72;
            sequenceIntervalHoursBox.ValueChanged += delegate { UpdateSequenceSummary(); };
            intervalPanel.Controls.Add(sequenceIntervalHoursBox);
            intervalPanel.Controls.Add(NewLabel("小时"));

            sequenceIntervalMinutesBox = new NumericUpDown();
            sequenceIntervalMinutesBox.Minimum = 0;
            sequenceIntervalMinutesBox.Maximum = 59;
            sequenceIntervalMinutesBox.Value = 10;
            sequenceIntervalMinutesBox.Width = 72;
            sequenceIntervalMinutesBox.ValueChanged += delegate { UpdateSequenceSummary(); };
            intervalPanel.Controls.Add(sequenceIntervalMinutesBox);
            intervalPanel.Controls.Add(NewLabel("分钟"));
            sequenceLayout.Controls.Add(intervalPanel, 1, 1);

            sequenceLayout.Controls.Add(NewLabel("序列执行次数（含首次，可为 0）"), 2, 1);
            sequenceCountBox = new TextBox();
            sequenceCountBox.Dock = DockStyle.Fill;
            sequenceCountBox.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            sequenceCountBox.TextChanged += delegate { UpdateSequenceSummary(); };
            sequenceLayout.Controls.Add(sequenceCountBox, 3, 1);

            sequenceSummaryLabel = new Label();
            sequenceSummaryLabel.AutoSize = true;
            sequenceSummaryLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            sequenceSummaryLabel.ForeColor = Color.FromArgb(80, 84, 92);
            sequenceSummaryLabel.Margin = new Padding(0, 8, 0, 0);
            sequenceLayout.SetColumnSpan(sequenceSummaryLabel, 5);
            sequenceLayout.Controls.Add(sequenceSummaryLabel, 0, 2);

            FlowLayoutPanel extraWakeActions = new FlowLayoutPanel();
            extraWakeActions.AutoSize = true;
            extraWakeActions.WrapContents = false;
            extraWakeActions.Margin = new Padding(0, 8, 0, 4);
            sequenceLayout.SetColumnSpan(extraWakeActions, 5);
            sequenceLayout.Controls.Add(extraWakeActions, 0, 3);
            extraWakeActions.Controls.Add(NewLabel("插入额外唤醒"));
            extraWakeDatePicker = new DateTimePicker();
            extraWakeDatePicker.Format = DateTimePickerFormat.Custom;
            extraWakeDatePicker.CustomFormat = "yyyy-MM-dd";
            extraWakeDatePicker.Width = 112;
            extraWakeActions.Controls.Add(extraWakeDatePicker);
            extraWakeTimePicker = new DateTimePicker();
            extraWakeTimePicker.Format = DateTimePickerFormat.Custom;
            extraWakeTimePicker.CustomFormat = "HH:mm";
            extraWakeTimePicker.ShowUpDown = true;
            extraWakeTimePicker.Width = 74;
            extraWakeActions.Controls.Add(extraWakeTimePicker);
            Button addExtraWake = NewButton("插入", false);
            addExtraWake.Click += delegate { AddExtraWake(); };
            extraWakeActions.Controls.Add(addExtraWake);
            Button removeExtraWake = NewButton("删除选中", false);
            removeExtraWake.Click += delegate { RemoveExtraWake(); };
            extraWakeActions.Controls.Add(removeExtraWake);

            extraWakeList = new ListBox();
            extraWakeList.Dock = DockStyle.Fill;
            extraWakeList.IntegralHeight = false;
            extraWakeList.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            sequenceLayout.SetColumnSpan(extraWakeList, 5);
            sequenceLayout.Controls.Add(extraWakeList, 0, 4);

            TableLayoutPanel options = new TableLayoutPanel();
            options.Dock = DockStyle.Fill;
            options.AutoSize = true;
            options.RowCount = 4;
            options.ColumnCount = 4;
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            options.Margin = new Padding(0, 12, 0, 8);
            root.Controls.Add(options, 0, 4);

            options.Controls.Add(NewLabel("模型"), 0, 0);
            modelBox = new ComboBox();
            modelBox.DropDownStyle = ComboBoxStyle.DropDown;
            modelBox.Items.AddRange(new object[] { "gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.6-sol" });
            modelBox.Dock = DockStyle.Fill;
            options.Controls.Add(modelBox, 1, 0);

            options.Controls.Add(NewLabel("最多尝试次数（含首次）"), 2, 0);
            retryBox = new NumericUpDown();
            retryBox.Minimum = 1;
            retryBox.Maximum = 5;
            retryBox.Value = 3;
            retryBox.Dock = DockStyle.Fill;
            options.Controls.Add(retryBox, 3, 0);

            runWithoutLogonCheck = new CheckBox();
            runWithoutLogonCheck.Text = "重启或注销后仍运行";
            runWithoutLogonCheck.AutoSize = true;
            runWithoutLogonCheck.Margin = new Padding(0, 9, 0, 0);
            runWithoutLogonCheck.CheckedChanged += delegate
            {
                if (runWithoutLogonCheck.Checked)
                {
                    runWithoutLogonCheck.AccessibleName = "重启或注销后仍运行；保存时需要输入当前 Windows 帐户密码，不能使用 PIN、指纹或人脸";
                }
                else
                {
                    runWithoutLogonCheck.AccessibleName = "未选择；普通睡眠唤醒不受影响，Windows 更新重启后可能漏跑";
                }
            };
            options.SetColumnSpan(runWithoutLogonCheck, 4);
            options.Controls.Add(runWithoutLogonCheck, 0, 1);

            Label runWithoutLogonHint = NewLabel("普通睡眠唤醒不需要；开启后保存时需输入当前 Windows 帐户密码，开机 PIN、指纹和人脸不可用。");
            runWithoutLogonHint.ForeColor = Color.FromArgb(100, 104, 112);
            runWithoutLogonHint.MaximumSize = new Size(780, 0);
            runWithoutLogonHint.Margin = new Padding(0, 3, 0, 0);
            options.SetColumnSpan(runWithoutLogonHint, 4);
            options.Controls.Add(runWithoutLogonHint, 0, 2);

            autoSleepCheck = new CheckBox();
            autoSleepCheck.Text = "任务唤醒电脑后，成功完成时自动睡眠";
            autoSleepCheck.AutoSize = true;
            autoSleepCheck.Margin = new Padding(0, 9, 0, 0);
            options.SetColumnSpan(autoSleepCheck, 4);
            options.Controls.Add(autoSleepCheck, 0, 3);

            Label executionHint = NewLabel("执行期间防睡眠与超时：程序自动管理；成功会提前结束，失败后交回 Windows 电源策略。");
            executionHint.ForeColor = Color.FromArgb(100, 104, 112);
            executionHint.Margin = new Padding(0, 6, 0, 0);
            options.SetColumnSpan(executionHint, 4);
            options.Controls.Add(executionHint, 0, 4);

            GroupBox promptGroup = NewGroup("触发消息");
            promptGroup.Height = 90;
            promptGroup.Dock = DockStyle.Top;
            promptGroup.Margin = new Padding(0, 4, 0, 12);
            root.Controls.Add(promptGroup, 0, 5);
            promptBox = new TextBox();
            promptBox.Multiline = true;
            promptBox.Dock = DockStyle.Fill;
            promptBox.Margin = new Padding(12);
            promptBox.ScrollBars = ScrollBars.Vertical;
            promptBox.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            promptGroup.Padding = new Padding(12);
            promptGroup.Controls.Add(promptBox);

            FlowLayoutPanel mainActions = new FlowLayoutPanel();
            mainActions.AutoSize = true;
            mainActions.WrapContents = true;
            mainActions.Margin = new Padding(0, 0, 0, 12);
            root.Controls.Add(mainActions, 0, 6);

            saveButton = NewButton("保存并启用", true);
            saveButton.Click += delegate { SaveAndEnable(); };
            mainActions.Controls.Add(saveButton);
            pauseButton = NewButton("暂停计划", false);
            pauseButton.Click += delegate { PauseSchedule(); };
            mainActions.Controls.Add(pauseButton);
            deleteTaskButton = NewButton("删除计划", false);
            deleteTaskButton.Click += delegate { DeleteSchedule(); };
            mainActions.Controls.Add(deleteTaskButton);
            Button openLog = NewButton("打开日志", false);
            openLog.Click += delegate { OpenLog(); };
            mainActions.Controls.Add(openLog);

            GroupBox timelineGroup = NewGroup("当前执行序列");
            timelineGroup.Dock = DockStyle.Fill;
            Panel timelinePanel = new Panel();
            timelinePanel.Dock = DockStyle.Fill;
            timelinePanel.Padding = new Padding(8, 6, 8, 6);
            timelineGroup.Controls.Add(timelinePanel);
            timelineSummaryLabel = new Label();
            timelineSummaryLabel.Dock = DockStyle.Top;
            timelineSummaryLabel.Height = 24;
            timelineSummaryLabel.ForeColor = Color.FromArgb(80, 84, 92);
            timelinePanel.Controls.Add(timelineSummaryLabel);
            timelineList = new ListView();
            timelineList.Dock = DockStyle.Fill;
            timelineList.View = View.Details;
            timelineList.FullRowSelect = true;
            timelineList.GridLines = true;
            timelineList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            timelineList.Columns.Add("状态", 88);
            timelineList.Columns.Add("触发时间", 165);
            timelineList.Columns.Add("来源", 360);
            timelineList.Resize += delegate { ResizeTimelineColumns(); };
            timelinePanel.Controls.Add(timelineList);

            GroupBox resultGroup = NewGroup("最近一次结果");
            resultGroup.Dock = DockStyle.Fill;
            resultBox = new TextBox();
            resultBox.Multiline = true;
            resultBox.ReadOnly = true;
            resultBox.ScrollBars = ScrollBars.Both;
            resultBox.Dock = DockStyle.Fill;
            resultBox.BackColor = Color.White;
            resultBox.Font = new Font("Consolas", 9F);
            resultGroup.Padding = new Padding(12);
            resultGroup.Controls.Add(resultBox);

            SplitContainer dataSplit = new SplitContainer();
            dataSplit.Dock = DockStyle.Fill;
            dataSplit.Orientation = Orientation.Horizontal;
            dataSplit.IsSplitterFixed = false;
            dataSplit.SplitterWidth = 8;
            dataSplit.SplitterDistance = 210;
            dataSplit.Panel1MinSize = 110;
            dataSplit.Panel2MinSize = 80;
            dataSplit.Margin = new Padding(0, 0, 0, 10);
            dataSplit.Panel1.Controls.Add(timelineGroup);
            dataSplit.Panel2.Controls.Add(resultGroup);
            root.Controls.Add(dataSplit, 0, 7);

            Label footer = new Label();
            footer.Text = "后台请求使用 read-only 沙盒并验证本次链路；程序不会读取或保存 ChatGPT 凭据。";
            footer.AutoSize = true;
            footer.ForeColor = Color.FromArgb(100, 104, 112);
            footer.Margin = new Padding(0, 12, 0, 0);
            root.Controls.Add(footer, 0, 8);

            Load += delegate { LoadState(); };
        }

        private static GroupBox NewGroup(string title)
        {
            GroupBox group = new GroupBox();
            group.Text = title;
            group.Dock = DockStyle.Fill;
            group.BackColor = Color.White;
            group.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            return group;
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
            int sourceWidth = timelineList.ClientSize.Width - 88 - 165 - SystemInformation.VerticalScrollBarWidth - 8;
            timelineList.Columns[2].Width = Math.Max(180, sourceWidth);
        }

        private static Button NewButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(105, 34);
            button.FlatStyle = FlatStyle.Flat;
            button.Margin = new Padding(0, 0, 8, 0);
            if (primary)
            {
                button.BackColor = Color.FromArgb(42, 105, 210);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(42, 105, 210);
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Color.FromArgb(35, 38, 44);
                button.FlatAppearance.BorderColor = Color.FromArgb(190, 194, 202);
            }
            return button;
        }

        private void LoadState()
        {
            config = JsonStore.LoadConfig();
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
            DateTime? next = SchedulePlanner.NextRun(config, now);
            RunRecord last = JsonStore.LoadLastRun();
            if (taskStatus.Exists && taskStatus.Enabled && next.HasValue)
            {
                statusLabel.Text = taskStatus.CanRunWithoutLogon
                    ? "已启用 · 无需登录也可运行，Windows 会按计划尝试唤醒电脑"
                    : "已启用 · 需要登录 Windows 才能运行";
                statusLabel.ForeColor = Color.FromArgb(28, 125, 80);
                planDetailLabel.Text = DescribeInstalledPlan(next, taskStatus, last);
            }
            else if (taskStatus.Exists && taskStatus.Enabled)
            {
                statusLabel.Text = config.LegacyDailyScheduleDetected
                    ? "已启用 · 检测到旧版每日计划，等待你更新"
                    : taskStatus.CanRunWithoutLogon
                        ? "已启用 · 无需登录也可运行，但当前没有待执行时间"
                        : "已启用 · 需要登录 Windows 才能运行，但当前没有待执行时间";
                statusLabel.ForeColor = Color.FromArgb(160, 100, 20);
                planDetailLabel.Text = DescribeInstalledPlan(null, taskStatus, last)
                    + "\r\n当前没有可计算的下一次触发；请检查计划后点击“更新并启用”。";
            }
            else if (taskStatus.Exists)
            {
                statusLabel.Text = "已暂停 · Windows 不会自动执行";
                statusLabel.ForeColor = Color.FromArgb(160, 100, 20);
                planDetailLabel.Text = "任务名：" + TaskXml.TaskName + "（唯一一条）\r\n点击“更新并启用”会覆盖更新这条任务，不会新增第二条。";
            }
            else
            {
                statusLabel.Text = "未启用 · Windows 中没有本程序的计划任务";
                statusLabel.ForeColor = Color.FromArgb(80, 84, 92);
                planDetailLabel.Text = "先确认计划内容，再点击“保存并启用”。程序只会创建一条 " + TaskXml.TaskName + "。";
            }

            saveButton.Text = taskStatus.Exists ? "更新并启用" : "保存并启用";
            UpdateTimeline(now);
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

        private string DescribeInstalledPlan(DateTime? next, WindowsTaskStatus taskStatus, RunRecord last)
        {
            StringBuilder detail = new StringBuilder();
            detail.Append("任务名：").Append(TaskXml.TaskName).Append("（唯一一条）");
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
            UpdateTimeline(now, ReadForm(config != null && config.Enabled), true);
        }

        private void UpdateTimeline(DateTime now, AppConfig preview, bool draft)
        {
            if (timelineList == null || timelineSummaryLabel == null)
            {
                return;
            }

            List<PlannedRun> plan = SchedulePlanner.Build(preview, now, 14);
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
                    if (item.Time <= now)
                    {
                        state = "已到期";
                        color = Color.FromArgb(120, 124, 132);
                    }
                    else if (next != null && item.Time == next.Time && item.Source == next.Source)
                    {
                        state = "下一次";
                        color = Color.FromArgb(28, 125, 80);
                    }
                    else
                    {
                        state = "待执行";
                        color = Color.FromArgb(55, 87, 140);
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
                        : "尚未保存可展示的计划。修改计划后点击“保存并启用”即可在此查看。";
                }
                else if (plan.Count > visibleLimit)
                {
                    timelineSummaryLabel.Text = (draft ? "当前未保存的计划预览共 " : "当前已保存计划共 ")
                        + plan.Count + " 条；为保持清晰，仅显示前 " + visibleLimit + " 条。";
                }
                else
                {
                    int futureCount = plan.Count(delegate(PlannedRun item) { return item.Time > now; });
                    timelineSummaryLabel.Text = (draft ? "当前未保存的计划预览共 " : "当前已保存计划共 ")
                        + plan.Count + " 条；待执行 " + futureCount + " 条。绿色行是下一次触发。";
                }
            }
            finally
            {
                timelineList.EndUpdate();
            }
            ResizeTimelineColumns();
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
            if (!SequenceStartShortcut.IsEnabled(sequenceCountBox.Text))
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
            bool validCount = int.TryParse(sequenceCountBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= 0;
            if (!validCount)
            {
                sequenceSummaryLabel.Text = "请输入大于等于 0 的整数；次数包含首次执行。";
                sequenceSummaryLabel.ForeColor = Color.FromArgb(160, 100, 20);
                sequenceNowButton.Enabled = false;
                sequenceIntervalHoursBox.Enabled = false;
                sequenceIntervalMinutesBox.Enabled = false;
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
                sequenceNowButton.Enabled = false;
                sequenceIntervalHoursBox.Enabled = false;
                sequenceIntervalMinutesBox.Enabled = false;
                sequenceSummaryLabel.Text = "序列执行 0 次；额外唤醒 " + extraCount + " 条，去重后共 " + totalCount + " 次，每条只执行一次。";
                sequenceSummaryLabel.ForeColor = extraCount > 0
                    ? Color.FromArgb(80, 84, 92)
                    : Color.FromArgb(160, 100, 20);
                return;
            }

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
            value.SequenceCount = int.TryParse(
                sequenceCountBox.Text.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sequenceCount)
                ? sequenceCount
                : 0;
            value.SequenceIntervalMinutes = ReadSequenceIntervalMinutes();
            value.ExtraWakeTimes = AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>());
            return AppConfig.Normalize(value);
        }

        private bool ValidateForm(bool requireTime)
        {
            int count;
            if (!int.TryParse(sequenceCountBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 0)
            {
                MessageBox.Show("序列执行次数必须是大于等于 0 的整数。次数包含首次执行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                sequenceCountBox.Focus();
                return false;
            }

            if (count > 1 && ReadSequenceIntervalMinutes() <= 0)
            {
                MessageBox.Show("执行间隔至少需要 1 分钟。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                sequenceIntervalMinutesBox.Focus();
                return false;
            }

            if (count > 0)
            {
                AppConfig preview = new AppConfig();
                preview.SequenceStart = ReadSequenceStart().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                preview.SequenceCount = count;
                preview.SequenceIntervalMinutes = ReadSequenceIntervalMinutes();
                if (!SequenceSchedule.LastRun(preview).HasValue)
                {
                    MessageBox.Show("执行次数过大，最后一次时间超出了 Windows 可表示范围。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    sequenceCountBox.Focus();
                    return false;
                }
                if (requireTime && ReadSequenceStart() <= DateTime.Now)
                {
                    MessageBox.Show("首次执行时间必须晚于当前时间。未来日期没有天数上限。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            List<string> extras = AppConfig.NormalizeDateTimes(extraWakeList.Items.Cast<string>());
            if (requireTime)
            {
                bool hasSequence = count > 0;
                if (!hasSequence && extras.Count == 0)
                {
                    MessageBox.Show("请至少设置 1 次序列执行或添加一个额外唤醒时间。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                foreach (string value in extras)
                {
                    DateTime extra;
                    if (AppConfig.TryParseDateTime(value, out extra) && extra <= DateTime.Now)
                    {
                        MessageBox.Show("额外唤醒时间必须晚于当前时间：" + value, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(modelBox.Text))
            {
                MessageBox.Show("请选择或填写一个 Codex 模型。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(promptBox.Text))
            {
                MessageBox.Show("触发消息不能为空。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private async Task TestNow()
        {
            if (!ValidateForm(false))
            {
                return;
            }
            AppConfig testConfig = ReadForm(config != null && config.Enabled);
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

            SetBusy(true, "正在发送真实 Codex 请求…");
            RunRecord result = await Task.Run(delegate { return BackgroundRunner.Execute("manual", false, testConfig); });
            RefreshStatus();
            ResetScheduleAdjustment scheduleAdjustment = ResetSchedulePlanner.Evaluate(
                result,
                testConfig.SequenceCount,
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

            string resultMessage;
            MessageBoxIcon resultIcon;
            string busyMessage;
            if (!result.Success)
            {
                resultMessage = "链路未联通。\r\n\r\n"
                    + "失败点：" + (string.IsNullOrWhiteSpace(result.Error) ? "未记录" : result.Error) + "\r\n"
                    + "下一步：" + RouteTestPresentation.FailureNextStep(result)
                    + "\r\n\r\n" + scheduleAdjustment.Message;
                resultIcon = MessageBoxIcon.Error;
                busyMessage = "链路未联通：" + (string.IsNullOrWhiteSpace(result.Error) ? "请查看日志" : result.Error);
            }
            else
            {
                resultMessage = "链路已联通。\r\n\r\n"
                    + RouteTestPresentation.FinalModelLabel(result) + "\r\n"
                    + "路由摘要：" + RouteTestPresentation.RouteSummary(result);
                if (!string.IsNullOrWhiteSpace(result.Provider))
                {
                    resultMessage += "\r\nprovider：" + result.Provider;
                }
                if (!string.IsNullOrWhiteSpace(result.UpstreamProvider))
                {
                    resultMessage += "\r\n实际上游：" + result.UpstreamProvider;
                }
                if (RouteTestPresentation.FinalModelValue(result) == "未识别")
                {
                    resultMessage += "\r\n\r\n最终模型证据不足，请打开日志查看本次结构化响应。";
                }
                resultMessage += "\r\n\r\n" + scheduleAdjustment.Message;
                resultIcon = MessageBoxIcon.Information;
                busyMessage = "链路已联通。";
            }
            SetBusy(false, busyMessage);
            MessageBox.Show(resultMessage, "测试请求链路", MessageBoxButtons.OK, resultIcon);
        }

        private void SaveAndEnable()
        {
            if (!ValidateForm(true))
            {
                return;
            }
            AppConfig candidate = ReadForm(true);
            OperationResult result = WindowsTaskService.InstallOrUpdate(candidate);
            if (result.Success)
            {
                config = candidate;
                JsonStore.SaveConfig(config);
            }
            MessageBox.Show(result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void PauseSchedule()
        {
            OperationResult result = WindowsTaskService.Pause();
            config = ReadForm(false);
            JsonStore.SaveConfig(config);
            MessageBox.Show(result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
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
            config = ReadForm(false);
            JsonStore.SaveConfig(config);
            MessageBox.Show(result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            RefreshStatus();
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
