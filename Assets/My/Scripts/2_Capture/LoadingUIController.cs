using UnityEngine;

/// <summary>
/// 로딩 화면의 UI 애니메이션을 제어한다.
/// 외곽 링은 일정한 속도로 회전시키고 내부의 별 3개는 각각 무작위 타이밍으로 크기를 조절하여 시각적 단조로움을 피하기 위함.
/// </summary>
public class LoadingUIController : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private RectTransform outerRing;
    [SerializeField] private RectTransform[] innerStars;

    [Header("Animation Settings")]
    [SerializeField] private float ringSpeed;
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
            // 각 별이 동시에 커지거나 작아지지 않도록 시작 시간과 재생 속도에 난수를 부여함.
            _starTimeOffsets[i] = UnityEngine.Random.Range(0f, 100f);
            _starSpeeds[i] = UnityEngine.Random.Range(minStarSpeed, maxStarSpeed);
        }
    }

    private void Update()
    {
        if (outerRing)
        {
            outerRing.Rotate(0f, 0f, ringSpeed * Time.deltaTime);
        }

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
}