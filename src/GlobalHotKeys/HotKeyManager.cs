﻿using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace GlobalHotKeys;

public class HotKeyManager : IDisposable
{
    private const uint HotKeyMsg = 0x312u;
    private const uint RegisterHotKeyMsg = 0x0400u;     // WM_USER
    private const uint UnregisterHotKeyMsg = 0x0401u;   // WM_USER + 1

    private readonly Subject<HotKey> _hotkey = new();   // _hotkey subject to fire _hotkey events.
    private readonly Thread _messageLoopThread;         // Store the message loop thread and window handle.
    private readonly IntPtr _hWnd;

    /// <summary>
    /// Constructor initializes the message loop thread and window.
    /// </summary>
    public HotKeyManager()
    {
        // messageLoopThreadHwnd:
        // Create a TaskCompletionSource to receive the window handle.
        var tcsHwnd = new TaskCompletionSource<IntPtr>();

        // Thread entry method.
        void ThreadEntry()
        {
            // Retrieve the module handle.
            var hInstance = NativeFunctions.GetModuleHandle(null);

            // Dictionary to keep track of registrations.
            var registrations = new Dictionary<int, HotKey>();

            // nextId: find the next free id from 0x0000 to 0xBFFF.
            int? NextId()
            {
                for (int i = 0x0000; i <= 0xBFFF; i++)
                {
                    if (!registrations.ContainsKey(i))
                    {
                        return i;
                    }
                }
                return null;
            }

            // register: wrapper for calling RegisterHotKey and updating registrations.
            bool register(IntPtr hWnd, VirtualKeyCode key, Modifiers modifiers, int id)
            {
                if (NativeFunctions.RegisterHotKey(hWnd, id, modifiers, key))
                {
                    registrations.Add(id, new HotKey { Id = id, Key = key, Modifiers = modifiers });
                    return true;
                }
                else
                {
                    return false;
                }
            }

            // unregister: wrapper for calling UnregisterHotKey and updating registrations.
            bool unregister(IntPtr hWnd, int id)
            {
                var registration = registrations.GetValueOrDefault(id);
                if (registration != null)
                {
                    if (NativeFunctions.UnregisterHotKey(hWnd, registration.Id))
                    {
                        registrations.Remove(id);
                        return true;
                    }
                }
                return false;
            }

            // messageHandler: processes window messages.
            IntPtr MessageHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
            {
                if (uMsg == RegisterHotKeyMsg)
                {
                    // Extract key and modifiers.
                    var key = (VirtualKeyCode)wParam.ToInt32();
                    var modifiers = (Modifiers)lParam.ToInt32();
                    var id = NextId();
                    if (id.HasValue)
                    {
                        return register(hWnd, key, modifiers, id.Value) ? new IntPtr(id.Value) : new IntPtr(-1);
                    }
                    else
                    {
                        return IntPtr.Zero;
                    }
                }
                else if (uMsg == UnregisterHotKeyMsg)
                {
                    var id = wParam.ToInt32();
                    return unregister(hWnd, id) ? new IntPtr(id) : new IntPtr(-1);
                }
                else if (uMsg == HotKeyMsg)
                {
                    var registration = registrations.GetValueOrDefault(wParam.ToInt32());
                    if (registration != null)
                    {
                        _hotkey.OnNext(registration);
                    }
                    return new IntPtr(1);
                }
                else
                {
                    return NativeFunctions.DefWindowProc(hWnd, uMsg, wParam, lParam);
                }
            }

            // Create the window class from the window procedure.
            var wndProcDelegate = new WndProc(MessageHandler);
            // Convert the WndProc delegate into a WNDCLASSEX structure.
            var wndClassEx = WNDCLASSEX.FromWndProc(wndProcDelegate);

            // Register the window class.
            var registeredClass = NativeFunctions.RegisterClassEx(ref wndClassEx);

            // messageLoop: processes messages until quit.
            void MessageLoop(IntPtr hWnd)
            {
                var msg = new TagMSG();
                var ret = 0;
                while (((ret = NativeFunctions.GetMessage(ref msg, hWnd, 0u, 0u)) != -1) && (ret != 0))
                {
                    NativeFunctions.TranslateMessage(ref msg);
                    NativeFunctions.DispatchMessage(ref msg);
                }
            }

            // cleanup: unregister any registrations and destroy the window.
            void Cleanup(IntPtr hWnd)
            {
                foreach (var key in registrations.Keys.ToArray())
                {
                    unregister(hWnd, key);
                }

                NativeFunctions.DestroyWindow(hWnd);
                NativeFunctions.UnregisterClass(wndClassEx.lpszClassName, hInstance);
            }

            // create the window.
            var localHWnd = NativeFunctions.CreateWindowEx(0, (uint)registeredClass, null, WindowStyle.WS_OVERLAPPED, 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            // Signal that the window has been created.
            tcsHwnd.SetResult(localHWnd);

            // enter message loop.
            MessageLoop(localHWnd);

            // cleanup the resources afterwards.
            Cleanup(localHWnd);
        }

        _messageLoopThread = new Thread(new ThreadStart(ThreadEntry))
        {
            Name = "GlobalHotKeyManager Message Loop"
        };
        _messageLoopThread.Start();
        _hWnd = tcsHwnd.Task.Result;
    }

    /// <summary>
    /// Register method: registers a _hotkey.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="modifiers"></param>
    /// <returns></returns>
    public IRegistration Register(VirtualKeyCode key, Modifiers modifiers)
    {
        // Retrieve the window handle.
        var hWnd = _hWnd;

        // tell the message loop to register the _hotkey.
        var result = NativeFunctions.SendMessage(hWnd, RegisterHotKeyMsg, new IntPtr((int)key), new IntPtr((int)modifiers));

        // return a disposable that instructs the message loop to unregister the _hotkey on disposal.
        return new Registration(hWnd, result);
    }

    /// <summary>
    /// HotKeyPressed property: returns an observable sequence of hotkeys.
    /// </summary>
    public IObservable<HotKey> HotKeyPressed => _hotkey.AsObservable();

    /// <summary>
    /// Dispose method: shuts down the message loop.
    /// </summary>
    public void Dispose()
    {
        // shutdown the message loop.
        NativeFunctions.PostMessage(_hWnd, (uint)WindowMessage.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        // wait for the shutdown.
        _messageLoopThread.Join();
    }

    /// <summary>
    /// Explicit interface implementation for IDisposable.
    /// </summary>
    void IDisposable.Dispose()
    {
        Dispose();
    }

    private class Registration : IRegistration
    {
        private readonly IntPtr _hWnd;

        public Registration(IntPtr hWnd, IntPtr result)
        {
            _hWnd = hWnd;
            Id = result.ToInt32();
        }

        public bool IsSuccessful => Id != -1;

        public int Id { get; }

        /// <summary>
        /// Dispose method unregisters the _hotkey if registration was successful.
        /// </summary>
        public void Dispose()
        {
            if (Id != -1)
            {
                NativeFunctions.SendMessage(_hWnd, UnregisterHotKeyMsg, new IntPtr(Id), IntPtr.Zero);
            }
        }
    }
}