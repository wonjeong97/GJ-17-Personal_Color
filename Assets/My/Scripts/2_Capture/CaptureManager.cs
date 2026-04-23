using System;
using System.Collections;
using System.Collections.Generic;
using Mediapipe.Unity.Experimental;
using My.Scripts.Global;
using UnityEngine;
using UnityEngine.UI;
using Wonjeong.Utils;

namespace My.Scripts._2_Capture
{
    [Serializable]
    public class CaptureDebugSettings
    {
        public bool showDebugPoints;
        public int debugPointSize;
    }

    /// <summary>
    /// Page1(웹캠 캡처) → Page2(퍼스널 컬러 분석) → Page3(결과) 흐름을 조율한다.
    /// 세부 기능은 WebcamController, ResultPageController에 위임한다.
    /// </summary>
    public class CaptureManager : MonoBehaviour
    {
        [Header("Pages")]
        [SerializeField] private CanvasGroup page1;
        [SerializeField] private CanvasGroup page2;
        [SerializeField] private CanvasGroup page3;
        [SerializeField] private CanvasGroup pageError;

        [Header("Analysis")]
        [SerializeField] private PersonalColorAnalyzer colorAnalyzer;
        [SerializeField] private PersonalColorClassifier colorClassifier;

        [Header("Page1 - Webcam")]
        [SerializeField] private Button captureButton;
        [SerializeField] private Text countdownText;

        [Header("Page2 - Loading")]
        [SerializeField] private Text loadingPercentageText;
        [SerializeField] private LoadingUIController loadingUIController;

        [Header("Page3")]
        [SerializeField] private Button homeButton;

        [Header("Error Page")]
        [SerializeField] private Button errorRetryButton;
        [SerializeField] private Button errorHomeButton;

        [Header("Transition")]
        [SerializeField] private float fadeDuration = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool showDebugPoints;
        [SerializeField] private int debugPointSize;

        [Header("Sub-Controllers")]
        [SerializeField] private WebcamController webcamController;
        [SerializeField] private ResultPageController resultPageController;

        private bool _isCounting;
        private float _targetLoadingTime;
        private bool _isLogicComplete;

        // ── Unity Lifecycle ──────────────────────────────────────────

        private void Start()
        {
            if (!ValidateComponents()) return;

            LoadDebugSettingsFromJson();
            if (debugPointSize == 0) debugPointSize = 9;

            InitPages();

            webcamController.OnWebcamReady += OnWebcamReady;
            webcamController.StartWebcam();

            if (countdownText) countdownText.text = "";

            if (GameManager.Instance)
            {
                captureButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
                errorRetryButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
                errorHomeButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
            }

            captureButton.onClick.AddListener(OnCaptureButtonClicked);
            errorRetryButton.onClick.AddListener(OnRetryButtonClicked);
            errorHomeButton.onClick.AddListener(LoadTitleScene);

            if (homeButton)
            {
                if (GameManager.Instance) homeButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
                homeButton.onClick.AddListener(LoadTitleScene);
            }
        }

