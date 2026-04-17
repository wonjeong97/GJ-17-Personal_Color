using My.Scripts.Global;
using UnityEngine;
using UnityEngine.UI;

namespace My.Scripts._0_Title
{
    /// <summary>
    /// 타이틀 씬의 UI 이벤트와 씬 전환을 관리한다.
    /// </summary>
    public class TitleManager : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Button startButton;

        private void Start()
        {
            if (!startButton)
            {
                Debug.LogError("startButton 컴포넌트가 할당되지 않았습니다.");
                return;
            }

            if (GameManager.Instance)
            {
                startButton.onClick.AddListener(GameManager.Instance.PlayClickSound);
            }
            
            startButton.onClick.AddListener(LoadDescriptionScene);
        }

        /// <summary>
        /// 시작하기 버튼 클릭 시 호출되어 설명 씬으로 이동한다.
        /// GameManager.ChangeScene()을 통해 페이드 효과를 포함한 씬 전환을 수행한다.
        /// </summary>
        private void LoadDescriptionScene()
        {
            GameManager.Instance.ChangeScene(GameConstants.Scene.Description);
        }
    }
}