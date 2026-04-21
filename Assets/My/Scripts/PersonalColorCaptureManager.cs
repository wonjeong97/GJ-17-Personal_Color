using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Tasks.Vision.ImageSegmenter;
using My.Scripts._2_Capture;
using MpImage = Mediapipe.Image;
using Rect = UnityEngine.Rect;

/// <summary>
/// 웹캠을 통해 실시간 프리뷰를 제공하고, 캡처 시 배경을 분리하여 퍼스널 컬러 분석을 위한 최종 이미지를 생성한다.
/// 주기적인 프리뷰 마스크 업데이트 기능을 제거하고 캡처 시에만 연산을 수행하도록 최적화됨.
/// </summary>
public class PersonalColorCaptureManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage captureDisplay;

    [Header("Model")]
    [SerializeField] private TextAsset segmentationModelAsset;

    [Header("Settings")]
    [SerializeField, Range(0f, 1f)] private float maskThreshold = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebug;
    [SerializeField] private int debugPointSize = 9;

    // 퍼스널 컬러 분석기 참조
    [SerializeField] private PersonalColorAnalyzer colorAnalyzer;

    private WebCamTexture _webCamTexture;
    private ImageSegmenter _segmenter;
    private bool _captured = false;

    // 디버그 포인트 데이터를 저장할 구조체
    private struct DetectionPoint
    {
        public Vector2 pixelPos;
        public UnityEngine.Color color;

        public DetectionPoint(Vector2 pos, UnityEngine.Color col)
        {
            pixelPos = pos;
            color = col;
        }
    }

    private List<DetectionPoint> _debugPoints = new List<DetectionPoint>();

    // 샘플 씬과 동일하게 ImageProcessingOptions 고정
    private readonly static Mediapipe.Tasks.Vision.Core.ImageProcessingOptions _imageProcessingOptions =
        new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);

    private void Start()
    {
        StartCamera();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_captured) 
            {
                ResumeCamera();
            }
            else 
            {
                CaptureAndProcess();
            }
        }
    }

    private void OnDestroy()
    {
        if (_segmenter != null) 
        {
            ((System.IDisposable)_segmenter).Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 초기화 및 카메라 제어
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 카메라를 초기화하고 라이브 프리뷰를 시작함.
    /// 프리뷰 시작 시 카메라 원본(16:9)을 UI 비율(8:9)에 맞게 시각적 크롭을 적용하기 위함.
    /// </summary>
    private void StartCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0) 
        { 
            Debug.LogError("카메라를 찾을 수 없습니다."); 
            return; 
        }

        _webCamTexture = new WebCamTexture(devices[0].name, 1280, 720);
        captureDisplay.texture = _webCamTexture;
        _webCamTexture.Play();

        // 프리뷰 라이브 화면에 시각적 크롭 적용
        ApplyUVCropToRawImage(captureDisplay, 1280, 720, new Vector2(8, 9));

        if (!segmentationModelAsset) 
        { 
            Debug.LogError("segmentationModelAsset 이 할당되지 않았습니다."); 
            return; 
        }

        Mediapipe.Tasks.Core.BaseOptions baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
            Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
            modelAssetBuffer: segmentationModelAsset.bytes);

        _segmenter = ImageSegmenter.CreateFromOptions(new ImageSegmenterOptions(
            baseOptions,
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE,
            outputCategoryMask: false,
            outputConfidenceMasks: true));
    }

    /// <summary>
    /// 캡처된 상태에서 다시 라이브 프리뷰 상태로 복귀함.
    /// 복귀 시 해제되었던 시각적 크롭을 재적용하기 위함.
    /// </summary>
    private void ResumeCamera()
    {
        _captured = false;
        captureDisplay.texture = _webCamTexture;
        _webCamTexture.Play();
        
        // 다시 프리뷰 모드로 돌아오므로 UV 크롭 재적용
        ApplyUVCropToRawImage(captureDisplay, 1280, 720, new Vector2(8, 9));
    }

    // ─────────────────────────────────────────────────────────
    // 플립 계산
    // ─────────────────────────────────────────────────────────

    private bool FlipH => IsDeviceFrontFacing(_webCamTexture.deviceName);
    private bool FlipV => !_webCamTexture.videoVerticallyMirrored;

    private static bool IsDeviceFrontFacing(string deviceName)
    {
        foreach (WebCamDevice dev in WebCamTexture.devices)
        {
            if (dev.name == deviceName) 
            {
                return dev.isFrontFacing;
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // 캡처 및 분석 처리
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 화면을 캡처하고 퍼스널 컬러 분석 및 합성을 진행함.
    /// 캡처 시에만 세그멘테이션과 색상 추출 연산을 수행하여 부하를 최소화하기 위함.
    /// </summary>
    private void CaptureAndProcess()
    {
        if (!_webCamTexture || _segmenter == null) return;

        int w = _webCamTexture.width;
        int h = _webCamTexture.height;

        using TextureFrame frame = new TextureFrame(w, h, TextureFormat.RGBA32);
        frame.ReadTextureOnCPU(_webCamTexture, FlipH, FlipV);

        // 배경 분리용(Segmentation) 이미지 별도 생성 및 사용
        using MpImage mpImageForSeg = frame.BuildCPUImage();
        float[] floats = RunSegmentation(mpImageForSeg, out int mw, out int mh);

        _debugPoints.Clear();

        UnityEngine.Color32[] flippedPixels = frame.GetPixels32();

        // 1. 색상 분석 실행 및 좌표 획득
        if (colorAnalyzer)
        {
            PersonalColorAnalyzer.ColorResult colorResult = colorAnalyzer.Analyze(flippedPixels, w, h, frame);

            if (showDebug && colorResult.isValid && colorResult.sampledPoints != null)
            {
                foreach (Vector2 pt in colorResult.sampledPoints)
                {
                    // 카메라 원본 좌표(pt)를 화면 표시용 역상 좌표로 변환함
                    float displayX = FlipH ? (w - 1 - pt.x) : pt.x;
                    float displayY = FlipV ? (h - 1 - pt.y) : pt.y;

                    _debugPoints.Add(new DetectionPoint(new Vector2(displayX, displayY), UnityEngine.Color.red));
                }
            }
        }
        else
        {
            Debug.LogWarning("colorAnalyzer가 할당되지 않아 색상 분석 및 디버그 포인트 렌더링을 건너뜁니다.");
        }

        // 2. 최종 화면 합성
        Texture2D finalImage = BuildDisplayImage(flippedPixels, floats, mw, mh, w, h, FlipH, FlipV);

        // 3. 디버그 점 그리기
        if (showDebug && _debugPoints.Count > 0)
        {
            DrawDebugPointsOnTexture(finalImage, _debugPoints, debugPointSize);
            Debug.Log($"캡처 이미지에 {_debugPoints.Count}개의 실제 분석 포인트를 표시했습니다.");
        }

        // 4. 비율 크롭
        finalImage = CropTextureToRatio(finalImage, new Vector2(8, 9));

        _webCamTexture.Pause();
        _captured = true;

        // 이미 물리적으로 8:9 크롭된 텍스처를 표시하므로 UV 크롭 시각 효과를 초기화함
        ResetUVCrop(captureDisplay);
        captureDisplay.texture = finalImage;
        
        SaveImage(finalImage);
        Debug.Log("캡처 완료. Space 를 다시 누르면 카메라로 돌아갑니다.");
    }

    /// <summary>
    /// 뒤집힌 공간(MediaPipe 입력)의 픽셀 + 마스크를 읽어 표시 방향(정방향)의 Texture2D 를 한 번의 CPU 패스로 생성.
    /// GPU Blit 의 알파 채널 문제를 피하고, 픽셀과 마스크가 항상 동일한 좌표를 참조하게 하기 위함.
    /// </summary>
    private Texture2D BuildDisplayImage(UnityEngine.Color32[] flippedPixels, float[] maskFloats,
        int maskW, int maskH, int w, int h, bool flipH, bool flipV)
    {
        UnityEngine.Color32[] dst = new UnityEngine.Color32[w * h];
        UnityEngine.Color32 transparent = new UnityEngine.Color32(0, 0, 0, 0);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 화면 표시 좌표를 기준으로 원본 픽셀 배열의 인덱스를 계산함
                int sx = flipH ? (w - 1 - x) : x;
                int sy = flipV ? (h - 1 - y) : y;

                // 마스크는 Top-Down 배열이므로 매핑을 위해 Y축을 추가로 반전시킴
                int maskSy = (h - 1) - sy;

                float conf = SampleBilinear(maskFloats, maskW, maskH, sx, maskSy, w, h);

                dst[y * w + x] = conf >= maskThreshold ? flippedPixels[sy * w + sx] : transparent;
            }
        }

        Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        if (!result)
        {
            Debug.LogError("캡처 텍스처를 생성하지 못했습니다.");
            return Texture2D.whiteTexture; 
        }

        result.SetPixels32(dst);
        result.Apply();
        return result;
    }

    // ─────────────────────────────────────────────────────────
    // 세그멘테이션 및 샘플링 유틸
    // ─────────────────────────────────────────────────────────

    private float[] RunSegmentation(MpImage mpImage, out int maskW, out int maskH)
    {
        maskW = mpImage.Width();
        maskH = mpImage.Height();

        ImageSegmenterResult result = _segmenter.Segment(mpImage, _imageProcessingOptions);

        if (result.confidenceMasks == null || result.confidenceMasks.Count == 0)
        {
            Debug.LogWarning("confidence mask 가 없습니다.");
            return new float[maskW * maskH];
        }

        MpImage personMask = result.confidenceMasks[0];
        maskW = personMask.Width();
        maskH = personMask.Height();

        float[] floats = new float[maskW * maskH];
        personMask.TryReadChannelNormalized(0, floats);

        foreach (MpImage mask in result.confidenceMasks)
        {
            mask.Dispose();
        }

        return floats;
    }

    private static float SampleBilinear(float[] data, int dw, int dh, int x, int y, int srcW, int srcH)
    {
        if (dw == srcW && dh == srcH) 
        {
            return data[y * dw + x];
        }

        float u = (float)x / Mathf.Max(srcW - 1, 1) * (dw - 1);
        float v = (float)y / Mathf.Max(srcH - 1, 1) * (dh - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, dw - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, dw - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, dh - 1);
        int y1 = Mathf.Clamp(y0 + 1, 0, dh - 1);
        
        float tx = u - x0;
        float ty = v - y0;
        
        return Mathf.Lerp(
            Mathf.Lerp(data[y0 * dw + x0], data[y0 * dw + x1], tx),
            Mathf.Lerp(data[y1 * dw + x0], data[y1 * dw + x1], tx), ty);
    }

    // ─────────────────────────────────────────────────────────
    // 시각 보정 및 이미지 처리 유틸
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// RawImage의 UV를 조정하여 텍스처가 지정된 비율로 크롭되어 보이도록 설정함.
    /// UI 찌그러짐을 방지하고 메모리 연산 없이 16:9 카메라 영상을 8:9로 맞추기 위함.
    /// </summary>
    private void ApplyUVCropToRawImage(RawImage targetImage, int sourceW, int sourceH, Vector2 targetRatio)
    {
        if (!targetImage) return;

        float sourceAspect = (float)sourceW / sourceH;
        float targetAspect = targetRatio.x / targetRatio.y;

        if (sourceAspect > targetAspect)
        {
            float widthRatio = targetAspect / sourceAspect;
            float xOffset = (1f - widthRatio) / 2f;
            targetImage.uvRect = new Rect(xOffset, 0f, widthRatio, 1f);
        }
        else
        {
            float heightRatio = sourceAspect / targetAspect;
            float yOffset = (1f - heightRatio) / 2f;
            targetImage.uvRect = new Rect(0f, yOffset, 1f, heightRatio);
        }
    }

    /// <summary>
    /// RawImage의 UV 크롭 설정을 초기화함.
    /// 이미 물리적으로 8:9 크롭이 완료된 텍스처를 표시할 때 화면이 이중으로 크롭되는 것을 방지하기 위함.
    /// </summary>
    private void ResetUVCrop(RawImage targetImage)
    {
        if (!targetImage) return;
        targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
    }

    /// <summary>
    /// 텍스처를 중앙 기준으로 지정된 비율로 크롭하여 새로운 텍스처를 반환함.
    /// 원본 텍스처 메모리는 내부에서 파괴됨.
    /// </summary>
    private Texture2D CropTextureToRatio(Texture2D sourceTex, Vector2 targetRatio)
    {
        if (!sourceTex)
        {
            Debug.LogError("CropTextureToRatio: sourceTex 가 null 입니다.");
            return Texture2D.whiteTexture;
        }

        int sourceW = sourceTex.width;
        int sourceH = sourceTex.height;
        float sourceAsp = (float)sourceW / sourceH;
        float targetAsp = targetRatio.x / targetRatio.y;

        int cropW;
        int cropH;

        if (sourceAsp > targetAsp) 
        {
            cropH = sourceH;
            cropW = Mathf.RoundToInt(sourceH * targetAsp);
        }
        else 
        {
            cropW = sourceW;
            cropH = Mathf.RoundToInt(sourceW / targetAsp);
        }

        int startX = (sourceW - cropW) / 2;
        int startY = (sourceH - cropH) / 2;

        UnityEngine.Color[] croppedPixels = sourceTex.GetPixels(startX, startY, cropW, cropH);

        Texture2D croppedTex = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
        if (!croppedTex)
        {
            Debug.LogError("크롭된 텍스처를 생성하지 못했습니다.");
            return Texture2D.whiteTexture;
        }

        croppedTex.SetPixels(croppedPixels);
        croppedTex.Apply();

        Destroy(sourceTex);

        return croppedTex;
    }

    /// <summary>
    /// 텍스처의 지정된 픽셀 좌표에 마커(점)를 그림.
    /// 캡처된 화면에 추출된 좌표를 시각적으로 확인하기 위함.
    /// </summary>
    private void DrawDebugPointsOnTexture(Texture2D targetTex, List<DetectionPoint> points, int pointSize)
    {
        if (!targetTex) return;

        int texW = targetTex.width;
        int texH = targetTex.height;
        UnityEngine.Color32[] pixels = targetTex.GetPixels32();
        
        if (pointSize % 2 == 0) pointSize++;
        int halfSize = pointSize / 2;
        
        foreach (DetectionPoint point in points)
        {
            int centerX = Mathf.RoundToInt(point.pixelPos.x);
            int centerY = Mathf.RoundToInt(point.pixelPos.y);
            
            UnityEngine.Color32 markerColor = point.color;
            
            for (int yOffset = -halfSize; yOffset <= halfSize; yOffset++)
            {
                for (int xOffset = -halfSize; xOffset <= halfSize; xOffset++)
                {
                    int x = centerX + xOffset;
                    int y = centerY + yOffset;
                    
                    if (x >= 0 && x < texW && y >= 0 && y < texH)
                    {
                        pixels[y * texW + x] = markerColor;
                    }
                }
            }
        }
        
        targetTex.SetPixels32(pixels);
        targetTex.Apply();
    }

    private void SaveImage(Texture2D tex)
    {
        if (!tex) return;
        string fileName = $"capture_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
        byte[] png = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes(path, png);
        Debug.Log($"이미지 저장 완료: {path}");
    }
}