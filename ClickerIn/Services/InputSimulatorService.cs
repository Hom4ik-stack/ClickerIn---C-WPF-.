using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ClickerIn.Helpers;
using ClickerIn.Models;

namespace ClickerIn.Services
{
    public interface IInputSimulatorService
    {
        void Click(int x, int y, ClickType type = ClickType.Left);
        void MoveMouse(int x, int y);
        void MoveMouseSmooth(int x, int y, int durationMs = 300);
        void MouseDown(int x, int y, ClickType button = ClickType.Left);
        void MouseUp(int x, int y, ClickType button = ClickType.Left);
        void Scroll(int x, int y, int amount, bool horizontal = false);
        void DragDrop(int x1, int y1, int x2, int y2, ClickType button = ClickType.Left, MouseMovementData trajectory = null);
        void PressKey(Key key);
        void KeyCombo(ModifierKeys mod, Key key);
        void TypeText(string text);
        void HoldKey(Key key, int durationMs);
        Task PlayMouseMovement(MouseMovementData data, CancellationToken ct);
    }

    public sealed class InputSimulatorService : IInputSimulatorService
    {
        private static readonly Random _rng = new Random();

        public void Click(int x, int y, ClickType type = ClickType.Left)
        {
            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(RandomDelay(10, 30));
            switch (type)
            {
                case ClickType.Left:
                    Fire(NativeMethods.MOUSEEVENTF_LEFTDOWN);
                    Thread.Sleep(RandomDelay(20, 60));
                    Fire(NativeMethods.MOUSEEVENTF_LEFTUP);
                    break;
                case ClickType.Right:
                    Fire(NativeMethods.MOUSEEVENTF_RIGHTDOWN);
                    Thread.Sleep(RandomDelay(20, 60));
                    Fire(NativeMethods.MOUSEEVENTF_RIGHTUP);
                    break;
                case ClickType.Middle:
                    Fire(NativeMethods.MOUSEEVENTF_MIDDLEDOWN);
                    Thread.Sleep(RandomDelay(20, 60));
                    Fire(NativeMethods.MOUSEEVENTF_MIDDLEUP);
                    break;
                case ClickType.DoubleLeft:
                    Fire(NativeMethods.MOUSEEVENTF_LEFTDOWN);
                    Thread.Sleep(RandomDelay(15, 35));
                    Fire(NativeMethods.MOUSEEVENTF_LEFTUP);
                    Thread.Sleep(RandomDelay(40, 80));
                    Fire(NativeMethods.MOUSEEVENTF_LEFTDOWN);
                    Thread.Sleep(RandomDelay(15, 35));
                    Fire(NativeMethods.MOUSEEVENTF_LEFTUP);
                    break;
                case ClickType.DoubleRight:
                    Fire(NativeMethods.MOUSEEVENTF_RIGHTDOWN);
                    Thread.Sleep(RandomDelay(15, 35));
                    Fire(NativeMethods.MOUSEEVENTF_RIGHTUP);
                    Thread.Sleep(RandomDelay(40, 80));
                    Fire(NativeMethods.MOUSEEVENTF_RIGHTDOWN);
                    Thread.Sleep(RandomDelay(15, 35));
                    Fire(NativeMethods.MOUSEEVENTF_RIGHTUP);
                    break;
            }
        }

        public void MoveMouse(int x, int y)
        {
            NativeMethods.SetCursorPos(x, y);
        }

        public void MoveMouseSmooth(int x, int y, int durationMs = 300)
        {
            NativeMethods.GetCursorPos(out var start);
            int steps = Math.Max(10, durationMs / 10);
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                t = t * t * (3 - 2 * t);
                int cx = start.X + (int)((x - start.X) * t);
                int cy = start.Y + (int)((y - start.Y) * t);
                NativeMethods.SetCursorPos(cx, cy);
                Thread.Sleep(Math.Max(1, durationMs / steps));
            }
        }

        public void MouseDown(int x, int y, ClickType button = ClickType.Left)
        {
            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(RandomDelay(5, 15));
            switch (button)
            {
                case ClickType.Left: Fire(NativeMethods.MOUSEEVENTF_LEFTDOWN); break;
                case ClickType.Right: Fire(NativeMethods.MOUSEEVENTF_RIGHTDOWN); break;
                case ClickType.Middle: Fire(NativeMethods.MOUSEEVENTF_MIDDLEDOWN); break;
            }
        }