        // F키로 JSON 설정을 즉시 다시 읽어 에디터 재시작 없이 디버그 환경을 갱신한다.
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F)) LoadDebugSettingsFromJson();
        }

        // ── 초기화 ───────────────────────────────────────────────────

        private void LoadDebugSettingsFromJson()
        {
            CaptureDebugSettings settings = JsonLoader.Load<CaptureDebugSettings>(GameConstants.Path.JsonSetting);
            if (settings != null)
            {
                showDebugPoints = settings.showDebugPoints;
                debugPointSize  = settings.debugPointSize;
            }
            else
            {
                Debug.LogWarning("[CaptureManager] Settings.json 로드 실패, 인스펙터 값 사용.");
            }
        }

        private bool ValidateComponents()
        {
            bool ok = true;
            void Req(UnityEngine.Object o, string name) { if (!o) { Debug.LogError($"{name}이 할당되지 않았습니다."); ok = false; } }
            void Opt(UnityEngine.Object o, string name) { if (!o) Debug.LogWarning($"{name}이 할당되지 않았습니다."); }

            Req(page1, "page1"); Req(page2, "page2"); Req(page3, "page3");
            Req(captureButton, "captureButton"); Req(countdownText, "countdownText");
            Req(webcamController, "webcamController"); Req(resultPageController, "resultPageController");
            Opt(pageError, "pageError"); Opt(loadingPercentageText, "loadingPercentageText");
            Opt(colorAnalyzer, "colorAnalyzer"); Opt(colorClassifier, "colorClassifier");
            Opt(homeButton, "homeButton");

            return ok;
        }

        private void InitPages()
        {
            SetPage(page1, true);
            SetPage(page2, false);
            SetPage(page3, false);
            SetPage(pageError, false);
            resultPageController.HideAllColorPanels();
        }

        private void OnWebcamReady()
        {
            if (GameManager.Instance) GameManager.Instance.ManualFadeIn();
        }

        // ── Page1 — 카운트다운 & 캡처 ────────────────────────────────

        private void OnCaptureButtonClicked()
        {
            if (_isCounting) return;
            StartCoroutine(CountdownAndCapture());
        }

        private IEnumerator CountdownAndCapture()
        {
            _isCounting = true;
            captureButton.interactable = false;
            countdownText.gameObject.SetActive(true);

            for (int count = 5; count >= 1; count--)
            {
                countdownText.text = count.ToString()+"초";
                yield return CoroutineData.GetWaitForSeconds(1f);
            }

            countdownText.gameObject.SetActive(false);

            if (GameManager.Instance) GameManager.Instance.PlayShutterSound();
            webcamController.CaptureFrame();

            yield return CoroutineData.GetWaitForSeconds(1f);
            yield return StartCoroutine(AnalyzeAndProceed());
        }

        // ── Page2 — 퍼스널 컬러 분석 ─────────────────────────────────

        /// <summary>
        /// 분석을 page1이 보이는 상태에서 먼저 실행한 뒤 결과에 따라 분기한다.
        /// - 성공: page1 → page2(로딩) → page3 (페이드 2회)
        /// - 실패: page1 → pageError (페이드 1회, 이중 페이드 방지)
        /// </summary>
        private IEnumerator AnalyzeAndProceed()
        {
            bool isSuccess = false;
            PersonalColorType resultType = default;

            Texture2D analysisTex = webcamController.AnalysisTexture;
            if (colorAnalyzer && colorClassifier && analysisTex)
            {
                int w = analysisTex.width;
                int h = analysisTex.height;

                using TextureFrame frame = new TextureFrame(w, h, TextureFormat.RGBA32);
                frame.ReadTextureOnCPU(analysisTex, false, false);

                Color32[] pixels = frame.GetPixels32();
                PersonalColorAnalyzer.ColorResult colorResult = colorAnalyzer.Analyze(pixels, w, h, frame);

                if (colorResult.isValid)
                {
                    resultType = colorClassifier.Classify(colorResult.skin, colorResult.eye, colorResult.hair);
                    isSuccess  = true;

                    if (showDebugPoints && colorResult.sampledPoints != null)
                    {
                        DrawDebugPointsOnTexture(
                            webcamController.CapturedTexture, colorResult.sampledPoints,
                            w, h,
                            webcamController.CropX, webcamController.CropY,
                            webcamController.LastFlipH, webcamController.LastFlipV);
                    }
                }
            }

            if (isSuccess)
            {
                yield return StartCoroutine(FadeToPage(page1, page2));
                yield return StartCoroutine(ShowResultRoutine(resultType));
            }
            else
            {
                resultPageController.PreSetupPage3(false, default, webcamController.CapturedTexture);
                yield return StartCoroutine(FadeToPage(page1, pageError));
            }
        }

        /// <summary>
        /// 성공 경로 전용: page2(로딩) 상태에서 카드 업로드·QR 생성 후 page3으로 전환한다.
        /// 로딩 애니메이션은 min(업로드 완료 시간, 10초)에 맞춰 진행한다.
        /// 업로드 최종 실패 시 pageError로 전환한다.
        /// </summary>
        private IEnumerator ShowResultRoutine(PersonalColorType resultType)
        {
            float startTime = Time.time;
            _isLogicComplete = false;
            _targetLoadingTime = 10f;

            StartCoroutine(AnimateLoadingText());

            resultPageController.PreSetupPage3(true, resultType, webcamController.CapturedTexture);
            yield return new WaitForEndOfFrame();
            yield return StartCoroutine(resultPageController.CaptureAndUploadRoutine());

            if (!resultPageController.UploadSucceeded)
            {
                _isLogicComplete = true; // 애니메이션 코루틴 정리
                yield return StartCoroutine(FadeToPage(page2, pageError));
                yield break;
            }

            yield return StartCoroutine(resultPageController.GenerateQRCode(resultPageController.UploadedUrl));

            // 업로드+QR 완료 시간과 10초 중 긴 쪽을 로딩 목표 시간으로 사용 (최소 10초 보장)
            float elapsed = Time.time - startTime;
            _targetLoadingTime = Mathf.Max(elapsed, 10f);
            _isLogicComplete = true;

            while (Time.time - startTime < _targetLoadingTime) yield return null;

            if (loadingPercentageText) loadingPercentageText.text = "100%";
            if (loadingUIController)  loadingUIController.SetProgress(1f);
            yield return CoroutineData.GetWaitForSeconds(1f);
            yield return StartCoroutine(FadeToPage(page2, page3));
        }

        private void OnRetryButtonClicked()
        {
            _isCounting = false;
            captureButton.interactable = true;
            webcamController.StartWebcam();
            StartCoroutine(FadeToPage(pageError, page1));
        }

        private IEnumerator AnimateLoadingText()
        {
            if (loadingPercentageText) loadingPercentageText.text = "0%";
            if (loadingUIController)  loadingUIController.SetProgress(0f);

            yield return CoroutineData.GetWaitForSeconds(0.5f);

            float elapsed = 0f;
            float currentDuration = 9.5f;

            while (true)
            {
                elapsed += Time.deltaTime;

                if (_isLogicComplete)
                {
                    currentDuration = _targetLoadingTime - 0.5f;
                    if (elapsed >= currentDuration) break;
                }
                else if (elapsed >= currentDuration * 0.9f)
                {
                    // 로직이 아직 끝나지 않았으면 99% 근방에서 시간을 늘려 대기
                    currentDuration += Time.deltaTime * 10f;
                }

                float ratio   = elapsed / currentDuration;
                int   percent = Mathf.Clamp(Mathf.RoundToInt(ratio * 100f), 0, 99);

                if (loadingPercentageText) loadingPercentageText.text = $"{percent}%";
                if (loadingUIController)  loadingUIController.SetProgress(ratio);

                yield return null;
            }

            if (loadingPercentageText) loadingPercentageText.text = "100%";
            if (loadingUIController)  loadingUIController.SetProgress(1f);
        }

        // ── 씬 전환 ──────────────────────────────────────────────────

        private void LoadTitleScene()
        {
            if (!GameManager.Instance)
            {
                webcamController.StopWebcam();
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.Scene.Title);
                return;
            }

            // 페이드 아웃 완료 시점(화면이 검은 상태)에 웹캠을 정지해 시각적 끊김을 제거한다.
            GameManager.Instance.ChangeScene(
                GameConstants.Scene.Title,
                onFadeOutComplete: webcamController.StopWebcam,
                autoFadeIn: true);
        }

        // ── UI 유틸 ───────────────────────────────────────────────────

        private IEnumerator FadeToPage(CanvasGroup from, CanvasGroup to)
        {
            if (to) { to.alpha = 0f; to.blocksRaycasts = false; to.interactable = false; }

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                if (from) from.alpha = 1f - t;
                if (to)   to.alpha   = t;
                yield return null;
            }

            // 전환 완료 후 모든 페이지를 정리하여 하나만 활성 상태로 유지한다.
            SetOnlyPage(to);
        }

        // 지정된 페이지만 활성화하고 나머지 모든 페이지를 alpha=0으로 정리한다.
        private void SetOnlyPage(CanvasGroup active)
        {
            SetPage(page1,     false);
            SetPage(page2,     false);
            SetPage(page3,     false);
            SetPage(pageError, false);
            SetPage(active,    true);
        }

        private static void SetPage(CanvasGroup group, bool visible)
        {
            if (!group) return;
            group.alpha          = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable   = visible;
        }

        /// <summary>
        /// 분석용 이미지(플립 적용)의 좌표를 표시용 이미지(크롭·논플립) 좌표계로 역산하여
        /// 샘플링 위치를 빨간 점으로 표시한다.
        /// </summary>
        private void DrawDebugPointsOnTexture(Texture2D targetTex, List<Vector2> points,
            int fullW, int fullH, int srcX, int srcY, bool flipH, bool flipV)
        {
            if (!targetTex) return;

            Color32[] pixels   = targetTex.GetPixels32();
            int       texW     = targetTex.width;
            int       texH     = targetTex.height;
            int       halfSize = debugPointSize / 2;
            Color32   marker   = Color.red;

            foreach (Vector2 pt in points)
            {
                float ux = flipH ? (fullW - 1 - pt.x) : pt.x;
                float uy = flipV ? (fullH - 1 - pt.y) : pt.y;
                int   cx = Mathf.RoundToInt(ux - srcX);
                int   cy = Mathf.RoundToInt(uy - srcY);

                for (int dy = -halfSize; dy <= halfSize; dy++)
                for (int dx = -halfSize; dx <= halfSize; dx++)
                {
                    int px = cx + dx;
                    int py = cy + dy;
                    if (px >= 0 && px < texW && py >= 0 && py < texH)
                        pixels[py * texW + px] = marker;
                }
            }

            targetTex.SetPixels32(pixels);
            targetTex.Apply();
        }
    }
}
