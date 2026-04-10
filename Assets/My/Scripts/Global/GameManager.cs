using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wonjeong.Data;
using Wonjeong.Reporter;
using Wonjeong.UI;
using Wonjeong.Utils;

namespace My.Scripts.Global
{
    /// <summary>
    /// 게임의 전반적인 상태와 씬 전환을 관리한다.
    /// 싱글톤, 설정 로드, 페이드 씬 전환 기능만을 추출한 경량화 버전.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;
        
        [SerializeField] private Reporter reporter;

        private bool _isTransitioning;
        private float _fadeTime = 0.5f;
        private Coroutine _transitionRoutine;

        /// <summary>
        /// 싱글톤 인스턴스를 초기화하고 전역 상태를 유지함.
        /// 중복 생성을 방지하고 씬 전환 시 파괴되지 않도록 설정하기 위함.
        /// </summary>
        private void Awake()
        {   
            if (!Instance)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                TimestampLogHandler.Attach();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 게임 초기 설정 로드 및 불필요한 마우스 커서/리포터 UI를 숨김.
        /// </summary>
        private void Start()
        {
            Cursor.visible = false;
            Application.runInBackground = true;
            
            LoadSettings();
            if (reporter && reporter.show) reporter.show = false;
        }
        
        /// <summary>
        /// 디버그 모드 전환 및 강제 씬 스킵 키보드 입력을 처리함.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.D) && reporter)
            {
                reporter.showGameManagerControl = !reporter.showGameManagerControl;
                if (reporter.show) reporter.show = false;
            }
            else if (Input.GetKeyDown(KeyCode.M)) 
            {
                Cursor.visible = !Cursor.visible;
            }
        }

        /// <summary>
        /// 로컬 JSON 파일에서 전역 환경 설정값을 로드함.
        /// 씬 전환 속도 및 API 엔드포인트 등의 런타임 구성값을 동적으로 반영하기 위함.
        /// </summary>
        private void LoadSettings()
        {
            Settings settings = JsonLoader.Load<Settings>(GameConstants.Path.JsonSetting);
            if (settings != null)
            {
                _fadeTime = settings.fadeTime;
            }
            else
            {
                Debug.LogWarning("Settings.json 설정이 누락됨.");
            }
        }

        /// <summary>
        /// 페이드 아웃 연출을 동반하여 지정된 씬으로 이동함.
        /// 중복 전환 요청을 막고 비동기 트랜지션을 시작하기 위함.
        /// </summary>
        /// <param name="sceneName">이동할 대상 씬의 이름</param>
        public void ChangeScene(string sceneName)
        {
            if (_isTransitioning) return;

            _isTransitioning = true;
            _transitionRoutine = StartCoroutine(ChangeSceneRoutine(sceneName));
        }

        /// <summary>
        /// 실제 씬 전환과 페이드 효과를 비동기로 제어하는 코루틴.
        /// 화면이 완전히 어두워진 후 씬을 로드하여 시각적 끊김을 방지하기 위함.
        /// </summary>
        /// <param name="sceneName">이동할 대상 씬의 이름</param>
        private IEnumerator ChangeSceneRoutine(string sceneName)
        {
            // FadeManager가 씬에 없을 경우 즉시 씬 로드 수행
            if (!FadeManager.Instance)
            {
                SceneManager.LoadScene(sceneName);
                _isTransitioning = false;
                yield break;
            }

            bool fadeDone = false;
            FadeManager.Instance.FadeOut(_fadeTime, () => { fadeDone = true; });
        
            while (!fadeDone) 
            {
                yield return null;
            }

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
            while (asyncLoad != null && !asyncLoad.isDone) 
            {
                yield return null;
            }

            FadeManager.Instance.FadeIn(_fadeTime);
            _isTransitioning = false;
        }
    }
}