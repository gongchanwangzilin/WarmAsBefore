using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;


public sealed class NotificationService
{
    public event Action<string, string>? Notify;
    public void Show(string title, string msg) => Notify?.Invoke(title, msg);
}
