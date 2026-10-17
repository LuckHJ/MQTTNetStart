using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CabinetFaceWpf;

public partial class MainWindow : Window
{
    private static readonly string[] IdleAnimationFiles =
    [
        "active.gif",
        "active-1.gif",
        "active-2.gif",
        "active-3.gif",
        "skill-1-loop.gif"
    ];

    private static readonly TimeSpan InactivityBeforeSleep = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BehaviorPollInterval = TimeSpan.FromMilliseconds(200);
    private const int MinimumMovementIntervalMilliseconds = 12_000;
    private const int MaximumMovementIntervalMilliseconds = 24_000;
    private const int MinimumMovementDistance = 120;
    private const int MaximumMovementDistance = 280;
    private const double MovementSpeed = 50;

    private readonly Random _random = new();
    private AnimatedGif? _currentAnimation;
    private DispatcherTimer? _animationTimer;
    private DispatcherTimer? _behaviorTimer;
    private DispatcherTimer? _movementTimer;
    private Action? _animationCompleted;
    private uint _lastInputTick;
    private int _animationFrameIndex;
    private bool _loopCurrentAnimation;
    private bool _isSleeping;
    private bool _isMoving;
    private bool _isDragging;
    private long _nextMovementAt;
    private long _movementStartedAt;
    private long _movementDurationMilliseconds;
    private double _movementStartLeft;
    private double _movementStartTop;
    private double _movementTargetLeft;
    private double _movementTargetTop;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;

        try
        {
            _lastInputTick = GetLastInputTick();
            _nextMovementAt = Environment.TickCount64 + GetNextMovementDelay();

            _animationTimer = new DispatcherTimer();
            _animationTimer.Tick += AnimationTimer_Tick;
            _behaviorTimer = new DispatcherTimer { Interval = BehaviorPollInterval };
            _behaviorTimer.Tick += BehaviorTimer_Tick;
            _movementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _movementTimer.Tick += MovementTimer_Tick;

            PlayAnimation("start.gif", loop: false, PlayIdleAnimation);
            _behaviorTimer.Start();
        }
        catch (Exception ex)
        {
            StopTimers();
            MessageBox.Show($"启动桌宠动作失败：{ex.Message}", "动画加载失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlayIdleAnimation()
    {
        if (_isSleeping || _isMoving || _isDragging) return;

        string animation = IdleAnimationFiles[_random.Next(IdleAnimationFiles.Length)];
        PlayAnimation(animation, loop: true);
    }

    private void PlayAnimation(string fileName, bool loop, Action? completed = null)
    {
        _animationTimer!.Stop();
        GifPlayer.Source = null;
        _currentAnimation?.Dispose();

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "PetActions", fileName);
        _currentAnimation = new AnimatedGif(path);
        _animationFrameIndex = 0;
        _loopCurrentAnimation = loop;
        _animationCompleted = completed;
        ShowAnimationFrame();
        _animationTimer.Start();
    }

    private void ShowAnimationFrame()
    {
        GifPlayer.Source = _currentAnimation!.Frames[_animationFrameIndex];
        _animationTimer!.Interval = _currentAnimation.GetFrameDuration(_animationFrameIndex);
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentAnimation == null) return;

        if (_animationFrameIndex + 1 < _currentAnimation.Frames.Count)
        {
            _animationFrameIndex++;
            ShowAnimationFrame();
            return;
        }

        if (_loopCurrentAnimation)
        {
            _animationFrameIndex = 0;
            ShowAnimationFrame();
            return;
        }

        _animationTimer!.Stop();
        Action? completed = _animationCompleted;
        _animationCompleted = null;
        completed?.Invoke();
    }

    private void BehaviorTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            uint lastInputTick = GetLastInputTick();
            long now = Environment.TickCount64;

            if (lastInputTick != _lastInputTick)
            {
                _lastInputTick = lastInputTick;
                _nextMovementAt = now + GetNextMovementDelay();
                if (_isSleeping)
                {
                    _isSleeping = false;
                    PlayIdleAnimation();
                }
            }

            uint idleMilliseconds = unchecked((uint)Environment.TickCount - lastInputTick);
            if (!_isDragging && idleMilliseconds >= InactivityBeforeSleep.TotalMilliseconds)
            {
                if (!_isSleeping) EnterSleep();
                return;
            }

