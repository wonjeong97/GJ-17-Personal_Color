using System.Collections;
using System.Collections.Generic;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using My.Scripts.Global;
using UnityEngine;
using UnityEngine.UI;

namespace My.Scripts._2_Capture
{
    /// <summary>
    /// 2_Capture 씬의 Page1(웹캠 캡처) → Page2(퍼스널 컬러 분석) → Page3(결과 표시) 흐름을 관리한다.
    /// </summary>
    public class CaptureManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────
        // Inspector Fields
        // ─────────────────────────────────────────────────────────

        [Header("Pages")]
        [SerializeField] private CanvasGroup page1;
        [SerializeField] private CanvasGroup page2;
        [SerializeField] private CanvasGroup page3;

        [Header("Page1 - Webcam")]
        [SerializeField] private RawImage previewImage;
        [SerializeField] private Button captureButton;
        [SerializeField] private Text countdownText;

        [Header("Page3 - Result")]
        [SerializeField] private RawImage photoImage;
        [SerializeField] private CanvasGroup springLight;
        [SerializeField] private CanvasGroup springBright;
        [SerializeField] private CanvasGroup summerLight;
        [SerializeField] private CanvasGroup summerMute;
        [SerializeField] private CanvasGroup autumnMute;
        [SerializeField] private CanvasGroup autumnDeep;
        [SerializeField] private CanvasGroup winterBright;
        [SerializeField] private CanvasGroup winterDeep;

        [Header("Webcam Settings")]
        [SerializeField] private int webcamWidth  = 1920;
        [SerializeField] private int webcamHeight = 1080;
        [SerializeField] private int webcamFps    = 30;

        [Header("Analysis")]
        [SerializeField] private PersonalColorAnalyzer   colorAnalyzer;
        [SerializeField] private PersonalColorClassifier colorClassifier;

        [Header("Page3 - UI")]
        [SerializeField] private Text   resultTitleText;
        [SerializeField] private Button homeButton;

        [Header("Transition")]
        [SerializeField] private float fadeDuration   = 0.5f;
        [SerializeField] private float page2MinSeconds = 2f;

        // ─────────────────────────────────────────────────────────
        // Private State
        // ─────────────────────────────────────────────────────────

        private WebCamTexture _webcamTexture;
        private bool          _isCounting;
        private Texture2D     _capturedTexture; // 크롭된 표시용 텍스처 (RGB24)
        private Texture2D     _analysisTexture; // 전체 해상도 분석용 텍스처 (RGBA32, 플립 적용)

        // ─────────────────────────────────────────────────────────
        // 프론트-facing 판별용 플립 플래그
        // ─────────────────────────────────────────────────────────

        private bool FlipH => IsDeviceFrontFacing(_webcamTexture.deviceName);
        private bool FlipV => !_webcamTexture.videoVerticallyMirrored;