        public void MouseUp(int x, int y, ClickType button = ClickType.Left)
        {
            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(RandomDelay(5, 15));
            switch (button)
            {
                case ClickType.Left: Fire(NativeMethods.MOUSEEVENTF_LEFTUP); break;
                case ClickType.Right: Fire(NativeMethods.MOUSEEVENTF_RIGHTUP); break;
                case ClickType.Middle: Fire(NativeMethods.MOUSEEVENTF_MIDDLEUP); break;
            }
        }

        public void Scroll(int x, int y, int amount, bool horizontal = false)
        {
            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(RandomDelay(10, 30));
            if (horizontal)
            {
                var input = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_MOUSE,
                    u = new NativeMethods.INPUT_UNION
                    {
                        mi = new NativeMethods.MOUSEINPUT
                        {
                            dwFlags = 0x01000,
                            mouseData = (uint)amount
                        }
                    }
                };
                NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
            }
            else
            {
                int remaining = amount;
                int chunkSize = 120;
                while (remaining != 0)
                {
                    int chunk = remaining > 0
                        ? Math.Min(remaining, chunkSize)
                        : Math.Max(remaining, -chunkSize);
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, (uint)chunk, UIntPtr.Zero);
                    remaining -= chunk;
                    if (remaining != 0) Thread.Sleep(RandomDelay(15, 40));
                }
            }
        }

        public void DragDrop(int x1, int y1, int x2, int y2, ClickType button = ClickType.Left,
            MouseMovementData trajectory = null)
        {
            MouseDown(x1, y1, button);
            Thread.Sleep(RandomDelay(30, 80));
            if (trajectory != null && trajectory.Points.Count > 0)
            {
                foreach (var pt in trajectory.Points)
                {
                    NativeMethods.SetCursorPos(pt.X, pt.Y);
                    if (pt.DelayMs > 0) Thread.Sleep(pt.DelayMs);
                }
            }
            else
            {
                MoveMouseSmooth(x2, y2, RandomDelay(200, 500));
            }
            Thread.Sleep(RandomDelay(30, 80));
            MouseUp(x2, y2, button);
        }

        public void PressKey(Key key)
        {
            byte vk = (byte)KeyInterop.VirtualKeyFromKey(key);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(RandomDelay(30, 80));
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public void KeyCombo(ModifierKeys mod, Key key)
        {
            var modKeys = new List<byte>();
            if (mod.HasFlag(ModifierKeys.Control)) modKeys.Add(0xA2);
            if (mod.HasFlag(ModifierKeys.Shift)) modKeys.Add(0xA0);
            if (mod.HasFlag(ModifierKeys.Alt)) modKeys.Add(0xA4);
            if (mod.HasFlag(ModifierKeys.Windows)) modKeys.Add(0x5B);

            foreach (var m in modKeys)
                NativeMethods.keybd_event(m, 0, NativeMethods.KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(RandomDelay(10, 30));

            byte vk = (byte)KeyInterop.VirtualKeyFromKey(key);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(RandomDelay(30, 80));
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(RandomDelay(10, 30));

            for (int i = modKeys.Count - 1; i >= 0; i--)
                NativeMethods.keybd_event(modKeys[i], 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (char c in text)
            {
                var inputs = new NativeMethods.INPUT[2];
                inputs[0].type = NativeMethods.INPUT_KEYBOARD;
                inputs[0].u.ki = new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = 0x0004 };
                inputs[1].type = NativeMethods.INPUT_KEYBOARD;
                inputs[1].u.ki = new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = 0x0004 | NativeMethods.KEYEVENTF_KEYUP };
                NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
                Thread.Sleep(RandomDelay(20, 70));
            }
        }

        public void HoldKey(Key key, int durationMs)
        {
            byte vk = (byte)KeyInterop.VirtualKeyFromKey(key);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(durationMs);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public async Task PlayMouseMovement(MouseMovementData data, CancellationToken ct)
        {
            if (data?.Points == null || data.Points.Count == 0) return;
            foreach (var pt in data.Points)
            {
                ct.ThrowIfCancellationRequested();
                NativeMethods.SetCursorPos(pt.X, pt.Y);
                if (pt.DelayMs > 0)
                    await Task.Delay(pt.DelayMs, ct);
            }
        }

        private static void Fire(uint flags)
        {
            NativeMethods.mouse_event(flags, 0, 0, 0, UIntPtr.Zero);
        }

        private static int RandomDelay(int min, int max)
        {
            return _rng.Next(min, max + 1);
        }
    }
}