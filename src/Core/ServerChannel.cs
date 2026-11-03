using System;

namespace NTEServerSwitch.Core
{
    public enum ChannelId
    {
        Official = 0,
        Bilibili = 1
    }

    /// <summary>
    /// 一个渠道（官服 / B服）的全部静态特征。
    /// </summary>
    public sealed class ServerChannel
    {
        private ServerChannel(ChannelId id, string displayName, string cacheFolderName,
                              string branch, int serialNumber, string accentColor)
        {
            Id = id;
            DisplayName = displayName;
            CacheFolderName = cacheFolderName;
            Branch = branch;
            SerialNumber = serialNumber;
            AccentColor = accentColor;
        }

        public ChannelId Id { get; private set; }

        public string DisplayName { get; private set; }

        /// <summary>sdkres 下的子目录名。</summary>
        public string CacheFolderName { get; private set; }

        /// <summary>CDN 上的分支目录名，同时对应 Config.ini 的 Branch= 取值。</summary>
        public string Branch { get; private set; }

        /// <summary>Config.ini 的 [VERSION] serialNumber，也决定清单目录后缀 _1 / _2。</summary>
        public int SerialNumber { get; private set; }

        /// <summary>界面强调色。</summary>
        public string AccentColor { get; private set; }

        public string SerialNumberText
        {
            get { return SerialNumber.ToString(); }
        }

        public static ServerChannel Official { get; private set; }

        public static ServerChannel Bilibili { get; private set; }

        static ServerChannel()
        {
            Official = new ServerChannel(ChannelId.Official, "官服", "official", "publish_ob", 1, "#2E9B4F");
            Bilibili = new ServerChannel(ChannelId.Bilibili, "B服", "bilibili", "publish_ob_B", 2, "#FF6597");
        }

        public static ServerChannel[] All
        {
            get { return new[] { Official, Bilibili }; }
        }

        public static ServerChannel FromId(ChannelId id)
        {
            return id == ChannelId.Bilibili ? Bilibili : Official;
        }

        public static ServerChannel FromCacheFolder(string folderName)
        {
            if (!string.IsNullOrEmpty(folderName) &&
                string.Equals(folderName, Bilibili.CacheFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return Bilibili;
            }
            return Official;
        }

        public static ServerChannel FromBranch(string branch)
        {
            if (!string.IsNullOrEmpty(branch) && branch.IndexOf("_B", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Bilibili;
            }
            return Official;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
