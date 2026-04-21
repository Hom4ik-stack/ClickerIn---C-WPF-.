using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using ClickerIn.Helpers;
using ClickerIn.Models;

namespace ClickerIn.Services
{
    public interface IRecorderService
    {
        void Start(RecordingOptions options);
        List<Step> Stop();
        bool IsRecording { get; }
        event Action<Step> StepRecorded;
    }

    public sealed class RecorderService : IRecorderService, IDisposable
    {
        private readonly MouseHookService _hook;
        private readonly IWindowManager _win;
        private readonly IDpiService _dpi;
        private readonly List<Step> _steps = new List<Step>();
        private readonly List<MousePoint> _movementBuffer = new List<MousePoint>();
        private readonly Stopwatch _sw = new Stopwatch();

        private RecordingOptions _options;
        private long _lastEventMs;
        private long _lastMoveMs;
        private bool _isButtonDown;
        private ClickType _heldButton;
        private Point _dragStartPos;
        private Point _lastMovePos;
        private int _stepId;

        public bool IsRecording { get; private set; }
        public event Action<Step> StepRecorded;

        public RecorderService(MouseHookService hook, IWindowManager win, IDpiService dpi)
        {
            _hook = hook;
            _win = win;
            _dpi = dpi;
        }

        public void Start(RecordingOptions options)
        {
            _options = options ?? new RecordingOptions();
            _steps.Clear();
            _movementBuffer.Clear();
            _stepId = 0;
            _lastEventMs = 0;
            _lastMoveMs = 0;
            _isButtonDown = false;
            IsRecording = true;
            _sw.Restart();

            _hook.MouseEvent += OnMouseEvent;
        }

        public List<Step> Stop()
        {
            IsRecording = false;
            _hook.MouseEvent -= OnMouseEvent;
            _sw.Stop();

            if (_isButtonDown)
            {
                FlushDrag(_lastMovePos);
                _isButtonDown = false;
            }

            var result = new List<Step>(_steps);
            _steps.Clear();
            _movementBuffer.Clear();
            return result;
        }

        private void OnMouseEvent(MouseEventArgs e)
        {
            if (!IsRecording) return;

            long nowMs = _sw.ElapsedMilliseconds;

            switch (e.Type)
            {
                case MouseEventType.Move:
                    HandleMove(e.Position, nowMs);
                    break;

                case MouseEventType.LeftDown:
                    HandleButtonDown(e.Position, ClickType.Left, nowMs);
                    break;
                case MouseEventType.LeftUp:
                    HandleButtonUp(e.Position, ClickType.Left, nowMs);
                    break;

                case MouseEventType.RightDown:
                    HandleButtonDown(e.Position, ClickType.Right, nowMs);
                    break;
                case MouseEventType.RightUp:
                    HandleButtonUp(e.Position, ClickType.Right, nowMs);
                    break;

                case MouseEventType.MiddleDown:
                    HandleButtonDown(e.Position, ClickType.Middle, nowMs);
                    break;
                case MouseEventType.MiddleUp:
                    HandleButtonUp(e.Position, ClickType.Middle, nowMs);
                    break;

                case MouseEventType.DoubleLeft:
                    FlushMovement(nowMs);
                    RecordClick(e.Position, ClickType.DoubleLeft, nowMs);
                    break;

                case MouseEventType.DoubleRight:
                    FlushMovement(nowMs);
                    RecordClick(e.Position, ClickType.DoubleRight, nowMs);
                    break;

                case MouseEventType.Scroll:
                    HandleScroll(e.Position, e.ScrollDelta, nowMs);
                    break;
            }
        }

        private void HandleMove(Point pt, long nowMs)
        {
            _lastMovePos = pt;

            if (!_options.RecordMouseMovement && !_isButtonDown)
                return;

            if (nowMs - _lastMoveMs < _options.MovementSampleIntervalMs)
                return;

            _lastMoveMs = nowMs;
            int delay = _movementBuffer.Count == 0
                ? (int)(nowMs - _lastEventMs)
                : (int)(_options.MovementSampleIntervalMs);

            _movementBuffer.Add(new MousePoint(pt.X, pt.Y, delay));
        }

        private void HandleButtonDown(Point pt, ClickType button, long nowMs)
        {
            FlushMovement(nowMs);
            _isButtonDown = true;
            _heldButton = button;
            _dragStartPos = pt;
        }

        private void HandleButtonUp(Point pt, ClickType button, long nowMs)
        {
            if (!_isButtonDown || _heldButton != button)
            {
                _isButtonDown = false;
                return;
            }

            _isButtonDown = false;
            double dist = Math.Sqrt(Math.Pow(pt.X - _dragStartPos.X, 2) + Math.Pow(pt.Y - _dragStartPos.Y, 2));

            if (dist > 10)
            {
                FlushDrag(pt);
            }
            else
            {
                _movementBuffer.Clear();
                RecordClick(_dragStartPos, button, nowMs);
            }
        }

