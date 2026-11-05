using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// SaveData.csv 읽기. 쓰기는 세이브 기능을 구현할 때 추가한다.
    ///
    /// 위치: Application.persistentDataPath/SaveData.csv
    ///   (Resources 폴더는 빌드 후 읽기 전용이라 세이브 파일을 둘 수 없다)
    ///
    /// 형식 (Key,Value 한 줄씩. 첫 줄 헤더는 있어도 되고 없어도 된다):
    ///   Key,Value
    ///   LastPlayedStage,2
    ///   HighestClearedStage,1
    /// </summary>
    public static class SaveDataStore
    {
        public const string FileName = "SaveData.csv";

        private const string KeyLastPlayedStage = "LastPlayedStage";
        private const string KeyHighestClearedStage = "HighestClearedStage";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// 세이브를 읽는다. 파일이 없거나, 읽을 수 없거나,
        /// 마지막 플레이 스테이지와 클리어 기록이 둘 다 없으면 null을 돌려준다 (= "기록 없음"으로 취급).
        /// LastPlayedStage가 0이어도 클리어 기록이 있으면 "기록 있음"이다 (엔딩 직후 상태).
        /// </summary>
        public static SaveData Load()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                Dictionary<string, string> values = ParseKeyValues(File.ReadAllLines(path));

                // 값이 없거나 숫자가 아니면 0으로 본다
                TryGetInt(values, KeyLastPlayedStage, out int lastPlayed);
                TryGetInt(values, KeyHighestClearedStage, out int highestCleared);
                lastPlayed = Mathf.Max(0, lastPlayed);
                highestCleared = Mathf.Max(0, highestCleared);

                if (lastPlayed == 0 && highestCleared == 0)
                {
                    Debug.LogWarning($"[SaveDataStore] 플레이 기록과 클리어 기록이 모두 없어 기록 없음으로 처리합니다: {path}");
                    return null;
                }

                return new SaveData(lastPlayed, highestCleared);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[SaveDataStore] 세이브 파일을 읽지 못해 기록 없음으로 처리합니다: {path}\n{e.Message}");
                return null;
            }
        }

        private static Dictionary<string, string> ParseKeyValues(string[] lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in lines)
            {
                // 엑셀의 "CSV UTF-8" 저장은 파일 맨 앞에 BOM을 붙인다
                string line = rawLine.Trim().TrimStart('﻿');
                if (line.Length == 0)
                {
                    continue;
                }

                int comma = line.IndexOf(',');
                if (comma < 0)
                {
                    continue;
                }

                string key = line.Substring(0, comma).Trim();
                string value = line.Substring(comma + 1).Trim();
                values[key] = value;
            }
            return values;
        }

        private static bool TryGetInt(Dictionary<string, string> values, string key, out int result)
        {
            result = 0;
            return values.TryGetValue(key, out string text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
    }
}
