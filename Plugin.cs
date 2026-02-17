using BepInEx;
using HarmonyLib;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MuckKorean
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.muck.korean";
        public const string PluginName = "Muck Korean";
        public const string PluginVersion = "1.0.0";

        public static Plugin Instance { get; private set; }
        public static string PluginPath { get; private set; }

        private void Awake()
        {
            Instance = this;
            PluginPath = Path.GetDirectoryName(Info.Location);

            Logger.LogInfo($"{PluginName} v{PluginVersion} 로딩 중...");

            // 1) 번역 로드
            TranslationManager.Load(PluginPath);
            Logger.LogInfo($"번역 항목 {TranslationManager.Count}개 로드 완료");

            // 2) 폰트 사전 준비 (파일 경로 확인 + Font 오브젝트 생성)
            FontManager.PreInitialize(PluginPath);

            // 3) Harmony 패치 적용 (FontEngine 패치 포함)
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll();
            Logger.LogInfo("Harmony 패치 적용 완료");

            // 4) TMP 폰트 에셋 생성 (FontEngine 패치가 활성화된 상태에서)
            FontManager.CreateFontAsset();

            // 5) 씬 로드 이벤트 등록
            SceneManager.sceneLoaded += OnSceneLoaded;

            Logger.LogInfo($"{PluginName} 로드 완료!");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"씬 로드됨: {scene.name} - 텍스트 번역 적용 중...");
            StartCoroutine(TranslateAllTextDelayed());
        }

        private IEnumerator TranslateAllTextDelayed()
        {
            yield return null;
            yield return null;
            TranslateAllActiveText();
            yield return new WaitForSeconds(0.5f);
            TranslateAllActiveText();
        }

        private void TranslateAllActiveText()
        {
            int translated = 0;

            // 디버그: 씬 내 모든 텍스트 컴포넌트 타입 로그
            var allTMP = FindObjectsOfType<TMP_Text>();
            var allTextMesh = FindObjectsOfType<TextMesh>();
            var allUIText = FindObjectsOfType<UnityEngine.UI.Text>();
            Logger.LogInfo($"[DEBUG] 씬 컴포넌트: TMP={allTMP.Length}, TextMesh={allTextMesh.Length}, UI.Text={allUIText.Length}");

            foreach (var tm in allTextMesh)
            {
                if (tm != null && !string.IsNullOrEmpty(tm.text))
                    Logger.LogInfo($"[DEBUG] TextMesh: '{tm.text}' GO={tm.gameObject.name} font={tm.font?.name}");
            }

            var tmpTexts = FindObjectsOfType<TMP_Text>();
            foreach (var tmp in tmpTexts)
            {
                if (tmp == null) continue;

                FontManager.ApplyFont(tmp);

                if (!string.IsNullOrEmpty(tmp.text))
                {
                    string result = TranslationManager.Translate(tmp.text);
                    if (result != tmp.text)
                    {
                        tmp.text = result;
                        translated++;
                    }
                }
            }

            var uiTexts = FindObjectsOfType<UnityEngine.UI.Text>();
            foreach (var uiText in uiTexts)
            {
                if (uiText == null || string.IsNullOrEmpty(uiText.text))
                    continue;

                string result = TranslationManager.Translate(uiText.text);
                if (result != uiText.text)
                {
                    uiText.text = result;
                    translated++;
                }
            }

            var textMeshes = FindObjectsOfType<TextMesh>();
            foreach (var tm in textMeshes)
            {
                if (tm == null || string.IsNullOrEmpty(tm.text))
                    continue;

                string result = TranslationManager.Translate(tm.text);
                if (result != tm.text)
                {
                    FontManager.ApplyFont(tm, result);
                    tm.text = result;
                    translated++;
                }
            }

            if (translated > 0)
                Logger.LogInfo($"씬 스캔: {translated}개 텍스트 번역 적용");
        }
    }
}
