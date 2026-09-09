using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FreshWorld.Core
{
    public enum ScheduleMode { DailyTimes = 0, GameDays = 1 }

    public sealed class ScheduleSettings
    {
        public ScheduleMode Mode { get; }
        public bool AutomaticEnabled { get; }
        public IReadOnlyList<TimeSpan> DailyTimes { get; }
        public TimeZoneInfo TimeZone { get; }
        public double GameDayInterval { get; }
        public bool RunMissedOnWorldStart { get; }
        internal string Fingerprint { get; }
        private readonly HashSet<TimeSpan> vegetationTimes;

        public ScheduleSettings(ScheduleMode mode, string dailyTimesCsv, string timeZoneId,
            double gameDayInterval, string vegetationDailyTimesCsv = "*", bool runMissedOnWorldStart = false,
            bool automaticEnabled = true)
        {
            if (!Enum.IsDefined(typeof(ScheduleMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            Mode = mode;
            AutomaticEnabled = automaticEnabled;
            var useDailyTimes = automaticEnabled && mode == ScheduleMode.DailyTimes;
            var useGameDays = automaticEnabled && mode == ScheduleMode.GameDays;
            if (useGameDays) ValidateInterval(gameDayInterval);
            GameDayInterval = useGameDays ? gameDayInterval : 24;
            RunMissedOnWorldStart = automaticEnabled && runMissedOnWorldStart;
            DailyTimes = Array.AsReadOnly(useDailyTimes ? ParseTimes(dailyTimesCsv, false) : Array.Empty<TimeSpan>());
            TimeZone = useDailyTimes ? ResolveTimeZone(timeZoneId) : TimeZoneInfo.Utc;
            vegetationTimes = useDailyTimes ? ParseVegetationTimes(DailyTimes, vegetationDailyTimesCsv) : new HashSet<TimeSpan>();
            var activeSettings = mode == ScheduleMode.DailyTimes
                ? string.Join("|", TimeZone.Id, string.Join(",", DailyTimes.Select(FormatTime)),
                    string.Join(",", vegetationTimes.OrderBy(t => t).Select(FormatTime)))
                : mode == ScheduleMode.GameDays ? gameDayInterval.ToString("R", CultureInfo.InvariantCulture) : "";
            Fingerprint = Hash(automaticEnabled ? "active-v2|" + mode + "|" + activeSettings : "automatic-disabled-v1").Substring(0, 16);
        }

        internal bool IncludeVegetation(TimeSpan slot) => vegetationTimes.Contains(slot);
        internal static string FormatTime(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static void ValidateInterval(double interval)
        {
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval), "Game day interval must be finite and greater than zero.");
        }

        private static TimeZoneInfo ResolveTimeZone(string timeZoneId) => string.Equals(timeZoneId, "Local", StringComparison.OrdinalIgnoreCase)
            ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        private static HashSet<TimeSpan> ParseVegetationTimes(IReadOnlyList<TimeSpan> times, string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var vegetation = new HashSet<TimeSpan>(text.Trim() == "*" ? times : ParseTimes(text, true));
            if (vegetation.Any(t => !times.Contains(t)))
                throw new ArgumentException("Vegetation times must be a subset of daily run times, or '*'.", nameof(text));
            return vegetation;
        }

        private static TimeSpan[] ParseTimes(string text, bool allowEmpty)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (allowEmpty && string.IsNullOrWhiteSpace(text)) return Array.Empty<TimeSpan>();
            var result = new HashSet<TimeSpan>();
            foreach (var raw in text.Split(','))
            {
                var value = raw.Trim();
                if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time) || time.TotalHours >= 24)
                    throw new ArgumentException("Daily times must contain HH:mm values separated by commas.", nameof(text));
                if (!result.Add(time)) throw new ArgumentException("Daily times cannot contain duplicates.", nameof(text));
            }
            if (result.Count == 0) throw new ArgumentException("At least one daily run time is required.", nameof(text));
            return result.OrderBy(t => t).ToArray();
        }
    }

    public readonly struct ScheduleClock
    {
        public DateTimeOffset UtcNow { get; }
        public double GameDay { get; }
        public ScheduleClock(DateTimeOffset utcNow, double gameDay)
        {
            if (double.IsNaN(gameDay) || double.IsInfinity(gameDay) || gameDay < 0)
                throw new ArgumentOutOfRangeException(nameof(gameDay));
            UtcNow = utcNow.ToUniversalTime();
            GameDay = gameDay;
        }
    }
}
