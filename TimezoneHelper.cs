using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ChatGPTAntiBanLauncher
{
    public sealed class PosixMappingResult
    {
        public bool IsSupported { get; set; }
        public string PosixTz { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class TimezoneResolveResult
    {
        public bool Success { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsSupported { get; set; }
        public string ResolvedTzValue { get; set; }
        public string DisplaySummary { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class TimezoneHelper
    {
        public static readonly string[] StandardIanaZones = new string[]
        {
            "America/Los_Angeles",
            "America/New_York",
            "America/Chicago",
            "America/Denver",
            "America/Phoenix",
            "America/Vancouver",
            "America/Toronto",
            "Europe/London",
            "Europe/Paris",
            "Europe/Berlin",
            "Europe/Amsterdam",
            "Asia/Tokyo",
            "Asia/Singapore",
            "Asia/Hong_Kong",
            "Asia/Seoul",
            "Asia/Taipei",
            "Australia/Sydney",
            "Pacific/Auckland"
        };

        private static readonly HashSet<string> KnownIanaZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "America/Los_Angeles", "America/New_York", "America/Chicago", "America/Denver",
            "America/Phoenix", "America/Vancouver", "America/Toronto", "America/Anchorage",
            "America/Honolulu", "Europe/London", "Europe/Paris", "Europe/Berlin",
            "Europe/Amsterdam", "Europe/Rome", "Europe/Madrid", "Europe/Zurich",
            "Europe/Stockholm", "Asia/Tokyo", "Asia/Singapore", "Asia/Hong_Kong",
            "Asia/Seoul", "Asia/Taipei", "Asia/Shanghai", "Asia/Bangkok",
            "Asia/Dubai", "Asia/Kolkata", "Asia/Kathmandu", "Australia/Sydney",
            "Australia/Melbourne", "Australia/Brisbane", "Australia/Perth", "Australia/Darwin",
            "Pacific/Auckland", "UTC", "Etc/UTC"
        };

        // 1. Separate entry point for raw integer seconds from GeoIP APIs
        public static bool ParseSecondsOffset(int totalSeconds, out TimeSpan offset)
        {
            // Range check: -12h (-43200s) to +14h (+50400s)
            if (totalSeconds < -43200 || totalSeconds > 50400)
            {
                offset = TimeSpan.Zero;
                return false;
            }
            offset = TimeSpan.FromSeconds(totalSeconds);
            return true;
        }

        public static bool ParseSecondsOffset(string secondsStr, out TimeSpan offset)
        {
            offset = TimeSpan.Zero;
            if (string.IsNullOrEmpty(secondsStr)) return false;

            int secs;
            if (int.TryParse(secondsStr.Trim(), out secs))
            {
                return ParseSecondsOffset(secs, out offset);
            }
            return false;
        }

        // 2. Separate entry point for user UTC offset text
        public static bool ParseUserUtcOffset(string offsetStr, out TimeSpan offset, out string errorMessage)
        {
            offset = TimeSpan.Zero;
            errorMessage = null;

            if (string.IsNullOrEmpty(offsetStr))
            {
                errorMessage = "UTC 偏移量不可为空";
                return false;
            }

            string s = offsetStr.Trim();

            // Strict full-string anchored match: e.g. "UTC+8", "UTC-05:00", "+08:00", "-5", "GMT+8"
            Match m = Regex.Match(s, @"^(?:UTC|GMT)?\s*([+-])(\d{1,2})(?::?(\d{2}))?$", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                errorMessage = string.Format("无法识别的 UTC 偏移量格式: '{0}' (必须形如 UTC-8 或 UTC+08:00)", s);
                return false;
            }

            int sign = (m.Groups[1].Value == "-") ? -1 : 1;
            int hours;
            if (!int.TryParse(m.Groups[2].Value, out hours))
            {
                errorMessage = "小时数无法转换为有效整数";
                return false;
            }

            int minutes = 0;
            if (m.Groups[3].Success)
            {
                if (!int.TryParse(m.Groups[3].Value, out minutes))
                {
                    errorMessage = "分钟数无法转换为有效整数";
                    return false;
                }
            }

            if (minutes < 0 || minutes >= 60)
            {
                errorMessage = string.Format("分钟数超出有效范围 0~59: {0}", minutes);
                return false;
            }

            if (hours < 0 || hours > 14)
            {
                errorMessage = string.Format("小时数超出有效时区范围 0~14: {0}", hours);
                return false;
            }

            int totalMinutes = sign * (hours * 60 + minutes);
            // World timezone offset valid range: UTC-12:00 (-720 min) to UTC+14:00 (+840 min)
            if (totalMinutes < -720 || totalMinutes > 840)
            {
                errorMessage = string.Format("总偏移量超出世界时区物理范围 (-12:00 ~ +14:00): {0} 分钟", totalMinutes);
                return false;
            }

            offset = TimeSpan.FromMinutes(totalMinutes);
            return true;
        }

        // Backward compatibility overload with non-throwing signature
        public static TimeSpan? ParseOffset(string offsetStr)
        {
            if (string.IsNullOrEmpty(offsetStr)) return null;

            TimeSpan ts;
            string err;
            if (ParseUserUtcOffset(offsetStr, out ts, out err))
            {
                return ts;
            }

            if (ParseSecondsOffset(offsetStr, out ts))
            {
                return ts;
            }

            return null;
        }

        public static string FormatOffset(TimeSpan? offset)
        {
            if (!offset.HasValue) return "未知";

            TimeSpan ts = offset.Value;
            string sign = ts.Ticks >= 0 ? "+" : "-";
            int totalMin = Math.Abs((int)ts.TotalMinutes);
            int absHours = totalMin / 60;
            int absMinutes = totalMin % 60;

            if (absMinutes == 0)
            {
                return string.Format("UTC{0}{1}", sign, absHours);
            }
            return string.Format("UTC{0}{1}:{2:D2}", sign, absHours, absMinutes);
        }

        public static bool ValidateIanaZone(string ianaName, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(ianaName))
            {
                errorMessage = "IANA 时区名称不可为空";
                return false;
            }

            string trimmed = ianaName.Trim();
            if (!KnownIanaZones.Contains(trimmed))
            {
                errorMessage = string.Format("未收录或不受支持的 IANA 时区名称: '{0}'", trimmed);
                return false;
            }

            return true;
        }

        public static PosixMappingResult MapUtcOffsetToPosix(TimeSpan offset)
        {
            int totalMinutes = (int)offset.TotalMinutes;

            // 1. Whole hour offsets: POSIX Etc/GMT syntax is fixed without DST
            if (offset.Minutes == 0)
            {
                int hours = (int)offset.TotalHours;
                if (hours == 0)
                {
                    return new PosixMappingResult { IsSupported = true, PosixTz = "Etc/UTC" };
                }

                // In POSIX syntax, west of GMT is positive, east is negative
                int posixHours = -hours;
                string sign = posixHours >= 0 ? "+" : "-";
                string posixTz = string.Format("Etc/GMT{0}{1}", sign, Math.Abs(posixHours));
                return new PosixMappingResult { IsSupported = true, PosixTz = posixTz };
            }

            // 2. Fractional offsets: verify non-DST permanent status
            // UTC+05:30 -> Asia/Kolkata (India Standard Time has NO DST, verified permanent)
            if (totalMinutes == 330)
            {
                return new PosixMappingResult { IsSupported = true, PosixTz = "Asia/Kolkata" };
            }

            // UTC+05:45 -> Asia/Kathmandu (Nepal Time has NO DST, verified permanent)
            if (totalMinutes == 345)
            {
                return new PosixMappingResult { IsSupported = true, PosixTz = "Asia/Kathmandu" };
            }

            // UTC+09:30 -> Australia/Darwin (Northern Territory has NO DST, verified permanent)
            // Note: Australia/Adelaide MUST NOT be used because it observes Daylight Saving Time
            if (totalMinutes == 570)
            {
                return new PosixMappingResult { IsSupported = true, PosixTz = "Australia/Darwin" };
            }

            // UTC-03:30 (Newfoundland / America/St_Johns): Observes DST (-03:30 winter / -02:30 summer)
            if (totalMinutes == -210)
            {
                return new PosixMappingResult
                {
                    IsSupported = false,
                    PosixTz = null,
                    ErrorMessage = "该偏移量 (UTC-03:30) 缺乏稳定的免夏令时 POSIX/IANA 映射支持，已拒绝注入以防止时钟偏差"
                };
            }

            // UTC+12:45 (Chatham Islands / Pacific/Chatham): Observes DST (+12:45 winter / +13:45 summer)
            if (totalMinutes == 765)
            {
                return new PosixMappingResult
                {
                    IsSupported = false,
                    PosixTz = null,
                    ErrorMessage = "该偏移量 (UTC+12:45) 缺乏稳定的免夏令时 POSIX/IANA 映射支持，已拒绝注入以防止时钟偏差"
                };
            }

            return new PosixMappingResult
            {
                IsSupported = false,
                PosixTz = null,
                ErrorMessage = string.Format("该偏移量 ({0}) 暂无可靠的固定免夏令时 POSIX/IANA 运行时映射支持", FormatOffset(offset))
            };
        }

        public static string MapUtcOffsetToPosixTz(string utcOffset)
        {
            if (string.IsNullOrEmpty(utcOffset))
            {
                throw new ArgumentException("UTC 偏移量为空，无法映射为 POSIX 时区");
            }

            TimeSpan ts;
            string err;
            if (!ParseUserUtcOffset(utcOffset, out ts, out err))
            {
                throw new ArgumentException(err);
            }

            PosixMappingResult res = MapUtcOffsetToPosix(ts);
            if (!res.IsSupported)
            {
                throw new NotSupportedException(res.ErrorMessage);
            }

            return res.PosixTz;
        }

        public static TimezoneResolveResult ResolveTimezone(string mode, string ianaName, string utcOffset, bool disableTz)
        {
            if (disableTz)
            {
                return new TimezoneResolveResult
                {
                    Success = true,
                    IsDisabled = true,
                    IsSupported = true,
                    ResolvedTzValue = null,
                    DisplaySummary = "(时区注入已关闭：遵从系统原生时钟)"
                };
            }

            if (string.Equals(mode, "utc", StringComparison.OrdinalIgnoreCase))
            {
                TimeSpan ts;
                string parseErr;
                if (!ParseUserUtcOffset(utcOffset, out ts, out parseErr))
                {
                    return new TimezoneResolveResult
                    {
                        Success = false,
                        IsSupported = false,
                        ErrorMessage = parseErr
                    };
                }

                PosixMappingResult mapping = MapUtcOffsetToPosix(ts);
                if (!mapping.IsSupported)
                {
                    return new TimezoneResolveResult
                    {
                        Success = false,
                        IsSupported = false,
                        ErrorMessage = mapping.ErrorMessage
                    };
                }

                return new TimezoneResolveResult
                {
                    Success = true,
                    IsSupported = true,
                    ResolvedTzValue = mapping.PosixTz,
                    DisplaySummary = string.Format("{0} -> {1}", FormatOffset(ts), mapping.PosixTz)
                };
            }

            // Mode: IANA
            string ianaErr;
            if (!ValidateIanaZone(ianaName, out ianaErr))
            {
                return new TimezoneResolveResult
                {
                    Success = false,
                    IsSupported = false,
                    ErrorMessage = ianaErr
                };
            }

            string validName = ianaName.Trim();
            return new TimezoneResolveResult
            {
                Success = true,
                IsSupported = true,
                ResolvedTzValue = validName,
                DisplaySummary = validName
            };
        }

        public static string BuildProcessTzValue(string mode, string ianaName, string utcOffset, bool disableTz)
        {
            TimezoneResolveResult res = ResolveTimezone(mode, ianaName, utcOffset, disableTz);
            if (res.IsDisabled) return null;

            if (!res.Success)
            {
                throw new ArgumentException("目标时区配置无效或不受支持: " + res.ErrorMessage);
            }

            return res.ResolvedTzValue;
        }

        public static bool IsTimezoneMatch(string mode, string selectedIana, string selectedUtc, string detectedIana, TimeSpan? detectedOffset)
        {
            if (string.IsNullOrEmpty(detectedIana) && !detectedOffset.HasValue) return false;

            if (string.Equals(mode, "iana", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(selectedIana) || string.IsNullOrEmpty(detectedIana)) return false;
                return string.Equals(selectedIana.Trim(), detectedIana.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                if (!detectedOffset.HasValue || string.IsNullOrEmpty(selectedUtc)) return false;
                TimeSpan selectedTs;
                string err;
                if (!ParseUserUtcOffset(selectedUtc, out selectedTs, out err)) return false;
                return (int)selectedTs.TotalMinutes == (int)detectedOffset.Value.TotalMinutes;
            }
        }
    }
}