        private static bool IsDeviceFrontFacing(string deviceName)
        {
            foreach (WebCamDevice dev in WebCamTexture.devices)
            {
                if (dev.name == deviceName) return dev.isFrontFacing;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ─────────────────────────────────────────────────────────

        private void Start()
        {
            if (!ValidateComponents()) return;

            InitPages();
            StartWebcam();
            countdownText.gameObject.SetActive(false);
            captureButton.onClick.AddListener(OnCaptureButtonClicked);

            if (homeButton)
                homeButton.onClick.AddListener(LoadTitleScene);
        }

        private void OnDestroy()
        {
            // CaptureFrame()에서 이미 Stop()됐을 수 있으나, 예외 경로를 위해 재확인
            if (_webcamTexture != null && _webcamTexture.isPlaying)
                _webcamTexture.Stop();
        }

        // ─────────────────────────────────────────────────────────
        // 초기화
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 필수 컴포넌트 할당 여부를 확인한다.
        /// </summary>
        private bool ValidateComponents()
        {
            bool valid = true;
            if (!page1)           { Debug.LogError("page1이 할당되지 않았습니다.");           valid = false; }
            if (!page2)           { Debug.LogError("page2가 할당되지 않았습니다.");           valid = false; }
            if (!page3)           { Debug.LogError("page3이 할당되지 않았습니다.");           valid = false; }
            if (!previewImage)    { Debug.LogError("previewImage가 할당되지 않았습니다.");    valid = false; }
            if (!captureButton)   { Debug.LogError("captureButton이 할당되지 않았습니다.");   valid = false; }
            if (!countdownText)   { Debug.LogError("countdownText가 할당되지 않았습니다.");   valid = false; }
            if (!photoImage)      { Debug.LogError("photoImage가 할당되지 않았습니다.");      valid = false; }
            if (!colorAnalyzer)   { Debug.LogWarning("colorAnalyzer가 할당되지 않았습니다. 분석이 스킵됩니다."); }
            if (!colorClassifier) { Debug.LogWarning("colorClassifier가 할당되지 않았습니다. 분류가 스킵됩니다."); }
            if (!resultTitleText) { Debug.LogWarning("resultTitleText가 할당되지 않았습니다."); }
            if (!homeButton)      { Debug.LogWarning("homeButton이 할당되지 않았습니다."); }
            return valid;
        }

        /// <summary>
        /// 씬 시작 시 Page1만 표시하고 나머지 페이지를 모두 숨긴다.
        /// </summary>
        private void InitPages()
        {
            SetPage(page1, true);
            SetPage(page2, false);
            SetPage(page3, false);
            HideAllColorPanels();
        }

        /// <summary>
        /// 사용 가능한 첫 번째 웹캠을 지정 해상도로 연결하고 스트리밍을 시작한다.
        /// </summary>
        private void StartWebcam()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("사용 가능한 웹캠 장치가 없습니다.");
                return;
            }

            _webcamTexture = new WebCamTexture(webcamWidth, webcamHeight, webcamFps);
            previewImage.texture = _webcamTexture;
            _webcamTexture.Play();

            // 웹캠이 실제 해상도를 확정한 후 uvRect 계산
            StartCoroutine(ApplyUvRectAfterReady());
        }

        /// <summary>
        /// 웹캠이 유효한 해상도를 반환할 때까지 대기 후 uvRect를 적용한다.
        /// </summary>
        private IEnumerator ApplyUvRectAfterReady()
        {
            yield return new WaitUntil(() => _webcamTexture.width > 16);
            ApplyCoverUvRect();
        }

        // ─────────────────────────────────────────────────────────
        // Page1 — 카운트다운 & 캡처
        // ─────────────────────────────────────────────────────────

        private void OnCaptureButtonClicked()
        {
            if (_isCounting) return;
            StartCoroutine(CountdownAndCapture());
        }

        /// <summary>
        /// 5→1 카운트다운 후 캡처하고 Page2로 전환한다.
        /// </summary>
        private IEnumerator CountdownAndCapture()
        {
            _isCounting = true;
            captureButton.interactable = false;
            countdownText.gameObject.SetActive(true);

            for (int count = 5; count >= 1; count--)
            {
                countdownText.text = count.ToString();
                yield return new WaitForSeconds(1f);
            }

            countdownText.gameObject.SetActive(false);
            CaptureFrame();

            // Page1 → Page2 전환 후 분석 시작
            yield return StartCoroutine(FadeToPage(page1, page2));
            yield return StartCoroutine(AnalyzeAndProceed());
        }

