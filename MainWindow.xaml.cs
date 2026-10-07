using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CV = OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace CabinetFaceWpf;

public partial class MainWindow : Window
{
    private enum AppState { Idle, Running, Snapped }

    private AppState _state = AppState.Idle;
    private CV.VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private CV.Mat? _snapFrame;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string UploadUrl = "http://localhost:8080/api/updateFaceFeature";

    public MainWindow()
    {
        InitializeComponent();
        UpdateUi();
    }

    private void UpdateUi()
    {
        var gray = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var green = new SolidColorBrush(Colors.LimeGreen);
        var orange = new SolidColorBrush(Colors.Orange);

        switch (_state)
        {
            case AppState.Idle:
                ledStatus.Fill = gray;
                txtStatus.Text = "就绪";
                txtPlaceholder.Visibility = Visibility.Visible;
                snapBadge.Visibility = Visibility.Collapsed;
                btnStart.Visibility = Visibility.Visible;
                btnStop.Visibility = Visibility.Collapsed;
                btnSnap.Visibility = Visibility.Collapsed;
                btnSave.Visibility = Visibility.Collapsed;
                btnUpload.Visibility = Visibility.Collapsed;
                btnDiscard.Visibility = Visibility.Collapsed;
                break;

            case AppState.Running:
                ledStatus.Fill = green;
                txtStatus.Text = "摄像头已开启";
                txtPlaceholder.Visibility = Visibility.Collapsed;
                snapBadge.Visibility = Visibility.Collapsed;
                btnStart.Visibility = Visibility.Collapsed;
                btnStop.Visibility = Visibility.Visible;
                btnSnap.Visibility = Visibility.Visible;
                btnSave.Visibility = Visibility.Collapsed;
                btnUpload.Visibility = Visibility.Collapsed;
                btnDiscard.Visibility = Visibility.Collapsed;
                break;

            case AppState.Snapped:
                ledStatus.Fill = orange;
                txtStatus.Text = "已抓拍 - 请选择操作";
                txtPlaceholder.Visibility = Visibility.Collapsed;
                snapBadge.Visibility = Visibility.Visible;
                btnStart.Visibility = Visibility.Collapsed;
                btnStop.Visibility = Visibility.Collapsed;
                btnSnap.Visibility = Visibility.Collapsed;
                btnSave.Visibility = Visibility.Visible;
                btnUpload.Visibility = Visibility.Visible;
                btnDiscard.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_state != AppState.Idle) return;

        try
        {
            _capture = new CV.VideoCapture(0);
            if (!_capture.IsOpened())
            {
                MessageBox.Show("打开摄像头失败！请检查设备连接和权限。", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _capture.Dispose();
                _capture = null;
                return;
            }

            _state = AppState.Running;
            UpdateUi();
            _cts = new CancellationTokenSource();
            await Task.Run(() => ReadFrameLoop(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            _state = AppState.Idle;
            UpdateUi();
            MessageBox.Show($"启动摄像头时出错：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReadFrameLoop(CancellationToken token)
    {
        using var frame = new CV.Mat();
        while (!token.IsCancellationRequested && _capture != null && _capture.IsOpened())
        {
            if (!_capture.Read(frame)) continue;

            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    imgCamera.Source = frame.ToWriteableBitmap();
                });
            }
            catch (TaskCanceledException) { break; }
            catch (OperationCanceledException) { break; }
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        if (_state != AppState.Running) return;
        StopCapture();
        imgCamera.Source = null;
        _state = AppState.Idle;
        UpdateUi();
    }

    private void BtnSnap_Click(object sender, RoutedEventArgs e)
    {
        if (_state != AppState.Running || _capture == null || !_capture.IsOpened()) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        using var frame = new CV.Mat();
        if (!_capture.Read(frame) || frame.Empty())
        {
            MessageBox.Show("抓拍失败，未获取到画面。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _snapFrame = frame.Clone();
        imgCamera.Source = _snapFrame.ToWriteableBitmap();

        _state = AppState.Snapped;
        UpdateUi();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_state != AppState.Snapped || _snapFrame == null || _snapFrame.Empty()) return;

        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JPEG 图片|*.jpg|PNG 图片|*.png|BMP 图片|*.bmp",
                FileName = $"snap_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".jpg"
            };

            if (dlg.ShowDialog() == true)
            {
                CV.Cv2.ImWrite(dlg.FileName, _snapFrame);
                MessageBox.Show($"图片已保存：\n{dlg.FileName}", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存图片失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        if (_state != AppState.Snapped || _snapFrame == null || _snapFrame.Empty()) return;

        btnUpload.IsEnabled = false;
        txtStatus.Text = "上传中...";

        try
        {
            byte[] imageBytes = _snapFrame.ToBytes(".jpg");

            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", $"snap_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

            HttpResponseMessage response = await _http.PostAsync(UploadUrl, content);
            string body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show($"上传成功！\n\n服务端返回：\n{body}", "上传成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"上传失败！HTTP {(int)response.StatusCode}\n\n服务端返回：\n{body}", "上传失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show("上传超时，请检查服务是否运行。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show($"网络错误：{ex.Message}\n\n请确认 {UploadUrl} 可访问。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"上传出错：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnUpload.IsEnabled = true;
            UpdateUi();
        }
    }

    private void BtnDiscard_Click(object sender, RoutedEventArgs e)
    {
        if (_state != AppState.Snapped) return;

        _snapFrame?.Dispose();
        _snapFrame = null;

        if (_capture != null && _capture.IsOpened())
        {
            _state = AppState.Running;
            UpdateUi();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadFrameLoop(_cts.Token), _cts.Token);
        }
        else
        {
            StopCapture();
            imgCamera.Source = null;
            _state = AppState.Idle;
            UpdateUi();
        }
    }

    private void StopCapture()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_capture != null)
        {
            _capture.Release();
            _capture.Dispose();
            _capture = null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _snapFrame?.Dispose();
        _snapFrame = null;
        StopCapture();
        base.OnClosed(e);
    }
}