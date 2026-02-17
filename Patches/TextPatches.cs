using System.Collections;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MuckKorean.Patches
{
    // 비활성 상태에서 설정된 한글 TMP 텍스트의 메시를 활성화 후 재생성
    public class DelayedMeshUpdater : MonoBehaviour
    {
        void OnEnable()
        {
            StartCoroutine(RebuildAfterLayout());
        }

        private IEnumerator RebuildAfterLayout()
        {
            yield return new WaitForEndOfFrame();
            yield return null;

            var tmp = GetComponent<TMP_Text>();
            if (tmp != null && tmp.gameObject.activeInHierarchy)
                tmp.ForceMeshUpdate();
            Destroy(this);
        }
    }

    internal static class KoreanTextHelper
    {
        /// <summary>
        /// 한글 폰트 메트릭이 원본 폰트(Roboto 등)보다 높아서
        /// Ellipsis/Truncate 오버플로 모드에서 텍스트가 rect를 초과하면 charCount=0이 되는 문제 수정.
        /// autoSizing을 활성화하여 폰트 크기를 약간 줄여 맞춤.
        /// </summary>
        internal static void FixOverflowIfNeeded(TMP_Text tmp)
        {
            if (tmp == null || tmp.enableAutoSizing)
                return;

            if (tmp.overflowMode == TextOverflowModes.Ellipsis ||
                tmp.overflowMode == TextOverflowModes.Truncate)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = Mathf.Max(1, tmp.fontSize - 4);
                tmp.fontSizeMax = tmp.fontSize;
            }
        }

        internal static bool ContainsKorean(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c >= '\uAC00' && c <= '\uD7A3')
                    return true;
            }
            return false;
        }
    }

    internal static class PatchLog
    {
        internal static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("MuckKorean.Patch");
    }

    // FontEngine 패치: OS 동적 폰트 대신 파일 경로에서 직접 로드
    // TMP_FontAsset.CreateFontAsset() 및 동적 글리프 생성 시 호출됨
    [HarmonyPatch(typeof(FontEngine), "LoadFontFace", new[] { typeof(Font), typeof(int) })]
    public static class FontEngine_LoadFontFace_Patch
    {
        static bool Prefix(Font font, int pointSize, ref FontEngineError __result)
        {
            if (font == null || FontManager.SourceFont == null || FontManager.FontFilePath == null)
                return true;

            if (font == FontManager.SourceFont)
            {
                // OS 폰트 대신 파일 경로에서 직접 로드
                __result = FontEngine.LoadFontFace(FontManager.FontFilePath, pointSize);
                return false; // 원본 메서드 스킵
            }

            return true; // 다른 폰트는 원본 그대로 실행
        }
    }

    // 핵심 패치: 코드에서 텍스트를 설정할 때 번역 + 폰트 적용
    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    public static class TMP_Text_SetText_Patch
    {
        static void Prefix(TMP_Text __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            string translated = TranslationManager.Translate(value);
            if (translated != value)
            {
                FontManager.ApplyFont(__instance);
                value = translated;

                if (KoreanTextHelper.ContainsKorean(translated))
                {
                    // 한글 폰트 메트릭이 원본보다 높아서 Ellipsis 모드에서 안 보이는 문제 수정
                    KoreanTextHelper.FixOverflowIfNeeded(__instance);

                    // 비활성 상태에서 한글 설정 시 → 활성화 후 메시 재생성
                    if (!__instance.gameObject.activeInHierarchy)
                    {
                        if (__instance.gameObject.GetComponent<DelayedMeshUpdater>() == null)
                            __instance.gameObject.AddComponent<DelayedMeshUpdater>();
                    }
                }
            }
            else
            {
                TranslationManager.DumpMissedKey(value);
            }
        }
    }

    // TMP 컴포넌트가 활성화될 때 이미 설정된 텍스트 번역 + 폰트 적용
    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    public static class TMP_UGUI_OnEnable_Patch
    {
        static void Postfix(TextMeshProUGUI __instance)
        {
            FontManager.ApplyFont(__instance);
            TranslateExistingText(__instance);

            // 비활성→활성 전환 시 한글 텍스트의 레이아웃+메시 리빌드 요청
            if (__instance != null && !string.IsNullOrEmpty(__instance.text)
                && KoreanTextHelper.ContainsKorean(__instance.text))
            {
                KoreanTextHelper.FixOverflowIfNeeded(__instance);
                __instance.SetAllDirty();
            }
        }

        static void TranslateExistingText(TMP_Text tmp)
        {
            if (tmp == null || string.IsNullOrEmpty(tmp.text))
                return;

            string translated = TranslationManager.Translate(tmp.text);
            if (translated != tmp.text)
                tmp.text = translated;
        }
    }

    // 월드 스페이스 TMP에도 동일 적용
    [HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
    public static class TMP_OnEnable_Patch
    {
        static void Postfix(TextMeshPro __instance)
        {
            FontManager.ApplyFont(__instance);

            if (__instance != null && !string.IsNullOrEmpty(__instance.text))
            {
                string translated = TranslationManager.Translate(__instance.text);
                if (translated != __instance.text)
                    __instance.text = translated;
            }
        }
    }

    // 기존 UI.Text 컴포넌트 (TMP가 아닌 레거시 텍스트)
    [HarmonyPatch(typeof(UnityEngine.UI.Text), "set_text")]
    public static class UIText_SetText_Patch
    {
        static void Prefix(UnityEngine.UI.Text __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            string translated = TranslationManager.Translate(value);
            if (translated != value)
            {
                PatchLog.Log.LogInfo($"[UI.Text] '{value}' → '{translated}' GO={__instance.gameObject.name} font={__instance.font?.name} fontSize={__instance.fontSize}");
                FontManager.ApplyFont(__instance, translated);
                value = translated;
            }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Text), "OnEnable")]
    public static class UIText_OnEnable_Patch
    {
        static void Postfix(UnityEngine.UI.Text __instance)
        {
            if (__instance == null || string.IsNullOrEmpty(__instance.text))
                return;

            string translated = TranslationManager.Translate(__instance.text);
            if (translated != __instance.text)
            {
                FontManager.ApplyFont(__instance, translated);
                __instance.text = translated;
            }
        }
    }

    // ItemInfo 툴팁 배경 크기 수정: text.mesh.bounds는 메인 폰트 메시만 포함
    // 한글은 fallback 폰트 서브메시에 있어서 크기 계산에서 빠짐
    // text.textBounds는 모든 문자(메인+fallback) 포함
    [HarmonyPatch(typeof(ItemInfo), "FitToText")]
    public static class ItemInfo_FitToText_Patch
    {
        static readonly AccessTools.FieldRef<ItemInfo, bool> leftCornerRef =
            AccessTools.FieldRefAccess<ItemInfo, bool>("leftCorner");
        static readonly AccessTools.FieldRef<ItemInfo, Vector3> defaultTextPosRef =
            AccessTools.FieldRefAccess<ItemInfo, Vector3>("defaultTextPos");

        static bool Prefix(ItemInfo __instance)
        {
            var text = __instance.text;
            if (text == null) return true;

            // textBounds: fallback 폰트 문자 포함, mesh.bounds: 메인 폰트만
            var bounds = text.textBounds;
            Vector2 sizeDelta = new Vector2(bounds.size.x + __instance.padding, bounds.size.y + __instance.padding);

            bool leftCorner = leftCornerRef(__instance);
            Vector3 defaultTextPos = defaultTextPosRef(__instance);

            if (leftCorner)
                text.transform.localPosition = -defaultTextPos - new Vector3(sizeDelta.x, sizeDelta.y, 0f);
            else
                text.transform.localPosition = defaultTextPos;

            __instance.image.rectTransform.sizeDelta = sizeDelta;
            __instance.image.rectTransform.position = text.rectTransform.position;
            Vector3 vector = new Vector3(__instance.padding / 2f, 0f, 0f);
            __instance.image.rectTransform.localPosition = text.rectTransform.localPosition - vector;

            return false; // 원본 메서드 스킵
        }
    }

    // TextMesh (레거시 3D 텍스트) - 아이템 이름 등 월드 공간 텍스트
    [HarmonyPatch(typeof(TextMesh), "set_text")]
    public static class TextMesh_SetText_Patch
    {
        static void Prefix(TextMesh __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            string translated = TranslationManager.Translate(value);
            if (translated != value)
            {
                PatchLog.Log.LogInfo($"[TextMesh] '{value}' → '{translated}' GO={__instance.gameObject.name}");
                FontManager.ApplyFont(__instance, translated);
                value = translated;
            }
            else
            {
                // TextMesh에서 번역 안 된 텍스트도 로그
                if (value.Length < 30 && !string.IsNullOrWhiteSpace(value))
                    PatchLog.Log.LogInfo($"[TextMesh] 미번역: '{value}' GO={__instance.gameObject.name}");
            }
        }
    }
}
