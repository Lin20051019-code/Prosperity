using System.Collections.Generic;
using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 轻量国际化字典：从 Resources/i18n/{lang}.csv 加载 key→text 映射。
    /// 用法：I18n.T("deal_broadcast", 2500) 或 I18n.T("rent_paid")
    /// CSV 格式：key,en（中文值直接硬编码在调用点作为默认，英文走字典）
    /// </summary>
    public static class I18n
    {
        private static Dictionary<string, string> table;
        private static bool loaded = false;

        /// <summary>当前语言（与 BargainState.language 同步）</summary>
        public static string Lang => BargainState.language;

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            table = new Dictionary<string, string>();

            if (Lang == "en")
            {
                var asset = Resources.Load<TextAsset>("i18n/en");
                if (asset == null)
                {
                    Debug.LogWarning("[I18n] Resources/i18n/en.csv not found, falling back to Chinese");
                    return;
                }
                foreach (var line in asset.text.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    var idx = trimmed.IndexOf(',');
                    if (idx < 1) continue;
                    var key = trimmed.Substring(0, idx).Trim();
                    var val = trimmed.Substring(idx + 1).Trim().Trim('"');
                    if (!table.ContainsKey(key)) table[key] = val;
                }
                Debug.Log($"[I18n] Loaded {table.Count} EN entries");
            }
        }

        /// <summary>取翻译。中文直接返回 defaultValue；英文查字典，缺失回退 defaultValue。</summary>
        public static string T(string key, string defaultValue = null)
        {
            if (Lang != "en") return defaultValue ?? key;
            EnsureLoaded();
            if (table != null && table.TryGetValue(key, out var v))
                return v.Replace("\\n", "\n"); // v4.2：CSV字面量\n转真实换行
            return defaultValue ?? key;
        }

        /// <summary>带参数的翻译：T("key", "默认值{price}元", price) → 替换{price}</summary>
        public static string T(string key, string defaultValue, params (string placeholder, object value)[] args)
        {
            var text = T(key, defaultValue);
            foreach (var (ph, val) in args)
                text = text.Replace($"{{{ph}}}", val?.ToString() ?? "");
            return text;
        }

        /// <summary>重置缓存（切换语言后调用）</summary>
        public static void Reset() { loaded = false; table = null; }
    }
}
