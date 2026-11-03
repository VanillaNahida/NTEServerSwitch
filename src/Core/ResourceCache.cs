using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NTEServerSwitch.Core
{
    public sealed class CacheFileEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("md5")]
        public string Md5 { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("zipMd5")]
        public string ZipMd5 { get; set; }

        [JsonPropertyName("zipSize")]
        public long ZipSize { get; set; }

        /// <summary>清单路径的文件名部分，也是 zip 内的条目名。</summary>
        [JsonIgnore]
        public string FileName
        {
            get
            {
                string path = Path ?? string.Empty;
                int slash = path.LastIndexOf('/');
                return slash >= 0 ? path.Substring(slash + 1) : path;
            }
        }
    }

    public sealed class ChannelCacheState
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("productVersion")]
        public string ProductVersion { get; set; }

        [JsonPropertyName("fileListUrl")]
        public string FileListUrl { get; set; }

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonPropertyName("checkedAtUtc")]
        public string CheckedAtUtc { get; set; }

        [JsonPropertyName("files")]
        public List<CacheFileEntry> Files { get; set; } = new List<CacheFileEntry>();

        [JsonPropertyName("removePaths")]
        public List<string> RemovePaths { get; set; } = new List<string>();

        [JsonIgnore]
        public bool HasFiles
        {
            get { return Files != null && Files.Count > 0; }
        }
    }

    public sealed class CacheState
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("channels")]
        public Dictionary<string, ChannelCacheState> Channels { get; set; } =
            new Dictionary<string, ChannelCacheState>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsEmpty
        {
            get { return Channels == null || Channels.Count == 0; }
        }

        public ChannelCacheState GetChannel(string folderName)
        {
            if (Channels == null || string.IsNullOrEmpty(folderName))
            {
                return null;
            }
            ChannelCacheState value;
            return Channels.TryGetValue(folderName, out value) ? value : null;
        }

        public ChannelCacheState GetChannel(ServerChannel channel)
        {
            return channel == null ? null : GetChannel(channel.CacheFolderName);
        }

        public void SetChannel(ServerChannel channel, ChannelCacheState value)
        {
            if (Channels == null)
            {
                Channels = new Dictionary<string, ChannelCacheState>(StringComparer.OrdinalIgnoreCase);
            }
            Channels[channel.CacheFolderName] = value;
        }
    }

    /// <summary>
    /// sdkres 目录的读写：state.json + 各渠道的原始文件副本。
    /// </summary>
    public sealed class ResourceCache
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true
        };

        private readonly LogService _log = LogService.Instance;

        public string Root
        {
            get { return AppPaths.CacheRoot; }
        }

        public CacheState LoadState()
        {
            string path = AppPaths.StateFilePath;
            try
            {
                if (!File.Exists(path))
                {
                    return new CacheState();
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                CacheState state = JsonSerializer.Deserialize<CacheState>(json, JsonOptions);
                if (state == null)
                {
                    return new CacheState();
                }
                if (state.SchemaVersion != CacheState.CurrentSchemaVersion)
                {
                    _log.Warn("缓存描述版本不一致（" + state.SchemaVersion + "），将重新建立。");
                    return new CacheState();
                }

                var normalized = new Dictionary<string, ChannelCacheState>(StringComparer.OrdinalIgnoreCase);
                if (state.Channels != null)
                {
                    foreach (KeyValuePair<string, ChannelCacheState> pair in state.Channels)
                    {
                        if (pair.Value == null)
                        {
                            continue;
                        }
                        if (pair.Value.Files == null)
                        {
                            pair.Value.Files = new List<CacheFileEntry>();
                        }
                        if (pair.Value.RemovePaths == null)
                        {
                            pair.Value.RemovePaths = new List<string>();
                        }
                        normalized[pair.Key] = pair.Value;
                    }
                }
                state.Channels = normalized;
                return state;
            }
            catch (Exception ex)
            {
                _log.Warn("读取缓存描述失败，将重新建立：" + ex.Message);
                return new CacheState();
            }
        }

        public void SaveState(CacheState state)
        {
            if (state == null)
            {
                return;
            }
            try
            {
                AppPaths.EnsureDirectory(Root);
                string json = JsonSerializer.Serialize(state, JsonOptions);
                string path = AppPaths.StateFilePath;
                string temp = path + ".nswtmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch (Exception ex)
            {
                _log.Warn("保存缓存描述失败：" + ex.Message);
            }
        }

        public string CachedPath(ServerChannel channel, CacheFileEntry entry)
        {
            if (channel == null || entry == null)
            {
                return null;
            }
            return PathUtil.SafeCombine(AppPaths.ChannelFilesDirectory(channel), entry.Path);
        }

        public bool IsValid(ServerChannel channel, CacheFileEntry entry, bool verifyHash)
        {
            string path = CachedPath(channel, entry);
            if (path == null)
            {
                return false;
            }
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != entry.Size)
                {
                    return false;
                }
                return !verifyHash || HashUtil.Md5Equals(HashUtil.Md5File(path), entry.Md5);
            }
            catch
            {
                return false;
            }
        }

        public int CountMissing(ServerChannel channel, ChannelCacheState state)
        {
            if (state == null || state.Files == null)
            {
                return 0;
            }
            int missing = 0;
            foreach (CacheFileEntry entry in state.Files)
            {
                if (!IsValid(channel, entry, false))
                {
                    missing++;
                }
            }
            return missing;
        }

        public long MeasureReadyBytes(ServerChannel channel, ChannelCacheState state, out int missing)
        {
            missing = 0;
            if (state == null || state.Files == null)
            {
                return 0;
            }
            long bytes = 0;
            foreach (CacheFileEntry entry in state.Files)
            {
                if (IsValid(channel, entry, false))
                {
                    bytes += entry.Size;
                }
                else
                {
                    missing++;
                }
            }
            return bytes;
        }

        /// <summary>删掉缓存里已经不在清单上的文件（版本变更后的残留）。</summary>
        public void PruneChannelFiles(ServerChannel channel, IEnumerable<string> keepPaths)
        {
            string directory = AppPaths.ChannelFilesDirectory(channel);
            if (!Directory.Exists(directory))
            {
                return;
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (keepPaths != null)
            {
                foreach (string path in keepPaths)
                {
                    keep.Add((path ?? string.Empty).TrimStart('/').Replace('/', '\\'));
                }
            }

            int removed = 0;
            try
            {
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar);
                    if (keep.Contains(relative))
                    {
                        continue;
                    }
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("清理缓存残留失败：" + relative + " - " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("遍历缓存目录失败：" + ex.Message);
            }

            if (removed > 0)
            {
                _log.Info("已清理 " + channel.DisplayName + " 缓存中 " + removed + " 个过期文件。");
            }
        }

        public void ClearChannel(ServerChannel channel)
        {
            string directory = AppPaths.ChannelDirectory(channel);
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                    _log.Info("已清空 " + channel.DisplayName + " 的资源缓存。");
                }
            }
            catch (Exception ex)
            {
                _log.Warn("清空缓存失败：" + ex.Message);
            }
        }

        /// <summary>缓存临时文件目录，下载的 zip 放这里，用完即删。</summary>
        public string TempDirectory
        {
            get { return Path.Combine(Root, ".tmp"); }
        }
    }
}
