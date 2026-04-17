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
        
        [Header("Warmth Weights (웜/쿨 판별 부위별 가중치)")]
        [Tooltip("피부 노란기 반영 비율")]
        [SerializeField] private float skinWarmWeight;
        [Tooltip("머리카락 노란기 반영 비율")]
        [SerializeField] private float hairWarmWeight;
        [Tooltip("눈동자 노란기 반영 비율")]
        [SerializeField] private float eyeWarmWeight;

        /// <summary>
        /// 피부, 눈동자, 머리카락 색상을 종합하여 8종 퍼스널 컬러 타입을 판별한다.
        /// Inspector에서 설정된 부위별 가중치를 정규화하여 웜/쿨 판별식에 유동적으로 적용하기 위함.
        /// </summary>
        /// <param name="skin">피부 평균 색상</param>
        /// <param name="eye">눈동자 평균 색상</param>
        /// <param name="hair">머리카락 평균 색상</param>
        /// <returns>판별된 퍼스널 컬러 타입</returns>
        public PersonalColorType Classify(Color skin, Color eye, Color hair)
        {
            Color.RGBToHSV(skin, out _, out float skinSat, out float skinVal);
            Color.RGBToHSV(eye, out _, out float eyeSat, out float eyeVal);

            float totalWeight = skinWarmWeight + hairWarmWeight + eyeWarmWeight;
            float sRatio = 0.6f;
            float hRatio = 0.25f;
            float eRatio = 0.15f;

            // 가중치 총합이 0인 경우(인스펙터 미설정)를 대비해 폴백(Fallback) 수치를 적용함.
            // 총합이 0보다 클 경우 입력된 수치를 1 기준으로 정규화함.
            // 예시: skinW=60, hairW=25, eyeW=15 입력 시 총합 100.
            // 결과: sRatio=0.6, hRatio=0.25, eRatio=0.15
            if (totalWeight > 0f)
            {
                sRatio = skinWarmWeight / totalWeight;
                hRatio = hairWarmWeight / totalWeight;
                eRatio = eyeWarmWeight / totalWeight;
            }
            else
            {
                Debug.LogWarning("가중치 총합이 0이므로 기본 가중치(0.6, 0.25, 0.15)를 적용합니다.");
            }

            // 피부, 머리카락, 눈동자의 노란기(G-B)를 정규화된 가중치로 합산하여 웜/쿨을 판별함.
            // 예시: 피부노란기=0.05, 머리노란기=0.02, 눈노란기=0.03 (가중치 0.6, 0.25, 0.15 적용)
            // 결과: (0.05 * 0.6) + (0.02 * 0.25) + (0.03 * 0.15) = 0.0395
            float warmScore = (skin.g - skin.b) * sRatio + (hair.g - hair.b) * hRatio + (eye.g - eye.b) * eRatio;

            // 피부 밝기와 눈동자 밝기를 혼합하여 전체적인 명도를 산출함.
            // 예시: 피부V=0.8, 눈V=0.2
            // 결과: (0.8 * 0.8) + (0.2 * 0.2) = 0.68
            float blendedVal = (skinVal * 0.8f) + (eyeVal * 0.2f);

            // 피부 채도와 눈동자 채도를 혼합하여 선명도를 산출함.
            // 예시: 피부S=0.3, 눈S=0.4
            // 결과: (0.3 * 0.8) + (0.4 * 0.2) = 0.32
            float blendedSat = (skinSat * 0.8f) + (eyeSat * 0.2f);

            bool isWarm  = warmScore  > warmScoreThreshold;
            bool isLight = blendedVal >= lightValueThreshold;
            bool isDeep  = blendedVal <  deepValueThreshold;
            bool isVivid = blendedSat >= vividSatThreshold;

            PersonalColorType result = DetermineType(isWarm, isLight, isDeep, isVivid);

            Debug.Log($"[퍼스널컬러 판별] WarmScore={warmScore:F3} Val={blendedVal:F3} Sat={blendedSat:F3} " +
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
