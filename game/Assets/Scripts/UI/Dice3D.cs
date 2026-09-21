using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 视效v2：3D像素骰子。
    /// 运行时创建（代码动态，零场景依赖）：两个立方体 + Resources/VFX/DiceFace1..6 六面贴图，
    /// 独立正交相机（独立渲染层，不影响主画面）+ RenderTexture 投到骰子面板上的 RawImage 视口。
    /// 掷骰：随机翻滚（角速度衰减+弹跳）→ 平滑落定到指定点数面朝相机。
    /// 贴图缺失时 Play() 返回 false，调用方回落原有2D洗面动画（见 DiceRollController）。
    /// 视口位置/大小：场景中的 "Dice3DViewport"（骰子面板子物体），美术可直接调 RectTransform。
    /// </summary>
    public static class Dice3D
    {
        private static RenderTexture _rt;
        private static Camera _cam;
        private static Transform _die1, _die2;
        private static GameObject _stage;
        private static MonoBehaviour _host;
        private static RawImage _view;

        /// <summary>是否正在翻滚（调用方用 WaitUntil 等待落定）</summary>
        public static bool IsRolling { get; private set; }

        // 落定朝向：把对应点数的面转到朝向相机(+Z)
        // 立方体材质槽顺序 +X,-X,+Y,-Y,+Z,-Z = 点数 2,5,3,4,1,6（对面和为7的标准骰）
        private static readonly int[] FaceMaterialOrder = { 2, 5, 3, 4, 1, 6 };

        /// <summary>创建视口（游戏开始时调用一次；重复调用无副作用）</summary>
        public static void EnsureView(GameObject dicePanel, Canvas canvas)
        {
            if (_view != null || dicePanel == null) return;
            if (Resources.Load<Texture2D>("VFX/DiceFace1") == null)
            {
                Debug.LogWarning("[Dice3D] Resources/VFX/DiceFace1..6 缺失，3D骰子停用（回落2D洗面）");
                return;
            }

            const int layer = 31; // 独立渲染层：只有3D骰子相机看它
            _rt = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32);
            _rt.Create();

            _stage = new GameObject("Dice3DStage");
            Object.DontDestroyOnLoad(_stage);

            var camGo = new GameObject("Dice3DCam");
            camGo.transform.SetParent(_stage.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.transform.localPosition = new Vector3(0, 0, -10);
            _cam.orthographic = true;
            _cam.orthographicSize = 1.6f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0); // 透明底：透出骰子面板背景
            _cam.cullingMask = 1 << layer;
            _cam.targetTexture = _rt;
            _cam.enabled = false; // 仅翻滚时渲染，静止帧保留在RT上

            _die1 = CreateDie("Die1", new Vector3(-0.85f, 0, 0), layer);
            _die2 = CreateDie("Die2", new Vector3(0.85f, 0, 0), layer);

            // UI视口：挂在骰子面板上（美术可在场景里调 Dice3DViewport）
            var viewObj = new GameObject("Dice3DViewport", typeof(RawImage));
            viewObj.transform.SetParent(dicePanel.transform, false);
            _view = viewObj.GetComponent<RawImage>();
            _view.texture = _rt;
            _view.raycastTarget = false; // 不挡面板按钮点击
            var rt = _view.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420, 220);
            rt.anchoredPosition = new Vector2(0, 20);
        }

        private static Transform CreateDie(string name, Vector3 pos, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(_stage.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * 0.9f;

            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Unlit/Texture");
            var mats = new Material[6];
            for (int i = 0; i < 6; i++)
            {
                var tex = Resources.Load<Texture2D>("VFX/DiceFace" + FaceMaterialOrder[i]);
                mats[i] = new Material(shader);
                mats[i].mainTexture = tex;
            }
            mr.materials = mats;

            if (_host == null) _host = go.AddComponent<DiceSpinHost>();
            return go.transform;
        }

        /// <summary>掷骰：翻滚后落定到 d1/d2 点数面。返回 false 表示3D骰不可用（回落2D）。</summary>
        public static bool Play(int d1, int d2, float duration = 1.2f)
        {
            if (_die1 == null || _die2 == null || _host == null) return false;
            if (IsRolling) return true; // 上一次还没结束，让调用方等待
            _cam.enabled = true;
            IsRolling = true;
            _host.StartCoroutine(RollRoutine(_die1, d1, duration, new Vector3(-0.85f, 0, 0)));
            _host.StartCoroutine(RollRoutine(_die2, d2, duration, new Vector3(0.85f, 0, 0)));
            return true;
        }

        private static IEnumerator RollRoutine(Transform die, int face, float duration, Vector3 homePos)
        {
            float spinTime = Mathf.Max(0.5f, duration * 0.65f);
            float settle = Mathf.Max(0.3f, duration * 0.35f);

            Vector3 axis = Random.onUnitSphere;
            float speed = Random.Range(500f, 750f) * Mathf.Deg2Rad;
            die.rotation = Random.rotationUniform;

            float t = 0f;
            while (t < spinTime)
            {
                t += Time.deltaTime;
                die.Rotate(axis, speed * Time.deltaTime, Space.World);
                // 翻滚期的衰减弹跳
                float bounce = Mathf.Abs(Mathf.Sin(t * 18f)) * 0.25f * (1f - t / spinTime);
                die.localPosition = homePos + new Vector3(0, bounce, 0);
                yield return null;
            }

            // 平滑落定到目标点数面
            Quaternion target = FaceToQuaternion(face);
            Quaternion from = die.rotation;
            Vector3 posFrom = die.localPosition;
            float k = 0f;
            while (k < 1f)
            {
                k += Time.deltaTime / settle;
                float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
                die.rotation = Quaternion.Slerp(from, target, s);
                die.localPosition = Vector3.Lerp(posFrom, homePos, s);
                yield return null;
            }
            die.localRotation = target;
            die.localPosition = homePos;

            // 两骰同帧落定；后者负责收尾（关相机省性能，RT保留最终画面）
            if (IsRolling)
            {
                IsRolling = false;
                _cam.enabled = false;
            }
        }

        /// <summary>点数面朝相机的朝向（材质分配见 FaceMaterialOrder）</summary>
        private static Quaternion FaceToQuaternion(int face)
        {
            switch (face)
            {
                case 1: return Quaternion.identity;          // 面1在 +Z
                case 2: return Quaternion.Euler(0, -90, 0);  // 面2在 +X → 转到 +Z
                case 3: return Quaternion.Euler(90, 0, 0);   // 面3在 +Y
                case 4: return Quaternion.Euler(-90, 0, 0);  // 面4在 -Y
                case 5: return Quaternion.Euler(0, 90, 0);   // 面5在 -X
                default: return Quaternion.Euler(0, 180, 0); // 面6在 -Z
            }
        }

        private class DiceSpinHost : MonoBehaviour { }
    }
}
