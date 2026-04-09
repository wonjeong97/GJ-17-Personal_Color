using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

        // 사용자의 명시적인 진입 액션을 처리하기 위해 클릭 이벤트를 연결함
        startButton.onClick.AddListener(LoadDescriptionScene);
    }

    /// <summary>
    /// 시작하기 버튼 클릭 시 호출되어 설명 씬으로 이동한다.
    /// 퍼스널 컬러 진단 프로세스의 첫 단계로 넘어가기 위함.
    /// </summary>
    private void LoadDescriptionScene()
    {
        SceneManager.LoadScene("1_Description");
    }
}