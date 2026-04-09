using System.Collections.Generic;
using UnityEngine;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.ImageSegmenter;
using MpImage = Mediapipe.Image;

/// <summary>
/// 캡처된 이미지에서 피부색, 눈동자색, 머리카락색을 분석한다.
/// </summary>
public class PersonalColorAnalyzer : MonoBehaviour
{
    [Header("Models")]
    [SerializeField] private TextAsset faceLandmarkerModel;
    [SerializeField] private TextAsset hairSegmenterModel;

    [Header("Sampling")]
    [SerializeField, Range(1, 30)] private int sampleRadius = 8;
    [SerializeField, Range(0f, 1f)] private float hairConfidenceThreshold = 0.5f;

    /// <summary>
    /// 색상 분석 결과를 담는 구조체.
    /// 피부, 눈동자, 머리카락의 분석 색상 데이터와 추출 좌표를 통합하여 관리하기 위함.
    /// </summary>
    public struct ColorResult
    {
        public UnityEngine.Color skin;
        public UnityEngine.Color eye;
        public UnityEngine.Color hair;
        public List<Vector2> sampledPoints;
        public bool isValid;
    }

    public ColorResult LastResult { get; private set; }

    // 478점 기반 얼굴 랜드마크 인덱스 정의
    private static readonly int[] _leftCheekIdx  = { 50, 117, 123, 205 };
    private static readonly int[] _rightCheekIdx = { 280, 346, 352, 425 };
    private static readonly int[] _foreheadIdx   = { 10, 151 };
    private static readonly int[] _leftIrisIdx   = { 468, 469, 470, 471, 472 };
    private static readonly int[] _rightIrisIdx  = { 473, 474, 475, 476, 477 };

    /// <summary>
    /// 캡처된 이미지와 랜드마크를 기반으로 피부, 눈동자, 머리카락 색상을 분석함.
    /// 추출에 사용된 픽셀 중심 좌표도 함께 수집하여 반환하기 위함.
    /// MpImage의 소유권 이전에 따른 ObjectDisposedException 방지를 위해 TextureFrame을 받아 매번 새로운 이미지를 빌드함.
    /// </summary>
    /// <param name="pixels">분석할 원본 이미지 픽셀</param>
    /// <param name="width">이미지 너비</param>
    /// <param name="height">이미지 높이</param>
    /// <param name="frame">MediaPipe 엔진용 이미지를 생성할 원본 텍스처 프레임</param>
    /// <returns>추출된 부위별 색상 및 좌표 결과 구조체</returns>
    public ColorResult Analyze(UnityEngine.Color32[] pixels, int width, int height, TextureFrame frame)
    {
        if (!faceLandmarkerModel || !hairSegmenterModel)
        {
            Debug.LogError("faceLandmarkerModel 또는 hairSegmenterModel 모델이 할당되지 않았습니다.");
            return default(ColorResult);
        }

        List<Vector2> points = new List<Vector2>();

        // FaceLandmarker 실행 전 새로운 MpImage 생성 (소유권 이전 대비)
        using MpImage faceImage = frame.BuildCPUImage();
        FaceLandmarkerResult landmarkResult = RunFaceLandmarker(faceImage);
        
        if (landmarkResult.faceLandmarks == null || landmarkResult.faceLandmarks.Count == 0)
        {
            Debug.LogWarning("얼굴 랜드마크를 찾지 못했습니다.");
            return default(ColorResult);
        }

        IList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> landmarks = landmarkResult.faceLandmarks[0].landmarks;
        int lmCount = landmarks.Count;

        UnityEngine.Color skin = SampleLandmarkRegions(pixels, width, height, landmarks, lmCount, points,
            _leftCheekIdx, _rightCheekIdx, _foreheadIdx);

        UnityEngine.Color eye = SampleLandmarkRegions(pixels, width, height, landmarks, lmCount, points,
            _leftIrisIdx, _rightIrisIdx);

        // HairSegmenter 실행 전 새로운 MpImage 생성
        using MpImage hairImage = frame.BuildCPUImage();
        UnityEngine.Color hair = SampleHairColor(pixels, width, height, hairImage, points);

        ColorResult result = new ColorResult { skin = skin, eye = eye, hair = hair, sampledPoints = points, isValid = true };
        LastResult = result;

        Debug.Log($"[퍼스널컬러] 피부: {ColorToHex(skin)}  눈동자: {ColorToHex(eye)}  머리카락: {ColorToHex(hair)}");
        return result;
    }