            if (!_isSleeping && !_isMoving && !_isDragging && now >= _nextMovementAt)
            {
                StartAutomaticMovement(now);
            }
        }
        catch (Exception ex)
        {
            StopTimers();
            MessageBox.Show($"桌宠行为更新失败：{ex.Message}", "桌宠错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnterSleep()
    {
        _isSleeping = true;
        StopAutomaticMovement();
        PlayAnimation("sleep.gif", loop: true);
    }

    private void StartAutomaticMovement(long now)
    {
        _nextMovementAt = now + GetNextMovementDelay();
        _movementStartLeft = Left;
        _movementStartTop = Top;

        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        double maximumLeft = Math.Max(virtualLeft, virtualRight - Width);
        double maximumTop = Math.Max(virtualTop, virtualBottom - Height);

        double angle = _random.NextDouble() * Math.PI * 2;
        double distance = _random.Next(MinimumMovementDistance, MaximumMovementDistance + 1);
        _movementTargetLeft = Math.Clamp(
            _movementStartLeft + Math.Cos(angle) * distance, virtualLeft, maximumLeft);
        _movementTargetTop = Math.Clamp(
            _movementStartTop + Math.Sin(angle) * distance, virtualTop, maximumTop);

        double actualDistance = Math.Sqrt(
            Math.Pow(_movementTargetLeft - _movementStartLeft, 2) +
            Math.Pow(_movementTargetTop - _movementStartTop, 2));
        if (actualDistance < 1)
        {
            _nextMovementAt = now + MinimumMovementIntervalMilliseconds;
            return;
        }

        _movementDurationMilliseconds = Math.Max(1, (long)(actualDistance / MovementSpeed * 1000));
        _movementStartedAt = now;
        _isMoving = true;
        PlayAnimation("move.gif", loop: true);
        _movementTimer!.Start();
    }

    private void MovementTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isMoving) return;

        double progress = Math.Clamp(
            (double)(Environment.TickCount64 - _movementStartedAt) / _movementDurationMilliseconds, 0, 1);
        double easedProgress = progress * progress * (3 - 2 * progress);
        Left = _movementStartLeft + (_movementTargetLeft - _movementStartLeft) * easedProgress;
        Top = _movementStartTop + (_movementTargetTop - _movementStartTop) * easedProgress;

        if (progress >= 1)
        {
            StopAutomaticMovement();
            PlayIdleAnimation();
        }
    }

    private void StopAutomaticMovement()
    {
        _movementTimer?.Stop();
        _isMoving = false;
    }

    private int GetNextMovementDelay()
    {
        return _random.Next(MinimumMovementIntervalMilliseconds, MaximumMovementIntervalMilliseconds + 1);
    }

    private static uint GetLastInputTick()
    {
        var inputInfo = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref inputInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return inputInfo.Tick;
    }

    private void Pet_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            _lastInputTick = GetLastInputTick();
            _nextMovementAt = Environment.TickCount64 + GetNextMovementDelay();
            _isSleeping = false;

            if (!_isMoving && !_isDragging)
            {
                PlayAnimation("click.gif", loop: false, PlayIdleAnimation);
            }
        }
        catch (Exception ex)
        {
            StopTimers();
            MessageBox.Show($"无法处理桌宠鼠标操作：{ex.Message}", "桌宠错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        _isDragging = true;
        StopAutomaticMovement();
        _isSleeping = false;
        PlayAnimation("move.gif", loop: true);

        try
        {
            DragMove();
        }
        finally
        {
            _isDragging = false;
            PlayIdleAnimation();
        }
    }

    private void OpenCameraMenu_Click(object sender, RoutedEventArgs e)
    {
        var cameraWindow = new CameraWindow { Owner = this };
        cameraWindow.ShowDialog();
    }

    private void OpenSudokuMenu_Click(object sender, RoutedEventArgs e)
    {
        var sudokuWindow = new SudokuWindow { Owner = this };
        sudokuWindow.Show();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void StopTimers()
    {
        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer.Tick -= AnimationTimer_Tick;
        }

        if (_behaviorTimer != null)
        {
            _behaviorTimer.Stop();
            _behaviorTimer.Tick -= BehaviorTimer_Tick;
        }

        if (_movementTimer != null)
        {
            _movementTimer.Stop();
            _movementTimer.Tick -= MovementTimer_Tick;
        }

        StopAutomaticMovement();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopTimers();
        GifPlayer.Source = null;
        _currentAnimation?.Dispose();
        _currentAnimation = null;
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Tick;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo inputInfo);
}
