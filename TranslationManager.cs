using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace MuckKorean
{
    public static class TranslationManager
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("MuckKorean.Translation");
        private static Dictionary<string, string> _translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static List<KeyValuePair<Regex, string>> _regexTranslations = new List<KeyValuePair<Regex, string>>();
        private static HashSet<string> _missedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _pluginPath;

        public static int Count => _translations.Count;

        public static void Load(string pluginPath)
        {
            _pluginPath = pluginPath;
            string filePath = Path.Combine(pluginPath, "translations.json5");

            if (!File.Exists(filePath))
            {
                Log.LogWarning($"translations.json5 파일을 찾을 수 없습니다: {filePath}");
                return;
            }

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            ParseJson(json);

            if (_regexTranslations.Count > 0)
                Log.LogInfo($"정규식 번역 {_regexTranslations.Count}개 로드");
        }

        // TMP 리치텍스트 태그 패턴: <size=90%>, <color=red>, </color>, <b>, </b> 등
        private static readonly Regex RichTextTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        // "3x Rock", "Unlocked Workbench", "200 Gold", "Mithril Ore (x5)" 패턴
        private static readonly Regex NxItemRegex = new Regex(@"^(\d+)x (.+)$", RegexOptions.Compiled);
        private static readonly Regex UnlockedRegex = new Regex(@"^Unlocked (.+)$", RegexOptions.Compiled);
        private static readonly Regex GoldRegex = new Regex(@"^(\d+) Gold$", RegexOptions.Compiled);
        private static readonly Regex ItemXnRegex = new Regex(@"^(.+) \(x(\d+)\)$", RegexOptions.Compiled);
        // 제작 재료 패턴: "Mithril bar - 5", "Bark - 5", "Fir Wood - 10"
        private static readonly Regex CraftReqRegex = new Regex(@"^(.+) - (\d+)$", RegexOptions.Compiled);
        // 채팅 아이템 획득 패턴: "Player: Picked up (Item)" (아이템명 사전 번역 필요)
        private static readonly Regex ChatPickupRegex = new Regex(@"^(.+): Picked up \((.+)\)$", RegexOptions.Compiled);
        // 문자열 시작 부분의 연속된 리치텍스트 태그 추출
        private static readonly Regex LeadingTagsRegex = new Regex(@"^((?:<[^>]+>)+)", RegexOptions.Compiled);

        /// <summary>
        /// 태그 제거 후 매칭된 번역에 원본의 선행 태그를 복원
        /// 예: "<size=50%><i>Its red and shiny" → "<size=50%><i>빨갛고 윤기가 난다"
        /// </summary>
        private static string PreserveLeadingTags(string original, string translated)
        {
            Match m = LeadingTagsRegex.Match(original);
            if (m.Success)
                return m.Value + translated;
            return translated;
        }

        public static string Translate(string original)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            string trimmed = original.Trim();

            // 1) 원본 그대로 매칭 시도
            if (_translations.TryGetValue(trimmed, out string translated))
                return translated;

            // 2) 리치텍스트 태그 제거 후 매칭 시도 (선행 태그 보존)
            string stripped = RichTextTagRegex.Replace(trimmed, "").Trim();
            if (stripped != trimmed && !string.IsNullOrEmpty(stripped))
            {
                if (_translations.TryGetValue(stripped, out translated))
                    return PreserveLeadingTags(trimmed, translated);
            }

            // 3) 복합 텍스트 처리: \n으로 분할하여 각 줄을 독립적으로 번역
            //    예: "Rock\n<size=50%>(Press "E" to pickup" → "돌\n<size=50%>(E 키를 눌러 줍기)"
            if (trimmed.Contains("\n"))
            {
                string[] lines = trimmed.Split('\n');
                bool anyTranslated = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string lineResult = TranslateLine(lines[i]);
                    if (lineResult != lines[i])
                    {
                        lines[i] = lineResult;
                        anyTranslated = true;
                    }
                }
                if (anyTranslated)
                    return string.Join("\n", lines);
            }

            // 4) 정규식 패턴 매칭 (단일 행 텍스트용, 태그 제거 후 매칭 시 선행 태그 보존)
            foreach (var pair in _regexTranslations)
            {
                if (pair.Key.IsMatch(trimmed))
                    return pair.Key.Replace(trimmed, pair.Value);

                if (stripped != trimmed && pair.Key.IsMatch(stripped))
                    return PreserveLeadingTags(trimmed, pair.Key.Replace(stripped, pair.Value));
            }

            // 5) 아이템명 포함 패턴: 내부 아이템명을 사전 번역 후 재조합
            //    "3x Rock" → "3x 돌", "Unlocked Workbench" → "작업대 해금!", "200 Gold" → "200 골드"
            Match nxMatch = NxItemRegex.Match(trimmed);
            if (nxMatch.Success)
            {
                string itemName = nxMatch.Groups[2].Value;
                if (_translations.TryGetValue(itemName, out translated))
                    return nxMatch.Groups[1].Value + "x " + translated;
            }

            Match unlockMatch = UnlockedRegex.Match(trimmed);
            if (unlockMatch.Success)
            {
                string itemName = unlockMatch.Groups[1].Value;
                if (_translations.TryGetValue(itemName, out translated))
                    return translated + " 해금!";
            }

            Match goldMatch = GoldRegex.Match(trimmed);
            if (goldMatch.Success)
                return goldMatch.Groups[1].Value + " 골드";

            // 6) 거래 UI: "Mithril Ore (x5)" → "미스릴 광석 (x5)"
            Match itemXnMatch = ItemXnRegex.Match(trimmed);
            if (itemXnMatch.Success)
            {
                string itemName = itemXnMatch.Groups[1].Value;
                string count = itemXnMatch.Groups[2].Value;
                if (_translations.TryGetValue(itemName, out translated))
                    return translated + " (x" + count + ")";
            }

            // 7) 제작 재료: "Mithril bar - 5" → "미스릴 주괴 - 5"
            Match craftMatch = CraftReqRegex.Match(trimmed);
            if (!craftMatch.Success && stripped != trimmed)
                craftMatch = CraftReqRegex.Match(stripped);
            if (craftMatch.Success)
            {
                string itemName = craftMatch.Groups[1].Value;
                string count = craftMatch.Groups[2].Value;
                if (_translations.TryGetValue(itemName, out translated))
                    return PreserveLeadingTags(trimmed, translated + " - " + count);
            }

            // 8) 채팅 메시지: "Player: Picked up (Item)" → json5의 "Picked up" 번역 사용
            Match pickupMatch = ChatPickupRegex.Match(stripped != trimmed ? stripped : trimmed);
            if (pickupMatch.Success)
            {
                string playerName = pickupMatch.Groups[1].Value;
                string itemName = pickupMatch.Groups[2].Value;
                string translatedItem = _translations.TryGetValue(itemName, out translated) ? translated : itemName;
                string pickupText = _translations.TryGetValue("Picked up", out string pickupTranslated) ? pickupTranslated : "획득";
                return PreserveLeadingTags(trimmed, playerName + ": " + translatedItem + " " + pickupText);
            }

            return original;
        }

        /// <summary>
        /// 단일 행 텍스트 번역 (줄바꿈 없는 텍스트 전용)
        /// </summary>
        private static string TranslateLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return line;

            string trimmed = line.Trim();

            // 사전 매칭
            if (_translations.TryGetValue(trimmed, out string translated))
                return translated;

            // 리치텍스트 태그 제거 후 매칭 (선행 태그 보존)
            string stripped = RichTextTagRegex.Replace(trimmed, "").Trim();
            if (stripped != trimmed && !string.IsNullOrEmpty(stripped))
            {
                if (_translations.TryGetValue(stripped, out translated))
                    return PreserveLeadingTags(trimmed, translated);
            }

            // 정규식 (태그 제거 후 매칭 시 선행 태그 보존)
            foreach (var pair in _regexTranslations)
            {
                if (pair.Key.IsMatch(trimmed))
                    return pair.Key.Replace(trimmed, pair.Value);

                if (stripped != trimmed && pair.Key.IsMatch(stripped))
                    return PreserveLeadingTags(trimmed, pair.Key.Replace(stripped, pair.Value));
            }

            // 제작 재료: "Mithril bar - 5" → "미스릴 주괴 - 5"
            Match craftMatch = CraftReqRegex.Match(trimmed);
            if (!craftMatch.Success && stripped != trimmed)
                craftMatch = CraftReqRegex.Match(stripped);
            if (craftMatch.Success)
            {
                string itemName = craftMatch.Groups[1].Value;
                string count = craftMatch.Groups[2].Value;
                if (_translations.TryGetValue(itemName, out translated))
                    return translated + " - " + count;
            }

            return line;
        }

        public static void DumpMissedKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length < 2 || key.Length > 200)
                return;

            // 숫자만 있거나, 특수문자만 있는 경우 무시
            if (Regex.IsMatch(key, @"^[\d\s\.\,\:\;\-\+\=\!\?\#\%\&\*\(\)\[\]\{\}\/\\]+$"))
                return;

            if (_missedKeys.Add(key))
            {
                try
                {
                    string missedPath = Path.Combine(_pluginPath, "missed_strings.txt");
                    File.AppendAllText(missedPath, key + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void ParseJson(string json)
        {
            // JSON5 주석 제거: // 한줄 주석, /* */ 블록 주석
            json = Regex.Replace(json, @"//.*?$", "", RegexOptions.Multiline);
            json = Regex.Replace(json, @"/\*.*?\*/", "", RegexOptions.Singleline);

            var matches = Regex.Matches(json, @"""([^""\\]*(?:\\.[^""\\]*)*)""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""");

            foreach (Match match in matches)
            {
                string key = UnescapeJson(match.Groups[1].Value);
                string value = UnescapeJson(match.Groups[2].Value);

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    if (key.StartsWith("regex:"))
                    {
                        string pattern = key.Substring(6);
                        try
                        {
                            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.Singleline);
                            _regexTranslations.Add(new KeyValuePair<Regex, string>(regex, value));
                        }
                        catch (Exception ex)
                        {
                            Log.LogWarning($"정규식 패턴 오류: '{pattern}' - {ex.Message}");
                        }
                    }
                    else
                    {
                        _translations[key] = value;
                    }
                }
            }
        }

        /// <summary>
        /// 번역 값에서 한글 문자(가-힣)만 추출하여 고유 문자열 반환
        /// </summary>
        public static string GetAllKoreanCharacters()
        {
            var chars = new HashSet<char>();
            foreach (var value in _translations.Values)
            {
                foreach (char c in value)
                {
                    if (c >= '\uAC00' && c <= '\uD7A3') // 한글 완성형 범위
                        chars.Add(c);
                }
            }
            // 정규식 번역의 대체 문자열에서도 수집
            foreach (var pair in _regexTranslations)
            {
                foreach (char c in pair.Value)
                {
                    if (c >= '\uAC00' && c <= '\uD7A3')
                        chars.Add(c);
                }
            }

            var sb = new StringBuilder(chars.Count);
            foreach (char c in chars)
                sb.Append(c);
            return sb.ToString();
        }

        private static string UnescapeJson(string s)
        {
            return s
                .Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
    }
}
