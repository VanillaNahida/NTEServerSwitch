using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace NTEServerSwitch.Core
{
    /// <summary>清单里的一条 <File>。</summary>
    public sealed class ManifestFileEntry
    {
        public string Path { get; set; }

        public string Checksum { get; set; }

        public long Size { get; set; }

        public string ZipChecksum { get; set; }

        public long ZipSize { get; set; }

        /// <summary>清单路径的文件名部分，zip 内的条目名。</summary>
        public string FileName
        {
            get
            {
                int slash = (Path ?? string.Empty).LastIndexOf('/');
                return slash >= 0 ? Path.Substring(slash + 1) : (Path ?? string.Empty);
            }
        }

        public override string ToString()
        {
            return Path + " (" + Size + ")";
        }
    }

    public static class ManifestPath
    {
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new FormatException("清单中的路径为空");
            }

            string path = raw.Trim().Replace('\\', '/');
            while (path.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                path = path.Replace("//", "/");
            }
            if (path[0] != '/')
            {
                path = "/" + path;
            }
            if (path.IndexOf(':') >= 0)
            {
                throw new FormatException("清单路径含有驱动器号：" + raw);
            }
            foreach (string segment in path.Split('/'))
            {
                if (segment == "..")
                {
                    throw new FormatException("清单路径含有上级引用：" + raw);
                }
            }
            return path;
        }
    }

    /// <summary>Version.ini</summary>
    public sealed class VersionInfo
    {
        public string Version { get; private set; }

        public string Build { get; private set; }

        public string FileListUrl { get; private set; }

        public string SourceUrl { get; private set; }

        public static VersionInfo Parse(string text, string sourceUrl)
        {
            IniFile ini = IniFile.Parse(text ?? string.Empty);
            string fileListUrl = ini.Get("VERSION", "FileListURL");
            if (string.IsNullOrEmpty(fileListUrl))
            {
                fileListUrl = ini.Get("VERSION", "FileListUrl");
            }
            if (string.IsNullOrWhiteSpace(fileListUrl))
            {
                throw new InvalidDataException("Version.ini 中没有 FileListURL");
            }

            fileListUrl = fileListUrl.Trim();
            if (fileListUrl.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                fileListUrl = Endpoints.CombineUrl(Endpoints.DirectoryUrl(sourceUrl), fileListUrl);
            }

            return new VersionInfo
            {
                Version = (ini.Get("VERSION", "Version") ?? string.Empty).Trim(),
                Build = (ini.Get("VERSION", "Build") ?? string.Empty).Trim(),
                FileListUrl = fileListUrl,
                SourceUrl = sourceUrl
            };
        }
    }

    /// <summary>AllFiles.xml</summary>
    public sealed class FileList
    {
        private readonly Dictionary<string, ManifestFileEntry> _byPath;

        private FileList()
        {
            _byPath = new Dictionary<string, ManifestFileEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public string ProductVersion { get; private set; }

        public string BaseUrl { get; private set; }

        public string SourceUrl { get; private set; }

        public List<ManifestFileEntry> Files { get; private set; }

        public IReadOnlyDictionary<string, ManifestFileEntry> ByPath
        {
            get { return _byPath; }
        }

        public static FileList Parse(string xml, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new InvalidDataException("文件清单为空");
            }

            XDocument document = XDocument.Parse(xml);
            XElement root = document.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "All_Files", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("不是有效的 AllFiles.xml（根节点为 " +
                                               (root == null ? "null" : root.Name.LocalName) + "）");
            }

            var list = new FileList();
            var productVersionNode = root.Element("ProductVersion");
            list.ProductVersion = productVersionNode == null || productVersionNode.Attribute("Version") == null
                ? string.Empty
                : productVersionNode.Attribute("Version").Value;

            var urlNode = root.Element("Url");
            list.BaseUrl = urlNode == null || urlNode.Attribute("BaseUrl") == null
                ? Endpoints.DirectoryUrl(sourceUrl)
                : urlNode.Attribute("BaseUrl").Value;

            list.SourceUrl = sourceUrl;
            var entries = new List<ManifestFileEntry>();

            foreach (XElement node in root.Elements("File"))
            {
                string rawPath = node.Attribute("Path") == null ? null : node.Attribute("Path").Value;
                if (string.IsNullOrEmpty(rawPath))
                {
                    continue;
                }

                var entry = new ManifestFileEntry
                {
                    Path = ManifestPath.Normalize(rawPath),
                    Checksum = ReadString(node, "Checksum"),
                    Size = ReadLong(node, "Size"),
                    ZipChecksum = ReadString(node, "ZipChecksum"),
                    ZipSize = ReadLong(node, "ZipSize")
                };

                if (entry.Size > 0 && entry.Checksum.Length == 0)
                {
                    throw new InvalidDataException("清单条目缺少 Checksum：" + entry.Path);
                }

                entries.Add(entry);
                list._byPath[entry.Path] = entry;
            }

            if (entries.Count == 0)
            {
                throw new InvalidDataException("文件清单里没有任何文件");
            }

            list.Files = entries;
            return list;
        }

        private static string ReadString(XElement node, string name)
        {
            XAttribute attribute = node.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value.Trim().ToLowerInvariant();
        }

        private static long ReadLong(XElement node, string name)
        {
            XAttribute attribute = node.Attribute(name);
            long value;
            if (attribute != null && long.TryParse(attribute.Value.Trim(), NumberStyles.Integer,
                                                   CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return 0;
        }

        public ManifestFileEntry Find(string path)
        {
            ManifestFileEntry entry;
            return path != null && _byPath.TryGetValue(path, out entry) ? entry : null;
        }
    }

    /// <summary>两个渠道清单的差异集，是"缓存里到底放什么"的唯一依据。</summary>
    public sealed class ManifestDiff
    {
        public List<ManifestFileEntry> OfficialFiles { get; private set; }

        public List<ManifestFileEntry> BilibiliFiles { get; private set; }

        public List<string> OfficialRemovePaths { get; private set; }

        public List<string> BilibiliRemovePaths { get; private set; }

        public static ManifestDiff Compute(FileList official, FileList bilibili)
        {
            if (official == null || bilibili == null)
            {
                throw new ArgumentNullException("official");
            }

            var diff = new ManifestDiff
            {
                OfficialFiles = new List<ManifestFileEntry>(),
                BilibiliFiles = new List<ManifestFileEntry>(),
                OfficialRemovePaths = new List<string>(),
                BilibiliRemovePaths = new List<string>()
            };

            var allPaths = new HashSet<string>(official.ByPath.Keys, StringComparer.OrdinalIgnoreCase);
            allPaths.UnionWith(bilibili.ByPath.Keys);

            foreach (string path in allPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                ManifestFileEntry inOfficial;
                ManifestFileEntry inBilibili;
                bool hasOfficial = official.ByPath.TryGetValue(path, out inOfficial);
                bool hasBilibili = bilibili.ByPath.TryGetValue(path, out inBilibili);

                if (hasOfficial && hasBilibili && HashUtil.Md5Equals(inOfficial.Checksum, inBilibili.Checksum))
                {
                    continue;
                }

                if (hasOfficial)
                {
                    diff.OfficialFiles.Add(inOfficial);
                }
                else
                {
                    // 只在 B 服清单里：切到官服时必须删掉
                    diff.OfficialRemovePaths.Add(path);
                }

                if (hasBilibili)
                {
                    diff.BilibiliFiles.Add(inBilibili);
                }
                else
                {
                    diff.BilibiliRemovePaths.Add(path);
                }
            }

            return diff;
        }
    }
}
