using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LyricSpace.Studio.Playback;

/// <summary>Reads only the Windows audio session state. No audio samples are captured.</summary>
internal sealed class SpotifyAudioSession
{
    private HashSet<uint> _processes = [];
    private double _refreshAt;

    public LocalAudioState Read(double now)
    {
        if (now >= _refreshAt)
        {
            _processes.Clear();
            foreach (Process process in Process.GetProcessesByName("Spotify"))
            {
                using (process)
                {
                    _processes.Add((uint)process.Id);
                }
            }

            _refreshAt = now + 3;
        }

        bool inactive = false;
        try
        {
            using var devices = new MMDeviceEnumerator();
            foreach (MMDevice device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using var endpoint = device;
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var audio = sessions[i];
                    if (!_processes.Contains(audio.GetProcessID))
                    {
                        continue;
                    }

                    var state = audio.State;
                    if (state == AudioSessionState.AudioSessionStateActive)
                    {
                        return LocalAudioState.Active;
                    }

                    inactive |= state == AudioSessionState.AudioSessionStateInactive;
                }
            }
        }
        catch (COMException)
        {
            return LocalAudioState.Unknown;
        }

        return inactive ? LocalAudioState.Inactive : LocalAudioState.Unknown;
    }
}
