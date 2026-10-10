using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 2D(正投影)カメラと3D(透視投影)カメラを切り替える仮スクリプト。
/// 切り替え時は遷移用カメラで「焦点を中心にしたドリーズーム」を行い、
/// 焦点面の大きさを保ったまま遠近感だけを滑らかに変化させる。
/// </summary>
public class CameraModeSwitcher : MonoBehaviour
{
    public enum ViewMode { Mode2D, Mode3D }

    /// <summary>
    /// 遷移中の「見え方」。両端のカメラも同じ形で表して補間する。
    /// </summary>
    private struct ViewState
    {
        public Vector3 focusPoint;   // 画面中心で捉えている点
        public Quaternion rotation;
        public float distance;       // カメラから焦点面までの距離
        public float halfHeight;     // 焦点面で見えている範囲の半分の高さ(ワールド単位)
        public float strength;       // 遠近感の強さ: 1 = 透視投影 / 0 = 正投影
        public float near;
        public float far;
        public Color background;
    }

    // これ以下の強さは投影行列が極端な値になるため丸める(ほぼ正投影)
    private const float MinStrength = 0.001f;

    [Header("Cameras")]
    [SerializeField] private Camera camera2D;   // Projection: Orthographic
    [SerializeField] private Camera camera3D;   // Projection: Perspective

    [Header("Settings")]
    [SerializeField] private ViewMode startMode = ViewMode.Mode3D;
    [SerializeField] private bool allowKeyToggle = true; // デバッグ用: Tabキーで切り替え

    [Header("Transition")]
    [SerializeField] private bool smoothTransition = true;
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.8f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool useUnscaledTime = false;
    [Tooltip("未設定の場合は実行時に自動生成します(URPなどで設定を揃えたい場合は手動で用意)")]
    [SerializeField] private Camera transitionCamera;

    [Header("Focus (遷移の中心)")]
    [Tooltip("画面の中心付近で捉えている対象(プレイヤーなど)。未設定の場合は Focus Distance を使う")]
    [SerializeField] private Transform focusTarget;
    [Tooltip("Focus Target 未設定時に、カメラから焦点面までの距離として使う値(2D/3D共通)")]
    [SerializeField, Min(0.1f)] private float focusDistance = 10f;

    [Header("Perspective Falloff")]
    [Tooltip("背景(奥の物体)が焦点距離の何倍ほど遠いかの目安。" +
             "切り替えの直前/直後に画面が急に変わって見えるなら大きく、" +
             "序盤に変化が集中しすぎるなら小さくする")]
    [SerializeField, Min(1.1f)] private float depthRatio = 5f;

    /// <summary>現在の(遷移中は遷移先の)モード</summary>
    public ViewMode CurrentMode { get; private set; }

    /// <summary>遷移アニメーション中かどうか</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>モードの切り替えが始まったときに通知(遷移を使う場合は遷移の開始時)</summary>
    public event Action<ViewMode> OnModeChanged;

    /// <summary>遷移が完全に終わったときに通知(遷移なしの場合は切り替え直後)</summary>
    public event Action<ViewMode> OnTransitionCompleted;

    private Coroutine transitionRoutine;
    private bool createdTransitionCamera;
    private ViewState lastState; // 遷移中の最新状態(割り込み時の起点)

    private void Awake()
    {
        if (camera2D == null || camera3D == null)
        {
            Debug.LogError("[CameraModeSwitcher] カメラが未設定です。", this);
            enabled = false;
            return;
        }

        // 念のため投影モードを強制
        camera2D.orthographic = true;
        camera3D.orthographic = false;

        SetupTransitionCamera();

        CurrentMode = startMode;
        ShowImmediate(startMode);
    }

    private void OnDestroy()
    {
        if (createdTransitionCamera && transitionCamera != null)
        {
            Destroy(transitionCamera.gameObject);
        }
    }

    private void Update()
    {
        if (allowKeyToggle && TabPressed())
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        SetMode(CurrentMode == ViewMode.Mode2D ? ViewMode.Mode3D : ViewMode.Mode2D);
    }

