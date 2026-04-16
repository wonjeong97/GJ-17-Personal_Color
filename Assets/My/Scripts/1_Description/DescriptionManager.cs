using My.Scripts.Global;
using UnityEngine;
using UnityEngine.UI;

namespace My.Scripts._1_Description
{
    /// <summary>
    /// 설명 씬의 페이지 전환 및 씬 이동 로직을 관리한다.
    /// 사용자가 순차적으로 페이지를 확인한 후 캡처 씬으로 넘어가도록 유도하기 위함.
    /// </summary>
    public class DescriptionManager : MonoBehaviour
    {
        [Header("Pages")]
        [SerializeField] private GameObject page1;
        [SerializeField] private GameObject page2;

        [Header("UI Components")]
        [SerializeField] private Button nextButton;
        [SerializeField] private Button startCaptureButton;
        [SerializeField] private Button homeButton;

        private void Start()
        {
            if (!page1 || !page2)
            {
                Debug.LogError("페이지 오브젝트가 할당되지 않았습니다.");
                return;
            }

            if (!nextButton || !startCaptureButton || !homeButton)
            {
                Debug.LogError("UI 버튼 컴포넌트가 모두 할당되지 않았습니다.");
                return;
            }

            // 초기 상태 설정: 페이지 1 활성화, 페이지 2 비활성화
            page1.SetActive(true);
            page2.SetActive(false);

            // 이벤트 리스너 등록
            nextButton.onClick.AddListener(ShowPage2);
            startCaptureButton.onClick.AddListener(LoadCaptureScene);
            homeButton.onClick.AddListener(LoadTitleScene);
        }

        /// <summary>
        /// 다음 버튼 클릭 시 호출되어 두 번째 페이지를 표시한다.
        /// 사용자가 첫 번째 설명을 모두 읽고 다음 단계로 진행하기 위함.
        /// </summary>
        private void ShowPage2()
        {
            page1.SetActive(false);
            page2.SetActive(true);
        }

        /// <summary>
        /// 얼굴 촬영 버튼 클릭 시 호출되어 캡처 씬으로 이동한다.
        /// 캡처 씬의 웹캠 준비 시간을 벌기 위해 자동 페이드인을 비활성화함.
        /// </summary>
        private void LoadCaptureScene()
        {
            if (GameManager.Instance)
            {
                // 세 번째 인자를 false로 전달하여 캡처 씬의 수동 페이드인을 기다리게 함
                GameManager.Instance.ChangeScene(GameConstants.Scene.Capture, null, false);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.Scene.Capture);
            }
        }

        /// <summary>
        /// 홈 버튼 클릭 시 호출되어 타이틀 씬으로 이동한다.
        /// </summary>
        private void LoadTitleScene()
        {
            if (GameManager.Instance)
            {
                GameManager.Instance.ChangeScene(GameConstants.Scene.Title);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.Scene.Title);
            }
        }
    }
}