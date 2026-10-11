using System;
using System.Collections.Generic;
using UnityEngine;

public class AudioPlayer : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string label;            // 識別用の名前(Inspector表示用)
        public AudioSource prefab;      // 生成元のPrefab
        public AudioClip defaultClip;   // 既定のクリップ(任意)
        public bool loop;               // 既定のループ設定
        [Range(0f, 1f)] public float volume = 1f;
    }

    [SerializeField] private List<Entry> entries = new();   // Prefabを複数指定
    [SerializeField] private Transform root;                // 生成先(未指定なら自分の子)

    private readonly List<AudioHandle> handles = new();

    /// <summary>entries[index] のPrefabと既定クリップで生成 + 再生。</summary>
    public AudioHandle Play(int index, bool autoDestroy = true)
    {
        if (!IsValid(index)) return null;
        var e = entries[index];
        return Play(index, e.defaultClip, e.loop, e.volume, autoDestroy);
    }

    /// <summary>entries[index] のPrefabを使い、クリップ等を指定して生成 + 再生。</summary>
    public AudioHandle Play(int index, AudioClip clip, bool loop = false, float volume = 1f, bool autoDestroy = true)
    {
        if (!IsValid(index) || clip == null) return null;

        var src = Instantiate(entries[index].prefab, root != null ? root : transform);
        src.playOnAwake = false;
        src.clip = clip;
        src.loop = loop;
        src.volume = volume;

        var handle = new AudioHandle(src, autoDestroy);
        handles.Add(handle);
        src.Play();
        return handle;
    }

    /// <summary>labelで検索して再生(インデックスを覚えたくない場合用)。</summary>
    public AudioHandle Play(string label, bool autoDestroy = true)
    {
        int index = entries.FindIndex(e => e.label == label);
        return index >= 0 ? Play(index, autoDestroy) : null;
    }

    public void StopAll()
    {
        foreach (var h in handles) h.Stop();
    }

    public void DestroyAll()
    {
        foreach (var h in handles) h.Destroy();
        handles.Clear();
    }

    private void Update()
    {
        for (int i = handles.Count - 1; i >= 0; i--)
        {
            var h = handles[i];
            if (!h.IsAlive) { handles.RemoveAt(i); continue; }
            if (h.IsFinished) { h.Destroy(); handles.RemoveAt(i); }
        }
    }

    private void OnDestroy() => DestroyAll();

    private bool IsValid(int index) =>
        index >= 0 && index < entries.Count && entries[index].prefab != null;
}

/// <summary>生成したAudioSource1つを操作するためのハンドル。</summary>
public sealed class AudioHandle
{
    private AudioSource source;
    private readonly bool autoDestroy;
    private bool stopped;
    private bool paused;

    internal AudioHandle(AudioSource source, bool autoDestroy)
    {
        this.source = source;
        this.autoDestroy = autoDestroy;
    }

    public bool IsAlive => source != null;
    public bool IsPlaying => IsAlive && source.isPlaying;

    public bool Loop
    {
        get => IsAlive && source.loop;
        set { if (IsAlive) source.loop = value; }
    }

    public float Volume
    {
        get => IsAlive ? source.volume : 0f;
        set { if (IsAlive) source.volume = value; }
    }

    /// <summary>停止後にもう一度頭から鳴らす。</summary>
    public void Play()
    {
        if (!IsAlive) return;
        stopped = false; paused = false;
        source.Play();
    }

    public void Pause()
    {
        if (!IsAlive) return;
        paused = true;
        source.Pause();
    }

    public void Resume()
    {
        if (!IsAlive) return;
        paused = false;
        source.UnPause();
    }

    /// <summary>停止のみ(インスタンスは残る)。</summary>
    public void Stop()
    {
        if (!IsAlive) return;
        stopped = true;
        source.Stop();
    }

    /// <summary>停止してGameObjectごと破棄。</summary>
    public void Destroy()
    {
        if (!IsAlive) return;
        source.Stop();
        UnityEngine.Object.Destroy(source.gameObject);
        source = null;
    }

    // 「自然に再生が終わった」ときだけtrue(手動Stop・Pause・ループ中は対象外)
    internal bool IsFinished =>
        IsAlive && autoDestroy && !stopped && !paused
        && !source.loop && !source.isPlaying && !AudioListener.pause;
}