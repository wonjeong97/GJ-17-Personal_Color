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
        
        [Header("Sound")]
        [SerializeField] private AudioSource uiAudioSource;
        [SerializeField] private AudioClip defaultClickSound;
        [SerializeField] private AudioClip shutterSound;

        private bool _isTransitioning;
        private float _fadeTime = 0.5f;
        private Coroutine _transitionRoutine;

        private const float IdleTimeout = 60f;
        private float _idleTimer;

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
                
                // 오디오 소스 컴포넌트가 없는 경우 자동 추가
                if (!uiAudioSource)
                {
                    uiAudioSource = gameObject.AddComponent<AudioSource>();
                    uiAudioSource.playOnAwake = false;
                }

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

            UpdateIdleTimer();
        }

        private void UpdateIdleTimer()
        {
            bool isTitle = SceneManager.GetActiveScene().name == GameConstants.Scene.Title;
            if (isTitle || _isTransitioning) return;

            if (Input.anyKey || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.touchCount > 0)
            {
                _idleTimer = 0f;
                return;
            }

            _idleTimer += Time.deltaTime;
            if (_idleTimer >= IdleTimeout)
            {
                _idleTimer = 0f;
                ChangeScene(GameConstants.Scene.Title);
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
        /// 페이드 아웃 연출을 동반하여 지정된 씬으로 이동한다.
        /// 화면이 완전히 가려진 시점에 정리 작업을 수행하고, 새로운 씬의 준비 상태에 따라 페이드인을 지연시키기 위함.
        /// </summary>
        /// <param name="sceneName">이동할 대상 씬의 이름</param>
        /// <param name="onFadeOutComplete">화면이 완전히 어두워졌을 때 실행할 정리 로직 (예: 웹캠 정지)</param>
        /// <param name="autoFadeIn">새로운 씬 로드 후 자동으로 페이드인을 수행할지 여부</param>
        public void ChangeScene(string sceneName, System.Action onFadeOutComplete = null, bool autoFadeIn = true)
        {
            if (_isTransitioning) return;

            _isTransitioning = true;
            _transitionRoutine = StartCoroutine(ChangeSceneRoutine(sceneName, onFadeOutComplete, autoFadeIn));
        }

        private IEnumerator ChangeSceneRoutine(string sceneName, System.Action onFadeOutComplete, bool autoFadeIn)
        {
            if (!FadeManager.Instance)
            {
                onFadeOutComplete?.Invoke();
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

            // 화면이 완전히 블랙인 상태에서 웹캠 정지 등 무거운 정리 작업을 수행함
            onFadeOutComplete?.Invoke();

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
            while (asyncLoad != null && !asyncLoad.isDone)
            {
                yield return null;
            }

            // 새로운 씬에서 웹캠 등이 준비될 때까지 기다려야 할 경우 자동 페이드인을 건너뜀
            if (autoFadeIn)
            {
                FadeManager.Instance.FadeIn(_fadeTime);
            }

            _isTransitioning = false;
        }

        /// <summary>
        /// 외부에서 수동으로 페이드인을 호출한다.
        /// 특정 씬(예: 캡처 씬)에서 하드웨어 준비가 완료된 시점에 화면을 보여주기 위함.
        /// </summary>
        public void ManualFadeIn()
        {
            if (FadeManager.Instance)
            {
                FadeManager.Instance.FadeIn(_fadeTime);
            }
        }
        
                
        /// <summary>
        /// UI 기본 클릭음을 재생한다.
        /// 씬 전환 애니메이션 중에도 사운드가 끊기지 않도록 전역 매니저에서 오디오를 처리하기 위함.
        /// </summary>
        public void PlayClickSound()
        {
            if (uiAudioSource && defaultClickSound)
            {
                uiAudioSource.PlayOneShot(defaultClickSound);
            }
            else
            {
                Debug.LogWarning("uiAudioSource 또는 defaultClickSound가 할당되지 않았습니다.");
            }
        }
        
        /// <summary>
        /// 사진 촬영 시 셔터 효과음을 재생한다.
        /// 캡처 버튼 클릭 후 실제 데이터가 처리되는 시점에 청각적 피드백을 제공하기 위함.
        /// </summary>
        public void PlayShutterSound()
        {
            if (uiAudioSource && shutterSound)
            {
                uiAudioSource.PlayOneShot(shutterSound);
            }
            else
            {
                Debug.LogWarning("uiAudioSource 또는 shutterSound가 할당되지 않았습니다.");
            }
        }
    }
}