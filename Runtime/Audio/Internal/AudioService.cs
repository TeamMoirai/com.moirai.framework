using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Sound.Internal
{
    internal sealed class AudioService : IAudioService, IDisposable
    {
        private readonly IAudioBackend _backend;
        private readonly IAudioClipLeaseSource _source;
        private readonly AudioOptions _options;
        private readonly Dictionary<string, IAudioClipLease> _clips =
            new Dictionary<string, IAudioClipLease>(StringComparer.Ordinal);
        private readonly Dictionary<int, IAudioClipLease> _voices = new Dictionary<int, IAudioClipLease>();
        private int _nextVoice = 1;
        private bool _closing;

        internal AudioService(IAudioBackend backend, IAudioClipLeaseSource source, AudioOptions options)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _backend.SetVolume(0, _options.MasterVolume);
        }

        public void SetMasterVolume(float volume)
        {
            if (volume < 0f || volume > 1f) throw new ArgumentOutOfRangeException(nameof(volume));
            _options.MasterVolume = volume;
            _backend.SetVolume(0, volume);
        }

        public bool TryPlay(string clipKey, float volume = 1f, bool loop = false)
        {
            EnsureOpen();
            if (!_clips.TryGetValue(clipKey, out IAudioClipLease lease)) return false;
            StartVoice(lease, volume, loop);
            return true;
        }

        public async UniTask PlayAsync(string clipKey, float volume, bool loop,
            CancellationToken cancellationToken = default)
        {
            EnsureOpen();
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
            _voices.Remove(voice);
            _backend.Stop(voice, fadeSeconds);
            if (!IsReferenced(lease)) RemoveClip(lease);
        }

        public void Dispose()
        {
            if (_closing) return;
            _closing = true;
            foreach (KeyValuePair<int, IAudioClipLease> voice in _voices)
                _backend.Stop(voice.Key, _options.DefaultFadeSeconds);
            _voices.Clear();
            foreach (IAudioClipLease lease in _clips.Values) lease.Dispose();
            _clips.Clear();
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
