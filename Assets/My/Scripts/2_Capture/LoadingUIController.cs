using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로딩 화면의 UI 애니메이션을 제어한다.
/// 외부에서 전달받은 진행률에 따라 외곽 링의 FillAmount를 채우고, 
/// 내부 별들은 무작위 타이밍으로 크기를 조절하여 시각적 단조로움을 피하기 위함.
/// </summary>
public class LoadingUIController : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private Image outerRing;
    [SerializeField] private RectTransform[] innerStars;

    [Header("Animation Settings")]
    [SerializeField] private float minStarScale;
    [SerializeField] private float maxStarScale;
    [SerializeField] private float minStarSpeed;
    [SerializeField] private float maxStarSpeed;

    private float[] _starTimeOffsets;
    private float[] _starSpeeds;

    private void Start()
    {
        if (!outerRing)
        {
            Debug.LogError("outerRing 할당 누락됨.");
        }
        else
        {
            // 인스펙터 설정 누락을 대비하여 코드로 Filled 타입 및 360도 채우기를 강제 설정함
            outerRing.type = Image.Type.Filled;
            outerRing.fillMethod = Image.FillMethod.Radial360;
            outerRing.fillOrigin = (int)Image.Origin360.Bottom;
            outerRing.fillAmount = 0f;
        }

        if (innerStars == null || innerStars.Length == 0)
        {
            Debug.LogError("innerStars 배열이 비어있음.");
            return;
        }

        int length = innerStars.Length;
        _starTimeOffsets = new float[length];
        _starSpeeds = new float[length];

        for (int i = 0; i < length; i++)
        {
            _starTimeOffsets[i] = UnityEngine.Random.Range(0f, 100f);
            _starSpeeds[i] = UnityEngine.Random.Range(minStarSpeed, maxStarSpeed);
        }
    }

    private void Update()
    {
        if (innerStars != null)
        {
            for (int i = 0; i < innerStars.Length; i++)
            {
                RectTransform star = innerStars[i];
                
                if (star)
                {
                    float t = (Time.time + _starTimeOffsets[i]) * _starSpeeds[i];
                    float normalizedSin = (Mathf.Sin(t) + 1f) / 2f;
                    float currentScale = Mathf.Lerp(minStarScale, maxStarScale, normalizedSin);

                    star.localScale = new Vector3(currentScale, currentScale, 1f);
                }
            }
        }
    }

    /// <summary>
    /// 외부에서 로딩 진행률을 전달받아 외곽 링의 시각적 채움 정도를 갱신한다.
    /// 퍼센트 텍스트와 UI 애니메이션을 정확히 동기화하기 위함.
    /// </summary>
    /// <param name="progress">0.0f ~ 1.0f 사이의 진행률 값</param>
    public void SetProgress(float progress)
    {
        if (outerRing)
        {
            outerRing.fillAmount = Mathf.Clamp01(progress);
        }
    }
}