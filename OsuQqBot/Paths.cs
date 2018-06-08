﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OsuQqBot
{
    /// <summary>
    /// 用于存储各种路径的静态类
    /// </summary>
    static class Paths
    {
        internal static string BasePath
        {
            get
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.IsNullOrEmpty(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return desktop;
            }
        }

        /// <summary>
        /// Bot 数据存储根目录
        /// </summary>
        internal static string DataPath => Path.Combine(BasePath, "Sheep Bot Data");

        /// <summary>
        /// json配置文件所在路径
        /// </summary>
        internal static string JsonConfigPath => Path.Combine(DataPath, "config.json");

        internal static string IgnoreListPath => Path.Combine(DataPath, "ignore.txt");
    }
}
