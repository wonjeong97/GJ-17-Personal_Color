using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Wonjeong.Utils;

namespace My.Scripts._2_Capture
{
    /// <summary>
    /// 외부 API를 활용하여 주어진 URL을 QR 코드 텍스처로 변환한다.
    /// 통신 불안정으로 인한 실패를 방지하기 위해 타임아웃 및 재시도 로직을 포함함.
    /// </summary>
    public class QRCodeGenerator : MonoBehaviour
    {
        [Header("Network Settings")]
        [SerializeField] private int timeoutSeconds = 10;
        [SerializeField] private int maxRetries = 10;
        [SerializeField] private float retryDelay = 1.0f;

        private const string ApiBaseUrl = "https://api.qrserver.com/v1/create-qr-code/?size=256x256&data=";

        /// <summary>
        /// 전달받은 URL을 QR 코드로 변환하여 콜백으로 반환한다.
        /// 일시적 네트워크 단절에 대비하여 지정된 횟수만큼 통신 재시도를 수행하기 위함.
        /// </summary>
        /// <param name="url">QR 코드로 변환할 웹 주소</param>
        /// <param name="onSuccess">성공 시 텍스처를 반환받을 콜백 함수</param>
        /// <param name="onError">최종 실패 시 에러 메시지를 반환받을 콜백 함수</param>
        public IEnumerator Generate(string url, Action<Texture2D> onSuccess, Action<string> onError)
        {
            string encodedUrl = Uri.EscapeDataString(url);
            string requestUrl = ApiBaseUrl + encodedUrl;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(requestUrl))
                {
                    // 무한 대기 상태에 빠지는 것을 방지하기 위해 명시적인 타임아웃을 설정함
                    req.timeout = timeoutSeconds;

                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        Texture2D qrTexture = DownloadHandlerTexture.GetContent(req);
                        onSuccess?.Invoke(qrTexture);
                        yield break; // 성공 시 코루틴을 즉시 종료함
                    }

                    if (attempt < maxRetries - 1)
                    {
                        Debug.LogWarning($"QR 생성 API 호출 실패 ({attempt + 1}/{maxRetries}): {req.error}. {retryDelay}초 후 재시도합니다.");
                        // 서버 부하 및 연속적인 통신 충돌을 막기 위해 지정된 시간만큼 대기함
                        yield return CoroutineData.GetWaitForSeconds(retryDelay);
                    }
                    else
                    {
                        Debug.LogError($"QR API 최종 통신 실패 (URL: {url}) - {req.error}");
                        onError?.Invoke(req.error);
                    }
                }
            }
        }
    }
}