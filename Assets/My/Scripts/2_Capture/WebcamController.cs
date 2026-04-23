using System;
using System.Collections;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace My.Scripts._2_Capture
{
    public class WebcamController : MonoBehaviour
    {
        [SerializeField] private RawImage previewImage;
        [SerializeField] private int webcamWidth = 1024;
        [SerializeField] private int webcamHeight = 576;
        [SerializeField] private int webcamFps = 30;

        private WebCamTexture _webcamTexture;

        public Texture2D CapturedTexture { get; private set; }
        public Texture2D AnalysisTexture { get; private set; }
        public int CropX { get; private set; }
        public int CropY { get; private set; }
        public bool LastFlipH { get; private set; }
        public bool LastFlipV { get; private set; }

        public event Action OnWebcamReady;

        private bool FlipH => IsDeviceFrontFacing(_webcamTexture.deviceName);
        private bool FlipV => !_webcamTexture.videoVerticallyMirrored;

        private void OnDestroy() => StopWebcam();

        public void StartWebcam()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("사용 가능한 웹캠 장치가 없습니다.");
                return;
            }

            // 이전 캡처 텍스처 해제 후 재시작
            if (CapturedTexture) { Destroy(CapturedTexture); CapturedTexture = null; }
            if (AnalysisTexture) { Destroy(AnalysisTexture); AnalysisTexture = null; }

            StopWebcam();

            _webcamTexture = new WebCamTexture(webcamWidth, webcamHeight, webcamFps);
            previewImage.texture = _webcamTexture;
            _webcamTexture.Play();

            StartCoroutine(ApplyUvRectAfterReady());
        }

        public void StopWebcam()
        {
            if (!_webcamTexture) return;
            if (_webcamTexture.isPlaying) _webcamTexture.Stop();
            Object.Destroy(_webcamTexture);
            _webcamTexture = null;
        }

        /// <summary>
        /// 웹캠을 정지하고 두 종류의 텍스처를 생성한다.
        /// - AnalysisTexture : 전체 해상도, RGBA32, 플립 적용 → MediaPipe 분석용
        /// - CapturedTexture : uvRect 기준 크롭, RGB24 → 화면 표시용
        /// Stop() 전에 플립 플래그를 캐싱하여 Stop() 이후에도 좌표 역산에 사용 가능하게 함.
        /// </summary>
        public void CaptureFrame()
        {
            if (!_webcamTexture) return;

            _webcamTexture.Pause();

            int fullW = _webcamTexture.width;
            int fullH = _webcamTexture.height;
            bool flipH = FlipH;
            bool flipV = FlipV;
            LastFlipH = flipH;
            LastFlipV = flipV;

            using (TextureFrame analysisFrame = new TextureFrame(fullW, fullH, TextureFormat.RGBA32))
            {
                analysisFrame.ReadTextureOnCPU(_webcamTexture, flipH, flipV);
                AnalysisTexture = new Texture2D(fullW, fullH, TextureFormat.RGBA32, false);
                AnalysisTexture.SetPixels32(analysisFrame.GetPixels32());
                AnalysisTexture.Apply();
            }

            Rect uv = previewImage.uvRect;
            int srcX = Mathf.RoundToInt(uv.x * fullW);
            int srcY = Mathf.RoundToInt(uv.y * fullH);
            int srcW = Mathf.RoundToInt(uv.width * fullW);
            int srcH = Mathf.RoundToInt(uv.height * fullH);

            CropX = srcX;
            CropY = srcY;

            Color32[] rawPixels = _webcamTexture.GetPixels32();
            Color32[] croppedPixels = new Color32[srcW * srcH];
            for (int row = 0; row < srcH; row++)
            {
                Array.Copy(rawPixels, (srcY + row) * fullW + srcX, croppedPixels, row * srcW, srcW);
            }

            CapturedTexture = new Texture2D(srcW, srcH, TextureFormat.RGB24, false);
            CapturedTexture.SetPixels32(croppedPixels);
            CapturedTexture.Apply();

            _webcamTexture.Stop();
            previewImage.texture = CapturedTexture;
            previewImage.uvRect = new Rect(0f, 0f, 1f, 1f);

            Debug.Log($"[캡처] 표시={CapturedTexture.width}x{CapturedTexture.height} / 분석={AnalysisTexture.width}x{AnalysisTexture.height}");
        }

        public void ApplyCoverUvRect()
        {
            float camAspect = (float)_webcamTexture.width / _webcamTexture.height;
            float imageAspect = previewImage.rectTransform.rect.width / previewImage.rectTransform.rect.height;

            Rect uvRect;
            if (camAspect > imageAspect)
            {
                float uvWidth = imageAspect / camAspect;
                uvRect = new Rect((1f - uvWidth) * 0.5f, 0f, uvWidth, 1f);
            }
            else
            {
                float uvHeight = camAspect / imageAspect;
                uvRect = new Rect(0f, (1f - uvHeight) * 0.5f, 1f, uvHeight);
            }

            previewImage.uvRect = uvRect;
        }

        // 웹캠이 유효한 해상도를 반환할 때까지 대기 후 uvRect를 적용하고 준비 이벤트를 발생시킨다.
        private IEnumerator ApplyUvRectAfterReady()
        {
            yield return new WaitUntil(() => _webcamTexture.width > 16);
            ApplyCoverUvRect();
            OnWebcamReady?.Invoke();
        }

        private static bool IsDeviceFrontFacing(string deviceName)
        {
            foreach (WebCamDevice dev in WebCamTexture.devices)
            {
                if (dev.name == deviceName) return dev.isFrontFacing;
            }
            return false;
        }
    }
}
