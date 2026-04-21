using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Screen = UnityEngine.Screen;

namespace My.Scripts._2_Capture
{
    public class ResultPageController : MonoBehaviour
    {
        [Header("Photo")]
        [SerializeField] private RawImage photoImage;
        [SerializeField] private CanvasGroup errorPanel;

        [Header("Color Panels")]
        [SerializeField] private CanvasGroup springLight;
        [SerializeField] private CanvasGroup springBright;
        [SerializeField] private CanvasGroup summerLight;
        [SerializeField] private CanvasGroup summerMute;
        [SerializeField] private CanvasGroup autumnMute;
        [SerializeField] private CanvasGroup autumnDeep;
        [SerializeField] private CanvasGroup winterBright;
        [SerializeField] private CanvasGroup winterDeep;

        [Header("Card Frame")]
        [SerializeField] private Image cardFrameImage;
        [SerializeField] private Sprite springFrameSprite;
        [SerializeField] private Sprite summerFrameSprite;
        [SerializeField] private Sprite autumnFrameSprite;
        [SerializeField] private Sprite winterFrameSprite;

        [Header("QR Code")]
        [SerializeField] private RawImage qrCodeImage;
        [SerializeField] private QRCodeGenerator qrCodeGenerator;

        [Header("Card Capture & Upload")]
        [SerializeField] private RectTransform imageCardFrame;
        [SerializeField] private Camera uiCamera;
        [SerializeField] private CanvasGroup page3;

        private const string UploadUrl    = "http://211.110.44.101:8501/uploadBinary.cfm";
        private const int    MaxRetries   = 10;
        private const int    TimeoutSec   = 20;

        public bool   UploadSucceeded { get; private set; }
        public string UploadedUrl     { get; private set; }

        /// <summary>
        /// Page3가 보이지 않는 상태에서 사진·패널·프레임을 미리 설정한다.
        /// 페이드 인 시 찌그러짐 없이 바로 표시되도록 uvRect Center Crop도 적용한다.
        /// </summary>
        public void PreSetupPage3(bool isSuccess, PersonalColorType type, Texture2D capturedTexture)
        {
            if (capturedTexture && photoImage)
            {
                photoImage.texture = capturedTexture;

                float texAspect = (float)capturedTexture.width / capturedTexture.height;
                float uiAspect = photoImage.rectTransform.rect.width / photoImage.rectTransform.rect.height;

                if (texAspect > uiAspect)
                {
                    float uvW = uiAspect / texAspect;
                    photoImage.uvRect = new Rect((1f - uvW) * 0.5f, 0f, uvW, 1f);
                }
                else
                {
                    float uvH = texAspect / uiAspect;
                    photoImage.uvRect = new Rect(0f, (1f - uvH) * 0.5f, 1f, uvH);
                }
            }

            HideAllColorPanels();

            if (isSuccess)
            {
                CanvasGroup panel = GetColorPanel(type);
                if (panel) panel.alpha = 1f;
                SetCardFrameSprite(type);
            }
            else
            {
                if (errorPanel) errorPanel.alpha = 1f;
            }

            if (qrCodeImage) qrCodeImage.gameObject.SetActive(false);
        }

        /// <summary>
        /// imageCardFrame 영역을 캡처하여 서버에 업로드한다.
        /// Page3가 숨겨져 있어도 alpha를 순간 복구해 렌더링 후 원상 복구하는 방식을 사용한다.
        /// 업로드 결과는 UploadSucceeded / UploadedUrl 프로퍼티로 확인한다.
        /// </summary>
        public IEnumerator CaptureAndUploadRoutine()
        {
            UploadSucceeded = false;
            UploadedUrl     = null;

            if (!imageCardFrame || !uiCamera)
            {
                Debug.LogWarning("imageCardFrame 또는 uiCamera가 할당되지 않아 카드 캡처를 건너뜁니다.");
                yield break;
            }

            // ── 카드 영역 렌더링 ───────────────────────────────────────
            RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
            uiCamera.targetTexture = rt;

            float prevAlpha = page3.alpha;
            page3.alpha = 1f;

            yield return new WaitForEndOfFrame();
            uiCamera.Render();

            Vector3[] corners = new Vector3[4];
            imageCardFrame.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 topRight   = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);

