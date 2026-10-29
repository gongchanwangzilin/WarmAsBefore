using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;


public sealed class PermissionBroker
{
    public async Task<bool> RequestLocation()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return s == PermissionStatus.Granted;
    }

    public async Task<bool> RequestCamera()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.Camera>();
        return s == PermissionStatus.Granted;
    }

    public async Task<bool> RequestMic()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.Microphone>();
        return s == PermissionStatus.Granted;
    }
}
