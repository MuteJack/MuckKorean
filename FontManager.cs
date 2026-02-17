using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MuckKorean
{
    public static class FontManager
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("MuckKorean.Font");
        public static TMP_FontAsset KoreanFont { get; private set; }
        public static Font SourceFont { get; private set; }
        public static string FontFilePath { get; private set; }
        private static bool _initialized;

        // 1단계: 폰트 파일 찾기 + Font 오브젝트 생성 (패치 적용 전에 호출)
        public static void PreInitialize(string pluginPath)
        {
            // 시스템 폰트 폴더에서 malgun.ttf 찾기
            string fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            FontFilePath = Path.Combine(fontDir, "malgun.ttf");

            if (!File.Exists(FontFilePath))
            {
                Log.LogWarning($"malgun.ttf를 찾을 수 없습니다: {FontFilePath}");
                FontFilePath = null;
                return;
            }

            SourceFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 44);
            if (SourceFont == null)
            {
                Log.LogWarning("Font.CreateDynamicFontFromOSFont 실패");
                return;
            }

            UnityEngine.Object.DontDestroyOnLoad(SourceFont);

            // 동적 폰트 아틀라스 리빌드 시 TextMesh 머티리얼 텍스처 갱신
            Font.textureRebuilt += OnFontTextureRebuilt;

            Log.LogInfo($"맑은 고딕 폰트 준비 완료: {FontFilePath}");
        }

        private static void OnFontTextureRebuilt(Font rebuiltFont)
        {
            if (rebuiltFont != SourceFont)
                return;

            var textMeshes = UnityEngine.Object.FindObjectsOfType<TextMesh>();
            foreach (var tm in textMeshes)
            {
                if (tm == null || tm.font != SourceFont)
                    continue;

                var renderer = tm.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                    renderer.material.mainTexture = SourceFont.material.mainTexture;
            }
        }

        // 2단계: TMP 폰트 에셋 생성 (패치 적용 후에 호출 - FontEngine 패치가 활성 상태)
        public static void CreateFontAsset()
        {
            if (SourceFont == null || FontFilePath == null)
                return;

            // FontEngine 패치 덕분에 CreateFontAsset 내부의
            // FontEngine.LoadFontFace(Font, int) 호출이 파일 경로 로딩으로 우회됨
            KoreanFont = TMP_FontAsset.CreateFontAsset(SourceFont);

            if (KoreanFont == null)
            {
                Log.LogWarning("TMP_FontAsset 생성 실패 (패치 적용 후에도)");
                return;
            }

            KoreanFont.name = "KoreanFont-Dynamic";
            UnityEngine.Object.DontDestroyOnLoad(KoreanFont);

            if (KoreanFont.atlasTextures != null)
            {
                foreach (var tex in KoreanFont.atlasTextures)
                    if (tex != null) UnityEngine.Object.DontDestroyOnLoad(tex);
            }
            if (KoreanFont.material != null)
                UnityEngine.Object.DontDestroyOnLoad(KoreanFont.material);

            // 번역에 사용되는 모든 한글 문자를 아틀라스에 미리 등록
            PrePopulateAtlas();

            _initialized = true;
            Log.LogInfo("한글 TMP 폰트 에셋 생성 완료!");
        }

        private static void PrePopulateAtlas()
        {
            // 번역 값에서 모든 고유 한글 문자 수집
            string allKoreanChars = TranslationManager.GetAllKoreanCharacters();
            if (string.IsNullOrEmpty(allKoreanChars))
                return;

            string missing;
            bool success = KoreanFont.TryAddCharacters(allKoreanChars, out missing);

            Log.LogInfo($"아틀라스 사전 등록: {allKoreanChars.Length}자 요청, 성공={success}");
            if (!string.IsNullOrEmpty(missing))
                Log.LogWarning($"아틀라스 등록 실패 문자 {missing.Length}자: {missing.Substring(0, Math.Min(20, missing.Length))}...");

            // 아틀라스 텍스처가 변경되었을 수 있으므로 DontDestroyOnLoad 재적용
            if (KoreanFont.atlasTextures != null)
            {
                foreach (var tex in KoreanFont.atlasTextures)
                    if (tex != null) UnityEngine.Object.DontDestroyOnLoad(tex);
            }
        }

        public static void ApplyFont(TextMesh textMesh, string translatedText)
        {
            if (SourceFont == null || textMesh == null || string.IsNullOrEmpty(translatedText))
                return;

            SourceFont.RequestCharactersInTexture(translatedText);
            textMesh.font = SourceFont;

            var renderer = textMesh.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null && SourceFont.material != null)
                renderer.material.mainTexture = SourceFont.material.mainTexture;
        }

        public static void ApplyFont(UnityEngine.UI.Text uiText, string translatedText)
        {
            if (SourceFont == null || uiText == null || string.IsNullOrEmpty(translatedText))
                return;

            SourceFont.RequestCharactersInTexture(translatedText, uiText.fontSize, uiText.fontStyle);
            uiText.font = SourceFont;
        }

        public static void ApplyFont(TMP_Text textComponent)
        {
            if (!_initialized || KoreanFont == null || textComponent == null)
                return;

            // 기존 폰트의 fallback 테이블에 한글 폰트 추가
            if (textComponent.font != null && textComponent.font != KoreanFont)
            {
                // KoreanFont 머티리얼 셰이더를 기존 폰트와 일치시킴 (TMP 폴백 렌더링 호환성)
                if (textComponent.font.material != null && KoreanFont.material != null
                    && textComponent.font.material.shader != KoreanFont.material.shader)
                {
                    KoreanFont.material.shader = textComponent.font.material.shader;
                }

                var fallbacks = textComponent.font.fallbackFontAssetTable;
                if (fallbacks == null)
                {
                    textComponent.font.fallbackFontAssetTable = new List<TMP_FontAsset> { KoreanFont };
                }
                else if (!fallbacks.Contains(KoreanFont))
                {
                    fallbacks.Add(KoreanFont);
                }
            }
            else if (textComponent.font == null)
            {
                textComponent.font = KoreanFont;
            }
        }
    }
}
