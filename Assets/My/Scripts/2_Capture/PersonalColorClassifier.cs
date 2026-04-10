using UnityEngine;

namespace My.Scripts._2_Capture
{
    /// <summary>
    /// 피부·눈동자·머리카락 색상으로부터 퍼스널 컬러 타입을 판별한다.
    /// Inspector에서 임계값을 조정할 수 있도록 MonoBehaviour로 구현됨.
    /// </summary>
    public class PersonalColorClassifier : MonoBehaviour
    {
        [Header("Warmth (피부의 따뜻함 판별)")]
        [Tooltip("skin.g - skin.b 값이 이 임계값보다 크면 Warm(봄·가을)으로 분류")]
        [SerializeField] private float warmScoreThreshold = 0.04f;

        [Header("Brightness (피부 밝기 판별)")]
        [Tooltip("피부 HSV Value가 이 값 이상이면 Light(봄·여름)으로 분류")]
        [SerializeField] private float lightValueThreshold = 0.67f;
        [Tooltip("피부 HSV Value가 이 값 미만이면 Deep(가을 Deep·겨울 Deep)으로 분류")]
        [SerializeField] private float deepValueThreshold = 0.54f;

        [Header("Chroma (선명도 판별)")]
        [Tooltip("피부 HSV Saturation이 이 값 이상이면 Vivid/Clear(봄 Bright·겨울 Bright)로 분류")]
        [SerializeField] private float vividSatThreshold = 0.27f;

        /// <summary>
        /// 피부·눈동자·머리카락 색상을 기반으로 8종 퍼스널 컬러 타입을 판별한다.
        /// </summary>
        /// <param name="skin">피부 평균 색상</param>
        /// <param name="eye">눈동자 평균 색상</param>
        /// <param name="hair">머리카락 평균 색상</param>
        /// <returns>판별된 퍼스널 컬러 타입</returns>
        public PersonalColorType Classify(Color skin, Color eye, Color hair)
        {
            Color.RGBToHSV(skin, out _, out float skinSat, out float skinVal);

            // 피부 Yellow 성분(G-B)과 머리카락 Yellow 성분의 가중 평균으로 따뜻함을 산출
            // 노란/주황 톤(Warm)이면 G > B, 핑크/블루 톤(Cool)이면 G ≈ B 이하
            float warmScore = (skin.g - skin.b) * 0.7f + (hair.g - hair.b) * 0.3f;

            bool isWarm  = warmScore  > warmScoreThreshold;
            bool isLight = skinVal   >= lightValueThreshold;
            bool isDeep  = skinVal   <  deepValueThreshold;
            bool isVivid = skinSat   >= vividSatThreshold;

            PersonalColorType result = DetermineType(isWarm, isLight, isDeep, isVivid);

            Debug.Log($"[퍼스널컬러 판별] WarmScore={warmScore:F3} SkinVal={skinVal:F3} SkinSat={skinSat:F3} " +
                      $"→ Warm={isWarm} Light={isLight} Deep={isDeep} Vivid={isVivid} → {result}");

            return result;
        }

        /// <summary>
        /// 따뜻함·밝기·선명도 플래그 조합으로 퍼스널 컬러 타입을 결정한다.
        /// 결정 트리:
        ///   Warm  + Light + Vivid → SpringBright
        ///   Warm  + Light + Muted → SpringLight
        ///   Warm  + Deep          → AutumnDeep
        ///   Warm  + Medium        → AutumnMute
        ///   Cool  + Light         → SummerLight
        ///   Cool  + Deep  + Vivid → WinterBright
        ///   Cool  + Deep  + Muted → WinterDeep
        ///   Cool  + Medium        → SummerMute
        /// </summary>
        private static PersonalColorType DetermineType(bool isWarm, bool isLight, bool isDeep, bool isVivid)
        {
            if (isWarm)
            {
                if (isLight) return isVivid ? PersonalColorType.SpringBright : PersonalColorType.SpringLight;
                if (isDeep)  return PersonalColorType.AutumnDeep;
                return PersonalColorType.AutumnMute;
            }
            else
            {
                if (isLight) return PersonalColorType.SummerLight;
                if (isDeep)  return isVivid ? PersonalColorType.WinterBright : PersonalColorType.WinterDeep;
                return PersonalColorType.SummerMute;
            }
        }
    }
}
