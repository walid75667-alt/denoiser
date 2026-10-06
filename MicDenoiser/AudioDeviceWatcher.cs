using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicDenoiser;

internal sealed class AudioDeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Action<string, bool> _changed;
    private int _disposed;
    public AudioDeviceWatcher(Action<string, bool> changed)
    {
        _changed = changed;
        try { _devices.RegisterEndpointNotificationCallback(this); }
        catch { _devices.Dispose(); throw; }
    }
    private void Notify(string id, bool lost)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        // A COM notification must never throw back into the audio service.
        try { _changed(id, lost); } catch { }
    }
    public void OnDeviceStateChanged(string id, DeviceState state) => Notify(id, (state & DeviceState.Active) == 0);
    public void OnDeviceRemoved(string id) => Notify(id, true);
    public void OnDeviceAdded(string id) => Notify(id, false);
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) { }
    public void OnPropertyValueChanged(string id, PropertyKey key) { }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _devices.UnregisterEndpointNotificationCallback(this); }
        finally { _devices.Dispose(); }
    }
}