        /// <summary>
        /// 웹캠을 완전히 종료하고 두 종류의 텍스처를 저장한다.
        /// - _analysisTexture : 전체 해상도, RGBA32, 플립 적용 → MediaPipe 분석용
        /// - _capturedTexture : uvRect 기준 크롭, RGB24 → Page1/3 표시용
        /// Stop() 전에 플립 플래그를 캐싱하여 Stop() 이후에도 분석에 사용 가능하게 함.
        /// </summary>
        private void CaptureFrame()
        {
            if (_webcamTexture == null) return;

            _webcamTexture.Pause();

            int fullW = _webcamTexture.width;
            int fullH = _webcamTexture.height;

            // ── 분석용: 전체 해상도 + RGBA32 + 플립 적용 ──────────────
            // Stop() 전에 플립 값을 캐싱 (Stop() 후에는 videoVerticallyMirrored 접근 불가)
            bool flipH = FlipH;
            bool flipV = FlipV;

            using (TextureFrame analysisFrame = new TextureFrame(fullW, fullH, TextureFormat.RGBA32))
            {
                analysisFrame.ReadTextureOnCPU(_webcamTexture, flipH, flipV);
                _analysisTexture = new Texture2D(fullW, fullH, TextureFormat.RGBA32, false);
                _analysisTexture.SetPixels32(analysisFrame.GetPixels32());
                _analysisTexture.Apply();
            }

            // ── 표시용: uvRect 기준 크롭 + RGB24 ──────────────────────
            Rect uv = previewImage.uvRect;
            int srcX = Mathf.RoundToInt(uv.x      * fullW);
            int srcY = Mathf.RoundToInt(uv.y      * fullH);
            int srcW = Mathf.RoundToInt(uv.width  * fullW);
            int srcH = Mathf.RoundToInt(uv.height * fullH);

            Color32[] rawPixels     = _webcamTexture.GetPixels32();
            Color32[] croppedPixels = new Color32[srcW * srcH];

            for (int row = 0; row < srcH; row++)
            {
                System.Array.Copy(
                    rawPixels, (srcY + row) * fullW + srcX,
                    croppedPixels, row * srcW, srcW);
            }

            _capturedTexture = new Texture2D(srcW, srcH, TextureFormat.RGB24, false);
            _capturedTexture.SetPixels32(croppedPixels);
            _capturedTexture.Apply();

            // 픽셀 읽기 완료 후 웹캠 완전 종료
            _webcamTexture.Stop();

            // Page1 미리보기를 캡처 이미지로 교체하고 UV 초기화
            previewImage.texture = _capturedTexture;
            previewImage.uvRect  = new Rect(0f, 0f, 1f, 1f);

            Debug.Log($"[캡처] 표시={_capturedTexture.width}x{_capturedTexture.height} / 분석={_analysisTexture.width}x{_analysisTexture.height}");
        }

        // ─────────────────────────────────────────────────────────
        // Page2 — 퍼스널 컬러 분석
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 캡처된 텍스처로 PersonalColorAnalyzer를 실행하고,
        /// 결과를 PersonalColorClassifier로 분류한 뒤 Page3으로 전환한다.
        /// Page2는 최소 page2MinSeconds 초 동안 유지된다.
        /// </summary>
        private IEnumerator AnalyzeAndProceed()
        {
            // Page2 최소 노출 시간 타이머와 분석을 병렬로 시작
            float page2StartTime = Time.time;

            // 페이드인이 완료되도록 한 프레임 대기
            yield return null;

            PersonalColorType resultType = PersonalColorType.SpringLight; // fallback

            if (colorAnalyzer && colorClassifier && _analysisTexture)
            {
                int w = _analysisTexture.width;
                int h = _analysisTexture.height;

                // 전체 해상도 RGBA32 텍스처에서 TextureFrame 생성 (이미 플립 적용 완료)
                using TextureFrame frame = new TextureFrame(w, h, TextureFormat.RGBA32);
                frame.ReadTextureOnCPU(_analysisTexture, false, false);

                Color32[] pixels = frame.GetPixels32();
                PersonalColorAnalyzer.ColorResult colorResult = colorAnalyzer.Analyze(pixels, w, h, frame);

                if (colorResult.isValid)
                {
                    resultType = colorClassifier.Classify(colorResult.skin, colorResult.eye, colorResult.hair);
                }
                else
                {
                    Debug.LogWarning("색상 분석 결과가 유효하지 않습니다. 기본 타입을 사용합니다.");
                }
            }
            else
            {
                Debug.LogWarning("분석 컴포넌트가 미할당 상태입니다. 기본 타입으로 Page3를 표시합니다.");
            }

            // 분석이 빨리 끝나도 Page2 최소 노출 시간을 보장
            float elapsed = Time.time - page2StartTime;
            if (elapsed < page2MinSeconds)
                yield return new WaitForSeconds(page2MinSeconds - elapsed);

            // Page2 → Page3 전환 및 결과 표시
            yield return StartCoroutine(FadeToPage(page2, page3));
            ShowResult(resultType);
        }

