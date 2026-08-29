using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 全局音频管理器：管理BGM和SFX的播放，跨场景持久化。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioSource sfxSource;

        [Header("BGM Clips")]
        [SerializeField] private AudioClip mainMenuBGM;       // 终章
        [SerializeField] private AudioClip gameSceneBGM;     // 全局音乐
        [SerializeField] private List<AudioClip> bargainBGMs = new List<AudioClip>(); // bargain过程, bargain过程2

        [Header("SFX Clips")]
        [SerializeField] private AudioClip diceRollSFX;       // 摇骰子
        [SerializeField] private AudioClip diceLandSFX;       // 骰子落下
        [SerializeField] private List<AudioClip> eventSFXs = new List<AudioClip>();  // 事件1, 事件2
        [SerializeField] private AudioClip rewardSFX;        // 奖励
        [SerializeField] private AudioClip penaltySFX;       // 惩罚
        [SerializeField] private AudioClip trainSFX;          // 火车
        [SerializeField] private AudioClip coinSFX;           // 金币增加
        [SerializeField] private AudioClip buildingUpgradeSFX; // 建筑升级
        [SerializeField] private AudioClip victorySFX;        // 胜利
        [SerializeField] private List<AudioClip> defeatSFXs = new List<AudioClip>(); // 失败, 失败1, 失败2, 失败结算

        private Coroutine trainStopRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure exactly one AudioListener exists (remove extras on other GameObjects)
            var listeners = FindObjectsOfType<AudioListener>();
            foreach (var l in listeners)
            {
                if (l.gameObject != gameObject)
                    Destroy(l);
            }
            if (GetComponent<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();

            if (bgmSource == null) bgmSource = gameObject.AddComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();

            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;

            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (bgmSource != null) bgmSource.volume = GameSettings.BGMVolume;
            if (sfxSource != null) sfxSource.volume = GameSettings.SFXVolume;
        }

        /// <summary>更新BGM音量（由设置面板调用）</summary>
        public void SetBGMVolume(float volume)
        {
            if (bgmSource != null) bgmSource.volume = volume;
        }

        /// <summary>更新SFX音量（由设置面板调用）</summary>
        public void SetSFXVolume(float volume)
        {
            if (sfxSource != null) sfxSource.volume = volume;
        }

        // ==================== BGM ====================

        public void PlayBGM(AudioClip clip)
        {
            if (bgmSource == null || clip == null) return;
            bgmSource.clip = clip;
            bgmSource.volume = GameSettings.BGMVolume;
            bgmSource.Play();
        }

        public void StopBGM()
        {
            if (bgmSource != null) bgmSource.Stop();
        }

        public void PlayMainMenuBGM() => PlayBGM(mainMenuBGM);

        public void PlayGameSceneBGM() => PlayBGM(gameSceneBGM);

        public void PlayRandomBargainBGM()
        {
            if (bargainBGMs != null && bargainBGMs.Count > 0)
                PlayBGM(bargainBGMs[Random.Range(0, bargainBGMs.Count)]);
        }

        // ==================== SFX ====================

        public void PlaySFX(AudioClip clip)
        {
            if (sfxSource == null || clip == null) return;
            sfxSource.PlayOneShot(clip, GameSettings.SFXVolume);
        }

        private IEnumerator PlaySFXDelayedRoutine(AudioClip clip, float delay)
        {
            yield return new WaitForSeconds(delay);
            PlaySFX(clip);
        }

        public void PlayDiceRoll() => PlaySFX(diceRollSFX);

        public void PlayDiceLandDelayed(float delay = 0.5f)
        {
            if (diceLandSFX != null)
                StartCoroutine(PlaySFXDelayedRoutine(diceLandSFX, delay));
        }

        public void PlayRandomEvent()
        {
            if (eventSFXs != null && eventSFXs.Count > 0)
                PlaySFX(eventSFXs[Random.Range(0, eventSFXs.Count)]);
        }

        public void PlayReward() => PlaySFX(rewardSFX);

        public void PlayPenalty() => PlaySFX(penaltySFX);

        /// <summary>播放火车音效，2秒后自动停止</summary>
        public void PlayTrain()
        {
            if (sfxSource == null || trainSFX == null) return;
            if (trainStopRoutine != null) StopCoroutine(trainStopRoutine);
            sfxSource.clip = trainSFX;
            sfxSource.loop = true;
            sfxSource.volume = GameSettings.SFXVolume;
            sfxSource.Play();
            trainStopRoutine = StartCoroutine(StopTrainAfter(2f));
        }

        private IEnumerator StopTrainAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            sfxSource.Stop();
            sfxSource.loop = false;
            sfxSource.clip = null;
            trainStopRoutine = null;
        }

        public void PlayCoin() => PlaySFX(coinSFX);

        public void PlayBuildingUpgrade() => PlaySFX(buildingUpgradeSFX);

        public void PlayVictory() => PlaySFX(victorySFX);

        public void PlayRandomDefeat()
        {
            if (defeatSFXs != null && defeatSFXs.Count > 0)
                PlaySFX(defeatSFXs[Random.Range(0, defeatSFXs.Count)]);
        }
    }
}
