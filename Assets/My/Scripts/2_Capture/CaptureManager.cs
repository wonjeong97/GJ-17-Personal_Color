using System;
using System.Collections;
using System.Collections.Generic;
using Mediapipe.Unity.Experimental;
using My.Scripts.Global;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Screen = UnityEngine.Screen;
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
        [SerializeField] private CanvasGroup errorPanel;
        [SerializeField] private CanvasGroup springLight;
        [SerializeField] private CanvasGroup springBright;
        [SerializeField] private CanvasGroup summerLight;
        [SerializeField] private CanvasGroup summerMute;
        [SerializeField] private CanvasGroup autumnMute;
        [SerializeField] private CanvasGroup autumnDeep;
        [SerializeField] private CanvasGroup winterBright;
        [SerializeField] private CanvasGroup winterDeep;

        [Header("Webcam Settings")]
        [SerializeField] private int webcamWidth = 1920;
        [SerializeField] private int webcamHeight = 1080;
        [SerializeField] private int webcamFps = 30;

        [Header("Analysis")]
        [SerializeField] private PersonalColorAnalyzer colorAnalyzer;
        [SerializeField] private PersonalColorClassifier colorClassifier;

        [Header("Page2 - Analysis")]
        [SerializeField] private Text loadingPercentageText;

        [Header("Page3 - UI")]
        [SerializeField] private Button homeButton;

        [Header("Page3 - QR Code")]
        [SerializeField] private RawImage qrCodeImage;
        [SerializeField] private string debugQrUrl = "https://www.google.com";
        [SerializeField] private QRCodeGenerator qrCodeGenerator;

        [Header("Page3 - Capture Target")]
        [SerializeField] private RectTransform imageCardFrame;
        [SerializeField] private Camera uiCamera;

        [Header("Transition")]
        [SerializeField] private float fadeDuration = 0.5f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugPoints;
        [SerializeField] private int debugPointSize;
        
        // ─────────────────────────────────────────────────────────
        // Private State
        // ─────────────────────────────────────────────────────────

        private WebCamTexture _webcamTexture;
        private bool _isCounting;
        private Texture2D _capturedTexture; // 크롭된 표시용 텍스처 (RGB24)
        private Texture2D _analysisTexture; // 전체 해상도 분석용 텍스처 (RGBA32, 플립 적용)
        
        private float _targetLoadingTime;
        private bool _isLogicComplete;
        
        private int _cropX;
        private int _cropY;
        private bool _lastFlipH;
        private bool _lastFlipV;

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
            
            LoadDebugSettingsFromJson();

            if (debugPointSize == 0)
            {
                debugPointSize = 9;
                Debug.Log("debugPointSize가 0이므로 기본값 9로 세팅합니다.");
            }

            InitPages();
            StartWebcam();
            countdownText.text = "촬영하기";

            if (GameManager.Instance)
            {
                captureButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
            }
            captureButton.onClick.AddListener(OnCaptureButtonClicked);

            if (homeButton)
            {
                if (GameManager.Instance)
                {
                    homeButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
                }
                homeButton.onClick.AddListener(LoadTitleScene);
            }
        }
        
        /// <summary>
        /// 매 프레임 사용자 입력을 감지한다.
        /// F키 입력 시 JSON 설정을 즉시 다시 읽어와 에디터 재시작 없이 캡처 디버그 환경(점 표시 여부, 점 크기)을 갱신하기 위함.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                LoadDebugSettingsFromJson();
            }
        }

        private void OnDestroy()
        {
            StopWebcam();
        }

        // ─────────────────────────────────────────────────────────
        // 초기화
        // ─────────────────────────────────────────────────────────
        
        /// <summary>
        /// 로컬 JSON 파일에서 캡처 전용 디버그 설정값을 로드하여 인스펙터 값을 덮어쓴다.
        /// </summary>
        private void LoadDebugSettingsFromJson()
        {
            // Settings.json 파일 내부에 캡처 디버그용 키값이 함께 들어있다고 가정하고 파싱함
            CaptureDebugSettings debugSettings = JsonLoader.Load<CaptureDebugSettings>(GameConstants.Path.JsonSetting);
            
            if (debugSettings != null)
            {
                showDebugPoints = debugSettings.showDebugPoints;
                debugPointSize = debugSettings.debugPointSize;
            }
            else
            {
                Debug.LogWarning("[CaptureManager] Settings.json을 불러오지 못해 인스펙터의 설정값을 사용합니다.");
            }
        }

        /// <summary>
        /// 필수 컴포넌트 할당 여부를 확인한다.
        /// </summary>
        private bool ValidateComponents()
        {
            bool valid = true;
            if (!page1)
            {
                Debug.LogError("page1이 할당되지 않았습니다.");
                valid = false;
            }

            if (!page2)
            {
                Debug.LogError("page2가 할당되지 않았습니다.");
                valid = false;
            }

            if (!page3)
            {
                Debug.LogError("page3이 할당되지 않았습니다.");
                valid = false;
            }

            if (!previewImage)
            {
                Debug.LogError("previewImage가 할당되지 않았습니다.");
                valid = false;
            }

            if (!captureButton)
            {
                Debug.LogError("captureButton이 할당되지 않았습니다.");
                valid = false;
            }

            if (!countdownText)
            {
                Debug.LogError("countdownText가 할당되지 않았습니다.");
                valid = false;
            }

            if (!photoImage)
            {
                Debug.LogError("photoImage가 할당되지 않았습니다.");
                valid = false;
            }

            if (!errorPanel)
            {
                Debug.LogWarning("errorPanel이 할당되지 않았습니다.");
            }

            if (!loadingPercentageText)
            {
                Debug.LogWarning("loadingPercentageText가 할당되지 않았습니다.");
            }

            if (!colorAnalyzer)
            {
                Debug.LogWarning("colorAnalyzer가 할당되지 않았습니다. 분석이 스킵됩니다.");
            }

            if (!colorClassifier)
            {
                Debug.LogWarning("colorClassifier가 할당되지 않았습니다. 분류가 스킵됩니다.");
            }

            if (!homeButton)
            {
                Debug.LogWarning("homeButton이 할당되지 않았습니다.");
            }

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
        /// 사용 가능한 첫 번째 웹캠을 연결하고 스트리밍을 시작한다.
        /// 재진입 시 기존에 남은 텍스처 참조를 정리하여 하드웨어 충돌을 방지하기 위함.
        /// </summary>
        private void StartWebcam()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("사용 가능한 웹캠 장치가 없습니다.");
                return;
            }

            // 혹시 남아있을 수 있는 이전 세션의 참조 정리
            StopWebcam();

            _webcamTexture = new WebCamTexture(webcamWidth, webcamHeight, webcamFps);
            previewImage.texture = _webcamTexture;
            _webcamTexture.Play();

            StartCoroutine(ApplyUvRectAfterReady());
        }

        /// <summary>
        /// 웹캠 작동을 완전히 정지하고 메모리에서 해제한다.
        /// 하드웨어 리소스 반환을 보장하여 다시 씬에 진입했을 때 '텍스처 없음' 오류를 방지하기 위함.
        /// </summary>
        private void StopWebcam()
        {
            if (_webcamTexture)
            {
                if (_webcamTexture.isPlaying)
                {
                    _webcamTexture.Stop();
                }

                // 엔진 내부의 하드웨어 핸들을 명확히 해제
                Object.Destroy(_webcamTexture);
                _webcamTexture = null;
            }
        }

        /// <summary>
        /// 웹캠이 유효한 해상도를 반환할 때까지 대기 후 uvRect를 적용하고 화면을 표시한다.
        /// 웹캠 초기화 전의 검은 화면이나 찌그러진 화면이 사용자에게 노출되는 것을 방지하기 위함.
        /// </summary>
        private IEnumerator ApplyUvRectAfterReady()
        {
            // 웹캠이 실제 픽셀 데이터를 스트리밍할 때까지 대기함
            yield return new WaitUntil(() => _webcamTexture.width > 16);

            ApplyCoverUvRect();

            // 웹캠과 UI 배치가 완전히 끝난 시점에 비로소 화면을 밝게 만듦
            if (GameManager.Instance)
            {
                GameManager.Instance.ManualFadeIn();
            }
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
                yield return CoroutineData.GetWaitForSeconds(1f);
            }

            countdownText.gameObject.SetActive(false);
            CaptureFrame();
            
            yield return CoroutineData.GetWaitForSeconds(1f);

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
            if (!_webcamTexture) return;
            
            if (GameManager.Instance)
            {
                GameManager.Instance.PlayShutterSound();
            }

            _webcamTexture.Pause();

            int fullW = _webcamTexture.width;
            int fullH = _webcamTexture.height;

            bool flipH = FlipH;
            bool flipV = FlipV;

            _lastFlipH = flipH;
            _lastFlipV = flipV;

            using (TextureFrame analysisFrame = new TextureFrame(fullW, fullH, TextureFormat.RGBA32))
            {
                analysisFrame.ReadTextureOnCPU(_webcamTexture, flipH, flipV);
                _analysisTexture = new Texture2D(fullW, fullH, TextureFormat.RGBA32, false);
                _analysisTexture.SetPixels32(analysisFrame.GetPixels32());
                _analysisTexture.Apply();
            }

            Rect uv = previewImage.uvRect;
            int srcX = Mathf.RoundToInt(uv.x * fullW);
            int srcY = Mathf.RoundToInt(uv.y * fullH);
            int srcW = Mathf.RoundToInt(uv.width * fullW);
            int srcH = Mathf.RoundToInt(uv.height * fullH);

            _cropX = srcX;
            _cropY = srcY;

            Color32[] rawPixels = _webcamTexture.GetPixels32();
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

            _webcamTexture.Stop();

            previewImage.texture = _capturedTexture;
            previewImage.uvRect = new Rect(0f, 0f, 1f, 1f);

            Debug.Log($"[캡처] 표시={_capturedTexture.width}x{_capturedTexture.height} / 분석={_analysisTexture.width}x{_analysisTexture.height}");
        }

        // ─────────────────────────────────────────────────────────
        // Page2 — 퍼스널 컬러 분석
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 비동기 분석 로직을 수행하고 결과 페이지로 전환한다.
        /// 로딩 화면(Page2)이 유지되는 동안 Page3의 사전 세팅, 프레임 캡처, 로컬 저장, QR 생성을 모두 완료하여 체감 지연을 없애기 위함.
        /// </summary>
       private IEnumerator AnalyzeAndProceed()
        {
            float page2StartTime = Time.time;
            _isLogicComplete = false;
            _targetLoadingTime = 10f; 

            yield return null;

            StartCoroutine(AnimateLoadingText());

            bool isSuccess = false;
            PersonalColorType resultType = default;

            if (colorAnalyzer && colorClassifier && _analysisTexture)
            {
                int w = _analysisTexture.width;
                int h = _analysisTexture.height;

                using TextureFrame frame = new TextureFrame(w, h, TextureFormat.RGBA32);
                frame.ReadTextureOnCPU(_analysisTexture, false, false);

                Color32[] pixels = frame.GetPixels32();
                PersonalColorAnalyzer.ColorResult colorResult = colorAnalyzer.Analyze(pixels, w, h, frame);

                if (colorResult.isValid)
                {
                    resultType = colorClassifier.Classify(colorResult.skin, colorResult.eye, colorResult.hair);
                    isSuccess = true;

                    // 분석된 색상의 원본 좌표를 시각적으로 확인하기 위해 디버그 점을 그림
                    if (showDebugPoints && colorResult.sampledPoints != null)
                    {
                        DrawDebugPointsOnTexture(_capturedTexture, colorResult.sampledPoints, w, h, _cropX, _cropY, _lastFlipH, _lastFlipV);
                    }
                }
            }

            PreSetupPage3(isSuccess, resultType);
            yield return new WaitForEndOfFrame();
            yield return StartCoroutine(CaptureAndSaveCardRoutine());

            bool isQrReady = false;
            if (isSuccess && qrCodeGenerator)
            {
                StartCoroutine(qrCodeGenerator.Generate(
                    debugQrUrl,
                    onSuccess: (Texture2D qrTexture) =>
                    {
                        if (qrCodeImage)
                        {
                            qrCodeImage.texture = qrTexture;
                            qrCodeImage.gameObject.SetActive(true);
                        }
                        isQrReady = true;
                    },
                    onError: (string errorMsg) =>
                    {
                        Debug.LogError($"QR 생성 실패: {errorMsg}");
                        isQrReady = true; 
                    }
                ));
            }
            else
            {
                isQrReady = true;
            }

            while (!isQrReady)
            {
                yield return null;
            }

            float logicTime = Time.time - page2StartTime;
            _targetLoadingTime = Mathf.Max(logicTime + 1f, 10f);
            _isLogicComplete = true; 

            while (Time.time - page2StartTime < _targetLoadingTime)
            {
                yield return null;
            }

            if (loadingPercentageText)
            {
                loadingPercentageText.text = "100%";
            }

            yield return CoroutineData.GetWaitForSeconds(1f);
            yield return StartCoroutine(FadeToPage(page2, page3));
        }

        /// <summary>
        /// Page3가 화면에 보이지 않는 상태에서 사진과 테마 등 데이터를 미리 꽂아넣는다.
        /// 사진 비율이 다를 경우 발생하는 찌그러짐을 방지하기 위해 uvRect를 조절하여 Center Crop을 적용함.
        /// </summary>
        private void PreSetupPage3(bool isSuccess, PersonalColorType type)
        {
            if (_capturedTexture && photoImage)
            {
                photoImage.texture = _capturedTexture;

                // 텍스처와 UI의 가로세로 비율(Aspect Ratio)을 계산함
                float texAspect = (float)_capturedTexture.width / _capturedTexture.height;
                float uiAspect = photoImage.rectTransform.rect.width / photoImage.rectTransform.rect.height;

                // UI보다 텍스처가 더 넓은 경우 좌우를 자름
                if (texAspect > uiAspect)
                {
                    float uvWidth = uiAspect / texAspect;
                    photoImage.uvRect = new Rect((1f - uvWidth) * 0.5f, 0f, uvWidth, 1f);
                }
                // UI보다 텍스처가 더 긴 경우 위아래를 자름
                else
                {
                    float uvHeight = texAspect / uiAspect;
                    photoImage.uvRect = new Rect(0f, (1f - uvHeight) * 0.5f, 1f, uvHeight);
                }
            }

            HideAllColorPanels();

            if (isSuccess)
            {
                CanvasGroup targetPanel = GetColorPanel(type);
                if (targetPanel) targetPanel.alpha = 1f;
            }
            else
            {
                if (errorPanel) errorPanel.alpha = 1f;
            }

            // 캡처 화면에 빈 로우 이미지가 찍히지 않도록 임시로 꺼둠
            if (qrCodeImage) qrCodeImage.gameObject.SetActive(false);
        }

        /// <summary>
        /// 전용 카메라와 렌더 텍스처를 이용해 특정 UI 영역(Image_CardFrame)만 잘라내어 저장한다.
        /// 알파가 0인 CanvasGroup을 순간적으로 살려서 메모리에 그린 뒤 다시 숨기는 트릭을 사용함.
        /// </summary>
        private IEnumerator CaptureAndSaveCardRoutine()
        {
            if (!imageCardFrame || !uiCamera)
            {
                Debug.LogWarning("imageCardFrame 또는 uiCamera가 할당되지 않아 카드 캡처를 건너뜁니다.");
                yield break;
            }

            // 1. 임시 렌더 텍스처 생성 및 카메라 타겟 변경
            RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
            uiCamera.targetTexture = rt;

            // 2. 메모리 렌더링을 위해 아주 찰나에 Page3의 알파를 1로 강제 복구함 (현재 Page2에 가려져 있으므로 유저에겐 안 보임)
            float prevAlpha = page3.alpha;
            page3.alpha = 1f;

            yield return new WaitForEndOfFrame();

            uiCamera.Render();

            // 3. RectTransform의 4개 꼭짓점을 구해 화면(Screen) 좌표계로 변환
            Vector3[] corners = new Vector3[4];
            imageCardFrame.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);

            int width = Mathf.RoundToInt(topRight.x - bottomLeft.x);
            int height = Mathf.RoundToInt(topRight.y - bottomLeft.y);

            // 4. 지정된 영역만큼 픽셀 읽어오기
            RenderTexture.active = rt;
            Texture2D screenTex = new Texture2D(width, height, TextureFormat.RGB24, false);
            screenTex.ReadPixels(new Rect(bottomLeft.x, bottomLeft.y, width, height), 0, 0);
            screenTex.Apply();

            // 5. 카메라 및 UI 상태 원상 복구
            uiCamera.targetTexture = null;
            RenderTexture.active = null;
            page3.alpha = prevAlpha;
            Destroy(rt);

            // 6. 로컬 디스크에 파일로 저장함 (향후 이 위치에서 서버 API POST 요청으로 대체 가능)
            SaveLocalCardImage(screenTex);
            Destroy(screenTex);
        }

        /// <summary>
        /// 추출된 Texture2D를 PNG로 인코딩하여 로컬 Persistent Path에 저장한다.
        /// </summary>
        private void SaveLocalCardImage(Texture2D tex)
        {
            if (!tex) return;

            string fileName = $"ColorCard_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            byte[] png = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, png);
            Debug.Log($"카드 이미지 로컬 저장 완료 (서버 전송 대용): {path}");
        }

        /// <summary>
        /// 로딩 텍스트를 0%에서 100%로 애니메이션한다.
        /// 로직 소요 시간에 맞춰 퍼센트 증가 속도를 자연스럽게 동기화하기 위함.
        /// </summary>
        private IEnumerator AnimateLoadingText()
        {
            if (!loadingPercentageText) yield break;

            loadingPercentageText.text = "0%";
            yield return CoroutineData.GetWaitForSeconds(0.5f);

            float elapsed = 0f;
            float currentDuration = 9.5f; // 초기 예상 시간 (최소 10초 - 0.5초 대기)

            while (true)
            {
                elapsed += Time.deltaTime;

                if (_isLogicComplete)
                {
                    // 로직이 끝났다면 확정된 최종 대기 시간으로 목표를 갱신함
                    currentDuration = _targetLoadingTime - 0.5f;
                    if (elapsed >= currentDuration)
                    {
                        break;
                    }
                }
                else if (elapsed >= currentDuration * 0.9f)
                {
                    // 통신 지연 등으로 로직이 예상(10초)보다 길어질 경우, 
                    // 90% 구간에서 목표 시간을 계속 늘려 100%에 조기 도달하는 것을 막음
                    currentDuration += Time.deltaTime * 10f; 
                }

                // 0 ~ 99 사이의 정수형 퍼센트 계산
                int percent = Mathf.Clamp(Mathf.RoundToInt((elapsed / currentDuration) * 100f), 0, 99);
                loadingPercentageText.text = $"{percent}%";
                
                yield return null;
            }

            loadingPercentageText.text = "100%";
        }

        // ─────────────────────────────────────────────────────────
        // Page3 — 결과 표시
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 타이틀 씬으로 이동한다.
        /// 화면이 페이드 아웃되어 완전히 어두워진 뒤에 하드웨어를 정지하여 시각적 끊김을 제거하기 위함.
        /// </summary>
        private void LoadTitleScene()
        {
            if (!GameManager.Instance)
            {
                StopWebcam();
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.Scene.Title);
                return;
            }

            // 페이드 아웃이 완료된 시점(화면이 까만 상태)에 실행될 콜백으로 StopWebcam을 전달함
            GameManager.Instance.ChangeScene(
                GameConstants.Scene.Title,
                onFadeOutComplete: StopWebcam,
                autoFadeIn: true
            );
        }

        /// <summary>
        /// PersonalColorType에 대응하는 CanvasGroup을 반환한다.
        /// </summary>
        private CanvasGroup GetColorPanel(PersonalColorType type)
        {
            return type switch
            {
                PersonalColorType.SpringLight => springLight,
                PersonalColorType.SpringBright => springBright,
                PersonalColorType.SummerLight => summerLight,
                PersonalColorType.SummerMute => summerMute,
                PersonalColorType.AutumnMute => autumnMute,
                PersonalColorType.AutumnDeep => autumnDeep,
                PersonalColorType.WinterBright => winterBright,
                PersonalColorType.WinterDeep => winterDeep,
                _ => null
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
            if (to)
            {
                to.alpha = 0f;
                to.blocksRaycasts = false;
                to.interactable = false;
            }

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                if (from) from.alpha = 1f - t;
                if (to) to.alpha = t;
                yield return null;
            }

            SetPage(from, false);
            SetPage(to, true);
        }

        /// <summary>
        /// CanvasGroup의 표시 여부와 레이캐스트 차단 상태를 일괄 설정한다.
        /// </summary>
        private static void SetPage(CanvasGroup group, bool visible)
        {
            if (!group) return;

            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }

        /// <summary>
        /// Page3의 8종 컬러 패널 및 에러 패널을 모두 숨긴다.
        /// </summary>
        private void HideAllColorPanels()
        {
            CanvasGroup[] panels =
            {
                springLight, springBright, summerLight, summerMute,
                autumnMute, autumnDeep, winterBright, winterDeep, errorPanel
            };
            foreach (CanvasGroup panel in panels)
            {
                SetPage(panel, false);
            }
        }

        // ─────────────────────────────────────────────────────────
        // UV 크롭 (RawImage Cover 적용)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// RawImage의 표시 비율에 맞춰 웹캠 텍스처의 uvRect를 Center-Crop(Cover)으로 설정한다.
        /// </summary>
        private void ApplyCoverUvRect()
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
        
        /// <summary>
        /// 추출된 색상의 픽셀 위치를 캡처된 텍스처 위에 빨간색 점으로 시각화한다.
        /// 분석용 이미지(플립 적용)의 좌표를 표시용 이미지(크롭 및 논플립)의 좌표계로 역산하여 변환하기 위함.
        /// </summary>
        private void DrawDebugPointsOnTexture(Texture2D targetTex, List<Vector2> points, int fullW, int fullH, int srcX, int srcY, bool flipH, bool flipV)
        {
            if (!targetTex) return;

            Color32[] pixels = targetTex.GetPixels32();
            int texW = targetTex.width;
            int texH = targetTex.height;
            
            int halfSize = debugPointSize / 2;
            Color32 markerColor = Color.red;

            foreach (Vector2 pt in points)
            {
                // 1. 분석 텍스처에 적용됐던 플립을 역산하여 카메라 원본 좌표계로 복구함
                float unflipX = flipH ? (fullW - 1 - pt.x) : pt.x;
                float unflipY = flipV ? (fullH - 1 - pt.y) : pt.y;

                // 2. 화면 표시를 위해 잘려나간 UV 크롭 오프셋을 빼서 최종 텍스처 내 좌표를 구함
                int cx = Mathf.RoundToInt(unflipX - srcX);
                int cy = Mathf.RoundToInt(unflipY - srcY);

                for (int dy = -halfSize; dy <= halfSize; dy++)
                {
                    for (int dx = -halfSize; dx <= halfSize; dx++)
                    {
                        int px = cx + dx;
                        int py = cy + dy;

                        // 3. 변환된 좌표가 캡처 이미지 영역 내부인지 검사하고 점을 덮어씀
                        if (px >= 0 && px < texW && py >= 0 && py < texH)
                        {
                            pixels[py * texW + px] = markerColor;
                        }
                    }
                }
            }

            targetTex.SetPixels32(pixels);
            targetTex.Apply();
        }
    }
}