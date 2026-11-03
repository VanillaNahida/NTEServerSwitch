using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NTEServerSwitch.Core
{
    /// <summary>GitHub Release 里跟"要不要提示更新"有关的那几个字段。</summary>
    public sealed class ReleaseInfo
    {
        public string TagName { get; set; }

        /// <summary>去掉前缀 v 并补齐成 x.y.z 的版本号。</summary>
        public string Version { get; set; }

        public string Title { get; set; }

        public string HtmlUrl { get; set; }

        public string Body { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// 从 GitHub Releases 取最新发行版并与自身版本比对。
    /// 只用 latest 接口（不含预发行版），任何失败都由调用方静默处理 —— 检查更新绝不该打扰正常使用。
    /// </summary>
    public static class UpdateChecker
    {
        public const string Owner = "VanillaNahida";
        public const string ProjectName = "NTEServerSwitch";
        public const string Repository = Owner + "/" + ProjectName;

        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/" + Repository + "/releases/latest";

        public const string ReleasesPageUrl =
            "https://github.com/" + Repository + "/releases";

        public const string LatestReleasePageUrl =
            ReleasesPageUrl + "/latest";

        public static Version CurrentVersion
        {
            get
            {
                Version version = typeof(UpdateChecker).Assembly.GetName().Version;
                return version ?? new Version(1, 0, 0, 0);
            }
        }

        /// <summary>当前版本的三段文本，例如 1.1.0。</summary>
        public static string CurrentVersionText
        {
            get { return Format(CurrentVersion); }
        }

        /// <summary>把任意写法（v1.2.0 / 1.2 / 1.2.0.0）归一成 1.2.0，交给 Version.TryParse。</summary>
        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string trimmed = text.Trim();
            if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
            {
                trimmed = trimmed.Substring(1);
            }

            // 只保留数字与点，防止 "-beta" 这类后缀把解析搞崩
            var builder = new System.Text.StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                if ((c >= '0' && c <= '9') || c == '.')
                {
                    builder.Append(c);
                }
                else
                {
                    break;
                }
            }
            trimmed = builder.ToString().Trim('.');
            if (trimmed.Length == 0)
            {
                return null;
            }

            // Version 要求至少两段（"2" 解析不了），这里补齐成 x.y.z
            string[] parts = trimmed.Split('.');
            if (parts.Length > 4)
            {
                return null;
            }
            if (parts.Length < 3)
            {
                var padded = new System.Text.StringBuilder(trimmed);
                for (int i = parts.Length; i < 3; i++)
                {
                    padded.Append(".0");
                }
                trimmed = padded.ToString();
            }

            Version parsed;
            if (!Version.TryParse(trimmed, out parsed))
            {
                return null;
            }
            return parsed;
        }

        public static string Format(Version version)
        {
            if (version == null)
            {
                return "0.0.0";
            }
            int build = version.Build < 0 ? 0 : version.Build;
            return version.Major.ToString(CultureInfo.InvariantCulture) + "." +
                   version.Minor.ToString(CultureInfo.InvariantCulture) + "." +
                   build.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>latest 的 tag 是否比当前版本新。版本号解析不出来时一律当作"没有更新"。</summary>
        public static bool IsNewer(string tag, Version current)
        {
            Version remote = ParseVersion(tag);
            if (remote == null)
            {
                return false;
            }
            if (current == null)
            {
                return true;
            }
            return Compare(remote, current) > 0;
        }

        private static int Compare(Version left, Version right)
        {
            // 缺段按 0 处理：v2 等价于 2.0.0，不能因为 Minor = -1 就判成更旧
            int result = left.Major.CompareTo(right.Major);
            if (result != 0)
            {
                return result;
            }
            int leftMinor = left.Minor < 0 ? 0 : left.Minor;
            int rightMinor = right.Minor < 0 ? 0 : right.Minor;
            result = leftMinor.CompareTo(rightMinor);
            if (result != 0)
            {
                return result;
            }
            int leftBuild = left.Build < 0 ? 0 : left.Build;
            int rightBuild = right.Build < 0 ? 0 : right.Build;
            return leftBuild.CompareTo(rightBuild);
        }

        /// <summary>
        /// 拉取 latest release。网络错误、限流、JSON 异常都返回 null（调用方静默处理）。
        /// </summary>
        public static async Task<ReleaseInfo> CheckAsync(UpdateClient client, CancellationToken token)
        {
            if (client == null)
            {
                return null;
            }

            string json;
            try
            {
                json = await client.GetStringAsync(LatestReleaseApiUrl, token).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            return ParseRelease(json);
        }

        /// <summary>解析 GitHub 的 release JSON；结构不对时返回 null。</summary>
        public static ReleaseInfo ParseRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    JsonElement message;
                    if (root.TryGetProperty("message", out message) &&
                        message.ValueKind == JsonValueKind.String &&
                        !root.TryGetProperty("tag_name", out _))
                    {
                        // {"message": "Not Found"} 之类，不是 release
                        return null;
                    }

                    string tag = ReadString(root, "tag_name");
                    if (string.IsNullOrEmpty(tag))
                    {
                        return null;
                    }

                    Version parsed = ParseVersion(tag);
                    var info = new ReleaseInfo
                    {
                        TagName = tag,
                        Version = parsed == null ? tag : Format(parsed),
                        Title = ReadString(root, "name"),
                        HtmlUrl = ReadString(root, "html_url"),
                        Body = ReadString(root, "body")
                    };

                    string published = ReadString(root, "published_at");
                    DateTimeOffset publishedAt;
                    if (!string.IsNullOrEmpty(published) &&
                        DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture,
                                                DateTimeStyles.AdjustToUniversal, out publishedAt))
                    {
                        info.PublishedAt = publishedAt;
                    }

                    if (string.IsNullOrEmpty(info.HtmlUrl))
                    {
                        info.HtmlUrl = LatestReleasePageUrl;
                    }
                    if (string.IsNullOrEmpty(info.Title))
                    {
                        info.Title = tag;
                    }
                    return info;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ReadString(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }
            return value.GetString() ?? string.Empty;
        }
    }
}
