using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Settings;

namespace Moirai.Atropos.Sound.Internal
{
    internal sealed class AudioService : IAudioService, IDisposable
    {
        private readonly IAudioBackend _backend;
        private readonly IAudioClipLeaseSource _source;
        private readonly AudioOptions _options;
        private readonly IKeyValueStore _settings;
        private float _masterVolume;
        private bool _muted;
        private readonly Dictionary<string, IAudioClipLease> _clips =
            new Dictionary<string, IAudioClipLease>(StringComparer.Ordinal);
        private readonly Dictionary<int, IAudioClipLease> _voices = new Dictionary<int, IAudioClipLease>();
        private int _nextVoice = 1;
        private bool _closing;

        internal AudioService(IAudioBackend backend, IAudioClipLeaseSource source,
            AudioOptions options, IKeyValueStore settings)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _options.Validate();
            float storedVolume = _settings.GetFloat(AudioSettingKeys.MasterVolume,
                _options.MasterVolume);
            _masterVolume = storedVolume >= 0f && storedVolume <= 1f
                ? storedVolume : _options.MasterVolume;
            _muted = _settings.GetBool(AudioSettingKeys.MasterMuted, false);
            _options.MasterVolume = _masterVolume;
            _backend.SetVolume(0, EffectiveVolume);
        }

        public void SetMasterVolume(float volume)
        {
            if (volume < 0f || volume > 1f) throw new ArgumentOutOfRangeException(nameof(volume));
            _masterVolume = volume;
            _options.MasterVolume = volume;
            _settings.SetFloat(AudioSettingKeys.MasterVolume, volume);
            _backend.SetVolume(0, EffectiveVolume);
        }

        public void SetMasterMuted(bool muted)
        {
            if (_muted == muted) return;
            _muted = muted;
            _settings.SetBool(AudioSettingKeys.MasterMuted, muted);
            _backend.SetVolume(0, EffectiveVolume);
        }

        public bool TryPlay(string clipKey, float volume = 1f, bool loop = false)
        {
            EnsureOpen();
            ValidateClipKey(clipKey);
            ValidateVolume(volume);
            if (!_clips.TryGetValue(clipKey, out IAudioClipLease lease)) return false;
            StartVoice(lease, volume, loop);
            return true;
        }

        public async UniTask PlayAsync(string clipKey, float volume, bool loop,
            CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            ValidateClipKey(clipKey);
            ValidateVolume(volume);
            if (_clips.TryGetValue(clipKey, out IAudioClipLease cached))
            {
                StartVoice(cached, volume, loop);
                return;
            }
            IAudioClipLease lease = await _source.AcquireAsync(clipKey, cancellationToken);
            await UniTask.SwitchToMainThread();
            if (_closing)
            {
                lease.Dispose();
                throw new ObjectDisposedException(nameof(AudioService));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!_clips.TryAdd(clipKey, lease))
            {
                lease.Dispose();
                lease = _clips[clipKey];
            }
            StartVoice(lease, volume, loop);
        }

        public void Stop(int voice, float fadeSeconds)
        {
            if (fadeSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(fadeSeconds));
            if (!_voices.TryGetValue(voice, out IAudioClipLease lease)) return;
            _backend.Stop(voice, fadeSeconds);
            _voices.Remove(voice);
            if (!IsReferenced(lease)) RemoveClip(lease);
        }

        public void Dispose()
        {
            if (_closing) return;
            _closing = true;
            var failures = new List<Exception>();
            foreach (KeyValuePair<int, IAudioClipLease> voice in _voices)
            {
                try { _backend.Stop(voice.Key, _options.DefaultFadeSeconds); }
                catch (Exception error) { failures.Add(error); }
            }
            _voices.Clear();
            foreach (IAudioClipLease lease in _clips.Values)
            {
                try { lease.Dispose(); }
                catch (Exception error) { failures.Add(error); }
            }
            _clips.Clear();
            if (failures.Count != 0) throw new AggregateException(failures);
        }

        private void StartVoice(IAudioClipLease lease, float volume, bool loop)
        {
            ValidateVolume(volume);
            int voice = _nextVoice++;
            _voices.Add(voice, lease);
            _backend.Play(voice, lease.Clip, volume, loop);
        }

        private bool IsReferenced(IAudioClipLease lease)
        {
            foreach (IAudioClipLease value in _voices.Values)
                if (ReferenceEquals(value, lease)) return true;
            return false;
        }

        private void RemoveClip(IAudioClipLease lease)
        {
            string key = null;
            foreach (KeyValuePair<string, IAudioClipLease> pair in _clips)
            {
                if (!ReferenceEquals(pair.Value, lease)) continue;
                key = pair.Key;
                break;
            }
            if (key == null) return;
            _clips.Remove(key);
            lease.Dispose();
        }

        private static void ValidateClipKey(string clipKey)
        {
            if (clipKey == null) throw new ArgumentNullException(nameof(clipKey));
            if (clipKey.Length == 0)
                throw new ArgumentException("Clip key must not be empty.", nameof(clipKey));
        }

        private float EffectiveVolume => _muted ? 0f : _masterVolume;

        private void ValidateVolume(float volume)
        {
            if (volume < 0f || volume > 1f) throw new ArgumentOutOfRangeException(nameof(volume));
        }

        private void EnsureOpen()
        {
            if (_closing) throw new ObjectDisposedException(nameof(AudioService));
        }
    }
}
