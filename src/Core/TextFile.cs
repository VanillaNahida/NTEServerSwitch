using System;
using System.IO;
using System.Text;

namespace NTEServerSwitch.Core
{
    /// <summary>带原始编码与行尾信息的文本内容，用于"读进来改几个字再写回去"。</summary>
    public sealed class TextDocument
    {
        public string Text { get; set; }

        public Encoding Encoding { get; set; }

        public string NewLine { get; set; }
    }

    public static class TextFile
    {
        static TextFile()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
                // 只影响极少数非 UTF-8 文件，忽略。
            }
        }

        public static TextDocument Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding;
            int preambleLength;
            DetectEncoding(bytes, out encoding, out preambleLength);

            string text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new TextDocument
            {
                Text = text,
                Encoding = encoding,
                NewLine = DetectNewLine(text)
            };
        }

        public static void Write(string path, TextDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            byte[] body = document.Encoding.GetBytes(document.Text);
            byte[] preamble = document.Encoding.GetPreamble();

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".nswtmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (preamble.Length > 0)
                {
                    stream.Write(preamble, 0, preamble.Length);
                }
                stream.Write(body, 0, body.Length);
                stream.Flush(true);
            }

            File.Move(tempPath, path, true);
        }

        private static void DetectEncoding(byte[] bytes, out Encoding encoding, out int preambleLength)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                preambleLength = 3;
                return;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                preambleLength = 2;
                return;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                preambleLength = 2;
                return;
            }

            preambleLength = 0;
            try
            {
                var strictUtf8 = new UTF8Encoding(false, true);
                strictUtf8.GetString(bytes);
                encoding = new UTF8Encoding(false);
                return;
            }
            catch
            {
                // 不是合法 UTF-8，继续尝试本地代码页。
            }

            try
            {
                encoding = Encoding.GetEncoding("GB18030");
            }
            catch
            {
                encoding = Encoding.Latin1;
            }
        }

        private static string DetectNewLine(string text)
        {
            int index = text.IndexOf('\n');
            if (index > 0 && text[index - 1] == '\r')
            {
                return "\r\n";
            }
            if (index >= 0)
            {
                return "\n";
            }
            return "\r\n";
        }
    }
}