    /// <summary>
    /// モードを切り替える。遷移中に呼ばれた場合は、現在の見た目から新しい遷移先へ繋ぎ直す。
    /// </summary>
    public void SetMode(ViewMode mode)
    {
        if (mode == CurrentMode) return;

        if (!smoothTransition)
        {
            CancelTransition();
            CurrentMode = mode;
            ShowImmediate(mode);
            OnModeChanged?.Invoke(mode);
            OnTransitionCompleted?.Invoke(mode);
            return;
        }

        StartTransition(mode);
    }

    // ------------------------------------------------------------
    // 遷移処理
    // ------------------------------------------------------------

    private void StartTransition(ViewMode mode)
    {
        Camera target = GetCamera(mode);

        Camera liveSource = null;       // 通常は元カメラの動きに追従する
        ViewState frozenFrom = default; // 割り込み時は、その時点の状態を起点に固定する
        CameraClearFlags startClearFlags;

        if (IsTransitioning)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;

            frozenFrom = lastState;
            startClearFlags = transitionCamera.clearFlags;
        }
        else
        {
            liveSource = GetCamera(CurrentMode);
            transitionCamera.CopyFrom(liveSource);
            transitionCamera.ResetProjectionMatrix();
            startClearFlags = liveSource.clearFlags;
        }

        // 2D用/3D用でCulling Maskが違っても、遷移中は両方が見えるようにする
        transitionCamera.cullingMask = camera2D.cullingMask | camera3D.cullingMask;

        CurrentMode = mode;

        // AudioListenerは切れ目が出ないよう、遷移の開始時点で遷移先へ移す
        SetListener(camera2D, mode == ViewMode.Mode2D);
        SetListener(camera3D, mode == ViewMode.Mode3D);

        OnModeChanged?.Invoke(mode);

