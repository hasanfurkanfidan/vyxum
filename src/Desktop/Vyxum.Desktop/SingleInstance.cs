using System.Runtime.InteropServices;

namespace Vyxum.Desktop;

/// <summary>Allows one window per Windows session; a second launch asks the first to come to the front.</summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Vyxum.Desktop";
    private const string ActivateEventName = @"Local\Vyxum.Desktop.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    /// <summary>Returns the lock for the first instance. Returns null (after signalling the running window) for a later one.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(false, MutexName);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (owned)
        {
            return new SingleInstance(mutex, activate);
        }

        // This process was just started by the user, so it may hand the foreground right to the running window.
        AllowSetForegroundWindow(-1);
        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onActivate"/> (on a pool thread) each time another launch asks for the window.</summary>
    public void ListenForActivation(Action onActivate) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate.Dispose();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
