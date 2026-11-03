using System;
using System.Collections.Generic;
using System.Text;

namespace NTEServerSwitch.Core
{
    public enum IniLineKind
    {
        Blank,
        Comment,
        Section,
        Entry,
        Other
    }

    // 一行 INI 内容。
    public sealed class IniLine
    {
        public IniLineKind Kind { get; set; }

        public string Raw { get; set; }

        public string NewLine { get; set; }

        public string SectionName { get; set; }

        public string KeyPrefix { get; set; }

        public string Key { get; set; }

        public string Separator { get; set; }

        public string Value { get; set; }

        public string InlineComment { get; set; }

        public bool Dirty { get; set; }

        public string Render()
        {
            if (!Dirty)
            {
                return Raw ?? string.Empty;
            }
            return (KeyPrefix ?? string.Empty) + (Key ?? string.Empty) + (Separator ?? "=")
                   + (Value ?? string.Empty) + (InlineComment ?? string.Empty);
        }
    }

    // 一个 INI 文件。
    public sealed class IniFile
    {
        private readonly List<IniLine> _lines = new List<IniLine>();
        private string _defaultNewLine = "\r\n";

        public IReadOnlyList<IniLine> Lines
        {
            get { return _lines; }
        }

        public string DefaultNewLine
        {
            get { return _defaultNewLine; }
        }

        public static IniFile Parse(string text)
        {
            var ini = new IniFile();
            if (text == null)
            {
                return ini;
            }

            int index = 0;
            while (index < text.Length)
            {
                int start = index;
                while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                {
                    index++;
                }

                string content = text.Substring(start, index - start);
                string newLine = string.Empty;
                if (index < text.Length)
                {
                    if (text[index] == '\r')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '\n')
                        {
                            newLine = "\r\n";
                            index += 2;
                        }
                        else
                        {
                            newLine = "\r";
                            index += 1;
                        }
                    }
                    else
                    {
                        newLine = "\n";
                        index += 1;
                    }
                }

                ini._lines.Add(Classify(content, newLine));
            }

            foreach (var line in ini._lines)
            {
                if (!string.IsNullOrEmpty(line.NewLine))
                {
                    ini._defaultNewLine = line.NewLine;
                    break;
                }
            }
            return ini;
        }

        private static IniLine Classify(string content, string newLine)
        {
            var line = new IniLine
            {
                Raw = content,
                NewLine = newLine,
                KeyPrefix = string.Empty,
                Separator = "=",
                Value = string.Empty,
                InlineComment = string.Empty
            };

            string trimmed = (content ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                line.Kind = IniLineKind.Blank;
                return line;
            }
            if (trimmed[0] == ';' || trimmed[0] == '#')
            {
                line.Kind = IniLineKind.Comment;
                return line;
            }
            if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                line.Kind = IniLineKind.Section;
                line.SectionName = trimmed.Substring(1, trimmed.Length - 2).Trim();
                return line;
            }

            int equals = content.IndexOf('=');
            if (equals <= 0)
            {
                line.Kind = IniLineKind.Other;
                return line;
            }

            int prefixEnd = 0;
            while (prefixEnd < equals && (content[prefixEnd] == ' ' || content[prefixEnd] == '\t'))
            {
                prefixEnd++;
            }

            string key = content.Substring(prefixEnd, equals - prefixEnd).Trim();
            if (key.Length == 0)
            {
                line.Kind = IniLineKind.Other;
                return line;
            }

            string rest = content.Substring(equals + 1);
            string value = rest;
            string inlineComment = string.Empty;

            int commentIndex = IndexOfInlineComment(rest);
            if (commentIndex >= 0)
            {
                value = rest.Substring(0, commentIndex);
                inlineComment = rest.Substring(commentIndex);
            }

            line.Kind = IniLineKind.Entry;
            line.KeyPrefix = content.Substring(0, prefixEnd);
            line.Key = key;
            line.Separator = "=";
            line.Value = value.Trim();
            line.InlineComment = inlineComment;
            return line;
        }

        private static int IndexOfInlineComment(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == ';' && i > 0 && (value[i - 1] == ' ' || value[i - 1] == '\t'))
                {
                    return i - 1;
                }
            }
            return -1;
        }

        public string Get(string section, string key)
        {
            var line = FindEntry(section, key);
            return line == null ? null : line.Value;
        }

        public bool Contains(string section, string key)
        {
            return FindEntry(section, key) != null;
        }

        public IniLine FindEntry(string section, string key)
        {
            string current = null;
            foreach (var line in _lines)
            {
                if (line.Kind == IniLineKind.Section)
                {
                    current = line.SectionName;
                    continue;
                }
                if (line.Kind != IniLineKind.Entry)
                {
                    continue;
                }
                if (!SectionEquals(current, section))
                {
                    continue;
                }
                if (string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return line;
                }
            }
            return null;
        }

        public bool Set(string section, string key, string value)
        {
            var line = FindEntry(section, key);
            if (line == null)
            {
                return false;
            }
            if (!string.Equals(line.Value, value, StringComparison.Ordinal))
            {
                line.Value = value ?? string.Empty;
                line.Dirty = true;
            }
            return true;
        }

        /// <summary>存在则改值，不存在则插入到该节头部（紧跟在节标题后）。</summary>
        public void Ensure(string section, string key, string value)
        {
            if (Set(section, key, value))
            {
                return;
            }

            int sectionIndex = IndexOfSection(section);
            var line = new IniLine
            {
                Kind = IniLineKind.Entry,
                KeyPrefix = string.Empty,
                Key = key,
                Separator = "=",
                Value = value ?? string.Empty,
                InlineComment = string.Empty,
                Dirty = true,
                NewLine = _defaultNewLine
            };

            if (sectionIndex < 0)
            {
                var header = new IniLine
                {
                    Kind = IniLineKind.Section,
                    SectionName = section,
                    Dirty = true,
                    NewLine = _defaultNewLine
                };
                _lines.Add(header);
                _lines.Add(line);
                return;
            }

            if (string.IsNullOrEmpty(_lines[sectionIndex].NewLine))
            {
                _lines[sectionIndex].NewLine = _defaultNewLine;
            }
            _lines.Insert(sectionIndex + 1, line);
        }

        public bool Remove(string section, string key)
        {
            string current = null;
            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                if (line.Kind == IniLineKind.Section)
                {
                    current = line.SectionName;
                    continue;
                }
                if (line.Kind != IniLineKind.Entry || !SectionEquals(current, section))
                {
                    continue;
                }
                if (string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public int IndexOfSection(string section)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Kind == IniLineKind.Section && SectionEquals(_lines[i].SectionName, section))
                {
                    return i;
                }
            }
            return -1;
        }

        public string ToText()
        {
            var builder = new StringBuilder();
            foreach (var line in _lines)
            {
                builder.Append(line.Render());
                builder.Append(line.NewLine ?? string.Empty);
            }
            return builder.ToString();
        }

        private static bool SectionEquals(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
