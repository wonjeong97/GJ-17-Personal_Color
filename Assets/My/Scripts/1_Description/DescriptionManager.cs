using My.Scripts.Global;
using UnityEngine;
using UnityEngine.UI;

namespace My.Scripts._1_Description
{
    /// <summary>
    /// 설명 씬의 UI 이벤트와 씬 전환을 관리한다.
    /// </summary>
    public class DescriptionManager : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Button nextButton;
        [SerializeField] private Button homeButton;

        private void Start()
        {
            if (!nextButton)
            {
                Debug.LogError("nextButton 컴포넌트가 할당되지 않았습니다.");
                return;
            }

            if (!homeButton)
            {
                Debug.LogError("homeButton 컴포넌트가 할당되지 않았습니다.");
                return;
            }

            nextButton.onClick.AddListener(LoadNextScene);
            homeButton.onClick.AddListener(LoadTitleScene);
        }

        /// <summary>
        /// 다음 버튼 클릭 시 호출되어 다음 씬으로 이동한다.
        /// </summary>
        private void LoadNextScene()
        {
            GameManager.Instance.ChangeScene(GameConstants.Scene.Capture);
        }

        /// <summary>
        /// 홈 버튼 클릭 시 호출되어 타이틀 씬으로 이동한다.
        /// </summary>
        private void LoadTitleScene()
        {
            GameManager.Instance.ChangeScene(GameConstants.Scene.Title);
        }
    }
}