        transitionRoutine = StartCoroutine(
            TransitionRoutine(target, mode, liveSource, frozenFrom, startClearFlags));
    }

    private IEnumerator TransitionRoutine(
        Camera target, ViewMode mode,
        Camera liveSource, ViewState frozenFrom, CameraClearFlags startClearFlags)
    {
        IsTransitioning = true;

        camera2D.enabled = false;
        camera3D.enabled = false;
        transitionCamera.enabled = true;

        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = transitionCurve.Evaluate(Mathf.Clamp01(elapsed / transitionDuration));

            // 両端のカメラが動いていても追従できるよう、毎フレーム状態を取り直す
            ViewState from = liveSource != null ? CaptureState(liveSource) : frozenFrom;
            ViewState to = CaptureState(target);

            lastState = Evaluate(from, to, t);
            ApplyState(lastState, target.aspect);

            // スカイボックス/単色などの背景設定は補間できないので、中間で切り替える
            transitionCamera.clearFlags = t < 0.5f ? startClearFlags : target.clearFlags;

            yield return null;
        }

        // 遷移完了: 遷移用カメラを止めて、遷移先のカメラに引き継ぐ
        EndTransitionCamera();
        target.enabled = true;

        IsTransitioning = false;
        transitionRoutine = null;
        OnTransitionCompleted?.Invoke(mode);
    }

    private void CancelTransition()
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }
        IsTransitioning = false;
        EndTransitionCamera();
    }

    // ------------------------------------------------------------
    // 見え方(ViewState)の取得・補間・適用
    // ------------------------------------------------------------

    private ViewState CaptureState(Camera cam)
    {
        Transform tr = cam.transform;
        float d = GetFocusDistance(cam);
        bool perspective = !cam.orthographic;

        return new ViewState
        {
            focusPoint = tr.position + tr.forward * d,
            rotation = tr.rotation,
            distance = d,
            halfHeight = perspective
                ? d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                : cam.orthographicSize,
            strength = perspective ? 1f : 0f,
            near = cam.nearClipPlane,
            far = cam.farClipPlane,
            background = cam.backgroundColor
        };
    }

    private float GetFocusDistance(Camera cam)
    {
        if (focusTarget != null)
        {
            Transform tr = cam.transform;
            float d = Vector3.Dot(focusTarget.position - tr.position, tr.forward);
            if (d > cam.nearClipPlane) return d;
        }
        return Mathf.Max(focusDistance, cam.nearClipPlane + 0.01f);
    }

    private ViewState Evaluate(ViewState a, ViewState b, float t)
    {
        return new ViewState
        {
            // 焦点を中心に、位置は「焦点 - 向き × 距離」で決めるので背景が自然に回り込む
            focusPoint = Vector3.Lerp(a.focusPoint, b.focusPoint, t),
            rotation = Quaternion.Slerp(a.rotation, b.rotation, t),
            distance = Mathf.Lerp(a.distance, b.distance, t),

            // 拡大縮小は比率で補間すると自然に見える
            halfHeight = Mathf.Exp(Mathf.Lerp(Mathf.Log(a.halfHeight), Mathf.Log(b.halfHeight), t)),

            // 遠近感は「奥の物体の見かけの大きさ」が均等に変わるよう、変換した空間で補間する
            strength = GToStrength(Mathf.Lerp(StrengthToG(a.strength), StrengthToG(b.strength), t)),

            near = Mathf.Lerp(a.near, b.near, t),
            far = Mathf.Lerp(a.far, b.far, t),
            background = Color.Lerp(a.background, b.background, t)
        };
    }

    private void ApplyState(ViewState s, float aspect)
    {
        Transform tr = transitionCamera.transform;
        Vector3 position = s.focusPoint - s.rotation * Vector3.forward * s.distance;
        tr.SetPositionAndRotation(position, s.rotation);

        transitionCamera.projectionMatrix = BuildProjection(s, aspect);
        transitionCamera.backgroundColor = s.background;
    }

    /// <summary>
    /// 焦点面の大きさを固定したまま、遠近感の強さだけを変える投影行列を作る。
    /// 強さ 1 で通常の透視投影、0 に近づくほど正投影に近づく
    /// (仮想的にカメラを後ろへ引きながら画角を絞るドリーズームと等価)。
    /// </summary>
    private static Matrix4x4 BuildProjection(ViewState s, float aspect)
    {
        float strength = Mathf.Max(s.strength, MinStrength);
        float u = s.distance * (1f - strength) / strength; // 仮想的に引く距離

        float n = s.near;
        float f = s.far;
        float scale = (s.distance + u) / s.halfHeight;

        var m = Matrix4x4.zero;
        m[0, 0] = scale / aspect;
        m[1, 1] = scale;
        m[2, 2] = -(2f * u + f + n) / (f - n);
        m[2, 3] = -(u * (n + f) + 2f * n * f) / (f - n);
        m[3, 2] = -1f;
        m[3, 3] = u;
        return m;
    }

    // 遠近感の強さ s(0..1) と、均等補間用の値 g(0..1) の相互変換
    private float StrengthToG(float s)
    {
        return Mathf.Log(1f + s * (depthRatio - 1f)) / Mathf.Log(depthRatio);
    }

    private float GToStrength(float g)
    {
        return (Mathf.Pow(depthRatio, g) - 1f) / (depthRatio - 1f);
    }

    // ------------------------------------------------------------
    // ヘルパー
    // ------------------------------------------------------------

    private void ShowImmediate(ViewMode mode)
    {
        bool is2D = mode == ViewMode.Mode2D;

        camera2D.enabled = is2D;
        camera3D.enabled = !is2D;

        // AudioListenerが2つ有効にならないように切り替える
        SetListener(camera2D, is2D);
        SetListener(camera3D, !is2D);
    }

    private void SetupTransitionCamera()
    {
        if (transitionCamera == null)
        {
            var go = new GameObject("TransitionCamera");
            transitionCamera = go.AddComponent<Camera>();
            createdTransitionCamera = true;
        }
        EndTransitionCamera();
    }

    private void EndTransitionCamera()
    {
        if (transitionCamera == null) return;
        transitionCamera.enabled = false;
        transitionCamera.ResetProjectionMatrix();
    }

    private Camera GetCamera(ViewMode mode)
    {
        return mode == ViewMode.Mode2D ? camera2D : camera3D;
    }

    private static void SetListener(Camera cam, bool enabled)
    {
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = enabled;
    }

    private static bool TabPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }
}