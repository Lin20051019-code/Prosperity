using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace SheNicest.UI
{
    /// <summary>
    /// LLM台词服务（第一层试点）：仅生成讨价还价中AI的台词文本+情绪+态度。
    /// 成交/还价决策仍由BargainState确定性规则驱动，LLM态度只经BargainState.aiAttitudeAdjust
    /// 以±AttitudeToRateRange(5%)修正AI让步率（带1轮滞后，由BargainOfferController落值）。
    /// 配置：%persistentDataPath%/llm_config.json（OpenAI兼容chat/completions，优先于内置key，可换厂商）。
    /// 2026-09-26起内置默认key（用户批准的打包分发模式：朋友零配置即可用；内置key随exe分发、
    /// 可被提取/抓包，泄露后去DeepSeek平台作废重发即可；AppData配置文件可覆盖内置）。
    /// 失败安全：任何失败（缺key/超时/HTTP错误/解析失败）调用方回落CSV台词池；
    /// 连续失败≥MaxFailures次本次运行熔断（静态，跨场景生效），重启游戏后恢复。
    /// </summary>
    public static class LlmDialogService
    {
        public const float AttitudeToRateRange = 0.05f; // attitude(-1..1)×0.05 → AI让步率修正上限±5%
        private const int TimeoutSeconds = 5;
        private const int MaxFailures = 2;

        // ===== 内置key（2026-09-26用户批准：打包分发给朋友免配置直用；AppData配置文件优先级更高可覆盖。
        // 二进制可被抠出/抓包可见，泄露后去DeepSeek平台作废重发即可封顶损失）=====
        private const string BuiltInApiKey = "sk-a72faffd5f4b41d9847e93f4733d148b";
        private const string BuiltInBaseUrl = "https://api.deepseek.com/v1";
        private const string BuiltInModel = "deepseek-chat";
        private static bool loggedBuiltIn;

        // ===== 配置（persistentDataPath下，可改URL/模型接其它OpenAI兼容厂商）=====
        [Serializable]
        private class LlmConfig
        {
            public string baseUrl = "https://api.deepseek.com/v1";
            public string apiKey = "";
            public string model = "deepseek-chat";
        }

        private static string ConfigPath => Path.Combine(Application.persistentDataPath, "llm_config.json");

        /// <summary>读取配置；文件不存在时生成模板供用户填key（每次请求都重读，改完配置无需重启）。</summary>
        private static LlmConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var tpl = new LlmConfig();
                    File.WriteAllText(ConfigPath, JsonUtility.ToJson(tpl, true));
                    Debug.Log($"[LlmDialog] 已生成配置模板: {ConfigPath}（不填也能用内置key；填apiKey可覆盖内置）");
                    return tpl;
                }
                var cfg = JsonUtility.FromJson<LlmConfig>(File.ReadAllText(ConfigPath));
                return cfg ?? new LlmConfig();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LlmDialog] 配置读取失败: {e.Message}");
                return null;
            }
        }

        // ===== OpenAI兼容请求/响应结构 =====
        [Serializable] private class LlmMsg { public string role; public string content; }
        [Serializable] private class LlmReq { public string model; public List<LlmMsg> messages; public float temperature; public int max_tokens; }
        [Serializable] private class LlmChoice { public LlmMsg message; }
        [Serializable] private class LlmResp { public List<LlmChoice> choices; }
        [Serializable] private class LlmLine { public string text; public string emotion; public float attitude; }

        /// <summary>一次台词请求的可轮询结果（协程不好带返回值，用可变对象+WaitFor）</summary>
        public class Fetch
        {
            public bool done;   // 请求已结束（成功或失败）
            public bool ok;     // 成功拿到台词
            public string text;
            public int emotion;     // 0平 1怒 2惊 3意（与BargainState台词池情绪索引一致）
            public float attitude;  // -1强硬 ~ 1愿让步
        }

        private static int consecutiveFailures;
        private static bool sessionDisabled;

        /// <summary>整体可用：游戏内开关开 + 未熔断 + 非WebGL（key是否有效由请求结果判断）</summary>
        public static bool IsEnabled
        {
            get
            {
                if (!GameSettings.LlmDialogsEnabled) return false;
                if (sessionDisabled) return false;
                if (Application.platform == RuntimePlatform.WebGLPlayer) return false;
                return true;
            }
        }

        /// <summary>启动一次台词生成（非阻塞，由host承载协程）。开关未开/已熔断时返回未启动的Fetch。</summary>
        public static Fetch StartFetch(MonoBehaviour host, string systemPrompt, string userPrompt)
        {
            var f = new Fetch();
            if (host == null || !host.isActiveAndEnabled) { f.done = true; return f; }
            if (!IsEnabled) { f.done = true; return f; }
            host.StartCoroutine(FetchRoutine(systemPrompt, userPrompt, f));
            return f;
        }

        /// <summary>等待Fetch结束（额外硬超时，防场景卸载杀掉协程后调用方傻等）</summary>
        public static IEnumerator WaitFor(Fetch f, float timeoutSec)
        {
            if (f == null) yield break;
            float t = 0f;
            while (!f.done && t < timeoutSec)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator FetchRoutine(string systemPrompt, string userPrompt, Fetch f)
        {
            // 双保险（调用方已按IsEnabled判断，这里防竞态）
            if (!IsEnabled) { f.done = true; yield break; }

            var cfg = LoadConfig();
            string apiKey = cfg != null ? cfg.apiKey : null;
            string baseUrl = cfg != null ? cfg.baseUrl : null;
            string model = cfg != null ? cfg.model : null;
            // 配置文件缺key/缺字段 → 内置key兜底（打包分发模式：朋友零配置即可用；AppData配置可覆盖）
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            {
                apiKey = BuiltInApiKey;
                baseUrl = BuiltInBaseUrl;
                model = BuiltInModel;
                if (!loggedBuiltIn)
                {
                    loggedBuiltIn = true;
                    Debug.Log($"[LlmDialog] 配置未填key，使用内置key（{ConfigPath} 里的key优先级更高）");
                }
            }

            var req = new LlmReq
            {
                model = model,
                messages = new List<LlmMsg>
                {
                    new LlmMsg { role = "system", content = systemPrompt },
                    new LlmMsg { role = "user", content = userPrompt },
                },
                temperature = 1.1f,
                max_tokens = 90,
            };
            string body = JsonUtility.ToJson(req);

            UnityWebRequest www = null;
            try
            {
                www = new UnityWebRequest(baseUrl.TrimEnd('/') + "/chat/completions", UnityWebRequest.kHttpVerbPOST);
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + apiKey);
                www.timeout = TimeoutSeconds;

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    OnFail(f, $"HTTP {(int)www.responseCode} {www.error}");
                    yield break;
                }

                var resp = JsonUtility.FromJson<LlmResp>(www.downloadHandler.text);
                string content = (resp != null && resp.choices != null && resp.choices.Count > 0)
                    ? resp.choices[0].message.content : null;
                if (string.IsNullOrEmpty(content)) { OnFail(f, "响应为空"); yield break; }

                var line = ParseLine(content);
                if (line == null) { OnFail(f, "台词JSON解析失败"); yield break; }

                f.text = line.text;
                f.emotion = EmotionIndex(line.emotion);
                f.attitude = Mathf.Clamp(line.attitude, -1f, 1f);
                f.ok = true;
                f.done = true;
                consecutiveFailures = 0;
                Debug.Log($"[LlmDialog] LLM台词: 「{f.text}」 情绪={f.emotion} 态度={f.attitude:F2}");
            }
            finally
            {
                if (www != null) www.Dispose();
            }
        }

        /// <summary>从模型回复中抠出JSON对象并解析（容错前后闲话与markdown围栏）；台词去换行、超长截断。</summary>
        private static LlmLine ParseLine(string content)
        {
            try
            {
                var m = Regex.Match(content, @"\{[\s\S]*\}");
                if (!m.Success) return null;
                var line = JsonUtility.FromJson<LlmLine>(m.Value);
                if (line == null || string.IsNullOrEmpty(line.text)) return null;
                line.text = Regex.Replace(line.text.Trim(), @"[\r\n]+", "");
                if (line.text.Length > 40) line.text = line.text.Substring(0, 38) + "……";
                if (line.text.Length == 0) return null;
                return line;
            }
            catch { return null; }
        }

        private static int EmotionIndex(string tag)
        {
            switch (tag != null ? tag.Trim() : null)
            {
                case "怒": return 1;
                case "惊": return 2;
                case "意": return 3;
                default: return 0; // 平/未知
            }
        }

        private static void OnFail(Fetch f, string reason)
        {
            f.ok = false;
            f.done = true;
            consecutiveFailures++;
            if (sessionDisabled) return; // 熔断后不再刷日志
            if (consecutiveFailures >= MaxFailures)
            {
                sessionDisabled = true;
                Debug.LogWarning($"[LlmDialog] 连续{consecutiveFailures}次失败（{reason}），本次运行已熔断，全部回落CSV台词。配置文件：{ConfigPath}");
            }
            else
            {
                Debug.LogWarning($"[LlmDialog] 台词生成失败（{reason}），本条回落CSV台词池");
            }
        }
    }
}
