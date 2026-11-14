using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// SaveData.csv 읽기와 쓰기.
    ///
    /// 위치: Application.persistentDataPath/SaveData.csv
    ///   (Resources 폴더는 빌드 후 읽기 전용이라 세이브 파일을 둘 수 없다)
    ///
    /// 형식 (Key,Value 한 줄씩. 첫 줄 헤더는 있어도 되고 없어도 된다):
    ///   Key,Value
    ///   LastPlayedStage,2
    ///   HighestClearedStage,1
    ///
    /// 언제 무엇을 기록하는지는 docs/decisions/003-save-data-and-stage-unlock.md의 "세이브를 쓰는 쪽이 지킬 규칙".
    /// </summary>
    public static class SaveDataStore
    {
        public const string FileName = "SaveData.csv";

        private const string KeyLastPlayedStage = "LastPlayedStage";
        private const string KeyHighestClearedStage = "HighestClearedStage";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        // 웹 빌드는 파일 쓰기가 브라우저 저장소(IndexedDB)에 바로 반영되지 않고 File.Replace도 제대로 동작하지 않아,
        // 같은 CSV 내용을 PlayerPrefs(브라우저에 바로 저장됨)에 넣는다
#if UNITY_WEBGL && !UNITY_EDITOR
        private const bool UsePlayerPrefs = true;
#else
        private const bool UsePlayerPrefs = false;
#endif
        private const string PrefsKey = "CoffeeSpec.SaveData";

        /// <summary>
        /// 세이브를 읽는다. 파일이 없거나, 읽을 수 없거나,
        /// 마지막 플레이 스테이지와 클리어 기록이 둘 다 없으면 null을 돌려준다 (= "기록 없음"으로 취급).
        /// LastPlayedStage가 0이어도 클리어 기록이 있으면 "기록 있음"이다 (엔딩 직후 상태).
        /// </summary>
        public static SaveData Load()
        {
            SaveData save = LoadRaw();
            if (save == null)
            {
                return null;
            }

            if (save.LastPlayedStage == 0 && save.HighestClearedStage == 0)
            {
                Debug.LogWarning($"[SaveDataStore] 플레이 기록과 클리어 기록이 모두 없어 기록 없음으로 처리합니다: {FilePath}");
                return null;
            }
            return save;
        }

        /// <summary>스테이지에 들어갈 때: 마지막 플레이 스테이지를 그 번호로 기록한다.</summary>
        public static void RecordStageEntered(int stageNumber)
        {
            if (stageNumber < 1)
            {
                return;
            }

            SaveData current = LoadRaw();
            int highestCleared = current?.HighestClearedStage ?? 0;
            Write(new SaveData(stageNumber, highestCleared));
        }

        /// <summary>
        /// 스테이지를 클리어했을 때: 최고 클리어 기록을 올리고(줄어들지 않음),
        /// 다음 스테이지가 있으면 그 번호를, 마지막 스테이지(엔딩)면 0을 마지막 플레이 스테이지로 기록한다.
        /// 다음 스테이지 씬에 들어가기 전에 게임을 꺼도 타이틀의 게임 시작이 다음 스테이지로 이어지게 하려고 여기서 미리 기록한다.
        /// </summary>
        public static void RecordStageCleared(int stageNumber, bool isLastStage)
        {
            if (stageNumber < 1)
            {
                return;
            }

            SaveData current = LoadRaw();
            int highestCleared = Mathf.Max(current?.HighestClearedStage ?? 0, stageNumber);
            int lastPlayed = isLastStage ? 0 : stageNumber + 1;
            Write(new SaveData(lastPlayed, highestCleared));
        }

        /// <summary>
        /// 새 게임: 플레이 기록과 클리어 기록을 모두 지운다 (세이브 파일 삭제 = "기록 없음").
        /// 값을 0으로 쓰지 않고 파일을 지우는 이유는, Load가 이미 "파일 없음"을 기록 없음으로 다루기 때문이다.
        /// </summary>
        public static void DeleteAll()
        {
            if (UsePlayerPrefs)
            {
                PlayerPrefs.DeleteKey(PrefsKey);
                PlayerPrefs.Save();
                Debug.Log("[SaveDataStore] 세이브 삭제 (PlayerPrefs)");
                return;
            }

            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                // 쓰다가 꺼져서 남은 임시 파일도 함께 지운다
                string tempPath = path + ".tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                Debug.Log($"[SaveDataStore] 세이브 삭제: {path}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[SaveDataStore] 세이브 파일을 지우지 못했습니다: {path}\n{e.Message}");
            }
        }

        // 파일 내용을 그대로 읽는다. 둘 다 0인 경우도 돌려준다 (쓰기 전에 기존 값을 합치기 위해).
        private static SaveData LoadRaw()
        {
            if (UsePlayerPrefs)
            {
                if (!PlayerPrefs.HasKey(PrefsKey))
                {
                    return null;
                }
                Dictionary<string, string> prefValues = ParseKeyValues(PlayerPrefs.GetString(PrefsKey).Split('\n'));
                TryGetInt(prefValues, KeyLastPlayedStage, out int prefLastPlayed);
                TryGetInt(prefValues, KeyHighestClearedStage, out int prefHighestCleared);
                return new SaveData(Mathf.Max(0, prefLastPlayed), Mathf.Max(0, prefHighestCleared));
            }

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
                return new SaveData(Mathf.Max(0, lastPlayed), Mathf.Max(0, highestCleared));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[SaveDataStore] 세이브 파일을 읽지 못해 기록 없음으로 처리합니다: {path}\n{e.Message}");
                return null;
            }
        }

        private static void Write(SaveData save)
        {
            string path = FilePath;
            string tempPath = path + ".tmp";
            string text =
                "Key,Value\n" +
                $"{KeyLastPlayedStage},{save.LastPlayedStage.ToString(CultureInfo.InvariantCulture)}\n" +
                $"{KeyHighestClearedStage},{save.HighestClearedStage.ToString(CultureInfo.InvariantCulture)}\n";

            if (UsePlayerPrefs)
            {
                PlayerPrefs.SetString(PrefsKey, text);
                PlayerPrefs.Save();
                Debug.Log($"[SaveDataStore] 저장 (PlayerPrefs): LastPlayedStage={save.LastPlayedStage}, HighestClearedStage={save.HighestClearedStage}");
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // 쓰다가 꺼져도 기존 세이브가 깨지지 않도록 임시 파일에 먼저 쓰고 바꿔 끼운다
                File.WriteAllText(tempPath, text);
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                Debug.Log($"[SaveDataStore] 저장: LastPlayedStage={save.LastPlayedStage}, HighestClearedStage={save.HighestClearedStage} ({path})");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[SaveDataStore] 세이브 파일을 쓰지 못했습니다: {path}\n{e.Message}");
            }
        }

        private static Dictionary<string, string> ParseKeyValues(string[] lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in lines)
            {
                // 엑셀의 "CSV UTF-8" 저장은 파일 맨 앞에 BOM을 붙인다
                string line = rawLine.Trim().TrimStart('\uFEFF');
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