            int w = Mathf.RoundToInt(topRight.x - bottomLeft.x);
            int h = Mathf.RoundToInt(topRight.y - bottomLeft.y);

            RenderTexture.active = rt;
            Texture2D cardTex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cardTex.ReadPixels(new Rect(bottomLeft.x, bottomLeft.y, w, h), 0, 0);
            cardTex.Apply();

            uiCamera.targetTexture = null;
            RenderTexture.active = null;
            page3.alpha = prevAlpha;
            Destroy(rt);

            // ── PNG 인코딩 후 업로드 ──────────────────────────────────
            byte[] imageBytes = cardTex.EncodeToPNG();
            Destroy(cardTex);

            yield return StartCoroutine(UploadWithRetry(imageBytes));
        }

        private IEnumerator UploadWithRetry(byte[] data)
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                if (attempt > 0)
                    yield return new WaitForSeconds(1f);

                using UnityWebRequest req = new UnityWebRequest(UploadUrl, "POST");
                req.uploadHandler   = new UploadHandlerRaw(data);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/octet-stream");
                req.timeout = TimeoutSec;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    UploadedUrl     = req.downloadHandler.text.Trim();
                    UploadSucceeded = true;
                    Debug.Log($"[업로드] 성공 (시도 {attempt + 1}/{MaxRetries}): {UploadedUrl}");
                    yield break;
                }

                Debug.LogWarning($"[업로드] 실패 (시도 {attempt + 1}/{MaxRetries}): {req.error}");
            }

            Debug.LogError("[업로드] 최대 시도 횟수 초과. 최종 실패.");
        }

        public IEnumerator GenerateQRCode(string url)
        {
            if (!qrCodeGenerator || string.IsNullOrEmpty(url)) yield break;

            yield return qrCodeGenerator.Generate(
                url,
                onSuccess: (Texture2D qrTex) =>
                {
                    if (qrCodeImage)
                    {
                        qrCodeImage.texture = qrTex;
                        qrCodeImage.gameObject.SetActive(true);
                    }
                },
                onError: (string msg) => Debug.LogError($"QR 생성 실패: {msg}")
            );
        }

        public void HideAllColorPanels()
        {
            CanvasGroup[] panels =
            {
                springLight, springBright, summerLight, summerMute,
                autumnMute, autumnDeep, winterBright, winterDeep, errorPanel
            };
            foreach (CanvasGroup panel in panels)
            {
                if (!panel) continue;
                panel.alpha = 0f;
                panel.blocksRaycasts = false;
                panel.interactable = false;
            }
        }

        private CanvasGroup GetColorPanel(PersonalColorType type) => type switch
        {
            PersonalColorType.SpringLight  => springLight,
            PersonalColorType.SpringBright => springBright,
            PersonalColorType.SummerLight  => summerLight,
            PersonalColorType.SummerMute   => summerMute,
            PersonalColorType.AutumnMute   => autumnMute,
            PersonalColorType.AutumnDeep   => autumnDeep,
            PersonalColorType.WinterBright => winterBright,
            PersonalColorType.WinterDeep   => winterDeep,
            _ => null
        };

        private void SetCardFrameSprite(PersonalColorType type)
        {
            if (!cardFrameImage) return;

            Sprite target = null;
            switch (type)
            {
                case PersonalColorType.SpringLight:
                case PersonalColorType.SpringBright:
                    target = springFrameSprite; break;
                case PersonalColorType.SummerLight:
                case PersonalColorType.SummerMute:
                    target = summerFrameSprite; break;
                case PersonalColorType.AutumnMute:
                case PersonalColorType.AutumnDeep:
                    target = autumnFrameSprite; break;
                case PersonalColorType.WinterBright:
                case PersonalColorType.WinterDeep:
                    target = winterFrameSprite; break;
            }

            if (target)
                cardFrameImage.sprite = target;
            else
                Debug.LogWarning("해당 계절의 프레임 스프라이트가 할당되지 않았습니다.");
        }

    }
}
