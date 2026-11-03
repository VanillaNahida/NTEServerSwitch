using System;
using System.IO;
using System.Security.Cryptography;

namespace NTEServerSwitch.Core
{
    public static class HashUtil
    {
        /// <summary>计算文件 MD5，返回小写十六进制字符串。</summary>
        public static string Md5File(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                               1 << 20, FileOptions.SequentialScan))
            {
                using (var md5 = MD5.Create())
                {
                    return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
                }
            }
        }

        public static string Md5Bytes(byte[] data)
        {
            if (data == null)
            {
                return string.Empty;
            }
            return Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
        }

        public static bool Md5Equals(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>文件长度与 MD5 都与期望一致。</summary>
        public static bool Matches(string path, long expectedSize, string expectedMd5)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != expectedSize)
                {
                    return false;
                }
                return Md5Equals(Md5File(path), expectedMd5);
            }
            catch
            {
                return false;
            }
        }
    }
}