    /// <summary>
    /// Face Landmarker 모델을 실행하여 얼굴 특징점 데이터를 추출함.
    /// 추출된 랜드마크를 통해 피부 및 눈동자 영역의 픽셀 좌표를 산출하기 위함.
    /// </summary>
    /// <param name="mpImage">MediaPipe 엔진용 이미지 객체</param>
    /// <returns>얼굴 랜드마크 분석 결과</returns>
    private FaceLandmarkerResult RunFaceLandmarker(MpImage mpImage)
    {
        Mediapipe.Tasks.Core.BaseOptions baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
            Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
            modelAssetBuffer: faceLandmarkerModel.bytes);

        FaceLandmarkerOptions options = new FaceLandmarkerOptions(
            baseOptions,
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE,
            numFaces: 1);

        using FaceLandmarker landmarker = FaceLandmarker.CreateFromOptions(options);
        Mediapipe.Tasks.Vision.Core.ImageProcessingOptions ipo = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);
        return landmarker.Detect(mpImage, ipo);
    }

    /// <summary>
    /// Hair Segmenter를 실행하고 머리카락 영역의 평균 색상을 계산함.
    /// 추출 좌표를 수집하기 위해 points 리스트를 매개변수로 전달하기 위함.
    /// </summary>
    private UnityEngine.Color SampleHairColor(UnityEngine.Color32[] pixels, int width, int height, MpImage mpImage, List<Vector2> outPoints)
    {
        Mediapipe.Tasks.Core.BaseOptions baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
            Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
            modelAssetBuffer: hairSegmenterModel.bytes);

        ImageSegmenterOptions options = new ImageSegmenterOptions(
            baseOptions,
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE,
            outputCategoryMask: false,
            outputConfidenceMasks: true);

        using ImageSegmenter segmenter = ImageSegmenter.CreateFromOptions(options);
        Mediapipe.Tasks.Vision.Core.ImageProcessingOptions ipo = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);
        ImageSegmenterResult result = segmenter.Segment(mpImage, ipo);

        if (result.confidenceMasks == null || result.confidenceMasks.Count < 2)
        {
            Debug.LogWarning("머리카락 마스크를 얻지 못했습니다.");
            if (result.confidenceMasks != null)
            {
                foreach (MpImage m in result.confidenceMasks)
                {
                    m.Dispose();
                }
            }
            return UnityEngine.Color.black;
        }

        MpImage hairMask = result.confidenceMasks[1];
        
        int mw = hairMask.Width();
        int mh = hairMask.Height();
        float[] maskFloats = new float[mw * mh];
        
        hairMask.TryReadChannelNormalized(0, maskFloats);

        foreach (MpImage m in result.confidenceMasks)
        {
            m.Dispose();
        }

        return AverageColorByMask(pixels, width, height, maskFloats, mw, mh, hairConfidenceThreshold, outPoints);
    }

    /// <summary>
    /// 지정된 랜드마크 그룹의 주변 픽셀을 순회하여 평균 색상을 추출함.
    /// 분석의 기준이 된 랜드마크의 중심 좌표를 outPoints 리스트에 저장하기 위함.
    /// </summary>
    private UnityEngine.Color SampleLandmarkRegions(UnityEngine.Color32[] pixels, int w, int h,
        IList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> landmarks, int lmCount, List<Vector2> outPoints,
        params int[][] indexGroups)
    {
        float r = 0f;
        float g = 0f;
        float b = 0f;
        int count = 0;

        foreach (int[] group in indexGroups)
        {
            foreach (int idx in group)
            {
                if (idx >= lmCount) continue;

                Mediapipe.Tasks.Components.Containers.NormalizedLandmark lm = landmarks[idx];
                int cx = Mathf.RoundToInt(lm.x * (w - 1));
                int cy = Mathf.RoundToInt(lm.y * (h - 1));

                if (outPoints != null)
                {
                    outPoints.Add(new Vector2(cx, cy));
                }

                for (int dy = -sampleRadius; dy <= sampleRadius; dy++)
                {
                    for (int dx = -sampleRadius; dx <= sampleRadius; dx++)
                    {
                        if (dx * dx + dy * dy > sampleRadius * sampleRadius) continue;
                        int px = Mathf.Clamp(cx + dx, 0, w - 1);
                        int py = Mathf.Clamp(cy + dy, 0, h - 1);
                        UnityEngine.Color32 c = pixels[py * w + px];
                        
                        if (c.a < 128) continue;
                        
                        r += c.r; 
                        g += c.g; 
                        b += c.b;
                        count++;
                    }
                }
            }
        }

        if (count == 0) return UnityEngine.Color.gray;
        return new UnityEngine.Color(r / count / 255f, g / count / 255f, b / count / 255f);
    }

   /// <summary>
    /// 세그멘테이션 마스크 가중치가 임계값을 넘는 픽셀들의 평균 색상을 추출함.
    /// 머리카락 영역 전체의 평균 좌표가 이마로 쏠리는 현상을 방지하기 위해, 영역의 바운딩 박스를 계산하여 최상단(정수리) 근처를 대표 좌표로 저장하기 위함.
    /// </summary>
    private static UnityEngine.Color AverageColorByMask(UnityEngine.Color32[] pixels, int pw, int ph,
        float[] mask, int mw, int mh, float threshold, List<Vector2> outPoints)
    {
        float r = 0f;
        float g = 0f;
        float b = 0f;
        int count = 0;
        
        int minX = pw;
        int maxX = 0;
        int minY = ph;
        int maxY = 0;

        for (int y = 0; y < ph; y++)
        {
            for (int x = 0; x < pw; x++)
            {
                float mu = (float)x / Mathf.Max(pw - 1, 1) * (mw - 1);
                float mv = (float)y / Mathf.Max(ph - 1, 1) * (mh - 1);
                
                int mx0 = Mathf.Clamp(Mathf.FloorToInt(mu), 0, mw - 1);
                int mx1 = Mathf.Clamp(mx0 + 1, 0, mw - 1);
                int my0 = Mathf.Clamp(Mathf.FloorToInt(mv), 0, mh - 1);
                int my1 = Mathf.Clamp(my0 + 1, 0, mh - 1);
                
                float tx = mu - mx0;
                float ty = mv - my0;
                
                float conf = Mathf.Lerp(
                    Mathf.Lerp(mask[my0 * mw + mx0], mask[my0 * mw + mx1], tx),
                    Mathf.Lerp(mask[my1 * mw + mx0], mask[my1 * mw + mx1], tx), ty);

                if (conf < threshold) continue;
                
                UnityEngine.Color32 c = pixels[y * pw + x];
                if (c.a < 128) continue;
                
                r += c.r; 
                g += c.g; 
                b += c.b;
                count++;

                // 마스크 영역의 경계(바운딩 박스)를 구하기 위해 픽셀 좌표의 최솟값/최댓값을 갱신함
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (count > 0 && outPoints != null)
        {
            // MediaPipe 좌표계는 y=0이 화면의 최상단임
            // 따라서 minY가 머리카락의 가장 꼭대기(정수리)를 의미함
            // 시각적 대표성을 위해 좌우 중앙(X)과 최상단에서 15% 정도 아래 지점(Y)을 마커 위치로 산출함
            float repX = (minX + maxX) / 2f;
            float repY = Mathf.Lerp(minY, maxY, 0.15f);
            
            outPoints.Add(new Vector2(repX, repY));
        }

        if (count == 0) return UnityEngine.Color.black;
        return new UnityEngine.Color(r / count / 255f, g / count / 255f, b / count / 255f);
    }

    /// <summary>
    /// Color 데이터를 Hex 문자열로 변환함.
    /// 디버그 로그에서 색상을 직관적으로 확인하기 위함.
    /// </summary>
    /// <param name="c">변환할 색상</param>
    /// <returns>Hex 컬러 포맷 문자열</returns>
    private static string ColorToHex(UnityEngine.Color c)
    {
        UnityEngine.Color32 c32 = (UnityEngine.Color32)c;
        return $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}";
    }
}