        private void FlushDrag(Point endPos)
        {
            long nowMs = _sw.ElapsedMilliseconds;
            int delay = (int)(nowMs - _lastEventMs);
            _lastEventMs = nowMs;

            var step = CreateBaseStep(StepActionType.DragDrop, _dragStartPos, delay);
            step.DragToX = endPos.X;
            step.DragToY = endPos.Y;
            step.MouseButtonHeld = _heldButton;

            if (_movementBuffer.Count > 2)
            {
                var points = _options.SimplifyPath
                    ? SimplifyPath(_movementBuffer, _options.SimplifyTolerance)
                    : new List<MousePoint>(_movementBuffer);
                step.MouseMovement = new MouseMovementData { Points = points };
            }

            _movementBuffer.Clear();
            AddStep(step);
        }

        private void HandleScroll(Point pt, int delta, long nowMs)
        {
            FlushMovement(nowMs);
            int delay = (int)(nowMs - _lastEventMs);

            if (_steps.Count > 0 && delay < 200)
            {
                var last = _steps[_steps.Count - 1];
                if (last.ActionType == StepActionType.Scroll)
                {
                    last.ScrollAmount += delta;
                    _lastEventMs = nowMs;
                    return;
                }
            }

            _lastEventMs = nowMs;
            var step = CreateBaseStep(StepActionType.Scroll, pt, delay);
            step.ScrollAmount = delta;
            AddStep(step);
        }
        private void RecordClick(Point pt, ClickType click, long nowMs)
        {
            int delay = (int)(nowMs - _lastEventMs);
            _lastEventMs = nowMs;

            var step = CreateBaseStep(StepActionType.Click, pt, delay);
            step.ClickType = click;
            AddStep(step);
        }

        private void FlushMovement(long nowMs)
        {
            if (_movementBuffer.Count < 2 || _isButtonDown)
            {
                _movementBuffer.Clear();
                return;
            }

            int delay = _movementBuffer.Count > 0 ? _movementBuffer[0].DelayMs : (int)(nowMs - _lastEventMs);
            var lastPt = _movementBuffer[_movementBuffer.Count - 1];
            _lastEventMs = nowMs;

            var step = CreateBaseStep(StepActionType.MoveMouse, new Point(lastPt.X, lastPt.Y), delay);

            var points = _options.SimplifyPath
                ? SimplifyPath(_movementBuffer, _options.SimplifyTolerance)
                : new List<MousePoint>(_movementBuffer);
            step.MouseMovement = new MouseMovementData { Points = points };

            _movementBuffer.Clear();
            AddStep(step);
        }

        private Step CreateBaseStep(StepActionType type, Point pt, int delayMs)
        {
            var hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = pt.X, Y = pt.Y });
            var root = _win.GetRootWindow(hwnd);
            var proc = _win.GetProcessName(root);
            var title = _win.GetTitle(root);
            double dpiScale = _dpi.GetSystemDpiScale();

            int relX = pt.X, relY = pt.Y;
            if (root != IntPtr.Zero && NativeMethods.GetWindowRect(root, out var rect))
            {
                relX = pt.X - rect.Left;
                relY = pt.Y - rect.Top;
            }

            return new Step
            {
                Id = ++_stepId,
                ActionType = type,
                X = pt.X,
                Y = pt.Y,
                RelativeX = relX,
                RelativeY = relY,
                DelayBeforeMs = Math.Max(0, delayMs),
                DelayAfterMs = 50,
                RecordedProcessName = proc,
                RecordedWindowTitle = title,
                RecordedDpiScale = dpiScale,
                CoordType = !string.IsNullOrEmpty(proc) ? CoordinateType.RelativeToWindow : CoordinateType.Global,
                Target = !string.IsNullOrEmpty(proc) ? TargetType.WindowByProcess : TargetType.Desktop,
                TargetName = proc
            };
        }

        private void AddStep(Step step)
        {
            _steps.Add(step);
            StepRecorded?.Invoke(step);
        }

        private List<MousePoint> SimplifyPath(List<MousePoint> points, double tolerance)
        {
            if (points.Count < 3) return new List<MousePoint>(points);

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            DouglasPeucker(points, 0, points.Count - 1, tolerance, keep);

            var result = new List<MousePoint>();
            for (int i = 0; i < points.Count; i++)
                if (keep[i]) result.Add(points[i]);
            return result;
        }

        private void DouglasPeucker(List<MousePoint> pts, int start, int end, double tol, bool[] keep)
        {
            if (end - start < 2) return;

            double maxDist = 0;
            int maxIdx = start;
            double ax = pts[start].X, ay = pts[start].Y;
            double bx = pts[end].X, by = pts[end].Y;
            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            for (int i = start + 1; i < end; i++)
            {
                double d;
                if (lenSq < 1e-10)
                {
                    d = Math.Sqrt(Math.Pow(pts[i].X - ax, 2) + Math.Pow(pts[i].Y - ay, 2));
                }
                else
                {
                    double t = Math.Max(0, Math.Min(1, ((pts[i].X - ax) * dx + (pts[i].Y - ay) * dy) / lenSq));
                    double px = ax + t * dx, py = ay + t * dy;
                    d = Math.Sqrt(Math.Pow(pts[i].X - px, 2) + Math.Pow(pts[i].Y - py, 2));
                }
                if (d > maxDist) { maxDist = d; maxIdx = i; }
            }

            if (maxDist > tol)
            {
                keep[maxIdx] = true;
                DouglasPeucker(pts, start, maxIdx, tol, keep);
                DouglasPeucker(pts, maxIdx, end, tol, keep);
            }
        }

        public void Dispose()
        {
            if (IsRecording) Stop();
        }
    }
}