        // ─────────────────────────────────────────────────────────
        // Page3 — 결과 표시
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 캡처된 사진을 photoImage에 표시하고, 퍼스널 컬러 타입 패널을 활성화하며,
        /// 타이틀 텍스트를 "나만의 컬러 카드"로 변경한다.
        /// </summary>
        private void ShowResult(PersonalColorType type)
        {
            if (_capturedTexture && photoImage)
                photoImage.texture = _capturedTexture;

            if (resultTitleText)
                resultTitleText.text = "나만의 컬러 카드";

            CanvasGroup targetPanel = GetColorPanel(type);
            if (targetPanel) SetPage(targetPanel, true);

            Debug.Log($"[Page3] 퍼스널 컬러: {type}");
        }

        /// <summary>
        /// 타이틀 씬으로 이동한다.
        /// </summary>
        private void LoadTitleScene()
        {
            GameManager.Instance.ChangeScene(GameConstants.Scene.Title);
        }

        /// <summary>
        /// PersonalColorType에 대응하는 CanvasGroup을 반환한다.
        /// </summary>
        private CanvasGroup GetColorPanel(PersonalColorType type)
        {
            return type switch
            {
                PersonalColorType.SpringLight  => springLight,
                PersonalColorType.SpringBright => springBright,
                PersonalColorType.SummerLight  => summerLight,
                PersonalColorType.SummerMute   => summerMute,
                PersonalColorType.AutumnMute   => autumnMute,
                PersonalColorType.AutumnDeep   => autumnDeep,
                PersonalColorType.WinterBright => winterBright,
                PersonalColorType.WinterDeep   => winterDeep,
                _                              => null
            };
        }

        // ─────────────────────────────────────────────────────────
        // UI 유틸 — CanvasGroup 페이드 & 활성화
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// from 페이지를 페이드 아웃하고 to 페이지를 페이드 인한다.
        /// </summary>
        private IEnumerator FadeToPage(CanvasGroup from, CanvasGroup to)
        {
            float elapsed = 0f;

            // 대상 페이지 raycast만 먼저 차단
            if (to) { to.alpha = 0f; to.blocksRaycasts = false; to.interactable = false; }

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                if (from) from.alpha = 1f - t;
                if (to)   to.alpha   = t;
                yield return null;
            }

            SetPage(from, false);
            SetPage(to,   true);
        }

        /// <summary>
        /// CanvasGroup의 표시 여부와 레이캐스트 차단 상태를 일괄 설정한다.
        /// </summary>
        private static void SetPage(CanvasGroup group, bool visible)
        {
            if (!group) return;
            group.alpha          = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable   = visible;
        }

        /// <summary>
        /// Page3의 8종 컬러 패널을 모두 숨긴다.
        /// </summary>
        private void HideAllColorPanels()
        {
            CanvasGroup[] panels = { springLight, springBright, summerLight, summerMute,
                                     autumnMute, autumnDeep, winterBright, winterDeep };
            foreach (CanvasGroup panel in panels)
                SetPage(panel, false);
        }

        // ─────────────────────────────────────────────────────────
        // UV 크롭 (RawImage Cover 적용)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// RawImage의 표시 비율에 맞춰 웹캠 텍스처의 uvRect를 Center-Crop(Cover)으로 설정한다.
        /// </summary>
        private void ApplyCoverUvRect()
        {
            float camAspect   = (float)_webcamTexture.width / _webcamTexture.height;
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
    }
}
