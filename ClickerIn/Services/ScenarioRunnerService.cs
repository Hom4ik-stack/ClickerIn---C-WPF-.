using System;
using System.Threading;
using System.Threading.Tasks;
using ClickerIn.Helpers;
using ClickerIn.Models;

namespace ClickerIn.Services
{
    public sealed class RunProgress
    {
        public int Step { get; set; }
        public int Total { get; set; }
        public int Loop { get; set; }
        public double Percent { get; set; }
        public string Message { get; set; }
    }

    public interface IScenarioRunner
    {
        Task Run(Scenario scenario, CancellationToken ct, IProgress<RunProgress> progress = null);
        void Stop();
        bool IsRunning { get; }
        event Action<string> StatusChanged;
    }

    public sealed class ScenarioRunnerService : IScenarioRunner
    {
        private readonly IInputSimulatorService _sim;
        private readonly IWindowManager _win;
        private readonly IDpiService _dpi;
        private readonly IWindowProfileService _profileSvc;
        private static readonly Random _rng = new Random();
        private CancellationTokenSource _internalCts;

        public bool IsRunning { get; private set; }
        public event Action<string> StatusChanged;

        public ScenarioRunnerService(IInputSimulatorService sim, IWindowManager win,
            IDpiService dpi, IWindowProfileService profileSvc)
        {
            _sim = sim;
            _win = win;
            _dpi = dpi;
            _profileSvc = profileSvc;
        }

        public async Task Run(Scenario scenario, CancellationToken ct, IProgress<RunProgress> progress = null)
        {
            IsRunning = true;
            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _internalCts.Token;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool success = true;

            try
            {
                if (scenario.WindowProfile != null)
                    _profileSvc.RestoreProfile(scenario.WindowProfile);

                if (!string.IsNullOrEmpty(scenario.TargetProcessName))
                {
                    var hwnd = _win.FindByProcess(scenario.TargetProcessName);
                    if (hwnd != IntPtr.Zero)
                        _win.BringToFront(hwnd);
                    await Task.Delay(300, token);
                }

                int loops = scenario.IsLoop ? (scenario.LoopCount <= 0 ? int.MaxValue : Math.Max(1, scenario.LoopCount)) : 1;

                for (int loop = 1; loop <= loops; loop++)
                {
                    token.ThrowIfCancellationRequested();
                    string loopDisplay = loops == int.MaxValue ? "∞" : loops.ToString();
                    StatusChanged?.Invoke("Цикл " + loop + "/" + loopDisplay);

                    for (int i = 0; i < scenario.Steps.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var step = scenario.Steps[i];
                        if (!step.Enabled) continue;

                        int total = scenario.Steps.Count;
                        double pct;
                        if (loops == int.MaxValue)
                            pct = (double)(i + 1) / total * 100;
                        else
                            pct = ((double)(loop - 1) * total + i + 1) / (loops * total) * 100;

                        if (progress != null)
                        {
                            progress.Report(new RunProgress
                            {
                                Step = i + 1,
                                Total = total,
                                Loop = loop,
                                Percent = pct,
                                Message = step.DisplayAction
                            });
                        }

                        await ExecuteStep(step, scenario, token);
                    }

                    if (loop < loops)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (OperationCanceledException) { success = false; throw; }
            catch { success = false; throw; }
            finally
            {
                sw.Stop();
                IsRunning = false;
                scenario.Stats.RecordRun(success, sw.Elapsed);
            }
        }

        private async Task ExecuteStep(Step step, Scenario scenario, CancellationToken ct)
        {
            int delayBefore = Humanize(step.DelayBeforeMs, step.RandomizeDelayBefore,
                step.RandomDelayBeforeRange, scenario.HumanizeDelays, scenario.HumanizePercent);
            if (delayBefore > 0) await Task.Delay(delayBefore, ct);

            int x = step.X, y = step.Y;

            if (step.CoordType == CoordinateType.RelativeToWindow && !string.IsNullOrEmpty(step.RecordedProcessName))
            {
                var hwnd = _win.FindByProcess(step.RecordedProcessName);
                if (hwnd != IntPtr.Zero)
                {
                    if (_win.GetWindowRect(hwnd, out var rect))
                    {
                        double scale = _dpi.GetSystemDpiScale() / step.RecordedDpiScale;
                        x = rect.Left + (int)(step.RelativeX * scale);
                        y = rect.Top + (int)(step.RelativeY * scale);
                    }
                }
            }

            if (step.RandomizeCoords)
            {
                int offsetX = _rng.Next(-step.RandomCoordsRadius, step.RandomCoordsRadius + 1);
                int offsetY = _rng.Next(-step.RandomCoordsRadius, step.RandomCoordsRadius + 1);
                x = Math.Max(0, x + offsetX);
                y = Math.Max(0, y + offsetY);
            }

            for (int attempt = 0; attempt <= step.RetryCount; attempt++)
            {
                try
                {
                    await ExecuteAction(step, x, y, ct);
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch when (attempt < step.RetryCount)
                {
                    await Task.Delay(step.RetryDelayMs, ct);
                }
            }

            int delayAfter = Humanize(step.DelayAfterMs, step.RandomizeDelayAfter,
                step.RandomDelayAfterRange, scenario.HumanizeDelays, scenario.HumanizePercent);
            if (delayAfter > 0) await Task.Delay(delayAfter, ct);
        }

        private async Task ExecuteAction(Step step, int x, int y, CancellationToken ct)
        {
            switch (step.ActionType)
            {
                case StepActionType.Click:
                    if (step.MouseMovement != null && step.MouseMovement.PointCount > 0)
                        await _sim.PlayMouseMovement(step.MouseMovement, ct);
                    _sim.Click(x, y, step.ClickType);
                    break;

                case StepActionType.MoveMouse:
                    if (step.MouseMovement != null && step.MouseMovement.PointCount > 0)
                        await _sim.PlayMouseMovement(step.MouseMovement, ct);
                    else
                        _sim.MoveMouseSmooth(x, y);
                    break;

                case StepActionType.Scroll:
                    _sim.Scroll(x, y, step.ScrollAmount, step.ScrollHorizontal);
                    break;

                case StepActionType.DragDrop:
                    int dx = step.DragToX, dy = step.DragToY;
                    if (step.RandomizeCoords)
                    {
                        int offsetX = _rng.Next(-step.RandomCoordsRadius, step.RandomCoordsRadius + 1);
                        int offsetY = _rng.Next(-step.RandomCoordsRadius, step.RandomCoordsRadius + 1);
                        dx = Math.Max(0, dx + offsetX);
                        dy = Math.Max(0, dy + offsetY);
                    }
                    _sim.DragDrop(x, y, dx, dy, step.MouseButtonHeld, step.MouseMovement);
                    break;

                case StepActionType.MouseDown:
                    _sim.MouseDown(x, y, step.MouseButtonHeld);
                    break;

                case StepActionType.MouseUp:
                    _sim.MouseUp(x, y, step.MouseButtonHeld);
                    break;

                case StepActionType.KeyPress:
                    if (step.KeyAction != null)
                    {
                        if (step.KeyAction.HoldDurationMs > 0)
                            _sim.HoldKey(step.KeyAction.Key, step.KeyAction.HoldDurationMs);
                        else
                            _sim.PressKey(step.KeyAction.Key);
                    }
                    break;

                case StepActionType.KeyCombo:
                    if (step.KeyAction != null)
                        _sim.KeyCombo(step.KeyAction.Modifiers, step.KeyAction.Key);
                    break;

                case StepActionType.TextInput:
                    _sim.TypeText(step.InputText);
                    break;

                case StepActionType.Wait:
                    await Task.Delay(step.DelayBeforeMs, ct);
                    break;

                case StepActionType.ScreenCheck:
                    break;
            }
        }

        public void Stop()
        {
            _internalCts?.Cancel();
            IsRunning = false;
        }

        private int Humanize(int baseMs, bool randomize, int range, bool humanize, int humanPct)
        {
            int result = baseMs;
            if (randomize) result += _rng.Next(-range, range + 1);
            if (humanize && humanPct > 0)
                result += _rng.Next(-(baseMs * humanPct / 100), baseMs * humanPct / 100 + 1);
            return Math.Max(0, result);
        }
    }
}